# 11 — Karar kaydı: ürün sahibiyle yapılan strateji konuşması (2026-10-29 / 30)

> Bu belge, Ekim denetiminden sonraki strateji konuşmasında söylenen her
> şeyin kalıcı kaydı: ürün sahibinin soruları ve yönlendirmeleri, verilen
> cevaplar, ölçülen sayılar ve çıkan kararlar. Ayrıntılı planlar
> 00-10 numaralı dosyalarda. Bu kayıt "neden böyle karar verdik?"
> sorusunun cevabı. Yeni bir oturum sohbet geçmişine değil bu dosyaya
> bakmalı.

---

## 1. Bağlam: Ekim denetimi (2026-10-29)

- **İncelenen:** son bir ayın 44 commit'i (2026-09-28 → 2026-10-27, ~87k satır). 6 paralel inceleme ajanı ve 13 düzeltme ajanı (Sonnet) kullanıldı.
- **Başlangıç:** CI bir aydır hiç yeşil değildi. Backend NuGet açık taramasında (7 High CVE), frontend lint'te (15 hata) düşüyordu, dolayısıyla build ve test adımlarına hiç gelinmiyordu.
- **En ciddi bulgular:**
  - herkesin ortak hesapla girebildiği OAuth "dev simülasyonu";
  - MCP'nin korumasız SQL çalıştırması;
  - Google token doğrulamasının eksikliği;
  - Stripe ödemelerinin kaybolabilmesi;
  - DDL betiklerinde sandbox içinde kabuk çalıştırmaya varan enjeksiyon;
  - CanvasHub `room-` önekiyle yetki atlatma;
  - ekipten çıkarılan kurucunun projeyi yönetmeye devam etmesi;
  - UI genelinde sahte başarı mesajları ve uydurma veri.
- **Sonuç:**
  - PR #2 ile 88 commit `main`'e squash merge edildi (`be9a234`);
  - CI'ın 4 job'ı ilk kez yeşil oldu; Docker'lı integration testleri ve `RunTests` 19/19 dahil;
  - yerelde 2536 backend testi, 280 frontend testi geçti.
- **Ayrıntı:** [last-review/04-EKIM-DENETIMI.md](../last-review/04-EKIM-DENETIMI.md).

## 2. Soru: "Proje şu an nasıl, güzel mi?"

**Verilen cevap (özet, ayrıntı [01](01-PROJE-DEGERLENDIRMESI.md)):**

- **Teknik derinlik sıra dışı.** Ürünün asıl kozu Change Review'ın risk katmanları ve değişikliği gerçek motorda kanıtlaması. Rakiplerin çoğu "şema üret" der; Namines "bu değişiklik canlıda neyi kırar, kanıtla" der. Buna ek olarak kanonik IR, MCP/CLI, test kültürü ve belgelenmiş güvenlik bilinci var.
- **Zayıf yan genişlik:** 1-2 kişiye 14 ürün yüzeyi düşüyor. Denetimdeki sorunların çoğu (sahte başarı, bir ay kırmızı CI) bu genişlikten çıktı. Konumlandırma bulanık, üretim altyapısı henüz "ürün" değil (`sslip.io`, `namines.com` yok), ölçüm yok.
- **Öneri:** yeni platformlardan önce kısa bir **ürünleştirme turu**. Ayrıca 6 kural yazılmalı:
  1. hata gizlenmez;
  2. uydurma sayı gösterilmez;
  3. yetki sunucuda kontrol edilir;
  4. CI yeşil değilse merge yapılmaz;
  5. her yüzeyin bir ölçümü olur;
  6. güvenlik varsayılanı kapalıdır.

## 3. Soru: "Masaüstü, mobil ve eklenti tasarlayacağız; entegrasyonlar gerekli; kapsamlı planla"

**İlk turda verilen plan:** 00-08 numaralı dosyalar. Ana fikirler:

- Her platform çekirdek kamaya **bir işle** hizmet eder.
- Üç platformun **ortak önkoşulu** bugün kodda yok ([02](02-ORTAK-TEMEL.md)). Ölçülen durum:
  - OAuth 2.1 yetkilendirme sunucusu: **yok**;
  - refresh token: **yok**;
  - device flow: **yok**;
  - passkey: **yok**;
  - cihaz kaydı ve push: **yok**;
  - `/api/v1`: **yok**;
  - var olanlar: GitHub ve Google girişi, TOTP MFA, SignalR.
- Bu yüzden yol haritasında ilk iş temel ([08](08-YOL-HARITASI.md)).
- 18-ekosistem planındaki "Neon/PlanetScale OAuth anında onaylanır" iddiası **doğrulanmamış** olarak işaretlendi.

