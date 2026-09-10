# Namines — Durum ve kalan işler

> Bu belge **ne bittiğini ve neyin KANITLANDIĞINI** ayırır. "Yazıldı" ile
> "gerçek bir sisteme karşı çalıştığı görüldü" aynı şey değil; karışması, en
> çok ihtiyaç duyulan anda çalışmayan bir özellik demek.

---

## Kanıt seviyeleri

| Seviye | Anlamı |
|---|---|
| ✅ **Canlı kanıtlandı** | Gerçek bir sisteme karşı çalıştırıldı ve sonucu bağımsız bir araçla (psql, mc, docker) doğrulandı |
| 🟡 **Test edildi** | Otomatik testi var ama gerçek dış sisteme karşı hiç çalışmadı |
| ⚪ **Yazıldı** | Derleniyor, kanıtı yok |
| ❌ **Yok** | Bilinçli olarak kapsam dışı |

---

## Namines Vault (yedekleme)

| Yetenek | Durum | Kanıt |
|---|---|---|
| PostgreSQL yedek/geri yükleme | ✅ | Yedek → veriyi boz → geri yükle → bağımsız `psql` ile satır kontrolü |
| AES-256-GCM şifreleme | ✅ | Tek bayt çevrildi → geri yükleme reddedildi, veri el değmedi |
| Bütünlük doğrulama (V5) | ✅ | Şifresi ÇÖZÜLEN ama geçerli arşiv OLMAYAN dosya üretildi → reddedildi |
| Zamanlanmış yedek (V4) | ✅ | İki API instance aynı anda uyandı → **tek** yedek alındı |
| Saklama politikası | ✅ | `retain=1` ile eski otomatik yedek silindi, elle alınanlara dokunulmadı |
| Nesne depo (S3/MinIO) | ✅ | Gerçek MinIO: yedek nesne olarak yazıldı, ondan geri yüklendi, doğrulandı |
| MySQL yedek/geri yükleme | ✅ | Yedek → veriyi boz → geri yükle → bağımsız `mysql` ile satır, tetikleyici ve yabancı anahtar kontrolü |
| MariaDB yedek/geri yükleme | ✅ | Aynı zincir MariaDB 10.6 sunucusunda tekrarlandı |
| Motorlar arası geri yükleme engeli | ✅ | MySQL yedeği MariaDB bağlantısına geri yüklenmek istendi → reddedildi |
| MSSQL / Oracle yedek | ❌ | **Mimari sınır, eksiklik değil:** `BACKUP DATABASE TO DISK` ve `expdp` dosyayı sunucunun kendi diskine yazar; istemciye akıtılabilen çıktı vermezler. Namines dosyayı kendi tarafına çekemediği için şifreleyemez, nesne depoya koyamaz, geri yüklenebilirliğini kanıtlayamaz. |
| Kısmi (tek tablo) geri yükleme | ❌ | v2 |

**Deploy şartı:** API'nin Docker daemon'una erişimi gerekiyor; bugünkü
`docker-compose.yml` bunu bilinçli olarak vermiyor. Ayrıntı ve seçenekler:
[`namines-vault/02-ERISIM-VE-DEPLOY.md`](namines-vault/02-ERISIM-VE-DEPLOY.md).
`GET /api/vault/health` bu durumu açıkça söylüyor.

---

## Namines Ground (yönetilen veritabanı)

| Yetenek | Durum | Kanıt |
|---|---|---|
| Provizyon (LocalPostgres) | ✅ | Veritabanı + rol oluştu; `datacl`'de PUBLIC **yok**, rol `SUPERUSER` değil |
| İdempotans | ✅ | İkinci provizyon aynı kaydı döndü, sunucuda hâlâ tek veritabanı |
| Silme + geri alma | ✅ | PendingDelete → bağlantı kalktı, veritabanı durdu → geri alma → yeniden bağlanıldı |
| Kalıcı silme (bekleme penceresi) | ✅ | Pencere 0'a çekildi → veritabanı ve rol sunucudan gitti |
| Kullanım ölçümleri | ✅ | Gerçek sayılar (7.7 MB, 1 bağlantı) PostgreSQL'den okundu |
| Plan kotası (sayı) | ✅ | Gerçek free-tier kullanıcı: 1. veritabanı açıldı, 2.'si *"Free plan limit of 1"* ile reddedildi |
| Ground DB'sinin yanlışlıkla kopmasına karşı koruma | ✅ | Ground yönetimindeki projede "bağlantıyı kaldır" reddedildi (bağlantı bozulmadı); Ground dışı projede normal çalışmaya devam etti |
| Free plan barındırma hakkı | ✅ | Free artık 0 değil **1** — Ground kendi Supabase'imiz olma iddiasıyla ücretsiz kullanıcıya da zemin veriyor |
| Boyut uyarısı (Free, 500 MB) | ✅ | Gerçek DB 789 MB'a şişirildi → uyarı geldi (`storageWarning`) → **yazma hâlâ çalışıyor** (kısıtlama yok, yalnızca bilgilendirme) |
| **Neon sağlayıcısı** | ✅ | **2026-09-09 canlı kanıtlandı.** Provizyon → Namines'in kendi şifreli bağlantısıyla yazılan tablo bağımsız `psql`'de göründü (ve tersi) → idempotans → silme → kalıcı silme sonrası Neon'da proje 404'e düştü |
| Copy-on-write branch | ❌ | v2 |
| Çok bölgeli | ❌ | v2 |

