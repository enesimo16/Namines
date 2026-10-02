# Doğrulanan kontroller

Bu incelemede **kod okunarak** doğru çalıştığı kanıtlanan kontroller. Amaç:
bir sonraki denetimin aynı yerleri baştan taramakla zaman kaybetmemesi.

Bir şeyin burada olması, o dosyanın **tamamının** denetlendiği anlamına gelmez —
yalnızca yazan kontrolün doğrulandığı anlamına gelir.

## Bu düzeltme turunda TESTLE kanıtlananlar (2026-09-30 → 10-02)

Yukarıdaki bölümler **kod okunarak** doğrulandı. Buradakiler ayrı: her biri
bir regresyon testiyle kilitlendi ve çoğu **mutasyon testiyle** — düzeltme geri
alınıp testin gerçekten kırmızıya döndüğü görülerek — doğrulandı.

| Kontrol | Test | Mutasyon |
|---|---|---|
| DDL tanımlayıcı kaçışı (6 motor, FK/index/UNIQUE/enum dahil) | `IdentifierEscapingTests` (11) | ✅ |
| Geçersiz tanımlayıcı → 400, iç ayrıntı sızmıyor | `CompileInvalidIdentifierTests` (6) | — |
| `SELECT ... INTO` ve gövdeye gömülü yazma fiilleri okuma SAYILMIYOR | `SqlStatementKindTests` (+13) | ✅ |
| Gateway `expand` izinsiz tabloyu okuyamıyor | `GatewayExpandPermissionTests` (5) | ✅ |
| Admin ham-SQL/dışa-aktarım anahtarı basamıyor | `GatewayKeyRawSqlEscalationTests` (4) | — |
| `AllowDeskSql` kapalıyken anahtar yolu da reddediliyor | `GatewayKeyStoredConnectionSqlTests` (3) | — |
| Çıkarılan üyenin anahtarı doğrulanmıyor + açıkça iptal ediliyor | `RemovedMemberKeepsAccessTests` (7) | ✅ |
| Denetim kaydına başkasının `projectId`'si yazılamıyor | `AuditProjectIdSpoofingTests` (4) | ✅ |
| SQLite yoluyla sunucuda dosya OLUŞMUYOR | `ExecutorSqliteFileCreationTests` (7) | ✅ gerçekten dosya oluşuyordu |
| Kurtarma yolu saklı sağlayıcı kimliğini geri veriyor | `GroundCancelDeleteTests` + `GroundIdentifierTests` (12) | ✅ |
| Onay sayısı CR açılışında sabitleniyor | `ApprovalCountPinningTests` (4) | ✅ |
| Stripe tekrar iletim + sıra dışı teslimat | `StripeWebhookIdempotencyTests` (4) | ✅ (sıra yarısı) |
| AI kotası eşzamanlı rezervasyonda tavanı aşmıyor | `AiQuotaServiceTests` (+1, gerçek PostgreSQL) | ✅ 3 kat aşım ölçüldü |
| `Outputs/` temizliği (sahipsiz dosyalar dahil) | `SandboxOutputRetentionTests` (7) | kısmi |
| Anonim `/vault/health` sunucu yolu sızdırmıyor | `VaultHealthDisclosureTests` (4) | ✅ |
| CoderAI paketi: paket başına parola + loopback bağlama | `CoderAiPackageSecretsTests` (10) | ✅ 8/10 |
| Bot PR yorumu markdown kaçışı | `PullRequestReviewEscapingTests` (4) | — |
| NVLT1 varsayılan reddi + açık izinle okunabilirlik | `BackupCipherTests` | — |
| **NVLT3: yedek kimliği AAD'ye bağlı** (içerik, uzunluk değil) | `BackupCipherTests` (+11) | ✅ |
| MCP yalnızca yerel/özel ağa bağlanıyor | `McpConnectionGuardTests` (13) | — |
| Varsayılan branch: GET okur, POST oluşturur | `DefaultBranchVerbTests` (5) | — |
| **Gateway hız limiti süreçler arası paylaşılıyor** | `GatewayRateLimiterDistributedTests` (4, gerçek Redis) | ✅ |

