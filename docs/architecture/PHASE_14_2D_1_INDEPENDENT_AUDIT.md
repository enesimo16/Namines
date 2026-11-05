# Faz 14.2D-1 Bağımsız Denetim

> Kapsam: `arch/phase-14-2`, `f5fcd96..a00861c` (1. tur) ve `c736d90..ca19825` (2. tur, §8) ve `155a4b7..3558d0f` (3. tur, §9). Denetçi kodu yazmadı, kod değiştirmedi; yalnızca bu belge eklendi.

## Son karar (3. tur): ACCEPTED

3. turda R2-M1 ve 2. turun Minor maddeleri kapandı; gerçek PG'de yeniden üretilerek doğrulandı. D-1 kapsamında açık madde yok (§9).

## 2. tur kararı: CONDITIONALLY ACCEPTED

1. turun bütün bulguları kapandı ve çalıştırılarak doğrulandı (§8). Ama 2. turda gelen `Hub_02` yeni bir Major açtı (R2-M1): belgeli "mevcut kurulum sonradan rollere geçer" yolunda `--migrate` `42501` ile düşüyor. Düzeltme küçük; aynı fazda yapılıp yalnız bu madde yeniden doğrulanınca faz ACCEPTED olabilir. Aşağıdaki 1. tur metni tarihsel kayıttır.

## 1. tur kararı: CONDITIONALLY ACCEPTED
> Ortam: Windows 11, .NET 8 SDK, Docker. Test harness `postgres:17-alpine` (Testcontainers); senaryolar `namines-verify` (`postgres:16-alpine`). `namines_*` MCP araçları `CONNECT_TIMEOUT`, yerine gerçek PostgreSQL kullanıldı.

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

## 8. 2. tur (`c736d90..ca19825`)

Yöntem 1. turla aynı: `ca19825` için ayrı worktree, temiz derleme, aynı problar hem `f5fcd96` hem yeni ikiliyle. Uygulayıcının sayılarına değil kendi koşumlarıma dayanıyor.

### 8.1 Faz sonu tablosu (2. tur)

| Alan | Sonuç | Kanıt |
|---|---|---|
| Build | PASS | `--no-incremental` Release: 0 uyarı, 0 hata, 34,5 sn |
| Unit | PASS | Tam süit içinde; B-1 ve org taşıma için yeni testler mutasyonla düştü (§8.4) |
| Integration | PASS | `Namines.Tests` 3166/3166; gerçek PG yarış testi `Phase14_2D1LostUpdateTests` |
| Architecture | PASS, dar boşlukla | 5 mutasyonun 5'i düştü, 1 kaçtı (R2-m2) |
| Migration | **FAIL** (R2-M1) | Boş DB, süper kullanıcı yükseltmesi, Down/Up temiz; rollere sonradan geçen yükseltme `Hub_02`'de düşüyor |
| Data integrity | PASS | B-1 probu 0/203 kayıp; Up, `Hub_02` Down, yeniden Up, tam Down ve Up: veri birebir |
| Security | PASS | 829 karşılaştırma 0 fark; Gateway kapısı tüm veri düzlemi uçlarında |
| API compatibility | PASS | 829 durum/şekil 0 fark; sync 409 gövdesi tabanla aynı; frontend `tsc` 0 hata |
| Documentation | PASS | 1. tur doküman bulguları düzeltildi; m-9 D-2'ye atandı |
| Independent audit | **CONDITIONALLY ACCEPTED** | R2-M1 aynı fazda |

### 8.2 Test sayıları (2. tur, çalıştırıldı)

