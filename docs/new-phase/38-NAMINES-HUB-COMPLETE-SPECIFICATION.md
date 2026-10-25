# Namines Hub — Objektif Mimari, Pazar ve Ürün Spesifikasyonu (v2 — Süzülmüş Baskı)

> **Doküman Durumu:** v2 — Objektif Mühendislik ve Pazar Süzgecinden Geçirilmiş Spesifikasyon  
> **Temel Prensip:** Sıfır sahte özellik, sıfır paralel tablo karmaşası, mevcut `CloudProject` + `Branch` + `ChangeRequest` omurgasının doğrudan değerlendirilmesi.

---

## İÇİNDEKİLER

1. [Objektif Özeleştiri: v1 Planındaki Yanlışlar ve Düzeltmeler](#1-objektif-özeleştiri-v1-planındaki-yanlışlar-ve-düzeltmeler)
2. [Pazar Analizi, Rakipler ve Konumlandırma](#2-pazar-analizi-rakipler-ve-konumlandırma)
3. [Artılar, Eksiler ve Stratejik Riskler](#3-artılar-eksiler-ve-stratejik-riskler)
4. [Kesilen Özellikler (Cut List) vs. Eklenen Yüksek Değerli Özellikler](#4-kesilen-özellikler-cut-list-vs-eklenen-yüksek-değerli-özellikler)
5. [Mevcut Altyapı Gerçeği: 3NF ve 450 Tabloluk ERP Analizi](#5-mevcut-altyapı-gerçeği-3nf-ve-450-tabloluk-erp-analizi)
6. [Sadeleştirilmiş GitHub Formunda Repo Mimarisi](#6-sadeleştirilmiş-github-formunda-repo-mimarisi)
7. [Gerçekçi Veri Modeli (`CloudProject` Genişletmesi)](#7-gerçekçi-veri-modeli-cloudproject-genişletmesi)
8. [API Uç Noktaları ve Çift Yönlü GitHub Köprüsü](#8-api-uç-noktaları-ve-çift-yönlü-github-köprüsü)
9. [Uygulama Faz Planı (Faz 1 — Faz 4)](#9-uygulama-faz-planı-faz-1--faz-4)

---

## 1. OBJEKTİF ÖZELEŞTİRİ: v1 PLANINDAKİ YANLIŞLAR VE DÜZELTMELER

Bir önceki taslakta yer alan ancak kod tabanının gerçekleriyle çarpıştığında sorun çıkaracak **3 kritik mimari hata** tespit edilip düzeltilmiştir:

### 1.1. Hata 1: Paralel `ArchitectureRepo` Tablosu Yaratmak
- **v1 Hatası:** `ArchitectureRepo` adında yeni bir tablo önerilmişti.
- **Kod Gerçeği:** Sistemdeki `Branches`, `SchemaVersions`, `ChangeRequests` (PR), `AutomationRules` (Flow), `VaultBackups`, `GroundDatabases` ve `GatewayApiKeys` tablolarının **tamamı `CloudProject.Id`'ye Foreign Key ile bağlıdır**. Ayrı bir `ArchitectureRepo` tablosu açmak, iki paralel proje kavramı yaratır ve mevcut PR/Branch sistemini boşa düşürür.
- **Doğru Çözüm:** Yeni bir repo tablosu **açılmayacak**. `CloudProject` zaten bizim Repo'muzdur. `CloudProject` üzerine `Slug`, `Visibility`, `Description`, `Category`, `Tags`, `StarCount`, `ForkCount`, `ForkedFromProjectId` kolonları eklenecektir.

### 1.2. Hata 2: "3NF Kurallarını Deterministik Denetliyoruz" İddiası
- **v1 Hatası:** `NslValidator`'ın 3NF geçişli bağımlılıkları (transitive dependency) zaten denetlediği varsayılmıştı.
- **Kod Gerçeği (`NslValidator.cs` satır 24):** Kodda açıkça *"3NF şüphesi (NSL019) bilinçli olarak dışarıda — sezgisel ve yanlış pozitifi yüksek"* yazmaktadır. Şu an 3NF yalnızca AI üretim prompt'unda (`SchemaPromptBuilder.cs`) talimat olarak yer almakta, deterministik linter'da **bulunmamaktadır**.
- **Doğru Çözüm:** Olmayan bir kontrolü varmış gibi "3NF Verified" rozetiyle sunmak geliştirici güvenini sarsar. Faz 1'de `NslValidator`'a somut, yanlış pozitifi düşük **1NF/3NF deterministik kuralları (`NSL019`, `NSL020`)** eklenecek; rozet ise dürüstçe **"AST & Engine Verified"** olarak adlandırılacaktır.

### 1.3. Hata 3: 450 Tabloluk ERP'de Mevcut Tuvalin Hazır Olduğu Varsayımı
- **v1 Hatası:** React Flow'un şu an sadece görünür tabloları çizdiği ve modül filtresi olduğu varsayılmıştı.
- **Kod Gerçeği:** `frontend/app/canvas/page.tsx` içinde `onlyRenderVisibleElements` özelliği **aktif değildir** ve `TableModel` üzerinde henüz bir **Modül/Domain (`Group` / `Schema`)** gruplaması yoktur. 450 tablo tek seferde DOM'a basıldığında ~13.500+ DOM düğümü oluşur ve tarayıcı kilitlenir.
- **Doğru Çözüm:** 450 tabloluk kurumsal ERP şemalarını sorunsuz çalıştırmak için:
  1. React Flow'da `onlyRenderVisibleElements={true}` (60+ tabloda otomatik devreye giren) açılacak.
  2. Tablolara `Module / SchemaGroup` alanı eklenecek (sol kenar çubuğundan *sadece Muhasebe*, *sadece İK* tablolarını filtreleme).

---

## 2. PAZAR ANALİZİ, RAKİPLER VE KONUMLANDIRMA

Veritabanı şema ve yaşam döngüsü pazarındaki mevcut oyuncular ve Namines Hub'ın doldurduğu boşluk:

| Rakip | Güçlü Yönü | Kritik Eksikliği | Namines Hub'ın Üstünlüğü |
|---|---|---|---|
| **dbdiagram.io** | Hızlı DBML yazımı, popüler paylaşım linkleri. | Sadece statik çizim aracı. Gerçek DB motoru testi yok, PR/onay yok, 3-yollu merge yok. | Sadece çizim değil; **6 motorda gerçek DDL derleme + Docker test + PR akışı**. |
| **ChartDB / DrawDB** | Açık kaynak, şık görsel arayüz. | Tek kişilik çizim aracı; ekip yönetişimi, branch, etki analizi ve topluluk hub'ı yok. | Sunucu-otoriteli Branch, ChangeRequest onay süreci ve kurumsal yönetişim. |
| **Bytebase / Atlas** | Güçlü CI/CD migrasyon yönetişimi ve linter. | Görsel tuval (ERD) deneyimi yok veya çok zayıf; geliştiricilerin şema keşfedeceği bir topluluk vitrini yok. | **Görsel Tuval + Topluluk Hub'ı + Deterministik Etki Analizi** tek çatı altında. |
| **Supabase / Neon** | Kolay barındırılan PostgreSQL. | Sadece PostgreSQL'e kilitli (vendor lock-in). Çoklu motor (MSSQL, Oracle, MySQL) desteği yok. | **6 Motor Desteği** (Kurumsal MSSQL/Oracle dahil) ve motorlar arası kayıp analizi. |

### Piyasa Payı ve Büyüme Fırsatı (Neden Hub?)
1. **Programmatic SEO & Organik Trafik:**  
   Yazılımcılar her gün Google'da *"e-commerce database schema"*, *"multi-tenant saas postgres schema"*, *"double entry accounting database design"* arıyor. Hub'daki her public şema (`/hub/namines/fintech-ledger`), canlı ERD'si ve 6 dildeki SQL çıktısıyla Google aramalarında doğrudan 1. sıraya aday organik bir iniş sayfasıdır.
2. **Viral GitHub Rozet Döngüsü:**  
   Açık kaynak projelerin `README.md` dosyalarına koyacağı `[![Namines Schema](...)]` rozeti, sıfır pazarlama bütçesiyle geliştirici trafiği çeker.
3. **PLG (Product-Led Growth) Hunisi:**  
   Ücretsiz kullanıcı Hub'dan bir ERP/SaaS şemasını `Fork` ederek başlar → Kendi ekibini davet ettiğinde `Team` görünürlüğüne, `Database PR` onaylarına ve `Vault/Desk` özelliklerine ihtiyaç duyar → **Pro / Team** abonesine dönüşür.

---

## 3. ARTILAR, EKSİLER VE STRATEJİK RİSKLER

### Artılar (+)
- **Mevcut Kodun %70'ini Değerlendirir:** Zaten yazılmış ama ayrı sayfalarda dağınık duran `Branch`, `ChangeRequest` (`/review`), `SchemaThreeWayMerger`, `GithubScanController` ve `GithubBotService` tek bir anlaşılır çatı ("Repo") altında birleşir.
- **Cold-Start (Boş Tuval) Sorununu Çözer:** Kullanıcı sıfırdan şema çizmek yerine doğrulanmış bir mimariyi fork edip üzerine inşa eder.
- **Yüksek Savunulabilirlik (Moat):** Yapay zeka modelleri tekil SQL üretebilir ama **topluluk tarafından doğrulanmış, versiyonlanmış, Docker'da test edilmiş mimari repoları** taklit edemez.

### Eksiler ve Riskler (-)
- **Risk 1 — "Boş Forum / Hayalet Kasaba" Etkisi:**  
  Kullanıcı sayısı azken genel bir "Forum / Discussions" sekmesi açılırsa içerisi bomboş görünür ve ürüne güvensizlik yaratır.
  - **Önlem:** Genel forum sekmesi **açılmayacak**. Yorumlar yalnızca somut **Şema Issue'ları** ve **Pull Request'ler** altında yapılacak. Ayrıca Hub, bizzat bizim hazırladığımız **20+ kurumsal kalitede doğrulanmış şema (Official Blueprints)** ile dolu olarak başlayacak.
- **Risk 2 — Çöp Şema Kirliliği (Low-Quality Spam):**  
  Herkesin 1 tabloluk `test123` projesini Public yapması vitrini çöplüğe çevirir.
  - **Önlem:** `Public` yapmak herkese açık olacak (link ile paylaşılabilir), ancak **Hub Vitrininde (`/hub`)** listelenmek için **Kalite Kapısı (Quality Gate)** şartı aranacak: *En az 4 tablo, en az 2 FK ilişkisi, 0 kritik NSL hatası ve dolu açıklama.*
- **Risk 3 — Kapsam Şişmesi (GitHub'ı Birebir Kopyalama Tuzağı):**  
  GitHub'daki Wiki, Projects, Actions, Releases, Watch, Assignees gibi özellikleri kopyalamaya çalışmak odağı dağıtır.
  - **Önlem:** Sadece veritabanı yaşam döngüsünde karşılığı olan çekirdek özellikler alınacak (bkz. Bölüm 4).

---

## 4. KESİLEN ÖZELLİKLER (CUT LIST) VS. EKLENEN YÜKSEK DEĞERLİ ÖZELLİKLER

### ❌ Kesilen / Kaldırılan Özellikler (Gereksiz Şişkinlik)

| Kaldırılan Özellik | Neden Kaldırıldı? |
|---|---|
| **Ayrı `ArchitectureRepo` Tablosu** | `CloudProject` ile ikizlik yaratıyor ve mevcut 30+ tablonun FK yapısını bozuyordu. |
| **`Discussions` (Genel Forum) Sekmesi** | İlk aşamada boş kalıp "ölü platform" hissi verecekti; `Issues` ve `PR` yorumları teknik tartışma için fazlasıyla yeterli. |
| **`Watch` Butonu ve Bildirim Sayacı** | Gerçek zamanlı e-posta/bildirim kuyruğu olmadan "Watch" butonu işlevsiz bir süstür. Sadece `Star` ve `Fork` kalacak. |
| **GitHub Gist Export** | Kullanıcı zaten tek tıkla SQL kopyalayabiliyor veya doğrudan GitHub reposuna PR açabiliyor. Gist gereksiz OAuth izni ve bakım yüküdür. |
| **Karmaşık Issue Alanları (Assignee, Milestone)** | Namines bir Jira/Linear değildir. Issue'lar sadece şema kusurları, DBA bulguları ve mimari öneriler için yalın tutulmalıdır. |

### ✅ Eklenen Yüksek Değerli Özellikler (Veritabanına Özel Farklılaştırıcılar)

| Eklenen Özellik | Neden Çok Değerli? |
|---|---|
| **DBA Bulgusunu Tek Tıkla Issue'ya Dönüştürme** | Linter'ın bulduğu *"Orders tablosunda FK indeksi eksik"* uyarısını tek tıkla o projede açık bir `RepoIssue` haline getirir. |
| **Hub Kalite Kapısı (Quality Gate)** | Vitrine sadece `>= 4 tablo`, `0 kritik hata` ve `>= 2 ilişki` içeren temiz mimarilerin çıkmasını sağlar. |
| **Modül / Domain Filtreleme (450+ Tablo Desteği)** | Devasa ERP şemalarında tabloları domain'lere (*Finans, Stok, İK*) ayırır ve tuvalde izole görünüm sağlar. |
| **Gerçek 1NF/3NF Deterministik Kuralları (`NSL019-020`)** | Tekrarlayan kolonları (`tel1, tel2`) ve tablo içi geçişli bağımlılık şüphesini yakalayan gerçek deterministik kurallar. |
| **Upstream Diff (Fork Karşılaştırma)** | Fork edilen bir şemanın orijinal şemadan ne kadar saptığını tek tıkla görsel diff olarak gösterir. |

---

## 5. MEVCUT ALTYAPI GERÇEĞİ: 3NF VE 450 TABLOLUK ERP ANALİZİ

### 5.1. 3NF Konusunda Dürüst Durum ve Yapılacak Ek
- **Bugün ne var:** `NslValidator.cs` içinde 18 deterministik kural (PK kontrolü, FK tip uyumu, cascade döngüsü, isim sınırları) var. Ancak 3NF kuralı (`NSL019`) yanlış pozitif endişesiyle boş bırakılmış.
- **Ne eklenecek:** `NslValidator`'a iki somut normalizasyon kuralı eklenecek:
  1. **`NSL019` (1NF Tekrarlayan Kolon Grubu):** Aynı tabloda `phone_1, phone_2` veya `item1, item2, item3` gibi sonu rakamla biten tekrarlı kolon dizilerini yakalar → *"Ayrı bir ilişkili tabloya ayırın"* uyarısı verir.
  2. **`NSL020` (3NF Geçişli Bağımlılık Şüphesi):** Bir tabloda başka bir tablonun adı önek olarak geçiyorsa (ör. `orders` tablosunda `customer_id` FK'sı varken yanında `customer_name`, `customer_email`, `customer_phone` tutuluyorsa) → *"Geçişli bağımlılık (3NF ihlali): Bu kolonlar hedef tabloda yaşamalı"* uyarısı verir.

### 5.2. 450 Tabloluk ERP İçin Yapılacak 3 Somut Mühendislik İyileştirmesi
1. **React Flow Viewport Sanallaştırması:** `canvas/page.tsx` içinde tablo sayısı 50'yi geçtiğinde `onlyRenderVisibleElements={true}` otomatik aktifleşecek ve kenar animasyonları kapatılacak.
2. **Modül / Grup Filtresi (`Table.SchemaGroup`):** 450 tabloluk bir ERP'yi tek ekranda görmek imkânsızdır. Sol üst araç çubuğuna **Modül Seçici** eklenecek; kullanıcı sadece seçtiği modülün (ve onun doğrudan FK komşularının) tablolarını tuvalde görecek.
3. **Hızlı Tablo Arama ve Odaklama (`Ctrl+F` / Arama Kutusu):** 450 tablo içinde aranan tabloya anında zoom yapan (`fitView({ nodes: [{ id }] })`) arama çubuğu.

---

## 6. SADELEŞTİRİLMİŞ GITHUB FORMUNDA REPO MİMARİSİ

Rota: `/hub/[owner]/[slug]` (Hem ekip içi hem de public repolar için tek birleşik görünüm)

```
+--------------------------------------------------------------------------------------------------+
|  owner / repo-name   [Private | Team | Public]              [Star ⭐ 142]  [Fork 🍴 28]  [Open in Studio 🚀] |
+--------------------------------------------------------------------------------------------------+
|  [ 1. Schema & Code ]    [ 2. Issues (4) ]    [ 3. Pull Requests (2) ]    [ 4. Settings ]        |
+--------------------------------------------------------------------------------------------------+
|  Branch: [ main v ]  |  Modül: [ Tümü (42 Tablo) v ]  |  PostgreSQL  |  AST Verified (0 Error)   |
+--------------------------------------------------------------+-----------------------------------+
|                                                              |  GENERATED FILES                  |
|                                                              |  • DIAGRAM (Canlı ERD)        [✓] |
|                     CANLI ERD TUVALİ                         |  • schema.sql           (DDL)     |
|                                                              |  • DbContext.cs         (EF Core) |
|       (React Flow — Sadece Seçili Modül veya Tüm Şema)       |  • schema.prisma        (Prisma)  |
|                                                              |  • openapi.json         (Gateway) |
|                                                              |  • README.md            (Docs)    |
+--------------------------------------------------------------+-----------------------------------+
```

### 4 Çekirdek Sekme (Sıfır Şişkinlik)
1. **Schema & Code:**  
   - Sol/Merkez: Canlı ERD diyagramı (veya sağdan bir dosya seçildiğinde o dosyanın derlenmiş kodu).
   - Sağ Panel: Canlı üretilen artefaktlar (`schema.sql`, `DbContext.cs`, `schema.prisma`, `openapi.json`, `README.md`).
   - Üst Şerit: Sunucu-taraflı `Branch` seçici (`main`, `feat/billing`), Modül filtresi.
2. **Issues:**  
   - Şema hataları, eksik indeksler ve mimari tartışmalar.
   - Tuvaldeki DBA Linter bulgularından tek tıkla Issue açabilme (`relatedTable` bağlantısıyla).
3. **Pull Requests (ChangeRequests):**  
   - Mevcut `ChangeRequestController` ve `/review` ekranımızın doğrudan buraya gömülmesi.
   - Görsel şema diff'i + Risk seviyesi (`Safe` / `Risky` / `Breaking`) + Gerçek Docker test sonucu + 3-yollu AST Merge butonu.
4. **Settings (Sadece Owner/Admin görür):**  
   - Repo adı, slug, açıklama, kategori, etiketler.
   - Görünürlük: `Private` / `Team` / `Public`.
   - GitHub Repo bağlantısı (`LinkedGithubRepo`) ve README Badge kodu.
   - Tehlikeli Bölge: Projeyi sil.

---

## 7. GERÇEKÇİ VERİ MODELİ (`CloudProject` GENİŞLETMESİ)

Yeni bir repo tablosu açmak yerine mevcut [`CloudProject.cs`](file:///c:/Users/PC/Desktop/apps/namines/backend/Namines.Core/Models/Auth/CloudProject.cs) modeli genişletilir ve yanına yalnızca `RepoIssue`, `RepoIssueComment` ve `RepoStar` eklenir:

```csharp
namespace Namines.Core.Models.Auth;

public enum ProjectVisibility
{
    Private = 0, // Sadece oluşturan kullanıcı (Varsayılan)
    Team = 1,    // Organizasyon üyeleri görebilir, branch ve PR açabilir
    Public = 2   // Herkes görebilir, yıldızlayabilir ve kendi hesabına fork edebilir
}

// CloudProject üzerine eklenecek alanlar:
public partial class CloudProject
{
    /// <summary>URL-dostu benzersiz kısa ad (ör: "multi-tenant-saas").</summary>
    public string? Slug { get; set; }

    public string? Description { get; set; }

    public ProjectVisibility Visibility { get; set; } = ProjectVisibility.Private;

    /// <summary>Hub kategorisi: "FinTech", "SaaS", "E-Commerce", "ERP", "AI", "Healthcare", "General".</summary>
    public string Category { get; set; } = "General";

    /// <summary>Virgülle ayrılmış veya JSON etiketler (ör: "postgresql,3nf,multi-tenant").</summary>
    public string? Tags { get; set; }

    public int StarCount { get; set; } = 0;
    public int ForkCount { get; set; } = 0;

    /// <summary>Bu proje başka bir projeden fork edildiyse kaynak projenin ID'si.</summary>
    public string? ForkedFromProjectId { get; set; }

    /// <summary>Bağlı GitHub deposu ("owner/repo").</summary>
    public string? LinkedGithubRepo { get; set; }
}

// Yeni Tablo 1: Şema Issue Kaydı
public class RepoIssue
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = null!;
    public CloudProject Project { get; set; } = null!;

    public int IssueNumber { get; set; } // Proje bazında 1, 2, 3...
    public string Title { get; set; } = null!;
    public string Body { get; set; } = string.Empty;
    public bool IsClosed { get; set; } = false;

    public string? RelatedTableName { get; set; } // Örn: "orders"
    public string? NslRuleCode { get; set; }      // Örn: "NSL006" (Linter'dan açıldıysa)
    public string? Labels { get; set; }           // "dba-risk,performance"

    public string CreatedByUserId { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
}

// Yeni Tablo 2: Issue Yorumları
public class RepoIssueComment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string IssueId { get; set; } = null!;
    public RepoIssue Issue { get; set; } = null!;

    public string Body { get; set; } = null!;
    public string AuthorUserId { get; set; } = null!;
    public ApplicationUser AuthorUser { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Yeni Tablo 3: Yıldızlama (Compound PK: ProjectId + UserId)
public class RepoStar
{
    public string ProjectId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

---

## 8. API UÇ NOKTALARI VE ÇİFT YÖNLÜ GITHUB KÖPRÜSÜ

### 8.1. Hub & Repo API (`/api/hub`)
| Metot | Uç Nokta | Açıklama |
|---|---|---|
| `GET` | `/api/hub/explore` | Kalite kapısını geçmiş `Public` repoları listeler (kategori, motor, arama ve sıralama: *popular / recent*). |
| `GET` | `/api/hub/repos/{owner}/{slug}` | Repo detayını, şemasını, branch'lerini, açık issue ve PR sayılarını döner. |
| `PATCH` | `/api/hub/repos/{projectId}/settings` | Görünürlük (`Private`/`Team`/`Public`), açıklama, kategori, slug ve etiketleri günceller. |
| `POST` | `/api/hub/repos/{projectId}/star` | Yıldız verir veya geri alır (toggle). |
| `POST` | `/api/hub/repos/{projectId}/fork` | Şemayı kullanıcının kendi çalışma alanına klonlar (`ForkedFromProjectId` dolu yeni bir `CloudProject` + `main` branch açar). |
| `GET/POST` | `/api/hub/repos/{projectId}/issues` | Projeye ait issue listesi ve yeni issue açma (DBA bulgusundan açma dahil). |
| `PATCH` | `/api/hub/issues/{issueId}/state` | Issue kapatma / yeniden açma. |
| `GET/POST` | `/api/hub/issues/{issueId}/comments` | Issue altındaki yorumları listeleme ve yeni yorum ekleme. |
| `GET` | `/api/hub/badge/{owner}/{slug}.svg` | GitHub README'leri için canlı SVG rozeti (`[Namines | 28 Tables · PostgreSQL · Verified]`). |

### 8.2. GitHub Çift Yönlü Köprüsü
1. **GitHub'dan Çekme (`Import from GitHub`):**  
   Mevcut `GithubScanController` (`POST /api/github/scan`) kullanılır; dönen şema doğrudan yeni bir `CloudProject` (Repo) olarak kaydedilir ve `LinkedGithubRepo` alanı doldurulur.
2. **GitHub'a Gönderme (`Export PR to GitHub`):**  
   Mevcut `IGithubBotService` kullanılır; onaylanan bir `ChangeRequest` veya derlenen DDL/Prisma/EF Core çıktısı bağlı GitHub deposuna PR olarak açılır.

---

## 9. UYGULAMA FAZ PLANI (FAZ 1 — FAZ 4)

| Faz | Başlık | Yapılacak Somut İşler |
|---|---|---|
| **Faz 1** | **Backend Omurgası & 3NF Kuralları** | 1. `CloudProject` genişletmesi + `RepoIssue`, `RepoIssueComment`, `RepoStar` tabloları ve EF Core migration.<br>2. `NslValidator`'a gerçek `NSL019` (1NF) ve `NSL020` (3NF) kurallarının eklenmesi.<br>3. `HubController` (`/api/hub/*`) uçlarının ve SVG rozet üreticisinin yazılması.<br>4. Yetkilendirme ve fork izolasyonu için xUnit testleri. |
| **Faz 2** | **GitHub Formunda Repo Sayfası & 450 Tablo Optimizasyonu** | 1. `/hub/[owner]/[slug]` sayfası: Header (`Star`, `Fork`, `Open in Studio`), `Schema & Code` (ERD + Dosya Ağacı), `Issues`, `Pull Requests` (`ChangeRequest` entegreli) ve `Settings` sekmeleri.<br>2. Tuvalde 450 tablo için `onlyRenderVisibleElements` ve Modül/Grup filtresi. |
| **Faz 3** | **Namines Hub Keşif Vitrini (`/hub`)** | 1. `/hub` ana keşif sayfası (arama, motor ve kategori filtreleri, sıralama).<br>2. Başlangıç için 10+ doğrulanmış kurumsal mimari şablonunun (FinTech, ERP, Multi-tenant SaaS, E-Commerce) vitrine eklenmesi.<br>3. Header ve ana sayfaya "Hub" navigasyonunun eklenmesi. |
| **Faz 4** | **GitHub Köprüsü, OAuth, Stripe ve Telemetri** | 1. Repo sayfasından tek tıkla GitHub'dan şema çekme ve GitHub'a PR açma.<br>2. Google & GitHub OAuth girişi.<br>3. Stripe canlı abonelik ve monitoring/analitik entegrasyonu. |
