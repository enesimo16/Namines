# Namines Vault — Erişim modeli ve deploy

Bu belge iki soruyu cevaplıyor:

1. **Vault'a nereden erişiyoruz?** Kendi sitesi mi var, Desk'in içinde mi?
2. **Proje deploy edildiğinde bunlar otomatik olarak çalışır mı?**

---

## 1. Vault'un kendi sitesi YOK

**Vault bir arayüz uygulaması değil, bir arka uç modülü.** Kullanıcının gördüğü
ekran **Namines Desk'in içinde bir görünüm** — soldaki *İşlemler* grubunda
"Yedekler".

```
Tarayıcı
   │
   └─ Namines Desk  (Next.js, :3200)     ← kullanıcının gördüğü TEK arayüz
         │  services/desk/app/Vault.tsx
         │  services/desk/lib/vault.ts
         │
         └─ HTTP ─→ Namines.API  (:5000)
                       │  Controllers/VaultController.cs   → /api/vault/*
                       │  Infrastructure/Services/VaultService.cs
                       │
                       └─ Namines.Vault  (sınıf kütüphanesi, sunucu yok)
                             ├─ pg_dump / pg_restore  → geçici konteyner
                             └─ AES-256-GCM + dosya deposu
```

### Neden ayrı bir site değil

Üç somut sebep:

**Yedekleme veriye erişimin bir parçası, ayrı bir ürün değil.** Kullanıcı zaten
Desk'te verisine bakıyor, satır düzenliyor, SQL çalıştırıyor. "Bu verinin bir
kopyasını al" o bağlamın içinde anlamlı; ayrı bir siteye gönderilmek, projeyi
ve oturumu ikinci kez seçtirmek demek olurdu.

**İkinci bir arayüz, ikinci bir yetki yüzeyi demek.** Vault'un yaptığı en yıkıcı
işlem (geri yükleme) veritabanının tamamını siliyor. Bunu Desk'in zaten
doğrulanmış oturumu ve rol modeli (`OrgAccess`) üzerinden yapmak, aynı kararı
ikinci bir yerde tekrar uygulamaktan hem daha güvenli hem daha az kod.

**`Namines.Vault` yine de ayrı bir proje** — ama derleme sınırı olarak, ürün
sınırı olarak değil. `Namines.Vault.csproj` yalnızca `Namines.Core`'a bağlı;
`Namines.Infrastructure` referansı **yok**. Yedekleme mantığının veritabanına
ya da kimliğe bulaşması derleme hatası veriyor.

### Erişim özeti

| Katman | Nerede çalışır | Kullanıcı görür mü |
|---|---|---|
| `Namines.Vault` | `Namines.API` sürecinin içinde, kütüphane olarak | Hayır |
| `/api/vault/*` | `Namines.API` (:5000) | Hayır (Desk çağırır) |
| "Yedekler" ekranı | Namines Desk (:3200) | **Evet — tek giriş noktası** |

Ana Namines uygulamasında (`frontend/`, :3000) Vault ekranı **yok**. Yedekleme
canlı veritabanı bağlanmış projeler için anlamlı; canlı bağlantı Desk'in
konusu, tasarım düzleminin değil.

### Yetkiler

| İşlem | Gereken rol | Neden |
|---|---|---|
| Yedekleri listeleme | Üye (Viewer+) | Salt okuma |
| Yedek alma | Editor+ | Veriyi değiştirmez, kaybı önler — dar bir role kilitlemek riski artırırdı |
| Doğrulama | Editor+ | Geçici bir sunucuda yapılır, projenin veritabanına dokunmaz |
| İndirme / silme | Admin+ | İndirme, verinin TAMAMINI almaktır |
| Zamanlama ayarı | Admin+ | Diski ve yedek geçmişini kalıcı etkiler |
| **Geri yükleme** | **Owner** | Veritabanının üzerine yazar; faturalama/org silme ile aynı ağırlıkta |

---

## 2. Deploy edildiğinde ne otomatik olur, ne olmaz

Kısa cevap: **zamanlanmış yedekler otomatik çalışır, ama üç şart sağlanmadan
çalışmaz — ve ikisi bugünkü `docker-compose.yml`'de sağlanmıyor.**

### Kendiliğinden olan

| Şey | Neden otomatik |
|---|---|
| Veritabanı tabloları (`VaultBackups`, `VaultRestores`, `VaultSchedules`) | EF migration'ları açılışta uygulanıyor — elle SQL yok |
| Zamanlayıcının başlaması | `AddHostedService<VaultScheduleBackgroundService>()`; API her açıldığında başlar |
| Zamanlanmış yedeğin doğrulanması | Her otomatik yedekten sonra kendiliğinden denenir |
| Eski otomatik yedeklerin temizlenmesi | Saklama sayısı aşıldığında |
| Çoklu instance'ta tek yedek | Koşullu `UPDATE` ile satır kapma — instance sayısından bağımsız |