| Komut | Proje | Toplam | Geçen | Kalan | Atlanan | Inconclusive | Süre | Ortam |
|---|---|---|---|---|---|---|---|---|
| `dotnet test -c Release --no-build` (paralel kapalı, `ca19825` temiz ağaç) | `Namines.Tests` | 3166 | 3166 | 0 | 0 | 0 | 23 dk 38 sn | Docker, PG17. Süre benim problarımla aynı anda koştuğu için uzun |
| Aynı | `Namines.Tests.RunTests` | 19 | 19 | 0 | 0 | 0 | 1 dk 37 sn | Aynı |
| `npx tsc --noEmit` | `frontend` | | | 0 hata | | | | `ca19825` kaynağı |
| `npx vitest run lib/shareLinkMessage.test.ts` | `frontend` | 2 | 2 | 0 | 0 | | | Tam frontend süiti koşulmadı |
| `smoke-d1.js` | `namines_app`, 14.2C'den yükseltme, fonksiyon sahibi elle düzeltildikten sonra | 95 | 95 | 0 | | | | PG16 |

### 8.3 1. tur bulgularının doğrulaması

| ID | Sonuç | Kanıt |
|---|---|---|
| B-1 | Kapandı | `probes.js lost` yeni ikiliyle üç koşum: ikisi 200 dönen 64, 69, 70 durumda **0** kayıp (toplam 0/203). Ayar artık `RenameAsync` ile yalnız ad ve motor yazıyor; `SaveAsync` sürümsüz güncellemeyi reddediyor. Round-1 davranışını geri getiren mutasyon 3 test düşürdü |
| M-1 | Kapandı | `GatewayController` 1920'den 1505 satıra indi. Anahtar, köken, IP, hız sınırı, tablo, expand, dışa aktarım, ham SQL ve maske `IGatewayRequestGate`'te; controller'da üç kopya yok. Matris 0 fark. Kalan: R2-m1 |
| M-2 | Kapandı | `IProjectLifecycleService` aktörlü. Sync, paylaşım, paylaşımı geri alma, tembel org taşıma, otomatik onay oradan. Tabanla aynı probda 9 satırın 9'u aynı (§8.5) |
| m-1 | Kapandı, dar boşlukla | Benim kaçan üç mutasyonumun SQL biçimleri artık düşüyor (§8.4). Kalan: R2-m2 |
| m-2 | Kapandı (tablo/dizi) | Rollere sonradan geçişte `ops` tablo ve dizileri `namines_migrator`'a geçti. Fonksiyon sahipliği eksik: R2-M1 |
| m-3 | Kapandı | Döndürme tek işlemde (okudum); uygulayıcının tetikleyiciyle testi süitte geçti |
| m-4 | Kapandı | `ToolbarPanel` `closedOldestLinks` için `warning` gösteriyor; `tsc` 0, test 2/2 |
| m-5 | Kapandı | Ölü metotlar silindi |
| m-6 | Kapandı | Rapor envanteri 3 ham SQL yeri diyor |
| m-7 | Kapandı (okudum) | `SecretPair` ve `SessionSet` desenleri ile `Error` maskesi eklendi; testler süitte |
| m-8 | Kapandı | §8.6 |
| m-9 | Kapandı | Sahibi Faz 14.2D-2 (rapor §10, `IMPLEMENTATION_STATUS`) |

### 8.4 Mutasyonlar (her biri tek başına, derle, filtreli test, geri al)

| Mutasyon | Sonuç |
|---|---|
| Ops'ta dize birleştirerek `hub."RepoStars"` ham SQL | Düştü: `Sql_text_never_names_another_modules_table(Namines.Ops)` |
| Gateway'de interpolasyonla `studio.project_connections` okuma | Düştü: aynı test (Gateway) |
| Yeni controller, anahtarı `IGatewayAccess` ile kapısız doğruluyor | Düştü, ama yalnız API sözleşme anlık görüntüsüyle (yeni rota) |
| B-1'i geri getir: `SaveAsync` sürümsüz kabul, ayar tam `SaveAsync` | Düştü: 3 test (gerçek PG yarış testi dahil) |
| Hedef org üyeliği kontrolünü kaldır | Düştü: `Sharing_into_an_organization_the_actor_is_not_in_is_refused` |
| Var olan bir eylemde (`FeedbackController.Submit`) kapısız `IGatewayAccess.AuthenticateAsync` | **Kaçtı**: `Architecture` 153/153 geçti (R2-m2) |

