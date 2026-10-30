# 09 — OAuth kataloğu: bağlanabilecek her şey

> **Karar (2026-10-30, ürün sahibi):** Sistemi ileri taşıyacak, OAuth
> sunan her sağlayıcı alınacak. Hem **giriş** (Sign in with ...) hem de
> **hesap bağlama** (Connect ...) için.
>
> Bu belge tam listeyi, her birinin ne işe yaradığını, nereden kayıt
> alınacağını, istenecek en dar scope'u ve bilinen onay süreçlerini
> veriyor. Mimari [02](02-ORTAK-TEMEL.md) (kimlik) ve
> [06](06-ENTEGRASYON-MIMARISI.md) (bağlayıcılar) içinde.
>
> **Uyarı:** Sağlayıcı programları değişiyor. "Doğrula" işaretli satırlar,
> uygulamaya başlamadan önce sağlayıcının güncel dokümanından kontrol
> edilmeli.

---

## 1. İki ayrı kavram, iki ayrı tablo

| | **Giriş (Identity)** | **Bağlantı (Connector)** |
|---|---|---|
| Amaç | Kullanıcının Namines'e kim olduğunu kanıtlaması | Kullanıcının **kaynaklarına** (DB, depo, proje) Namines'e erişim vermesi |
| Örnek | "GitHub ile giriş yap" | "Supabase hesabını bağla" |
| Scope | `openid email profile` (en dar) | Kaynağa özel (`projects:read` ...) |
| Saklanan | Dış kimlik (`AspNetUserLogins`), token **saklanmaz** | Şifreli access/refresh token (`ConnectorAccounts`) |
| Sahiplik | Kullanıcı | Organizasyon (bağlayan kişi kaydedilir) |

Aynı sağlayıcı ikisini birden yapabilir (GitHub, Google, Microsoft).
Bunlar yine de **iki ayrı onay** olarak istenir:
- giriş için dar scope alınır;
- bağlantı için ek scope, ilk ihtiyaç anında artımlı (incremental) istenir.

Girişte geniş scope istemek, kayıt oranını düşürür ve Google gibi
sağlayıcılarda uygulama doğrulama sürecini ağırlaştırır.

## 2. Giriş sağlayıcıları

