# Faz 14.2D-2 Bağımsız Denetim

> Kapsam: `arch/phase-14-2`, `00c287b..1cc4ad8` (D-1 ACCEPTED tabanı ile D-2 sonu). Denetçi üretim kodu yazmadı ve değiştirmedi; yalnızca bu belge eklendi. Uygulayıcının raporu (`PHASE_14_2D_2_REPORT.md`) çalıştırılarak yeniden üretilmeden kabul edilmedi.

## Son karar (2. tur): CONDITIONALLY ACCEPTED

2. turda M-1..M-4 ve m-1..m-8 çalıştırılarak yeniden denetlendi (§10): hepsi kapandı ya da gerekçeli kabul edildi. Üç küçük madde açık (R2-m1 ters sıra kalıntısı, R2-m2 ayırt edici olmayan regresyon testi, R2-m3 eski runbook). Bütün seri değerlendirmesi §11'de.

## 1. tur kararı: CONDITIONALLY ACCEPTED

Faz kabul ölçütlerinin çekirdeği çalıştırılarak doğrulandı: `AuthDbContext` yalnızca kimlik varlıklarını taşıyor, başka hiçbir derleme ona erişemiyor, Stripe kolonları fiziksel düştü ve veri kayıpsız taşındı, oturumlar korundu, denetim izi tetikleyiciyle değiştirilemiyor, yetki matrisi tabanla birebir, tüm testler gerçek PG ile geçiyor. Ama gerçek PG'de kendi yazdığım problarla iki fatura doğruluğu hatası (M-1, M-2) üretildi, kota yarışı kabul ölçütü ana AI yolunda karşılanmıyor (M-3) ve controller'larda kimlik/yetki mantığı kaldı (M-4). Bunlar aynı fazda kapanmalı; yeniden denetimde yalnız bu dört madde ve belge düzeltmeleri bakılır. Veri kaybı, migration veya tenant izolasyonu bulgusu yok.

## 1. Faz sonu tablosu

| Alan | Sonuç | Kanıt |
|---|---|---|
| Build | PASS | `dotnet build backend/Namines.sln -c Release --no-incremental`: 0 uyarı, 0 hata, 33,4 sn |
| Unit | PASS | Tam süit içinde; yeni Application servis testleri çalıştı. Ama M-1/M-2 yarışlarını sınayan test yok |
| Integration | PASS | `Namines.Tests` 3331/3331 gerçek PG (Testcontainers PG17) |
| Architecture | PASS, dar boşluklarla | 5 kendi mutasyonumdan 2'si düştü, 3'ü kaçtı (m-2) |
| Migration | PASS | §3: boş DB, dağınık D-1 verisi, Down, Up, yinelenen müşteri, eşzamanlı, geç roller |
| Data integrity | PASS | §3: 57 tablo alan düzeyinde md5 birebir; Stripe alanları satır satır eşit (tek istisna m-3) |
| Security | **FAIL (koşullu)** | Matris 0 fark, IDOR yok; ama M-1, M-2, M-3 |
| API compatibility | PASS | 829 karşılaştırma 0 fark; 46 etiket yanıt şekli 0 fark; frontend dosyası değişmedi |
| Documentation | **FAIL (küçük)** | `IMPLEMENTATION_STATUS.md` bayat/çelişkili, rapor iki iddiada yanlış (m-4, m-5) |
| Independent audit | **CONDITIONALLY ACCEPTED** | Bu belge |

## 2. Test sayıları (çalıştırıldı)

Ortam: Windows 11, .NET 8 SDK, Docker; Testcontainers `postgres:17-alpine`; senaryolar `namines-verify` (`postgres:16-alpine`). `1cc4ad8` için ayrı temiz worktree, taban `00c287b` için ayrı worktree. Süit, benim PG problarım ve 1M kullanıcılık migration ölçümüyle aynı makinede koştu: süre karşılaştırma değeri taşımaz, sonuç taşır.

| Komut | Proje | Toplam | Geçen | Kalan | Atlanan | Süre |
|---|---|---|---|---|---|---|
| `dotnet test -c Release --no-build` (paralel kapalı) | `Namines.Tests` | 3331 | 3331 | 0 | 0 | 25 dk 56 sn |
| Aynı | `Namines.Tests.RunTests` | 19 | 19 | 0 | 0 | 1 dk 9 sn |
| `dotnet-ef migrations has-pending-model-changes` | 11 bağlam (Auth, Studio, Hub, Gateway, Ops, Billing, Governance, Vault, Ground, Flow, Desk) | 11 | 11 "No changes" | 0 | | uygulayıcının atladığı Studio/Hub/Gateway/Ops dahil |
| `smoke-d1.js` (gerçek API, `namines_app`, geç roller) | | 95 | 95 | 0 | | |
| `smoke-d2.js` (aynı, geç roller) | | 44 | 44 | 0 | | |
| `smoke-d1.js` taban ikilisi, boş D-1 DB | | 95 | 95 | 0 | | |
| `smoke-d2.js` taban ikilisi | | 44 | 44 | 0 | | |
| `shapecmp.js` taban ve yeni `smoke-d2` | | 46 etiket | | 0 fark | | |
| `aud/matrix.js` (220 etiket: rota x anon/yabancı/viewer/editor/owner) | taban `00c287b` ve yeni | 829 | | 0 fark | | |
| `npx tsc --noEmit` | frontend | | | 0 hata | | |
| `npx eslint .` | frontend | | | 0 hata, 188 uyarı | | uyarılar önceden var, frontend diff'i boş |
| `npx vitest run --maxWorkers=4` | frontend | 282 | 282 | 0 | 0 | 38,9 sn |
| `npx next build --webpack` | frontend | | | rc=0 | | |