Her mutasyondan sonra `git checkout`; temiz derleme 0 uyarı, 0 hata.

### 8.5 Regresyon taraması (çalıştırıldı)

- **Yetki matrisi.** Taban ve yeni ikili, 212 eylem, 829 karşılaştırma: 0 fark. Gateway oturum yolları ve ham SQL hakları dahil (yabancı ve viewer durumları aynı).
- **Sync ve lifecycle probu (`aud/r2.js`), taban ve yeni ikili, çıktı birebir aynı:**
  - eski sürümle sync: 409, gövde anahtarları `changedAt,currentVersion,message,projectId,projectName,yourVersion`;
  - aynı yeni kimlikle 4 eşzamanlı sync: `[200,409,409,409]`, 1 satır;
  - sürümsüz istemci sync'i: 200;
  - yabancının başkasının kimliğiyle sync'i: kopya oluşur, sahibin projesi değişmez;
  - ekipsiz kullanıcının ekip paylaşımı: 200 (kişisel org);
  - üye olunan ekibe paylaşım: 200, taşındı;
  - ekip sahibinin geri alması: 200, kurucunun org'una döndü;
  - otomatik onay: sahip 200, yabancı 404;
  - ayarda ad ve motor değişikliği: şema korunur, Hub okuma modeli güncel.
- **Hedef org sıkılaştırması.** Tek üretim çağıranı `TeamController.ShareProject`; hedef org `ActiveOrgAsync` ile çağıranın üyeliklerinden (yoksa kişisel org) seçiliyor. Gerçek frontend akışı bu kontrole takılamaz; probda iki durum da 200.
- **Atomiklik, silme yarışı, hesap silme devri.** 1. turun problarıyla yeniden koştu, sonuçlar aynı: 5 enjeksiyon geri alındı, 10 turda 5xx yok, kalan satır 0, deadlock 0, devir doğru.

### 8.6 Hub_02 (deyim düzeyi GC)

- **Gerçek veriyle Down ve Up** (karışık 14.2C tohumu):
  - Up: veri birebir, tetikleyici `STATEMENT`;
  - `Hub_01`'e Down: veri birebir, tetikleyici `ROW`, satır düzeyi GC çalışıyor;
  - yeniden Up: veri birebir, deyim düzeyi GC çalışıyor;
  - tam Down: katalog başlangıçla aynı (yalnız fonksiyon gövdesindeki yorumlar düşüyor), sonra tekrar Up;
  - boş DB ile yükseltilmiş DB: katalog 0 fark.
- **Eşzamanlılık ve projeler arası durumlar** (`aud/gc2.sh`, iki psql oturumu):
  - tek deyimde iki projenin bağları silindi: yalnız artık bağı kalmayan etiketler gitti;
  - issue cascade ve proje cascade: doğru;
  - iki işlem aynı etiketin iki bağını eşzamanlı sildi: etiket gitti;
  - commit edilmemiş yeni bağ varken son eski bağ silindi: etiket kaldı, bağ korundu;
  - GC'si süren etikete bağ ekleme: FK hatası (satır düzeyiyle aynı, sessiz kayıp yok);
  - ters sırada çakışan çok etiketli silmeler: deadlock 0.

### 8.7 Yeni bulgular