> **Mutasyon testi iki kez kendi testlerimin yetersiz olduğunu gösterdi:**
> C-1'de ilk kaçış geçişim 34 noktayı atlamıştı (test kırmızı döndü), ve M-8'de
> NVLT3 testleri **yanlış sebepten** geçiyordu — AAD'nin BOYUTU kimlik uzunluğuna
> bağlı olduğu için içerik hiç ölçülmemiş oluyordu. Bir testin geçmesi, DOĞRU
> ŞEYDEN geçtiği anlamına gelmiyor.

---

## Yetkilendirme ve kiracı izolasyonu

- **`OrgAccess`** tek kaynak; `GetRoleAsync` org üyeliğinden çözüyor, eski
  `OrganizationId == null` satırını kurucu-Owner sayıyor, `CanEditAsync` `Billing`'i
  açıkça dışlıyor, `CountVotingMembersAsync` Viewer/Billing'i saymıyor (2-kişi kuralı
  Viewer eklenerek gevşetilemiyor).
- **`BranchController`** sekiz ucun hepsinde "önce yükle, sonra ana projeyi doğrula"
  deseni; iç içe kimlikler istekten değil yüklenen satırdan türetiliyor.
- **`VaultController`** `FindBackupAsync` hem `Id` hem `ProjectId` ile eşleşiyor;
  rol merdiveni tutarlı (görüntüleme=üye, oluştur/doğrula=Editor, indir/sil/zamanla=Admin,
  **restore=Owner + yazılı veritabanı adı onayı**, sunucu tarafında ordinal karşılaştırma).
- **`GroundController`** provision/delete/cancel hepsi `GetRoleAsync == Owner`;
  kayıt her zaman `ProjectId` ile bulunuyor. `GatewayKeyController.ClearProjectConnection`
  Ground-yönetimli bağlantının ayrılmasını reddediyor (zayıf kapı kapalı).
- **`ChangeRequestController`** her eylem CR'ı önce yükleyip `cr.ProjectId` üzerinden
  yetkilendiriyor (istekten gelen proje kimliği kullanılmıyor).
- **`AutomationController`** yerel `OwnsProjectAsync` en dar kontrol — aşırı yetki veremez.
- **Anahtar→proje bağlaması:** anahtar varsa proje her zaman `key.ProjectId`'den,
  istekten değil. A projesi için verilmiş anahtar B'nin saklı bağlantısına yöneltilemiyor.
- **Tablo izinleri** list/detail/create/update/delete/bulk-delete/export'ta uygulanıyor;
  export ayrı bir izin istiyor; `rpc` fonksiyon adını `forWrite: true` ile aynı kontrolden
  geçiriyor. İzin satırı yoksa **fail-closed**.
- **API anahtarı iptali önbelleklenmiyor** — her istekte DB'ye gidiliyor, tam özet
  sabit zamanlı karşılaştırılıyor, `RevokedAt`/`ExpiresAt` aynı kod yolunda.
- **`ProjectMemberController`** Owner katmanı düzeltmesi eksiksiz: Owner vermek,
  bir Owner'ın rolünü değiştirmek ve onu çıkarmak Owner istiyor; `Enum.IsDefined`
  tanımsız rolü engelliyor; son-Owner koruması iki yolda da var.
- **`TeamController`** davetler Owner veremiyor; jetonlar özetlenmiş, koltuk sayımı
  bekleyenleri de içeriyor.

## SQL ve veritabanı

- **Gateway dinamik SQL temiz:** her kolon yolu (`BuildProjection`, `BuildWhere`,
  sıralama, PK, fonksiyon adı) `ValidateIdentifierOrThrow` (`^[A-Za-z_][A-Za-z0-9_]*$`)
  **ve** motora göre ayırıcı ikileyen `Quote`'tan geçiyor; operatörler enum,
  değerler `DbParameter`, sıralama yönü enum.
  > Dikkat: bu `Quote` doğru, **DDL üreticilerininki değil** (bkz. C-1).