### Desteklenen motorlar

| Motor | Araçlar | İstemci imajı (varsayılan) |
|---|---|---|
| PostgreSQL | `pg_dump` / `pg_restore` | `Vault__PostgresImage` — `postgres:17-alpine` |
| MySQL | `mysqldump` / `mysql` | `Vault__MySqlImage` — `mysql:8.0` |
| MariaDB | `mysqldump` / `mysql` | `Vault__MariaDbImage` — `mariadb:10.6` |

Araçlar API sunucusuna **kurulmuyor**; her yedek/geri yükleme, istemciyi
taşıyan kısa ömürlü bir konteynerde çalışıyor.

**İmaj etiketi sunucu sürümünden küçük olmamalı:** eski bir istemci yeni bir
sunucuyu reddeder — ve bu iyi, sessizce eksik dump almaktansa.

**MySQL ve MariaDB için imajlar AYRI** ve bu bilinçli: MySQL 8 istemcisiyle
MariaDB'ye dump almak (ya da tersi) kimlik doğrulama eklentisi ve sürüm-özel
deyimler yüzünden sessizce eksik/bozuk çıktı verebiliyor. Ayrıca MariaDB
istemcisi `--column-statistics` bayrağını hiç tanımıyor; canlı denemede dump
daha ilk adımda bu yüzden düştü.

Hangi motorların desteklendiği `GET /api/vault/health` cevabındaki `engines`
alanında ve Vault ekranında **yazıyor** — desteklenmeyen bir motorda kullanıcı
bunu ilk yedek denemesinde öğrenmemeli.

**Bir yedek yalnızca KENDİ motoruna geri yüklenebilir.** Motor, yedek kaydına
yazılıyor; proje sonradan başka bir motora taşınırsa geri yükleme reddediliyor.
Sebep: uyuşmayan bir geri yükleme, `--clean` hedefin nesnelerini düşürdükten
sonra sözdizimi hatalarıyla durup veritabanını boş bırakabilirdi.

### Sağlanması GEREKEN üç şart

**1. `Vault__BackupEncryptionKey` tanımlı olmalı.**
Tanımsızsa Vault **açıkça durur** — şifresiz yedek yazmaz. Bu bilinçli:
sessizce düz metin yedek almak, şifrelemenin hiç olmamasından daha kötü olurdu,
çünkü kullanıcı korunduğunu sanır.

> `Security__ConnectionEncryptionKey` ile **aynı olmamalı**. Aynı anahtar,
> birinin sızmasını ikisinin sızması yapar: bağlantı dizesini çözebilen biri,
> o bağlantının bütün geçmiş yedeklerini de çözebilirdi.

**2. API'nin Docker daemon'una erişimi olmalı.**
`pg_dump`/`pg_restore` geçici bir `postgres:17-alpine` konteynerinde çalışıyor.
Bugünkü `docker-compose.yml`'de backend konteynerinin daemon'a erişimi **yok**
(`docker.sock` bilinçli olarak mount edilmiyor — host'ta root eşdeğeri yetki
verirdi). Bu hâliyle **deploy'da yedekleme çalışmaz.**

Seçenekler, tercih sırasıyla:

- **API'yi host üzerinde çalıştırmak** (konteyner içinde değil). Daemon'a erişim
  doğal olarak var. En az değişiklik.
- **Provisioning broker'ı** — `new-phase/06-DATA-PLANE.md`'de zaten planlanmış
  olan, konteyner işlerini yapan ayrı ve dar yetkili servis. Doğru uzun vadeli
  cevap; `docker.sock`'u kimseye vermeden aynı işi yapar.
- **`docker.sock`'u backend'e mount etmek** — çalışır ama **önerilmez**:
  AGENTS.md'nin açık kuralına aykırı ve backend'i ele geçiren biri host'un
  tamamını ele geçirir.

**3. Yedeklerin durduğu yer KALICI olmalı.**

**Önerilen: nesne depo.** `Vault__S3__Bucket` tanımlıysa yedekler diske değil
oraya yazılır ve sunucu diskinin üç sorunu birden ortadan kalkar: disk yedek
boyutuyla dolmuyor, birden fazla instance aynı dosyayı görüyor, sunucu yeniden
kurulsa da yedekler kalıyor.