**R2-M1 (Major): rollere sonradan geçen kurulumda yükseltme `Hub_02`'de düşüyor.**
`backend/Namines.Hub/Infrastructure/Persistence/Migrations/20261105192348_Hub_02_StatementLevelLabelGc.cs` (`CREATE OR REPLACE FUNCTION hub.issue_label_gc`), `deploy/sql/roles.sql` (sahiplik devri yalnız tablo ve dizi).
- **Neden:** `hub.issue_label_gc` fonksiyonunu 14.2C migration'ı oluşturuyor; sahibi o migration'ı çalıştıran rol. `roles.sql` tabloların ve dizilerin sahipliğini devrediyor, fonksiyonlarınkini değil. Belgeli geçiş sırası şu: `roles.sql`, sonra `ConnectionStrings__Migrations` (`namines_migrator`) ile migrate. Bu sırayla `Hub_02` `42501: must be owner of function issue_label_gc` alıyor.
- **Sonuç:** Hub `Hub_01`'de kalıyor, Gateway ve Ops çalışmıyor, Production açılışı bekleyen migration yüzünden reddediliyor. Fail-closed, veri kaybı yok, ama kesinti var. 1. turda aynı yol 95/95 geçiyordu; regresyon `Hub_02` ile geldi. Uygulayıcının m-2 testi bu yolu kapsamıyor.
- **Yeniden üretim:**
  1. 14.2C tohumunu süper kullanıcıyla geri yükle (fonksiyon sahibi `postgres`).
  2. Yeni `roles.sql`'i çalıştır.
  3. `namines_migrator` ile `--migrate`: rc≠0, `42501`; geçmiş tabloları studio 1, hub 1 (`Hub_01`), gateway ve ops yok.
  4. `ALTER FUNCTION hub.issue_label_gc() OWNER TO namines_migrator` sonrası `--migrate` rc=0 ve `namines_app` ile duman 95/95.
  5. Boş DB'de önce `roles.sql`, sonra migrator: sorun yok.
- **Düzeltme önerisi:** `roles.sql` modül şemalarındaki fonksiyonların sahipliğini de devretsin (ve bu yolu sınayan test); ya da `Hub_02` sahipliği ele alsın.

**R2-m1 (Minor): Desk SQL oturum yolunda Owner kontrolü controller'da kaldı.**
`GatewayController.cs:599` (`DeskSql`) ve `:787` (`RequireDeskSqlOwnerAsync`). Aynı kural kapıda da var (`GatewayRequestGate.AuthorizeStoredConnectionSqlAsync`, satır 255-265). Davranış aynı (matris 0 fark), ama kural iki yerde.

**R2-m2 (Minor): kapı taraması yalnız `GatewayController`'a bakıyor.**
`Phase14_2D1BoundaryScanTests.cs` (~139-150). Başka bir controller'ın var olan bir eyleminde `IGatewayAccess.AuthenticateAsync` ile kapısız anahtar doğrulama yakalanmadı. Koddaki gerçek ihlal değil. Öneri: `IGatewayAccess` dışarıya `internal` olsun ya da tarama tüm API derlemesini kapsasın.

**R2-m3 (Minor, faz dışı): `POST api/migration/generate` yabancı ve viewer için 500.**
- **Sınıflandırma:** yetki hatası değil. Anon 401 alıyor. Oturumlu her kullanıcı servise ulaşıyor ve AI sağlayıcısı yapılandırılmadığında `AiNotConfiguredException` atılıyor.
- **Neden 500:** `MigrationController.GenerateMigration` bunu kendi `catch`'inde `500` ve `ex.Message` olarak döndürüyor. Merkezi `ExceptionMiddleware` aynı istisnayı 503'e eşliyor, controller'ın `catch`'i bunu öne geçiriyor. Bu bir hata işleme kusuru.
- **Kapsam:** `f5fcd96`'da da aynı (matris 0 fark). `MigrationController` D-1'de değişmedi ve dört modülden birine ait değil.
- **Blokluyor mu:** hayır. Faz dışı backlog; sahibi AI/araçlar alanı.

### 8.8 2. turda doğrulanamayanlar

- Tam frontend süiti, ESLint, `next build`.
- CLI ve MCP.
- m-3 ve m-7 testlerini ayrı mutasyonla sınamadım; kodu okudum, testleri süitte geçti.
- Kilit süresi yük altında yeniden ölçülmedi.
- Üretim veritabanı.
- 1. turdaki diğer sınırlar geçerli (§6).

