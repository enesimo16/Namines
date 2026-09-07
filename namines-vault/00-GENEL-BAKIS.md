# Namines Vault — Genel Bakış (plan)

> **Durum:** plan, henüz kod yok. Sıra: [`third-phase/00-BASLA-BURADAN.md`](../third-phase/00-BASLA-BURADAN.md)
> §3'e göre **Desk → Vault → Ground**. Desk v2 bitti
> ([`namines_desk/11-DESK-V2-TAMAMLANDI.md`](../namines_desk/11-DESK-V2-TAMAMLANDI.md)) —
> bu, sıradaki servis.

---

## 1. Vault nedir, ne değildir

**Namines Vault**: kullanıcının **gerçek, canlı** veritabanının yedeğini alan
ve geri yükleyen bir yetenek — ana Namines backend'inin **içinde** bir
.NET modülü olarak (`Namines.Vault.*` isim alanı), ayrı bir deploy birimi
DEĞİL.

> **Karar değişikliği (2026-09-07):** İlk planda third-phase §5'in
> mikroservis disiplini burada da uygulanmıştı. Gözden geçirildi — gerekçe
> [`third-phase/00-BASLA-BURADAN.md`](../third-phase/00-BASLA-BURADAN.md)'nin
> §3'ünün dayandığı `second-phase/14-AYRI-URUN-DEVELOPMENT-HOSTING.md`
> aslında **farklı bir konu** hakkında (kullanıcının kendi uygulamasını
> barındırma riski, Shopify tarzı) ve Vault'a hiç uygulanmıyor. Asıl
> belirleyici teknik gerçek: Vault, bağlantı dizesini çözmek için ana
> backend'in ZATEN sahip olduğu `AesGcmConnectionSecretProtector`'a muhtaç.
> Ayrı bir servis olsaydı bu ya kopyalanır (şifreleme anahtarının iki yerde
> durması saldırı yüzeyini büyütür) ya da her işlemde ana API'ye geri HTTP
> çağrısı atılırdı (izolasyon kazancı sıfır, gecikme + karmaşıklık kesin).
> Modül olmak bu ödünü ortadan kaldırıyor: aynı süreçte, aynı güvenli
> bileşene doğrudan erişim.
>
> **İzolasyon YİNE DE korunuyor** — yalnızca süreç seviyesinde değil, kod
> seviyesinde: kendi isim alanı (`Namines.Vault.*`), kendi klasörü
> (`backend/Namines.Vault/`), kendi rota öneki (`/api/vault/*`), kendi
> arayüzü (`IBackupProvider` — third-phase §5'in "sahte mikroservisi
> engelleyen" ilkesinin modül karşılığı: dış bağımlılıklara sızmadan iç
> disiplin). Arayüzde ve API sözleşmesinde hâlâ **"Namines Vault"** olarak
> adlandırılıyor — kullanıcı için görünmez fark, geliştirici için net sınır.

**Vault DEĞİLDİR:**
- Namines'in kendi control-plane veritabanının (`namines-control-db`) yedeği —
  o ayrı, zaten mevcut operasyonel bir konudur, Vault'un ürünü değil.
- Bir "sır kasası" (secrets vault, HashiCorp Vault tarzı). `new-phase/24-ROADMAP.md`'deki
  "Sır kasası (Vault) entegrasyonu" (§2.5) **farklı bir kavram** — sandbox
  kimlik bilgilerini saklamak için. İsim çakışması bilinçli değil, tesadüfi;
  karıştırılmaması için bu not buraya düşüldü.
- Bir DDL/şema sandbox'ı. Aşağıdaki §2 tam olarak bunun neden yanlış olduğunu
  anlatıyor.

---

## 2. Mevcut kod neden Vault'un temeli OLAMAZ

`backend/Namines.Infrastructure/Services/DockerBackupService.cs`
(`RunSandboxAndBackupAsync(jobId, sqlContent, dbType, onProgress)`) var, ama:

- Parametre **`sqlContent`** — yani DDL (şema), veri değil.
- Akış: DDL'den geçici bir Docker container'da **boş** bir veritabanı kurar,
  sonra o boş veritabanını yedekler.
- Sonuç: bu bir **şema yedeği**, gerçek satırları hiç içermiyor.

Vault'un yapması gereken **tam tersi**: kullanıcının zaten var olan, dolu,
canlı veritabanına bağlanıp `pg_dump` / `mysqldump` / `BACKUP DATABASE`
çalıştırmak. `DockerBackupService`'e dokunulmuyor — o, third-phase §7'de
listelenen ve Desk'in kanıtlandığı gün tek commit'te temizlenecek eski
Scaffolder/CoderAI kümesinin bir parçası, Vault'la ilgisi yok.

