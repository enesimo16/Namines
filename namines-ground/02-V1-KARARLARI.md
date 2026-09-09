# 02 — Ground v1: kararlar ve revize inşa sırası

> **Bu dokümanın işi:** `00-GENEL-BAKIS.md` *ne/neden*, `01-INSA-PLANI.md`
> *nasıl* diyor. Bu doküman **bugün karşılaşılan gerçeklerle** o planı
> güncelliyor: hangi açık kararlar nasıl kapatıldı, hangi kabul kriteri
> bugün karşılanamıyor ve bunun yerine ne yapılıyor.

---

## 1. Planı bugün olduğu gibi uygulayamayan tek gerçek: Neon anahtarı yok

`01-INSA-PLANI.md` G1'in kabul kriteri şunu istiyor:

> 1. Provizyon çağrılır; **Neon panelinde** veritabanının gerçekten oluştuğu görülür.
> 2. Dönen bağlantıyla **bağımsız bir `psql` oturumu** açılır, `SELECT 1` çalışır —
>    API'nin "başarılı" demesi yeterli sayılmaz.

`NEON_API_KEY` depoda yok. `new-phase/34-SENDEN-BEKLENENLER.md` madde 1 hâlâ
**kullanıcının yapması gereken bir iş** olarak duruyor ("neon.tech → kayıt ol").
Genel bakış §2'de hesabın 🟢 işaretli olması "hesap açılabilir" demek, "anahtar
elimizde" demek değil.

**Sonuç:** Neon sağlayıcısı yazılabilir ve sahte bir istemciyle tamamen test
edilebilir, ama **canlı Neon'a karşı kanıtlanamaz**. `00-GENEL-BAKIS.md` §9
madde 2'nin kuralı ("kanıt = çalışan komut + görülen çıktı") gereği, canlı
denenmemiş bir sağlayıcı **"çalışıyor" sayılmaz** — Vault'ta MySQL için
verilen kararla birebir aynı.

### Bunun yerine: ikinci bir sağlayıcı, kendi sunucumuzdaki PostgreSQL

`IDatabaseProvider`'ın **ikinci** uygulaması: `LocalPostgresProvider` —
operatörün **zaten işlettiği** bir PostgreSQL sunucusunda proje başına
veritabanı + ayrı kullanıcı açar.

Bunu eklemek üç şeyi aynı anda çözüyor:

| Sorun | Nasıl çözülüyor |
|---|---|
| Ground'un uçtan uca kanıtlanamaması | Yerel sağlayıcı **bugün** gerçek bir veritabanı açıyor; bağımsız `psql` ile doğrulanıyor |
| Soyutlamanın kanıtlanamaması | Bir arayüzün gerçek sınavı **ikinci** uygulamadır. Tek uygulamalı arayüz, doğru soyutlandığını kanıtlamaz |
| Neon hesabı olmayan kullanıcı | Kendi sunucusunu kullanan (self-hosted) kurulum Ground'u üçüncü taraf hesabı olmadan kullanabiliyor |

**Bu, §2'nin reddettiği "kendi cluster'ımızı işletmek" DEĞİL.** Aradaki fark
kritik: §2, *müşteriler için* 7/24 nöbetli bir veritabanı filosu işletmeyi
reddediyor. `LocalPostgresProvider` ise **operatörün kendi sunucusunda, kendi
sorumluluğunda** çalışıyor — Namines kimseye SLA vermiyor. Bu yüzden arayüzde
bu sağlayıcı **açıkça** "kendi sunucun, kendi sorumluluğun" diye etiketleniyor.

---

## 2. Açık kararlar (§8) — kapatıldı

Üçü de kapatılmadan G1 yazılamaz. Verilen cevaplar **yapılandırmadan
okunuyor**, koda gömülü değil: ürün kararı değiştiğinde kod değişmesin.

### 2.1 Plan bazlı kota

`PlanQuotas.PlanLimits`'e `ManagedDatabases` eklenir. Bugüne kadar
eklenmemesinin gerekçesi dosyanın kendi yorumunda yazıyor — *"Olmayan bir
özelliğe kota koymak, uygulanmayan bir kuralı 'uygulanıyor' diye kaydetmek
olurdu."* Özellik artık var, dolayısıyla kota da meşru.

| Plan | Yönetilen DB | Gerekçe |
|---|---|---|
| Free | **0** | Her yönetilen DB gerçek ve sürekli bir maliyet. `BranchDatabases: 0` ile birebir aynı gerekçe — ücretsiz katmanda sınırsız açılması sunucuyu/faturayı taşır |
| Pro | **2** | `BranchDatabases: 2` ile hizalı; bir üretim + bir hazırlık ortamı |
| Team | **20** | `BranchDatabases: 20` ile hizalı |
| Enterprise | **-1** | Sözleşmeyle |
| Dev | sınırsız | Mevcut desen |

