# Namines - AI Destekli İnteraktif Veritabanı Mimari Oluşturucu

Namines, modern yazılım geliştiricileri, veritabanı mimarları ve veri mühendisleri için tasarlanmış, yapay zeka destekli, interaktif bir veritabanı şema oluşturma ve derleme platformudur. Karmaşık metinleri veya sesli komutları saniyeler içinde DDL scriptlerine, Entity Framework Core modellerine ve kurumsal veri sözlüklerine (PDF) dönüştürür.

## 🚀 Özellikler

- **AI Tabanlı Şema Üretimi:** Groq (Bulut) veya Ollama (Lokal) kullanarak doğal dil (metin veya ses) ile veritabanı tasarlayın.
- **İnteraktif Canvas (React Flow):** Yapay zekanın ürettiği şemayı görsel bir arayüzde inceleyin, düğümleri (tabloları) taşıyın ve bağlantıları (ilişkileri) düzenleyin.
- **Bölgesel Revizyon (Regional Prompting):** Sadece seçili tabloları hedefe alarak yapay zekaya "Bu tabloya audit log kolonları ekle" gibi özel komutlar verin.
- **Canlı Kural Motoru (Linter):** Veritabanı kurallarına uymayan ilişkileri veya tip uyumsuzluklarını anında tespit eder (Örn: VARCHAR olan PK'ya INT olan FK bağlanması).
- **Kurumsal Çıktı Üretimi (Compiler):**
  - MSSQL, PostgreSQL ve MySQL için optimize edilmiş DDL (SQL) scriptleri.
  - C# projeleri için hazır Entity Framework Core DbContext ve Model sınıfları (.zip).
  - QuestPDF destekli detaylı Veritabanı Sözlüğü (.pdf).
  - Mermaid ER diyagramı ve README.md çıktıları.
- **Docker Sandbox Entegrasyonu:** Üretilen SQL scriptini izole bir Docker container'ı içerisinde çalıştırarak test eder ve oluşturulan veritabanının yedeğini (.tar/.bak) size sunar.

## 🏗️ Mimari ve Teknoloji Yığını

Namines, "Clean Architecture" prensiplerine sıkı sıkıya bağlı kalınarak geliştirilmiştir:

### Backend (.NET 8)
- **Namines.API:** Sunum katmanı, RESTful endpointler, SSE (Server-Sent Events) ile canlı Docker log akışı.
- **Namines.Core:** Domain modelleri (SchemaTable, SchemaColumn), Interface'ler (Portlar) ve AI prompt inşa edicileri.
- **Namines.Infrastructure:** Dış dünya adaptörleri (Groq API, Ollama API, Docker.DotNet, EF Core/QuestPDF Generator'ları).

### Frontend (Next.js 16 - App Router)
- **UI & Stil:** Tailwind CSS, Lucide React (İkonlar).
- **State Management:** Zustand ile merkezi durum (schema, nodes, edges, aiProvider) yönetimi.
- **Görselleştirme:** `@xyflow/react` ile özelleştirilebilir interaktif diyagramlar ve Mermaid.js önizlemeleri.
- **Ses Entegrasyonu:** `MediaRecorder` API üzerinden alınan ses, backend aracılığıyla Whisper modeline aktarılır.

## ⚙️ Kurulum ve Çalıştırma

### Gereksinimler
- Docker Engine & Docker Compose (Sandbox özellikleri için şarttır).
- Node.js 18+ (Lokal geliştirme için).
- .NET 8 SDK (Lokal geliştirme için).
- Groq API Key (Bulut AI özellikleri için).
- *Opsiyonel:* Ollama (Lokal AI özellikleri için `localhost:11434` üzerinde çalışır durumda olmalıdır. Önerilen modeller: `qwen2.5-coder`, `deepseek-coder`).

### 🐳 Docker Compose ile Tek Komut Kurulum (Tester / Demo)

1. Kök dizinde `.env` dosyası oluştur:
```env
GROQ_API_KEY=gsk_sizin_api_anahtariniz_buraya
JWT_KEY=minimum_32_karakter_guvenli_bir_jwt_anahtari
```

2. Build et ve başlat:
```bash
docker compose up --build
```

3. Tarayıcıda aç: [http://localhost:3000](http://localhost:3000)

> **Not:** İlk build 3-5 dakika sürer. Sonraki açılışlar ~30 saniyedir.

### Manuel (Lokal) Geliştirme Ortamı

**Backend İçin:**
1. `backend/Namines.API/appsettings.json` içerisine `Groq:ApiKey` değerinizi girin.
2. Konsolu açın ve çalıştırın:
```bash
cd backend
dotnet restore
dotnet run --project Namines.API
```
Backend `http://localhost:5000` portunda çalışacaktır.

**Frontend İçin:**
1. Konsolu açın ve bağımlılıkları yükleyin:
```bash
cd frontend
npm install
```
2. Geliştirme sunucusunu başlatın:
```bash
npm run dev
```
Frontend `http://localhost:3000` portunda çalışacaktır.

## 🛡️ Kalite Güvencesi ve Test (QA Audit)
Proje üzerinde kapsamlı bellek ve istisna (exception) yönetimi denetimleri yapılmıştır. `DockerService` içindeki unmanaged kaynaklar (Tar arşiv akışları) ve `CompileController` içindeki MemoryStream/ZipArchive işlemleri `using` blokları ile güvenli hale getirilmiştir. Tüm hatalar Global `ExceptionMiddleware` tarafından yakalanıp formatlanmaktadır. Derleme aşamasında (build) sıfır uyarı (0 warning) prensibine uyulmuştur.
