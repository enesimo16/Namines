# 09 — Güvenilirlik ve kararlılık denetimi

> ## ✅ DURUM (2026-09-09)
> | Bulgu | Durum |
> |---|---|
> | REL-001 Executor zaman aşımı yok | ✅ `CommandTimeout` + `CancellationToken` |
> | REL-002 DDL transaction yanılsaması | ✅ `PartialApplyPossible` + UI uyarısı |
> | REL-003a Optimistic concurrency | ✅ `xmin` belirteci + 409; **teşhis düzeltildi** |
> | REL-004 Retry politikası | 🟡 Açık — P2 |
> | REL-005 Kaynak limitleri | 🟡 Açık — üretim dağıtımıyla birlikte |

Soru "çalışıyor mu" değil: **"bozulduğunda ne oluyor?"**

---

## Arıza senaryoları — sistem ne yapıyor?

| Senaryo | Davranış | Kanıt / Değerlendirme |
|---|---|---|
| Kullanıcının DB'si erişilemez | Bağlantı 10 sn'de zaman aşımına düşer, hata döner | `GatewayService.ConnectTimeoutSeconds = 10` ✅ |
| Kullanıcının DB'si çok yavaş | Sorgu 15 sn'de kesilir | `GatewayService.QueryTimeout` ✅ |
| Executor'da uzun DDL | 120 sn'de kesilir, istemci iptal edebilir | ✅ REL-001 düzeltildi |
| Control DB düşerse | `/health/ready` kırmızı olur | `AspNetCore.HealthChecks.NpgSql` ✅ |
| İşlenmemiş istisna | Korelasyon ID'li JSON, stack trace sızmaz | `ExceptionMiddleware.cs` ✅ |
| Docker daemon yok (Vault) | `ProbeAsync` açık mesaj döner, UI gösterir | Canlı doğrulandı ✅ |
| Çoklu instance, zamanlanmış yedek | **Tek** yedek alınır | Koşullu `ExecuteUpdateAsync` ile satır kapma; testi var ✅ |
| Yedek şifreleme anahtarı yok | Vault **açıkça durur** | Fail-closed ✅ |
| Geri yükleme başarısız | Ön-yedek alınmış olur, işlem geri alınır | `RestoreAsync` ön-yedek akışı ✅ |
| Yanlış motora geri yükleme | Reddedilir | Bu oturumda canlı denendi ✅ |
| AI sağlayıcısı düşerse | `try/catch` ile null döner, akış devam eder | `ChangeRequestController:341` ✅ |

Bu tablo, projenin **en güçlü tarafını** gösteriyor: arıza yolları düşünülmüş
ve çoğu kanıtlanmış.

---

## REL-001 — Executor'da komut zaman aşımı yok

### Finding
`GatewayService` bağlantı ve sorgu zaman aşımlarını sabit olarak tanımlarken
`DatabaseExecutorService.ExecuteScriptAsync` içinde `CommandTimeout`
ayarlanmıyor.

### Location
`backend/Namines.Infrastructure/Services/DatabaseExecutorService.cs` —
`ExecuteScriptAsync`

### Problem
Varsayılan `CommandTimeout` sürücüye göre 30 sn ile sonsuz arasında değişir
(Npgsql varsayılanı 30 sn, `Microsoft.Data.SqlClient` 30 sn, Oracle sürücüsü
farklı). Büyük bir tabloya `ALTER TABLE` çalıştıran bir betik isteği ve
bağlantı havuzundaki bir yuvayı uzun süre tutabilir.

Ayrıca `ExecuteScriptAsync` **`CancellationToken` almıyor** — istemci vazgeçse
bile iş sunucuda devam eder.

### Risk
Havuz tükenmesi; birkaç uzun betik tüm uygulamayı yavaşlatabilir.

### Severity **MEDIUM** · ### Effort S · ### Priority P1

### Recommendation
`command.CommandTimeout` açıkça ayarlansın ve metot `CancellationToken` alsın.
Depodaki diğer servisler zaten token taşıyor; bu istisna.

### ✅ Yapıldı
- `StatementTimeoutSeconds = 120` sabiti ve `command.CommandTimeout` ataması.
  120 sn, Gateway'in 15 sn'lik sorgu zaman aşımından uzun (büyük tabloda
  `ALTER` dakikalar sürebilir) ama **sınırsız değil**.
- `ExecuteScriptAsync` ve `TestConnectionAsync` artık `CancellationToken`
  alıyor; controller isteğin token'ını geçiriyor.

---

## REL-002 — DDL transaction'ının motor bağımlılığı

