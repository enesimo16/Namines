# Namines Hub — Kapsamlı Mimari, Ürün ve GitHub Entegrasyon Planı

> **Vizyon:** Veritabanı ve sistem mimarileri için "Dünyanın GitHub + HuggingFace'i".
> Geliştiricilerin üretim kalitesinde, deterministik olarak doğrulanmış veritabanı şemalarını paylaştığı, tartıştığı, tek tıkla kendi projelerine fork ettiği ve GitHub repolarıyla çift yönlü senkronize çalıştığı açık topluluk platformu.

---

## 1. Ürün Konsepti ve Değer Önerisi

| Problem | Namines Hub Çözümü |
|---|---|
| Geliştiriciler yeni bir proje (FinTech, SaaS, AI Agent, E-Ticaret) kurarken bloglardan çöp/hatalı SQL kopyalıyor. | **Doğrulanmış Şema Vitrini:** Kural motorundan geçmiş (%100 3NF, Zero Drift, Safe DDL) hazır mimariler. |
| SQL dosyaları statiktir; ilişkileri ve mimariyi gözde canlandırmak zordur. | **İnteraktif ERD & Canlı Tuval:** Sayfayı terk etmeden şemayı gezme, tabloları filtreleme ve DDL inceleme. |
| Şablonu projeye aktarmak zahmetlidir. | **Tek Tıkla Fork:** "Fork to Canvas" butonu ile şemayı doğrudan kendi çalışma alanına klonlama. |
| Mimariler hakkında topluluk tartışması yapacak merkezî bir yer yok. | **Şema Bazlı Forum / Tartışma:** Tablo ve mimari kararları üzerine spesifik teknik tartışmalar (GitHub Discussions tarzı). |
| GitHub repolarındaki şemalar dokümante edilmemiştir. | **Çift Yönlü GitHub Entegrasyonu:** Repodan şema çekme, Hub'dan PR/Gist açma ve README için canlı durum rozeti (Badge). |

---

## 2. Kullanıcı Akışları ve Sistem Mimarisi

```mermaid
flowchart TD
    subgraph GitHub Ekosistemi
        GH_Repo["GitHub Repository (Public/Private)"]
        GH_PR["Pull Request / Gist"]
        GH_Badge["README SVG Rozeti"]
    end

    subgraph Namines Hub (/hub)
        Hub_Explore["Hub Keşif Vitrini (/hub)"]
        Hub_Detail["Şema Detay Sayfası (/hub/[slug])"]
        Hub_ERD["İnteraktif Canlı ERD Tuvali"]
        Hub_DDL["Çoklu Motor DDL Önizleyici"]
        Hub_Forum["Topluluk Tartışmaları / Yorumlar"]
    end

    subgraph Namines Studio / Canvas
        User_Canvas["Kullanıcı Tuvali (/canvas)"]
        Publish_Modal["Publish to Hub Modalı"]
    end

    GH_Repo -->|"1. Import from Repo"| Hub_Explore
    User_Canvas -->|"2. Publish Schema"| Publish_Modal --> Hub_Detail
    Hub_Detail --> Hub_ERD
    Hub_Detail --> Hub_DDL
    Hub_Detail --> Hub_Forum
    Hub_Detail -->|"3. Fork to Canvas"| User_Canvas
    Hub_Detail -->|"4. Export to GitHub PR/Gist"| GH_PR
    Hub_Detail -->|"5. Canlı Doğrulama Rozeti"| GH_Badge
```

---

## 3. GitHub Çift Yönlü Entegrasyonu (Bi-Directional Sync)

### 3.1. GitHub'dan Şema Çekme (Import from GitHub)
1. **Repo URL Girdisi:** Kullanıcı `github.com/owner/repo` linkini verir.
2. **Otomatik Dosya Taraması (`IGithubScanner`):**
   - Prisma şemaları (`schema.prisma`)
   - EF Core C# DbContext & Entity sınıfları (`*.cs`)
   - SQL DDL / Migration dosyaları (`*.sql`)
   - Django, Drizzle, TypeORM modelleri
3. **AST Dönüşümü:** Koddan kanonik `DatabaseSchema` (IR) çıkarılır.
4. **Hub Taslağı Oluşturma:** Şema başlığı, README'den açıklama ve otomatik etiketler (*ör. #postgresql, #ecommerce*) ile Hub'a tek tıkla yayınlanır.

### 3.2. GitHub'a Şema Gönderme (Export to GitHub)
1. **GitHub PR Olarak Açma:** Hub'daki bir şemayı veya yapılan bir fork'u doğrudan hedef repoya `namines/schema-update` branch'i ve PR olarak gönderme (`IGithubBotService`).
2. **GitHub Gist Olarak Paylaşma:** Tek tıkla şemanın PostgreSQL, MySQL ve SQLite DDL betiklerini içeren bir public/secret Gist oluşturma.
3. **CI/CD Workflow Entegrasyonu:** Repo içerisine `.github/workflows/namines-lint.yml` eklenerek her commit'te şemanın kırıcı değişiklik (breaking change) testlerinden geçmesi.

### 3.3. Canlı GitHub README Rozeti (Dynamic SVG Badge)
Geliştiriciler repolarının `README.md` dosyasına şu satırı ekler:
```markdown
[![Namines Verified Schema](https://namines.com/api/hub/badge/saas-core)](https://namines.com/hub/saas-core)
```
- **Dinamik SVG Çıktısı:**
  `[ Namines Hub | 24 Tables · Postgres · 3NF Verified ]`
- Tıklandığında doğrudan Hub'daki interaktif ERD sayfasına yönlendirir.

---

## 4. Topluluk ve Forum Katmanı (Discussions)