- **Desk salt-okunur katmanlı:** tek ifade + baş anahtar kelime + yasak kelime
  (`INTO` dahil) + Owner-only + varsayılan kapalı. Kalan risk kodda dürüstçe belgeli.
- **`FindDisallowedHost` (`Addr=` düzeltmesi eksiksiz):** regex artık
  `server|host|data source|datasource|address|addr|network address` tanıyor **ve**
  sürücü builder'ının çözdüğü host ayrıca aday ekleniyor; boş aday listesi **deny**.
- **`SchemaImpactAnalyzer`** tablo/kolon silme, yeniden adlandırma, tip daraltma,
  NOT NULL, ilişki/indeks silme ve cascade sorunlarını yakalıyor; `ComputeOverallRisk`
  monoton yükseltiyor — bir silme `Safe`'e düşemiyor.
- **`Safe` otomatik onay** `previousVersion is not null && Safe && opt-in` ile kapılı;
  ilk sürümde taban olmadığı için her şeyin "Safe" görüneceği tuzak açıkça ele alınmış.

## Kriptografi ve SSRF

- **Vault NVLT2:** dosya başına rastgele tuz → PBKDF2-SHA256 600k → dosyaya özel anahtar;
  blok başına taze 96-bit nonce; AAD = `magic|tuz|indeks|final`, indeks sunucuda yeniden
  hesaplanıyor. Silme/yeniden sıralama/çoğaltma/başka dosyadan yapıştırma etiketi bozuyor;
  kesme hem kısa başlık okumasıyla hem final-bayrağı şartıyla yakalanıyor; final bloktan
  sonra veri reddediliyor. Boş girdi ve tam blok katı doğru ele alınmış.
- **`SsrfGuard` adres aralıkları (IPv6 düzeltmesi eksiksiz):** loopback, `0.0.0.0/8`,
  `10/8`, CGNAT, `127/8`, `169.254/16`, `172.16/12`, `192.168/16`, `192.0.0/24`,
  `198.18/15`, `>= 224`; v6'da `::`, `fec0::/10`, link-local, `fc00::/7`, multicast,
  IPv4-mapped, NAT64, 6to4. `IsHostSafe` **tüm** çözülen adreslerin public olmasını istiyor;
  `IsHostPrivate` bilinçli olarak tersi değil.
- **Webhook ve URL içe aktarma:** `AllowAutoRedirect = false` ikisinde de; webhook her
  çalıştırmada (test dahil) yeniden doğrulanıyor, 3xx `Skipped` olarak kaydediliyor ve
  gövde asla loga girmiyor (yanıt okuyan proxy değil); başlık adı doğrulanıyor,
  değerlerden CR/LF atılıyor.
- **AI sağlayıcıları:** tüm temel adresler derleme-zamanı sabiti — arayüzdeki
  "özel LLM yönlendirme" bir base-URL sinki üretmiyor.
- **Vault S3:** `ServiceUrl`/bölge/anahtarlar yalnızca `IConfiguration`'dan; istek etkili değil.
- **Docker daemon uçları:** beş kurulum yerinde de sabit socket/pipe, env/config override yok.

## Konteyner ve dosya

- **`docker.sock` hiçbir yere mount edilmiyor** (kod + iki compose dosyası doğrulandı);
  `Binds`, `Mounts`, `Privileged`, `CapAdd`, `PidMode`, `NetworkMode=host` yok.
  Vault doğrulama container'ı boş `HostConfig` kullanıyor; temizlik `finally`'de.
- **Yedek parolaları** komut satırında değil `Env` ile geçiyor.
- **Güvenilmeyen arşiv çıkarma yok:** her zip yeri `Create`/`Update`; tek çıkarma
  Docker'ın ürettiği arşiv ve .NET hedef dışına çözülen girdileri reddediyor.
- **İş kimliği yetkisi:** `Docker` ve `CoderAI` uçlarının altısında da
  `IsValidJobId` (Guid) + `[Authorize]` + `job.UserId` karşılaştırması.

## Kimlik doğrulama

- **JWT:** issuer/audience/lifetime/signing key doğrulanıyor; dev anahtarı
  `!IsDevelopment()` **ve** eşitlik karşılaştırmasıyla kapılı; `Jwt:Key` yoksa
  Development dışında başlatma başarısız.
