# Faz 14.2C Raporu: CSV ve Koleksiyon Normalizasyonu

> Dal: `arch/phase-14-2`. Temel: `322d416`. Ortam: Windows 11, .NET 8 SDK, Docker,
> `postgres:17-alpine` (Testcontainers), `postgres:16` (`namines-verify`: duman, EXPLAIN, katalog).

## 1. Kabul tablosu

| Alan | Sonuç | Kanıt |
|---|---|---|
| Build | PASS | `dotnet build backend/Namines.sln -c Release --no-incremental`: 0 uyarı, 0 hata |
| Unit | PASS | `GatewayAllowlistParityTests` (eski eşleşmeye karşı tablo), `GatewayKeyRestrictionTests`; sayılar §8 |
| Integration | PASS | Gerçek PostgreSQL: `Phase14_2CMigrationTests`, `Phase14_2BMigrationTests`, `MigrationHygieneTests`, `Namines.Tests.RunTests`; sayılar §8 |
| Architecture | PASS | `Phase14_2CNormalizationArchitectureTests`, `ModularMonolithArchitectureTests`; §8 |
| Migration | PASS | Boş DB, bir önceki migration'da dağınık veri, Down + Up, üretim yolu `--migrate`, hacimli veri (§3, §7) |
| Data integrity | PASS | Migration içi `RAISE EXCEPTION` (eksik/fazla/yinelenen/yetim/alan/sıra/bayrak/karantina), 23 Up + 6 Down bozma senaryosu, doldurma hatasını yakalama testi; SQL/C# fuzz 30.000 + 30.000 girdi 0 fark |
| Security | PASS, tek bilinçli istisna §5 (L-1) | §5: ilk denetim bir genişleme buldu (B-1), kapatıldı, fuzz ve yeni doğrulama koruyor. Boşalan liste reddeder, maske list/detail/export/expand'de gerçek PG ile sınandı. "Hiç genişlemez" tablodaki ve fuzz'daki girdiler için ölçüldü, matematiksel kanıt değildir |
| API compatibility | PASS, farklar §4 tablosunda | Alan adları ve tipleri aynı (metin); gerçek API'ye karşı 53/53 duman; frontend: `tsc` 0 hata, vitest 39 dosya 280/280 |
| Documentation | PASS | Bu rapor, `IMPLEMENTATION_STATUS.md`, `DATABASE_NORMALIZATION.md`, `MODULE_OWNERSHIP.md`, `docs/deploy/MIGRATION-VE-GERI-ALMA.md` §5.2 |
| Performans | PASS | §7: etiket filtresi 2-33 ms, anahtar doğrulama tek sorguda 0,17 ms (200 bin issue, 20 bin anahtar) |
| Independent audit | ACCEPTED (ikinci tur, `cb4d3a4`; backlog BL-C1..C8 faza atandı) | `PHASE_14_2C_INDEPENDENT_AUDIT.md` |

## 2. Veri modeli (gerçekte oluşan)

| Tablo | Kolonlar | Anahtar / kısıt |
|---|---|---|
| `gateway.api_key_allowed_origins` | Id, ApiKeyId, Origin varchar(512), Position | UNIQUE (ApiKeyId, Origin); CHECK kalıp; FK `public."GatewayApiKeys"` CASCADE |
| `gateway.api_key_allowed_ips` | Id, ApiKeyId, Cidr varchar(64), Position | UNIQUE (ApiKeyId, Cidr); CHECK `/` var ve `"Cidr"::cidr` (host bitli ya da bozuk değer yazılamaz); FK CASCADE |
| `gateway.table_masked_columns` | Id, TablePermissionId, ColumnName varchar(512), Position | UNIQUE (TablePermissionId, ColumnName); CHECK boş değil; FK `public."GatewayTablePermissions"` CASCADE |
| `hub.issue_labels` | Id, ProjectId, Name, NormalizedName | UNIQUE (ProjectId, NormalizedName); FK `studio.projects` CASCADE |
| `hub.repo_issue_labels` | RepoIssueId, IssueLabelId, DisplayName, Position | PK (RepoIssueId, IssueLabelId); IX (IssueLabelId, RepoIssueId); iki FK CASCADE; `trg_repo_issue_labels_gc` |
| `public."GatewayApiKeys"` (yeni kolonlar) | RestrictOrigins, RestrictIps boolean NOT NULL | Kısıt var mı, satır yokluğundan ayrı (§5) |