TRX karşılaştırması (`r3-full.trx`, D-1 tabanı 3184 test, ile bu koşum): 3184 -> 3331, 152 eklendi, 5 silindi/yeniden adlandırıldı (hepsi mimari test, aşağıda m-5). Atlanan 0. Yeni `[Skip]`, `SkippableFact` ya da yorumlanmış assertion yok (test diff'inde arandı). Kaldırılan 53 `Assert` satırı, eklenen 455'in içinde yeni sözleşmelerle yeniden ifade edilmiş; zayıflatma bulunmadı (tek istisna: IL taramasında tasarım zamanı fabrikaları muaf, m-5).

## 3. Çalıştırılarak doğrulananlar

**Uç durum (reflection + katalog).** Taze DB'de `public` şeması yalnızca `AspNetRoleClaims, AspNetRoles, AspNetUserClaims, AspNetUserLogins, AspNetUserRoles, AspNetUserTokens, AspNetUsers, OrganizationMembers, Organizations, TeamInvites` tablolarını içeriyor. `AuthDbContext` `internal`, `InternalsVisibleTo` yalnızca test derlemeleri. Modül projeleri yalnızca `Core` ve `BuildingBlocks`'a başvuruyor; `Namines.Infrastructure` kompozisyon kökü. Controller'larda `DbContext` kullanımı yok; izin listesi boş. `ApplicationUser` üzerinde Stripe alanı yok; `AspNetUsers`, `Organizations`, `OrganizationMembers`, `TeamInvites` ham SQL dize taraması: kimlik dışı hiçbir derlemede yok. Bağlantı sırrı `studio.project_connections.EncryptedConnectionString` `v1:` şifreli (AAD `IConnectionSecretProtector`), paylaşım jetonu `project_shares.ShareTokenHash` 64 hex, API anahtarı `KeyHash`. `studio.projects` 10 kolon.

**Migration (gerçek `--migrate`, taban D-1 ikilisiyle üretilmiş veri).** Veri: taban ikilisinde `smoke-d1.js` + `smoke-d2.js` ile üretilen gerçek kullanıcı/proje/CR/kota, üstüne uygulayıcının `seed-d2.sql`'i ve benim eklediğim 5 kirli kullanıcı (boşluklu `' cus_ws '`, yalnız dönem sonu, boş string durum/plan, bilinmeyen plan kodu, iki kullanıcıda aynı `StripeSubscriptionId`).

| Senaryo | Sonuç |
|---|---|
| D-1 durumundan Up (20 kullanıcı, 7 org, 5 CR) | rc=0, 6,4 sn; 57 tablonun her biri `to_jsonb` md5: AspNetUsers (Stripe kolonları hariç) hash/stamp dahil, roller, claim, login, token, org, üyelik, davet, CR, onay, denetim, kota, politika, kullanım, webhook, outbox birebir |
| Stripe alanları satır satır | Stripe alanı olan 13 kullanıcıdan 12'si eşit; tek fark: `StripeCustomerId=''` olan kullanıcı müşteri tablosuna taşınmadı (m-3) |
| Down (belgelenen sıra: Desk, Flow, Ground, Vault, Governance, Billing, Auth `Phase14_2D1_DropModuleOrganizationCopies`) | tüm tablolar birebir, Stripe kolonları geri geldi; `''` -> `NULL` (m-3) |
| Yeniden Up | Aynı sonuç; yalnız yeni `CreatedAt/UpdatedAt` damgaları farklı (beklenen), Stripe alanları satır satır eşit |
| Taze DB ile yükseltilmiş DB katalog farkı (FK, kısıt, indeks, tetikleyici, varsayılan, fonksiyon, 485 satır) | 0 fark |
| Yinelenen `StripeCustomerId` (iki kullanıcı) | `Billing_01: the same StripeCustomerId belongs to more than one user (cus_1). Resolve the duplicates, then re-run.` rc!=0, `billing.customers` oluşmadı, 5 Stripe kolonu yerinde, hiçbir şey değişmedi; yinelen düzeltilince yeniden çalıştırma rc=0 |
| 3 eşzamanlı `--migrate` (D-1 durumu) | hepsi rc=0, sonuç tek süreçle aynı, `billing.customers` 8 |
| Geç roller (D-1 durumu -> `roles.sql` -> `--migrate` `namines_migrator` -> uygulama `namines_app`) | rc=0; tüm tablo/diziler migrator'a ait, `governance.change_request_audit_guard` ve `hub.issue_label_gc` sahibi migrator; `namines_app` hiçbir tabloda `SELECT/INSERT/UPDATE/DELETE` ya da dizide `USAGE` eksiği yok; smoke 95/95 ve 44/44 |
| 1M kullanıcı (333 bin Stripe müşterisi, 500 bin kullanım olayı) `--migrate` | rc=0, 28,9 sn duvar saati (süreç açılışı ve tam süit eş zamanlı); 333.341 müşteri, 333.344 abonelik satırı. Kilit penceresi izole ölçülemedi (§7) |

**Oturum sürekliliği.** Eski ikiliyle (`00c287b`) kayıt + giriş yapıldı, JWT ve `namines_token` çerezi dosyaya yazıldı; DB yükseltildi (taze `--migrate`); yeni ikili başlatıldı. Eski Bearer jeton ve eski çerez ile `/quota/status`, `/team`, `/usage`: hepsi 200; eski parolayla giriş 200. Uygulamada yenileme jetonu mekanizması yok (JWT + `sstamp` talebi + çerez), yani "refresh token" maddesi uygulanamaz. Kullanıcı satırlarına UPDATE yok, SecurityStamp/ConcurrencyStamp/hash md5 aynı.

**Denetim izi tetikleyicisi (`namines_app` rolüyle).** UPDATE ve DELETE `42501` ile reddedildi; TRUNCATE `permission denied` (hak yok + tetikleyici); `ALTER TABLE ... DISABLE TRIGGER` "must be owner"; yalnız `ActorUserId` NULL'lama izinli, `ActorUserId` NULL'lama + başka alan reddedildi; boştan kullanıcıya atama reddedildi; kullanıcı silinince aktör FK `SET NULL` ile boşaldı. İstisna: bkz. m-1.

**Webhook (gerçek API, gerçek PG, imzalı yük, `aud/wh.js`).** İmza yok/hatalı/biçimsiz/yanlış sır/eski zaman damgası: 400, DB'ye satır yok. Geçerli olay: 200, etkisi tek; yineleme: 200 `duplicate`; aynı olayın 30 paralel teslimi: 1 işlendi + 29 `duplicate`, tek satır. Yaşam döngüsü (active, past_due, deleted, ardından daha eski active): `active -> past_due -> canceled|Individual`, eski olay `stale`, durum canceled kalıyor. Bulgular: M-1, M-2, m-6.

**Governance (gerçek API, `aud/gov.js`).** 8 tur, 3 eşzamanlı oy (onay, onay, red): her turda tam bir kazanan, oy satırı 1, denetim `0,3` ya da `0,2` tutarlı. Aynı kullanıcının 10 paralel oyu: 1 x 200, 9 x 409, oy 1. Çözülmüş istek için çift onay/red: 409. Yabancı kullanıcı karar/detay/denetim/test koşusu/tarama/listeleme/otomatik-onay: hepsi 404. Üye olmayanın yabancı projede CR oluşturması: 404. Not: yazar, Safe riskli kendi CR'ını onaylayabiliyor (yalnız Destructive/Breaking için ayrı kişi şart; taban davranışı, kural belgeli).

**Identity (gerçek API).** Aynı projede iki sahibin karşılıklı düşürmesi ve karşılıklı çıkarması: her turda `409` + `200`, organizasyonda 1 sahip kalıyor (4 tur). Tek kullanımlık davet: 4 kullanıcı eş zamanlı kabul: `200,409,409,409`, üyelik +1, davet 1 satır; yeniden kabul 409; süresi dolmuş davet 409. Üyelik iptali anında etkili: üye kaldırılır kaldırılmaz proje üye listesi 200 -> 404. Hesap silme uçtan uca (proje, CR, yıldız, kota, politika, Billing müşteri/abonelik/kullanım, Desk kayıtlı sorgu/geçmiş/devir jetonu, Vault yedek, Ground kaydı): `DELETE /auth/me` 200; tüm şemalarda metin kolonu taraması: kullanıcı kimliğine kalan referans yalnız `governance.ChangeRequestAuditLogs.ChangeRequestId` (hesap silme denetim satırı, tasarım gereği), proje kimliğine kalan yalnız `ChangeRequestAuditLogs` (anlık kopya, tasarım gereği); Desk FK `CASCADE` ile gerçekten silindi; eski jeton ve parola sonrası 401. Şifre sıfırlama ve e-posta doğrulama uçları kodda yok (arandı): kapsam dışı. Organizasyon değiştirme ucu bulunmadı.

**Performans (EXPLAIN ANALYZE, 1M kullanıcı, 300 bin kota, 500 bin kullanım, 300 bin denetim, 200 bin webhook).** Üyelik rolü `IX_OrganizationMembers_UserId` 0,05 ms; atomik kota UPDATE `PK_UserAIQuotas` 1,05 ms; dönem özeti kompozit indeks 1,57 ms; denetim istek/proje `ChangeRequestId_CreatedAt`, `ProjectId_CreatedAt` 0,15/0,10 ms; webhook en yeni olay index-only 0,14 ms; müşteri araması 1,18 ms; yönetici 14 gün (166 bin x 3 satır) 68,9 ms index-only paralel. Seq scan yok. `DROP COLUMN` metadata işlemidir (satır yeniden yazımı yok), 1M kullanıcıda toplam süre 28,9 sn içinde.

**Mimari testlerde kendi mutasyonlarım (her biri tek başına, derle, `Architecture` süiti, geri al).**

| Mutasyon | Sonuç |
|---|---|
| `UsageController`'a `RequestServices.GetService(typeof(DbContext))` | Düştü: `Controllers_do_not_use_any_DbContext` (+ rota anlık görüntüsü) |
| `AuthDbContext`'te `Entity<DeskHandoffToken>()` (DbSet'siz) | Düştü: `AuthDbContext_model_contains_only_identity_entity_types`, `Every_context_has_no_pending_model_changes` |
| `ChangeRequestService`'te `billing."UserAIQuotas"` adını anan SQL dizesi (kullanılmayan sabit) | **Kaçtı** (187/187 geçti) |
| `UsageMeterService`'te `public."AspNetUsers"` anan interpolasyonlu SQL metodu | **Kaçtı** |
| `BillingCustomer` (Domain) üzerinde `[Microsoft.EntityFrameworkCore.Index]` | **Kaçtı** |

