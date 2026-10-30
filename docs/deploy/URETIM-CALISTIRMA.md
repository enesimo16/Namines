# Üretimde çalıştırma

> Bu belge, denetimin iki bulgusunu kapatmak için yazıldı:
> **DEVOPS-001** (dağıtım tanımı depoda yok) ve **DR** (şifreleme anahtarı
> kurtarma planı yok). Bkz. [`docs/audit/`](../audit/).

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

---

## 7. Yedekleme, doğrulama ve geri yükleme (`scripts/namines-backup`)

### Ne yedeklenir
Her çalıştırma **iki ayrı, şifreli** arşiv üretir:

| Arşiv | İçerik |
|---|---|
| `namines-backup-<ts>.tar.gz.age` | `pg_dump` (gzip), `app_data`, Caddyfile, `metadata.txt` |
| `namines-secrets-<ts>.tar.gz.age` | `.env` ve DB parolası içeren compose dosyası |

Sırlar bilerek DB dump'ından ayrıdır: tek bir sızıntı hem veriyi hem anahtarları vermesin. İkisini **farklı yerde / farklı erişimle** saklayın.

### Şifreleme (zorunlu)
Şifreleme yapılandırılmadan yedek **alınmaz**. `/etc/namines-backup.conf` (ya da ortam değişkenleri):

```bash
# Önerilen: age. Özel anahtarı SUNUCUDA TUTMAYIN (yalnızca public key gerekir).
#   age-keygen -o namines-backup.key   # güvenli bir yerde saklayın (parola yöneticisi / kasa)
BACKUP_AGE_RECIPIENT=age1xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
# Yedek: gpg --symmetric (AES256). Parola dosyası modu 600.
# BACKUP_GPG_PASSPHRASE_FILE=/etc/namines-backup.pass
# Opsiyonel off-site (rclone remote'u önceden `rclone config` ile kurulur: S3/B2/R2)
# BACKUP_RCLONE_REMOTE=r2:namines-backups/prod
# BACKUP_REMOTE_RETENTION_DAYS=30
```
Şifresiz arşiv hiçbir koşulda uzak depoya yüklenmez. (`BACKUP_ALLOW_UNENCRYPTED=1` yalnızca yerel acil durum içindir.)

Yerel saklama: 14 gün, ancak en yeni 7 arşiv her zaman korunur. Uzak depoda `BACKUP_REMOTE_RETENTION_DAYS` (en yeni 7 yine korunur).

### Cron
```cron
15 3 * * *  /home/ubuntu/namines/scripts/namines-backup           >> /var/log/namines-backup.log 2>&1
45 3 * * 0  BACKUP_AGE_IDENTITY_FILE=/run/secrets/backup.key /home/ubuntu/namines/scripts/namines-backup verify >> /var/log/namines-backup.log 2>&1
```
Haftalık `verify` özel anahtara ihtiyaç duyar; anahtarı yalnızca o anda mount edin ya da doğrulamayı güvenli bir başka makinede yapın.

### `namines-backup verify`
En son ana arşivi çözer, dump başlığındaki PostgreSQL major sürümüyle **geçici** bir container açar (`docker run --rm postgres:<major>-alpine`), dump'ı yükler ve kontrol eder: tablo sayısı ≥ `BACKUP_VERIFY_MIN_TABLES` (varsayılan 20), `__EFMigrationsHistory` dolu, üretimde kullanıcı varsa yedekte de var. Üretim DB'ye yazmaz; container ve geçici dosyalar her durumda silinir. Başarısızlıkta çıkış kodu 1.

### Geri yükleme runbook'u
1. **Karar:** olay türünü belirle (yanlış silme / bozulma / sunucu kaybı). Gerekirse API'yi durdur: `docker compose -f docker-compose.prod.yml stop namines-backend`.
2. **Arşivi getir:** yerelde yoksa `rclone copy r2:namines-backups/prod/namines-backup-<ts>.tar.gz.age .` (+ `.sha256`).
3. **Çöz ve aç:** `BACKUP_AGE_IDENTITY_FILE=/yol/key namines-backup restore <dosya>` (SHA-256 doğrulanır, `/tmp/namines-restore-*` altına açılır).
4. **Önce doğrula:** `namines-backup verify <dosya>` ile geçici container'da dump'ın sağlam olduğunu gör.
5. **Hedef DB'ye yükle** (boş/yeni veritabanı tercih edilir; dump `--clean --if-exists` içerir, mevcut nesneleri siler):
   `gunzip -c supabase-db.sql.gz | docker exec -i namines-control-db psql -v ON_ERROR_STOP=1 -U namines -d namines_control`