Bkz. [DB-007](05-database-audit.md#db-007--işlem-transaction-yönetimi).

Kod betiği tek transaction'da çalıştırıp hata durumunda geri alıyor — ama
MySQL / MariaDB / Oracle'da DDL **implicit commit** yapar. Yani "hepsi ya da
hiçbiri" garantisi PostgreSQL ve MSSQL dışında **yok**, ama arayüz onu veriyor
gibi davranıyor.

### Severity **MEDIUM** · ### Priority P1

### ✅ Yapıldı — garanti verilmiyor, gerçek söyleniyor
`ExecutionResult`'a `PartialApplyPossible` alanı eklendi. Motor DDL'i
transaction dışı işliyorsa (MySQL / MariaDB / Oracle) ve en az bir ifade
çalıştıysa `true` dönüyor; `DbPushModal` bunu kullanıcıya açık bir uyarı
olarak gösteriyor:

> *"this engine commits DDL outside the transaction, so the first N
> statement(s) may already be applied. Check the database before retrying."*

Geri alma çağrısı da "en iyi çaba" olarak işaretlendi — motor zaten commit
etmişse sessizce hiçbir şey yapmıyor ve kod bunu söylüyor.

---

## REL-003 — Eşzamanlılık ve yarış koşulları

| Alan | Koruma | Değerlendirme |
|---|---|---|
| Zamanlanmış yedek (çoklu instance) | Koşullu UPDATE ile satır kapma | ✅ Doğru desen, testli |
| Change request onayı | `(ChangeRequestId, UserId)` unique indeksi | ✅ Çift oy imkânsız |
| Şema sürümleri | `(BranchId, Version)` indeksi | ✅ |
| **Aynı projeye eşzamanlı şema kaydı** | Görülmedi | ⚠️ Optimistic concurrency (`RowVersion`) taraması yapıldı, bulunamadı |

### REL-003a — Optimistic concurrency yok

İki kullanıcı aynı projeyi aynı anda düzenlerse, son yazan kazanır ve ilk
kullanıcının değişikliği **sessizce kaybolur**. Ürün ekip çalışması iddiasında
(Organization, roller, change request) olduğu için bu gerçekçi bir senaryo.

### Severity **MEDIUM** · ### Effort M · ### Priority P2
### Recommendation
`CloudProject` ve `SchemaVersion` üzerinde `[Timestamp] byte[] RowVersion`;
çakışmada 409 ve UI'da "bu proje başkası tarafından değiştirildi" mesajı.

### ✅ Yapıldı (10.09.2026) — B-32

#### Teşhis düzeltildi: "son yazan kazanır" DEĞİL, "ikinci yazan sessizce düşer"

Bulgu "son yazan kazanır ve ilk kullanıcının değişikliği sessizce kaybolur"
diyordu. Kod okunduğunda tablo farklı çıktı: `useProjectHistoryStore.syncAllToCloud`
yüklemeden **önce** bulutu okuyor ve buluttaki sürüm daha yeniyse yüklemeyi
**atlıyor**. Yani üzerine yazma zaten olmuyordu.

Ama bu, sorunu çözmüyor — **aynasını** üretiyor: kullanıcının kendi yerel
değişikliği hiç gönderilmiyor ve **kimse ona bunu söylemiyor**. "Kaydedildi"
sanıp sekmeyi kapatan kullanıcı çalışmasını kaybediyor. İkisi de aynı
sebepten kötü: sessizlik.

#### İki ayrı düzeltme

**1) Sunucu — belirteç ve 409.** `CloudProject.RowVersion`, PostgreSQL'in
`xmin` sistem kolonuna eşlendi. `AuthController.SyncProjects`, istemcinin
gönderdiği sürümü güncel sürümle karşılaştırıyor; eşleşmezse **409** +
hangi proje, ne zaman değiştiği ve iki sürüm numarası.

> **Neden yalnızca `[Timestamp]` eklemek YETMEZ:** EF'in kontrolü, varlığın
> YÜKLENDİĞİ andaki değeri kullanır. Sunucu satırı isteğin başında okuduğu
> için o değer her zaman güncel olur ve `WHERE RowVersion = …` koşulu HER
> ZAMAN eşleşir — hiçbir çakışma yakalanmaz. Koruma, **istemcinin en son
> gördüğü sürümü geri göndermesiyle** oluşur. Kolonu ekleyip bırakmak,
> koruma gibi görünen ama hiçbir şey yapmayan bir değişiklik olurdu.

> **Neden ayrı bir kolon değil:** `xmin` her satırda zaten var ve her
> güncellemede motor tarafından değişiyor. Kendi `byte[]` kolonumuz mevcut
> satırlar için backfill ve her güncellemede elle artırma gerektirirdi —
> ikisi de atlanabilir adımlar, ve atlandığında koruma sessizce yok olurdu.
> Migration bir kolon EKLEMİYOR.

**2) İstemci — sessizlik kaldırıldı.** `syncAllToCloud` artık (a) `rowVersion`
gönderiyor, (b) bulut daha yeni olduğu için yüklemeyi atladığında kullanıcıya
**uyarı gösteriyor**, (c) 409'u ağ hatasından ayırıyor — 409'da "sonra tekrar
denenir" demek yanlış olurdu, tekrar denemek aynı sonucu verir.

#### `SchemaVersion`'a RowVersion EKLENMEDİ — gerekçe