**Docker'ın rolü burada da SANDBOX değil**: yalnızca istemci araçlarını
(`pg_dump`, `mysqldump`, `sqlcmd`) taşınabilir şekilde çalıştırmak için resmi
imajlar kullanılacak. Zorunlu değil — ileride binary'ler paketlenirse
tamamen çıkarılabilir (third-phase §4'ün aynı tespiti).

---

## 3. Kapsam — v1 (MVP)

Namines Desk'in kendi v1/v2 ayrımıyla aynı disiplin: önce **yürüyen iskelet**,
sonra ekran/motor genişlemesi.

| # | Yetenek | v1'de var mı |
|---|---|---|
| 1 | Manuel yedek alma (tek tıkla, tam dump) | ✔ |
| 2 | Yedek listesi (tarih, boyut, motor, durum) | ✔ |
| 3 | Geri yükleme (yeni/var olan bir bağlantıya) | ✔ |
| 4 | Zamanlanmış otomatik yedek (günlük/haftalık) | ✔ |
| 5 | Yedek indirme (kullanıcı kendi diskine) | ✔ |
| 6 | Nokta-zamanlı geri yükleme (point-in-time restore) | ❌ — v2, sağlayıcıya bağlı (bkz. §7) |
| 7 | Çapraz-motor geri yükleme (PG dump → MySQL'e yükle) | ❌ — kalıcı olarak dışarıda, §8 |
| 8 | Şifreleme-at-rest (yedek dosyası) | ✔ — zorunlu, opsiyonel değil |

**v1 tek motor ile başlar: PostgreSQL.** Desk'in "önce PostgreSQL'de kanıtla,
sonra motor bazında dürüstçe genişlet" disiplini (third-phase §2, §6.3)
burada da geçerli — MySQL/MariaDB/SQL Server/Oracle/SQLite için `pg_dump`
yerine geçecek araç ayrı ayrı denenmeden "destekleniyor" denmeyecek.

---

## 4. Mimari

### 4.1 Akış (v1)

```
Kullanıcı (Vault arayüzü) → JWT ile giriş, proje seç (Desk'in D1 deseni)
  → "Yedek al" → POST /api/backup/{projectId}
      → sunucu: CloudProject.EncryptedConnectionString'i çöz (Desk'in AES-256-GCM deseniyle AYNI mekanizma — bkz. §5)
      → connection string'e göre doğru araç seçilir: pg_dump | mysqldump | sqlcmd BACKUP DATABASE
      → çıktı nesne depoya (S3/MinIO uyumlu) yazılır, ŞİFRELİ
      → VaultBackupRecord: {id, projectId, engine, sizeBytes, startedAt, completedAt, status, storageKey}
  → "Geri yükle" → POST /api/restore/{backupId}
      → hedef bağlantıyı doğrula (yazma izni + "bu SİLİCİ bir işlem" onayı — Desk'in "SİL" yazma deseniyle
        aynı ilke: 34-SENDEN-BEKLENENLER.md madde 12'nin kökeni)
      → dump'ı hedefe uygula, ilerlemeyi akıt
```

### 4.2 Depolama

Yedek dosyaları **Vault'un kendi veritabanında değil**, nesne depoda
(S3-uyumlu — MinIO'yu kendi barındırmak ya da Cloudflare R2/Backblaze B2 gibi
ucuz bir sağlayıcı; `new-phase/06-DATA-PLANE.md` §9'daki "MinIO/S3'e yedek"
maddesiyle aynı yön) durur. Vault'un kendi DB'sinde yalnızca **metadata**
(`VaultBackupRecord`) tutulur — dump içeriği asla control-plane DB'sine
girmez.

**Kota:** plan bazlı saklama süresi ve toplam depolama sınırı (Free: 1 yedek,
7 gün; Pro/Team: N yedek, 30-90 gün) — tam sayılar bir ürün kararı,
[`34-SENDEN-BEKLENENLER.md`](../new-phase/34-SENDEN-BEKLENENLER.md)'ye
eklenmesi gereken açık madde (bkz. §9).

### 4.3 Zamanlanmış yedekler

Basit bir arka plan işi (`.NET` `IHostedService` + `Cron` ifadesi, ya da
mevcut altyapıda zaten kullanılan bir job scheduler varsa onun deseni) —
Ground henüz yokken bile Vault BYODB (kullanıcının kendi bağlantısı)
üzerinde bağımsız çalışabilir; Ground'a bağımlı DEĞİL.

---

## 5. Kimlik doğrulama ve bağlantı çözümü

Desk'in D1 kararı **birebir tekrarlanır**, yeniden icat edilmez:

- Vault, JWT ile çalışır — ham bağlantı dizesi veya API anahtarı istemciye
  hiç gelmez.
- Bağlantı, `CloudProject.EncryptedConnectionString` üzerinden **sunucu
  tarafında** çözülür — Vault artık aynı süreçte yaşayan bir modül olduğu
  için (§1'deki mimari kararı) `AesGcmConnectionSecretProtector`'ı
  **doğrudan** enjekte eder, HTTP'ye hiç çıkmaz. Ham parola yalnızca çözüldüğü
  isteğin yaşam süresince bellekte durur, hiçbir yere loglanmaz/yazılmaz.
- Yetki: `OrgAccess.CanViewAsync` yeterli değil — yedek almak/geri yüklemek
  **Admin+** gerektirir (Desk'in API anahtarı yönetimiyle aynı eşik,
  `GatewayKeyController.CanManageAsync`), çünkü geri yükleme **verinin
  tamamını değiştirebilir**.

**Neden doğrudan enjeksiyon, ayrı bir servise HTTP değil:** Vault ayrı bir
deploy birimi olsaydı, bağlantı çözme mantığını ya kopyalamak (şifre çözme
anahtarının iki yerde durması saldırı yüzeyini büyütür) ya da her işlemde
ana sürece geri HTTP çağrısı atmak gerekirdi — ikisi de kazancı olmayan bir
maliyet. Modül olmak (§1) bu ödünü tamamen ortadan kaldırıyor: aynı süreç,
aynı güvenli bileşen, tek bir doğruluk kaynağı.

---

## 6. Geri yükleme güvenliği

Geri yükleme **geri alınamaz** ve hedef veritabanının **tamamını** üzerine
yazar. Desk'in toplu silme deseninin (10+ satırda "SİL" yazma zorunluluğu)
çok daha ağır bir versiyonu gerekiyor:

- Hedefteki mevcut satır/tablo sayısı önceden gösterilir ("Bu işlem şu anki
  1.240 satırı geri dönüşü olmayan şekilde SİLECEK").
  Kullanıcı **projenin adını** yazmalı (GitHub'ın "type the repo name to
  delete" deseni — Desk'in "SİL" kelimesinden daha güçlü bir onay, çünkü
  yanlış projeye geri yükleme burada çok daha pahalı bir hata).
- Restore işlemi audit trail'e yazılır (Desk'in `GatewayAuditEntry` deseniyle
  aynı ilke — ama Vault'un kendi tablosunda, `VaultRestoreEntry`).
- Restore ÖNCESİ hedefin **otomatik bir "önce" yedeği** alınır (kullanıcı
  restore'u pişman olursa geri dönebilsin) — bu, tek başına Vault'u
  kendi güvenlik ağı yapan bir karar.

---

## 7. Sağlayıcıya özgü sınırlar (dürüstlük tablosu)

| Motor | Araç | Nokta-zamanlı geri yükleme | v1 destek |
|---|---|---|---|
| PostgreSQL | `pg_dump` / `pg_restore` | Sağlayıcı PITR sunuyorsa (Neon: ✔) | ✔ |
| MySQL / MariaDB | `mysqldump` | Yalnızca binlog varsa | ❌ v1 |
| SQL Server | `sqlcmd` + `BACKUP DATABASE` | `.bak` + log restore ile mümkün | ❌ v1 |
| Oracle | `expdp`/`impdp` | Kurumsal lisans gerektirebilir | ❌ v1 |
| SQLite | Dosya kopyası | Anlamsız (tek dosya) | ❌ v1 — zaten dosya bazlı, Vault'a ihtiyacı yok |

**Kural (third-phase §6.3 ile aynı):** yazıp canlı doğrulanmayan motor
"destekleniyor" sayılmaz. v1 yalnızca PostgreSQL'i "✔" işaretler.

---

## 8. Kalıcı olarak Vault'a GİRMEYECEKLER

- **Çapraz-motor geri yükleme** (PostgreSQL dump'ını MySQL'e yükleme) — veri
  tipleri, kısıtlar ve prosedürel kod arasında kayıpsız bir çeviri yok; bunu
  "destekliyoruz" demek kullanıcıyı sessiz veri bozulmasına sokardı.
  Namines'in şema **üretimi** çok-motorlu, ama Vault'un **yedek/geri
  yükleme**si tek motor içinde kalır.
- **Namines'in kendi control-plane DB'sinin yedeklenmesi** — bu bir DevOps/
  altyapı sorumluluğu, ürün özelliği değil.
- **Otomatik felaket kurtarma orkestrasyon** (bölge failover vb.) — Ground'un
  sağlayıcısı (Neon) bunu zaten sunuyor; Vault onu tekrar icat etmiyor.

---

## 9. Bu planın açtığı yeni "senden beklenenler"

Aşağıdakiler `new-phase/34-SENDEN-BEKLENENLER.md`'ye eklenmesi gereken açık
kararlar (Vault'un koduna başlamadan önce netleşmeli):

1. **Nesne depo sağlayıcısı** — kendi barındırılan MinIO mu, yoksa Cloudflare
   R2 / Backblaze B2 / AWS S3 gibi yönetilen bir hizmet mi? (Maliyet ve
   operasyonel yük burada farklı.)
2. **Plan bazlı saklama/kota sayıları** — Free/Pro/Team için yedek sayısı ve
   saklama süresi (yukarıdaki §4.2'nin taslak önerisi onay bekliyor).
3. **Zamanlanmış yedek sıklığı seçenekleri** — kullanıcıya kaç seçenek
   sunulacak (günlük/haftalık sabit mi, yoksa cron ifadesi mi)?

---

## 10. Değişmeyen kurallar (third-phase ve Desk'ten miras)

1. **Modül sınırı.** Ayrı bir deploy birimi değil ama kod disiplini aynı
   sıkılıkta: `Namines.Vault.*` isim alanı dışına sızmaz, kendi arayüzü
   (`IBackupProvider`) üzerinden çağrılır, kendi rota önekinde yaşar
   (`/api/vault/*`) — third-phase §5'in "sahte mikroservisi engelleyen"
   ilkesinin modül karşılığı (§1).
2. **Kanıt = çalışan komut + görülen çıktı.** Bir yedek/geri yükleme iddiası,
   gerçek bir `psql`/`mysql` bağımsız doğrulamasıyla kanıtlanmadan
   "çalışıyor" sayılmaz.
3. **Motor bazında dürüstlük.** §7'deki tablo, kodlanmadan önce de sonra da
   güncel tutulur.
4. **Uydurma güvenlik yok.** Şifreleme-at-rest, onay akışı ve audit kaydı
   opsiyonel özellik değil — v1'in parçası.
5. **Yürüyen iskelet önce.** Tek motor (PostgreSQL), tek akış (manuel yedek +
   geri yükleme) uçtan uca kanıtlanmadan zamanlanmış yedek / ikinci motor
   eklenmez.

---

## 11. Ek özellikler (öneri, sınır içinde)

Aşağıdakiler Vault'un **tek işine** (bir veritabanının yedeğini almak/geri
yüklemek) hizmet ediyor — hiçbiri kapsam dışına taşmıyor (ör. izleme,
faturalama, kullanıcı yönetimi gibi başka bir modülün işi değil).

1. **Yedek bütünlüğü doğrulama.** Alınan her yedek, arka planda geçici bir
   sandbox'a GERÇEKTEN geri yüklenip başarı/hata kaydediliyor —
   "yedek alındı ama bozuktu" sınıfı hatayı fark edilmeden aylarca
   saklanmaktan kurtarır. Sonuç `VaultBackupRecord.VerifiedAt`/`VerifyError`
   olarak tutulur, kullanıcıya "✓ doğrulandı" rozetiyle gösterilir.
2. **Yedek boyutu/sıklık trendi.** Desk'in Analytics deseniyle aynı —
   zaman içinde yedek boyutu büyüyorsa bu bir kapasite sinyali; tamamen
   `VaultBackupRecord` verisinden türetilir, uydurma sayı yok.
3. **Restore'a ekstra sürtünme, PROJE DEĞİŞTİYSE.** Varsayılan: bir yedek
   yalnızca ALINDIĞI projeye geri yüklenebilir. Başka bir projeye geri
   yükleme (ör. prod yedeğini staging'e taşımak) ayrı, daha ağır bir onay
   ister (hedef proje adını yazma) — yanlış projeye geri yükleme, yanlış
   satırı güncellemekten çok daha pahalı bir hata.
4. **Yedek notu/etiketi.** Kullanıcı manuel bir yedeğe kısa bir not
   düşebilir (“migration öncesi”, “Cuma denemesi”) — liste büyüdükçe hangi
   yedeğin ne için alındığını hatırlamak zorlaşır.
5. **Webhook bildirimi (e-posta DEĞİL).** Yedek başarılı/başarısız
   olduğunda kullanıcının kendi verdiği bir URL'ye POST — e-posta altyapısı
   beklemeden (34-SENDEN-BEKLENENLER madde 14'ün engellediği e-posta
   bildiriminden FARKLI bir kanal) bugün yapılabilir bir bildirim biçimi.
6. **Kısmi geri yükleme (v2).** Tüm veritabanı yerine seçili tablo(lar) —
   kullanıcı yanlışlıkla sildiği tek bir tabloyu geri getirmek için tüm
   veritabanını riske atmak istemeyebilir. v1'in "tüm veritabanı" akışından
   sonra değerlendirilir.
