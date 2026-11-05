# Faz 14.2B Raporu: CloudProjects Ayrıştırması

> Dal: `arch/phase-14-2`. Temel: `5565811`. Ortam: Windows 11, .NET 8 SDK 8.0.427, Docker 29.9,
> `postgres:17-alpine` (Testcontainers), `postgres:16-alpine` (EXPLAIN ve katalog kanıtı).
> Bu rapor önceki `PHASE_14_2B_IMPLEMENTATION_REPORT.md`'nin yerini alır; o dosya kolonların
> düşürüldüğünü ve testlerin yeşil olduğunu iddia ediyordu, ikisi de doğru değildi.

## 1. Kabul tablosu

| Alan | Sonuç | Kanıt |
|---|---|---|
| Build | PASS | `dotnet build backend/Namines.sln -c Release --no-incremental`: 0 uyarı, 0 hata |
| Unit | PASS | `Namines.Tests` içinde Docker gerektirmeyenler; sayılar §7 |
| Integration | PASS | Gerçek PostgreSQL: `Phase14_2BMigrationTests`, `Phase14_2BFlowTests`, `MigrationHygieneTests`, `Namines.Tests.RunTests`; sayılar §7 |
| Architecture | PASS | `ModularMonolithArchitectureTests`; §7 |
| Migration | PASS | Boş DB, seed'li eski DB, Down + Up, yarım eski üreteç artığı (§3) |
| Data integrity | PASS | Migration içi alan alan doğrulama (`RAISE EXCEPTION`) + test dökümü: Up1 = Up2 (§3) |
| Security | PASS | §5; AES-GCM AAD, tek çözme noktası, hash-only paylaşım, SSRF testi, duman testinde log taraması |
| API compatibility | PASS, 2 bilinçli fark | §4; rota/alan/durum kodu değişmedi, gerçek API'ye karşı 31/31 duman kontrolü |
| Documentation | PASS | Bu rapor, `IMPLEMENTATION_STATUS.md`, `MODULE_OWNERSHIP.md`, `MIGRATION_PLAN.md`, `DATABASE_NORMALIZATION.md` |
| Independent audit | (denetçi dolduracak) | |

## 2. Veri modeli (gerçekte oluşan)

| Tablo | Kolonlar | Anahtar / kısıt |
|---|---|---|
| `studio.projects` | Id, Name, DbType, SchemaJson, NodePositionsJson, UserId, OrganizationId, AutoApproveSafeChanges, CreatedAt, UpdatedAt, xmin | Eski `CloudProjects` satırları yerinde; FK'ler tabloyu izledi |
| `studio.project_connections` | ProjectId, EncryptedConnectionString?, ConnectionDbType?, AllowDeskSql, CreatedAt, UpdatedAt | PK+FK ProjectId CASCADE (1:0..1) |
| `studio.project_shares` | Id, ProjectId, ShareTokenHash, CreatedAt, RevokedAt? | UNIQUE ShareTokenHash, IX ProjectId, CHECK uzunluk = 64, FK CASCADE |
| `hub.repositories` | ProjectId, OwnerUserId, Slug?, Description?, Visibility, Category, StarCount, ForkCount, ForkedFromProjectId?, LinkedGithubRepo?, CreatedAt, UpdatedAt | PK+FK ProjectId CASCADE; self-FK SET NULL; UNIQUE (OwnerUserId, Slug) dolu slug'larda; kısmi Explore indeksleri; CHECK'ler |
| `hub.project_tags` | ProjectId, Tag, DisplayTag, Position | PK (ProjectId, Tag); IX (Tag, ProjectId); FK repositories CASCADE |