Üst tablolar `public`'te kaldı: FK tablo kimliğini izler, D-1'in `SET SCHEMA`'sı bu tabloları bozmaz.
`GatewayDbContext` üst tabloları da bugünkü gerçek yerlerine (`public`) eşler ve hepsini
`ExcludeFromMigrations` ile tutar; önceden var olmayan `gateway."GatewayApiKeys"`'e eşliydi (kimse sorgulamıyordu). Her
eşlenen kolonun DB'de var olması test edildi. Issue etiketleri yalnızca `AuthDbContext`'te: `RepoIssues` Hub bağlamına
D-1'de geçecek.

### Spesifikasyondan bilinçli sapmalar

| Konu | Spesifikasyon | Yapılan | Neden |
|---|---|---|---|
| `hub.issue_labels` kapsamı | Global (Id, Name, NormalizedName unique) | Proje kapsamlı, UNIQUE (ProjectId, NormalizedName) | Global satırda bir kiracının yazımı ve etiket metni diğerine karışır, hesap silindiğinde metin geride kalır, temizlikte kiracılar arası yarış olur. Filtre zaten proje içinde. |
| Join satırında `DisplayName`, `Position` | Yok | Var | Kayıpsızlık: "Bug" ve "bug" yazan iki issue aynı etikete bağlanıyor ama API yazımı ve sırayı eskisi gibi döndürüyor (14.2B `DisplayTag` deseni). |
| Etiket yanıtı | Görevde "label array" denmiş | Virgüllü metin korundu | Kodda alan zaten metin: `frontend/types/hub.ts` `labels: string \| null`, `HubIssuesTab` `split(',')`. Dizi yapmak sözleşmeyi bozardı. |
| `Restrict*` kolonları | Yok | Var | §5, kapalıya düşmenin tek güvenilir yolu. |

## 3. Migration

`20261105114438_Phase14_2C_NormalizeCollections`, `AuthDbContext` zincirinde (`Database.Migrate()` / `--migrate`). SQL
`Phase14_2C_NormalizationScripts`'te; tek işlem, üç üst tabloda ACCESS EXCLUSIVE:

1. **Genişlet:** bayrak kolonları, 5 tablo, indeksler, tetikleyici.
2. **Doldur:** C# `Split(',', RemoveEmpty | Trim)` ile aynı ayrıştırma. Boşluk kümesi `string.Trim()` ile karakter karakter aynı
   (`\s` yerele bağlı olduğu için kullanılmadı). NOT VALID CHECK'ler UPDATE'te yeniden denetlendiği için aynı işlemde
   kaldırılıp aynen geri eklenir (`RateLimitPerMinute=0`, 20.001 karakterli `Body` testte var).