## 4. Ürün sahibinin yönlendirmeleri (2026-10-30) ve cevaplar

### 4.1 "OAuth isteyen her şeyi alalım"

| Yönlendirme | Karar | Belge |
|---|---|---|
| GitHub, Google, Microsoft, LinkedIn, Supabase, Firebase, Cloudflare vb. sistemi ileri taşıyan her şey | **Kabul.** Giriş (G1-G8) ve bağlantı (C1-C21, A1-A4) olarak iki ayrı katalog oluşturuldu | [09](09-OAUTH-KATALOGU.md) |

**Eklenen ve dikkat çekilen noktalar:**
- **Apple ile giriş iOS'ta zorunlu:** App Store kuralı, uygulamada başka sağlayıcıyla giriş varsa bunu istiyor.
- **Cloudflare:** genel self-servis 3. parti OAuth beklenmemeli; v1'de scoped API token kullanılacak.
- **Neon ve PlanetScale:** OAuth partner veya erişim başvurusu gerekebilir; v1'de token kullanılacak.
- **Google:** geniş scope'lar (`cloud-platform` gibi) haftalar süren bir doğrulamaya giriyor; başvuru Faz 0'da yapılacak.
- **Microsoft:** publisher verification olmadan onay ekranında "doğrulanmamış" uyarısı çıkıyor.
- **Hesap birleştirme kuralları:** Ekim denetimindeki ele geçirme açıklarından çıkarılan 5 zorunlu kural yazıldı ([09](09-OAUTH-KATALOGU.md) §4).
- **Bilinçli olarak alınmayanlar:** Facebook ve X/Twitter girişi (K-15).

### 4.2 Eklenti: "Supabase ve Firebase'den otomatik veri/tablo/DB çekme; Namines'i tam zamanlı kontrol; önüne geçmek değil entegre çalışmak"

| Yönlendirme | Karar | Belge |
|---|---|---|
| Otomatik çekme | **Kabul.** Sayfa açılınca proje algılanır. Bağlıysa şema, tablolar, RLS/Rules ve drift otomatik gelir; veri istek üzerine sayfalı gösterilir | [04](04-TARAYICI-EKLENTISI.md) §2 |
| Tam zamanlı Namines kontrolü | **Kabul.** Yan panel Namines'in kendisi: projeler, şema, CR, Desk, Vault, Ground, Flow ve canlı bildirimler | [04](04-TARAYICI-EKLENTISI.md) §3 |
| Entegre çalışmak | **Kabul**, konumlandırmaya yazıldı | [04](04-TARAYICI-EKLENTISI.md) başlık, [00](00-README.md) |

**Eklenen kurallar ve gerekçeleri:**
- **Veri resmi API'den gelir, sayfadan kazınmaz.** Konsol arayüzü değişse bile çalışır, çerezlere ve gizli değerlere dokunulmaz, mağaza incelemesinden geçer.
- **Okunan satırlar Namines'te saklanmaz (K-13).** Yalnızca açık "yedekle" veya "içe aktar" eylemiyle kopyalanır. Entegrasyonun güven sözü bu.
- **Supabase `service_role` anahtarı kullanılmaz.** RLS'i atlayan bir anahtar dolaşıma girmez.
- **Eklenti Safe/Risky onaylayabilir (K-12); Breaking/Destructive ve DB kontrol eylemleri mobil imza ister.** Gerekçe: tarayıcı ikinci bir güven alanı değil.
- **Firebase eklentinin ilk sürümüne alındı;** Faz 2'de öne çekildi.

### 4.3 Mobil: "Expo mu bilmiyorum; iOS ve Android'de sıkıntısız olmalı; amaç DB'yi yönetmek, kapatmak, talimatları işlemek, analiz edip sunmak"

| Yönlendirme | Karar | Belge |
|---|---|---|
| Teknoloji | **Expo**, ama Expo Go değil **development build** ile; güvenlik açısından kritik native modül (donanım imzası) **kendi kodumuz**, yaklaşık 300 satır | [05](05-MOBIL.md) §1 |
| DB'yi yönetmek ve kapatmak | **Kabul.** Pano + kontrol eylemleri: duraklat/başlat, salt-okuma, sorgu öldürme, bağlantı kesme, dondurma, acil durum şalteri. Hepsi imzalı ve geri alınabilir (K-14) | [05](05-MOBIL.md) §2.1-2.2 |
| Talimatları işlemek | **Kabul.** Talimat → yapılandırılmış plan → risk → onay → uygulama. **AI hiçbir zaman doğrudan çalıştırmaz** | [05](05-MOBIL.md) §2.3 |
| Analiz edip sunmak | **Kabul.** Öneri kartları: eksik index, güvenlik, drift, kota, yedek sağlığı, maliyet, anomali | [05](05-MOBIL.md) §2.4 |