Sahiplik: `ProjectConnection`/`ProjectShare` Studio'nun, `ProjectRepository`/`ProjectTag`/sayaçlar Hub'ın.
`AuthDbContext` bu tabloları yalnızca modelinde tutar (DbSet yok): DDL tek zincirde kalsın ve SQLite
testleri `EnsureCreated` ile kurabilsin. `StudioDbContext`/`HubDbContext` aynı tabloları
`ExcludeFromMigrations` ile eşler, migration üretmez. Branch/SchemaVersion/CrossDatabaseRelation ve
RepoIssues bu bağlamlardan çıkarıldı (kendi fazlarına kadar `public`'te). `RepoStars` Hub bağlamında,
gerçek konumu `public` olarak eşli. Kullanılmayan `ProjectGitRepository`, `ProjectPublication`,
`SchemaShareLink` varlıkları ve üç SQL üreteci silindi.

## 3. Migration

İki migration, `AuthDbContext` zincirinde (`Database.Migrate()` / `--migrate` yolunun tek zinciri):

1. `20261105055013_Phase14_2B_ReconcileModelDrift`: özeti olmayan düz metin token'ları özetler, doğrular,
   `ShareToken` kolonunu düşürür; Vault/Ground org FK'lerini ve indekslerini kaldırır. Bu ikisi önceki
   fazdan kalan kayma idi (`has-pending-model-changes` kırmızıydı).
2. `20261105060611_Phase14_2B_DecomposeCloudProjects`: tek işlemde kilit, tablo oluşturma, doldurma,
   alan alan doğrulama, 13 kolonu fiziksel DROP, `SET SCHEMA studio` + `RENAME TO projects`, ad düzeltmeleri.

**Doğrulama (migration içinde, `RAISE EXCEPTION`):** bağlantılarda eksik/yetim/alan farkı (şifreli metin,
motor, onay); paylaşımda eksik/yinelenen/yetim; repolarda eksik/yetim ve 10 alanın `IS DISTINCT FROM`
karşılaştırması; etikette ekleme sorgusundan bağımsız ikinci ayrıştırmayla proje başına sayım,
kesintisiz Position, `Tag = key(DisplayTag)`.

**Şifreli metin:** AAD proje kimliği ve o değişmiyor (`IConnectionSecretProtector.Protect(x, projectId)`),
bu yüzden bayt bayt kopya. AAD'siz eski satırlar protector'ın mevcut geri düşüşüyle çözülür (test var).

**Kilit:** `CloudProjects` üzerinde ACCESS EXCLUSIVE, işlem boyunca. DROP COLUMN ve SET SCHEMA yalnızca
katalog işi; doldurma süresi proje sayısıyla orantılı. Proje başına birkaç satır yazıldığı için küçük
kurulumlarda saniye altı beklenir; canlıda kısa bir bakım penceresi gerekir.

**Tekrar çalıştırma ve kısmi durum:** EF geçmişi tek seferlik çalıştırmayı garanti eder (test var).
Bağlanmamış eski üreteç SQL'inin prod'da hiç çalışmadığı **varsayımdır** (kod yolu yoktu, yalnızca testler
çağırıyordu). Artık varsa migration açık mesajla durur, işlem geri alınır, kaynak dokunulmamış kalır;
tablo temizlenince yeniden çalışır (test var). Şekli farklı tablonun üstüne sessizce yazmak yerine bu seçildi.

**Dağıtım sırası:** tek adımlı yerinde taşıma olduğu için eski kod yeni şemada çalışmaz ve yeni kod eski şemada
çalışmaz. Eski sürümü durdur, yedek al, `--migrate`, yeni sürümü aç (`docs/deploy/MIGRATION-VE-GERI-ALMA.md` §5.1).
Repo kuralına bilinçli istisna, `MigrationCompatibilityTests.DeliberateExceptions`'ta gerekçeli.

**Geri alma (`Down`):** kolonları ve veriyi geri koyar, doğrular, şemaları siler. Kayıplar bilinçli:
- düz metin token geri gelmez (özetten türetilemez), linkler özetle çalışmaya devam eder;
- eski tablo proje başına tek hash tutabildiği için en yeni etkin bağlantı döner, diğer etkin linkler ve
  iptal geçmişi kaybolur;
- `Tags` normalleştirilmiş hâliyle döner ("a, b ,A" → "a,b").

**Test edilenler (`Phase14_2BMigrationTests`, gerçek PG):** boş DB; `AddDeletionAuditActions`'tan seed'li
yükseltme (AAD'li + AAD'siz sır, düz metin/yalnız özet/ikisi birden token, çift yazımlı ve boşluklu CSV,
Türkçe harf, fork, yetim fork, slug'sız, boş slug, sahipler arası aynı slug, bağlantısız konsol onayı,
private/team/public); Down + Up döküm eşitliği; katalog; artık tablo davranışı; Studio/Hub eşlemesinin her
kolonunun DB'de var olması.

**Katalog kanıtı** (migrate edilmiş gerçek PG, `namines-verify`):

```
CloudProjects_relations = 0        CloudProjects_constraints = 0
moved_columns_on_projects = 0      ShareToken_column_anywhere = 0
studio.projects kolonları: Id, Name, DbType, SchemaJson, NodePositionsJson, UserId, CreatedAt,
                           UpdatedAt, AutoApproveSafeChanges, OrganizationId
studio.projects'e gelen FK: 19 (Branches, SchemaVersions, ChangeRequests, GatewayApiKeys,
  GatewayTablePermissions, CrossDatabaseRelations x2, vault x3, ground, desk x2, flow, RepoIssues,
  RepoStars, project_connections, project_shares, repositories), hepsi FK_*_projects_*
İndeksler: PK_projects, IX_projects_{OrganizationId,UserId}; PK_project_connections;
  PK_project_shares, IX_project_shares_{ShareTokenHash,ProjectId}; PK_repositories,
  IX_repositories_{OwnerUserId_Slug,Slug,ForkedFromProjectId,Explore_Popular,Explore_Recent};
  PK_project_tags, IX_project_tags_Tag_ProjectId
```

`has-pending-model-changes`: "No changes" (AuthDbContext), "No changes" (Studio, Hub).

## 4. Kararlar ve bilinçli davranış farkları

- **Slug'sız proje:** her projenin repo satırı var, `Slug` nullable. Boş dizge de kaynaktaki gibi korunur.
  Uydurma genel URL üretilmez. Benzersizlik eski kuralla aynı: dolu slug'larda (sahip, slug). Ayar uçlarının
  küresel "slug alınmış" ön kontrolü de aynen duruyor (409).
- **`OwnerUserId` kopyası:** modüller arası join olmadan (sahip, slug) benzersizliği için. Tek değiştiği yer
  hesap silmede devir, aynı işlemde güncelleniyor (test var).
- **Paylaşım linki yeniden gösterim:** arayüz her tıklamada POST atıp dönen token'dan URL kopyalıyor; GET yok.
  Özet saklayınca aynı token geri verilemez. Tek satırda döndürmek daha önce yapıştırılmış URL'leri sessizce
  öldürürdü. Bu yüzden **proje başına çok satır**: her POST yeni link üretir, eskiler çalışır, DELETE hepsini
  kapatır. Sözleşme (`{ token }`) değişmedi. Tavan 50 etkin link; aşılınca en eskisi kapanır. POST'a
  `sensitive` hız sınırı eklendi (dakikada 5, yeni 429 ihtimali). Brief'teki "rotate" seçeneğinden sapma.
- **`AllowDeskSql` bağlantısız:** eski tabloda bağlantı olmadan açık olabiliyordu ve bağlantı silinince
  korunuyordu. Kayıpsızlık için satır, bağlantı/motor/onaydan biri varsa var; şifreli metin nullable.
- **Modüller arası işlem:** `ICrossModuleTransaction` = `SharedTransactionManager` (Auth + Studio + Hub tek
  işlem, Auth'un retry stratejisi içinde). Yeniden denemede kayıp olmasın diye `SaveChanges(false)` +
  `AcceptAllChanges`. Proje oluşturma, ayarlar, fork, proje/hesap silme, tohumlama bununla.
- **Etiket:** `Tag` kanonik anahtar (trim + `ToLowerInvariant`), PK yinelemeyi DB'de engeller. Görünen yazım
  ve sıra `DisplayTag`/`Position`. SQL anahtarı ICU ile üretilir ve `İ` korunur çünkü .NET invariant onu
  değiştirmez (test C# ile eşitliği doğruluyor).
- **Sayaçlar:** yıldız satırı + sayaç tek işlem, tek SQL ifadesi. **Yeni:** fork silinince kaynağın `ForkCount`'u
  düşer, hesap silinince yıldızladığı repoların `StarCount`'u düşer (eskiden kalıcı kayıyordu).

**Bilinçli API farkları (rota, alan adı, durum kodu aynı):**
1. `Tags` normalleştirilmiş döner ("a, b ,A" → "a,b"); etiketsiz projede `""` yerine `null`. Arayüz iki
   durumu da karşılıyor (`repo.tags || ''`).
2. Explore `q` etikette artık tam eşleşir ("post" artık "postgresql" etiketini bulmaz; ad ve açıklamada alt
   dizge aynen). Sahibin kuralı: etikette LIKE yok. Eklenen: opsiyonel `?tag=`.

## 5. Güvenlik geçişi

| Konu | Durum |
|---|---|
| IDOR / kiracılar arası | Hub, Share, Gateway uçları yetki kontrolünü aynı `OrgAccess` üzerinden yapıyor; özel repo yabancıya 404; başka sahibin altından slug çözülmüyor (test) |
| Sır sızıntısı | `EncryptedConnectionString` `[JsonIgnore]`; hiçbir DTO taşımıyor; proje listesi yalnızca `hasConnection`; duman testinde yanıt ve log taraması temiz; doğrulama hataları yalnız sayı içerir |
| Tek çözme noktası | `TenantProjectAuthorityService`; rotasyon aracı bilinçli istisna; mimari test kaynak taramasıyla zorluyor |
| Çözmeden önce yetki | Gateway önce rol/anahtar, sonra üst veri, en son çözme; `ResolveForUserAsync` kiracı dışına null (test) |
| AAD | Şifreli metni başka projeye kopyalamak çözülmüyor (gerçek PG testi) |
| SSRF | Gateway/Introspect bağlanmadan önce kendi denetimini yapıyor; `ResolveAsync` `HostPolicyDenyReason` döndürüyor; sıkı politikada saklı `127.0.0.1` reddediliyor (test) |
| Paylaşım token'ı | 192-bit, yalnız SHA-256; anonim uçlara `share-public` IP başına 120/dk (429 gerçek API'de doğrulandı); özet token gibi kullanılamıyor (test) |
| Mass assignment | İstek gövdelerinde sayaç/sahip/fork/sır alanı yok (mimari test) |
| Cache anahtarı | Bu faz Hub/Share/Gateway'e cache eklemedi; mevcut `TenantCacheKey` kullanımları değişmedi |
| Arka plan işçileri | Vault/Ground kendi kaydının `ProjectId`'siyle `ResolveAsync` kullanıyor, davranış değişmedi; `DataRetentionService` taşınan alana dokunmuyor |
| Düzeltilen bulgu | `SharedTransactionManager` işlem sonrası dispose edilmiş bağlantıyı geri takıyordu; Studio/Hub ayrı 100'lük havuz açıp "too many clients" veriyordu; `SqlWorkbenchService` DI'da kurulamıyordu. Üçü de yalnız gerçek motorda/ayakta uygulamada göründü |

**Açık kalan, bu fazın dışı:** hesap silmede devredilen repo, varisin aynı slug'ına çarparsa unique ihlaliyle
hesap silme hata verir (eskiden de `(UserId, Slug)` aynı davranıyordu). Vault/Ground `OrganizationId` kolonu
duruyor, FK'si yok (14.2A kararı, kolon silinmedi).

## 6. Performans

Tohum: 50.012 proje, 10.012 public, 149.754 etiket, 5.004 paylaşım; `ANALYZE` sonrası, PG 16, EF'in ürettiği
SQL loglardan alındı. Tam çıktı için `süreler `EXPLAIN (ANALYZE, BUFFERS)` çıktısından`.

| Sorgu | Plan | Süre |
|---|---|---|
| Explore popüler (LIMIT 200) | Index Scan `IX_repositories_Explore_Popular`, sort yok | 0,96 ms |
| Explore yeni | Index Scan `IX_repositories_Explore_Recent`, sort yok | 0,18 ms |
| Etiket filtresi (EXISTS) | Index Only Scan `IX_project_tags_Tag_ProjectId` + Hash Join + Sort | 5,41 ms |
| Kategori | Bitmap Index `IX_repositories_Explore_Recent` + top-N | 7,26 ms |
| 200 kimlik proje özeti | Index Scan `PK_projects` | 2,04 ms |
| 200 kimlik etiket | Bitmap `PK_project_tags` | 2,32 ms |
| Paylaşım özetiyle arama | Index Scan `IX_project_shares_ShareTokenHash` + PK | 0,14 ms |
| Proje listesi | BitmapOr `IX_projects_UserId` + `IX_projects_OrganizationId` | 1,44 ms |
| Sahip + slug çözümü | BitmapOr PK + `IX_repositories_Slug` | 0,12 ms |

**N+1 yok:** filtresiz Explore istek başına sabit 5 sorgu (repo, özet, etiket, şema, sahip adı); proje listesi
bağlantıyı tek toplu sorguyla alıyor.

**Bilinen sınır:** `q` ve `dbType` Studio alanlarına baktığı için SQL join'siz, bellekte süzülür. Eşleşme azsa
tüm public repolar 200'lük turlarla taranır: bu tohumda eşleşmeyen `q` ~1,05 sn, filtresiz ~35 ms, `tag` ~15 ms
(gerçek HTTP, 2. çağrı). D-2'de iki modül aynı bağlamda birleşmeyecekse bir arama indeksi gerekir.

## 7. Test sonuçları

| Komut | Proje | Toplam | Geçen | Kalan | Atlanan | Süre |
|---|---|---|---|---|---|---|
| Temel (değişiklikten önce, `5565811`) | Namines.Tests | 2785 | 2776 | 9 | 0 | 42 m 36 s |
| Son (`815d76e`) `dotnet test backend/Namines.Tests -c Release --no-build` | Namines.Tests | 2804 | 2804 | 0 | 0 | 20 m 15 s |
| Son `dotnet test backend/Namines.Tests.RunTests -c Release --no-build` | Namines.Tests.RunTests (Launch dahil) | 19 | 19 | 0 | 0 | 1 m 7 s |
| Son, filtre `ModularMonolithArchitectureTests` | Namines.Tests | 19 | 19 | 0 | 0 | 7 s |

2804'ün 198'i `Namines.Tests.Integration` ad alanında (5 `Phase14_2BMigrationTests`, 12 `Phase14_2BFlowTests`, 5
`MigrationHygieneTests`), gerçek PostgreSQL'de. Atlanan yok. Sıralı koşu
(`xunit.parallelizeAssembly=false`, `parallelizeTestCollections=false`), Docker gerçek. Süre makine yüküne çok
bağlı: aynı suite 39-59 dk da sürdü.

Yeşile giderken ara koşular: 2804'te 2 hata (benim testimde eksik paylaşım tohumu; Oracle zaman aşımı), 1 hata
(`GatewayApiKeyTests` taze container'a ilk bağlantıda "connection aborted"). Son ikisi altyapı kaynaklı, testlerde
yalnızca o hataya özel yeniden deneme eklendi, doğrulamalar değişmedi. Bunlar aralıklıydı: tek bir yeşil koşu
gelecekte de düşmeyeceklerini kanıtlamaz.

Temel koşudaki 9 hatanın 1'i gerçek (`MigrationModelSnapshotTests`, kayma); 8'i o sırada makinede paralel derleme
çalışırken Npgsql okuma zaman aşımı (`AiQuotaServiceTests`, `DeskHandoffTests`, `GatewayApiKeyTests`).

Ayakta uygulama: `Namines.API --migrate` temiz DB'de başarılı; uygulama açıldı, 10 blueprint tohumlandı; gerçek
HTTP duman testi 31/31 (kayıt, sync, 409, ayarlar, yıldız, fork, Explore, paylaşım, bağlantı, Gateway şeması,
SQL konsolu bayrağı, iptal, silme, 429). Log'da ERR 0, parola/şifreli metin eşleşmesi 0.

**Oracle testi:** `OracleIntrospectionTests` soğuk container'da aralıklı düşüyordu (her koşuda başka test, tek
başına koşarken de; `TaskCanceledException`). Bu fazın kodu değil: `DbIntrospectionService` değişmedi, kök neden
sözlük görünümlerinin ilk ayrıştırmasının 15 sn'lik `QueryTimeout`'u aşması. Testte yalnızca zaman aşımı için en
fazla 4 deneme eklendi, doğrulamalar aynı. Üründeki 15 sn değişmedi; soğuk Oracle hedefinde kullanıcı ilk denemede
zaman aşımı görebilir, ayrı bir iş.

## 8. Yapılmayanlar

- 14.2C konuları (`RepoIssues.Labels`, Gateway CSV kolonları) kapsam dışı.
- `CloudProject` sınıfının adı ve `AuthDbContext.CloudProjects` DbSet'i duruyor (D-1/D-2).
- `ModuleMigrationOrchestrator` hâlâ başlangıçta çağrılmıyor; modül bağlamlarının kendi migration'ı yok. Tek
  zincir `AuthDbContext`.
- `RepoStars`, `RepoIssues`, `RepoIssueComments` fiziksel olarak `public`'te.
- Frontend'e dokunulmadı, frontend testi koşulmadı (sözleşme değişmedi).
- CLI ve MCP taşınan veriyi kullanmıyor (MCP yalnız `HubBlueprintCatalog`); yalnızca derleme doğrulandı.
- `namines_*` MCP araçları bu oturumda bağlanamadı (CONNECT_TIMEOUT), `namines_analyze_impact` ve `namines_prove_migration` çalıştırılamadı. Kanıt yerine gerçek PostgreSQL testleri.
