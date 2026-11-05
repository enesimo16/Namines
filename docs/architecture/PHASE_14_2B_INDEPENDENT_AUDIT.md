# Faz 14.2B Bağımsız Kabul Denetimi

> Denetlenen: `arch/phase-14-2`, ikinci tur `f741b8b..9f59c07` (birinci tur `5565811..68c9fec`). Tarih: 2026-11-05.
> Ortam: Windows 11, .NET 8.0.427, Docker 29.9; kendi `postgres:16-alpine` container'ım, testlerde Testcontainers (`postgres:17-alpine`). Uygulayıcının rapor ve test çıktılarına güvenilmedi; aşağıdaki her sayı bu denetimde üretildi.

## Nihai karar (ikinci tur): CONDITIONALLY ACCEPTED

Birinci turun bütün bulguları (B-1, B-2, M-1, M-2, L-1..L-5) gerçek koşularla kapandı. Ancak B-2 düzeltmesi **yeni bir bloklayıcı getirdi: B-3**. Abonelik iptali artık commit'ten sonra çalışıyor ve kaybolabiliyor. ACCEPTED verilemez; B-3 kapanmadan 14.2C'ye geçilemez. Yeniden denetim yalnızca B-3 ve tam test koşusudur.

## Kabul tablosu (ikinci tur, `9f59c07`)

| Alan | Sonuç | Kanıt |
|---|---|---|
| Build | PASS | `dotnet build backend/Namines.sln -c Release --no-incremental`: 0 uyarı, 0 hata |
| Unit | PASS | `Namines.Tests`: 2812 toplam, 2812 geçti, 0 kaldı, 0 atlandı, 11 dk 45 sn, sıralı. Docker gerektirmeyenler bu sayının içinde |
| Integration | PASS, B-3 senaryosu yok | Aynı koşuda gerçek PG; `Namines.Tests.RunTests`: 19/19, 1 dk 28 sn. Bir önceki deneme benim kabuğumda `MSYS_NO_PATHCONV=1` yüzünden .NET çalışma zamanını bulamayıp başlamadı (kod hatası değil); temiz kabukta yeniden koşuldu |
| Architecture | PASS | `ModularMonolithArchitectureTests` yeşil; yazım noktaları elle de tarandı (§A) |
| Migration | PASS | Boş DB, seed'li Up, Down, Up, en az yetkili rol (boş ve seed'li): §A. `has-pending-model-changes` Auth, Studio, Hub: temiz |
| Data integrity | PASS | 52 kötü niyetli satır: alan farkı yok, etiket 52/52 C# ile aynı, Up1 = Up2 |
| Security | PASS | Gerçek API'ye karşı 38/38 yeniden geçti; log'da bağlantı parolası 0 |
| API compatibility | PASS, farklar §4 | Rotalar ve DTO alanları aynı; `tsc` 0 hata; vitest 39 dosya, 280/280. Frontend'de tek değişiklik arama kutusu metni |
| Performans (ek) | PASS | Eşleşmeyen `q` ve motor: 14-54 ms, test yükü altında (önce 570-625 ms boşta) |
| Billing bütünlüğü (ek) | **FAIL** | B-3 |
| Documentation | PASS, B-3 hariç | Rapor güncel; `PHASE_14_2B_REPORT.md` §4 "pending" anlatımı B-3 ile düzeltilmeli |
| Independent audit | CONDITIONALLY ACCEPTED | Bu belge |

## Açık bloklayıcı

### B-3 (HIGH): Commit'ten sonra Stripe iptali kaybolabiliyor; `pending` satırını kimse işlemiyor

`backend/Namines.Infrastructure/Services/AccountDeletionService.cs:284`, `:296-312`. **Yeni:** birinci turda iptal işlemden önce çalışıyordu.

**"Kim yeniden deniyor?" Hiç kimse.** Üretim kodunda `pending` veya `manual_cancellation_required` okuyan bir yer yok (tam grep; işçi yok, Admin ucu yok). Üç kayıp yolu:

| Yol | Ne olur | İz |
|---|---|---|
| Commit ile Stripe arasında süreç ölür (deploy, OOM) | İptal hiç gönderilmez, hesap yok, **müşteri ödemeye devam eder** | Yalnız `stripe:"pending"`. Abonelik kimliği hiçbir yerde yok: kullanıcı satırı silindi, `Details` kimliği taşımıyor, log yazılmadı. **Sessiz ve kurtarılamaz** |
| İstemci commit'ten sonra bağlantıyı keser | `ct` = `RequestAborted` (`AuthController.cs:922`). İptal edilmiş token `:301`'de Stripe çağrısını düşürür, `StripeSubscriptionCanceller.cs:42` yutup `false` döner; `:312` de aynı token'la patlar | Satır `pending` kalır; kimlik yalnız log'da. Bu yol kod okumasıyla belirlendi, çalıştırılarak denenmedi |
| Stripe geçici hatası | `manual_cancellation_required`, yeniden deneme yok | Kimlik yalnız log'da |

Gerçek API'de kanıt (abonelikli hesap, Stripe anahtarı yok): `DELETE /api/auth/me` 200; denetim satırı `"stripe":"manual_cancellation_required"`; `sub_audit_test_1` denetim tablosunda **0**, log'da 1 satır.

Eski sırada süreç ölürse hesap duruyor ve abonelik iptal edilmiş oluyordu; yani ödeme devam etmiyordu. Yeni sıra işlem hatasındaki sorunu doğru çözdü ama ölümcül yolu "silinmiş hesaba sessiz tahsilat"a çevirdi. Üçüncü yol önceden de vardı; ilk ikisi bu düzeltmeyle geldi.

**Gerekli düzeltme (transactional outbox):**
1. Tablo: `PendingSubscriptionCancellation` (`Id`, `SubscriptionId`, `CreatedAt`, `Attempts`, `LastAttemptAt`, `CompletedAt`), `AuthDbContext` zincirinde ileri migration. Kullanıcıya FK yok; satır hesaptan uzun yaşamalı. D-2'de Billing'e taşınır.
2. Satır, kullanıcı silmeyle **aynı işlemde** yazılır (`:284` yanında).
3. Commit sonrası ilk deneme `ct` ile değil, `CancellationToken.None` + süre sınırıyla yapılır. Başarıda `CompletedAt` ve denetim satırı güncellenir.
4. Arka plan işçisi tamamlanmamış satırları artan aralıkla yeniden dener. `CancelAsync` `resource_missing` için `true` döndüğünden idempotent; çoklu instance için `FOR UPDATE SKIP LOCKED`. Eşiği aşan satır her turda `LogError` üretir ve Admin'de görünür. Stripe anahtarı yoksa satır bekler, silinmez.
5. Retention açık satırı silmez.
6. Gerçek PG testleri: (a) commit sonrası canceller çağrılmadan süreç ölümü benzetimi, işçi tamamlar; (b) iptal edilmiş token, iptal yine gider; (c) Stripe önce hata sonra başarı, tek iptal; (d) işlem düşerse outbox satırı da yok.

## İkinci tur bulguları

