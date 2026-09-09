# Üretimde çalıştırma

> Bu belge, denetimin iki bulgusunu kapatmak için yazıldı:
> **DEVOPS-001** (dağıtım tanımı depoda yok) ve **DR** (şifreleme anahtarı
> kurtarma planı yok). Bkz. [`docs/audit/`](../docs/audit/).

---

## 1. Zorunlu ortam değişkenleri

Uygulama, aşağıdakiler eksikse **açılmaz**. Bu bilinçli: sessizce güvensiz bir
varsayılana düşmek, hiç açılmamaktan kötüdür.

| Değişken | Neden zorunlu | Nasıl üretilir |
|---|---|---|
| `Jwt__Key` | Tanımsızsa depoda **açıkça yazılı** bir anahtar kullanılırdı; onunla herkes jeton üretebilir | `openssl rand -base64 48` |
| `Security__ConnectionEncryptionKey` | Kullanıcıların bağlantı dizeleri bununla şifreleniyor | `openssl rand -base64 32` |
| `Vault__BackupEncryptionKey` | Yedekler bununla şifreleniyor. Tanımsızsa Vault **şifresiz yedek yazmaz, durur** | `openssl rand -base64 32` |
| `POSTGRES_PASSWORD` | Control DB parolası | Uzun ve rastgele |
| `PUBLIC_API_URL` | Frontend derlemesine gömülüyor | ör. `https://api.ornek.com` |

**`Jwt__Key` ile `Security__ConnectionEncryptionKey` ile
`Vault__BackupEncryptionKey` ÜÇÜ DE FARKLI OLMALI.** Birinin sızması, ötekilerin
de sızması anlamına gelmemeli: bağlantı dizelerini çözebilen biri, o
bağlantıların bütün geçmiş yedeklerini de çözebilir olmamalı.

---

## 2. Dağıtım

```bash
# 1) Migration'ları uygula — uygulamadan AYRI adım
docker compose -f docker-compose.prod.yml --profile migrate run --rm namines-migrate

# 2) Yığını aç
docker compose -f docker-compose.prod.yml up -d

# 3) Doğrula
curl -fsS https://<api>/health/ready
```

**Migration neden ayrı adım:** Açılışta koşarsa (a) geri alınamaz bir şema
değişikliği deploy anında, onay adımı olmadan uygulanır; (b) iki instance aynı
anda açılırsa yarışırlar; (c) geri alma eski sürümü yeni şemaya karşı çalışmak
zorunda bırakır.

Uygulama, `Database__MigrateOnStartup=false` iken bekleyen migration varsa
**açılmayı reddeder.** Yani (1) adımını atlarsanız sessizce eski şemaya karşı
çalışmaz — açılmaz ve size söyler.

---

## 3. TLS ve reverse proxy

`docker-compose.prod.yml` **TLS sonlandırmaz.** Servisler yalnızca
`127.0.0.1`'e bağlanır; önlerinde bir reverse proxy (Caddy, nginx, Traefik) ya
da bir PaaS load balancer'ı olmalıdır.

### Proxy'nin sağlaması gerekenler

| Gereklilik | Neden |
|---|---|
| `X-Forwarded-Proto` **gönder** | Uygulama auth cookie'sinin `Secure` bayrağını buna göre koyuyor |
| Dışarıdan gelen `X-Forwarded-*` başlıklarını **EZ** | Aksi hâlde saldırgan `X-Forwarded-Proto: https` göndererek cookie davranışını yanıltır |
| `X-Forwarded-For` gönder | Rate limit, kimliksiz çağıranları IP'ye göre bölüyor |
| WebSocket upgrade'e izin ver | Canvas'ın çoklu kullanıcı özelliği `/hubs/canvas` üzerinden |

### Aynı site mi, cross-site mi

**Tercih edilen:** frontend ve API'yi aynı kayıtlı alan adı altında dağıtın
(`app.ornek.com` + `api.ornek.com`). O zaman `Auth__CrossSiteCookie` **boş**
kalır, cookie `SameSite=Lax` olur ve CSRF yüzeyi büyük ölçüde kapanır.

