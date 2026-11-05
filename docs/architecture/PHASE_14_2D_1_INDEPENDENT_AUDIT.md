# Faz 14.2D-1 Bağımsız Denetim

> Kapsam: `arch/phase-14-2`, `f5fcd96..a00861c`. Denetçi kodu yazmadı, kod değiştirmedi; yalnızca bu belge eklendi.
> Ortam: Windows 11, .NET 8 SDK, Docker. Test harness `postgres:17-alpine` (Testcontainers); senaryolar `namines-verify` (`postgres:16-alpine`). `namines_*` MCP araçları `CONNECT_TIMEOUT`, yerine gerçek PostgreSQL kullanıldı.

## Karar: CONDITIONALLY ACCEPTED

Migration, veri koruma, atomiklik, API uyumu ve yetki matrisi çalıştırılarak doğrulandı; ciddi sorun bulunmadı. Ama yeni bir veri kaybı regresyonu (B-1) ve D-1 kabul maddesine değen iki boşluk (M-1, M-2) var. Bunlar aynı fazda düzeltilip yeniden denetlenmeli. Minor maddeler düzeltilmeli ya da sahibi ve fazı yazılarak açıkça atanmalı: spesifikasyon "boşluk bir sonraki faza taşınmaz" diyor.

## 1. Faz sonu tablosu