---

## Namines Desk

| Yetenek | Durum |
|---|---|
| Veri görüntüleme/düzenleme, SQL konsolu, denetim kaydı, ekip | ✅ |
| Yedekler görünümü | ✅ |
| Barındırma görünümü | ✅ |

Ground ve Vault'un **kendi siteleri yok** — ikisi de arka uç modülü ve
kullanıcıya bakan tek yüzleri Desk içindeki görünümler. Gerekçe:
[`namines-vault/02-ERISIM-VE-DEPLOY.md`](namines-vault/02-ERISIM-VE-DEPLOY.md) §1.

---

## Şema okuma (introspection)

| Motor | Durum | Not |
|---|---|---|
| PostgreSQL | ✅ | Canlı kullanımda |
| MySQL | ✅ | Gerçek MySQL 8'e karşı test edildi (`MySqlIntrospectionTests`) |
| MSSQL | ⚪ | **Bu makinede çalıştırılamıyor** (Docker VM 1.9 GB, motor 2000 MB istiyor) |
| Oracle | ⚪ | İmaj yok, diskte yer yok |

> MySQL testi yazılınca **iki gerçek hata** çıktı ve düzeltildi:
> yabancı anahtar ilişkileri hiç okunmuyordu (Canvas çizgi çizmiyor, DDL
> yeniden üretilirken FK'lar sessizce kayboluyordu) ve `AUTO_INCREMENT`
> görülmüyordu. Aynı ilişki boşluğu MSSQL ve Oracle'da da vardı; sorgular
> yazıldı ama o iki motor canlı doğrulanamadı.

---

## Testler

| Paket | Sonuç |
|---|---|
| `Namines.Tests` | 1480 geçiyor, 3 atlanıyor (13 dk) |
| `Namines.Tests` (entegrasyon) | Gerçek konteynerlere karşı geçiyor; MSSQL'e bağlı olanlar **gerekçesiyle atlanıyor** |
| `Namines.Tests.RunTests` | 15 geçiyor, 2 atlanıyor (MSSQL) |
| Desk (vitest) | 99 geçiyor |

**Atlama ≠ geçme.** Bir testin çevresel bir kısıt yüzünden kırmızı yanması
"testler zaten kırmızı" alışkanlığı yaratır ve gerçek hataları görünmez kılar;
bu yüzden motor barındırılamıyorsa test **gerekçesini yazarak** atlanıyor.
CI'da Docker'a yeterli bellek verilmeli — orada atlanan test bir uyarıdır.

> ⚠️ **Koşu sonucunu okurken atlanan sayısına bakın.** Bir kez şu yaşandı:
> derlemenin hemen ardından koşan `docker info` yüklü makinede zaman aşımına
> uğradı, prob "Docker yok" dedi ve **127 integration testi sessizce atlandı** —
> suite yine "Başarılı!" yazdı. Yeşil ama hiçbir şey kanıtlamayan bir koşu,
> kırmızı bir koşudan daha tehlikeli. Prob artık 30 saniye bekliyor ve bir kez
> yeniden deniyor, ama sayıya bakma alışkanlığı yerini tutmaz.

---

## Güvenlik turu KAPANDI (10.09.2026)

Denetim sonrası güvenlik listesinin **kod tarafı bitti**. Ayrıntı ve gerekçeler:
[`docs/audit/29-IYILESTIRME-PLANI-2026-09-10.md`](docs/audit/29-IYILESTIRME-PLANI-2026-09-10.md)

| İş | Durum |
|---|---|
| HIBP sızdırılmış parola kontrolü | ✅ `Password123456` → 42.513 sızıntı → red |
| MFA (TOTP + 8 kurtarma kodu) + arayüz | ✅ 12 adım canlı doğrulandı |
| Gateway yetki koruması | ✅ Konvansiyon testi — yetkisiz uç eklenince build kırılıyor |
| SSRF egress allowlist | ✅ `Security:DbEgress:AllowedHosts`, canlı doğrulandı |
| `DockerController.StreamLogs` | ✅ `[Authorize]` + cookie; bir jobId oracle'ı da kapandı |
| Executor'ın SSRF bayrağı | ✅ Tek kapılıydı → ortak çift kapılı politikaya bağlandı |
| SOC 2 / HIPAA | ⬜ Kod işi değil |

**Bu turda canlı denemeyle yakalanan 4 gerçek hata:**

1. **HIBP kontrolü tamamen ölüydü.** 7 birim testi geçiyordu, derleme temizdi
   ve her istek `"BaseAddress must be set"` ile fail-open'a düşüp **her
   parolayı kabul ediyordu**. `AddPasswordValidator<T>`, `AddHttpClient<T>`'nin
   tipli fabrikasını kullanmıyor.
2. **Kurtarma kodları çalışmıyordu.** Tire siliniyordu; Identity kodları
   `xxxxx-xxxxx` saklıyor. Telefonunu kaybeden kullanıcı hesabına **hiç**
   giremiyordu.
3. **Testler aslında koşmamıştı.** `dotnet build` iki kez sessizce başarısız
   oldu (koşan test paketi DLL'i kilitliyordu) ve eski ikili koştu.
4. **Executor SSRF'te en zayıf kapıyı kullanıyordu.** Kendi
   `Executor:AllowPrivateHosts` bayrağı yalnızca config'e bakıyordu; aynı
   kararın diğer tarafı ise ortam + config çift kapısı istiyordu.

**Doğrulanmayan:** MFA arayüzünün kimlik doğrulanmış dalı tarayıcıda
tıklanmadı (B-64).

---

## Kapsamlı denetim ve düzeltme turu (2026-09-09)

Projenin tamamı denetlendi — mimari, backend, frontend, veritabanı, güvenlik,
kimlik/yetki, performans, güvenilirlik, DevOps, test, UI/UX, erişilebilirlik,
ürün ve teknik borç. 28 rapor: [`docs/audit/`](docs/audit/).

**Sonuç:** 54 bulgu. Kod kalitesi 72/100, üretime hazırlık 64/100, karar
**ALMOST** — mimari sağlam, engel altı noktasal açıktı.

Ardından düzeltmeler uygulandı:

| | Denetimde | Şimdi |
|---|---|---|
| Açık P0 | 6 | **0** |
| Açık P1 | 19 | 8 |
| Backend testi | 1.480 | **1.505** |
| Güvenlik puanı | 62 | **84** |
| Üretime hazırlık | 64 | **79** |

**Kapatılan kritik açıklar:**
- JWT fallback anahtarı yalnızca `IsProduction()` ile kapalıydı — `Staging`
  ya da unutulmuş bir ortam adında depoda yazılı anahtarla imzalanıyordu.
- Mermaid `securityLevel: 'loose'` + `dangerouslySetInnerHTML` → paylaşılan
  şemada saklı XSS.
- "Personal Access Token" özelliği **sahteydi**: tarayıcıda `Math.random()`,
  sunucuya hiç gitmiyor, "revoke" hiçbir şeyi iptal etmiyordu.
- `/api/executor/execute` keyfi SQL'i denetimsiz çalıştırıyordu — ürünün kendi
  ChangeRequest yönetişim zincirinin yanından geçerek. Artık `SqlExecutionAudit`.
- Cross-site cookie dağıtımında CSRF koruması yoktu → `CsrfProtectionMiddleware`.
- Üretim dağıtım tanımı depoda yoktu → `docker-compose.prod.yml`.

**Ayrıca:** jeton iptali (`POST /api/auth/revoke-all-sessions`), hesap
kilitleme, 12 karakter parola, `Billing` rol tuzağı, executor zaman aşımı,
DDL kısmi-uygulama uyarısı, `[JsonIgnore]`, CI'da bağımlılık taraması ve
atlanan test eşiği.

**Denetimin kendi yanlış pozitifleri (dürüstlük kaydı):** iki bulgu geri
çekildi — `pageSize` tavanı zaten vardı, Desk gezinmesi zaten "Yedekler" /
"Barındırma" diyordu. Bir yeni bulgu çıktı: `npm run lint` CI'da hiç koşmuyor
ve 129 hata birikmiş (FE-008).

**STOP SHIP listesi TEMİZ.** Son madde de kapandı:
[`deploy/URETIM-CALISTIRMA.md`](deploy/URETIM-CALISTIRMA.md) şifreleme
anahtarlarının nasıl saklanacağını, rotation'ın nasıl yapılacağını (eski
anahtarı SAKLAYARAK) ve `Vault:BackupEncryptionKey` kaybolursa geçmiş yedeklerin
tamamının kalıcı olarak okunamaz hâle geleceğini yazıyor.

**Rakip verisi toplandı.** İlk denetimde web araçları çalışmadığı için boş
bırakılmıştı; resmî fiyat sayfaları tarayıcıyla okundu:

| Rakip | Ücretsiz | İlk ücretli |
|---|---|---|
| **Bytebase** (baş rakip) | 20 kullanıcı, 10 instance | $20/kullanıcı/ay |
| **Atlas** | Sınırlı | $9/geliştirici/ay |
| **Supabase** | 500 MB, 2 proje | $25/ay |

**En net ticari fark, doğrulandı:** Bytebase onay iş akışını ve risk
değerlendirmesini **Enterprise'a** (fiyat sorulacak) saklıyor; Namines ikisini
de temel üründe veriyor.

**Geri çekilen iddia:** MCP sunucusu farklılaştırıcı değil — Bytebase onu
ücretsiz katmanda veriyor.

**Acı gerçek:** Sorgu geçmişi, kaydedilmiş sorgular ve veri dışa aktarma,
üçü de Bytebase'in ücretsiz katmanında; Namines'te hiçbiri yok. Bunlar eksik
özellik değil temel beklenti — öncelikleri yükseltildi.

**Denetim sırasında bulunan bir sürpriz:** `.gitignore` bütün `*.md`
dosyalarını yok sayıyor ve bir allowlist tutuyordu. 28 denetim raporu ve
üretim kılavuzu yazıldı; `git status` **hiçbirini göstermedi**. Yalnızca
`git check-ignore` ile fark edildi. `docs/` ve `deploy/` açıldı, tuzak
`.gitignore`'a not düşüldü (DOC-005).

---

## Nerede kaldık (son oturum)

**Bitti:** Vault artık **çok motorlu**. Tek sağlayıcı yerine motor başına bir
sağlayıcı kayıtlı; `GET /api/vault/health` desteklenen motorları **liste**
olarak dönüyor ve Desk bunu ekranda gösteriyor.

MySQL ve MariaDB **canlı doğrulandı** — ikisinde de zincirin tamamı:
yedek → veriyi boz → geri yükle → **bağımsız** bir `mysql` oturumunda satır,
tetikleyici ve yabancı anahtar kontrolü. Motorlar arası geri yükleme denendi ve
reddedildi. Doğrulama akışı (temiz sunucuya gerçek restore) ikisinde de geçti.
Kullanılan deneme veritabanları, projeler, yedekler ve konteynerler silindi.

Yol boyunca çıkan gerçek hata: **MariaDB istemcisi `--column-statistics`
bayrağını tanımıyor**, ilk yedek denemesi düştü. Bayrak artık istemciye göre
veriliyor ve regresyonun testi yazıldı.

**Sıradaki iş — Vault:**

| # | Konu | Not |
|---|---|---|
| 1 | Yedekleme sırasında ilerleme göstergesi | İstek şu an yedek bitene kadar açık kalıyor; büyük veritabanında arka plan işine taşınmalı |
| 2 | Kısmi (tek tablo) geri yükleme | v2 kapsamı |
| 3 | Geri yükleme sonrası otomatik doğrulama | Bugün elle tetikleniyor (`POST .../verify`) |

**Sıradaki iş — Ground:** Free plan için kendi barındırdığımız 1 veritabanı ve
boyut **uyarısı** duruyor (kısıtlama YOK — kararı böyle verildi). Neon ek bir
seçenek olarak canlı doğrulandı. Supabase'e taşıma henüz açılmadı.

---

## Senden bekleyenler

| # | Konu | Neden gerekli |
|---|---|---|
| 1 | Docker Desktop belleği (≥ 3 GB) | MSSQL'e bağlı testler ve "Run Tests"in MSSQL yolu bu makinede hiç çalışamıyor |
| 2 | Stripe fiyat kimlikleri | Ödeme akışı yapılandırılmamış |
| 3 | Üretimde nesne depo bilgileri | `Vault__S3__*` — yoksa yedekler sunucu diskinde kalır |
| 4 | Neon organizasyonu — genişleme planlanırsa | Test hesabı bir **organizasyon** anahtarı; başka bir Neon hesabına geçilirse `Ground__Neon__OrgId`'nin de güncellenmesi gerekir (kişisel hesap anahtarında bu alan boş bırakılmalı) |