**Zorunluysa** (ör. Vercel + Railway gibi ayrı domain'ler):
`Auth__CrossSiteCookie=true` verin. Cookie `SameSite=None; Secure` olur.
Bu durumda CSRF koruması `CsrfProtectionMiddleware`'e devrolur — cookie ile
gelen yazma istekleri `X-Namines-Request` başlığı taşımak zorundadır. Kendi
istemcinizi yazıyorsanız bu başlığı gönderin; `Authorization: Bearer`
kullanıyorsanız gerekmez.

---

## 4. Şifreleme anahtarlarının saklanması ve kurtarılması

> **Bu bölüm, yedekleme sisteminin en acı ironisini önlemek için var:
> yedek var, anahtar yok.**

### `Vault__BackupEncryptionKey`

Bütün yedekler bu anahtarla AES-256-GCM ile şifreleniyor.
**Anahtar kaybolursa geçmiş yedeklerin TAMAMI kalıcı olarak okunamaz hâle
gelir.** Kurtarma yolu yoktur — tasarım gereği yoktur.

**Zorunlu prosedür:**

1. Anahtarı en az **iki bağımsız yerde** saklayın. Örnek: (a) ekibin parola
   yöneticisi, (b) çevrimdışı bir kopya (kapalı zarf / donanım anahtar kasası).
   İki kopyanın **aynı sistemde** olması tek kopya sayılır.
2. Kimin erişimi olduğunu yazın. Tek kişiye bağlı bir anahtar, o kişiye bağlı
   bir şirket demektir.
3. Yeni bir ortam kurarken anahtarı **yeniden üretmeyin** — o ortamın eski
   yedeklerini açamaz hâle gelirsiniz.

### Anahtar değiştirme (rotation)

Anahtarı değiştirmeniz gerekirse:

1. **Eski anahtarı SAKLAYIN.** Eski yedekler yalnızca onunla açılır.
2. Yeni anahtarı `Vault__BackupEncryptionKey` olarak verin — yeni yedekler
   onunla şifrelenir.
3. Eski yedekleri geri yüklemek gerekirse, uygulamayı geçici olarak eski
   anahtarla çalıştırın.

> ⚠️ Vault bugün **çoklu anahtar** desteklemiyor: aynı anda hem eski hem yeni
> anahtarla çözebilme yeteneği yok. Rotation bu yüzden manuel bir işlem.
> Bu bilinen bir sınır ve düzenli rotation gerekiyorsa önce o yetenek
> eklenmelidir.

### `Security__ConnectionEncryptionKey`

Kullanıcıların kayıtlı bağlantı dizeleri bununla şifreleniyor. Kaybolursa
kullanıcılar veritabanı bağlantılarını **yeniden girmek** zorunda kalır —
yedeklerin aksine kurtarılabilir bir kayıp, ama yine de saklanmalı.

### `Jwt__Key`

Kaybolursa yalnızca herkes yeniden giriş yapar. Aslında bu, bir güvenlik
olayında **kasıtlı olarak yapılacak** şeydir. Yine de yedeklenmesi, plansız bir
toplu çıkışı önler.

---

## 5. Kontrol listesi

Üretime çıkmadan önce:

- [ ] Beş zorunlu değişken tanımlı ve **üçü de birbirinden farklı**
- [ ] `Vault__BackupEncryptionKey` iki bağımsız yerde saklanıyor
- [ ] Anahtarlara kimin eriştiği yazılı
- [ ] Migration adımı deploy hattında ayrı bir iş olarak tanımlı
- [ ] Reverse proxy `X-Forwarded-*` başlıklarını eziyor
- [ ] `/health/ready` yeşil
- [ ] `Vault__S3__*` tanımlı (yoksa yedekler sunucu diskinde kalır ve sunucu
      yeniden kurulduğunda giderler)
- [ ] **Control DB'nin kendi yedeği** ayarlanmış — Vault kullanıcının
      veritabanını koruyor, Namines'in kendi kayıtlarını değil
- [ ] Bir geri yükleme **tatbikatı** yapıldı (yedeği gerçekten geri yükleyin;
      denenmemiş bir yedek, yedek değildir)

---

## 6. Bilinen sınırlar

| Sınır | Ayrıntı |
|---|---|
| Vault çoklu anahtar desteklemiyor | Rotation manuel (yukarıda) |
| Control DB yedeği otomatik değil | Platformunuzun DB yedeğini kullanın |
| RPO / RTO tanımlı değil | İş gereksinimine göre belirlenmeli |
| Sıfır kesintili dağıtım tanımı yok | Migration'ları ileriye uyumlu yazın: kolon ekleyin, hemen silmeyin |
