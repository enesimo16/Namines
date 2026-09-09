# 12 — UI/UX denetimi

> ⚠️ **Sınır:** Bu denetim sırasında uygulama tıklanarak gezilmedi. Aşağıdakiler
> kod okumasından ve durum sinyallerinin sayımından çıkarıldı. Bir UX denetimi
> normalde ekrana bakılarak yapılır; bu rapor onun yerini tutmaz, ona hazırlık
> yapar.

---

## Ölçülen durum sinyalleri

| Sinyal | `frontend/` | `services/desk/` |
|---|---|---|
| Yükleniyor durumu | 50 + 37 (`isLoading`) | 5 |
| Boş durum (empty state) | 30 + 28 | 11 (ilk sayımda 0 görünmüştü — büyük/küçük harf hatası) |
| Hata durumu | 193 | 51 |
| Skeleton | **0** | 0 |
| Onay / "emin misiniz" | 84 (iki uygulama toplamı) | — |

**Okuma:** Hata durumları en yoğun işlenen alan (193). Bu iyi bir işaret —
çoğu projede tam tersidir. Skeleton hiç yok; yükleme metinle gösteriliyor.

> ⚠️ **Bu tablo bir yanlış pozitif üretti.** "Desk'te boş durum yok" sonucu,
> aramanın büyük harfle (`Empty`) yapılmasından geliyordu; CSS sınıfı `empty`.
> Sayımla yapılan denetimin sınırı tam olarak budur — sayı bir işaret, kanıt
> değil. Düzeltilmiş bulgu aşağıda (UX-001).

---

## UX-001 — Desk'te boş durumlar İNCE (kısmen yanlış pozitifti)

> **Düzeltme:** İlk bulgu "boş durum yok" diyordu. Bu **yanlıştı** — arama
> büyük harfle (`Empty`) yapılmıştı ve CSS sınıfı `empty` olduğu için hiçbir
> şey bulamadı. Boş durumlar **var**. Gerçek sorun nitelikti, yokluk değil.

### Finding
Desk'te iki farklı kalitede boş durum vardı:

- **İyi:** `Projects`, `Deployments`, `Analytics` — başlık + sebep + sonraki adım.
- **İnce:** `ApiKeys`, `Members`, `Vault`, `Ground`, `Desk` (tablo) — tek satır:
  *"Bu projede üye yok."*

### Location
`services/desk/app/*.tsx` — Projects, Vault, Ground, Members, Logs, Analytics,
Deployments ekranlarının tamamı.

### Problem
Desk, kullanıcının **ilk karşılaştığı** yönetim arayüzü. İlk girişte her liste
boş: proje yok, yedek yok, üye yok, log yok. Boş durum yoksa kullanıcı boş bir
tablo görür ve "bozuk mu, yoksa gerçekten boş mu?" diye düşünür.

Boş durum, onboarding'in en ucuz biçimidir: "Henüz yedek almadınız. İlk yedeği
almak için önce bir veritabanı bağlayın →".

### Risk
İlk kullanım deneyiminde terk (drop-off). Ürünün değerini gösteremeden kaybedilen
kullanıcı.

### Severity **MEDIUM** (ürün açısından HIGH)
### Effort S — ekran başına birkaç satır
### Priority **P1**

### ✅ Yapıldı
Ortak `services/desk/app/EmptyState.tsx` bileşeni yazıldı (başlık + açıklama +
isteğe bağlı eylem) ve ince olan 5 ekrana uygulandı: `ApiKeys`, `Members`,
`Vault`, `Ground`, `Desk` (tablo seçilmedi / tablo boş).

**Filtre sonucu boş kalan listeler bilerek dışarıda bırakıldı** — orada
kullanıcı ne yapacağını zaten biliyor (filtreyi değiştirecek) ve büyük bir
açıklama kutusu gürültü olurdu. Bu ayrım bileşenin kendi belgesinde yazılı.

---

## UX-002 — Onboarding yolu var ama tek yönlü

### Bulgu
`/new` ve `/demo` rotaları var; `frontend/lib/templates.ts` 1.942 satır şablon
tanımı taşıyor. Yani "sıfırdan başlama" sorunu düşünülmüş.

`/demo` özellikle değerli: kayıt olmadan denemek, bu kategoride dönüşümün en
büyük belirleyicisi.

### Eksik
Denetimde görülemedi: kullanıcı `/demo`'dan gerçek bir hesaba **nasıl geçiyor**?
Demo'da yaptığı iş korunuyor mu? Korunmuyorsa demo, kullanıcıyı ikna ettiği anda
onu sıfırdan başlamaya zorluyor demektir — dönüşümün klasik kaybı.

### Recommendation
Demo → kayıt akışında şemayı taşı (localStorage'daki tuval durumu kayıt sonrası
sunucuya senkronlansın). Kod bunu destekleyecek yapıya sahip görünüyor
(`useSchemaStore` + `/api/auth/sync`).
### Effort M · ### Priority P1 (ürün etkisi yüksek)

---

## UX-003 — Yıkıcı işlemlerde onay: KISMEN İYİ

84 onay/`confirm` kullanımı var. Denetimde **doğrulanan** en iyi örnek Vault:

```
Geri yüklemek için veritabanının adını birebir yazın
```