3. **Doğrula (`RAISE EXCEPTION`):** ham metin yeniden ayrılır. IP ve origin için doğrulamanın **kendi** ayrıştırıcıları var
   (`namines_2c_check_ip`, `_check_origin`: elle, `inet` kullanmaz; ilk sürümde doğrulama doldurmayla aynı fonksiyonu paylaşıyordu ve
   B-1'i göremedi). Kırpma (`namines_2c_trim`) ve etiket anahtarı (`namines_2c_key`) ise iki tarafta **ortak**, onları yalnızca
   C#'a karşı testler sınar. Kontroller: eksik, fazla, yinelenen, yetim, bayrak,
   kesintisiz Position, kanoniklik, karantina tamlığı. Maskede "maskesi olup satırı kalmayan izin". Etikette yazım dâhil
   alan karşılaştırması, proje dışı bağ, kullanılmayan katalog satırı.
4. **Daralt:** dört kolon fiziksel DROP.

**Uç durumlar (hepsi testte):**

| Girdi | Sonuç |
|---|---|
| NULL, `""`, boşluk | Satır yok, kısıt yok (eskisi gibi) |
| `",,"`, `" , ,"` | Satır yok, **kısıt yok** (eski kod da serbest bırakıyordu; davranış korundu) + `blank_list_means_no_restriction` karantina izi: sahip kısıtladığını sanıyor olabilir. Yalnızca boşluk iz bırakmaz (L-5) |
| Yinelenen, büyük/küçük harf farklı origin | Tek satır, ilk sıra, küçük harf |
| `10.1.2.3/8` ve `10.0.0.0/8` | Tek satır `10.0.0.0/8` |
| `::ffff:1.2.3.4` | `1.2.3.4/32` |
| `*`, `https://*.x.com`, `https://a.com/`, yol, `null`, kullanıcı bilgisi, ASCII dışı, port dışı | Karantina (`wildcard_not_supported`, `null_origin_not_supported`, `invalid_origin`, `too_long`) |
| `not-an-ip`, `1.2`, `0x7f.0.0.1`, `010.0.0.1`, `[::1]`, `fe80::1%3`, `10.0.0.0/+8`, `/08`, `::ffff:x/n` | Karantina (`invalid_ip`). .NET bunların bir kısmını kabul ediyordu; reddetmek yalnızca daraltır |
| Karışık (`https://ok.com,*`) | Geçerli yaşar, geçersiz karantinaya, kısıt devam |
| Yalnız geçersiz | Satır yok, `Restrict*=true`, **her istek reddedilir** |
| Maske `Email,email` | İki satır: PostgreSQL'de iki ayrı kolon olabilir, birleştirmek sızdırır |
| Maske > 512 karakter | Karantina (`column_name_too_long`): hiçbir motorda kolon adı olamaz, indekslenemez. Diğer girdiler kalır, Down geri koyar |
| Etiket `Bug, bug ,BUG` | Tek bağ, ilk yazım; Türkçe `İ` ve Yunanca Σ C# `ToLowerInvariant` ile aynı |
| Yetim | FK nedeniyle kaynakta olamaz; doğrulama yine sayar, negatif testi var |

**Gömülü IPv4 (denetim B-1, kapatıldı):** PostgreSQL `inet` `::ffff:1.2.3` ve `::.2.3.4/0` kabul ediyor, .NET etmiyordu; eski kodda hiç
eşleşmeyen kural migration sonrası çalışıyordu (`::/0` = tüm IPv6). Cast'ten önce noktalı kuyruk için tam dört oktet şartı eklendi, C#
aynı kuralı uygular (`::ffff:1.2.3.07` gibi baştaki sıfırı .NET kabul ediyordu, artık iki taraf da reddeder, yönü daraltma). Bu değerler
parite tablosunda; `Dogrulama_doldurma_fonksiyonunun_hatasini_gorebiliyor` korumayı söküp doğrulamanın patladığını gösterir.
`Sql_ve_csharp_ayristiricilari_fuzz_ile_ayni`: sabit tohum, 30.000 IP + 30.000 origin; doldurma, doğrulama ve C# aynı sonucu vermek
zorunda. Fuzz kendi yazdığım doğrulama ayrıştırıcısında da bir hata yakaladı (`https://[::1]:` kabulü), düzeltildi.

**Kilit bekleme:** `SET LOCAL lock_timeout = '30s'` (Up ve Down): açık kalmış bir işlem varsa migration `55P03` ile düşer ve yeniden
denenir, kuyrukta bekleyip her okumayı tıkamaz.

**Karantina:** yeni tablo açılmadı, 14.2B öncesinden var olan `migration_quarantine.orphaned_rows` kullanıldı
(`source_table` = `public.GatewayApiKeys.AllowedOrigins` vb., `row_data` = key_id, project_id, entry, ord, raw).

**Dağıtım sırası:** tek adımlı olduğu için eski kod yeni şemada ÇALIŞMAZ, fail-closed'dır (sorgu hatası; maskesiz okuma ya da
serbest allowlist değil). Eski sürümü durdur, yedek al, `--migrate`, yeni sürümü aç
(`docs/deploy/MIGRATION-VE-GERI-ALMA.md` §5.2). `MigrationCompatibilityTests.DeliberateExceptions`'ta gerekçeli.
İşlem tek olduğu için doldurulmuş ama maskesi kaybolmuş ara durum yok.

**Bulunan üretim sorunu:** `--migrate` 30 saniyelik sorgu zaman aşımıyla çalışıyordu; 200 bin issue'luk veride migration
30.040 ms'de düştü (işlem geri alındı, veri sağlam, yükseltme bitmez). `Database:MigrationCommandTimeoutSeconds`
(varsayılan 1800) eklendi. `deploy/sql/roles.sql`'de `gateway` şeması yoktu: eklendi ve migration eski kurulumlar için
`namines_app`'e yetki verir; en az yetkili rol testi yeni 5 tabloyu da sınıyor.