**Neden Expo?** Flutter, Kotlin Multiplatform ve iki ayrı native uygulamayla karşılaştırıldı:
- Mevcut TS kod tabanıyla `@namines/sdk`, i18n ve Zod paylaşımı yalnızca Expo'da birebir mümkün.
- EAS ile iOS derlemesi için Mac gerekmiyor.
- Eski Expo'nun sorunları (Expo Go, native modül eklenememesi) development build ve CNG ile aşılıyor. Gerekirse `prebuild` ile kilitlenmeden çıkılabiliyor.
- **Riskler ve önlemleri:**
  - 3. parti kütüphanenin bakımı bırakılabilir → kritik modülü biz yazıyoruz;
  - Expo SDK yükseltmeleri iş çıkarır → her yükseltme ayrı iş kalemi, Maestro e2e testleriyle;
  - StrongBox her Android cihazda yok → TEE'ye düşülür ve güven seviyesi buna göre işaretlenir.
- **Flutter'ı ne zaman seçerdik:** ekipte Dart bilen biri olsaydı ve web ile kod paylaşımı önemsiz olsaydı. İkisi de değil.

### 4.4 Masaüstü: "Tauri iyi, ama MCP her şeye erişim vermiyor. MCP'yi çok mu geliştirelim, yoksa iki MCP mi yapalım (masaüstü + IDE)?"

**Ölçüm:** MCP **13 araç** açıyor; API'de 43 controller ve **211 uç** var. MCP sistemin yaklaşık **%6**'sını kapsıyor. Endişe doğrulandı.

**Karar (K-11): tek motor, üç yüzey** ([10](10-MOTOR-VE-MCP-MIMARISI.md)):

| Yüzey | Kim kullanır | Kapsam | Protokol |
|---|---|---|---|
| `namines-agent` | Masaüstü (Tauri sidecar) | **Tüm** işlemler (yerel + bulut) | stdio JSON-RPC 2.0, akış, iptal, olay |
| `namines-mcp` | IDE'ler (Claude Code, Cursor, VS Code, Windsurf, Zed) | 13 → ~45 araç, toolset + profil (`readonly`/`developer`/`full`) | MCP stdio |
| Remote MCP | Claude.ai, ChatGPT | Bulut işlemleri | MCP Streamable HTTP + OAuth |