`VaultController.Restore` `ConfirmDatabaseName`'i tam eşleşme ile kontrol ediyor
(`StringComparison.Ordinal`). Bu, GitHub'ın repo silme akışıyla aynı desen ve
**doğru** desen.

### Eksik olan
Aynı titizlik `DatabaseExecutorController` yolunda **yok**: `DROP TABLE` içeren
bir betik hiçbir onay olmadan çalışıyor (bkz. SEC-001).

### Öneri
Yıkıcı işlemler için tek bir kural belirle ve her yerde uygula:
- **Geri alınabilir** → basit onay
- **Geri alınamaz** → nesne adını yazdır
Bugün bu kural Vault'ta var, executor'da yok.

---

## UX-004 — İki ayrı arayüz, iki ayrı dil (kelimenin tam anlamıyla)

> **Not:** Bu başlık mecazi yazılmıştı; denetim sonunda **birebir doğru**
> olduğu görüldü: `frontend` İngilizce, `services/desk` Türkçe.

`frontend/` ve `services/desk/` ayrı uygulamalar. Kullanıcı açısından soru:
**"Ben şimdi hangisindeyim ve neden?"**

`DURUM.md` bunu kısmen cevaplıyor (Ground ve Vault'un kendi siteleri yok, Desk
üzerinden erişiliyor). Ama bu bilgi **belgede**, arayüzde değil.

### Risk
Kullanıcı canvas'ta şema tasarlarken yedek almak isterse Desk'e geçmesi
gerektiğini nereden bilecek? İki uygulama arasında görsel süreklilik ve
gezinme köprüsü yoksa ürün "iki ayrı ürün" gibi hissedilir.

### Severity **MEDIUM** · ### Effort M · ### Priority P2
### Recommendation
Ortak bir üst gezinme çubuğu ve tutarlı tasarım dili. `FRONTEND.md`'deki sabit
palet iki uygulamada da uygulanıyorsa yarı yol zaten alınmış — **doğrulanmadı**.

---

## UX-005 — Sahte jeton ekranı (ürün açısından)

Bkz. [SEC-003](06-security-audit.md#sec-003--sahte-personal-access-token-özelliği).

UX açısından da ayrı bir sorun: kullanıcı bu ekranda bir iş yapıyor, sistem
"başarılı" diyor, ama hiçbir şey olmuyor. Bu, **güven bozan** en tehlikeli
etkileşim türü — çünkü hata mesajı bile vermiyor.

### Priority **P0** — kaldırılmalı ya da gerçek yapılmalı.

---

## Terminoloji değerlendirmesi

Ürünün kendi sözlüğü var: **Canvas**, **Compile**, **Branch**, **Change Request**,
**Gateway**, **Vault**, **Ground**, **Desk**, **Eject**, **NSL**.

| Terim | Değerlendirme |
|---|---|
| Branch, Change Request | ✅ Geliştirici zaten biliyor (git'ten) |
| Compile, Eject | ✅ Anlamlı ve öğretici |
| Vault | ✅ "Yedek" için sezgisel |
| **Ground** | ⚠️ Ne olduğu adından anlaşılmıyor ("yönetilen veritabanı") |
| **Desk** | ⚠️ Aynı sorun |
| **Gateway** | 🟡 Doğru ama jenerik |

**Öneri:** Ground ve Desk gibi soyut adların yanına arayüzde her zaman bir
açıklayıcı alt başlık koy ("Ground — yönetilen veritabanı"). Marka adı
öğretilebilir, ama ilk karşılaşmada anlam taşımalı.

### ❌ Kısmen YANLIŞ POZİTİF — düzeltme

Bu değerlendirme **kod dosyası adlarına** bakılarak yapıldı, arayüzdeki
etiketlere değil. `services/desk/lib/nav.ts` kontrol edildiğinde Desk'in
gezinmesinin zaten açıklayıcı adlar kullandığı görüldü:

| Modül | Arayüzdeki etiket |
|---|---|
| Vault | **Yedekler** |
| Ground | **Barındırma** |
| Gateway (API anahtarları) | **API anahtarları** |
| Canvas | **Şema** |
| Desk (veri) | **Veri** |

Yani kullanıcı hiçbir zaman "Ground" kelimesini görmüyor. Bulgu geri çekildi.

**Ayakta kalan gerçek gözlem:** `frontend` **İngilizce**, `services/desk`
**Türkçe**. Aynı ürünün iki yüzü iki farklı dilde. Bu, terminolojiden daha
büyük bir tutarlılık sorunu — ve denetimin ilk taramasında i18n bölümünde
"UI İngilizce" denerek kaçırıldı. Bkz. UX-004.

---

## Denetlenmesi gereken, denetlenemeyen alanlar

Aşağıdakiler için uygulamanın gezilmesi şart:

1. Gezinme mantığı ve "neredeyim" hissi
2. Hata mesajlarının ekranda gerçekten görünüp görünmediği
3. Mobil kullanılabilirlik (canvas mobilde muhtemelen kullanılamaz — bu bir
   ürün kararı olabilir ama **açıkça söylenmeli**)
4. Yükleme sürelerinin algılanan hızı
5. Klavye kısayolları (kod taramasında bulunamadı — muhtemelen yok)
