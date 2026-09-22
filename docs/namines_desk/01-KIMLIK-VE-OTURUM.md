# 01 — Kimlik ve Oturum

> **Amaç:** kullanıcı Desk'e girsin, **kendi Namines hesabıyla** eşleşsin, kendi
> projelerini görsün. Anahtar kavramıyla hiç karşılaşmasın.

---

## 1. Bugünkü hâl ve neden yanlış

v0.1'de giriş ekranı ham bir Gateway API anahtarı istiyor.

Üç ayrı sorun:

1. **Anahtar nereden alınacak belli değil.** Kullanıcının önce ana uygulamada
   `POST /api/gateway/keys/{projectId}` çağırması, sonra tablo izinlerini tek tek
   açması, sonra da ham anahtarı kopyalaması gerekiyor. Hiçbiri arayüzde yok.
2. **Ham anahtar yalnızca bir kez görünüyor** (`KeyHash` saklanıyor, ham değer
   değil — doğru bir güvenlik kararı). Kullanıcı kaybederse geri alamıyor,
   yenisini üretmek zorunda.
3. **Anahtar insan için tasarlanmadı.** `GatewayApiKey`'in alanları
   (`AllowedOrigins`, `AllowedIps`, `RateLimitPerMinute`, `CanExecuteSql`)
   bir *uygulamayı* tarif ediyor, bir kişiyi değil.

---

## 2. Karar: Desk oturumla çalışır

Gateway **zaten** iki kimlik yolu destekliyor. `GatewayController.AuthorizeAsync`:

- `X-Namines-Key` başlığı varsa → API anahtarı yolu, **tablo izinleri uygulanır**
- Başlık yoksa → oturum (JWT) yolu, `[Authorize]` kimliği doğrular,
  **tablo izinleri uygulanmaz**

İkinci yolun izin uygulamaması bilinçli ve doğru: oturum sahibi kendi projesinin
bağlantı dizesini zaten girebilir, yani erişebileceği her tabloya zaten
erişebilir. İzin katmanı **dış uygulamaları** sınırlamak için var.

**Desk = oturum yolu.** Kullanıcı e-posta + parola ile girer, JWT alır.

API anahtarları kaldırılmıyor — sadece asıl sahibine bırakılıyor: dış
uygulamalar. Desk onları **yönetmek** için bir ekran sunabilir (v1 kapsamı
dışı, [`09`](09-YOL-HARITASI.md)).

---

## 3. Gereken backend değişikliği (küçük)

Oturum yolunda bir boşluk var: **hangi projenin bağlantısı kullanılacak?**

Bugün bu bilgi anahtardan geliyor (`apiKey.ProjectId`). Oturumda anahtar yok.

`GatewayController.ResolveConnectionAsync` şu an:

```
istekte bağlantı var mı? → onu kullan
yoksa → anahtardan projeyi bul → şifreli bağlantıyı çöz
```

Eklenecek üçüncü dal:

```
yoksa ve istek `projectId` taşıyorsa
  → JWT kullanıcısının O PROJEYE erişimi var mı, DOĞRULA
  → varsa şifreli bağlantıyı çöz
```

**Yetki doğrulaması atlanamaz.** `projectId`'yi doğrulamadan kabul etmek,
herhangi bir giriş yapmış kullanıcının başkasının proje kimliğini yazıp o
veritabanına bağlanabilmesi demek olurdu. Kontrol, `GatewayKeyController`'ın
zaten kullandığı `_context.CanManageMembersAsync(projectId, userId)` ile aynı
yerden yapılmalı — iki ayrı yetki mantığı zamanla ayrışır.

### Etkilenen uçlar

`list`, `detail`, `create`, `update`, `delete`, `schema` — hepsi
`ResolveConnectionAsync` üzerinden geçtiği için tek noktada çözülür.

`GET /api/gateway/schema` ayrıca `ResolveKeyAsync`'e bağlı (anahtar zorunlu);
oturum yolunu da kabul edecek şekilde genişletilmeli.

---

## 4. Giriş akışı

### v1: Desk'in kendi giriş formu

```
Desk /login  →  POST {API}/api/auth/login  →  JWT
             →  sessionStorage
             →  GET /api/auth/projects
```

**Neden `sessionStorage`, `localStorage` değil:** JWT bir erişim belgesi. Sekme
kapanınca kalmamalı; paylaşılan bir makinede kalıcı saklamak, kapatıldığı
sanılan bir oturumu açık bırakır. (v0.1'de anahtar için de aynı karar verildi.)

### v1.1: Ana uygulamadan devir (SSO)

Kullanıcı ana uygulamada zaten girişli; Desk'e geçerken tekrar parola sormak
gereksiz sürtünme. Çözüm: ana uygulamadaki "Namines Desk" bağlantısı tek
kullanımlık, kısa ömürlü bir devir jetonu taşır; Desk onu JWT'ye çevirir.

**v1'de yapılmıyor** çünkü yeni bir uç + jeton ömrü/iptal tasarımı gerektiriyor
ve giriş formu bunsuz da çalışıyor. Sürtünme, güvenlik açığından iyidir.

> ⚠️ Jetonu URL'de taşımak, tarayıcı geçmişine ve `Referer` başlığına
> düşmesi demektir. Tasarlandığında tek kullanımlık ve saniyeler ömürlü olmalı.

---

## 5. Kabul kriterleri

| # | Kriter | Nasıl kanıtlanır |
|---|---|---|
| 1 | Doğru parolayla giriş → projeler listelenir | Tarayıcıda, gerçek hesapla |
| 2 | Yanlış parola → 401, açıklayıcı mesaj | Tarayıcıda |
| 3 | Oturumsuz `/api/gateway/list` → 401 | `curl` |
| 4 | **Başkasının `projectId`'si ile istek → 403/404** | İki ayrı hesap, `curl` |
| 5 | Sekme kapanınca oturum düşer | Tarayıcıda |
| 6 | Kullanıcı hiçbir yerde anahtar girmez | Arayüz gözden geçirmesi |

Kriter 4 en kritiği: **yazılmadan önce bu testin yazılması gerekiyor.**
Yetki kontrolü unutulursa özellik yine "çalışıyor" görünür — hatayı yalnızca
bu test yakalar.