> **"S3 mi MinIO mu" sorusu düştü.** MinIO S3 protokolünü konuşuyor, dolayısıyla
> tek uygulama (`S3BackupStore`) ikisini de karşılıyor — ayrı iki sınıf yazmak
> aynı protokolü iki kez uygulamak olurdu. MinIO ve diğer S3 uyumlu sunucular
> için `Vault__S3__ServiceUrl` verilir; gerçek AWS için boş bırakılıp `Region`
> verilir.
>
> Canlı doğrulandı (gerçek MinIO): yedek nesne olarak yazıldı, veri bozulduktan
> sonra o nesneden geri yüklendi ve doğrulama da nesne deposundan okuyarak geçti.

**Nesne depo yoksa sunucu diski** (`Vault__StoragePath`). Konteyner içinde
varsayılan yol geçicidir: **konteyner yeniden yaratıldığında bütün yedekler
gider** — yani yedekleme tam da işe yaraması gereken anda yok olur. Bugünkü
compose'da `namines-app-data` volume'u zaten var ve `Vault__StoragePath` onun
altına verilmiş durumda.

Hangi deponun kullanıldığı `GET /api/vault/health` ve Vault ekranında açıkça
yazıyor — kullanıcı bunu bilmeden yedeğe güvenmemeli.

### Deploy kontrol listesi

```bash
# 1) Yedek şifreleme anahtarı — baglanti anahtarindan FARKLI
Vault__BackupEncryptionKey=$(openssl rand -base64 32)

# 2) Yedekler KALICI bir yerde dursun.
#    Önerilen: nesne depo (S3 ya da MinIO — ikisi de aynı ayarlar).
Vault__S3__Bucket=namines-backups
Vault__S3__AccessKey=...
Vault__S3__SecretKey=...
#    MinIO / S3 uyumlu sunucu için:
Vault__S3__ServiceUrl=http://minio:9000
#    Gerçek AWS için ServiceUrl'i BOŞ bırak ve bölgeyi ver:
# Vault__S3__Region=eu-central-1
#
#    Nesne depo yoksa geri düşüş: kalıcı bir volume altındaki dizin.
Vault__StoragePath=/app/data/vault-backups

# 3) pg_dump imaj etiketi sunucu sürümüne eşit ya da ondan YENİ olmalı
#    (eski bir pg_dump yeni bir sunucuyu reddeder — ve bu iyi:
#     sessizce eksik dump almaktan iyidir)
Vault__PostgresImage=postgres:17-alpine
```

Ayrıca: API'nin Docker daemon'una erişimi (yukarıdaki 2. şart).

### Doğrulama

Deploy'dan sonra tek komutla:

```bash
curl -s https://<api>/api/vault/health
```

Dönen `store` alanı yedeklerin **nerede** durduğunu söyler. Geçici bir yol
görüyorsanız 3. şart sağlanmamış demektir.

---

## Bilinen sınırlar (v1)

| Sınır | Durum |
|---|---|
| ~~Yalnızca PostgreSQL~~ | **Kapatıldı:** MySQL ve MariaDB eklendi, ikisi de canlı doğrulandı (yedek → veriyi boz → geri yükle → bağımsız istemciyle satır/tetikleyici/yabancı anahtar kontrolü). MSSQL ve Oracle **kapsam dışı** — sebebi aşağıda. |
| MSSQL ve Oracle yok | Mimari, eksiklik değil. `BACKUP DATABASE TO DISK` ve `expdp`, dosyayı **sunucunun kendi diskine** yazar; istemci tarafına akıtılabilen bir çıktı vermezler. Namines yedeği kendi tarafına çekemediği için şifreleyemez, nesne depoya koyamaz ve geri yüklenebilirliğini kanıtlayamaz. Sunucu diskine dosya bırakan bir "yedek" ise Vault'un vaadini karşılamaz. |
| SQLite yok | Aynı sınıf sınır. SQLite bir sunucu değil, **dosya**: uzaktan bir bağlantı dizesiyle erişilemiyor. Namines kullanıcının veritabanına ağ üzerinden bağlanıyor; SQLite'ta bağlanılacak bir uç yok. |
| ~~Yedekler sunucu diskinde~~ | **Kapatıldı:** S3 uyumlu nesne depo eklendi ve gerçek MinIO'ya karşı doğrulandı. Disk artık nesne depo yapılandırılmamışsa devreye giren geri düşüş. |
| Kısmi geri yükleme yok | Tek tablo değil, veritabanının tamamı geri yüklenir |
| Kaçırılan çalıştırma telafi edilmez | Bilinçli: sunucu iki gün kapalı kaldıysa açılışta iki yedek almak diski doldurmaktan başka işe yaramaz |
| Yedekleme sırasında ilerleme göstergesi yok | İstek, yedek bitene kadar açık kalır. Büyük veritabanlarında arka plan işine taşınmalı. |
