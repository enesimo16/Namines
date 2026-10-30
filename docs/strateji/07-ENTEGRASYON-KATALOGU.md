# 07 — Entegrasyon kataloğu: hangi sağlayıcı, ne sırayla, ne kadar?

> Çerçeve [06](06-ENTEGRASYON-MIMARISI.md)'da. Bu belge sağlayıcı sağlayıcı
> plan. **Uyarı:** Sağlayıcıların OAuth programları, API'leri ve onay
> süreçleri sık değişiyor. "Kimlik" sütunu 2026 itibarıyla bilinen durum.
> Her bağlayıcıya başlamadan önce sağlayıcının güncel dokümanı doğrulanır.
> `18-EKOSISTEM` §9.2'deki "anında onay" iddiaları (özellikle Neon ve
> PlanetScale OAuth için) **doğrulanmadı**. Bu programlar partner başvurusu
> isteyebilir.

---

## Öncelik özeti

| Katman | Sağlayıcı | Neden bu sırada | Efor |
|---|---|---|---|
| **K1** | Supabase | Ground'da zaten var; kullanıcı kitlesi Namines'le birebir örtüşüyor; RLS = güvenlik hikâyesi | M |
| **K1** | Neon | Ground'da zaten var; branching Namines branch modeliyle doğal eşleşir | M |
| **K1** | Genel Postgres/MySQL (bağlantı dizesi) | Bugün var (BYODB); çerçeveye taşınır, tüm "küçük" sağlayıcıları kapsar | S |
| **K2** | Firebase (Firestore + Data Connect) | Büyük kitle; şemasız olduğu için Namines'in "şemaya dönüştür" değeri yüksek | L |
| **K2** | PlanetScale | Deploy request ↔ Change Review birebir; MySQL + Postgres | M |
| **K2** | Cloudflare D1 | Edge/SQLite kitlesi; SQLite motoru zaten var | M |
| **K2** | Turso (libSQL) | Aynı (SQLite) | S-M |
| **K3** | MongoDB Atlas | Büyük kitle, şemasız; Firestore çıkarım motoru yeniden kullanılır | L |
| **K3** | AWS (RDS/Aurora, DynamoDB) | Kurumsal; IAM karmaşık | L |
| **K3** | Google Cloud SQL / AlloyDB | Firebase'den sonra doğal | M |
| **K3** | Azure Database (Postgres/MySQL/SQL) | MSSQL motoru zaten var | M |
| **K4** | Railway, Render, DigitalOcean, Heroku, Aiven, Crunchy, Xata, Nhost, Appwrite, PocketBase | Çoğu K1 "genel Postgres" ile kapsanır; keşif API'si talep oldukça | S her biri |

S ≈ 1 hafta · M ≈ 1.5-2 hafta · L ≈ 3+ hafta (1 geliştirici + ajanlar).

---

## 1. Supabase (K1)

- **Kimlik:** Supabase OAuth (Management API için "OAuth Apps"), salt-okuma ve yazma scope'larıyla. Yedek yol: kişisel erişim token'ı.
- **Keşif:** organizasyonlar → projeler → bölge, durum.
- **Introspect:**
  - `public` ve kullanıcı şemaları IR'a alınır;
  - `auth.users`'a giden FK'ler **dış referans** olur;
  - eklentiler (`pgvector`, `postgis`) IR'a kaydedilir.
- **Politikalar (en büyük değer):**
  - RLS durumu ve politika metinleri okunur;
  - risk motoruna kurallar eklenir:
    - "RLS kapalı + `anon` rolüne SELECT izni" → **Breaking**;
    - "politika `true` dönüyor" → **Risky**;
    - "`service_role` istemci kodunda kullanılıyor" → Affected Code taraması.
- **Uygula:** Review'dan geçmiş DDL, Management API'nin SQL çalıştırma ucu ile ya da Supabase migration dosyası olarak (CLI uyumlu `supabase/migrations/*.sql` eject hedefi) uygulanır.
- **Branching:** Supabase branching açık projelerde preview branch ↔ Namines branch eşlemesi.
- **Yedek:** Vault mevcut Postgres sağlayıcısıyla. Denetimde `namines_vault` şema ayrımı Supabase'in kendi `vault` şemasıyla çakışmayı zaten çözdü.
- **Eklenti:** `supabase.com/dashboard` panel entegrasyonu ([04](04-TARAYICI-EKLENTISI.md) §3.2).
- **Ground ile ilişki:** Ground'un Supabase sağlayıcısı (Namines adına proje açma) ayrı kalır. Bir kullanıcı hem Ground DB'sine hem kendi Supabase hesabına sahip olabilir.