Bulgu onu da öneriyordu. Ölçüldü: `SchemaVersion` **append-only**.
`grep -rn "SchemaVersions.Update"` → 0 sonuç; yalnızca `AddAsync` var
(`BranchController:244`, `ChangeRequestController:156`). Hiç yerinde
güncellenmeyen bir tabloda çakışma belirteci **dekoratiftir**: hiçbir zaman
tetiklenmez, ama okuyan herkese "burada koruma var" der.

#### CANLI DOĞRULANDI (gerçek API + gerçek PostgreSQL)

| Durum | Sonuç |
|---|---|
| `GET /auth/projects` | `rowVersion = 1240` — belirteç gerçek veritabanından dolu geliyor |
| Doğru sürümle `sync` | **200**, kaydedildi |
| Artık eskiyen aynı sürümle `sync` | **409** + `yourVersion: 1240`, `currentVersion: 1269`, `changedAt` |
| Sürüm GÖNDERMEDEN `sync` (eski istemci) | **200** — geriye uyumluluk korunuyor |

#### Migration hakkında beklenmeyen bir sonuç

`dotnet ef migrations add` çıktısı `AddColumn<uint>(name: "xmin", …)` üretti.
İlk okumada bunun **üretimde patlayacağını** düşündüm: `xmin` PostgreSQL'de
ayrılmış bir sistem kolonu adı ve normalde eklenemez.

Uygulanınca **sorunsuz geçti**: Npgsql sağlayıcısı `rowVersion: true` + `xid`
tipi + `xmin` adı birleşimini sistem kolonu olarak tanıyor ve **hiç DDL
üretmiyor**. Migration yalnızca model anlık görüntüsünü kaydediyor.
Doğrulandı: uygulandıktan sonra `GET /auth/projects` çalışmaya devam ediyor
ve belirteç okunuyor.

Bu, `deploy/MIGRATION-VE-GERI-ALMA.md` kontrol listesinin neden "üretilen
dosyayı OKUMADAN commit etme" dediğinin iyi bir örneği — üretilen şey okununca
yanlış göründü, ölçülünce doğru çıktı.

#### Ek olarak yazılan testler

5 integration testi (`ProjectConcurrencyTests`): belirtecin dolu gelmesi, her
güncellemede değişmesi, araya giren yazmanın tespiti, **yanlış alarm
vermemesi** ve migration'ın fiziksel kolon eklememesi.

Bu testler gerçek PostgreSQL'e karşı (Testcontainers) **koştu ve geçti** (5/5). `xmin` PostgreSQL'e özgü olduğu için SQLite ile test edilemezdi.

---

## REL-004 — Yeniden deneme (retry) politikası yok

EF Core'un `EnableRetryOnFailure`'ı yapılandırılmamış. Control DB'ye giden
geçici bir ağ hatası (bulut ortamlarında olağan) isteği doğrudan düşürür.

### Severity **LOW** · ### Effort XS · ### Priority P2
### Recommendation
`UseNpgsql(..., o => o.EnableRetryOnFailure(3))`. Dikkat: bu, kullanıcının
veritabanına giden yollarda **istenmez** (bir DDL'i iki kez çalıştırmak
tehlikeli); yalnızca control DB için.

---

## REL-005 — Kaynak limitleri yok

`docker-compose.yml`'de `deploy.resources` / `mem_limit` yok. Bir Vault yedek
işlemi ya da bir eject üretimi belleği tüketirse konteyner OOM ile ölür ve
yanındaki her şeyi etkiler.

### Severity **LOW** (yerel yığın için) / **MEDIUM** (üretimde)
### Priority P2 — DEVOPS-001 ile birlikte çözülmeli.

---

## Kenar durum analizi

| Durum | Sistem ne yapıyor | Değerlendirme |
|---|---|---|
| Boş yanıt / null değer | C# nullable reference types açık | ✅ |
| Çok uzun string | Model doğrulaması **örneklemede görülmedi** | ⚠️ Doğrulanmadı |
| Milyonlarca kayıt | Gateway sayfalama zorunlu, `pageSize` `Math.Clamp(1, 200)` | ✅ Teyit edildi |
| Geçersiz SQL | Transaction geri alınır, kaçıncı ifadede patladığı söylenir | ✅ İyi hata mesajı |
| Süresi dolmuş jeton | 401 | ✅ |
| **İptal edilmiş jeton** | Kabul edilir | ❌ AUTH-001 |
| Yetkisiz nesne ID'si | 403/404 | ✅ Kontrollü controller'larda |
| Yinelenen istek | Idempotency anahtarı yok | ⚠️ Ödeme dışında düşük risk |

> ⚠️ **Hâlâ doğrulanmadı:** istek gövdesi boyut limitleri ve model doğrulama
> attribute'larının kapsamı. `pageSize` teyit edildi ve **zaten korunuyordu**
> (bkz. [PERF-007](08-performance-audit.md#perf-007--sayfalama-üst-sınırı--yanliş-pozitif)).
> **P2 teyit işi.**