## 9. 3. tur (`155a4b7..3558d0f`) ve son karar

**Son karar: ACCEPTED.** D-1 kapsamında açık madde kalmadı. 2. turun R2-M1, R2-m1, R2-m2 ve R2-m3 maddeleri kapandı; hepsini `3558d0f`'nin temiz derlemesinde kendim çalıştırarak doğruladım. m-9 (Vault/Ground/Flow/Desk) D-2'ye atanmış durumda; D-2 kabulünde kontrol edilmeli.

### 9.1 Faz sonu tablosu (3. tur)

| Alan | Sonuç | Kanıt |
|---|---|---|
| Build | PASS | `--no-incremental` Release: 0 uyarı, 0 hata, 33 sn |
| Unit | PASS | Tam süit içinde |
| Integration | PASS | 3184/3184; geç rollere geçiş için iki yeni gerçek PG testi |
| Architecture | PASS | Yeni testlere 3 mutasyon düşürdü. Yansımayla çağrı mutasyonu kaçtı; bilinçli bir kaçış girişimi olduğu için bulgu sayılmadı (§9.4) |
| Migration | PASS | §9.2: geç rollere geçiş, guard, süper kullanıcı kurulumları, boş DB |
| Data integrity | PASS | Geç geçişte veri birebir; B-1 probu bir kez daha 0/64 |
| Security | PASS | Matris 829 karşılaştırmada 2 fark: ikisi de beklenen 500→503. Production'da yığın izi yok |
| API compatibility | PASS | Bilinçli tek fark: `migration/generate` 500 yerine 503 |
| Documentation | PASS | Runbook'a `roles.sql` adımı eklendi |
| Independent audit | **ACCEPTED** | |

### 9.2 roles.sql ve Hub_02 guard (gerçek PG, `aud/r3roles.sh`)

| Senaryo | Sonuç |
|---|---|
| A. 14.2C süper kullanıcıyla kuruldu (karışık tohum), sonra `roles.sql`, sonra `namines_migrator` ile `--migrate` | rc=0, veri birebir. Duman `namines_app` ile 95/95. GC tetikleyicisi `namines_app` rolüyle çalıştı |
| A'da `roles.sql` ikinci kez | rc=0 (idempotent); sahiplik ve yetkiler değişmedi |
| Sahiplik dağılımı (A, E, F) | Tablo, dizi, `hub.issue_label_gc`, ve `public` dışındaki şemalar `namines_migrator`'da. `public` `pg_database_owner`'da. `namines_app` hiçbir nesnenin sahibi değil |
| Karantina | `namines_app`'in `migration_quarantine` üzerinde USAGE yetkisi yok, okuyabildiği tablo 0 |
| B. Fonksiyonun sahibi bilerek `postgres`'e geri alındı | `42501`, mesaj `Re-run deploy/sql/roles.sql`. `roles.sql` yeniden çalışınca `--migrate` rc=0 |
| C. Boş DB, yalnız süper kullanıcı | rc=0, guard tetiklenmedi |
| D. 14.2C süper kullanıcı kurulumu, rol yok | rc=0, guard tetiklenmedi |
| E. Boş DB: önce `roles.sql`, sonra migrator ile migrate, sonra `roles.sql` yine | rc=0, rc=0; uygulama rolü yetkileri eksiksiz |
| F. Süper kullanıcıyla tam migrate, sonra `roles.sql`, sonra süper kullanıcıyla yeniden migrate | rc=0 |
| G. `pgcrypto` eklentisi ile kendi enum ve view'ım | Eklentiye ait fonksiyon `postgres`'te kaldı (doğru); enum ve view `namines_migrator`'a geçti |

### 9.3 Gateway ve 503