## 2. Neon (K1)

- **Kimlik:**
  - Neon API anahtarı (kullanıcının oluşturduğu) **v1 yolu**;
  - Neon OAuth, partner programı onaylanırsa v2 (doğrulanacak).
- **Keşif:** projeler → branch'ler → veritabanları → roller.
- **Kısa ömürlü kimlik:** Her işlem için branch'e özel rol ve parola alınabiliyor. Kalıcı parola saklanmaz.
- **Branching (öne çıkan özellik):**
  - Namines'te CR açılınca Neon'da otomatik **branch** açılır, DDL orada uygulanır, "Run Tests" gerçek veri kopyasında koşar.
  - Bugünkü boş container yerine gerçek veriyle kanıt: **ürün farkı**.
  - CR kapanınca branch silinir.
- **Uygula:** Review sonrası ana branch'e DDL.

## 3. Firebase (K2): iki ayrı ürün, iki ayrı strateji

### 3a. Firestore (şemasız belge DB)

- **Kimlik:** Google OAuth (Cloud/Firebase scope'ları) veya service account JSON. v1'de service account **önerilmez**: kalıcı ve geniş yetkili bir anahtar.
- **Çıkarım (inference):**
  - koleksiyon başına N belge örneklenir (yapılandırılabilir, varsayılan 200);
  - alan → tip dağılımı çıkarılır (`string` %97, `null` %3);
  - `DocumentReference` alanları → ilişki önerisi;
  - alt koleksiyonlar → 1-N ilişki.
  - Sonuç `inferred: true` IR'dır, güven oranı ile birlikte.
- **Ne yapılır bu şemayla?**
  1. **Görselleştirme ve dokümantasyon:** Firestore'un hiç olmayan ER diyagramı.
  2. **Security Rules üretimi:** çıkarılmış şemadan tip doğrulamalı `firestore.rules` taslağı üretilir. Bu taslak Change Review'dan geçer; "kural gevşetiliyor" → Risky/Breaking.
  3. **Göç planı:** Firestore → Postgres (Supabase/Neon/Data Connect) şeması + veri taşıma betiği. Büyük bir dönüşüm kitlesi.
- **Uygula:** DDL yok. Yalnızca rules dağıtımı (Firebase Rules API) ve index tanımları (`firestore.indexes.json`) uygulanır.
- **Gizlilik:** Örneklenen belge **değerleri** saklanmaz; yalnızca tip istatistikleri tutulur. PII maskeleme mevcut `PiiMasker` ile.

### 3b. Firebase Data Connect (Postgres tabanlı)

- Arkasında Cloud SQL for PostgreSQL var; şema GraphQL SDL (`@table`) ile tanımlanıyor.
- **Eşleme:** IR ↔ Data Connect GraphQL şeması, çift yönlü. Yeni bir **eject hedefi** (`firebase.dataconnect`) olur; bugünkü 19 hedefin yanına.
- **Introspect:** Cloud SQL Postgres'e genel Postgres yolu veya Data Connect şema dosyaları (GitHub üzerinden) ile.
- Data Connect kullanıcısı için Namines klasik bir Postgres aracı gibi çalışır, üstüne GraphQL eşlemesi eklenir.

### 3c. Firebase Auth

- Şema değil ama `users` tablosu ilişkisi açısından önemli. Supabase'deki `auth.users` gibi **dış referans** olarak modellenir.

## 4. PlanetScale (K2)

- **Kimlik:** service token (v1); OAuth uygulaması (v2, onay süreci doğrulanacak).
- **Eşleme:** PlanetScale **deploy request** ↔ Namines Change Request. Namines CR onaylanınca deploy request açılır veya güncellenir. PlanetScale'in kendi şema farkı ile Namines risk analizi yan yana gösterilir.
- **Not:** Vitess MySQL'de FK desteği geçmişte kısıtlıydı. Mevcut durum doğrulanmalı. Destek yoksa `ReferentialActionSql` kuralı uyarınca FK'ler "mantıksal ilişki" olarak tutulur ve uyarı verilir.

## 5. Cloudflare D1 (K2) ve Turso (K2)

- **Ortak:** SQLite motoru Namines'te zaten var; DDL üretimi hazır.
- **D1:**
  - kimlik: scoped API token (D1 Edit izni). Cloudflare'in genel 3. parti OAuth'u D1 için yok sayılmalı (doğrulanacak);
  - keşif ve uygulama D1 REST API'si ile;
  - ek çıktı: `wrangler` migration dosyası eject hedefi (`migrations/0001_*.sql`).
- **Turso:**
  - kimlik: Platform API token;
  - keşif: organizasyon → veritabanları → gruplar;
  - libSQL protokolü ile introspect.
- **Edge'e özgü riskler:** D1'de `ALTER TABLE` kısıtları var (SQLite). Risk motoru "tablo yeniden oluşturma gerekiyor" uyarısını zaten SQLite için üretmeli; doğrulanacak.

## 6. MongoDB Atlas (K3)

- **Kimlik:** Atlas Admin API service account (OAuth client credentials).
- **Çıkarım:** Firestore motoru yeniden kullanılır (§3a). Ek olarak mevcut `$jsonSchema` validator'ları okunur.
- **Uygula:** `$jsonSchema` doğrulayıcı üretimi ve index tanımları, Review'dan geçerek.

## 7. AWS (K3)

- **RDS/Aurora:**
  - keşif için IAM role assumption (müşteri hesabında CloudFormation ile salt-okuma rolü kurulur; "Launch Stack" tek tık);
  - bağlantı için **IAM DB auth token** (15 dakika, parola yok).
  - Ağ: çoğu RDS özel VPC'de, Namines bulutundan erişilemez. Burada **masaüstü köprüsü** ([03](03-MASAUSTU-TAURI.md) §3 v2) veya müşteri tarafında küçük bir "agent" konteyneri gerekir.
- **DynamoDB:** şemasız. Anahtar şeması ve GSI'lar okunur; tek tablo tasarımı (single-table design) görselleştirmesi niş ama değerli. Talep oldukça.

## 8. Google Cloud SQL / AlloyDB ve Azure (K3)

- **GCP:** Firebase'in Google OAuth altyapısı yeniden kullanılır; Cloud SQL Admin API ile keşif; IAM DB auth.
- **Azure:** Entra ID OAuth; Azure Database for PostgreSQL/MySQL ve Azure SQL (MSSQL motoru hazır).
- Kurumsal satışın kapısı, ama ağ erişimi (private endpoint) yüzünden AWS ile aynı köprü veya agent ihtiyacı var.

## 9. K4: "genel bağlantı + keşif" sağlayıcıları

Railway, Render, DigitalOcean, Heroku Postgres, Aiven, Crunchy Bridge, Xata, Nhost:
- **v1:** Genel Postgres/MySQL bağlayıcısı + sağlayıcıya özgü **bağlantı dizesi yardımcıları** (doğru SSL modu, pooler portu, örnek format) yeterli.
- **v2:** Talep gelen sağlayıcıya keşif API'si eklenir (çoğunda API token ile 1 hafta).
- **Appwrite, PocketBase:** kendi koleksiyon modelleri var; şema API'si üzerinden çıkarım. Talep oldukça.

## 10. Entegrasyon dışı ama aynı çerçeveyle

| Hedef | Ne | Durum |
|---|---|---|
| GitHub | Depo tarama, PR botu, PR farkı | Var; çerçeveye taşınır (hesap modeli birleşir) |
| GitLab / Bitbucket | Aynı PR botu | K3 |
| Prisma / Drizzle / TypeORM depoları | Koddan şema çıkarma (`CodeSchemaExtractor` var) | Var, GitHub bağlayıcısının bir yeteneği |
| Slack / Teams / Discord | CR bildirimi, onay bağlantısı (onay **değil**, onay mobilde) | K2, bildirim servisiyle |
| OpenRouter / Google / GitHub Models | AI hesap bağlama (18 §9.3) | Ayrı "AI connectors" kümesi, aynı token kasası |

## 11. Başarı ölçütleri

- Bağlı hesap sayısı / aktif organizasyon.
- Bağlayıcı üzerinden açılan CR oranı (kama).
- Bağlayıcı başına introspect başarı oranı ve "bilinmeyen tip" oranı (eşleme kalitesi).
- Gece canlı duman testlerinin yeşil oranı (sağlayıcı API değişimini erken yakalama).