| # | Sağlayıcı | Durum | Neden | Kayıt yeri | Scope | Onay / not |
|---|---|---|---|---|---|---|
| G1 | **GitHub** | ✅ Var | Geliştirici kitlesinin varsayılanı | GitHub → Settings → Developer settings → OAuth Apps (ya da mevcut GitHub App'in kullanıcı yetkilendirmesi) | `read:user user:email` | Anında. Denetimde yalnızca birincil + doğrulanmış e-posta kullanılacak şekilde düzeltildi |
| G2 | **Google** | ✅ Var | En büyük kitle | Google Cloud Console → APIs & Services → Credentials → OAuth client + Consent screen | `openid email profile` | Temel scope'lar için marka doğrulaması kısa. Denetimde `aud`/`email_verified` düzeltildi |
| G3 | **Microsoft** (kişisel + iş hesapları) | 🆕 | Kurumsal kullanıcılar; Azure bağlayıcısının kapısı | Microsoft Entra admin center → App registrations → "Accounts in any organizational directory and personal Microsoft accounts" (`common`) | `openid email profile offline_access` | **Publisher verification** yapılmazsa onay ekranında "doğrulanmamış" uyarısı çıkar. MPN/Partner Center hesabı gerekir (doğrula). İş hesaplarında e-posta her zaman doğrulanmış değildir; `xms_edov` / tenant politikası kontrol edilmeli (doğrula) |
| G4 | **Apple** | 🆕 **zorunlu (iOS)** | App Store kuralı: iOS uygulamasında üçüncü parti giriş varsa Apple ile giriş (veya eşdeğer gizlilik odaklı seçenek) sunulmalı | Apple Developer → Certificates, IDs & Profiles → Services ID + Sign in with Apple key | `name email` | Apple Developer Program ($99/yıl). "E-postamı gizle" relay adresleri: hesap eşleme e-posta ile değil Apple `sub` ile yapılmalı |
| G5 | **LinkedIn** | 🆕 | Kurumsal / B2B güven, satış ekipleri | LinkedIn Developer Portal → Create app → Products → "Sign In with LinkedIn using OpenID Connect" | `openid profile email` | Ürün ekleme genelde hızlı. Şirket sayfası (LinkedIn Page) doğrulaması gerekir |
| G6 | **GitLab** | 🆕 | GitLab kullanan ekipler (PR botunun GitLab karşılığı da gelecek) | GitLab → Preferences → Applications (self-managed GitLab için instance başına) | `openid email profile` (+ bağlantı için `read_api`) | Anında |
| G7 | **Bitbucket / Atlassian** | 🆕 K3 | Atlassian ekipleri | Bitbucket workspace → OAuth consumers; Atlassian Developer Console (Jira için 3LO) | `account email` | Anında |
| G8 | **Kurumsal SSO** (SAML 2.0 / OIDC: Okta, Entra, Google Workspace, OneLogin) | 🆕 Team/Enterprise | Kurumsal satışın önkoşulu | Organizasyon başına yapılandırma (IdP metadata) | — | .NET tarafında Sustainsys.Saml2 veya ITfoxtec; organizasyona e-posta alan adı bağlanır, SCIM sonraki adım |

**Bilinçli olarak alınmayanlar:** Facebook, X/Twitter, Discord (giriş için).
Veritabanı aracı için güven sinyali düşük, destek yükü yüksek. Discord
yalnızca **bildirim** bağlantısı olarak var (§4).

## 3. Bağlantı sağlayıcıları (kaynak erişimi)

### 3a. Veritabanı ve BaaS

| # | Sağlayıcı | OAuth var mı? | Kayıt yeri | En dar başlangıç scope'u | Not |
|---|---|---|---|---|---|
| C1 | **Supabase** | ✅ Resmi (Management API OAuth Apps) | Supabase Dashboard → Organization → OAuth Apps | Projeleri ve veritabanını **okuma**; yazma artımlı | K1. Bkz. [07](07-ENTEGRASYON-KATALOGU.md) §1 |
| C2 | **Firebase / Google Cloud** | ✅ Google OAuth | Aynı Google OAuth istemcisi + ek scope'lar | Firestore okuma (`datastore`), proje listesi; Rules dağıtımı ayrı (`firebase`) | **`cloud-platform` gibi geniş scope'lar Google'ın hassas/kısıtlı scope doğrulamasına girer ve haftalar sürebilir.** Erken başvur, en dar scope ile başla (doğrula) |
| C3 | **Cloudflare** (D1, Workers, R2) | ⚠️ Genel self-servis 3. parti OAuth beklenmemeli (doğrula) | Kullanıcı kapsamlı **API token** oluşturur; Namines token şablon bağlantısı verir | D1 Read; yazma için D1 Edit | v1 token. Cloudflare partner/OAuth programı varsa v2 |
| C4 | **Neon** | ⚠️ OAuth partner entegrasyonu (başvuru, doğrula) | Neon ile partner görüşmesi; v1 kullanıcı API anahtarı | Proje/branch okuma | K1 |
| C5 | **PlanetScale** | ✅ OAuth Applications (erişim başvurusu gerekebilir, doğrula) | PlanetScale → Organization settings → OAuth applications | `read_databases`; deploy request yazma artımlı | K2 |
| C6 | **Vercel** | ✅ Integrations (OAuth benzeri kurulum akışı) | Vercel → Integrations Console → Create | Proje okuma + **env var yazma** | 🆕 Namines'in ürettiği bağlantı ayarını Vercel projesine yazmak ve Vercel Marketplace'te listelenmek. Büyüme kanalı |
| C7 | **Microsoft Azure** | ✅ Entra OAuth (G3 ile aynı uygulama) | Ek API izni: Azure Service Management `user_impersonation` | Abonelik/kaynak okuma | K3. Azure Postgres/MySQL/SQL |
| C8 | **Heroku** | ✅ OAuth | Heroku Dashboard → Account → Applications → API Clients | `read` | K4. Heroku Postgres keşfi |
| C9 | **DigitalOcean** | ✅ OAuth | DigitalOcean → API → OAuth Applications | `read` | K4. Managed Databases |
| C10 | **Netlify** | ✅ OAuth | Netlify → User settings → Applications → OAuth | — | K4. Vercel ile aynı amaç (env var) |
| C11 | **MongoDB Atlas** | ⚠️ Service account (client credentials), kullanıcı OAuth'u değil | Atlas → Organization → Access Manager → Service Accounts | Project Read Only | K3 |
| C12 | **AWS** | ❌ OAuth yok | CloudFormation "Launch Stack" ile müşteri hesabında salt-okuma IAM rolü + External ID | `rds:Describe*` | K3. Bağlantı IAM DB auth token ile |
| C13 | **Turso, Render, Railway, Aiven, Xata** | ⚠️ Çoğunda API token (Railway/Render için OAuth durumu doğrula) | Sağlayıcı paneli | Okuma | K2-K4 |

### 3b. Kod ve proje yönetimi

| # | Sağlayıcı | Kayıt | Scope | Ne için |
|---|---|---|---|---|
| C14 | **GitHub App** | ✅ Var (PR botu) | Contents read, Pull requests write, Checks write | PR farkı, bot yorumu, **Checks API ile "Namines risk: Breaking" durum kontrolü** (merge'i bloklayabilir) |
| C15 | **GitLab** | G6 ile aynı uygulama | `api` (artımlı) | MR botu |
| C16 | **Bitbucket** | G7 | `pullrequest:write` | PR botu |
| C17 | **Jira / Atlassian** | Atlassian Developer Console (OAuth 2.0 3LO) | `read:jira-work write:jira-work` | CR ↔ issue bağlama |
| C18 | **Linear** | Linear → Settings → API → OAuth applications | `read write` | CR ↔ issue bağlama |

### 3c. Bildirim ve iş birliği

| # | Sağlayıcı | Kayıt | Scope | Ne için |
|---|---|---|---|---|
| C19 | **Slack** | api.slack.com/apps → OAuth & Permissions | `chat:write`, `incoming-webhook` | CR, alarm, yedek bildirimi. Onay **bağlantısı** (asıl onay mobilde) |
| C20 | **Microsoft Teams** | Entra uygulaması + Teams app manifest | Bot Framework | Aynı |
| C21 | **Discord** | Discord Developer Portal → OAuth2 + Bot | `bot`, `webhook.incoming` | Topluluk ve küçük ekipler |

### 3d. Yapay zeka hesapları (18-plan §9.3 ile aynı)

| # | Sağlayıcı | Yöntem | Not |
|---|---|---|---|
| A1 | **OpenRouter** | OAuth PKCE, istemci kaydı gerekmez | Claude, GPT, Gemini, DeepSeek tek bağlantıyla |
| A2 | **Google Gemini** | Google OAuth + Generative Language scope (doğrula) | Kullanıcının kendi kotası |
| A3 | **GitHub Models** | GitHub token (doğrula: scope ve kullanım koşulları) | — |
| A4 | **Remote MCP** (Claude.ai, ChatGPT ve diğerleri **Namines'e** bağlanır) | Namines yetkilendirme sunucusu, Auth Code + PKCE + dinamik istemci kaydı | Burada Namines OAuth **veren** taraf. Bkz. [10](10-MOTOR-VE-MCP-MIMARISI.md) |

## 4. Hesap birleştirme (account linking) kuralları

Ekim denetimindeki açıklardan (OAuth ile önceden kayıt üzerinden hesap ele geçirme, 2FA atlatma) çıkarılan **zorunlu** kurallar:

1. **Eşleme önceliği** sağlayıcının değişmez kimliğidir (`sub`, GitHub `id`). E-posta yalnızca **ilk** eşlemede ve yalnızca sağlayıcı e-postayı **doğrulanmış** bildiriyorsa kullanılır.
2. Mevcut bir hesaba yeni sağlayıcı bağlamak için kullanıcı **oturum açmış** olmalı ve "bağla" demeli. Girişte sessiz otomatik birleştirme yalnızca e-posta doğrulanmışsa ve hesapta 2FA/passkey **yoksa** yapılır.
3. 2FA/passkey'li hesaplarda dış girişten sonra **ikinci faktör yine istenir**. Denetimde "403 requiresTwoFactor" ile engellendi; hedef, engellemek yerine 2FA adımına yönlendirmek.
4. Apple "e-postamı gizle" ve Microsoft iş hesapları için e-posta tabanlı eşleme yapılmaz; yalnızca `sub`.
5. Kullanıcı ayarlarında bağlı her giriş yöntemi listelenir. Son kalan yöntem kaldırılamaz (kilitlenme önlenir).

## 5. Uygulama mimarisi

- **Bugün:** `AuthController.OAuthCallback`, frontend'den gelen kodu kendisi takas ediyor (özel kod).
- **Hedef:** ASP.NET Core **external authentication handler'ları** + OpenIddict ([02](02-ORTAK-TEMEL.md)). Kod takası sunucuda yapılır; `state`, nonce ve PKCE standart kütüphanede.
- **Paketler:**
  - Google, Microsoft, OpenIdConnect: Microsoft'un resmi paketleri;
  - Apple, LinkedIn, GitLab, Bitbucket, Slack, Discord, Notion vb.: `AspNet.Security.OAuth.Providers` (aspnet-contrib, MIT);
  - SAML: Sustainsys.Saml2.
- **Bağlayıcı OAuth'u** ayrı uçlardan geçer: `/api/v1/connectors/{provider}/authorize` ve `/callback`. Token'lar `ConnectorAccounts`'ta şifreli tutulur ([06](06-ENTEGRASYON-MIMARISI.md) §3).
- **Yapılandırma:** her sağlayıcı ortam değişkeniyle açılır ve yoksa düğme görünmez. Örnekler: `Authentication__Microsoft__ClientId`, `Connectors__Supabase__ClientSecret`. Denetimde eklenen `.env.prod.example`'a eklenir.
- **Ortam başına ayrı OAuth uygulaması:** prod, staging ve dev için ayrı istemci. Geri dönüş URL'leri karışmasın, dev sırrı sızarsa prod etkilenmesin.

## 6. Kayıt ve onay takvimi (paralel başlat, uzun sürenler önce)

| Önce başlat (haftalar sürebilir) | Hemen alınır (gün içinde) |
|---|---|
| Google OAuth doğrulaması (hassas scope'lar için) | GitHub, GitLab, Bitbucket |
| Microsoft publisher verification | LinkedIn OIDC |
| Apple Developer Program (D-U-N-S / şirket hesabı) | Supabase OAuth App |
| Neon partner, PlanetScale OAuth erişimi | Slack, Discord, Linear |
| Vercel Marketplace listeleme incelemesi | Heroku, DigitalOcean, Netlify |

**Ön koşullar:** `namines.com` canlı olmalı. Gizlilik politikası, kullanım şartları ve destek e-postası yayında olmalı. Sağlayıcıların hemen hepsi bu üçünü istiyor (Faz 0).

## 7. Sıra (yol haritasına işlendi)

1. **Faz 1:** G3 Microsoft, G4 Apple, G5 LinkedIn, G6 GitLab ve hesap birleştirme kuralları. Mevcut GitHub/Google handler mimarisine taşınır.
2. **Faz 2:** C1 Supabase, C2 Firebase/Google, C3 Cloudflare (token), C4 Neon, C6 Vercel, C14 GitHub Checks.
3. **Faz 3+:** C5 PlanetScale, C19-C21 bildirimler, C15-C18 kod/proje araçları, G8 kurumsal SSO.
4. **Faz 6-7:** C7-C13 bulut ve küçük sağlayıcılar.