> Free'nin 0 olması Ground'u denenemez kılmıyor: **kendi sunucusunu bağlamak**
> (Desk'in var olan "bağlantı ekle" akışı) her planda serbest. Kota, *bizim
> açtığımız* kaynağa ait.

### 2.2 Silme bekleme süresi

**7 gün.** `Ground:DeleteGraceDays` ile değiştirilebilir.

Gerekçe: bir veritabanının yanlışlıkla silindiği, çoğu zaman ancak birileri
onu kullanmayı denediğinde anlaşılır — bu da hafta sonunu kapsayabilir. Daha
kısa bir pencere (24 saat) tatildeki bir ekibi kurtaramaz; daha uzunu (30 gün)
silinmiş sayılan kaynağın faturasını aylarca sürdürür.

Pencere içinde kayıt `PendingDelete`; **geri alınabilir**. Pencere dolunca
sağlayıcıdan kalıcı olarak silinir.

### 2.3 Neon organizasyon/proje yapısı

**Namines projesi başına bir Neon projesi.**

Alternatif (tek Neon projesi + proje başına branch) daha ucuz görünüyor ama
iki gerçek sorunu var: (1) Neon'da branch'ler **aynı depolamayı paylaşır**,
yani bir kiracının veri hacmi diğerlerinin maliyetine karışır ve faturayı
kiracıya dağıtmak tahmine dönerdi — `§9 madde 4: uydurma sayı yok` ile
çelişir; (2) branch silme, ata branch'e bağımlıdır — bir kiracının silinmesi
diğerlerini etkileyebilir.

Branch'ler v2'nin işi: **aynı** Namines projesinin PR önizlemesi için
(`00-GENEL-BAKIS.md` §10 madde 1) — orada paylaşım zaten istenen şey.

---

## 3. Revize inşa sırası

`01-INSA-PLANI.md`'nin G0–G4'ü geçerli; yalnızca sağlayıcı sırası değişti.

```
G0  Modül iskeleti + IDatabaseProvider + INeonClient ayrımı
G1  LocalPostgresProvider  → provizyon, uçtan uca CANLI kanıtlanır
G2  Kayıt + bağlantının projeye bağlanması (şifreli) + kota
G3  Silme: PendingDelete (7 gün) + geri alma + kalıcı silme (arka plan işi)
G4  NeonProvider           → yazılır, sahte istemciyle test edilir,
                             CANLI KANITLANMADI diye işaretlenir
G5  Desk arayüzü           → "Barındırma" görünümü
```

**Neden yerel sağlayıcı önce:** yürüyen iskeleti kanıtlanabilir kılan tek yol
(§1). Neon önce yazılsaydı G1–G3'ün tamamı kanıtlanmamış olurdu ve hata
sınıfları ancak anahtarın geldiği gün ortaya çıkardı.

---

## 4. Değişmeyen kurallar

`00-GENEL-BAKIS.md` §9'un dördü de aynen geçerli. İkisi bu belgede zaten
uygulandı:

- **Kanıt = çalışan komut + görülen çıktı** → §1'in tamamı bu kuralın sonucu.
- **Dürüst risk iletişimi** → `NeonProvider` "canlı kanıtlanmadı" etiketiyle
  gelir; `LocalPostgresProvider` "kendi sunucun, kendi sorumluluğun" der.
  Hiçbiri arayüzde olduğundan güvenli gösterilmez.

Ek olarak Vault'tan miras alınan ve burada da geçerli olan iki şey:

- **Bağlantı dizesi tarayıcıya asla gitmez** — `IConnectionSecretProtector`
  ile şifreli saklanır, sunucuda çözülür.
- **Yıkıcı işlem eşiği** — silme, Vault'un geri yükleme onayıyla aynı
  düzeyde: proje adını elle yazmak.

---

## 5. v1'e GİRMEYENLER (ve nedeni)

| Yetenek | Neden yok |
|---|---|
| Neon copy-on-write branch | v2. Önce tek akış kanıtlanmalı (§9 madde 3) |
| Multi-region | v2. Sağlayıcı arayüzü `Region` taşıyor, seçim arayüzü yok |
| MySQL/SQL Server yönetilen DB | Başka sağlayıcı, ayrı entegrasyon |
| Kullanım/faturalama metrikleri | `GetMetricsAsync` arayüzde var; yerel sağlayıcı gerçek boyutu döner, Neon'unki canlı kanıtlanamadığı için v1'de arayüze **yansıtılmaz** |
| Kendi işlettiğimiz cluster (müşteri için) | Kalıcı olarak ertelendi — §2 |
