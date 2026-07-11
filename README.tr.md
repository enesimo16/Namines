<div align="center">

# ⚡ Namines

### Yapay zeka ile saniyeler içinde etkileşimli veritabanı mimarileri tasarlayın.

Uygulamanızı düz metinle anlatın; Namines normalize bir şema üretir, etkileşimli bir tuval üzerinde
görselleştirir ve **altı** veritabanı motoru için üretime hazır DDL, EF Core modelleri, migration'lar,
sahte veri, dokümantasyon ve fazlasını oluşturur.

[English](README.md) · **Türkçe**

<br/>

![Namines açılış](docs/screenshots/landing.png)

</div>

---

## 📑 İçindekiler

- [Genel bakış](#-genel-bakış)
- [Ekran görüntüleri](#️-ekran-görüntüleri)
- [Özellikler](#-özellikler)
- [Teknoloji yığını](#-teknoloji-yığını)
- [Proje yapısı](#️-proje-yapısı)
- [Başlangıç](#-başlangıç)
- [AI token modeli](#️-ai-token-modeli)
- [Güvenlik](#-güvenlik)
- [Lisans](#-lisans)

## 🧭 Genel bakış

Namines bir fikri çalışan bir veri modeline dönüştürür. *“kullanıcılar, ürünler, siparişler ve sipariş
kalemleri olan bir e‑ticaret mağazası”* yazın (veya sesli söyleyin, ya da mevcut bir ERD ekran
görüntüsü bırakın); Namines:

1. Birincil/yabancı anahtarlar ve ilişkilerle **normalize (3NF) bir şema** üretir.
2. Gerçek zamanlı olarak birlikte düzenleyebileceğiniz **etkileşimli bir tuvalde** görselleştirir.
3. Seçtiğiniz motor için **DDL, EF Core, migration, sahte veri, diyagram ve dokümana** derler —
   hatta Docker'da tek kullanımlık bir veritabanı ayağa kaldırabilir.

Her şey, takılabilir AI sağlayıcılarıyla (Groq, Gemini, Ollama veya kendi anahtarınız) çalışan bir
.NET 8 API üzerinde koşar; tuval, paneller ve canlı iş birliğini ise bir Next.js ön yüzü sağlar.

## 🖼️ Ekran görüntüleri

| Etkileşimli tuval | DDL üretimi |
|---|---|
| ![Tuval](docs/screenshots/canvas.png) | ![Derleme](docs/screenshots/compile.png) |

| Bulut girişi | Açılış |
|---|---|
| ![Giriş](docs/screenshots/login.png) | ![Açılış](docs/screenshots/landing.png) |

## ✨ Özellikler

### Yapay zeka destekli tasarım
- Düz metin, **referans URL** veya **görsel** (vision) ile **şema üretimi**.
- **Sesli giriş** — komutunuzu söyleyin (Whisper).
- **AI DBA danışmanı** — şema sağlık skoru ve önceliklendirilmiş, açıklamalı sorunlar.
- **Smart Seed** — alan‑farkında sahte/test verisi.
- **Tersine mühendislik** — mevcut bir `DbContext`'i görsel şemaya dönüştürün.

### Görsel iş alanı
- **Etkileşimli tuval** (React Flow) — tabloları, kolonları ve ilişkileri sürükle‑bırak.
- **Gerçek zamanlı iş birliği** — canlı imleçlerle paylaşılabilir odalar (SignalR).
- **Sürüm kontrolü** — dallar (branch) ve iş alanına özgü migration geçmişi.

### Derleme & dışa aktarım
- **Çok motorlu DDL** — SQL Server, PostgreSQL, MySQL, MariaDB, SQLite, Oracle.
- **EF Core** modelleri ve rehberli **migration sihirbazı** (diff + önizleme).
- **Tarayıcı içi SQL konsolu** — üretilen DDL'i yerelde SQLite (WASM) ile çalıştırın.
- **Docker sandbox** — tek kullanımlık bir DB konteyneri kurup `.bak` indirin.
- **Geliştirici paketi** — çalışmaya hazır bir Streamlit yönetim uygulaması (ZIP) dışa aktarın.
- **Doküman & diyagram** — Data Dictionary PDF, README.md, Mermaid ER/class/flow (TR/EN).

### Platform
- **Hesaplar & bulut senkronizasyonu** — httpOnly cookie üzerinden JWT; projeler/dallar bulutta.
- **Adil AI kullanımı** — kullanıcı‑başı tavanlı paylaşımlı günlük token havuzu; tükenince istekler
  bloklanmadan ücretsiz yerel motora düşer.
- **Pro plan** — Stripe Hosted Checkout ile opsiyonel $5/ay katman.
- **Geri bildirim widget'ı** — dahili hata/öneri bildirimi.

## 🧱 Teknoloji yığını

| Katman | Yığın |
|---|---|
| Ön yüz | Next.js 16, React 19, TypeScript, Zustand, React Flow, Tailwind CSS |
| Arka uç | .NET 8, ASP.NET Core, EF Core (SQLite), SignalR, Serilog |
| AI | Groq (Llama 3.3 70B / GPT‑OSS 120B / Llama 4 Scout), Google Gemini, Ollama, OpenAI (BYOK), Whisper |
| Altyapı | Docker / docker‑compose, Stripe |

## 🗂️ Proje yapısı

```
backend/
  Namines.API/            ASP.NET Core Web API (controller, middleware, SignalR hub)
  Namines.Core/           Domain modelleri, prompt builder'lar, arayüzler
  Namines.Infrastructure/ AI servisleri, DDL üreticileri, EF Core, veri erişimi
frontend/                 Next.js uygulaması (canvas, compile, paneller, store'lar, hook'lar)
docker-compose.yml        Arka uç + ön yüz konteynerleri
```

## 🚀 Başlangıç

### Gereksinimler
- [.NET 8 SDK](https://dotnet.microsoft.com/) ve [Node.js 20+](https://nodejs.org/)
- Ücretsiz bir [Groq API anahtarı](https://console.groq.com/keys)
- (Opsiyonel) Docker Desktop — yalnızca Docker sandbox özelliği için gerekli

### 1. Sırları yapılandırın
Tüm sırlar, kök dizindeki tek bir **git‑ignored** `.env` dosyasında tutulur:

```bash
cp .env.example .env
```

En azından `Jwt__Key` (32+ karakter) ve `Groq__ApiKey` doldurun. `__` ayracı .NET config'e eşlenir
(`Jwt__Key` → `Jwt:Key`); arka uç başlangıçta `.env`'i otomatik yükler.

### 2. Arka ucu çalıştırın
```bash
cd backend/Namines.API
dotnet run
# → http://localhost:5000   (Swagger: /swagger)
```

### 3. Ön yüzü çalıştırın
```bash
cd frontend
npm install
npm run dev
# → http://localhost:3000
```

### …veya Docker ile
```bash
docker compose up --build
```

## 🎛️ AI token modeli

Namines, premium AI kullanımını **paylaşımlı günlük token havuzuna** göre ölçer; böylece tek bir
kullanıcı havuzu tüketemez — ve dormant hesaplara token ön‑tahsisi yapılmaz:

- `AiPool:DailyTokenPool` — paylaşımlı günlük bütçe (varsayılan **100 000**, ≈ Groq free tier günlük).
- `AiPool:PerUserDailyTokens` — kullanıcı‑başı günlük tavan (varsayılan **20 000**).
- Tüketim **talep üzerine** düşülür; havuz **veya** kullanıcı tavanı dolunca istekler şeffaf biçimde
  **ücretsiz yerel motora düşer** — tüm ücretsiz özellikler çalışmaya devam eder.

Havuzu istediğiniz zaman `appsettings.json` içinden büyütün (örn. `1000000`) — kod değişmez.

## 🔐 Güvenlik

- Sırlar asla commit edilmez — tek git‑ignored `.env` tek doğruluk kaynağıdır.
- JWT, `localStorage` yerine **httpOnly cookie**'de tutulur (XSS ile token çalınmasını azaltır).
- BYOK API anahtarları **AES‑256‑GCM** ile şifreli saklanır (non‑extractable Web Crypto anahtarı).
- Sunucu‑taraflı URL çekme ve DB bağlantı hedeflerinde **SSRF koruması**, hassas uçlarda **rate
  limiting** ve AI prompt'larında **prompt‑injection sertleştirmesi**.

## 📄 Lisans

[MIT Lisansı](LICENSE) altında yayımlanmıştır.
