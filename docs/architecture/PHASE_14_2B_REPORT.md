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
| API compatibility | PASS, farklar §4 tablosunda | Rota ve alan adları aynı; gerçek API'ye karşı 31/31 duman kontrolü; frontend `tsc` 0 hata, vitest 39 dosya 280/280 |
| Documentation | PASS | Bu rapor, `IMPLEMENTATION_STATUS.md`, `MODULE_OWNERSHIP.md`, `MIGRATION_PLAN.md`, `DATABASE_NORMALIZATION.md` |
| Performans | PASS | §6: eşleşmeyen `q` ~21 ms (önceden ~1,05 sn), sorgu sayısı public repo sayısından bağımsız |
| Independent audit | CONDITIONALLY ACCEPTED (ikinci tur, `9f59c07`): birinci tur bulguları kapandı, yeni bloklayıcı B-3 açık (commit sonrası Stripe iptali kaybolabiliyor) | `PHASE_14_2B_INDEPENDENT_AUDIT.md` |

## 2. Veri modeli (gerçekte oluşan)

| Tablo | Kolonlar | Anahtar / kısıt |
|---|---|---|
| `studio.projects` | Id, Name, DbType, SchemaJson, NodePositionsJson, UserId, OrganizationId, AutoApproveSafeChanges, CreatedAt, UpdatedAt, xmin | Eski `CloudProjects` satırları yerinde; FK'ler tabloyu izledi |
| `studio.project_connections` | ProjectId, EncryptedConnectionString?, ConnectionDbType?, AllowDeskSql, CreatedAt, UpdatedAt | PK+FK ProjectId CASCADE (1:0..1) |
| `studio.project_shares` | Id, ProjectId, ShareTokenHash, CreatedAt, RevokedAt? | UNIQUE ShareTokenHash, IX ProjectId, CHECK uzunluk = 64, FK CASCADE |
| `hub.repositories` | ProjectId, OwnerUserId, ProjectName, ProjectDbType, Slug?, Description?, Visibility, Category, StarCount, ForkCount, ForkedFromProjectId?, LinkedGithubRepo?, CreatedAt, UpdatedAt | PK+FK ProjectId CASCADE; self-FK SET NULL; UNIQUE (OwnerUserId, Slug) dolu slug'larda; kısmi Explore indeksleri; CHECK'ler |
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

3. `20261105100439_Phase14_2B_ExploreReadModel` (denetim B-1): `hub.repositories`'e `ProjectName`, `ProjectDbType`
   ekler, doldurur, alan alan doğrular, NOT NULL yapar. UPDATE, NOT VALID CHECK'lere takılmasın diye bu kısıtlar
   aynı işlemde kaldırılıp aynen geri eklenir (`Visibility=99` gibi eski satırlar; `MigrationHygieneTests` yakaladı).

**Kilit (ölçüldü):** ACCESS EXCLUSIVE, işlem boyunca. 50.000 proje, 150.000 etiket, 5.000 bağlantı, 5.000 paylaşım,
PG 16 container, `psql -f` ile migration betiği, duvar saati:

| Migration | Boşta | Eşzamanlı okuma yükü altında |
|---|---|---|
| `DecomposeCloudProjects` | 13,7 sn | 18,7 sn |
| `ExploreReadModel` | 1,2 sn | 1,6 sn |

Denetçi Decompose için 7 sn (boşta) ve 8 sn (yük altında) ölçmüştü. Farkı açıklamıyorum, makine ve tohum farklı (bu
ölçümde `docker exec` kaynaklı istemci süresi de var). Bakım penceresi planı yüksek sayıya göre yapılmalı. Süre
proje sayısıyla doğrusal beklenir, bu ayrıca ölçülmedi.

**Tekrar çalıştırma ve kısmi durum:** EF geçmişi tek seferlik çalıştırmayı garanti eder (test var).
Bağlanmamış eski üreteç SQL'inin prod'da hiç çalışmadığı **varsayımdır** (kod yolu yoktu, yalnızca testler
çağırıyordu). Artık varsa migration açık mesajla durur, işlem geri alınır, kaynak dokunulmamış kalır;
tablo temizlenince yeniden çalışır (test var). Şekli farklı tablonun üstüne sessizce yazmak yerine bu seçildi.

