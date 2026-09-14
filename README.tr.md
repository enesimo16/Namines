<div align="center">

# Namines

### Düz metinle yazılmış bir açıklamadan, çalışan ve yedeklenen bir veritabanına: yapay zeka destekli veritabanı tasarımı.

[English](README.md) · **Türkçe**

</div>

---

## İçindekiler

- [Bu nedir](#bu-nedir)
- [Kimler için](#kimler-için)
- [Parçalar nasıl bir araya geliyor](#parçalar-nasıl-bir-araya-geliyor)
- [Ekran görüntüleri](#ekran-görüntüleri)
- [Özellikler](#özellikler)
- [Teknoloji yığını](#teknoloji-yığını)
- [Proje yapısı](#proje-yapısı)
- [Başlangıç](#başlangıç)
- [AI token modeli](#ai-token-modeli)
- [Güvenlik](#güvenlik)
- [Lisans](#lisans)

## Bu nedir

Namines bir veritabanı tasarım ve yaşam döngüsü aracıdır. Bir uygulamanın düz metinle yazılmış
açıklamasını (ya da mevcut bir şemayı, bir görseli, bir referans URL'yi) alır; normalize bir şema,
düzenlenebilir bir görsel tuval ve altı veritabanı motoru için üretilmiş çıktılar (DDL, EF Core
modelleri, migration'lar, sahte veri, diyagram, dokümantasyon) üretir.

Tasarımın ötesinde, Namines bir proje için gerçek, barındırılan bir veritabanı da açabilir
(**Ground**), üretilen şemayı ona uygulayabilir, ilk yedeği alabilir (**Vault**) ve çalışan bir veri
yönetim panelini (**Desk**) geri verebilir — hepsi compile ekranındaki tek bir eylemden, **Launch**.
Zaten veri içeren bir veritabanına yapılan değişiklikler, sessizce uygulanmak yerine mevcut
değişiklik-inceleme akışına yönlendirilir.

## Kimler için

Bir fikirden çalışan, incelenebilir bir veritabanı şemasına — DDL'i veya migration'ları elle
yazmadan — geçmek isteyen geliştiriciler ve küçük takımlar; o veritabanı için ayrı bir altyapı
kurmadan onu barındırmanın, yedeklemenin ve görüntülemenin hafif bir yolunu isteyenler.

## Parçalar nasıl bir araya geliyor

| Parça | Ne yapar |
|---|---|
| Canvas (`/canvas`) | Şema üret ya da elle kur; görsel olarak, iş birliğiyle, sürüm geçmişiyle düzenle. |
| Compile (`/compile`) | Şemayı seçilen motor için DDL / EF Core / Prisma / sahte veri / doküman / diyagrama dönüştür. |
| Launch | `/compile`'dan: bir veritabanı aç (Ground), şemayı uygula, ilk yedeği al (Vault), bir Desk bağlantısı al — tek eylem; hedefte zaten veri varsa incelemeye yönlendirir. |
| Ground (`/ground`) | Desk'ten bağımsız, barındırılan veritabanı provizyonu ve durumu. |
| Vault (`/vault`) | Desk'ten bağımsız, zamanlanmış ve manuel yedekleme, geri yükleme. |
| Desk (`services/desk`, ayrı uygulama) | Bir projenin canlı veritabanı için CRUD/SQL veri yönetim paneli. |

## Ekran görüntüleri

Burada daha önce bulunan ekran görüntüleri bu oturumdaki arayüz yenilemesinden (İngilizce arayüz,
yeni görsel stil ve yukarıdaki Ground/Vault/Launch özellikleri) önce alınmıştı ve artık mevcut
uygulamayı yansıtmıyordu. Farklı bir ürünü gösterir hâlde bırakmak yerine kaldırıldı. Güncel
arayüzü görmek için uygulamayı yerelde çalıştırın (bkz. [Başlangıç](#başlangıç)).

## Özellikler

### Yapay zeka destekli tasarım
- Düz metin, referans URL veya görsel (vision) ile şema üretimi.
- Üretim komutu için sesli giriş (Whisper).
- AI DBA danışmanı: önceliklendirilmiş, açıklamalı sorunlarla bir şema sağlık skoru.
- Smart Seed: alan-farkında sahte/test verisi.
- Tersine mühendislik: mevcut bir `DbContext`'i görsel şemaya dönüştür.

### Görsel iş alanı
- Etkileşimli tuval (React Flow): tabloları, kolonları, ilişkileri sürükle-bırak.
- Komut paleti (Ctrl/Cmd+K) ile klavyeden her işleme ulaş.
- Ctrl+Z / Ctrl+Shift+Z ve 50 anlık görüntülük geçmiş yığınıyla geri al/yinele.
- Ctrl+F ile tuval araması: herhangi bir tablo veya kolonu bulup yakınlaştır.
- Beş hazır başlangıç şeması (e-ticaret, SaaS, CRM, blog, sağlık); mevcut şemaya ekle veya
  değiştir.
- Gerçek zamanlı iş birliği: canlı imleçler ve şema senkronizasyonlu paylaşılabilir odalar
  (SignalR).
- Sürüm kontrolü: dallar (branch) ve iş alanına özgü migration referans noktası.

### Derleme ve dışa aktarım
- Çok motorlu DDL: SQL Server, PostgreSQL, MySQL, MariaDB, SQLite, Oracle.
- EF Core modelleri ve rehberli migration sihirbazı (diff ve önizleme).
- Prisma şeması dışa aktarımı.
- SQL DDL içe aktarımı: `.sql` dosyası yapıştır veya yükle (`CREATE TABLE`,
  `ALTER TABLE ADD/DROP COLUMN`, `ALTER TABLE ADD FOREIGN KEY`).
- Tarayıcı içi SQL konsolu: üretilen DDL'i yerelde SQLite (WASM) ile çalıştır.
- Docker sandbox: tek kullanımlık bir DB konteyneri aç, yedek indir.
- İndirilebilir kod projesi: veritabanına ham bir bağlantı dizesi yerine kapsamlı, iptal edilebilir
  bir Gateway API anahtarıyla konuşan, üretilmiş bir full-stack proje.
- Doküman ve diyagram: Data Dictionary PDF, README.md, Mermaid ER/class/flow.

### Launch, Ground, Vault, Desk
- Launch: `/compile`'dan tek eylem — bir veritabanı açar, şemayı uygular, ilk yedeği alır ve Desk'e
  bir bağlantı döndürür; hedef veritabanında zaten veri varsa, ona dokunmak yerine değişikliği
  mevcut inceleme akışına yönlendirir.
- Ground: barındırılan PostgreSQL provizyonu (yapılandırmaya göre yerel, Neon veya Supabase),
  Desk'ten bağımsız.
- Vault: geri yükleme ile zamanlanmış ve manuel yedekleme, Desk'ten bağımsız.
- Desk: bir projenin canlı veritabanı için CRUD, SQL konsolu, API anahtarları ve
  dağıtım/log görünümleri sağlayan ayrı bir uygulama (`services/desk`).

### CI / geliştirici araçları
- Şema diff (`scripts/namines-diff.mjs`): iki şema JSON dosyası arasında bağımlılıksız bir Markdown
  diff raporu. Eklenen/silinen tablo, kolon ve ilişkileri algılar; yıkıcı değişikliklerde 2 çıkış
  koduyla döner, böylece CI merge'leri kapılayabilir.

### Platform
- Hesaplar ve bulut senkronizasyonu: httpOnly cookie'de JWT; projeler buluta kaydedilir (dallar
  cihaza özel, yerelde tutulur).
- Adil AI kullanımı: kullanıcı-başı tavanlı paylaşımlı günlük token havuzu; tükenince desteklenen
  özellikler bloklamak yerine ücretsiz yerel motora düşer.
- Pro plan: Stripe Hosted Checkout ile opsiyonel ücretli katman.
- Ana sayfada geri bildirim widget'ı.

## Teknoloji yığını

| Katman | Yığın |
|---|---|
| Ön yüz | Next.js 16, React 19, TypeScript, Zustand, React Flow, Tailwind CSS |
| Desk | Next.js 16, TypeScript, Tailwind CSS (`services/desk` altında ayrı uygulama) |
| Arka uç | .NET 8, ASP.NET Core, EF Core (PostgreSQL), SignalR (Redis backplane), Serilog |
| AI | Groq (Llama 3.3 70B / GPT-OSS 120B / Llama 4 Scout), Google Gemini, Ollama, OpenAI (BYOK), Whisper |
| Altyapı | Docker / docker-compose, Stripe |

## Proje yapısı

```
backend/
  Namines.API/             ASP.NET Core Web API (controller, middleware, SignalR hub)
  Namines.Core/            Domain modelleri, prompt builder'lar, arayüzler, paylaşılan yardımcılar
  Namines.Infrastructure/  AI servisleri, DDL üreticileri, EF Core, veri erişimi, Launch/Ground/Vault servisleri
  Namines.Ground/          Veritabanı provizyon sağlayıcıları (LocalPostgres, Neon, Supabase)
  Namines.Vault/           Yedekleme sağlayıcıları ve depolama
frontend/                  Next.js uygulaması (canvas, compile, Ground/Vault sayfaları, store'lar, hook'lar)
services/desk/             Ayrı Next.js uygulaması: bir projenin canlı veritabanı için CRUD/SQL paneli
docker-compose.yml         Control DB + arka uç + ön yüz konteynerleri
```

## Başlangıç

### Gereksinimler
- [.NET 8 SDK](https://dotnet.microsoft.com/) ve [Node.js 20+](https://nodejs.org/)
- Ücretsiz bir [Groq API anahtarı](https://console.groq.com/keys)
- Docker Desktop — control database (PostgreSQL) için ve Ground/Vault/Launch için zorunlu; yalnızca
  şema tasarımı ve derleme kullanacaksanız (canlı veritabanı olmadan) opsiyoneldir.

### 1. Sırları yapılandırın
Tüm sırlar, kök dizindeki tek bir git-ignored `.env` dosyasında tutulur:

```bash
cp .env.example .env
```

En azından `Jwt__Key` (32+ karakter) ve `Groq__ApiKey` doldurun. `__` ayracı .NET config'e eşlenir
(`Jwt__Key` → `Jwt:Key`); arka uç `.env`'i başlangıçta otomatik yükler.

### 2. Control database'i başlatın
```bash
docker compose up -d namines-control-db
```

### 3. Arka ucu çalıştırın
```bash
cd backend/Namines.API
dotnet run
# http://localhost:5000  (Swagger: /swagger)
```

### 4. Ön yüzü çalıştırın
```bash
cd frontend
npm install
npm run dev
# http://localhost:3000
```

### 5. (Opsiyonel) Desk'i çalıştırın
```bash
cd services/desk
npm install
npm run dev
# http://localhost:3200
```

### ...ya da her şeyi Docker ile çalıştırın
```bash
docker compose up --build
```
(Bu, control database, arka uç ve ön yüzü başlatır. Desk yukarıdaki gibi ayrı başlatılır.)

## Deploy

Production, yerel `.env` yerine ayrı env dosyaları kullanır:

| Hedef | Şablon | Not |
|---|---|---|
| Backend (Railway / Render / Fly / VPS) | [`deploy/backend.env.example`](deploy/backend.env.example) | `Jwt__Key`, `Groq__ApiKey`, `App__FrontendUrl` zorunlu |
| Frontend (Vercel) | [`deploy/frontend.env.example`](deploy/frontend.env.example) | Yalnızca `NEXT_PUBLIC_API_URL`, build sırasında gömülür |

Deploy'un çalışıp çalışmamasını belirleyen üç ayar:

1. `App__FrontendUrl` frontend'in tam origin'ine eşit olmalı. Production'da localhost otomatik
   eklenmez, yanlış değer tüm tarayıcı isteklerini CORS'a takar.
2. Frontend ve API farklı site'larda ise (örn. Vercel + Railway) `Auth__CrossSiteCookie=true`
   olmalı. Aksi hâlde tarayıcı auth cookie'sini bırakır ve login hiç tutmaz.
3. `NEXT_PUBLIC_API_URL` sonunda `/api` olmamalı — istemci bunu kendisi ekler.

Control database PostgreSQL'dir. Yönetilen bir sağlayıcı kullanın (Neon, RDS, Azure Database) ya da
`namines-control-db-data` volume'unu kalıcı tutun; her iki durumda da yedekleyin, aksi hâlde her
redeploy'da tüm hesaplar silinir.

## AI token modeli

Namines, premium AI kullanımını paylaşımlı bir günlük token havuzuna göre ölçer, böylece tek bir
kullanıcı havuzu — dormant hesaplara token ön-tahsisi yapmadan — tüketemez:

- `AiPool:DailyTokenPool`: paylaşımlı günlük bütçe (varsayılan 100.000, kabaca Groq'un ücretsiz
  katman günlük tokenı).
- `AiPool:PerUserDailyTokens`: kullanıcı-başı günlük tavan (varsayılan 20.000).
- Tüketim talep üzerine düşülür. Havuz ya da kullanıcı tavanı dolunca, desteklenen özellikler
  (doküman, sahte veri, geliştirici paketi, tersine mühendislik) hata vermek yerine ücretsiz yerel
  motora düşer.

Havuzu istediğiniz zaman (örn. 1.000.000'a) `appsettings.json` içinden büyütün; kod değişikliği
gerekmez.

## Güvenlik

- Sırlar asla commit edilmez; tek bir git-ignored `.env` tek doğruluk kaynağıdır.
- JWT, `localStorage` yerine httpOnly cookie'de tutulur, XSS ile token çalınmasını azaltır.
- BYOK API anahtarları AES-256-GCM ile şifreli saklanır (non-extractable Web Crypto anahtarı).
- Veritabanı bağlantı dizeleri şifreli saklanır (AES-256-GCM) ve bir istemciye asla döndürülmez;
  indirilen bir kod projesi bunun yerine kapsamlı, iptal edilebilir bir Gateway API anahtarıyla
  kimlik doğrular.
- Sunucu-taraflı URL çekme ve veritabanı bağlantı hedeflerinde SSRF koruması, hassas uçlarda rate
  limiting ve AI prompt'larında prompt-injection sertleştirmesi.
- Desk'in SSO devir jetonu bir URL'de değil, bir POST gövdesinde taşınır ve aynı-site ya da açıkça
  güvenilen origin'lerle sınırlıdır.

## Lisans

[MIT Lisansı](LICENSE) altında yayımlanmıştır.