6. **Sırları geri koy:** secrets arşivini aç, `.env`'i yerine kopyala. `Security__ConnectionEncryptionKey(s)`, `Jwt__Key`, `Vault__BackupEncryptionKey` **kaybolursa şifreli veriler okunamaz** (bkz. bölüm 4).
7. **Migration + başlat:** `... --profile migrate run --rm namines-migrate`, sonra `up -d`; `./scripts/smoke-test.sh`.
8. **Temizlik:** geçici açılmış dizini sil (`rm -rf /tmp/namines-restore-*`), olayı kaydet.

### PITR (nokta-zamanlı kurtarma) — öneri
Günlük `pg_dump` en kötü durumda 24 saatlik veri kaybı (RPO) demektir. RPO'yu dakikalara indirmek için:
- **Yönetilen sağlayıcı** (Neon, RDS, Supabase Pro, Azure): yerleşik PITR'yi açın — en az çaba.
- **Kendi PostgreSQL'iniz:** WAL arşivleme + base backup — `wal-g` veya `pgBackRest` ile (`archive_mode=on`, `archive_command`/`archive_command` = wal-g wal-push, şifreli S3/R2 hedefi), haftalık tam + günlük diferansiyel, düzenli `restore` tatbikatı.
Bu bir **plan maddesidir**; bu depoda henüz yapılandırılmamıştır. `pg_dump` yedekleri PITR kurulduktan sonra da mantıksal ikinci güvence olarak kalmalıdır.

---

## 8. Yavaş sorgu gözlemi (kontrol DB)

`docker-compose.prod.yml` ve `docker-compose.yml` kontrol DB'yi `shared_preload_libraries=pg_stat_statements` ve `log_min_duration_statement` (varsayılan **500 ms**, prod'da `PG_LOG_MIN_DURATION_MS` ile değişir; `-1` kapatır) ile başlatır. Yavaş sorgular container loguna düşer: `docker compose logs namines-control-db | grep "duration:"`.

Extension yeni kurulumlarda `deploy/sql/01-pg-stat-statements.sql` ile otomatik oluşur. **Mevcut** veri dizininde bir kez elle:
```bash
docker compose -f docker-compose.prod.yml up -d namines-control-db   # yeni command ile yeniden oluşur
docker compose exec namines-control-db psql -U namines -d namines_control -c "CREATE EXTENSION IF NOT EXISTS pg_stat_statements;"
```
En yavaş / en çok toplam süre harcayan 10 ifade:
```sql
SELECT round(total_exec_time::numeric, 0) AS total_ms, calls,
       round(mean_exec_time::numeric, 1) AS mean_ms, rows,
       left(query, 120) AS query
FROM pg_stat_statements
ORDER BY total_exec_time DESC
LIMIT 10;
-- Sıfırlamak için: SELECT pg_stat_statements_reset();
```

---

## 9. Bağlantı havuzu, veri saklama ve en az yetkili roller

- **Havuz/zaman aşımı** (`Database__MaxPoolSize` 50, `Database__CommandTimeoutSeconds` 30, `Database__ConnectTimeoutSeconds` 15, `Database__KeepaliveSeconds` 30): bağlantı dizesinde açıkça yazılan değer kazanır. Tüm API instance'larının toplamı PostgreSQL `max_connections` değerini aşmamalıdır (örn. 3 instance × 50 = 150).
- **Veri saklama:** `DataRetentionService` günde bir, 5000 satırlık partilerle çalışır; değişkenler `docs/new-phase/19-ENV-VARS.md`'de. Faturalama olayları (`UsageEvents`) varsayılan 7 yıl, en az 400 gün saklanır.
- **Rol ayrımı (opsiyonel, DB-38):** `deploy/sql/roles.sql` iki rol kurar: `namines_migrator` (şema sahibi, yalnızca `--migrate`) ve `namines_app` (yalnızca DML). `ConnectionStrings__Migrations` tanımlıysa migration adımı onu, çalışma zamanı `DefaultConnection`'ı kullanır; tanımsızsa davranış değişmez. Önerilen geçiş: betiği çalıştır → migration'ı `namines_migrator` ile koştur → `DefaultConnection`'ı `namines_app`'e çevir. **Sonraki adım (planlı):** Postgres Row Level Security ile kiracı izolasyonu — henüz yapılmadı.
- **Anahtar rotasyonu:** `Security__ConnectionEncryptionKeys__N__Id/Key` + `dotnet Namines.API.dll --reencrypt-secrets` (bkz. 19-ENV-VARS).