**Dağıtım sırası:** tek adımlı yerinde taşıma olduğu için eski kod yeni şemada çalışmaz ve yeni kod eski şemada
çalışmaz. Eski sürümü durdur, yedek al, `--migrate`, yeni sürümü aç (`docs/deploy/MIGRATION-VE-GERI-ALMA.md` §5.1).
Repo kuralına bilinçli istisna, `MigrationCompatibilityTests.DeliberateExceptions`'ta gerekçeli.

**Geri alma (`Down`):** kolonları ve veriyi geri koyar; repo, bağlantı, etiket ve paylaşımı doğrular; temiz veride
CHECK'leri VALIDATE eder. Kayıplar ve kalanlar bilinçli:
- şemalar kalır (`roles.sql`'in şemaya bağlı varsayılan yetkileri düşmesin diye), tablolar silinir;
- düz metin token geri gelmez (özetten türetilemez), linkler özetle çalışmaya devam eder;
- eski tablo proje başına tek hash tutabildiği için en yeni etkin bağlantı döner, diğer etkin linkler ve
  iptal geçmişi kaybolur;
- `Tags` normalleştirilmiş hâliyle döner ("a, b ,A" → "a,b").

**Negatif yol ve yetki (denetim L-4, M-1):** Up için 12, Down için 4 bozma senaryosu tek oturumda doğrulamayı
patlatıyor. `roles.sql` gerçek psql ile uygulanıp `namines_migrator` ile boş kurulum, Down, Up çalışıyor;
`namines_app` her adımdan sonra DML yapabiliyor, DDL yapamıyor (`42501`).

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
- **Hub okuma modeli (`OwnerUserId`, `ProjectName`, `ProjectDbType`):** modüller arası join olmadan (sahip, slug)
  benzersizliği ve Explore'un tek SQL'i için. Proje yazımıyla aynı işlemde güncelleniyor: sync, ayarlar, fork,
  tohumlayıcılar, hesap silme devri (test var: yeniden adlandırma aramaya yansıyor).
- **Hesap silme sırası (denetim B-2):** ön kontroller → kişisel projeler (blob/sağlayıcı, kullanıcı satırı silinmeden
  önce olmak zorunda, yoksa FK cascade temizliği atlar) → **tek işlem** (devir + slug çözümü, yıldızlar, kullanıcı,
  denetim) → Stripe, işlem başarılı olduktan sonra. Denetim satırı önce `pending` yazılır, sonra sonuçla güncellenir.
  İşlem düşerse Stripe'a dokunulmaz, devir geri alınır, yeniden deneme tamamlar (test var). **Kalan pencere:** beklenmeyen
  bir DB hatasında kişisel projeler silinmiş olarak kalır, hesap durur; kullanıcı yeniden deneyince biter.
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
  ve sıra `DisplayTag`/`Position`. SQL anahtarı ICU ile harf harf üretilir: bağlam duyarlı küçültme (sondaki Yunanca
  Σ) C#'tan ayrışmasın. `İ` korunur çünkü .NET invariant onu değiştirmez (test C# ile eşitliği doğruluyor).
- **Sayaçlar:** yıldız satırı + sayaç tek işlem, tek SQL ifadesi. **Yeni:** fork silinince kaynağın `ForkCount`'u
  düşer, hesap silinince yıldızladığı repoların `StarCount`'u düşer (eskiden kalıcı kayıyordu).

**API farkları (denetim §4; rota ve DTO alan adları aynı):**

| Fark | Frontend etkisi | Durum |
|---|---|---|
| `Tags` normalleşmiş, etiketsizde `null` | Yok: `repo.tags \|\| ''`, `tags?: string` | Kabul |
| Explore `q` etikette tam eşleşme ("post", "postgresql" etiketini bulmaz; ad ve açıklamada alt dizge aynen) | Kırılma yok; arama kutusu metni davranışa göre değişti (`app/hub/page.tsx`) | Sahibin kuralı: etikette LIKE yok. **Sahibe bildirilmeli** |
| `?tag=` eklendi | Yok | Kabul |
| POST paylaşım her çağrıda yeni link, eskiler geçerli | Yok: arayüz her tıklamada POST atıyor | Kabul |
| POST paylaşıma 429 (kendi kovası `share-create`, kullanıcı başına 30/dk) | Genel hata mesajı | Fork/ayar/silme kovasından ayrıldı (L-3) |
| Explore'a 429 (`explore-public`, IP başına 120/dk) | Genel hata | B-1 |
| Fork silinince `ForkCount`, hesap silinince `StarCount` düşüyor | Yok | Düzeltme, kabul |
| Devredilen repoda varisin slug'ı varsa `slug-2` (URL değişir, loglanır) | Yok | B-2 |

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

**Açık kalan, bu fazın dışı:** Vault/Ground `OrganizationId` kolonu duruyor, FK'si yok (14.2A kararı, kolon silinmedi).
duruyor, FK'si yok (14.2A kararı, kolon silinmedi).

## 6. Performans

Tohum: 50.012 proje, 10.012 public, 149.754 etiket, 5.004 paylaşım; `ANALYZE` sonrası, PG 16. SQL'ler EF'in ürettiği
biçimde, süreler `EXPLAIN (ANALYZE, BUFFERS)` çıktısından.

| Sorgu | Plan | Süre |
|---|---|---|
| Explore popüler (LIMIT 200) | Index Scan `IX_repositories_Explore_Popular`, sort yok | 0,96 ms |
| Explore yeni | Index Scan `IX_repositories_Explore_Recent`, sort yok | 0,18 ms |
| `q` eşleşmeyen (ad, açıklama, tam etiket) | Index Scan `IX_repositories_Explore_Popular` + filtre, 10.012 satır elenir | 18,48 ms |
| `dbType` eşleşmeyen | Bitmap `IX_repositories_Explore_Recent` + filtre | 5,31 ms |
| `dbType` eşleşen (LIMIT 200) | Bitmap + top-N heapsort | 5,76 ms |
| Etiket filtresi (EXISTS) | Index Only Scan `IX_project_tags_Tag_ProjectId` + Hash Join + Sort | 5,41 ms |
| Kategori | Bitmap Index `IX_repositories_Explore_Recent` + top-N | 7,26 ms |
| 200 kimlik proje özeti | Index Scan `PK_projects` | 2,04 ms |
| 200 kimlik etiket | Bitmap `PK_project_tags` | 2,32 ms |
| Paylaşım özetiyle arama | Index Scan `IX_project_shares_ShareTokenHash` + PK | 0,14 ms |
| Proje listesi | BitmapOr `IX_projects_UserId` + `IX_projects_OrganizationId` | 1,44 ms |
| Sahip + slug çözümü | BitmapOr PK + `IX_repositories_Slug` | 0,12 ms |

**N+1 yok:** Explore her filtreyle en fazla 5 sorgu (repo, özet, etiket, şema, sahip adı); 650 public repolu gerçek PG
testinde komut sayacıyla doğrulanıyor. Proje listesi bağlantıyı tek toplu sorguyla alıyor.

**Gerçek HTTP (2. ve 3. çağrı):** filtresiz 20-27 ms, `tag` 21 ms, eşleşmeyen `q` 21 ms, `dbType=oracle` 13-25 ms,
`dbType=mysql` 25 ms. Önceki sürümde eşleşmeyen `q` ~1,05 sn idi (denetçi 574-625 ms ölçtü).

**Bilinen sınır:** `q` alt dizge araması indeks kullanamaz, public repolar üzerinde doğrusal (10.012 satırda ~18 ms),
ama tek SQL ve `LIMIT 200`. Çok büyürse trigram indeksi gerekir; şu ölçekte gerekmiyor.

## 7. Test sonuçları

| Komut | Proje | Toplam | Geçen | Kalan | Atlanan | Süre |
|---|---|---|---|---|---|---|
| Temel (değişiklikten önce, `5565811`) | Namines.Tests | 2785 | 2776 | 9 | 0 | 42 m 36 s |
| Son (`be11e15`) `dotnet test backend/Namines.Tests -c Release --no-build` | Namines.Tests | 2812 | 2812 | 0 | 0 | 11 m 27 s |
| Son `dotnet test backend/Namines.Tests.RunTests -c Release --no-build` | Namines.Tests.RunTests (Launch dahil) | 19 | 19 | 0 | 0 | 1 m 4 s |
| Son `npx tsc --noEmit`, `npx vitest run` | frontend | 280 | 280 | 0 | 0 | 11 s (39 dosya, tsc 0 hata) |
| Önceki (`815d76e`) `dotnet test backend/Namines.Tests -c Release --no-build` | Namines.Tests | 2804 | 2804 | 0 | 0 | 20 m 15 s |
| Önceki `dotnet test backend/Namines.Tests.RunTests -c Release --no-build` | Namines.Tests.RunTests (Launch dahil) | 19 | 19 | 0 | 0 | 1 m 7 s |

2812'nin 206'sı `Namines.Tests.Integration` ad alanında (8 `Phase14_2BMigrationTests`, 17 `Phase14_2BFlowTests`), 19'u
`ModularMonolithArchitectureTests`, gerçek PostgreSQL'de. Atlanan yok. Sıralı koşu
(`xunit.parallelizeAssembly=false`, `parallelizeTestCollections=false`), Docker gerçek. Süre makine yüküne çok
bağlı: aynı suite 39-59 dk da sürdü.

Yeşile giderken ara koşular: 2804'te 2 hata (benim testimde eksik paylaşım tohumu; Oracle zaman aşımı), 1 hata
(`GatewayApiKeyTests` taze container'a ilk bağlantıda "connection aborted"). Son ikisi altyapı kaynaklı, testlerde
yalnızca o hataya özel yeniden deneme eklendi, doğrulamalar değişmedi. Bunlar aralıklıydı: tek bir yeşil koşu
gelecekte de düşmeyeceklerini kanıtlamaz.

Temel koşudaki 9 hatanın 1'i gerçek (`MigrationModelSnapshotTests`, kayma); 8'i o sırada makinede paralel derleme
çalışırken Npgsql okuma zaman aşımı (`AiQuotaServiceTests`, `DeskHandoffTests`, `GatewayApiKeyTests`).

Ayakta uygulama: `Namines.API --migrate` temiz DB'de başarılı; uygulama açıldı, 10 blueprint tohumlandı; gerçek
HTTP duman testi 31/31, denetim düzeltmelerinden sonra yeniden (kayıt, sync, 409, ayarlar, yıldız, fork, Explore, paylaşım, bağlantı, Gateway şeması,
SQL konsolu bayrağı, iptal, silme, 429). Log'da ERR 0, parola/şifreli metin eşleşmesi 0.

**Oracle testi:** `OracleIntrospectionTests` soğuk container'da aralıklı düşüyordu (her koşuda başka test, tek
başına koşarken de; `TaskCanceledException`). Bu fazın kodu değil: `DbIntrospectionService` değişmedi, kök neden
sözlük görünümlerinin ilk ayrıştırmasının 15 sn'lik `QueryTimeout`'u aşması. Testte yalnızca zaman aşımı için en
fazla 4 deneme eklendi, doğrulamalar aynı. Üründeki 15 sn değişmedi; soğuk Oracle hedefinde kullanıcı ilk denemede
zaman aşımı görebilir, ayrı bir iş.

## 8. Yapılmayanlar

- 14.2C konuları (`RepoIssues.Labels`, Gateway CSV kolonları) kapsam dışı.
- `CloudProject` sınıfının adı ve `AuthDbContext.CloudProjects` DbSet'i duruyor (D-1/D-2).
- `ModuleMigrationOrchestrator` silindi (L-5): modül bağlamlarının migration'ı yok, DDL tek zincirde, çalıştırılsa no-op olurdu. Modül başına migration D fazında gelirse yeniden yazılır.
- `RepoStars`, `RepoIssues`, `RepoIssueComments` fiziksel olarak `public`'te.
- Frontend'de yalnızca Explore arama kutusu metni değişti (`app/hub/page.tsx`). `tsc --noEmit` 0 hata; vitest 39 dosya, 280/280.
- CLI ve MCP taşınan veriyi kullanmıyor (MCP yalnız `HubBlueprintCatalog`); yalnızca derleme doğrulandı.
- `namines_*` MCP araçları bu oturumda bağlanamadı (CONNECT_TIMEOUT), `namines_analyze_impact` ve `namines_prove_migration` çalıştırılamadı. Kanıt yerine gerçek PostgreSQL testleri.