**Geri alma (`Down`, testli):** komut `docs/deploy/MIGRATION-VE-GERI-ALMA.md` §5.2; `dotnet ef` uygulamanın zaman aşımı ayarını okumaz, bağlantı dizesinde
`Command Timeout=1800` gerekir. Ölçüm: 200 bin issue, 20 bin anahtarın hepsi geçersiz, 80 bin karantina satırı: 10,9 sn. Denetim bu komutu 30 sn sınırında düşürmüştü; kendi ölçümümde asıl neden karantina aramasının karesel olmasıydı (izin başına jsonb tarama, 2 dakikada bitmedi), geçici tablolarla düzeltildi. Kolonlar ve veri döner, doğrulanır, tablolar silinir, karantina satırları (anahtarı
yaşayanlar) kalkar; Up yeniden üretir. Up1 = Up2 dökümü (origin, IP, maske, etiket, bayrak, karantina).
- Eski kod boş olmayan listeyi uyguladığı için geçersiz girdiler ham metinle geri döner, fail-closed korunur. Anahtar
  listeleri oluştuktan sonra değişmez (güncelleme ucu yok), bu yüzden ham metin hâlâ doğrudur.
- Karantina kaydı yok ama bayrak açıksa `invalid:deny-all` / `invalid` yazılır: boş liste eski kodda serbest demektir.
- **Kayıplar:** temiz listeler kanonik döner (`"HTTPS://A.com , b"` değil `"https://a.com"`; karar aynı, test tablo ile sınar).
  Etikette yalnızca boşluk biçimi ve yinelenenler. Boş `gateway` şeması kalır (varsayılan yetkiler düşmesin).

## 4. API farkları

Rotalar, alan adları ve tipler aynı. Yeni alan eklenmedi. Davranış farkları:

| Uç | Önce | Şimdi | Neden |
|---|---|---|---|
| `POST /api/gateway/keys/{id}` geçersiz origin/IP | 200, sonsuza dek eşleşmeyen kural | 400 `{error}`, anahtar üretilmez | Spesifikasyon: geçersiz reddedilir |
| Aynı uç, yalnızca virgül | 200, kısıtsız anahtar | 400 | Yazan kısıtladığını sanıyordu |
| Aynı uç, > 64 girdi | Sınırsız | 400 | Anahtar doğrulama sorgusu listeleri tek sorguda birleştirir |
| `allowedOrigins`/`allowedIps` yanıtı | Ham metin (`"a, b"`) | Kanonik (`"a,b"`, küçük harf, `10.1.2.3/8` → `10.0.0.0/8`, `1.2.3.4/32` → `1.2.3.4`) | Tek kaynak tablo |
| Geçerli girdisi kalmamış anahtar | Ham metin | `"(no valid entries: all requests are denied)"` | Null dönse arayüz "kısıt yok" gösterirdi; `GatewayKeyPanel` doluluğa bakıyor |
| `maskedColumns` yanıtı | Ham metin | `"a,b"`, boşsa null | Tek kaynak tablo |
| `PUT .../tables` > 512 karakter maske | Kabul | 400 | Kolon adı olamaz |
| Eşzamanlı aynı `PUT .../tables` | Son yazan kazanır | Biri UNIQUE'e takılıp 409 alabilir (L-3) | Güvenli: işlem geri alınır, maske durur. Yeniden deneme eklenmedi |
| `labels` yanıtı | Ham metin, `""` olabilir | `"Bug,perf"`, boşsa null | Frontend `issue.labels &&` ve `trim()` kullanıyor, etkilenmez |
| `GET .../issues?label=x` | **Alt dizge, harf duyarlı** (`perf`, `performance`'ı buluyordu) | **Tam eşleşme, harf duyarsız** | "CSV sorgulama kalmayacak"; alt dizge eşleşmesi hataydı. 14.2B tag filtresiyle aynı karar |

## 5. Güvenlik

**Mevcut Gateway semantiği (kodda doğrulandı, belge değil):** liste boşsa serbest; doluysa `OrdinalIgnoreCase` tam dizge;
`Origin` başlığı yoksa ret; **joker yok** (`*` yalnızca harfi harfine `*` başlığıyla eşleşiyordu); IP'de güvenilen proxy yoksa
ret, `/0` geçerli, ayrıştırılamayan kural eşleşmez, IPv4-eşlenmiş adres v4'e iner. Bilinçli tek fark aşağıda, gerisini parite tablosu sınar.

**Kapalıya düşme (bu fazın asıl riski):** "satır yok" iki anlama gelebilirdi: kısıt hiç yok, ya da her girdi geçersizdi. Bayrak
ikisini ayırır ve karar iki yönde de kapalıya düşer: `Restrict* || satır var` ise kısıt uygulanır, satır yoksa reddedilir. Yani:
- Tüm girdileri geçersiz liste reddeder (birim + gerçek PG + gerçek HTTP).
- Navigasyonu yüklemeyi unutan yeni kod yolu: bayrak açık, liste boş görünür, reddedilir (test).
- Elle yazılmış satır, bayrak unutulmuş: yine uygulanır (test).

**Genişlememe kanıtı:** eski eşleşmenin donmuş kopyası (`LegacyGatewayMatching`) kesme sonrası da testte durur. 31 origin
listesi x 26 deneme, 43 IP listesi x 23 deneme x güvenilir/güvenilmez: girdileri geçerliyse birebir aynı, değilse "yeni izin
veriyorsa eski de veriyordu". Aynı tablo migration'dan geçen gerçek satırlarla da koşar.

**Bilinçli fark (L-1, kapsam kimliği):** düz kural `fe80::1`, istemci `fe80::1%3`. Eski kod `IPAddress.Equals` ile kapsamı da karşılaştırıyordu (ret), eski
kodun CIDR yolu karşılaştırmıyordu (`fe80::1/128` izin). Şimdi her kural CIDR, yani izin. Karar: **kod değişmedi.** Aynı bağlantı-yerel adres; kural
kapsam taşıyamaz; `RemoteIpAddress` kapsamı yalnızca bağlantı-yerel doğrudan bağlantıda taşır. Eski davranış da tutarlı değildi. Test belgeliyor.

**Yeni kurallar:** sondaki `/` silinmez, varsayılan port düşürülmez: eski kod tarayıcının gönderdiği biçimle
eşleşmiyordu, temizlemek kapıyı açardı. Daralan tek durum: .NET'in gevşek kabul ettiği IP yazımları ve `null`/joker origin
literal başlıkları (karantinada görünür). `null` origin kasıtlı olarak reddedilir: korumalı her iframe onu gönderebilir.

**CORS:** `Program.cs`'teki CORS politikası anahtar listelerine bakmaz, kısıt kodu CORS'a bakmaz (mimari test).

**Maskeleme:** karar kodu (`MaskValues`, `OrdinalIgnoreCase` küme) değişmedi; yalnızca listenin kaynağı. `GatewayMaskingEndpointTests` gerçek PG'de
`list`, `detail`, `export` (csv, json) ve `expand` uçlarını sınar, maskesiz tablo karşı kontrolüyle. Atılan tek şey bayt bayt
aynı yineleme ve 512 karakteri aşan metin. Tablo okunamazsa sorgu hatası (500), boş liste değil (test). Doldurma tek işlemde,
PUT aynı `SaveChanges`'te fark yazar: maskesiz ara durum yok. Duman: maskeli okuma, güncelleme, temizleme gerçek veride.

**Kiracı ve önbellek:** Gateway'de izin ya da anahtar önbelleği **yok** (`IMemoryCache` kullanan yerler arasında değil, her istek
DB'ye gider), bu yüzden önbellek anahtarı riski doğmadı. Etiket sorguları proje koşuluyla. Join'de `label.ProjectId =
issue.ProjectId` veritabanı kısıtı değil: tek yazma yolu `RepoIssueLabelStore` projeyle arar, migration doğrular.

**Diğer:** hata mesajı reddedilen istemci adresini yankılamaz (test var). 400 mesajı yalnızca isteği yapanın kendi girdisini
içerir. Duman sonrası API logunda `ada@secret.example` ve `Password=verify` 0 kez geçti. Ham SQL enjeksiyon yüzeyi eklenmedi.
`RepoIssue`/anahtar varlıkları doğrudan serileştirilmiyor; yeni navigasyonlar yine de `[JsonIgnore]`.

**Yaşam döngüsü:** anahtar silinirse çocuklar CASCADE. İptal satırı silmez. Proje ve hesap silme (`ProjectDeletionService`,
`AccountDeletionService`) FK ile temizlenir, ek kod gerekmedi, test: proje silince çocuk kalmıyor, başka kiracı etkilenmiyor.
Etiket: ilk kullanımda doğar; issue silinince bağ gider; son bağ giderse `trg_repo_issue_labels_gc` katalog satırını siler
(issue silme ucu yok, FK ile gelen her yol geçer). Tetikleyici satırı önce kilitler: kilitsizken eşzamanlı eklenen commit'li bağı CASCADE ile siliyordu (denetim M-1, testli). Eşzamanlı aynı yeni etiket: UNIQUE reddeder, `CreateIssue` yeniden dener (test).
`DataRetentionService` yalnızca denetim kayıtlarını siler (`GatewayAuditEntries`), bu tablolar yaşayan yapılandırma ve kapsamında değil.

## 6. Repo geneli tarama ve karar tablosu

Yöntem: katalogdan tüm text/jsonb/varchar kolonları, `Split(',')`, `string.Join`, `Contains/Like` EF sorguları, `Id` ile biten FK'siz
metin kolonları. Üretim koduna `grep`, gerçek şemaya sorgu.

| Alan | Karar | Gerekçe |
|---|---|---|
| `RepoIssues.Labels` | **Normalize edildi** | Sorgulanan gerçek ilişki |
| `GatewayApiKeys.AllowedOrigins`, `AllowedIps` | **Normalize edildi** | Güvenlik kararı veriyor, her istekte okunuyor, doğrulanmalı |
| `GatewayTablePermissions.MaskedColumns` | **Normalize edildi** | Permission'a bağlı küme, dedupe ve bütünlük gerekli |
| `CloudProjects.Tags` | **14.2B yaptı, doğrulandı** | Kolon yok, tek sistem `hub.project_tags`; ikinci etiket sistemi yok (mimari test) |
| `GatewayAuditEntries.Columns` | **Denetim anlık görüntüsü, CSV kalır** | O anda dokunulan kolonlar; değişmez, ilişkisel sorgulanmıyor, kolon silinse de kayıt kalmalı |
| `GatewayAuditEntries.ActorUserId`, `ApiKeyId`, `ProjectId`; `SqlExecutionAudits.UserId`, `ProjectId`; `ChangeRequestAuditLogs.ChangeRequestId`, `ProjectId` | **Denetim anlık görüntüsü, FK'siz kalır** | Denetim kaydı silinen varlıktan uzun yaşamalı |
| `ChangeRequestAuditLogs.Details`, `SqlExecutionAudits.ScriptPreview`, `AutomationRunLog.ResultSummary`, `UsageEvents.Context` | **Denetim/serbest metin** | Liste değil |
| `SchemaJson`, `NodePositionsJson`, `SchemaVersions.SchemaJson` | **Toplu belge, JSON kalır** | Tek birim yazılır, sürümlenir, SQL'de içine sorgu atılmıyor |
| `ChangeRequests.ImpactReportJson`, `MigrationResultJson` | **Toplu belge** | Üretilmiş rapor, anlık görüntü |
| `automation_rules.ConditionsJson`, `ActionConfigJson`, `automation_actions.ActionConfigJson` | **Toplu belge** | Bağımsız varlık değil, kuralın parçası; tümü yüklenip `AutomationConditionEvaluator` ile değerlendiriliyor |
| `UserAIPolicies.AdvancedJson`, `AspNetUsers.ProfileJson` | **Küçük ayar belgesi** | Kullanıcıya özel, sorgulanmıyor |
| `CrossDatabaseRelations.Source/Target{Table,Column}Id`, `automation_rules.ScopeTableId` | **Belgeye referans, FK olamaz** | `SchemaJson` içindeki kimlikler, tablo değil |
| `RepoIssues.RelatedTableName`, `NslRuleCode` | **Kalır** | Tek değer, belgeye referans |
| `hub.repositories.OwnerUserId` | **Okuma modeli kopyası (14.2B)** | Modüller arası join yok; kopyanın yazımı testli |
| `vault/ground.OrganizationId` | **Modül sınırı** | 14.2B'de bilinçli FK'siz |
| `ground.ProviderProjectId`, `ProviderBranchId`, `AspNetUsers.StripeCustomerId`, `StripeSubscriptionId`, `StripeWebhookEvents.StripeCustomerId`, `PendingSubscriptionCancellations.SubscriptionId` | **Dış sistem kimliği, FK olamaz** | Başvurulan satır bu veritabanında değil; Stripe alanları D-2'de taşınır |
| `Security:McpAllowedDbHosts`, `Cors:AllowedOrigins`, `ForwardedHeaders:KnownNetworks` | **Yapılandırma, kalır** | DB'de değil, küçük sabit liste |
| `RlsSql` rol listesi, `DbIntrospectionService`, `McpConnectionGuard`, `NslParser`, Prisma/DDL ayrıştırıcıları | **Kapsam dışı** | Kullanıcı girdisi ya da bağlantı dizesi ayrıştırıyor, saklanan ilişki değil |

Sonuç: dört alan dışında sorgulanan gerçek bir ilişki bulunmadı, ek normalizasyon yapılmadı. `EF.Functions.Like` üretim kodunda yok.
Virgüllü bir alan üstünde `Contains` ile çalışan tek EF sorgusu etiket filtresiydi, kaldırıldı (Explore `q` serbest metin araması, liste değil).

## 7. Performans

**Migration süresi (ölçüm, `--migrate` duvar saati, PG 16 container; düzeltmelerden sonra 46-62 sn; denetçi önceki sürümde boşta 36 sn ölçtü, fark yük/ortam):** 200.000 issue (447.995 bağ, 50.046 etiket), 20.000 anahtar
(50.000 origin, 50.000 IP), 50.000 izin (150.000 maske): **110,7 sn, ACCESS EXCLUSIVE kilit altında.** Bu tablolarda okuma da
durur; bakım penceresi gerekir. Küçük veride (dumandaki DB) 2,2 sn. Süre doğrusal mı ayrıca ölçülmedi.

**EXPLAIN ANALYZE (aynı DB, EF'in ürettiği şekle göre elle yazılmış SQL, sonuçlar `ANALYZE` sonrası):**

| Sorgu | Süre | Plan |
|---|---|---|
| Etiket filtresi, COUNT, nadir (500 bağ) | 3,1 ms | `IX_issue_labels_ProjectId_NormalizedName` → `IX_repo_issue_labels_IssueLabelId_RepoIssueId` → `PK_RepoIssues` |
| Etiket filtresi, sayfa, nadir | 2,4 ms | Aynı |
| Etiket filtresi, sayfa, yaygın etiket | 33,5 ms | Aynı, satır çok |
| Anahtar doğrulama + iki liste | 0,17 ms | `IX_GatewayApiKeys_Prefix`, iki çocuk indeks |
| Tablo izni | 0,12 ms | `IX_GatewayTablePermissions_ProjectId_TableName` |
| Maskeli kolonlar | 0,07 ms | İzin indeksi, `IX_table_masked_columns_...` |

İlk ölçümde filtre `issue_labels`'ı **tam tarıyordu** (4 ms, 50 bin satır): koşulda proje yoktu ve UNIQUE indeks kullanılamıyordu.
`WithLabel(projectId, label)` ile düzeltildi, 8,9 → 3,1 ms. Tablolar büyüdükçe fark açılırdı.

**N+1:** anahtar doğrulama listeleri `AsSingleQuery` ile tek sorguda yükler, ek tur yok (`Anahtar_dogrulama_listeleri_tek_sorguda_yukluyor`,
SQL komutları sayılıyor). `IsTableAllowedAsync` çocuk tabloya hiç gitmez. Maske listesi tablo başına tek sorgu (önceki gibi).
Kartezyen çarpım API sınırıyla (64 x 64) tavanlı; bu sınırdan büyük eski listeler teoride mümkün, etkisi ölçülmedi.

## 8. Test sonuçları

| Komut | Proje | Toplam | Geçen | Kalan | Atlanan | Süre |
|---|---|---:|---:|---:|---:|---:|
| `dotnet test backend/Namines.Tests/Namines.Tests.csproj -c Release --no-build -- xUnit.ParallelizeAssembly=false xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1` | `Namines.Tests` | 2898 | 2898 | 0 | 0 | 15 dk 10 sn |
| Aynı | `Namines.Tests.RunTests` | 19 | 19 | 0 | 0 | 59 sn |
| `npx tsc --noEmit -p tsconfig.json` | `frontend` | | | 0 hata | | |
| `npx vitest run --maxWorkers=4` | `frontend` | 280 (39 dosya) | 280 | 0 | 0 | 39,7 sn |

**Sıralılık:** eski `-- xunit.parallelize...=false` argümanı etkisizdi (ad büyük/küçük harf duyarlı, denetim TRX ile gösterdi); bu rapordaki önceki
sayılar paralel koşmuştu. Doğrusu `-- xUnit.ParallelizeAssembly=false xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`. Kanıt `xUnit.DiagnosticMessages=true` çıktısı: eski argümanla `parallel test collections = on [12 threads]`, yenisiyle `off`.
Prisma kırmızısının kök nedeni: `CoderAiPackageSecretsTests` süreç genelinde çalışma dizinini değiştirip siliyor, `PrismaAvailable` `cmd.exe`'yi devralınan
dizinde açıyordu; `WorkingDirectory` verildi, test paralel olmayan koleksiyona alındı.

Docker ve PostgreSQL ayakta; ortam yüzünden atlanan test yok, inconclusive yok. Frontend'de kod değişmedi. `eslint` ve `next build`
bu turda çalıştırılmadı (denetçi çalıştırdı, frontend bu fazda değişmedi). `services/desk` testleri çalıştırılmadı: Desk'in `apiKeys.ts`'i yalnızca aynı metin alanlarını tipliyor.

Toplam düştü (önceki 4675): 1830 satırlık kartezyen teori iki tablo testine çevrildi, kapsam aynı, yeni testler eklendi.

**Önceki koşuda kırmızı:** ilk tam koşu 4673/4675 idi: iki `GatewayAuditTests` testi `InitializeAsync`'te `Exception while writing to
stream` ile düştü. Değişen kodla ilgisi yok (ilk bağlantı yazımı). Kök neden denetçinin ayrı programıyla doğrulandı: Npgsql havuzu bağlantı dizesine
göre anahtarlı, test başına açılan container host portunu geri dönüştürürse havuz ölü bağlantı verir. Önlem: container başlarken
`PostgresTestPools.Reset()`. Tüm PostgreSQL sınıflarında uygulanıyor; son tam koşu temiz.


**Gerçek uygulama duman testi:** seed'li eski veri `20261105105313`'te, `dotnet Namines.API.dll --migrate` (2,2 sn), API
`Security__AllowPrivateDbHosts=true` ve `ForwardedHeaders__KnownNetworks=127.0.0.0/8` ile, ayrı `gw_target` DB'sine bağlı.
`node smoke-c.js`: **53/53**, API logunda ERR yok. Kapsam: anahtar DTO, 11 geçersiz liste → 400, tablo izni, maskeli/maskesiz okuma
(gerçek veri), origin izin/ret (büyük harf, sondaki `/`, port varyantı, başlıksız), IPv4/IPv6/CIDR izin/ret (gerçek
`X-Forwarded-For`), migrate edilmiş fail-closed eski anahtarlar (B-1 değerleri dâhil: `::.2.3.4/0` ve `::ffff:1.2.3` 403, `Restrict*=true`, satır yok), virgüllü eski anahtar, issue+etiket, filtre, iptal, proje silme.
Tohumlayıcı `HubBlueprintSeeder` açılışta çalıştı, etiket yazdı (20 satır).

**Katalog kanıtı** (`c_smoke`, migrate edilmiş):

```
old_columns = 0   (GatewayApiKeys.AllowedOrigins/AllowedIps, GatewayTablePermissions.MaskedColumns, RepoIssues.Labels)
gateway: PK_/IX_ api_key_allowed_ips, api_key_allowed_origins, table_masked_columns
hub:     PK_issue_labels, IX_issue_labels_ProjectId_NormalizedName, PK_repo_issue_labels, IX_repo_issue_labels_IssueLabelId_RepoIssueId
trigger: trg_repo_issue_labels_gc ON hub.repo_issue_labels
```

`has-pending-model-changes`: "No changes" (AuthDbContext, StudioDbContext, HubDbContext, GatewayDbContext).

## 9. Yapılmayanlar ve açık kalanlar

- `namines_*` MCP araçları (CLAUDE.md'nin istediği etki analizi ve kanıt): bu oturumda sunucu bağlanamadı (CONNECT_TIMEOUT).
  Yerine gerçek PG migration testleri ve üretim yolu çalıştırıldı.
- Üst tablolar (`GatewayApiKeys`, `GatewayTablePermissions`, `RepoIssues`) hâlâ `public`'te ve `HubController`/`GatewayKeyController`
  hâlâ `AuthDbContext` kullanıyor: D-1 işi. `RepoIssueLabelStore` Infrastructure'da, D-1'de Hub'a taşınmalı.
- `label.ProjectId = issue.ProjectId` veritabanı kısıtı değil (§5).
- Anahtar listelerini güncelleme ucu yok (önceden de yoktu). Fail-closed bir anahtarın çözümü: yenisini üret.
- 110 sn'lik kilit büyük kurulum için bakım penceresi ister; toplu/kademeli doldurma yapılmadı çünkü tek işlem sızıntı penceresini kapatıyor.
- Virgüllü eski anahtarlar (`",,"`) serbest kalır: davranış korundu, operatör `blank_list_means_no_restriction` kayıtlarını incelemeli.
- Karantina ham metni anahtar silindikten sonra da durur (L-2, D-2 backlog). Anahtar sorgusunda çarpım eski büyük listelerde tavansız (L-6, D-1).
  Etiket anahtarında Unicode 16 harfleri için ICU/.NET farkı (I-1), bu harfli eski etikette ikinci katalog satırı açar.
- `namines_app`'e `migration_quarantine` yetkisi verilmedi; karantinayı okuyan arayüz yok, operatör SQL ile bakar.