| ID | Durum | Kanıt |
|---|---|---|
| B-1 | **Kapandı** | Tek SQL + sabit toplu okuma. 50.000 proje / 10.012 public, test yükü altında: `q=zzzznomatch` 24-54 ms, `dbType=Oracle` 14-20 ms, `tag` 33-46 ms. `explore-public` 120/dk IP. Test `CommandCounter` ile gerçek SQL sayıyor (650 repoda ≤5). `q=100%`, `q=_` 200; `İstanbul/istanbul/şema/ŞEMA` ad üzerinde buluyor |
| B-2 | **Kapandı** | §B. Gerçek API: çakışan varis ile `DELETE /api/auth/me` 200, repo `same-name-2`, varisin `same-name`'i değişmedi, sahip güncel, WRN log'u var |
| M-1 | **Kapandı** | Boş DB + `roles.sql` + migrator: 50/50 migration. Seed'li: Up, Down, Up migrator ile geçti; 5 tabloda `namines_app` DML yetkili |
| M-2 | **Kapandı** | Kilit ölçülü, §4 tablosu var. Kendi ölçümüm: Decompose **11 sn** (50.000 proje, boşta, EF log zaman damgası; harf harf anahtar fonksiyonu 7 sn'den çıkardı), ReadModel 1 sn. Raporun 13,7 sn'si `docker exec` içeriyor, aynı büyüklük; pencere planı için yeterli |
| L-1 | **Kapandı** | Seed'e `𐐀`, `Ⅷ`, `Ⓐ`, `ẞ`, `ΣΣΣ`, `ﬁ`, `K` eklendi; C# eşleniği ile 52/52 birebir, fark 0 |
| L-2 | **Kapandı** | `when (ex is not PostgresException && ...)` |
| L-3 | **Kapandı** | `share-create` 30/dk ayrı kova; iptal satırı 90 gün sonra siliniyor (`DataRetentionService`, gerçek PG testi). 50 link tavanı raporda bilinçli karar |
| L-4 | **Kapandı** | `Up_/Down_dogrulamasi_bozulmus_veriyi_yakaliyor`; `Down` etiket ve paylaşımı da doğruluyor, temizse VALIDATE ediyor |
| L-5 | **Kapandı** | Kod silindi. `MIGRATION_PLAN.md:38` eski anlatımı duruyor (`:4` notu düzeltiyor): sıradaki doküman turunda temizlenmeli, bloklamaz |

## §A. Okuma modeli migration'ı ve kayma

**`Phase14_2B_ExploreReadModel` NOT VALID durumu birebir mi? Evet.** Decompose'da durdurup `(tablo, kısıt, convalidated, tanım)` dökümü aldım, sonra `--migrate` ile `ExploreReadModel`'i uyguladım; EXCEPT iki yönde **0 fark**. 9 CHECK'in 7'si NOT VALID kalıyor, kısıtlar yine denetliyor, kaldırma aynı işlemde (`pg_get_constraintdef` " NOT VALID" ekini koruyor). Yalnız NOT VALID olanlar kaldırılıyor. Repo alanları ve `UpdatedAt` değişmedi. `ProjectName/ProjectDbType` NOT NULL, proje ile 0 fark.

**Tam yol:** Up → Down → Up: Up1 = Up2 (repo, bağlantı, paylaşım, etiket, proje: 0 fark). Down farkları yalnız bildirilenler. Şemalar bilinçli olarak kalıyor (`DROP SCHEMA` kaldırıldı).

**Kayabilir mi? Bugünkü kodda hayır; koruma yok.**
- `CloudProject.Name/DbType` yazan üretim satırları: yalnız `AuthController.cs:474-475` ve `HubController.cs:490,496` (tam grep: `.Name =`, `.DbType =`, `SetProperty`, ham SQL). İkisi de `ICrossModuleTransaction` içinde okuma modelini yazıyor.
- Eşzamanlı ad değişimi xmin ile çakışır; proje satırı işlem boyunca kilitli olduğundan iki yazım iç içe geçemez.
- `new ProjectRepository`: 4 yer, hepsi ad ve motoru dolduruyor. `DevSandboxSeeder` işlem dışı ama her açılışta yeniden eşitliyor.
- Hesap devri ad veya motor değiştirmiyor. `TeamController` yalnız org değiştiriyor. MCP/CLI projeye doğrudan yazmıyor (`CloudProject`, `AuthDbContext`, `auth/sync` eşleşmesi 0).
- Gerçek API: sync ve PATCH ile ad ve motor değişimi aramaya yansıdı, eski ad artık bulunmuyor.
- **Öneri (bloklamaz):** DB düzeyinde ve testte yeni yazım noktasını yakalayan bir şey yok. `Name`/`DbType` atamasını tarayan bir mimari test ekleyin.

## §B. B-2'nin kalan penceresi: kabul edilebilir, bloklayıcı değil

Kişisel projeler işlemden önce silindiği için beklenmeyen DB hatasında proje silinmiş, hesap duruyor kalıyor. **Kabul edilebilir**, gerekçe:
- Kullanıcı parola ve e-posta onayıyla tam olarak bu projelerin silinmesini istedi; niyete aykırı veri kaybı yok.
- Silinenler yalnız kendisinin ve tek üyesi olduğu org'ların projeleri. Başka kullanıcı etkilenmiyor; devredilecekler işlemde, geri alınıyor.
- Kalıcı neden kalmadı (slug çakışması çözüldü, ön kontroller başta). Yeniden deneme tamamlıyor (gerçek PG testi, trigger ile düşürülen işlem).
- Sıra gerekçesi doğru: kullanıcı satırı önce silinirse FK cascade blob/sağlayıcı temizliğini atlar.
- **Öneri (bloklamaz):** bu durumda genel 500 yerine "projeleriniz silindi, işlemi yeniden deneyin" mesajı.

Bu kabul B-3'e uzanmaz: orada zarar görülmüyor ve kullanıcı yeniden deneyemiyor çünkü hesabı yok.

---

# Birinci tur kaydı (`68c9fec`)

Aşağıdaki bulgular ve sayılar birinci tura aittir; durumları yukarıdaki tabloda. §1-§6 kanıtları o commit içindir, §3-§5 sonuçları ikinci turda yeniden doğrulandı.

## Birinci tur bulguları

### B-1 (HIGH): Explore'da `q`/`dbType` tüm public repoları uygulama belleğinde tarıyor

`backend/Namines.API/Controllers/HubController.cs:231-273`. 200'lük turlarla ilerleyen döngü, tur başına 3 sorgu çalıştırıyor. Uç anonim ve hız sınırı yok (`:128`). Motor filtresi arayüzün normal yolu (`frontend/app/hub/page.tsx:96`), yani "uç durum" değil.

Ölçüm (50.000 proje, 10.012 public, makine boşta, 3 ölçümün değerleri):

| İstek | Süre |
|---|---|
| Filtresiz | 12-27 ms |
| `?q=zzzznomatch` | 574-625 ms |
| `?dbType=Oracle` | 567-577 ms (test yükü altında 3,4 sn'ye kadar) |
| Eşdeğer tek SQL (`EXPLAIN ANALYZE`, hash join) | 20,6 ms |

Eski kod tek sorguydu; yeni kod 150 sorgu, yaklaşık 30 kat yavaş ve public repo sayısıyla doğrusal. 100.000 public repoda hesaplanan: yaklaşık 6 sn ve 1.500 sorgu, kimlik doğrulamasız ve sınırsız. Bu, sahibin "full scan yok / performans" standardını ihlal eder. Raporun "D-2'de arama indeksi gerekir" notu da "açık taşınmaz" kuralına aykırı. **Karar: bloklayıcı.**

**Gerekli düzeltme:**
1. `hub.repositories`'e `OwnerUserId` deseniyle Hub'a ait okuma modeli kolonları ekle: `ProjectName`, `ProjectDbType`. İleri migration ile doldur, migration içinde alan alan doğrula.
2. Yazımları proje yazımıyla aynı `ICrossModuleTransaction` içinde yap: `AuthController` sync, `UpdateSettings`, fork, tohumlayıcılar.
3. `HubApplicationService.GetExploreCandidatesAsync` `q`/`dbType` filtresini tek SQL'de uygulasın (ad/açıklama `LIKE`, etiket `EXISTS`), `LIMIT 200`.
4. `CollectExploreCandidatesAsync` döngüsünü ve `HasTag` CSV bölmesini sil.
5. Explore'a anonim IP hız sınırı politikası ekle.
6. Gerçek PG testi: eşleşmeyen `q` sabit sayıda sorgu çalıştırıyor; yeniden adlandırma aramaya yansıyor. Plan raporda.

### B-2 (HIGH): Varisin aynı slug'ı varsa hesap silme kalıcı 500 veriyor ve geri alınamaz iş yapmış oluyor

`backend/Namines.Infrastructure/Services/AccountDeletionService.cs:246` → `HubApplicationService.cs:307`, `IX_repositories_OwnerUserId_Slug` ihlali. Kişisel projeler (`:199`) ve Stripe iptali işlem (`:237`) **başlamadan önce** gerçekleşiyor.

Gerçek API'de yeniden üretim:
1. A ve H aynı takım org'unun üyesi. A'nın org projesi ve H'nin kendi projesi aynı adda ("Same Name"), ikisinin slug'ı `same-name`.
2. `DELETE /api/auth/me` (A).
3. Sonuç: `500 23505 IX_repositories_OwnerUserId_Slug`. Hesap duruyor, A'nın kişisel projesi **fiziksel olarak silinmiş**, ikinci deneme yine 500.

Slug, addan ve yalnızca sahip içinde benzersiz üretildiği için (`BuildUniqueSlugAsync`) aynı adlı projeler sıradan bir durum. Regresyon değil (`5565811`'de `IX_CloudProjects_UserId_Slug` aynı davranışı veriyordu), ama bu fazın yeniden yazdığı akış ve taşınan veri üzerinde, rapor da "kapsam dışı" diye bırakmış. **Karar: bloklayıcı.**

**Gerekli düzeltme:**
- `ReassignOwnerAsync` işlem içinde çakışmayı çözsün: varis altında slug doluysa `slug-2`, `slug-3`... (`BuildUniqueSlugAsync` kuralıyla) ve logla.
- Çakışma çözülemiyorsa hata, adım 2'den **önce** bir ön kontrolde dönmeli.
- Gerçek PG testi: `Hesap_silme_varis_slug_cakismasi`, sonuçta hesap silinmiş, repo varise geçmiş, slug benzersiz.

## Bu fazda kapanacak küçük bulgular

### M-1 (MEDIUM): En az yetkili rolle `Down` + `Up` kırık

`Phase14_2B_DecompositionScripts.cs:425-426` şemaları düşürüyor; `deploy/sql/roles.sql:35` migrator'a veritabanı `CREATE` yetkisi vermiyor.

Yeniden üretim: `roles.sql`, `namines_migrator` ile Up (geçer), Down (geçer), yeniden Up: `42501 permission denied for database`. Veri kaybı yok, işlem geri alınıyor; `roles.sql` yeniden çalıştırılınca geçiyor. Runbook bunu söylemiyor (`docs/deploy/MIGRATION-VE-GERI-ALMA.md` §5.1). Aynı kök neden, rollerle **boş** DB kurulumunu da `20261024215203` migration'ında durduruyor (49 migration'ın 33'ü uygulanıyor; bu kısım önceden vardı).

**Düzeltme:** `roles.sql:35` için ayrı satır: `GRANT CONNECT, CREATE ON DATABASE :"dbname" TO namines_migrator;` ve `:38-40` yorumunu düzelt. Doğrulandı: yetkiden sonra boş DB'de 49/49 uygulanıyor. Geri alma için bir test ekle.

### M-2 (MEDIUM): Rapor eksik ve bir iddiası ölçümsüz

`docs/architecture/PHASE_14_2B_REPORT.md`:
- `:60-61` kilit süresi ölçülmemiş ("saniye altı beklenir"). Ölçüm: 50.000 projede ACCESS EXCLUSIVE tutan migration **7 sn** (boşta), 8 sn (yük altında). Bu sayı rapora ve runbook'a yazılmalı.
- `:154` bozuk cümle.
- `:214` frontend testi koşulmadı diyor; bu denetimin sayıları eklenmeli.
- `:125` "2 bilinçli fark" eksik; §4'teki tablo esas alınmalı.
- B-1, B-2, M-1 kapanınca §1, §5, §6 güncellenmeli.

### Bloklamayan, düzeltilmesi önerilen

| ID | Sev. | Yer | Bulgu | Düzeltme |
|---|---|---|---|---|
| L-1 | LOW | `Phase14_2B_DecompositionScripts.cs:31` | SQL anahtarı C# ile her zaman aynı değil. Yunanca `ΑΣ` için SQL `ας`, C# `ασ`: `?tag=ΑΣ` taşınan satırı bulmuyor, `ΑΣ`/`ασ` yinelemesi ayıklanmıyor. `U+E000` içeren etiket `İ`'ye çöküyor ve `İ` etiketi kayboluyor. 50 proje üzerinden `ProjectTagRules` eşleniğiyle karşılaştırmada 2 fark (t07, t13), diğer 48 birebir. Rapordaki "C# ile eşit" iddiası yalnız Türkçe fixture'da sınanmış | Migration prod'da donmadan önce: `lower`'dan önce `Σ`→`σ`, koruma için sentinel yerine `İ` parçalı işleme; fixture'a ekle |
| L-2 | LOW | `GatewayApiKeyTests.cs:47` | `catch NpgsqlException`, `PostgresException`'ı da yakalıyor; yorum "yalnız bağlantı hatası" diyor. Kalıcı hata yine son denemede patlar, aralıklı migration hatası gizlenebilir | `when (ex is not PostgresException && attempt < n)` |
| L-3 | LOW | `ShareController.cs:46`, `StudioApplicationService.cs:19` | POST'a yeni 429 geldi ve kova fork/ayar/silme ile ortak (brief yalnız anonim uçları istiyordu); arayüz genel hata gösteriyor (`ToolbarPanel.tsx:207`). 50 linkten sonra en eski link sessizce kapanıyor. İptal satırları hiç temizlenmiyor | `hub-write` politikası; iptal satırlarına saklama süresi |
| L-4 | LOW | `Phase14_2B_DecompositionScripts.cs` | Etiket doğrulaması ekleme ile aynı anahtar fonksiyonunu kullanıyor; alan farkı `RAISE` dalları testle tetiklenmiyor (kendi okumamda mantık doğru). `Down` doğrulaması etiket/paylaşım karşılaştırmıyor; `Down` CHECK'leri temiz veride bile NOT VALID bırakıyor | Negatif test; `Down` sonunda VALIDATE denemesi |
| L-5 | LOW | `ModuleMigrationOrchestrator.cs` | DI'da kayıtlı, hiç çağrılmıyor: ölü kod ve yanıltıcı | En geç D-2'de sil |

## 1. Migration kanıtı

**Üretim yolu (`Namines.API --migrate`, boş DB):** exit 0, 49 migration. Katalog:
- `CloudProjects` adlı ilişki, kısıt, indeks: 0.
- `ShareToken` kolonu: 0. Düz metin token kolonu hiçbir şemada yok.
- `studio.projects`: 10 kolon; 13 kolon fiziksel olarak düşmüş.
- `studio.projects`'e gelen FK: 19, silme davranışları korunmuş. Doğrulanmamış FK: 0.

**Satır satır SQL okuması (`b731850`):** kilit doğrulamadan önce alınıyor; tüm Up tek EF işleminde. Reconcile ayrı işlemde ve tek başına güvenli.
- Doğruladım: yinelenen legacy token'da Reconcile `23505` ile durdu, geçmişe yazılmadı, `ShareToken` kolonu yerinde kaldı.
- AAD proje kimliği olduğu için bayt kopyası doğru.
- Veri kaybı yolu yok. Bilinçli olarak dönmeyenler: düz metin token, normalleşmemiş CSV, `''` şifreli metin → NULL.

**Kötü niyetli seed (`20261030215219`, 50 proje).** Kapsam:
- Virgül, boş, `İ/ı/I/i`, Unicode boşluk, zero-width, tab/newline, 256 ve 4000+ karakter etiket.
- `''` slug (aynı sahipte iki kez), farklı sahipte aynı slug, `Dup/dup`, 5000 karakter slug.
- Self fork, fork döngüsü, silinmiş kaynağa fork (FK kapalı eklendi).
- Motorsuz şifreli metin, şifresiz `AllowDeskSql`, `''` şifreli metin, 100 KB şifreli metin.
- Token + hash, uyumsuz token/hash, yalnız hash, `''` hash, hatalı uzunlukta ve büyük harf hash.
- NOT VALID CHECK'leri aşan satırlar (Visibility=7, StarCount=-1, 5000 karakter açıklama).

Sonuçlar:

| Karşılaştırma | Sonuç |
|---|---|
| Proje çekirdek alanları, repo 12 alan | Fark yok (tek istisna: silinmiş kaynağa işaret eden `f07` → NULL, tasarım gereği; self fork ve döngü korunmuş) |
| Bağlantı | Kayıp 0, şifreli metin birebir |
| Paylaşım | Uyumsuz çiftte çalışan hash korunmuş, yalnız token'da SHA-256 doğru, boşlarda satır yok, 9/9 |
| Etiket (C# karşılaştırması) | 48/50 birebir; 2 fark L-1 |
| CHECK | Aşan satırlar NOT VALID, yeni yazımlar denetleniyor |
| Down | Şema, indeks, FK adları geri; fark yalnız raporda bildirilenler: `Enc` c03/c04/c07 (`''`→NULL), `Fork` f07, `Hash` h01/h05/h07/h10 (hesaplanmış), `Token` (düz metin gitti), `Tags` normalleşmiş |
| Up2 = Up1 | Repo, bağlantı, paylaşım, etiket, proje: EXCEPT farkı 0 |

**En az yetkili rol, seed'li DB:** `roles.sql` + migrator ile Up geçti, tablolar migrator'ın, `namines_app` DML yetkili ve şemada DDL yetkisiz. Kırık nokta M-1.

## 2. Tüketici taraması

- `_context.` veya `Set<>` ile taşınan tablolara modül dışı erişim: yok (yalnız Studio/Hub servisleri, `TenantProjectAuthorityService`, `ConnectionSecretReencryptor`).
- `_studio == null` geri düşüşleri ve üreteçler silinmiş.
- Kalan tek opsiyonel parametre `HubModule`/`StudioModule` kurucuları: veri yolu değil.
- `new CloudProject` 4 yerde; hepsi repo satırını aynı işlemde yazıyor.
- `CloudProject.UserId` değişen tek yer hesap silme ve `OwnerUserId` orada güncelleniyor.
- CLI/MCP taşınan alan kullanmıyor.

## 3. Güvenlik

**Kod okuması:**
- Çözme yalnız `TenantProjectAuthorityService:199` ve rotasyon aracında.
- Gateway önce yetki, sonra üst veri, en son çözme.
- `ResolveForUserAsync` kiracı dışına null.
- Rotasyon yeni tabloda (test gerçek PG).

**Gerçek API, 38/38 (A sahibi, B yabancı):**
- B için özel repo görüntüleme, `_` ile görüntüleme, yıldız, fork, paylaşım oluşturma/iptal, issue listesi: 404.
- Ayar, silme, bağlantı yazma/silme, SQL konsolu bayrağı, gateway şeması: reddedildi.
- Public repoda B, A'nın saklı bağlantısını kullanamıyor. Fork bağlantı ve paylaşım taşımıyor, özel açılıyor.
- Mass assignment (`userId`, `slug`, `encryptedConnectionString`, `starCount`, `ownerUserId`, `forkedFromProjectId`) sync ve PATCH'te etkisiz. B'nin A'nın kimliğiyle sync'i A'yı değiştirmiyor.
- Proje listesi ve repo DTO'sunda şifreli alan yok; özet token gibi kullanılamıyor (404).
- Anonim paylaşım uçlarında 429 var; 192-bit token ile kaba kuvvet pratik değil.
- API log'unda bağlantı parolası eşleşmesi 0. SSRF: özel adres bağlantı isteği 400.

## 4. API uyumu ve frontend

Rotalar aynı, DTO alan adları aynı. Davranış farkları:

| Fark | Frontend etkisi | Değerlendirme |
|---|---|---|
| `Tags` normalleşmiş, etiketsizde `null` | Yok: `repo.tags \|\| ''`, `tags?: string` | Kabul |
| Explore `q` etikette tam eşleşme | Kırılma yok; arama kutusu "tags" diyor, kısmi etiket artık bulunmuyor (`app/hub/page.tsx:179`) | Brief'in kararı; **sahibe bildirilmeli** |
| `?tag=` eklendi | Yok | Kabul |
| POST paylaşım her çağrıda yeni link, eskiler geçerli | Yok: UI her tıklamada POST atıyor | Kabul |
| POST paylaşıma 429 | Genel hata mesajı | L-3 |
| Fork silinince `ForkCount`, hesap silinince `StarCount` düşüyor | Yok | Düzeltme, kabul |

## 5. Test kalitesi

**`git diff 5565811..HEAD -- backend/Namines.Tests`:** silinen doğrulamalar silinen üreteçlere ve varlıklara ait veya yeni tablolara birebir çevrilmiş; zayıflatma bulmadım. Yeni `Skip` yok.

`Phase14_2BFlowTests` ve `Phase14_2BMigrationTests` gerçek PG'de, üretimle aynı ayrı bağlantı ve retry düzeniyle, controller üzerinden; mock yok. Eksik: B-1 ve B-2 senaryoları.

**İki retry:**
- Oracle: yalnız zaman aşımı, doğrulama aynı, hata gizlemiyor. Üründeki 15 sn tavanı ayrı açık iş olarak raporda.
- Gateway: kalıcı hatayı gizlemiyor, filtre fazla geniş (L-2).

## 6. "Yapılmayanlar" listesi

| Madde | B'ye ait mi | Karar |
|---|---|---|
| Varis slug çakışması | Evet | **B-2** |
| Kilit ölçümü | Evet (standart) | Ölçüldü, rapora yazılacak (M-2) |
| Frontend testi | Evet | Koşuldu, geçti; rapora yazılacak (M-2) |
| `ModuleMigrationOrchestrator` | Hayır: brief tek zincir kararı verdi | L-5 |
| `RepoStars` `public`'te | Hayır: brief 4 tablo diyor, Hub taşıması D-1 | D-1'e kayıtlı |
| Vault/Ground `OrganizationId` | Hayır: brief yalnız FK kaymasını istiyor; kod yalnız yazıyor, yetkide okumuyor | D fazı backlog |
| `namines_*` MCP araçları | Bu denetimde de bağlanamadı (CONNECT_TIMEOUT) | Kanıt gerçek PG |