## 4. Bulgular

### Major

**M-1. Webhook: sonradan gelen ama daha geç zaman damgalı bir olay, daha eski `checkout.session.completed`'ı "stale" yapıp kullanıcıyı hiç bağlamıyor.**
`backend/Namines.Billing/Application/StripeWebhookService.cs:125-130` (`isLatest`), `:153`; eşleme yalnız checkout'ta kuruluyor (`:209`).
Müşteri->kullanıcı eşlemesini kuran tek olay `checkout.session.completed`. Aynı müşteri için `customer.subscription.updated` (eşleme yok, yan etkisiz ama kaydediliyor) önce gelirse ve zaman damgası daha yeniyse, sonra gelen checkout `stale` sayılıp uygulanmıyor: ödeme yapan kullanıcı hiç Corporate/abone olmaz, olay "işlendi" olarak kaydedildiği için Stripe yeniden de denemez.
Yeniden üretim (`aud/wh.js`, gerçek API ve PG, test sırrı): `cus_NEW` için `subscription.updated` @+50 sn -> 200; sonra `checkout.session.completed` @+40 sn (`client_reference_id` kullanıcı) -> `{"received":true,"stale":true}`, `billing.customers` satırı 0, kullanıcı `Type=0`.
Not: aynı `isLatest` mantığı tabanda (`StripeWebhookController.cs:167-202`) vardı, yani regresyon değil; ama D-2 kabulü "doğru abonelik yaşam döngüsü" ve "sıra dışı olaylar"ı istiyor ve mantık değişmeden modüle taşındı. Düzeltme: sıra korumasını yalnızca durumu yazan olay türleri ve yalnızca zaten eşlenmiş müşteri için uygula; eşleme (checkout) hiç stale olmasın; eşleme yoksa güncelleme olayını kaydetme ya da işlenmedi say (Stripe yeniden denesin).

**M-2. Webhook: aynı müşteri için farklı iki olay eşzamanlı gelirse durum zamana göre değil commit sırasına göre kalıyor; iptal geri alınabiliyor.**
`StripeWebhookService.cs:125-131` (sıralama kontrolü ile yazma arasında müşteri/abonelik satırı kilidi yok).
Yeniden üretim: `subscription.updated(active)` @t ile `subscription.deleted` @t+1 aynı anda (iki paralel istek), 25 tur: 10 turda son durum `canceled` değil (eski `active` sonradan yazıldı), kullanıcı ücretli kalıyor. İki istek de kendi işleminde "ben en yeniyim" görüyor; `billing.subscriptions` satır kilidi ikincisini bekletiyor ama ikincisi tekrar kontrol etmiyor. Etki: iptal edilmiş abonelik yarışta geri geliyor (ücretsiz erişim). Mevcut testler (`A_late_older_event_does_not_undo_a_newer_cancellation`) yalnız ardışık sırayı sınıyor. Düzeltme: işlemin başında müşteri satırını (ya da `pg_advisory_xact_lock(customer)`) kilitle ve kontrolü kilit altında yap; farklı olaylar için gerçek PG paralel testi ekle.

**M-3. Kota kabul ölçütü ana AI yolunda karşılanmıyor: `AIQuotaMiddleware` `CheckAsync` (okuma) sonra yanıttan sonra `ConsumeAsync`.**
`backend/Namines.API/Middleware/AIQuotaMiddleware.cs:154` (Check), `:177` (Consume); `AiQuotaService.cs` `ConsumeAsync` kullanıcı sayacını tavanda kırpıyor ama havuz ve ekip sayaçlarını sınırsız artırıyor.
`/api/aidba`, `/api/schema/revise`, `/api/coderai`, `/api/smartseed`, `/api/migration/*`, `/api/documentation/*`, `/api/reverseengineer`, `/api/voice`: bütçe, istek bitip 2xx dönünce düşülüyor. Bütçe tükenmeden önce N paralel istek hepsi `Allowed` görür; harcama N x tahmin olur (havuz ve ekip sayacı tavan üstüne çıkar, kullanıcı sayacı kırpılır ama sağlayıcıya gerçek maliyet gider). Atomik `TryReserveAsync` yalnız Gateway `/query/nl`, `SchemaController` (generate), Flow ve Governance açıklaması yolunda. Bu bir regresyon değil (middleware tabanda aynı), uygulayıcı "doğrulanmadı" diye listeledi; fakat spec "quota race-free (atomic)" ve "concurrent consumption test proves no overspend" diyor ve ana tüketim yolu bunu sağlamıyor.
Durum: AI sağlayıcı anahtarı yok, canlı olarak sömürülmedi; kod okumasıyla gösterildi (okundu, çalıştırılmadı). Gerçek PG testleri (`The_free_pool_is_never_overspent...`, `One_user_cap_is_never_exceeded...`, `The_team_pool_is_never_overspent...`) yalnız `TryReserveAsync` yolunu kanıtlıyor. Düzeltme: middleware'de `TryReserveAsync` ile rezerve edip yanıtta `ReconcileAsync`/`RefundAsync` (Schema yolunda zaten var) ya da bu uçlara eşzamanlı istek sınırı.