**Gerekçeler:**
- **Neden masaüstü MCP konuşmamalı:** MCP dil modeli araçları için tasarlanmış. Sonuç metin olarak döner, akış yok, ve araç az olmalı (çok araç model seçim kalitesini düşürür). Masaüstünün ihtiyacı ise tipli veri, 100 bin satır akışı, iptal ve olay aboneliği. "Masaüstü için ikinci bir MCP" de bu kısıtları masaüstüne taşırdı.
- **Neden "iki MCP" yerine tek motor:** Ürün sahibinin doğru tespitini (masaüstü ile IDE'nin ihtiyacı farklı) korur, ama mantığı iki kez yazmaz. Her işlem **İşlem Kayıt Defteri**'nde bir kez tanımlanır (ad, şema, risk, scope); üç yüzey de bu kayıttan üretilir.
- **Ek kazanç:** Denetimde bulunan "MCP açıklaması 'Safely' diyor ama her şeyi çalıştırıyor" ve "parametre adı uyuşmazlığı" sınıfı hatalar yapısal olarak kapanır.
- **Güvenlik:** Destructive bir işlem MCP veya Remote MCP üzerinden **asla** doğrudan çalışmaz; CR açılır ve onay bağlantısı döner. Kontrol eylemleri yalnızca **talep** olarak oluşturulur ve mobil imza ister.

## 5. Soru: "DB'de kaç tablo var ve DB gerçekten sağlam mı?"

**Ölçüm (2026-10-30, `main` = `be9a234`):**
- Yöntem: `dotnet ef dbcontext script` ile mevcut modelden üretilen tam DDL.
- **Tek DbContext:** `AuthDbContext` (IdentityDbContext). **37 migration**.
- **`has-pending-model-changes` → model ile migration'lar arasında kayma yok.**
- **44 tablo:**

| Şema | Tablo | Örnekler |
|---|---|---|
| `public` | 34 | Kimlik (7 AspNet* tablosu), CloudProjects, Branches, SchemaVersions, ChangeRequests (+Approvals, AuditLogs), Organizations, OrganizationMembers, TeamInvites, GatewayApiKeys, GatewayTablePermissions, GatewayAuditEntries, SqlExecutionAudits, UsageEvents, UserAIQuotas, UserAIPolicies, GlobalAiUsages, OrgAiUsages, UserBillingSettings, StripeWebhookEvents, FeatureFlags, AiToolRoutes, Feedbacks, CrossDatabaseRelations, RepoIssues, RepoIssueComments, RepoStars |
| `desk` | 3 | desk_handoff_tokens, saved_queries, sql_query_history |
| `flow` | 3 | automation_rules, automation_actions, automation_run_logs |
| `namines_vault` | 3 | vault_backups, vault_restores, vault_schedules |
| `ground` | 1 | ground_databases |

- **Kısıtlar:**
  - 46 foreign key: 31 CASCADE, 13 RESTRICT, 2 SET NULL;
  - 46 index + 16 unique index;
  - **0 CHECK kısıtı**.

**Sağlamlık değerlendirmesi:** Derin denetim ayrı bir raporda,
[last-review/05-DB-SAGLAMLIK-DENETIMI.md](../last-review/05-DB-SAGLAMLIK-DENETIMI.md):
46 bulgu, DB-01 → DB-46.

**Genel puan: 5.5 / 10.** Şema modeli ve CI sağlam; zayıf olan yaşam
döngüsü (silme, saklama, yedek, kısıtlar).

| Alan | Puan |
|---|---|
| Referans bütünlüğü | 5 |
| Silme davranışı | 4 |
| Index'ler | 6 |
| Kısıtlar ve tipler | 4 |
| Eşzamanlılık | 7 |
| Benzersizlik | 6 |
| Migration hijyeni | 7 |
| DB seviyesinde güvenlik | 7 |
| Operasyon | 4 |

**Gerçekten iyi olanlar:**
- model kayması yok; testler gerçek Postgres'e karşı `MigrateAsync` çalıştırıyor;
- üretimde migration ayrı bir adımda yapılıyor;
- Stripe idempotency örnek düzeyde;
- kota düşümü tek atomik UPDATE;
- tüm zamanlar `timestamptz`, para `numeric`;
- bağlantı dizeleri AES-GCM ile şifreli;
- davet ve handoff token'ları hash'li.

**En kritik bulgular** (ilk üçü koordinatör tarafından ayrıca doğrulandı):
1. `GatewayAuditEntries` tablosunda hiç index yok. `CloudProjects.UserId` pratikte indexsiz (yalnızca filtreli `(UserId, Slug)` index'i var).
2. **Hesap silme kod yolu yok.** Eklenirse `AspNetUsers`'a giden 9 RESTRICT FK silmeyi engeller. `CloudProjects.UserId` CASCADE ise takım projelerini de siler.
3. E-posta benzersizliği yalnızca uygulama kodunda (`RequireUniqueEmail` açık değil). Kişisel organizasyon oluşturmada unique kısıt yok, yarış durumunda çift kayıt oluşabilir.
4. Proje silinince `ChangeRequests` ve `ChangeRequestAuditLogs` iz bırakmadan siliniyor; silme işleminin kendisi de denetim kaydına yazılmıyor.
5. `vault_*` ve `ground_databases` tablolarının projeye FK'si yok. Proje silinince şifreli yedekler ve sağlayıcı tarafındaki Ground DB'si sahipsiz kalıyor.
6. Kontrol DB yedeği yalnızca yerel ve şifresiz; `.env` (şifreleme anahtarı) dump ile aynı arşivde; geri yükleme testi ve PITR yok.
7. Saklama süresi yalnızca 2 tabloda tanımlı. Audit, usage ve webhook tabloları sınırsız büyüyor.
8. 0 CHECK kısıtı; kullanıcı girdisi sütunları sınırsız `text`.
9. Bağlantı havuzu ayarı yalnızca dokümanda var, kodda yok.
10. `ShareToken` ve TOTP sırrı / kurtarma kodları düz metin olarak saklanıyor.

**Hızlı kazanımlar:**
- eksik index'ler + unique token'lar (~2 saat);
- havuz ve timeout ayarları (~3 saat);
- unique e-posta, kişisel organizasyon, `KeyHash` (~yarım gün).

Tam düzeltme sırası raporun §10'unda.

## 6. Açık kararlar

Güncel liste [08 — Senden beklenen kararlar](08-YOL-HARITASI.md#senden-beklenen-kararlar): K-1 → K-15. Bu konuşmada ürün sahibi açıkça yön verdi:
- **K-3:** mobil teknoloji, Expo önerildi;
- **K-11:** tek motor;
- **K-12:** eklentide onay sınırı;
- **K-13:** veri saklanmaması;
- **K-14:** kontrol eylemlerine imza;
- **K-15:** Facebook/X girişi alınmaması.

Bu kararlar ürün sahibinin onayıyla kesinleşir. O zamana kadar 08'deki "Önerim" sütunu geçerli varsayılır.