- **Yetki matrisi.** Taban ve yeni ikili, 829 karşılaştırma. Tek farklar `POST api/migration/generate` için yabancı ve viewer: 500→503. Uygulayıcının bildirdiğiyle aynı; başka fark yok.
- **Desk konsol probu (`aud/r3.js`), taban ve yeni ikili, çıktı birebir:** bağlantı yokken 400, Admin 403, yabancı 404, konsol kapalıyken 403, konsol açıkken sahip 200, konsol açıkken Admin 403.
- **503 gövdesi.** Development'ta `detailed` alanı yığın izini içeriyor; bu merkezi middleware'in var olan, Development'a özel davranışı. Production'da yığın izi yok ve gövde sabit bir metin: "Groq is not configured on this server...". Eski 500 yanıtı `ex.Message`'ı her ortamda döndürüyordu; yeni hâl daha az bilgi sızdırıyor.

### 9.4 Mutasyonlar (her biri tek başına, derle, filtreli test, geri al)

| Mutasyon | Sonuç |
|---|---|
| `IGatewayAccess` yeniden public + `FeedbackController.Submit` içinde kapısız `AuthenticateAsync` (2. turda kaçan mutasyon) | Düştü: 10 test (assembly başına tarama ve "internal kalmalı" kontrolü) |
| `GatewayController.DeskSql` içinde `GetRoleAsync` ile ikinci Owner kontrolü | Düştü: `Gateway_controller_leaves_owner_checks_to_the_gate` |
| API'den yansımayla internal `IGatewayAccess.AuthenticateAsync` çağrısı | Kaçtı (164/164). Testin kendi açıklaması bunu "ikinci kat" diye tanımlıyor; derleyici doğrudan kullanımı engelliyor. Yansımayla bilinçli kaçış kaza değil, kod incelemesinin konusu. Bulgu değil, not |
| `Hub_02` guard'ı kaldırıldı | Düştü: guard testi |
| `roles.sql` fonksiyonları devretmiyor | Düştü: 2 gerçek PG testi (geç rollere geçiş ve guard) |

Mutasyonlardan sonra çalışma ağacı temiz; temiz derleme 0 uyarı, 0 hata.

### 9.5 Sayılar (3. tur, çalıştırıldı)

| Komut | Proje | Toplam | Geçen | Kalan | Atlanan | Inconclusive | Süre | Ortam |
|---|---|---|---|---|---|---|---|---|
| `dotnet test -c Release --no-build` (paralel kapalı, `3558d0f` temiz ağaç) | `Namines.Tests` | 3184 | 3184 | 0 | 0 | 0 | 20 dk 33 sn | Docker, PG17; TRX sayaçları aynı |
| Aynı | `Namines.Tests.RunTests` | 19 | 19 | 0 | 0 | 0 | 1 dk 6 sn | Aynı |
| `smoke-d1.js` | `namines_app`, geç rollere geçişle yükseltilmiş DB | 95 | 95 | 0 | | | | PG16 |
| `probes.js lost` | Yeni ikili | 64 durumda ikisi de 200 | | 0 kayıp | | | | PG16 |

### 9.6 3. turda doğrulanamayanlar

- Frontend (bu turda frontend değişmedi; ESLint, vitest ve `next build` sonuçları uygulayıcının bildirimi, ben koşmadım).
- CLI ve MCP dumanı.
- Üretim veritabanı ve yük altında kilit süresi.
- 1. ve 2. turun §6 ve §8.8'deki sınırları geçerli.

### 9.7 Faz dışı notlar (blok değil)

- m-9: Vault, Ground, Flow, Desk Faz 14.2D-2'ye atandı. D-2 kabulünde "AuthDbContext yalnız Identity" maddesiyle birlikte denetlenmeli.
- `MigrationController` ve `ReverseEngineerController` diğer istisnalarda hâlâ `ex.Message` döndürüyor (D-1 öncesinden kalma, D-1 modülü değil). Backlog'a.