Her Hub şemasının altında tam teşekküllü bir mimari tartışma paneli yer alır:
- **Tablo Bazlı Bağlam:** Yorum yazılırken şemadaki belirli bir tablo etiketlenebilir (`@table:orders`).
- **Mimari İnceleme & Öneriler:**
  - *"Burada soft delete yerine audit/temporal table kullanmak performansı nasıl etkiler?"*
  - *"Stripe webhook id'si için unique constraint eklenmeli mi?"*
- **Sürüm Geçmişi (Revisions):** Yazar şemayı güncellediğinde, v1.0, v1.1 gibi revizyonlar ve aralarındaki görsel diff listelenir.

---

## 5. Veritabanı Modeli (Control Plane)

Namines'in mevcut PostgreSQL kontrol veritabanına eklenecek modeller:

```csharp
// 1. Hub'da paylaşılan şema kaydı
public class HubSchema
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = string.Empty; // "enterprise-saas-starter"
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // "FinTech", "SaaS", "E-Commerce"
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string TargetEngine { get; set; } = "PostgreSQL";
    public int TableCount { get; set; }
    public string SchemaJson { get; set; } = string.Empty; // Kanonik IR JSON
    public string DdlScript { get; set; } = string.Empty;
    
    // Yazar ve Topluluk Bilgisi
    public string AuthorUserId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorAvatarUrl { get; set; }
    public string? GithubRepoUrl { get; set; }
    
    // Metrikler
    public int StarCount { get; set; } = 0;
    public int ForkCount { get; set; } = 0;
    public int ViewCount { get; set; } = 0;
    public bool IsVerified { get; set; } = false; // Namines Kural Motoru Onayı
    public double DbaHealthScore { get; set; } = 100.0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// 2. Tartışma ve Yorumlar
public class HubComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HubSchemaId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? UserAvatarUrl { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? TableContext { get; set; } // Opsiyonel tablo bağıntısı (örn: "orders")
    public Guid? ParentCommentId { get; set; } // Yanıtlar için iç içe hiyerarşi
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// 3. Yıldızlar (Stars)
public class HubStar
{
    public Guid HubSchemaId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// 4. Fork Takibi
public class HubFork
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OriginalSchemaId { get; set; }
    public Guid ForkedProjectId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

---

## 6. API Uç Noktaları (`/api/hub`)

| Metot | Uç Nokta | Açıklama |
|---|---|---|
| `GET` | `/api/hub` | Filtrelenmiş liste (arama, kategori, motor, sıralama: trending/stars/recent). |
| `GET` | `/api/hub/{slug}` | Tek şema detayı (IR json, DDL, istatistikler, yazar). |
| `POST` | `/api/hub` | Canvas'taki aktif projeyi Hub'da yayınlama (`Publish to Hub`). |
| `POST` | `/api/hub/{id}/fork` | Şemayı kullanıcının kendi çalışma alanına klonlama. |
| `POST` | `/api/hub/{id}/star` | Yıldız verme / geri alma (toggle). |
| `GET` | `/api/hub/{id}/comments` | Şemanın tartışma akışı ve yorumlarını listeleme. |
| `POST` | `/api/hub/{id}/comments` | Yeni yorum veya soru ekleme. |
| `GET` | `/api/hub/badge/{slug}` | GitHub README için SVG rozet çıktısı üretme. |
| `POST` | `/api/hub/github/import` | Verilen GitHub reposundan şema tarayıp Hub taslağı üretme. |
| `POST` | `/api/hub/{id}/github/export` | Şemayı GitHub PR veya Gist olarak dışa aktarma. |

---

## 7. Frontend Sayfaları ve Bileşenleri

1. **`/hub` (Keşif ve Topluluk Vitrini):**
   - Hero: "Açık Kaynak Veritabanı Mimarileri Kataloğu".
   - Kategori filtreleri (FinTech, SaaS, AI, Health, E-Commerce).
   - Arama ve etiket çubuğu.
   - Kartlar: Şema adı, motor ikonu, tablo sayısı, yıldız sayısı, yazar, "Quick Preview" ve "Fork" düğmeleri.
2. **`/hub/[slug]` (Şema Detay & İnteraktif İnceleme):**
   - **Üst Panel:** Başlık, yazar profili, GitHub repo linki, Fork butonu, Star butonu.
   - **Sekme 1 — İnteraktif Tuval (ERD):** React Flow tabanlı, zoom/pan destekli canlı diyagram.
   - **Sekme 2 — Compiled SQL DDL:** PostgreSQL, MySQL, MSSQL, SQLite sözdizimi seçicili kod bloğu.
   - **Sekme 3 — Veri Sözlüğü (Data Dictionary):** Tablolar, kolonlar, veri tipleri, primary/foreign key ve constraint listesi.
   - **Sekme 4 — Mimari Forum (Discussions):** Yorumlar, sorular, yanıtlar ve mimari inceleme başlıkları.
3. **Tuval Entegrasyonu (`/canvas`):**
   - Export menüsüne "Publish to Namines Hub" seçeneği.

---

## 8. Uygulama Aşamaları (Faz Planı)

- **Faz 1.1:** Backend veri modelleri (`HubSchema`, `HubComment`, `HubStar`, `HubFork`), EF Core migration ve `/api/hub` CRUD uçları.
- **Faz 1.2:** Frontend `/hub` vitrin sayfası ve `/hub/[slug]` interaktif detay sayfası (React Flow ERD + SQL sekmesi + fork butonu).
- **Faz 1.3:** Forum / Yorum katmanı (iç içe yorumlar, tablo etiketleme).
- **Faz 1.4:** GitHub Entegrasyonu (README dinamik SVG rozeti, repodan çekme ve GitHub Gist/PR export).