- **Login/MFA:** bilinmeyen e-posta ile yanlış parola aynı mesajı veriyor;
  `AccessFailedAsync` yanlış parolada **ve** yanlış TOTP'de artıyor; `mfa/disable`
  geçerli kod istiyor; kurtarma kodları tek kullanımlık; **"MFA-bekleyen jeton" türü yok**
  (o kimlik sınıfı tamamen ortadan kaldırılmış).
- **CSRF:** özel başlık şartı form/img/script ile takılamıyor; `fetch` ile takmak
  preflight'ı zorluyor ve CORS allowlist'ine çarpıyor. Webhook'lar ve login/register
  çerez taşımadıkları için haklı olarak dışında.
- **GitHub webhook:** ham gövde model binding'den **önce** okunuyor, `FixedTimeEquals`,
  sır yoksa fail-closed, red yanıtı "sır yok" ile "imza yanlış"ı ayırmıyor.
- **Stripe:** ham gövde imza doğrulamasından önce; müşteri kimliği
  `session.ClientReferenceId`'den (sunucu tarafında set ediliyor); plan fiyat
  kimliğinden sunucuda çözülüyor — istemciden plan/kota kabul eden uç yok.
- **Desk handoff jetonu:** 256-bit, özetlenmiş, 30 sn TTL, `ExecuteUpdateAsync` ile
  atomik tek kullanımlık; route POST-only, `Sec-Fetch-Site`/`Origin` ile çapraz-site
  reddi, `jsonForScript` `<` kaçışı, `Cache-Control: no-store`.
  > Jetonun kendisi sağlam; **verdiği şey** sorunlu (bkz. M-15).

## AI / agent

- **Agent tool yüzeyi gerçekten yetkisiz:** üç araç (`validate_schema`,
  `get_column_info`, `preview_ddl`) saf in-process hesaplama; hiçbiri bağlantı açmıyor,
  dosya yazmıyor, proje kimliği almıyor, ağa çıkmıyor. Sonuçlar uzunluk sınırlı.
- **MCP `open_change_request`** model-seçimli `projectId`'yi sunucuda `CanEditAsync`'ten
  geçiriyor — model kiracı geçemiyor.
- **CLI alternatif yetki yolu değil:** tüm komutlar yerel ve salt-okunur; jeton saklamıyor,
  sunucuya yazan komutu yok.
- **AI üretimi SQL asla otomatik uygulanmıyor:** `ExecuteScriptAsync`'in iki çağıranı var —
  kullanıcının kendi bağlantısı (denetlenen) ve Launch (yalnızca introspection'ın boş
  olduğunu doğruladığı taze veritabanına).
- **Bot depo içeriğini modele vermiyor:** `NslParser` + deterministik analiz;
  `BotCommandParser` yalnızca altı bilinen komutu kabul ediyor.

## Frontend

- **Mermaid `securityLevel: 'strict'`** — paylaşım linkiyle saklı XSS senaryosu
  yorumda gerekçe olarak yazılmış. `layout.tsx`'teki tek `dangerouslySetInnerHTML`
  sabit tema önyüklemesi. `app/share/[token]/` altında HTML sink yok.
- **Ana uygulama JWT'si `localStorage`'a yazılmıyor** (`partialize` ile dışlanmış),
  çerezle taşınıyor.

## Arka plan işleri

- Hepsi kendi `IServiceScope`'unu açıyor, öğe başına hata yakalıyor.
- `VaultScheduleBackgroundService` satırı koşullu `ExecuteUpdateAsync` ile **atomik**
  sahipleniyor → çok-instance çift çalıştırma imkânsız.
- `VaultBackupWorker` kaydı yeniden yüklüyor, `Running` değilse çalışmıyor, projesi
  silinmişse kaydı başarısız işaretliyor; çökme artığı `Running` satırları 6 saat
  eşiğiyle uzlaştırılıyor.
- `AutomationExecutor` projeyi yeniden yüklüyor ve yalnızca `Enabled` kuralları çekiyor.