**M-4. Kimlik/yetki mantığı controller'larda kaldı (`UserManager` ile).**
`backend/Namines.API/Controllers/AuthController.cs` (41 `_userManager` çağrısı: kayıt `:137`, parola/MFA `:782-858`, OAuth kullanıcı oluşturma `:1336-1375`, SecurityStamp `:659`), `backend/Namines.API/Controllers/AdminController.cs:90-125` (yönetici yetkisi controller'da çözülüyor; "yapılandırılmış dev e-postası" bulunursa GET isteğinde `IsDev=true` yazıp `UpdateAsync` çağırıyor, hata yutuluyor).
Brief madde 5: "Business logic and authorization live in Application services, controllers only map to HTTP". `DbContext` kuralı sağlanıyor (izin listesi boş, mutasyonla doğrulandı), ama kayıt, MFA, parola ve admin yetki kararı `ApplicationUser`/`UserManager` ile `Namines.API` içinde. Rapor yalnız `AdminController`'ı anıyor; `AuthController`'ın boyutu (41 çağrı) belgelenmemiş. Güvenlik/veri açığı değil, tasarım sapması. Düzeltme ya da yazılı kabul: `IIdentityAdmin` yönetici kapısını, `IIdentityAuth`-benzeri bir Application servisi kayıt/MFA akışını taşısın; ya da rapora açık karar ("kimlik çerçevesi API'si controller'da bilinçli") yazılsın ve mimari test `UserManager` kullanımını sınırlasın.

### Minor

| ID | Bulgu | Yer | Yeniden üretim / kanıt |
|---|---|---|---|
| m-1 | "Değiştirilemez" denetim izi, uygulama rolü ele geçirilirse (SQL enjeksiyonu, sızan bağlantı) aşılıyor: saklama istisnası düz bir oturum ayarı, `namines_app` yazabilir | tetikleyici `20261107113350_Governance_01_AdoptTables.cs:41`, `GovernanceServices.cs:164` | `namines_app` ile `BEGIN; SET LOCAL namines.audit_retention='on'; DELETE FROM governance."ChangeRequestAuditLogs" WHERE "Id"='al_2';` -> `DELETE 1`. Kod hatasına karşı koruma gerçek, kötü niyetli rol için değil. Düzeltme: saklama silmesini `SECURITY DEFINER` işleve (ayrı sahip) ya da ayrı role taşı |
| m-2 | D-1'in SQL-metin ve "tablo yalnız sahibinde eşlenir" sınırı Billing/Governance/Identity'ye genişletilmedi: `Owners` yalnız Studio/Hub/Gateway/Ops, taranan derlemeler Billing/Governance/Identity'yi içermiyor; Domain-EF testi öznitelikleri görmüyor. Koddaki gerçek ihlal yok (elle arandı), koruma eksik | `Phase14_2D1BoundaryScanTests.cs:22-28,92-100`, `Phase14_2D1ModuleArchitectureTests.cs:308-320` | §3 mutasyon tablosu, son üç satır |
| m-3 | Veri kenar durumları: `StripeCustomerId=''` taşınmıyor ve Down'da `NULL` oluyor; boşluklu kimlik (`' cus_ws '`) kırpılmadan taşınıyor, webhook eşleşmesi onu hiç bulamaz; `StripeSubscriptionId` indeksi benzersiz değil (iki kullanıcıda aynı `sub_1` kabul edildi). Önceden bozuk veri, kayıp yok | `Billing_01_AdoptTables.cs:58`, `IX_subscriptions_StripeSubscriptionId` | §3 migration tablosu |
| m-4 | `IMPLEMENTATION_STATUS.md` bayat ve çelişkili: başlık hâlâ "Faz 14.2D-1: 2. tur CONDITIONALLY ACCEPTED, yeniden denetim bekliyor" (D-1 3. turda ACCEPTED), D-1 satırı aynı, Aşama 5/6 "Governance/Identity/Billing hâlâ AuthDbContext zincirinde" der (§3 ile çelişir), Aşama 8 "Açık", son güncelleme 2026-11-05 | `docs/architecture/IMPLEMENTATION_STATUS.md` (başlık satırı, D-1 satırı, Aşama 5/6/8 satırları) | okuma |
| m-5 | Rapor §11 "yeniden adlandırılan/silinen test yok" yanlış: 5 mimari test silindi/yeniden adlandırıldı (`Controllers_do_not_use_any_DbContext_outside_the_explicit_D2_allowlist`, `Allowlist_has_no_stale_entries`, `Non_owner_contexts_state_a_reason_and_have_no_migrations`, `AuthDbContext_has_no_dbset_or_model_entry_for_the_four_modules`, `All_Eleven_Modules_Register_DbContexts_And_Facades` -> `..._And_Services`); eşdeğer ya da daha güçlü karşılıkları var. IL taraması `IDesignTimeDbContextFactory` türlerini muaf tutuyor | rapor §11, `Phase14_2D1ModuleArchitectureTests.cs` (IL taraması) | TRX adı karşılaştırması |
| m-6 | Geçerli imzalı ama ayrıştırılamaz gövde (boş ya da `data.object` eksik) 400 değil 500 `NullReferenceException` (Stripe.net `EventConverter`) üretir; saldırgan sırrı bilmeden ulaşamaz | `StripeWebhookService.cs` `ConstructEvent` çağrısı | imzalı boş gövde -> 500 |
| m-7 | Geçmiş hijyeni: `fea5610`..`e00b95f` (4 commit) API'yi derlemiyor (3-4 hata), `8bbac01` başlık "work in progress", test projesi `c9fe0b8` ve `8bbac01`'de derlenmiyor (205 ve 173 hata; sonraki commit'lerde test derlemesine bakılmadı); API `4d1ff40`'tan itibaren derleniyor. `git bisect` bu aralıkta kullanılamaz. Kabul edilebilir mi: tek faz, yerel dal, push yok; ama iyi uygulama değil. Rapor aralığı belgelemiyor | commit `fea5610` `StudioContracts.cs(250,18) CS0101` | her commit için `dotnet build backend/Namines.API` |
| m-8 | Identity için `Domain` ad alanı yok; kimlik varlıkları ile Vault/Ground/Flow/Desk varlıkları (`VaultBackup`, `GroundDatabase`, `SavedQuery`, `DeskHandoffToken`, `SqlQueryHistoryEntry`...) hâlâ `Namines.Core/Models/Auth` altında. Rapor yalnız kimlik varlıklarını belgeliyor. Spec "her modülün Domain'i" der | `Namines.Core/Models/Auth/` | `ls` |

### Bilgi (kabul edilen ya da tabandan gelen)
- `gateway.GatewayAuditEntries.ActorUserId`, `ops.SqlExecutionAudits.UserId`, `hub.repositories.OwnerUserId` FK'siz: hesap silme sonrası kullanıcı kimliği denetim satırlarında kalıyor (D-1 kararı).
- `ChangeRequestAuditLogs`'ta hesap silme satırı `ChangeRequestId` = kullanıcı kimliği olarak kalıyor (aktör anonimleştirilir, kimlik kalır).
- Desk silme yalnız FK `CASCADE`'e dayanıyor ama doğrulandı: gerçekten siliniyor (hem yükseltilmiş hem taze katalogda FK var, 0 katalog farkı).

## 5. Uygulayıcının "doğrulanmadı" listesinin yargısı

| Madde | Yargı |
|---|---|
| Gerçek Stripe çağrıları | Kabul edilebilir not. İmzalı sahte yükle yaşam döngüsü sınandı; M-1/M-2 bu sayede bulundu |
| Frontend süitleri | Kapandı: tsc 0 hata, eslint 0 hata, vitest 282/282, next build rc=0 (frontend diff'i boş) |
| Büyük hacimde DROP COLUMN | Kısmen kapandı: 1M kullanıcıda toplam 28,9 sn, metadata işlemi. Kilit penceresi izole ölçülemedi |
| Desk silme yalnız FK | Kabul edilebilir not; çalıştırılarak doğrulandı |
| AI kota middleware yolu | **Blocker değil, Major (M-3)**: kabul ölçütü ana yolda sağlanmıyor |
| JWT/çerez sürekliliği canlı | Kapandı: eski ikiliyle üretilen JWT ve çerez yeni ikilide çalışıyor |
| `Only_the_identity_assembly_references_AuthDbContext` mutasyonla sınanamaz | Kabul edilebilir not: bağlam `internal`, derleyici zaten engelliyor; ek olarak başka testler (reflection modeli) mutasyonla düştü |
| AdminController `UserManager` | Spec sapması, M-4 |
| `ApplicationUser` Core'da | Kabul edilebilir not + m-8 |
| IL taraması zayıflığı | m-2, m-5 |
| `has-pending-model-changes` 4 bağlam | Kapandı: 11/11 temiz |

## 6. Bütün serinin son hâl kontrolü (master prompt)

| Madde | Durum | Kanıt |
|---|---|---|
| 11 gerçek modül | Evet, m-8 notuyla | 11 zincir, 11 `__EFMigrationsHistory`, hepsi kendi bağlamı; Identity'de Domain yok |
| `AuthDbContext` yalnızca Identity | Evet | §3 reflection/katalog |
| `CloudProject` God Entity değil | Evet | `studio.projects` 10 kolon; bağlantı sırrı, paylaşım, repo, etiket ayrı tablolarda |
| Sırlar güvenli | Evet | bağlantı dizesi `v1:` şifreli, AAD; paylaşım jetonu hash; API anahtarı hash; webhook gövdesi saklanmıyor |
| CSV ilişkiler normalize, eski kolonlar düştü | Evet (D-1/C denetimleri; bu fazda yeniden açılmadı) | `project_tags`, `repo_issue_labels`, `api_key_allowed_*`, `table_masked_columns` var |
| Modüller arası DbContext yok, controller'da DbContext yok | Evet | proje başvuruları, `internal` bağlamlar, mutasyon |
| Sözleşmeyle iletişim | Evet, M-4 notuyla | `IIdentityDirectory`, `IAiQuota`, `ICrossModuleTransaction`... |
| Tenant izolasyonu | Evet | matris 829/0 fark; gov/identity IDOR 404 |
| Gerçek PG testleri | Evet | 3331/3331 Testcontainers PG17, atlanan 0 |
| Mimari testler | Evet, m-2 boşluğuyla | 5 mutasyondan 2 düştü |
| 0 uyarı build | Evet | 33,4 sn |
| Docs | Kısmen | Raporlar ve denetimler var; `IMPLEMENTATION_STATUS` bayat (m-4) |

Aşırı iddia: `IMPLEMENTATION_STATUS.md` satırları (m-4). Kalan borç: M-4 (kimlik akışları controller'da), m-8 (Domain/Core'da varlıklar, tarihsel DDL Auth geçmişinde), `Namines.Core` hâlâ hem sözleşme hem varlık deposu, middleware kota yolu (M-3).

## 7. Doğrulanamayanlar

- Gerçek Stripe (checkout, portal, iptal), AI sağlayıcısı (M-3 canlı sömürü), Redis'li dağıtık hız sınırı, PgBouncer altında danışma kilidi, üretim DB'si.
- Kilit penceresi: 1M kullanıcılı migration süresi ölçüldü (28,9 sn), ama `AspNetUsers` üzerindeki `ACCESS EXCLUSIVE` kilit süresini izole örnekleyemedim (örnekleyici docker-exec gecikmesi nedeniyle atladı); süit eş zamanlı koştu.
- Eski `roles.sql` ile kurulu DB'den yükseltme (D-1 R2 senaryosu) bu fazda yeniden koşulmadı; yalnız geçerli `roles.sql` ile geç rol yolu.
- Tam Down sırasında 2B outbox migration'ının açık-satır korumasını tetikleyen senaryo koşulmadı (Down'da açık outbox satırı vardı ama o migration'a inilmedi).
- İstek başına SQL sayısı / N+1 karşılaştırması taban ile yeniden ölçülmedi; indeks planları ve hacimli sorgular ölçüldü.
- Org değiştirme ucu, şifre sıfırlama ve e-posta doğrulama uçları kodda bulunmadı (sınanacak yüzey yok).
- Matris editor ve admin için yazma uçlarını, hesap/oturum/webhook/voice/docker uçlarını kapsamaz (D-1'deki gibi); bu uçlar ayrı problarla (§3) tarandı.
- Yeni ikilinin yazdığı satırlarla Down yapıp eski ikiliyle çalıştırma (geri alma + eski ikili duman) bu fazda koşulmadı; yalnız Down verisi birebir doğrulandı.

## 8. Yeniden denetimde bakılacaklar

M-1 ve M-2 için gerçek PG'de farklı olayların paralel/ters sırası testi ve aynı `wh.js` probunda 0 hata; M-3 için middleware yolunda atomik rezervasyon ya da eşzamanlı istek testi; M-4 için ya Application servisine taşıma ya yazılı karar + `UserManager` sınırı testi; m-4 ve m-5 belge düzeltmeleri; m-2 için SQL tarama genişletmesi (Owners'a Billing/Governance/Identity) ve öznitelik taraması.

## 9. Yöntem

`1cc4ad8` ve `00c287b` için ayrı worktree'ler, ayrı `dotnet publish` çıktıları. Taban ikilisiyle `smoke-d1.js` + `smoke-d2.js` + oturum yakalama çalıştırılarak D-1 durumunda DB üretildi, üstüne seed ve kirli kullanıcılar eklendi, şablon DB olarak çoğaltıldı. Tüm tablo parmak izi: `pg_temp` işleviyle her tablo için `to_jsonb` satırlarının sıralı md5'i (Stripe kolonları dışarıda). Katalog: FK, kısıt, indeks, tetikleyici, varsayılan, işlev gövdesi (`aud/cat.sql`). Problar: `aud/wh.js`, `aud/gov.js`, `aud/ident.js`, `aud/inv.js`, `aud/del.js`, `aud/sess-old.js`/`sess-new.js`, `aud/trg.sql`, `aud/mutD2.sh`. Geçici DB, rol ve worktree'ler iş bitince silindi; `arch/phase-14-2` temiz bırakıldı.

---

## 10. 2. tur (`4d255de..0dc558e`: `a17c3bc`, `16a5241`, `181294d`, `803c452`, `0dc558e`)

Yöntem 1. turla aynı: `0dc558e` için ayrı temiz worktree, taban `00c287b` ikilisi, gerçek PG. Uygulayıcının sayılarına değil kendi koşumlarıma dayanıyor.

### 10.1 Faz sonu tablosu (2. tur)

| Alan | Sonuç | Kanıt |
|---|---|---|
| Build | PASS | `--no-incremental` Release: 0 uyarı, 0 hata, 49,1 sn |
| Unit | PASS | Tam süit içinde; yeni testler eklendi (biri zayıf: R2-m2) |
| Integration | PASS | `Namines.Tests` 3347/3347 gerçek PG |
| Architecture | PASS, dar boşluklarla | Bu turda 7 mutasyonun 6'sı düştü, 1 kaçtı (R2-m4); 1. turda kaçan üçü (m-2) artık düşüyor |
| Migration | PASS | §10.3 |
| Data integrity | PASS | 57 tablo md5 birebir, Down birebir, Stripe alanları eşit (kasıtlı kırpma/NULL dışında) |
| Security | PASS | M-1, M-2, M-3, m-1 çalıştırılarak kapandı; yeni küçük bulgular §10.5 |
| API compatibility | PASS | 829/0 fark, 46 etiket şekil 0 fark, 50 etiketli kimlik akışı 0 fark, 15 etiketli yönetici okuma 0 fark |
| Documentation | PASS, küçük borçla | R2-m3 |
| Independent audit | **CONDITIONALLY ACCEPTED** | Üç küçük madde: R2-m1, R2-m2, R2-m3 |

### 10.2 Test sayıları (2. tur, çalıştırıldı)

| Komut | Proje | Toplam | Geçen | Kalan | Atlanan | Süre |
|---|---|---|---|---|---|---|
| `dotnet test -c Release --no-build` (paralel kapalı, `0dc558e` temiz worktree) | `Namines.Tests` | 3347 | 3347 | 0 | 0 | 29 dk 12 sn |
| Aynı | `Namines.Tests.RunTests` | 19 | 19 | 0 | 0 | 1 dk 9 sn |
| `dotnet-ef migrations has-pending-model-changes` | 11 bağlam | 11 | 11 "No changes" | | | |
| `smoke-d1.js` (gerçek API) | boş DB; D-1'den yükseltilmiş DB (`postgres`); geç roller `namines_app` | 95 x 3 | 95 x 3 | 0 | | |
| `smoke-d2.js` | aynı üç ortam | 44 x 3 | 44 x 3 | 0 | | 46 etiket şekli tabanla 0 fark (3 ortam) |
| `aud/matrix.js` taban ve yeni | 220 etiket | 829 karşılaştırma | | 0 fark | | |

Ortam: Windows 11, .NET 8, Testcontainers `postgres:17-alpine`; senaryolar `postgres:16-alpine`. Süit benim PG problarımla aynı anda koştu: süre karşılaştırma değeri taşımaz. TRX: 3331 -> 3347, 16 eklendi, 0 silindi/yeniden adlandırıldı, atlanan 0.

### 10.3 Migration ve oturum (yeniden çalıştırıldı)

D-1 durumu yine taban ikilisiyle üretildi (smoke-d1 + smoke-d2 + oturum yakalama + seed + 5 kirli kullanıcı).

- Yinelenen `StripeSubscriptionId` (iki kullanıcıda `sub_1`) artık migration'ı açık hatayla durduruyor (`Billing_01: the same StripeSubscriptionId belongs to more than one user (sub_1)`); `billing.customers` oluşmuyor, 5 Stripe kolonu yerinde. Çözülünce rc=0. Bu yeni ön kontrol runbook'ta yok (R2-m3).
- Up: 57 tablo `to_jsonb` md5 birebir. Stripe alanları satır satır eşit; kasıtlı fark: boşluklu `' cus_ws '` kırpıldı, `StripeCustomerId=''` taşınmadı (m-3 kapandı).
- Tam Down (Desk, Flow, Ground, Vault, Governance, Billing, Auth) birebir; yeniden Up aynı; taze ile yükseltilmiş katalog farkı 0 (502 satır). Filtreli benzersiz indeks `IX_subscriptions_StripeSubscriptionId ... WHERE "StripeSubscriptionId" IS NOT NULL` var.
- 3 eşzamanlı `--migrate`: üçü rc=0, sonuç aynı.
- Geç roller: rc=0, `namines_app` eksik hak yok. `governance.purge_change_request_audit`: `SECURITY DEFINER`, `search_path = governance, pg_temp`, `EXECUTE` yalnız sahibine ve `namines_app`'e (PUBLIC yok).
- Eski ikiliyle (`00c287b`) yakalanan JWT ve çerez, yükseltilmiş DB'de yeni ikiliyle: `/quota/status`, `/team`, `/usage` hepsi 200 (Bearer ve çerez); eski parolayla giriş 200. Uygulayıcı bunu yeniden koşmamıştı; koşuldu.

### 10.4 Madde madde

| ID | Sonuç | Kanıt |
|---|---|---|
| M-1 | **Kapandı** (ters alt-durum açık: R2-m1) | `wh.js`: bağlanmamış müşteri için `updated@+50`, sonra `checkout@+40` -> `mapped=1 type=2`. 29 paralel yineleme, imza matrisi, yaşam döngüsü, eski olay `stale`: önceki gibi |
| M-2 | **Kapandı** (süit testi zayıf: R2-m2) | Aynı yarış 80 tur: son durum `canceled` olmayan 0 (4d255de: 10/25). Müşteri başına `pg_advisory_xact_lock`. Kilidi kaldıran mutasyon (kalan her şey aynı) aynı HTTP probunda 34/60 yanlış: kilit gerçekten gerekli ve etkili. 30 turluk karışık checkout/updated/deleted paralel koşuda 5xx ve `40P01` (deadlock) yok. İşlem içinde Stripe HTTP çağrısı kilit ve bağlantı altında: yalnız aynı müşteriyi bekletir (R2-m5) |
| M-3 | **Kapandı** | Gerçek middleware, gerçek API: Free kullanıcı tek çağrı 2000 token; 40 paralel `smartseed` isteği: 4 `Allowed`, 36 `quota-exhausted`, sayaç tam 10000/10000. 400 dönen 5 uç (bozuk JSON, boş gövde, `schema/revise`, `schema/mockdata`, `aidba`) kotada net 0 (iade çalışıyor). Çağrı sürerken istemci kopması: rezervasyon korunuyor (iş yapıldı), sızıntı yok; istisna/iptalde `RefundAsync(CancellationToken.None)`. Çift ücret: `/api/schema/generate` kendi rezervasyonunu yapıyor, middleware orada yalnız `CheckAsync` + `HasReservedInScope`; diğer uçlar yalnız middleware'de rezerve ediyor. Tahmin ile gerçek maliyet farkı eskisi gibi (yalnız `generate` gerçek ölçümle uzlaştırıyor): kabul edilen borç |
| M-4 | **Kapandı** | `UserManager`/`SignInManager`/`RoleManager` hiçbir controller'da yok (grep + yeni mimari test). Kalan kullanım API derlemesinde yalnız başlangıç tohumlayıcıları, `Program.cs` ve `PwnedPasswordValidator`: controller değil, kabul. Davranış eşliği: 50 etiketli kimlik akışı (kayıt, yineleme, zayıf/pwned/boş, giriş, yanlış parola, kilitlenme, profil, MFA kurulum, oturum iptali, çıkış, OAuth, desk jetonu, hesap silme) taban ve yeni ile durum + gövde şekli + mesaj 0 fark; gerçek `Dev:Email` hesabıyla 15 yönetici okuma etiketi 0 fark; matris 829/0. Not: betiğim MFA etkinleştirme ve hesap silme başarı yollarına jeton yüzünden ulaşamadı (iki ikilide de aynı 401); o yollar süit testlerine dayanıyor. `IsDev` artık GET'te yazılmıyor; girişte yapılandırılmış `Dev:Email` için yazılmaya devam ediyor (`IdentityAccountService.cs:151`) ve `DevAccountSeeder` kalıcı yazıyor: yapılandırılmış geliştirici/yönetici kilitlenmiyor, yönetici çözümü e-postadan okunuyor. Yeni mimari test gerçek: controller alanı ve `[FromServices]` parametresi mutasyonda düştü; controller'da `RequestServices.GetService(typeof(UserManager<>))` düşmedi (R2-m4) |
| m-1 | **Kapandı** | `namines_app`: oturum ayarıyla DELETE reddedildi (tetikleyici artık `current_user` = tablo sahibi ister); `purge_change_request_audit(now()-10 gün)` -> 90 gün tabanı hatası; 100 günlük kesim yalnız 100 günden eski satırı siler (30 günlük kalır); geçici tablo ile `search_path` kaçırma etkisiz; işlev sonrası aynı işlemde düz DELETE reddedildi; doğrudan UPDATE reddedildi. `DataRetentionService` işlevi çağırıyor, ayarı 90 gün tabanına çekiyor. Bilinçli kalan: uygulama rolü 90 günden eski satırları işlev yoluyla silebilir (retention tasarımı) |
| m-2 | **Kapandı**, kalan: R2-m6 | `Owners` 7 modül, taranan derleme 14. 1. turda kaçan üç mutasyon: Governance'ta metot içinde `billing."UserAIQuotas"` SQL'i düştü, Billing'de interpolasyonlu `public."AspNetUsers"` SQL'i düştü, Domain'de EF özniteliği düştü. Kullanılmayan `const` dize düşmedi: derleyici kullanılmayan sabit metni IL'e yazmıyor, sorguya dönüşemez, kabul |
| m-3 | **Kapandı** | §10.3 |
| m-4 | **Kapandı**, küçük kalıntı | `IMPLEMENTATION_STATUS.md` D-1 ACCEPTED (3. tur), Aşama 5/6 tamam, D-2 "yeniden denetim bekliyor" diyor: doğru. Runbook eski (R2-m3) |
| m-5 | **Kapandı** | TRX: silinen/yeniden adlandırılan 0; rapor düzeltildi |
| m-6 | **Kapandı** | İmzalı boş gövde, `null`, `{}`, `[]`, metin, `data.object` eksik: hepsi 400 (mesaj bazen "Invalid Stripe signature", yanıltıcı ama 4xx) |
| m-7 | Kabul edilebilir borç | Geçmiş yeniden yazılmadı. Yerel dal, itilmedi, tek faz; squash birleştirmede bisect sorunu kalmaz. Ana dala olduğu gibi birleştirilirse `fea5610..e00b95f` bisect'i bozar |
| m-8 | Kabul edilebilir borç, **blocker değil** | "`AuthDbContext` yalnızca Identity" ölçütü yapısal olarak sağlanıyor (model tabloları, katalog, `internal` bağlam, derleme erişimi). Kalan, `Namines.Core/Models/Auth` içindeki kimlik varlıkları: ad alanı/derleme yerleşimi borcu; bağımlılık yönü (Core modülü bilmez) bozulmuyor, veri sahipliği ve DbContext izolasyonu etkilenmiyor. Spec'in "her modülün Domain'i" cümlesine tam uyum değil; `MODULE_OWNERSHIP.md` borcu kaydediyor |

### 10.5 Yeni küçük bulgular (2. tur)

| ID | Bulgu | Yer | Yeniden üretim |
|---|---|---|---|
| R2-m1 | M-1 düzeltmesinin ters kalıntısı: bağlı olmayan müşterinin `subscription.deleted` olayı etkisiz kaydediliyor ve sıra hesabına girmiyor; sonra gelen daha eski `checkout.session.completed` kullanıcıyı `active`/Corporate yapıyor. Checkout durumu sabit `"active"` yazıyor. Gerçekte yalnız sıra dışı teslimatta ve eşleme öncesi iptalde olur | `StripeWebhookService.cs` `apply` ifadesi (checkout için `!mapped \|\| isLatest`) ve `HandleCheckoutCompletedAsync` | `wh2.js`: `cus_v1` için bağlanmamış `deleted@+50` -> 200; `checkout@+40` -> 200, `state=active type=2`. Düzeltme: bağlanmamış müşterinin güncelleme/silme olayını "uygulanmadı, yeniden dene" say (5xx ya da kaydetme), ya da checkout'ta Stripe'tan okunan gerçek durumu yaz |
| R2-m2 | M-2'nin süit içi regresyon testi ayırt edici değil: `Different_events_of_one_customer_arriving_in_parallel_always_end_at_the_newest_state` kilit kapatılınca da geçiyor (3/3 koşum, 30 sn gerçek koşu). Kanıt yalnız canlı HTTP probunda (34/60 yanlış kilitsiz, 0/80 kilitli). Uygulayıcı bunu raporda kendisi söylüyor | `Phase14_2D2BillingRealPgTests.cs:460-497` | Kilit çağrısı `if (false)` ile kapatıldı, derlendi, filtreli test geçti. Düzeltme: iki ayrı bağlantıdan gerçek eşzamanlı teslim (başlangıç kapısı) ile HTTP düzeyinde yarış |
| R2-m3 | Runbook (`MIGRATION-VE-GERI-ALMA.md` §5.4) eski: yeni `StripeSubscriptionId` yinelenen ön kontrolünü anmıyor, kimlik kırpma/boş=NULL davranışını anmıyor, denetim izi maddesi hâlâ `namines.audit_retention = on` ile silme diyor (artık yalnız `purge_change_request_audit`) | `docs/deploy/MIGRATION-VE-GERI-ALMA.md:253,255` | okuma |
| R2-m4 | `No_controller_takes_UserManager_SignInManager_or_RoleManager` yalnız imza/alan/özellik türlerine bakıyor; IL içinden hizmet bulucu ile erişimi görmüyor (DbContext testi IL de tarıyor) | `Phase14_2D2IdentityBoundaryTests.cs` | Mutasyon: `RequestServices.GetService(typeof(UserManager<ApplicationUser>))`; yalnız rota anlık görüntüsü düştü, kimlik testi geçti |
| R2-m5 | Checkout işlenirken Stripe HTTP çağrısı müşteri advisory kilidi ve açık DB işlemi altında; Stripe yavaşsa o müşterinin diğer olayları ve bir bağlantı bekler | `StripeWebhookService.cs` `HandleCheckoutCompletedAsync` | okuma |
| R2-m6 | Tablo sahipliği ve SQL metin taramasının `Owners` listesinde Vault/Ground/Flow/Desk yok: bu dört modülün tablolarını başka modül SQL ile adlandırsa test görmez (koddaki gerçek ihlal yok) | `Phase14_2D1BoundaryScanTests.cs` `Owners` | okuma |

R2-m1, R2-m2 ve R2-m3 faz içinde kapanmalı (küçük). R2-m4, R2-m5, R2-m6 kabul edilebilir borç.

### 10.6 Doğrulanamayanlar (2. tur)

Gerçek Stripe; canlı AI sağlayıcısı (M-3 gerçek middleware üzerinden yerel yedekli `smartseed` ucuyla sınandı, canlı model çağrısı yok); üretim DB; PgBouncer; Redis. `AspNetUsers` üzerindeki kilit penceresi izole ölçülemedi. MFA etkinleştirme/devre dışı bırakma ve hesap silme başarı yollarının taban/yeni HTTP eşliği betiğimle sınanamadı (süit testlerine dayanıyor). Eski `roles.sql` ile kurulu DB'den yükseltme ve "Down sonra eski ikili" yeniden koşulmadı.

## 11. Son karar ve bütün seri

**Son karar: CONDITIONALLY ACCEPTED.** D-2'nin dört Major bulgusu ve m-1..m-8 çalıştırılarak kapandı ya da gerekçeli kabul edildi; veri, migration, oturum, tenant izolasyonu, kota ve denetim izi sağlam. ACCEPTED için kapsamda üç küçük madde açık: R2-m1 (abonelik yaşam döngüsünde ters sıra kalıntısı, tekrarlanabilir), R2-m2 (M-2'yi koruyan süit testi ayırt edici değil) ve R2-m3 (runbook yanlış bilgi içeriyor). Yeniden denetimde yalnız bunlara bakılır.

### Bütün seri, master prompt son hâli (0dc558e)

| Madde | Durum |
|---|---|
| 11 gerçek modül | Evet: 11 zincir, 11 geçmiş tablosu, her biri kendi bağlamı, Application ve Infrastructure; Identity'de ayrı Domain ad alanı yok (kabul edilen borç) |
| `AuthDbContext` yalnız Identity | Evet (reflection testi, katalog, `internal`) |
| `CloudProject` God Entity değil | Evet (`studio.projects` 10 kolon) |
| Sırlar güvenli | Evet: bağlantı dizesi şifreli + AAD, paylaşım jetonu hash, API anahtarı hash, webhook gövdesi saklanmıyor |
| CSV ilişkiler normalize, eski kolonlar düştü | Evet (14.2B/C/D-1 denetimleri; bu fazda yeniden açılmadı) |
| Modüller arası DbContext yok; controller'da DbContext ve kimlik yöneticisi yok | Evet |
| Sözleşmeyle iletişim, tenant izolasyonu | Evet (matris 829/0) |
| Gerçek PG testleri, mimari testler, 0 uyarı build | Evet (3347/3347, 19/19, mutasyonlu) |
| Belgeler | Evet; runbook güncellemesi bekliyor (R2-m3) |
| Dört bağımsız denetim ACCEPTED | 14.2B, 14.2C, 14.2D-1 ACCEPTED; 14.2D-2 bu belge: CONDITIONALLY ACCEPTED |

Kalan borç (sahibin bilmesi gerekenler):

- **Blocker (D-2 kapanışı için):** R2-m1, R2-m2, R2-m3 (küçük, aynı fazda).
- **Kabul edilen borç:** (1) Kimlik varlıkları ve Vault/Ground/Flow/Desk varlıkları `Namines.Core/Models/Auth` altında, Identity'nin ayrı Domain'i yok; tarihsel DDL Auth geçmişinde. (2) Başlangıç tohumlayıcıları ve `PwnedPasswordValidator` API derlemesinde `UserManager` kullanıyor. (3) `Namines.Core` hâlâ hem paylaşılan sözleşme hem varlık deposu. (4) `schema/generate` dışındaki AI uçlarında rezervasyon tahmini token ile yapılıyor, gerçek ölçümle uzlaştırma yok. (5) 90 günden eski denetim satırlarını uygulama rolü işlev yoluyla silebilir. (6) Geçmiş: `fea5610..e00b95f` API'yi derlemiyor; squash birleştirme önerilir. (7) R2-m4..m6. (8) Gerçek Stripe, canlı AI sağlayıcısı, üretim DB ve hacimli kilit penceresi hiç sınanmadı.