| Alan | Sonuç | Kanıt |
|---|---|---|
| Build | PASS | `dotnet build backend/Namines.sln -c Release --no-incremental`: 0 uyarı, 0 hata, 52 sn. `NoWarn` eklenmedi; yeni `#pragma` yalnız EF üretimi dosyalarda |
| Unit | PASS | Tam süit içinde; dört modül Application testleri çalıştı. Boşluk: B-1 yarışını sınayan test yok (SQLite'ta `xmin` yok) |
| Integration | PASS | Tam süit, gerçek PG (`MigrationHandoffTests`, `Phase14_2D1FlowTests` Testcontainers); ek olarak §3 senaryoları |
| Architecture | PASS, boşlukla | 36 test TRX'te. 2 kendi mutasyonum düştü, 3 kaçtı (m-1) |
| Migration | PASS | §3: boş DB, karışık 14.2C verisi, Down, Up, kesilme, eşzamanlılık, en az yetkili rol |
| Data integrity | **FAIL** | Migration kayıpsız, ama çalışma zamanında B-1: şema sessizce eziliyor |
| Security | **FAIL** (M-1) | Çalıştırılan yetki matrisinde açık bulunmadı; "yetki yalnız controller'da olmaz" maddesi veri düzleminde karşılanmıyor |
| API compatibility | PASS | 253 eylem öznitelik karşılaştırması 0 fark; 829 durum/şekil karşılaştırması 0 fark; duman şekli 56 etiket 0 fark |
| Documentation | **FAIL** | Rapor bazı iddialarda fazla iddialı (m-6, M-1, M-2) |
| Independent audit | **CONDITIONALLY ACCEPTED** | Bu belge |

## 2. Test sayıları (çalıştırıldı)

| Komut | Proje | Toplam | Geçen | Kalan | Atlanan | Inconclusive | Süre | Ortam |
|---|---|---|---|---|---|---|---|---|
| `dotnet test -c Release --no-build` (parallel kapalı, a00861c temiz ağaç) | `Namines.Tests` | 3123 | 3123 | 0 | 0 | 0 | 18 dk 45 sn | Docker, PG17; TRX sayaçları aynı |
| Aynı | `Namines.Tests.RunTests` | 19 | 19 | 0 | 0 | 0 | 1 dk 8 sn | Aynı |
| `smoke-d1.js` (uygulayıcının betiği, gerçek API) | `namines_app` rolü, 14.2C'den yükseltilmiş DB | 95 | 95 | 0 | | | | PG16 |
| Aynı | Eski `roles.sql` ile kurulu DB (`ops` şeması roles'te yok) | 95 | 95 | 0 | | | | PG16 |
| Aynı | Geri alınmış DB, `f5fcd96` ikilisi | 95 | 95 | 0 | | | | PG16 |
| `dotnet-ef migrations has-pending-model-changes` | 12 bağlam | 12 | 12 "No changes" | 0 | | | | |

Frontend, CLI ve MCP duman testleri yeniden çalıştırılmadı (§8). `git diff f5fcd96..a00861c -- frontend services` boş.

## 3. Çalıştırılarak doğrulananlar

**Migration (gerçek ikili, `--migrate`).** Kendi karışık verimi ekledim: Türkçe karakter, NULL, 100 KB JSON, süresi dolmuş ve iptal anahtar, IPv6 CIDR, proje dışı denetim satırı, farklı org yazılmış Vault/Ground satırı, üst dallı branch, ChangeRequest. Uygulayıcının `seed-14-2C.dump` dosyası zayıf: `CrossDatabaseRelations`, `GatewayTablePermissions`, Vault ve Ground boş, tek allowlist satırı var.

| Senaryo | Sonuç |
|---|---|
| 14.2C'den Up: 55 tablo, her biri `to_jsonb` md5 (bilinçli değişen kolonlar hariç) | Birebir aynı |
| Down (rapordaki sıra: Ops, Gateway, Hub, Studio, Auth), tekrar Up | Veri birebir; katalog başlangıca döndü (yalnız boş geçmiş tabloları kalır) |
| Yeni ikilinin yazdığı satırlarla Down, sonra `f5fcd96` ikilisi | Veri aynı, duman 95/95 |
| Boş DB'den Up ile yükseltilmiş DB katalog farkı (FK, kısıt, indeks, tetikleyici, varsayılan, fonksiyon) | 0 fark |
| Proje dışı etiket bağı, Hub RAISE | Auth 56, Studio 1, Hub 0; düzeltip yeniden: tamam |
| Yarım DB'de açılış | Production `InvalidOperationException` ile reddetti; Development uyarıyla açılır (bilinçli) |
| Üç `--migrate` eşzamanlı (14.2C), iki eşzamanlı (boş) | Hepsi rc=0, geçmiş tek, veri aynı |
| Süreç `SIGKILL` (Auth 34/56'da), yeniden çalıştırma | Tamam |
| Açık işlem varken `lock_timeout` | 32 sn'de `55P03`, Ops bekler; yeniden çalıştırma tamam |
| Hacim (200 bin issue, 200,5 bin bağ, 200 bin sürüm, 200 bin denetim) | 4,99 sn, veri aynı |
| Devir migration'ları | `Up`/`Down` boş (okudum); 12 bağlam "No changes" |

**Katalog.** Şemalar arası FK 33, şema başına gateway 6, hub 12, ops 1, studio 13, public 25: rapor doğru. `ON DELETE` davranışı taşımadan önceyle aynı; yalnız iki etiket FK'si bileşik oldu. `convalidated=false` yok, FK kolonu indekssiz yok. Tek kullanıcı fonksiyonu `hub.issue_label_gc`: gövdesi nitelikli, `SET ROLE namines_app; SET search_path=''` altında tetikleyici çalıştı, etiket toplandı. `Feedbacks_Id_seq` tabloyla taşındı, `namines_app` kullanabiliyor; görünüm yok. Kod ve deploy betiklerinde eski tablo adıyla ham SQL bulunmadı.

**Yetki matrisi (`aud/matrix.js`).** Controller'lardaki 212 HTTP özniteliği (hesap silme, oturum, webhook, voice, docker atlandı) anon, yabancı, viewer rolleriyle, GET'ler ayrıca editor ve owner ile; hem `f5fcd96` hem `a00861c`. 829 karşılaştırma (durum, owner GET yanıt şekli) 0 fark. Yabancı için başka kiracının projesinde 2xx yok. Admin uçları anon 401, yabancı ve viewer 403; `Admin` rolü ve `IsDev` kullanıcıyla olumlu akış geçti. Bir ilk koşumum `revoke-all-sessions` yüzünden geçersiz çıktı, o uçları atlayıp yeniden koştum; rakamlar geçerli koşumdan.

**Atomiklik (`aud/atomic.js`, gerçek tetikleyiciyle hata enjeksiyonu).** Beş akışta ikinci modül yazımı düşürüldü, hepsi geri alındı: sync (repo satırı düşer, proje yok), ayar (ad geri), fork (proje, branch, sayaç yok), proje silme (repo, issue, etiket, anahtar yerinde), hesap silme (yıldız, sayaç, geri bildirim yerinde). `ICrossModuleTransaction` gerçekten tek işlem.

**Silme akışları.** Tüm modüllerde dolu proje silindi: 13 sayaç 0, çocuk yetim yok, fork bağı NULL. Hesap silmede devir: proje ve Hub sahibi varise, slug çakışması çözüldü, anahtar iptal ve yaratıcı NULL, issue, yorum, branch, sürüm yazarı NULL, yıldız ve sayaç düştü, geri bildirim anonim. Silme ile yazma yarışı (issue, yorum, yıldız, commit, anahtar, izin, paylaşım, fork; 15 tur): 5xx yok, kalan satır 0, deadlock 0. BL-8: Vault ve Ground'da `OrganizationId` okuyan kod yok (grep), yetki `studio.projects`'ten.

**Eşzamanlılık.** 12 eşzamanlı issue: hepsi 201, numaralar tekil. Yıldız ve fork sayaçları satırla eşit. Aynı slug: 15/15 `200/409`. Dal satırı kilidi yalnız `FOR NO KEY UPDATE`, kısa; danışma kilidi `xact`, uzun iş altında tutulmuyor (okudum).

**Performans.** İstek başına SQL (`log_statement=all`, 20 issue, 4 yazar): issue listesi 6→7, yorum 3→4, repo 13→14, Explore 5→4, proje listesi 4→4, commit 5→9 (kilit ve işlem), fork 17→21. N+1 yok; sayı satır sayısıyla büyümüyor. EXPLAIN ANALYZE hacimde: nadir etiket sayfası 6,3 ms, yaygın etiket sayımı 19,1 ms, Explore 0,48 ms, proje listesi 0,97 ms, anahtar önek 0,04 ms. 3 yeni bileşik indeks uygun.

**Mimari kapsam.** Modüller `Core` ve `BuildingBlocks` dışına başvurmuyor; bağlamlar internal, `InternalsVisibleTo` yalnız testler. Kaynak taramasında başka modülün tablosuna ham SQL yok, `AuthDbContext` modül içinde yalnız yorumda. İzin listesindeki 12 controller'ın `DbSet` kullanımını saydım: yalnız Identity, Billing, Governance setleri. Kaldırılan testler: 4 metot gerçekten yok, kapsamı `Phase14_2D1ModuleArchitectureTests` ve `Deleting_project_cascades_to_flow_and_desk_rows_with_ddl_in_auth_chain` ile sürüyor; 7 satır yeniden adlandırma. Yeni `Skip` yok.

## 4. Bulgular

### Blocker

**B-1. Repo ayarı kaydı eşzamanlı şema kaydını sessizce eziyor (yeni regresyon, veri kaybı).**
`backend/Namines.Hub/Application/HubRepositoryService.cs:321,360`, `backend/Namines.Studio/Application/StudioProjectService.cs:59-67`.
`UpdateSettingsAsync` projeyi izlemesiz okur, `SaveAsync`'e bayat `SchemaJson`, `NodePositionsJson`, `OrganizationId` ile `ExpectedRowVersion` vermeden yazar. `SaveAsync` satırı yeniden okuduğu için `xmin` taze, çakışma görünmez, eski içerik yazılır. Tabanda varlık izleniyordu, yalnız değişen kolon yazılıyor, çakışma `xmin` ile yakalanıyordu.

Yeniden üretim: `aud/probes.js lost`, gerçek API, her tur yeni kullanıcı ve proje; `POST /auth/sync` (`rowVersion` ile) ve 0-6 ms sonra `PATCH /hub/repos/{id}/settings {description}`. İkisi de 200 döndüğünde DB'deki `SchemaJson` sync'in gönderdiği mi diye bakılır.

| İkili | İkisi 200 | Şema kayboldu |
|---|---|---|
| `a00861c` | 23, 59, 39 (üç koşum) | 2, 13, 10 |
| `f5fcd96` | 53, 34 (iki koşum) | 0, 0 |

Etki: kullanıcı 200 alır, şeması kaybolur; aynı yol taşınmış projenin org kaydını da geri alabilir. Düzeltme: `SaveAsync`'e `project.RowVersion` geçip 409'a düşürmek ya da ayar yolunda yalnız değişen alanı yazan ayrı sözleşme. Gerçek PG'de yarış testi şart.

### Major

**M-1. Gateway veri düzleminde yetki kararları hâlâ controller'da.**
`backend/Namines.API/Controllers/GatewayController.cs:220-268,298,1658-1680,1902-1912`; `backend/Namines.Infrastructure/Services/GatewayService.cs`; `backend/Namines.Gateway/Application/GatewayAccessService.cs` sınıf notu.
Spesifikasyon: "authorization not only at controllers". Anahtar yönetiminde karşılanıyor (`GatewayKeyService` kendi soruyor, mutasyonla sınadım). Ama anahtar doğrulama, origin, IP, hız sınırı, tablo izni birleşimi ve maskeleme controller'da, üç ayrı kopya. `IGatewayAccess.AuthenticateAsync` çağıran başka bir yol origin, IP, hız sınırını kendiliğinden almaz. Maskeleme (`MaskAsync`, `MaskValues`) ve SQL yürütme de modül dışında. Sınıf notu "Karar burada; controller yalnızca sonucu HTTP'ye çevirir" diyor, doğru değil. Davranış tabanla aynı; bugün sömürülebilir açık bulmadım.
Yeniden üretim: okuma; `grep -n 'IsOriginAllowed\|IsIpAllowed\|TryAcquireAsync' GatewayController.cs` üç yer gösterir, modülde tek birleşik kapı yok.

**M-2. Studio proje yaşam döngüsü izin listesindeki controller'larda.**
`backend/Namines.API/Controllers/AuthController.cs:430-525`, `TeamController.cs:420,450`, `ProjectMemberController.cs:67`, `Phase14_2D1ModuleArchitectureTests.cs:30-41`.
Sorunun cevabı: `DbContext` kullanımı açısından liste doğru, ama D-1 işi listede saklanıyor. Proje oluşturma ve güncelleme (`/auth/sync`: yetki kararı, sürüm çakışması, kimlik üretimi, işlem) `AuthController`'da, listede "D-2 Identity" diye duruyor. `IStudioProjects.SaveAsync` ve `SetOrganizationAsync` aktör almıyor, yetki controller'da. Spesifikasyon Studio'ya "project lifecycle" diyor ve her yeni Application servisi için yetkisiz senaryo istiyor. Rapor "hepsi D-2" diyor; bu eksik söylem. Düzeltme: aktörlü `IStudioProjectLifecycle` (sync ve taşıma), `AuthController` yalnız HTTP.
Yeniden üretim: okuma; `SyncProjects` gövdesi.

### Minor

| ID | Bulgu | Yer | Yeniden üretim |
|---|---|---|---|
| m-1 | Mimari testler üç sınır aşımını görmüyor. Koddaki gerçek ihlal değil, koruma eksiği | `Phase14_2D1ModuleArchitectureTests.cs:440-459` | Aynı anda uyguladım: `OpsAdminService`'te `SELECT ... FROM gateway."GatewayApiKeys"`, `HubDbContext`'te `studio."Branches"` eşleyen `ExcludeFromMigrations` varlık, API'de ham `NpgsqlCommand` ile `hub."RepoIssues"`. `Architecture` + `MigrationHandoffTests`: 148/148 geçti. Düşen kontrol: `OpsDbContext` public ve `RevokeAsync`'ten proje koşulunu silmek ayrı ayrı test düşürdü |
| m-2 | `roles.sql` sahiplik devri listelerinde `ops` yok. Sonradan rollere geçen kurulumda `ops` tabloları eski sahibinde kalır, sonraki Ops migration'ı `namines_migrator` ile düşer | `deploy/sql/roles.sql:66,69` | Süper kullanıcıyla migrate, sonra `roles.sql`: `ops` sahibi `postgres`, diğer üç şema `namines_migrator` |
| m-3 | Anahtar döndürme atomikliğini kaybetti: iptal ve yeni anahtar iki ayrı commit. İkincisi düşerse eski anahtar iptal, yenisi yok. Tabanda tek `SaveChanges` | `GatewayKeyService.cs:329-332` | Okuma, çalıştırılmadı |
| m-4 | BL-7 yarım: API `closedOldestLinks` döner, frontend okumaz, kullanıcı hâlâ habersiz | `ShareController.cs:54-56`, `frontend/services/api.ts:960`, `ToolbarPanel.tsx:203` | Okuma |
| m-5 | Ölü kod: `MoveToOrganizationAsync`, `SetAutoApproveAsync` üretimde çağrılmıyor, testleri var. Taşımada hedef org üyeliği sorulmuyor; bağlanırsa IDOR olur | `ProjectWorkspaceService.cs:65-82` | `git grep` |
| m-6 | Rapor §5 "ham SQL yalnız `ExecuteSqlInterpolatedAsync` (2 kilit)": üçüncü ham SQL var. Parametreli ve kendi şeması, açık değil | `GatewayAuditLog.cs:171` | Okuma |
| m-7 | Denetim ön izlemesi yalnız anahtar kelimeden hemen sonra tırnaklı değeri maskeliyor. `dblink_connect('host=h password=hunter2')` ve `SET app.key = 'hunter2'` açık kalır; `Error` hiç maskelenmez. Rapor "sır benzeri literal maskeleniyor" diyor | `SqlExecutionAuditService.cs`, `SecretLiteral` | Aynı deseni örneklerle koşturdum |
| m-8 | GC tetikleyicisi satır başına: 50.500 bağ silmek 11,5 sn. Doğrusal varsayımla 200 bin bağ ~46 sn, `CommandTimeout` 30 sn; proje silme zaman aşımına düşebilir. 14.2C'den beri var | `HubApplicationService.cs:390-398`, `hub.issue_label_gc` | Hacim DB'sinde işlem içinde `DELETE`. 200 bin ölçülmedi |
| m-9 | Vault, Ground, Flow, Desk için faz sahibi yok | `AuthDbContext.cs:286-292`, rapor §10 | Aşağıda |

**Vault/Ground/Flow/Desk kapsamı.** D-1 spesifikasyonu yalnız Studio, Hub, Gateway, Ops diyor; bu yüzden zincirin Auth'ta kalması ve servislerin somut bağlam kullanması D-1 Blocker'ı değil. Ama D-2 kabulü "AuthDbContext holds ONLY Identity entities" diyor ve bu tipler Auth modelinde duruyor; "Application no Infrastructure" kuralı da mimari testte yalnız dört modüle uygulanıyor. Hiçbir faz bunu üstlenmiyor. Rapor §10 yalnız not düşmüş: sahip ve faz yazılmalı, D-2 kabulünü doğrudan etkiler.

**"NOT DONE" listesi (§10).** D-2 tabloları, `ChangeRequests` FK kararı, üretimde çalıştırılmama, zincir başına kilit ölçümü: D-1 kabulünü engellemez. Durdur-migrate-aç gereği belgeli; `MigrateOnStartup` varsayılanı açıkken eski örnek ayaktaysa o örnek sorgu hatası alır (fail-closed).

## 5. Yalnız okunanlar

Gateway servisleri, Hub ve Studio servisleri, Ops servisleri, `SharedTransactionManager`, orkestratör, migration kaynakları, `ChangeRequestController` ve diğer controller diff'leri, yeni test isimleri. Application testlerinin yedi kategori kapsamını (başarı, hata, yetkisiz, geçersiz, yinelenen, eşzamanlılık, idempotency) yalnız test adından değerlendirdim; her servis için tam değil, B-1'in yakalanamaması bunun örneği.

## 6. Doğrulanamayanlar

- Üretim veritabanı, gerçek kilit süresi yük altında (raporun 2,9 sn'sini yeniden ölçmedim).
- Soğuk Oracle (BL-9), Redis'li dağıtık hız sınırı, Stripe, gerçek Vault blob ve Ground sağlayıcısı.
- Feature flag önbelleğinin çok örnekli davranışı, PgBouncer altında oturum danışma kilidi (depoda PgBouncer yapılandırması görmedim).
- Matris editor ve admin için yazma uçlarını kapsamadı; hesap, oturum, webhook, voice, docker uçları atlandı.
- Frontend, `services/desk`, CLI ve MCP dumanı yeniden çalıştırılmadı. Kaynak diff'i boş, tek ek alan koşullu `closedOldestLinks`; frontend'in `api.ts:960` onu yok sayar.
- `namines_analyze_impact`, `namines_prove_migration`: sunucu bağlanmadı.
- Tam süit benim senaryolarımla aynı makinede koştu; süre karşılaştırma değeri taşımaz, sonuç taşır.

## 7. Yeniden denetimde bakılacaklar

B-1 için gerçek PG yarış testi ve aynı probun 0 kayıp vermesi; M-1 için tek modül kapısı ve onu çağırmayan veri düzlemi ucunun testte düşmesi; M-2 için aktörlü Studio servisi ve yetkisiz testleri; m-1 için SQL metni ve model tablo sahipliği taraması.
