export interface GuideStepData {
  target?: string;
  tabSelector?: string;
  autoAction?: string;
  cleanupAction?: string;
  interactiveButton?: {
    en: string;
    tr: string;
    action: string;
  };
  badge?: {
    en: string;
    tr: string;
  };
  directive?: {
    en: string;
    tr: string;
  };
  title: {
    en: string;
    tr: string;
  };
  description: {
    en: string;
    tr: string;
  };
}

export interface GuideDefinition {
  id: string;
  category: {
    en: string;
    tr: string;
  };
  steps: GuideStepData[];
}

export const GUIDES: Record<string, GuideDefinition> = {
  // ── 1. /new Sayfası Rehberi (Exact 11 Steps) ───────────────────────────
  new: {
    id: 'new',
    category: { en: 'New Project Setup', tr: 'Yeni Proje Başlatma' },
    steps: [
      {
        target: '#prompt-input',
        badge: { en: 'Step 1/11 • Natural Language', tr: 'Adım 1/11 • Doğal Dil' },
        directive: {
          en: 'Enter your database requirements in plain English or Turkish.',
          tr: 'İstediğiniz veritabanı ihtiyacını Türkçe veya İngilizce serbestçe yazın.',
        },
        title: {
          en: 'AI Schema Prompt',
          tr: 'AI Şema Prompt Alanı',
        },
        description: {
          en: 'Describe tables, relations, fields, or business logic. For example: "Multi-tenant SaaS with teams, subscription plans, invoices, and audit logs".',
          tr: 'Tabloları, ilişkileri veya iş mantığını tanımlayın. Örneğin: "Faturalandırma, ekipler ve audit log içeren çok kiracılı SaaS veritabanı".',
        },
      },
      {
        target: '#new-model-dropdown-btn',
        badge: { en: 'Step 2/11 • AI Model Tier', tr: 'Adım 2/11 • AI Model Seçimi' },
        directive: {
          en: 'Choose the AI model tier that fits your schema scale and complexity.',
          tr: 'Projenizin karmaşıklığına göre Namines AI model seviyesini seçin.',
        },
        title: {
          en: 'AI Model Selection',
          tr: 'Namines AI Model Seçimi',
        },
        description: {
          en: 'Select from high-speed lightweight models for simple CRUD schemas, up to DeepSeek and reasoning models for complex relational systems.',
          tr: 'Hızlı CRUD modellerinden, 50-60 tablolu devasa kurumsal şemaları üretebilen akıl yürütme modellerine kadar seçim yapabilirsiniz.',
        },
      },
      {
        target: '#new-advanced-toggle-btn',
        badge: { en: 'Step 3/11 • Advanced Mode', tr: 'Adım 3/11 • Gelişmiş Mod' },
        directive: {
          en: 'Toggle to activate multi-turn planning and automated lint repair passes.',
          tr: 'Çok aşamalı planlama ve otomatik mimari onarım turlarını aktif etmek için tıklayın.',
        },
        title: {
          en: 'Advanced Multi-Pass Architecture',
          tr: 'Gelişmiş Mimari Modu',
        },
        description: {
          en: 'When active, Namines AI runs additional verification passes to optimize data types, resolve foreign key cycles, and validate naming conventions.',
          tr: 'Aktif olduğunda AI şemanızı üretmeden önce bir plan turu yapar, döngüsel yabancı anahtarları ve kolon tiplerini optimize eder.',
        },
      },
      {
        target: '#new-db-dropdown-btn',
        badge: { en: 'Step 4/11 • Database Engine', tr: 'Adım 4/11 • Motor Seçimi' },
        directive: {
          en: 'Pick your production database engine (PostgreSQL, SQL Server, MySQL, SQLite, Oracle).',
          tr: 'Hedef veritabanı motorunuzu seçin (PostgreSQL, SQL Server, MySQL, SQLite, Oracle).',
        },
        title: {
          en: 'Target Engine Selection',
          tr: 'Hedef Veritabanı Motoru',
        },
        description: {
          en: 'Namines tailors syntax, engine-specific collations, identity auto-increments, and JSON column types strictly for your selected database engine.',
          tr: 'Tüm DDL sözdizimi, otomatik artan kolonlar, collation kuralları ve veri tipleri seçtiğiniz motora özel üretilir.',
        },
      },
      {
        target: '#new-source-dropdown-btn',
        badge: { en: 'Step 5/11 • Templates & Import', tr: 'Adım 5/11 • Hazır Başlangıçlar' },
        directive: {
          en: 'Start without writing a prompt: pick SaaS/E-Commerce starters, scan GitHub repos, or import SQL.',
          tr: 'Prompt yazmadan başlayın: SaaS/E-ticaret şablonları seçin, GitHub deposu tarayın veya SQL dosyası yükleyin.',
        },
        title: {
          en: 'Architectural Starters & Import Hub',
          tr: 'Hazır Başlangıçlar & İçe Aktarma',
        },
        description: {
          en: 'Choose battle-tested starters (E-commerce, SaaS, Multi-tenant) or extract schemas from code, live connections, OpenAPI, and whiteboard photos.',
          tr: 'Endüstri standardı hazır şablonlarla saniyeler içinde başlayın veya mevcut kod, canlı DB ya da tahta çizimlerinden şema çıkarın.',
        },
      },
      {
        target: '#header-workspace-btn',
        badge: { en: 'Step 6/11 • Workspace', tr: 'Adım 6/11 • Çalışma Alanı' },
        directive: {
          en: 'Access all your saved database projects and workspaces.',
          tr: 'Kayıtlı tüm veritabanı projelerinize ve çalışma alanlarınıza buradan ulaşın.',
        },
        title: {
          en: 'Workspace & Projects',
          tr: 'Projeler & Çalışma Alanı',
        },
        description: {
          en: 'Manage cloud-synced database schemas, browse revisions, switch between projects, or export offline project snapshots.',
          tr: 'Bulutta senkronize edilen projelerinizi listeleyin, projeler arasında geçiş yapın ve yedeklerinizi yönetin.',
        },
      },
      {
        target: '#header-team-btn',
        badge: { en: 'Step 7/11 • Team', tr: 'Adım 7/11 • Ekip Yönetimi' },
        directive: {
          en: 'Collaborate with team members on database lifecycles.',
          tr: 'Ekip arkadaşlarınızı davet edin ve veritabanı yaşam döngüsünü birlikte yönetin.',
        },
        title: {
          en: 'Team & Collaboration',
          tr: 'Ekip & Ortak Çalışma',
        },
        description: {
          en: 'Invite engineering colleagues, assign granular viewer/editor roles, and manage organization-wide database access.',
          tr: 'Yazılımcı ve veritabanı yöneticisi çalışma arkadaşlarınızı ekibinize ekleyerek projelerinizi güvenle paylaşın.',
        },
      },
      {
        target: '#header-new-project-input',
        badge: { en: 'Step 8/11 • Project Name', tr: 'Adım 8/11 • Proje Adı' },
        directive: {
          en: 'Click to customize your project title anytime.',
          tr: 'Projenize özel bir isim vermek için doğrudan tıklayıp düzenleyin.',
        },
        title: {
          en: 'Project Title Editor',
          tr: 'Proje Başlığı Düzenleme',
        },
        description: {
          en: 'Sets the logical namespace for your database schema, generated DDL files, migration scripts, and exported backend SDKs.',
          tr: 'Veritabanınızın adını belirler; üretilen SQL dosyaları, C# sınıfları ve TypeScript SDK bu isimle adlandırılır.',
        },
      },
      {
        target: '#header-language-toggle',
        badge: { en: 'Step 9/11 • Language', tr: 'Adım 9/11 • Dil Seçimi' },
        directive: {
          en: 'Switch the entire interface between Turkish and English.',
          tr: 'Arayüzü tek tıkla Türkçe veya İngilizceye dönüştürün.',
        },
        title: {
          en: 'Language Preference (TR / EN)',
          tr: 'Dil Seçici (TR / EN)',
        },
        description: {
          en: 'Namines fully supports bi-lingual UI and prompts. AI generates documentation and descriptions in your preferred language.',
          tr: 'Namines tam iki dilli arayüz ve prompt desteğine sahiptir. Dokümantasyon ve açıklamalar seçtiğiniz dilde üretilir.',
        },
      },
      {
        target: '#header-theme-toggle',
        badge: { en: 'Step 10/11 • Theme', tr: 'Adım 10/11 • Tema Seçimi' },
        directive: {
          en: 'Toggle between Dark and Light mode.',
          tr: 'Aydınlık (Light) ve Karanlık (Dark) modlar arasında geçiş yapın.',
        },
        title: {
          en: 'Color Theme Toggle',
          tr: 'Karanlık / Aydınlık Tema',
        },
        description: {
          en: 'Optimized for day and night development sessions with contrast-tuned tokens, high legibility typography, and accessible borders.',
          tr: 'Yüksek kontrastlı, göz yormayan Shopify Polaris ve Tailwind renk token’larıyla gündüz ve gece çalışma konforu sağlar.',
        },
      },
      {
        target: '#header-user-account-btn, #auth-modal-trigger',
        badge: { en: 'Step 11/11 • Account', tr: 'Adım 11/11 • Hesap & Ayarlar' },
        directive: {
          en: 'Open account settings, view AI budget quotas, or sign in.',
          tr: 'Hesap ayarlarınızı açın, AI kullanım kotanızı görün veya giriş yapın.',
        },
        title: {
          en: 'User Profile & Settings',
          tr: 'Kullanıcı Profili & Ayarlar',
        },
        description: {
          en: 'Manage your subscription tier (Free, Pro, Team, Dev), inspect daily generation budgets, set custom AI provider API keys, and configure preferences.',
          tr: 'Abonelik planınızı yönetin, günlük AI limitinizi takip edin, özel Groq/Claude/DeepSeek API anahtarlarınızı girin ve tercihlerinizi yapılandırın.',
        },
      },
    ],
  },

  // ── 2. /canvas Sayfası Rehberi (Canvas + DB Stats + ALL Toolkit Tools + Navbar) ──
  canvas: {
    id: 'canvas',
    category: { en: 'Database Canvas', tr: 'Veritabanı Tuvali' },
    steps: [
      {
        target: '#react-flow-canvas',
        badge: { en: 'Canvas Area', tr: 'Tuval Alanı' },
        directive: {
          en: 'Drag with mouse to pan; use wheel to zoom. Drag connection handles between columns to link foreign keys.',
          tr: 'Boş alandan tutarak tuvali kaydırın, tekerlekle yakınlaştırın. Kolonlar arasındaki noktalardan sürükleyerek ilişki bağlayın.',
        },
        title: {
          en: 'Infinite Relational Canvas',
          tr: 'Sonsuz İlişkisel Veritabanı Tuvali',
        },
        description: {
          en: 'Your visual architecture workspace. Drag table cards, inspect primary/foreign keys, add indexes, and organize relational schemas with zero lag.',
          tr: 'Tüm tablolarınızı ve yabancı anahtar ilişkilerini görsel olarak yönettiğiniz ana tasarım alanıdır.',
        },
      },
      {
        target: '#schema-info-panel',
        badge: { en: 'Schema Metrics', tr: 'Şema Metrikleri' },
        directive: {
          en: 'Monitor total table & relation counts, branch state, and quickly jump to tables.',
          tr: 'Toplam tablo ve ilişki sayısını görün, aktif branch’i inceleyin ve tablolara odaklanın.',
        },
        title: {
          en: 'Live Schema Info & Branch Metrics',
          tr: 'Canlı Şema Bilgisi & İstatistikler',
        },
        description: {
          en: 'Provides instant real-time metrics of your physical database model and lets you switch between isolated server-side branches.',
          tr: 'Veritabanınızın fiziksel tablo sayılarını ve aktif çalışma branch’ini anlık olarak takip etmenizi sağlar.',
        },
      },
      {
        target: '#canvas-toolkit-fab',
        autoAction: 'open-toolkit',
        interactiveButton: {
          en: 'Toggle Toolkit Drawer',
          tr: 'Toolkit Çekmecesini Aç / Kapat',
          action: 'toggle-toolkit',
        },
        badge: { en: 'Toolkit Hub', tr: 'Geliştirici Araç Kiti' },
        directive: {
          en: 'Toolkit opened automatically: Explore 11 integrated database engineering tools.',
          tr: 'Toolkit otomatik açıldı: 11 entegre veritabanı mühendislik aracını buradan yönetin.',
        },
        title: {
          en: 'Integrated Developer Toolkit',
          tr: 'Geliştirici Araç Kiti Çekmecesi',
        },
        description: {
          en: 'Central launchpad for DBA health audits, code reverse engineering, OCR whiteboard import, migration scripts, and exports.',
          tr: 'Veritabanı denetimlerinden koddan şema çıkarmaya, canlı DB bağlantısından dışa aktarıma kadar tüm araçların merkezidir.',
        },
      },
      {
        target: '#canvas-dba-inspect-btn',
        badge: { en: 'Tool 1/11 • Advisor', tr: 'Araç 1/11 • DBA Denetimi' },
        directive: {
          en: 'Run automated health, index coverage, and N+1 query vulnerability audits.',
          tr: 'Otomatik sağlık, indeks kapsamı ve eksik anahtar denetimlerini çalıştırın.',
        },
        title: {
          en: 'DBA Health & Optimization Advisor',
          tr: 'DBA Sağlık & Optimizasyon Denetçisi',
        },
        description: {
          en: 'Detects missing foreign key indexes, redundant columns, and circular cascades with one-click automated AI repair suggestions.',
          tr: 'Eksik indeksleri, tehlikeli cascade silme kurallarını ve performans açıklarını tespit edip tek tıkla otomatik onarır.',
        },
      },
      {
        target: '#canvas-explain-schema-btn',
        badge: { en: 'Tool 2/11 • Explain', tr: 'Araç 2/11 • Şema Açıklama' },
        directive: {
          en: 'Generate human-readable architectural summaries in plain language.',
          tr: 'Şemanızın mimari özetini insan dilinde anında üretin.',
        },
        title: {
          en: 'Explain Schema with AI',
          tr: 'AI ile Şemayı Açıkla',
        },
        description: {
          en: 'Translates complex relational networks into concise technical documentation and onboarding briefs for teammates.',
          tr: 'Karmaşık ilişkisel ağları ekip arkadaşlarınız için anlaşılır teknik dokümantasyona ve özetlere dönüştürür.',
        },
      },
      {
        target: '#canvas-code-import-btn',
        badge: { en: 'Tool 3/11 • Reverse Code', tr: 'Araç 3/11 • Koddan Çıkar' },
        directive: {
          en: 'Paste or upload C# EF Core, TypeScript Prisma, or Python SQLAlchemy models.',
          tr: 'C# EF Core, TypeScript Prisma veya Python SQLAlchemy modellerinizi yapıştırıp şemaya dönüştürün.',
        },
        title: {
          en: 'Reverse Engineer from Code',
          tr: 'Koddan Tersine Mühendislik',
        },
        description: {
          en: 'Parses backend ORM entity classes and builds relational diagrams and DDL automatically without writing manual SQL.',
          tr: 'Backend entity sınıflarınızı analiz eder ve elle SQL yazmadan ilişkisel tuval kartlarını oluşturur.',
        },
      },
      {
        target: '#canvas-db-connect-btn',
        badge: { en: 'Tool 4/11 • Live DB', tr: 'Araç 4/11 • Canlı DB' },
        directive: {
          en: 'Connect existing PostgreSQL, MySQL, SQL Server, or SQLite instances.',
          tr: 'Mevcut PostgreSQL, MySQL, SQL Server veya SQLite veritabanınıza bağlanın.',
        },
        title: {
          en: 'Connect Live Database (Introspect)',
          tr: 'Canlı Veritabanı İçe Aktar',
        },
        description: {
          en: 'Read-only zero-persistence introspector reverse-engineers live tables, primary keys, and indexes directly onto the canvas.',
          tr: 'Güvenli, salt-okunur mekanizmayla canlı veritabanınızı tarayarak mevcut tablolarınızı tuvale çeker.',
        },
      },
      {
        target: '#canvas-gateway-btn',
        badge: { en: 'Tool 5/11 • Gateway', tr: 'Araç 5/11 • Kayıt Gezgini' },
        directive: {
          en: 'Browse live table records with safety-constrained pagination.',
          tr: 'Canlı tablo kayıtlarını güvenli sayfalamayla anlık görüntüleyin.',
        },
        title: {
          en: 'Browse Table Records (Gateway)',
          tr: 'Canlı Tablo Kayıt Gezgini',
        },
        description: {
          en: 'Safe REST gateway allows inspecting live data rows, column values, and relationship traversals without direct DB client tools.',
          tr: 'Ekstra DB istemcisi kurmadan canlı veri satırlarını ve kolon değerlerini güvenle incelemenizi sağlar.',
        },
      },
      {
        target: '#canvas-migration-btn',
        badge: { en: 'Tool 6/11 • Migrations', tr: 'Araç 6/11 • Migrasyon Motoru' },
        directive: {
          en: 'Generate versioned UP and DOWN migration DDL scripts with risk warnings.',
          tr: 'Sürümlü UP ve DOWN DDL script’lerini risk analizleriyle birlikte üretin.',
        },
        title: {
          en: 'DDL Migration Engine',
          tr: 'DDL Migrasyon Motoru',
        },
        description: {
          en: 'Calculates differential schema changes, flags breaking changes (dropped columns, type truncations), and builds safe scripts.',
          tr: 'Şema farklarını analiz eder; veri kaybı yaratabilecek değişiklikleri önceden uyarır ve geri alma (rollback) script’i hazırlar.',
        },
      },
      {
        target: '#canvas-cross-db-btn',
        badge: { en: 'Tool 7/11 • Virtual Links', tr: 'Araç 7/11 • Çapraz İlişkiler' },
        directive: {
          en: 'Define logical foreign keys across disparate database engines.',
          tr: 'Farklı veritabanı motorları arasında mantıksal yabancı anahtarlar kurun.',
        },
        title: {
          en: 'Cross-Database Virtual Relations',
          tr: 'Çapraz DB Sanal İlişkileri',
        },
        description: {
          en: 'Link microservice tables across PostgreSQL, MongoDB, and Redis conceptually without violating physical DB boundaries.',
          tr: 'Mikroservis mimarisindeki bağımsız veritabanı tablolarını fiziksel kısıtlara takılmadan mantıksal olarak birbirine bağlayın.',
        },
      },
      {
        target: '#canvas-vision-import-btn',
        badge: { en: 'Tool 8/11 • Vision OCR', tr: 'Araç 8/11 • Görselden Şema' },
        directive: {
          en: 'Upload whiteboard drawings, hand sketches, or ER diagram screenshots.',
          tr: 'Toplantı tahtası çizimlerini veya mimari ekran görüntülerini yükleyin.',
        },
        title: {
          en: 'Import from Diagram Image',
          tr: 'Diyagram Görselinden Şema Çıkar',
        },
        description: {
          en: 'Vision AI extracts entities, columns, and relationship lines from images and converts them directly into editable tables.',
          tr: 'Fotoğraf ve çizimlerdeki tablo ve ilişkileri OCR ve AI ile analiz edip doğrudan tuvale yerleştirir.',
        },
      },
      {
        target: '#canvas-sql-import-btn',
        badge: { en: 'Tool 9/11 • SQL Import', tr: 'Araç 9/11 • SQL Dosyası' },
        directive: {
          en: 'Load raw .sql DDL files containing CREATE TABLE statements.',
          tr: 'CREATE TABLE ifadeleri içeren .sql script dosyalarını doğrudan yükleyin.',
        },
        title: {
          en: 'Import SQL DDL (.sql)',
          tr: 'SQL DDL Dosyası Yükle',
        },
        description: {
          en: 'Parses database scripts offline, resolves constraints, and populates the canvas with relational schema nodes.',
          tr: 'SQL betiklerinizi çevrimdışı ayrıştırır, kısıtları çözer ve tabloları tuvale döker.',
        },
      },
      {
        target: '#canvas-edit-mode-btn',
        badge: { en: 'Tool 10/11 • Edit Mode', tr: 'Araç 10/11 • Düzenleme Modu' },
        directive: {
          en: 'Switch between protected view mode and direct canvas editing mode.',
          tr: 'Korumalı görüntüleme modu ile doğrudan düzenleme modu arasında geçiş yapın.',
        },
        title: {
          en: 'Visual Edit Mode Toggle',
          tr: 'Görsel Düzenleme Modu',
        },
        description: {
          en: 'In edit mode, click any column or table title to rename, add constraints, or alter data types inline.',
          tr: 'Düzenleme modunda kolonlara ve tablolara tıklayarak isimleri, tipleri ve kısıtları anında güncelleyebilirsiniz.',
        },
      },
      {
        target: '#canvas-export-dropdown-btn',
        autoAction: 'open-export-flyout',
        interactiveButton: {
          en: 'Reveal Export Menu',
          tr: 'Dışa Aktarma Menüsünü Göster',
          action: 'open-export-flyout',
        },
        badge: { en: 'Tool 11/11 • Export Hub', tr: 'Araç 11/11 • Dışa Aktarma' },
        directive: {
          en: 'Export vector SVG, high-res PNG, SQL DDL, Prisma schema, or full project zip.',
          tr: 'Vektörel SVG, yüksek çözünürlüklü PNG, SQL DDL, Prisma veya tam full-stack proje zip’i indirin.',
        },
        title: {
          en: 'Export & Package Schema',
          tr: 'Dışa Aktarma & Paketleme Merkezi',
        },
        description: {
          en: 'Download production-ready assets for documentation, presentations, or instant deployment with Next.js Admin Panel and TypeScript SDK.',
          tr: 'Sunumlar ve dokümantasyon için yüksek çözünürlüklü görseller ya da hazır çalışan Next.js Yönetim Paneli zip paketini alın.',
        },
      },
      {
        target: '#export-png-btn',
        autoAction: 'open-export-flyout',
        badge: { en: 'Export • PNG', tr: 'Dışa Aktar • PNG' },
        directive: {
          en: 'Download high-resolution raster image of your schema canvas.',
          tr: 'Şema tuvalinizin yüksek çözünürlüklü PNG görselini indirin.',
        },
        title: {
          en: 'High-Res PNG Diagram Image',
          tr: 'Yüksek Çözünürlüklü PNG Görseli',
        },
        description: {
          en: 'Perfect for presentations, Slack messages, Notion, and architectural wiki documentation.',
          tr: 'Sunumlar, ekip içi paylaşımlar ve teknik dokümantasyon sayfaları için net ve yüksek kaliteli görsel üretir.',
        },
      },
      {
        target: '#export-svg-btn',
        autoAction: 'open-export-flyout',
        badge: { en: 'Export • Vector SVG', tr: 'Dışa Aktar • Vektörel SVG' },
        directive: {
          en: 'Download infinite-resolution scalable vector graphic of your tables.',
          tr: 'Tablolarınızın ve ilişkilerinizin sonsuz çözünürlüklü vektörel SVG dosyasını indirin.',
        },
        title: {
          en: 'Scalable Vector Graphic (SVG)',
          tr: 'Vektörel Çizim (SVG)',
        },
        description: {
          en: 'Zero pixelation at any zoom level. Ideal for architectural blueprints, Figma imports, and printing.',
          tr: 'İstediğiniz kadar yakınlaştırın, piksellenme yapmaz. Figma, Illustrator ve büyük baskılar için idealdir.',
        },
      },
      {
        target: '#export-sql-btn',
        autoAction: 'open-export-flyout',
        badge: { en: 'Export • SQL DDL', tr: 'Dışa Aktar • SQL Kodu' },
        directive: {
          en: 'Download the production DDL script with CREATE TABLE statements and foreign keys.',
          tr: 'CREATE TABLE ifadeleri ve yabancı anahtarları içeren üretime hazır SQL dosyasını indirin.',
        },
        title: {
          en: 'SQL DDL Code Script (.sql)',
          tr: 'SQL DDL Betiği (.sql)',
        },
        description: {
          en: 'Engine-tailored SQL ready to execute in pgAdmin, SSMS, MySQL Workbench, or CI/CD pipelines.',
          tr: 'Seçili veritabanı motorunuza özel sözdizimiyle pgAdmin, SSMS veya terminalde hemen çalıştırılabilir.',
        },
      },
      {
        target: '#export-prisma-btn',
        autoAction: 'open-export-flyout',
        badge: { en: 'Export • Prisma', tr: 'Dışa Aktar • Prisma Şeması' },
        directive: {
          en: 'Download schema.prisma file for TypeScript and Next.js full-stack projects.',
          tr: 'TypeScript ve Next.js full-stack projeleri için schema.prisma dosyasını indirin.',
        },
        title: {
          en: 'Prisma Schema Definition',
          tr: 'Prisma ORM Şeması',
        },
        description: {
          en: 'Exports strongly-typed Prisma models with relation attributes, enum definitions, and indices.',
          tr: 'İlişki tanımları, enum tipleri ve indeksleri içeren eksiksiz schema.prisma çıktısı sağlar.',
        },
      },
      {
        target: '#export-zip-btn',
        autoAction: 'open-export-flyout',
        badge: { en: 'Export • Full Project', tr: 'Dışa Aktar • Tam Proje Paketi' },
        directive: {
          en: 'Download a complete full-stack project zip including Next.js Admin Panel and TypeScript SDK.',
          tr: 'Çalışan Next.js Yönetim Paneli ve TypeScript SDK içeren eksiksiz full-stack proje paketini indirin.',
        },
        title: {
          en: 'Full-Stack Project & Admin Panel (.zip)',
          tr: 'Full-Stack Proje & Yönetim Paneli (.zip)',
        },
        description: {
          en: 'Unpack and run: includes C# Web API, Next.js Admin Panel with CRUD screens and role-based access, and client SDK.',
          tr: 'İndirip hemen çalıştırın: C# Web API, veri yönetim ekranlarına sahip Next.js paneli ve TypeScript istemcisi tek pakette.',
        },
      },
      {
        target: '#regional-prompt-panel',
        autoAction: 'close-toolkit',
        badge: { en: 'AI Prompt Bar', tr: 'AI Mimari Çubuğu' },
        directive: {
          en: 'Update your schema using natural language without leaving the canvas.',
          tr: 'Tuvalden ayrılmadan doğal dil komutlarıyla şemanızı güncelleyin veya yeni tablolar ekleyin.',
        },
        title: {
          en: 'Natural Language Architecture Prompt',
          tr: 'Doğal Dil ile Şema Güncelleme',
        },
        description: {
          en: 'Type instructions like "Add soft delete deleted_at column to all tables" or "Add a subscriptions table linking to users" and let AI alter your canvas.',
          tr: '"Tüm tablolara deleted_at soft delete kolonu ekle" veya "Kullanıcılara bağlı bir abonelikler tablosu ekle" gibi komutlarla şemanızı tuval üstünde dönüştürün.',
        },
      },
      {
        target: '#tidy-up-btn',
        autoAction: 'close-toolkit',
        interactiveButton: {
          en: 'Run Tidy Up Now',
          tr: 'Şemayı Otomatik Hizala (Tidy Up)',
          action: 'click:#tidy-up-btn',
        },
        badge: { en: 'Navbar Action', tr: 'Tuval Eylemi' },
        directive: {
          en: 'Click to run Dagre algorithmic auto-layout and organize overlapping tables cleanly.',
          tr: 'Üst üste binen tabloları temiz bir hiyerarşide otomatik düzenlemek için tıklayın.',
        },
        title: {
          en: 'Algorithmic Auto-Layout (Tidy Up)',
          tr: 'Otomatik Düzen & Hizalama (Tidy Up)',
        },
        description: {
          en: 'Applies directed graph layout algorithms to align tables, minimize edge crossings, and optimize visual readability.',
          tr: 'Tablolar arasındaki bağlantıların çakışmasını önleyen matematiksel yerleşim algoritmasıyla tuvali anında toparlar.',
        },
      },
      {
        target: 'button[title="Share"], [data-tour="share-btn"]',
        autoAction: 'close-toolkit',
        badge: { en: 'Collaboration', tr: 'Canlı İş Birliği' },
        directive: {
          en: 'Share read-only review links or start a real-time multiplayer co-editing room.',
          tr: 'Salt-okunur inceleme linki alın veya gerçek zamanlı çok oyunculu oturum başlatın.',
        },
        title: {
          en: 'Live Sharing & Multiplayer Rooms',
          tr: 'Canlı Oturum & Link Paylaşımı',
        },
        description: {
          en: 'Invite engineering peers with live cursor tracking, instant schema synchronization, and branch-isolated room sessions.',
          tr: 'Çalışma arkadaşlarınıza anlık link gönderin; canlı imleç takibi ve gerçek zamanlı senkronizasyonla birlikte tasarlayın.',
        },
      },
      {
        target: '#sql-explorer-btn',
        autoAction: 'close-toolkit',
        badge: { en: 'SQL Console', tr: 'SQL Konsolu' },
        directive: {
          en: 'Slide open the interactive live SQL query explorer.',
          tr: 'İnteraktif canlı SQL sorgu konsolunu yan panelde açın.',
        },
        title: {
          en: 'Interactive SQL Console',
          tr: 'İnteraktif SQL Konsolu',
        },
        description: {
          en: 'Execute SQL queries, test generated DDL scripts, inspect result tables, and verify foreign key joins directly from the canvas.',
          tr: 'SQL sorgularını doğrudan çalıştırın, üretilen DDL script’lerini test edin ve sonuç tablolarını inceleyin.',
        },
      },
      {
        target: '#workspace-nav-bar, #workspace-nav',
        autoAction: 'close-toolkit',
        badge: { en: 'Global Switcher', tr: 'Modül Geçiş Çubuğu' },
        directive: {
          en: 'Navigate seamlessly between Canvas, Compile, Flow, Ground, and Vault.',
          tr: 'Canvas, Compile (Derle), Flow, Ground ve Vault modülleri arasında hızlıca geçiş yapın.',
        },
        title: {
          en: 'Workspace Product Navigator',
          tr: 'Ürün Modülleri Gezintisi',
        },
        description: {
          en: 'Switch directly from visual canvas design to code compilation, server-side event flows, hosted databases, and automatic backups.',
          tr: 'Görsel tasarımdan kod derlemeye, olay otomasyonlarına, barındırılan veritabanlarına ve yedekleme merkezine tek tıkla geçin.',
        },
      },
      {
        target: '#regional-prompt-panel, #regional-prompt-input',
        badge: { en: 'AI Prompt & Replay', tr: 'AI İsteği & Tekrar' },
        directive: {
          en: 'Type natural language requests to add tables, edit columns, or synthesize complete schemas.',
          tr: 'Yeni tablolar eklemek, kolonları düzenlemek veya tüm şemayı güncellemek için doğal dilde istek yazın.',
        },
        title: {
          en: 'AI Schema Prompt Bar',
          tr: 'Yapay Zekâ Şema İstek Çubuğu',
        },
        description: {
          en: 'Iterate rapidly on your database design using natural language. Use the history button to recall recent prompts or generate alternative schema variations.',
          tr: 'Doğal dille komut vererek veritabanınızı saniyeler içinde güncelleyin. Geçmiş butonuyla önceki promptları çağırabilir veya alternatif şema varyasyonları üretebilirsiniz.',
        },
      },
    ],
  },

  // ── 3. /compile Sayfası Rehberi ─────────────────────────────────────────
  compile: {
    id: 'compile',
    category: { en: 'Studio Compile', tr: 'Stüdyo Derleme & Eject' },
    steps: [
      {
        target: '#compile-engine-selector',
        badge: { en: 'Step 1/10 • Engine DDL', tr: 'Adım 1/10 • Motor Seçici' },
        directive: {
          en: 'Switch target engine to re-compile syntax for SQL Server, PostgreSQL, MySQL, SQLite, or Oracle.',
          tr: 'DDL sözdizimini PostgreSQL, SQL Server, MySQL, SQLite veya Oracle için anında yeniden derleyin.',
        },
        title: {
          en: 'Multi-Engine SQL Compiler',
          tr: 'Çoklu Veritabanı SQL Derleyicisi',
        },
        description: {
          en: 'Generates zero-drift SQL DDL with strict constraint ordering, engine-specific collations, identity auto-increments, and foreign keys.',
          tr: 'Seçilen motora göre yabancı anahtarları, indeksleri ve veri tiplerini hatasız sözdizimiyle anında üretir.',
        },
      },
      {
        target: '#compile-tab-SQL',
        tabSelector: '#compile-tab-SQL',
        autoAction: 'switch-tab:#compile-tab-SQL',
        badge: { en: 'Step 2/10 • Production SQL', tr: 'Adım 2/10 • Üretim DDL' },
        directive: {
          en: 'Review generated CREATE TABLE scripts, indexes, and referential integrity actions.',
          tr: 'Üretilen CREATE TABLE script’lerini, indeksleri ve referans bütünlüğü kurallarını inceleyin.',
        },
        title: {
          en: 'Production-Ready SQL DDL',
          tr: 'Üretim Seviyesi SQL DDL',
        },
        description: {
          en: 'Syntactically verified SQL script ready for direct pipeline execution. Copy to clipboard or download as a .sql file.',
          tr: 'Canlı sunucuda veya CI/CD hattında doğrudan çalıştırılmaya hazır, sözdizimi doğrulanmış SQL çıktısıdır.',
        },
      },
      {
        target: '#compile-tab-EF',
        tabSelector: '#compile-tab-EF',
        autoAction: 'switch-tab:#compile-tab-EF',
        badge: { en: 'Step 3/10 • C# EF Core', tr: 'Adım 3/10 • C# EF Core' },
        directive: {
          en: 'Inspect generated C# DbContext and strongly-typed entity classes.',
          tr: 'Üretilen C# DbContext sınıfını ve güçlü tipli entity modellerini inceleyin.',
        },
        title: {
          en: 'C# Entity Framework Core Code',
          tr: 'C# Entity Framework Core Çıktısı',
        },
        description: {
          en: 'Includes complete OnModelCreating Fluent API configurations, composite keys, navigation properties, and index definitions.',
          tr: 'Fluent API yapılandırmaları, composite anahtarlar ve navigation property’leri içeren eksiksiz C# kodudur.',
        },
      },
      {
        target: '#compile-tab-PRISMA',
        tabSelector: '#compile-tab-PRISMA',
        autoAction: 'switch-tab:#compile-tab-PRISMA',
        badge: { en: 'Step 4/10 • Prisma ORM', tr: 'Adım 4/10 • Prisma ORM' },
        directive: {
          en: 'Inspect modern schema.prisma definitions with relation attributes.',
          tr: 'İlişki kurallarını ve veri tiplerini içeren schema.prisma dosyasını inceleyin.',
        },
        title: {
          en: 'Prisma Schema & TypeScript Types',
          tr: 'Prisma Şeması & TypeScript Tipleri',
        },
        description: {
          en: 'Production-ready schema.prisma model file for modern TypeScript web stacks like Next.js, Remix, and Node.js.',
          tr: 'Next.js ve modern TypeScript projeleriniz için hazır, ilişkileri eksiksiz tanımlanmış schema.prisma dosyasıdır.',
        },
      },
      {
        target: '#compile-tab-ER',
        tabSelector: '#compile-tab-ER',
        autoAction: 'switch-tab:#compile-tab-ER',
        badge: { en: 'Step 5/10 • 13 Diagrams', tr: 'Adım 5/10 • 13 Diyagram' },
        directive: {
          en: 'Mermaid tab active: Explore ER, Class, Flowchart, State, Sequence, or Git Graph from dropdown.',
          tr: 'Mermaid sekmesi açıldı: ER diyagramı, sınıf şeması, akış şeması veya Git ağacını inceleyin.',
        },
        title: {
          en: 'Mermaid Visualizations & Diagrams',
          tr: 'Mermaid Görselleştirici & Şemalar',
        },
        description: {
          en: 'Explore 13 distinct diagram visualizations with zoom, pan, and direct high-resolution SVG vector downloads.',
          tr: 'Şemanızı 13 farklı Mermaid görseliyle inceleyin ve tek tıkla yüksek çözünürlüklü SVG vektörel dosyasını indirin.',
        },
      },
      {
        target: '#compile-diagram-controls',
        autoAction: 'switch-tab:#compile-tab-ER',
        badge: { en: 'Step 6/10 • Diagram Switcher', tr: 'Adım 6/10 • Diyagram Seçici' },
        directive: {
          en: 'Switch between 13 diagram types or click download to save as an SVG.',
          tr: '13 farklı diyagram türü arasında geçiş yapın veya SVG olarak indirin.',
        },
        title: {
          en: 'Mermaid Types & Vector Download',
          tr: 'Diyagram Türleri & Vektör İndirme',
        },
        description: {
          en: 'Select from ER Diagram, Class Diagram, Flowchart, Mind Map, State, Sequence, Gantt, Git Graph, User Journey, and Timeline.',
          tr: 'ER Diyagramı, Sınıf Şeması, Akış Şeması, Zihin Haritası, Durum Diyagramı ve Git Dal Şeması gibi 13 görsel format arasında geçiş yapın.',
        },
      },
      {
        target: '#compile-tab-SANDBOX',
        tabSelector: '#compile-tab-SANDBOX',
        autoAction: 'switch-tab:#compile-tab-SANDBOX',
        badge: { en: 'Step 7/10 • Real Docker', tr: 'Adım 7/10 • Gerçek Docker' },
        directive: {
          en: 'Spin up an ephemeral database container to execute and verify your DDL.',
          tr: 'Üretilen DDL’i gerçek bir geçici Docker container’ında test ederek doğrulayın.',
        },
        title: {
          en: 'Ephemeral Docker Sandbox Validation',
          tr: 'Gerçek Container Doğrulaması',
        },
        description: {
          en: 'Validates SQL against a real ephemeral container in Docker. Catches engine syntax errors, circular dependencies, and constraint conflicts before production.',
          tr: 'Üretilen DDL’i gerçek Docker container’ında çalıştırarak bağımlılık ve sözdizimi hatalarını canlıya geçmeden yakalar.',
        },
      },
      {
        target: '#compile-tab-EJECT',
        tabSelector: '#compile-tab-EJECT',
        autoAction: 'switch-tab:#compile-tab-EJECT',
        badge: { en: 'Step 8/10 • 19 Eject Targets', tr: 'Adım 8/10 • 19 Eject Hedefi' },
        directive: {
          en: 'Download complete full-stack boilerplates including Next.js Admin Panel & SDK.',
          tr: 'Next.js Yönetim Paneli, TypeScript SDK, FastAPI ve Go Gin dahil 19 proje paketini indirin.',
        },
        title: {
          en: 'Full-Stack Eject & Admin Panel',
          tr: 'Full-Stack Eject & Next.js Yönetim Paneli',
        },
        description: {
          en: 'Includes complete Next.js Admin Panel with data write UI and role permissions, TypeScript SDK, Spring Boot, and FastAPI starter templates.',
          tr: 'Veri ekleme/silme ekranlarına ve rol modeline sahip Next.js Yönetim Paneli, TypeScript SDK ve mikroservis şablonlarını zip olarak indirin.',
        },
      },
      {
        target: '#compile-tab-LAUNCH',
        tabSelector: '#compile-tab-LAUNCH',
        autoAction: 'switch-tab:#compile-tab-LAUNCH',
        badge: { en: 'Step 9/10 • Cloud Launch', tr: 'Adım 9/10 • Buluta Dağıtım' },
        directive: {
          en: 'Deploy database schema and backend to AWS, Azure, GCP, or DigitalOcean.',
          tr: 'Veritabanı şemanızı ve backend altyapınızı AWS, Azure veya GCP bulutuna tek tıkla dağıtın.',
        },
        title: {
          en: 'Automated Cloud Infrastructure (Launch)',
          tr: 'Bulut Altyapısı Dağıtımı (Launch)',
        },
        description: {
          en: 'Generates Terraform and Docker Compose manifests for instant cloud provisioning with zero manual setup.',
          tr: 'Terraform ve Docker Compose dosyalarını otomatik hazırlayarak şemanızı bulut ortamlarında saniyeler içinde ayağa kaldırır.',
        },
      },
      {
        target: '#compile-schema-inspector',
        badge: { en: 'Step 10/10 • Inspector Rail', tr: 'Adım 10/10 • Şema Denetçisi' },
        directive: {
          en: 'Search through tables, check column counts, and jump directly to any table DDL.',
          tr: 'Tablo adına göre filtreleme yapın, kolon sayılarını görün ve ilgili DDL bloğuna anında odaklanın.',
        },
        title: {
          en: 'Schema Inspector & Directory',
          tr: 'Şema Denetçisi & Tablo Dizini',
        },
        description: {
          en: 'Keep track of table counts, column metrics, and relational links. Click any table in the directory to highlight its DDL in the preview window.',
          tr: 'Toplam tablo ve kolon istatistiklerini takip edin. Dizindeki herhangi bir tabloya tıklayarak SQL çıktısındaki ilgili satıra atlayın.',
        },
      },
    ],
  },

  // ── 4. /flow Sayfası Rehberi ────────────────────────────────────────────
  flow: {
    id: 'flow',
    category: { en: 'Namines Flow', tr: 'Namines Flow' },
    steps: [
      {
        target: '#flow-header',
        badge: { en: 'Event Hub', tr: 'Olay Merkezi' },
        directive: {
          en: 'Monitor schema changes and trigger automated developer actions.',
          tr: 'Şema yaşam döngünüzü dinleyen sunucu-taraflı olay otomasyon merkezindesiniz.',
        },
        title: {
          en: 'Event-Driven Database Automation',
          tr: 'Olay Tabanlı Veritabanı Otomasyonu',
        },
        description: {
          en: 'Namines Flow reacts when your schema changes: notify a Slack channel, trigger a custom webhook, lint modifications, or seed test data.',
          tr: 'Şemanız değiştiğinde Slack bildirimi göndermekten webhook tetiklemeye ve test verisi üretmeye kadar tüm akışları otomatikleştirir.',
        },
      },
      {
        target: '#flow-pause-switch',
        badge: { en: 'Execution Control', tr: 'Çalışma Kontrolü' },
        directive: {
          en: 'Pause or resume browser flow notifications and animations.',
          tr: 'Tarayıcı içi akış bildirimlerini ve animasyonları duraklatın veya devam ettirin.',
        },
        title: {
          en: 'Flow Reaction Toggle',
          tr: 'Akış Tepkilerini Duraklatma / Başlatma',
        },
        description: {
          en: 'Allows muting local UI notifications during rapid development while keeping server-side webhook rules firing reliably.',
          tr: 'Geliştirme esnasında ekrandaki bildirimleri sessize alırken sunucu-taraflı webhook tetiklemelerinin çalışmasını sürdürür.',
        },
      },
      {
        target: '#flow-new-btn',
        badge: { en: 'Blank Rule', tr: 'Yeni Kural' },
        directive: {
          en: 'Create a custom automation rule from scratch with custom triggers and actions.',
          tr: 'Sıfırdan özel bir otomasyon kuralı oluşturmak için tıklayın.',
        },
        title: {
          en: 'Create Custom Flow Rule',
          tr: 'Özel Akış Kuralı Tanımlama',
        },
        description: {
          en: 'Configure table-scoped or whole-project triggers (TableAdded, ColumnDeleted, RelationAdded) and connect them to Slack, Discord, or Webhooks.',
          tr: 'Tablo bazlı veya tüm proje düzeyinde tetikleyiciler seçin; Slack, Discord, Webhook veya DBA denetim adımlarına bağlayın.',
        },
      },
      {
        target: '#flow-rules-list',
        badge: { en: 'Rules Pipeline', tr: 'Aktif Kurallar' },
        directive: {
          en: 'Inspect active rules, run test simulations, and toggle automation states.',
          tr: 'Aktif kuralları inceleyin, anlık test simülasyonları çalıştırın ve kuralları açıp kapatın.',
        },
        title: {
          en: 'Automation Pipeline & Test Runner',
          tr: 'Otomasyon Hattı & Test Çalıştırıcı',
        },
        description: {
          en: 'Review trigger conditions, execution logs, and run one-click test runs to verify that webhooks and external APIs respond with 200 OK.',
          tr: 'Tetikleyici koşullarını inceleyin ve tek tıkla simülasyon yaparak webhook’larınızın başarıyla yanıt verdiğini doğrulayın.',
        },
      },
      {
        target: '#flow-templates',
        badge: { en: 'Ready Templates', tr: 'Hazır Şablonlar' },
        directive: {
          en: 'Start in seconds with battle-tested automation templates.',
          tr: 'Tek tıkla hazır veritabanı otomasyon şablonlarını aktif edin.',
        },
        title: {
          en: 'Pre-Built Architectural Templates',
          tr: 'Hazır Mimari Otomasyon Şablonları',
        },
        description: {
          en: 'One-click presets: Slack notification when columns are deleted, Webhook call on table creation, DBA health check on schema change, and Mock data seeding.',
          tr: 'Kolon silindiğinde Slack bildirimi, tablo eklendiğinde webhook çağrısı, şema değiştiğinde DBA denetimi gibi popüler akışları tek tıkla kurun.',
        },
      },
    ],
  },

  // ── 5. /ground Sayfası Rehberi ──────────────────────────────────────────
  ground: {
    id: 'ground',
    category: { en: 'Namines Ground', tr: 'Namines Ground' },
    steps: [
      {
        target: '#ground-header',
        badge: { en: 'Data Plane', tr: 'Veri Katmanı' },
        directive: {
          en: 'Dedicated hosted database infrastructure provisioned in seconds.',
          tr: 'Saniyeler içinde ayağa kalkan bulut veritabanı altyapınız.',
        },
        title: {
          en: 'Managed Database Hosting (Ground)',
          tr: 'Barındırılan Bulut Veritabanları (Ground)',
        },
        description: {
          en: 'Namines Ground provisions isolated production-ready PostgreSQL, MySQL, and SQLite instances with automatic SSL and continuous monitoring.',
          tr: 'İzole, üretime hazır PostgreSQL, MySQL ve SQLite örneklerini otomatik SSL ve canlı izlemeyle anında sağlar.',
        },
      },
      {
        target: '#ground-project-picker',
        badge: { en: 'Project Context', tr: 'Proje Seçimi' },
        directive: {
          en: 'Switch between projects to inspect allocated compute and databases.',
          tr: 'Projeleriniz arasında geçiş yaparak tahsis edilen veritabanlarını listeleyin.',
        },
        title: {
          en: 'Project Database Selector',
          tr: 'Proje & Veritabanı Seçici',
        },
        description: {
          en: 'Isolates instances per project workspace so different teams and staging environments stay strictly segregated.',
          tr: 'Her projenin veritabanı örneklerini izole eder; geliştirme ve canlı ortamların karışmasını engeller.',
        },
      },
      {
        target: '#ground-new-db-btn',
        badge: { en: 'Provisioning', tr: 'Yeni Veritabanı' },
        directive: {
          en: 'Create a new managed database instance with a single click.',
          tr: 'Tek tıkla projenize bağlı yeni bir barındırılan veritabanı oluşturun.',
        },
        title: {
          en: 'Create New Managed Database',
          tr: 'Yeni Yönetilen Veritabanı Oluşturma',
        },
        description: {
          en: 'Give your database a name and Ground provisions isolated compute with automatic credentials, SSL, and network firewall.',
          tr: 'Veritabanınıza bir isim verin; Ground gerekli kaynakları, SSL sertifikalarını ve güvenlik duvarını otomatik yapılandırır.',
        },
      },
      {
        target: '#ground-database-card, #ground-project-picker',
        badge: { en: 'Live Status', tr: 'Canlı Durum' },
        directive: {
          en: 'Review database health, active connections, and storage consumption.',
          tr: 'Veritabanı sağlık durumunu, aktif bağlantı sayısını ve disk kullanımını izleyin.',
        },
        title: {
          en: 'Database Metadata & Metrics',
          tr: 'Veritabanı Sağlığı & Canlı Metrikler',
        },
        description: {
          en: 'Monitors real-time disk storage bytes, active connection pools, cloud provider region, and creation timestamps.',
          tr: 'Anlık disk kullanımını, aktif bağlantı havuzunu, sunucu bölgesini ve oluşturulma tarihini gösterir.',
        },
      },
      {
        target: '#ground-danger-zone, #ground-project-picker',
        badge: { en: 'Zero Data Loss', tr: 'Korumalı Silme' },
        directive: {
          en: 'Safely delete databases with automatic grace period rollback protection.',
          tr: 'Geri alınabilir koruma süresiyle veritabanlarını güvenle silin.',
        },
        title: {
          en: 'Safe Deletion with Grace Window',
          tr: 'Korumalı Silme & Geri Alma Süresi',
        },
        description: {
          en: 'Databases are never destroyed instantly: they enter a 7-day grace window during which you can cancel deletion with zero data loss.',
          tr: 'Veritabanları anında yok edilmez; 7 günlük koruma süresi boyunca tek tıkla silme işlemi geri alınabilir.',
        },
      },
      {
        target: '#ground-providers-card, #ground-project-picker',
        badge: { en: 'Multi-Cloud', tr: 'Çoklu Bulut Sağlayıcı' },
        directive: {
          en: 'Choose where your database lives across cloud providers.',
          tr: 'Veritabanınızın hangi bulut sağlayıcısında barındırılacağını seçin.',
        },
        title: {
          en: 'Cloud Infrastructure Providers',
          tr: 'Bulut Altyapı Sağlayıcıları',
        },
        description: {
          en: 'Ground supports local and cloud database providers with verified connection health checks and automatic failover.',
          tr: 'Doğrulanmış bağlantı kontrollerine sahip yerel ve bulut sağlayıcıları arasında ihtiyacınıza en uygun olanı seçin.',
        },
      },
    ],
  },

  // ── 6. /vault Sayfası Rehberi ───────────────────────────────────────────
  vault: {
    id: 'vault',
    category: { en: 'Namines Vault', tr: 'Namines Vault' },
    steps: [
      {
        target: '#vault-header',
        badge: { en: 'Zero-Loss Vault', tr: 'Sıfır Kayıp Kasası' },
        directive: {
          en: 'Continuous database snapshotting, encryption, and point-in-time recovery.',
          tr: 'Sürekli veritabanı anlık görüntüleri, şifreleme ve zaman içinde geri yükleme.',
        },
        title: {
          en: 'Automated Backup Vault',
          tr: 'Otomatik Yedekleme Kasası (Vault)',
        },
        description: {
          en: 'Namines Vault protects your production databases with encrypted periodic snapshots, retention policies, and instant rollback safety.',
          tr: 'Veritabanlarınızı şifreli periyodik anlık görüntüler, saklama politikaları ve tek tıkla geri yükleme güvencesiyle korur.',
        },
      },
      {
        target: '#vault-project-picker',
        badge: { en: 'Project Context', tr: 'Proje Seçimi' },
        directive: {
          en: 'Select the database project to inspect its backup history.',
          tr: 'Yedek geçmişini incelemek istediğiniz projeyi seçin.',
        },
        title: {
          en: 'Project Backup Selector',
          tr: 'Proje Yedek Seçici',
        },
        description: {
          en: 'Switch between active databases to review snapshot archives and disaster recovery readiness.',
          tr: 'Projeleriniz arasında geçiş yaparak yedek arşivlerini ve felaket kurtarma durumunu görüntüleyin.',
        },
      },
      {
        target: '#vault-backup-now-btn, #vault-project-picker',
        badge: { en: 'Instant Snapshot', tr: 'Anlık Yedek' },
        directive: {
          en: 'Take a manual encrypted backup before dangerous migrations or releases.',
          tr: 'Kritik bir güncelleme veya migrasyon öncesinde anında şifreli manuel yedek alın.',
        },
        title: {
          en: 'Take Backup Now',
          tr: 'Şimdi Yedek Al (Backup Now)',
        },
        description: {
          en: 'Triggers an immediate full database snapshot stored in multi-region encrypted cloud storage with zero table locking.',
          tr: 'Tabloları kilitlemeden anında tam veritabanı yedeği alır ve şifreli depolamaya kaydeder.',
        },
      },
      {
        target: '#vault-schedule-card, #vault-project-picker',
        badge: { en: 'Scheduler', tr: 'Otomatik Zamanlayıcı' },
        directive: {
          en: 'Configure automatic daily or weekly backup cadences and retention limits.',
          tr: 'Günlük veya haftalık otomatik yedekleme periyotları ve saklama sınırları belirleyin.',
        },
        title: {
          en: 'Automated Backup Schedule & Retention',
          tr: 'Yedekleme Zamanlayıcı & Saklama Politikası',
        },
        description: {
          en: 'Set cadence (Daily/Weekly), preferred UTC execution hour, and max snapshots to retain. Manual backups are never deleted by retention.',
          tr: 'Yedek sıklığını, UTC çalışma saatini ve saklanacak maksimum yedek sayısını belirleyin. Manuel yedekleriniz asla otomatik silinmez.',
        },
      },
      {
        target: '#vault-backups-table, #vault-project-picker',
        badge: { en: 'Restore Hub', tr: 'Geri Yükleme' },
        directive: {
          en: 'Download snapshot SQL dumps or restore to a sandboxed ephemeral database.',
          tr: 'Yedek dosyalarını indirin veya tek tıkla güvenli bir sandbox ortamına geri yükleyin.',
        },
        title: {
          en: 'Backup Archive & Point-in-Time Restore',
          tr: 'Yedek Arşivi & Geri Yükleme',
        },
        description: {
          en: 'Every backup shows creation timestamp, file size, checksum verification, and one-click rollback options.',
          tr: 'Tüm yedekler tarih, dosya boyutu ve doğrulama imzasıyla listelenir; tek tıkla eski bir duruma dönebilirsiniz.',
        },
      },
    ],
  },

  // ── 7. Özel Rehber: Database Advisor (DBA) ──────────────────────────────
  dba: {
    id: 'dba',
    category: { en: 'Database Advisor', tr: 'DBA Sağlık & İndeks Danışmanı' },
    steps: [
      {
        target: '#db-advisor-score-card',
        badge: { en: 'Health Score', tr: 'Sağlık Skoru' },
        directive: {
          en: 'Review overall database health score out of 100 based on industry best practices.',
          tr: 'Endüstri standartlarına dayalı 100 üzerinden veritabanı mimari sağlık skorunu inceleyin.',
        },
        title: {
          en: 'Architectural Health & Score Meter',
          tr: 'Mimari Sağlık Skoru & Değerlendirme',
        },
        description: {
          en: 'Audits relational schemas for missing indexes, dangerous CASCADE deletion rules, unconstrained VARCHARs, and table naming violations.',
          tr: 'Şemanızı eksik indeksler, tehlikeli silme kuralları, sınırsız kolon tipleri ve isimlendirme kuralları açısından denetler.',
        },
      },
      {
        target: '#db-advisor-run-analysis-btn',
        badge: { en: 'Live Audit', tr: 'Canlı Analiz' },
        directive: {
          en: 'Run a fresh AI audit pass across your schema tables and foreign keys.',
          tr: 'Mevcut tablolarınız ve yabancı anahtarlarınız üzerinde anlık bir AI denetim turu başlatın.',
        },
        title: {
          en: 'Run AI DBA Analysis',
          tr: 'AI DBA Analizini Çalıştır',
        },
        description: {
          en: 'Triggers a comprehensive deep check across all relational joins, primary keys, and index coverage.',
          tr: 'Tüm yabancı anahtar ilişkilerini, birincil anahtarları ve indeks kapsamını derinlemesine analiz eder.',
        },
      },
      {
        target: '#db-advisor-auto-fix-btn',
        badge: { en: 'AI Auto-Repair', tr: 'Otomatik Onarım' },
        directive: {
          en: 'Apply automated index creation and constraint normalization DDL in one click.',
          tr: 'Önerilen tüm indeks ve kısıt düzeltmelerini tek tıkla şemanıza uygulayın.',
        },
        title: {
          en: 'Fix All Issues with AI',
          tr: 'Tüm Sorunları AI ile Otomatik Onar',
        },
        description: {
          en: 'Advisor synthesizes optimal CREATE INDEX statements tailored to your target engine, instantly fixing slow join scans.',
          tr: 'Hedef veritabanı motorunuza en uygun indeksleri ve kısıtları otomatik üreterek şemanıza entegre eder.',
        },
      },
      {
        target: '#db-advisor-categories-tabs',
        badge: { en: 'Advisory Pillars', tr: 'Danışmanlık Kategorileri' },
        directive: {
          en: 'Filter findings by Performance, Security, or FinOps cost optimization.',
          tr: 'Bulguları Performans, Güvenlik veya FinOps maliyet optimizasyonuna göre filtreleyin.',
        },
        title: {
          en: 'Enterprise Advisory Pillars',
          tr: 'Kurumsal Danışmanlık Kategorileri',
        },
        description: {
          en: 'Categorizes issues into Performance (missing join indexes), Security (unmasked sensitive data), and FinOps (redundant cloud storage allocations).',
          tr: 'Sorunları Performans (JOIN indeksleri), Güvenlik (açık hassas veriler) ve FinOps (bulut depolama tasarrufu) başlıklarında sınıflandırır.',
        },
      },
      {
        target: '#db-advisor-issues-list',
        badge: { en: 'Actionable Issues', tr: 'Aksiyonel Bulgular' },
        directive: {
          en: 'Inspect individual table anomalies, severity levels, and guided remediation.',
          tr: 'Her bir tablo anomalisi, risk derecesi ve çözüm önerisini ayrıntılı inceleyin.',
        },
        title: {
          en: 'Anomalies & Guidance List',
          tr: 'Anomaliler & Çözüm Rehberi',
        },
        description: {
          en: 'Each issue provides exact root cause explanations, affected tables, and engine-compatible DDL scripts to eliminate vulnerabilities.',
          tr: 'Her bulgu kök neden açıklaması, etkilenen tablo bilgisi ve sorunu gideren SQL kod önerisiyle birlikte sunulur.',
        },
      },
    ],
  },

  // ── 8. Özel Rehber: DDL Migration Engine ─────────────────────────────────
  migration: {
    id: 'migration',
    category: { en: 'Migration Engine', tr: 'DDL Migrasyon Motoru' },
    steps: [
      {
        target: '#migration-stepper',
        badge: { en: '3-Step Pipeline', tr: '3 Adımlı Süreç' },
        directive: {
          en: 'Follow the guided 3-stage migration pipeline: Upload -> Edit -> Generate.',
          tr: 'Rehberli 3 aşamalı migrasyon sürecini takip edin: Yükle -> Tuvalde Düzenle -> Üret.',
        },
        title: {
          en: 'Guided Schema Migration Pipeline',
          tr: 'Rehberli Şema Migrasyon Hattı',
        },
        description: {
          en: 'Takes existing C# DbContext or SQL DDL, lets you make visual changes on the canvas, and generates verified differential migration code.',
          tr: 'Mevcut DbContext veya SQL dosyanızı alır, tuval üzerinde değişiklik yapmanızı sağlar ve farkları hatasız migrasyon koduna döker.',
        },
      },
      {
        target: '#migration-provider-select',
        badge: { en: 'Target Provider', tr: 'Hedef Veritabanı' },
        directive: {
          en: 'Select the target database engine for dialect-accurate DDL migration generation.',
          tr: 'Doğru DDL ve EF Core kod üretimi için hedef veritabanı motorunu seçin.',
        },
        title: {
          en: 'Database Provider Selection',
          tr: 'Veritabanı Sağlayıcı Seçimi',
        },
        description: {
          en: 'Configure target syntax (PostgreSQL, MSSQL, MySQL, SQLite, Oracle) so migration scripts respect exact engine conventions.',
          tr: 'Migrasyon komutlarının doğru SQL lehçesinde üretilmesi için hedef veritabanınızı belirleyin.',
        },
      },
      {
        target: '#migration-upload-dropzone',
        badge: { en: 'Upload or Baseline', tr: 'Yükleme veya Referans' },
        directive: {
          en: 'Drop your DbContext.cs file, paste code, or set the current canvas as the reference baseline.',
          tr: 'DbContext.cs dosyanızı sürükleyin, C# kodunu yapıştırın veya mevcut tuvali referans taban olarak belirleyin.',
        },
        title: {
          en: 'Baseline Schema Source',
          tr: 'Referans Şema Kaynağı',
        },
        description: {
          en: 'Namines compares future canvas modifications against this baseline to calculate exact schema diffs.',
          tr: 'Namines, tuvalde yapacağınız sonraki düzenlemeleri bu referans şemayla kıyaslayarak farkları hesaplar.',
        },
      },
      {
        target: '#migration-action-btn',
        badge: { en: 'Diff & Migration', tr: 'Fark & Migrasyon Üretimi' },
        directive: {
          en: 'Review differential schema changes, breaking change warnings, and generate UP/DOWN scripts.',
          tr: 'Şema farklarını, veri kaybı riski taşıyan değişiklikleri inceleyin ve UP/DOWN script’lerini üretin.',
        },
        title: {
          en: 'Differential Analysis & Reversible DDL',
          tr: 'Diferansiyel Analiz & Geri Alınabilir DDL',
        },
        description: {
          en: 'Calculates added tables, column modifications, and referential constraints with zero data loss. Builds both forward and rollback migration steps.',
          tr: 'Eklenen veya değişen tüm kolonları eksiksiz tespit eder; hem ileriye doğru geçiş (UP) hem de geri alma (DOWN) betiklerini hazırlar.',
        },
      },
    ],
  },

  // ── 9. Özel Rehber: Change Request Review (/review) ──────────────────────
  review: {
    id: 'review',
    category: { en: 'Change Review', tr: 'Değişiklik İnceleme Konsolu' },
    steps: [
      {
        target: '#workspace-nav',
        badge: { en: 'Review Console', tr: 'İnceleme Konsolu' },
        directive: {
          en: 'Inspect proposed database schema modifications before they hit production.',
          tr: 'Veritabanı şema değişikliklerini canlıya almadan önce inceleme konsolunda denetleyin.',
        },
        title: {
          en: 'Database Change Review Console',
          tr: 'Veritabanı Değişiklik İnceleme Konsolu',
        },
        description: {
          en: 'Git PR-like review experience specifically designed for database architecture, constraints, and migrations.',
          tr: 'Tıpkı bir GitHub Pull Request gibi, veritabanı değişikliklerini, DDL farklarını ve etki raporlarını tek ekranda sunar.',
        },
      },
      {
        target: 'main',
        badge: { en: '3-Way Merge', tr: '3-Yollu Birleştirme' },
        directive: {
          en: 'Detect drift between base snapshot, branch schema, and live production database.',
          tr: 'Ana sürüm, branch şeması ve canlı veritabanı arasındaki farkları (drift) yakalayın.',
        },
        title: {
          en: '3-Way Merge & Drift Detection',
          tr: '3-Yollu Birleştirme & Drift Tespiti',
        },
        description: {
          en: 'Ensures that manual production hotfixes are not accidentally overwritten when deploying branch modifications.',
          tr: 'Canlıda elle yapılmış acil düzeltmelerin yeni sürümle ezilmesini önler; çakışmaları önceden gösterir.',
        },
      },
      {
        target: 'header',
        badge: { en: 'Approval & Audit', tr: 'Onay & Denetim Kaydı' },
        directive: {
          en: 'Approve migrations with full cryptographic audit trail logging.',
          tr: 'Değişiklikleri onaylayın; tüm adımlar ChangeRequestAuditLog tablosuna kaydedilsin.',
        },
        title: {
          en: 'Governance & Audit Log',
          tr: 'Güvenlik & Denetim Kaydı (Audit Log)',
        },
        description: {
          en: 'Logs who proposed, reviewed, and approved every database change request for SOC2 and enterprise compliance.',
          tr: 'Kimin hangi değişikliği önerdiğini, kimin inceleyip onayladığını kurumsal denetim standartlarına uygun kaydeder.',
        },
      },
    ],
  },

  // ── 10. Özel Rehber: Server-Side Branch Sistemi ─────────────────────────
  branch: {
    id: 'branch',
    category: { en: 'Branching System', tr: 'Sunucu-Taraflı Branch Sistemi' },
    steps: [
      {
        target: '#branch-control-container, #schema-info-panel',
        badge: { en: 'Branch Isolation', tr: 'İzole Branchler' },
        directive: {
          en: 'Experiment with structural changes on an isolated branch without affecting teammates.',
          tr: 'Ekip arkadaşlarınızı ve canlıyı etkilemeden izole branch’lerde yeni modeller deneyin.',
        },
        title: {
          en: 'Server-Side Database Branching',
          tr: 'Sunucu-Taraflı Veritabanı Branch Modeli',
        },
        description: {
          en: 'Branches clone the relational architecture in a sandbox, letting you add experimental tables and test foreign keys safely.',
          tr: 'Şemanızı izole bir çalışma dalına kopyalar; deneysel tabloları ve ilişkileri güvenle denemenizi sağlar.',
        },
      },
      {
        target: '#branch-selector-btn',
        badge: { en: 'Branch Switcher', tr: 'Branch Seçici' },
        directive: {
          en: 'Switch between main, staging, and feature branches or create new branches.',
          tr: 'Main, staging ve özellik dalları arasında geçiş yapın ya da yeni bir branch oluşturun.',
        },
        title: {
          en: 'Switch & Manage Branches',
          tr: 'Dallar Arasında Geçiş & Yönetim',
        },
        description: {
          en: 'The canvas immediately reflects the branch schema, preserving your layouts, position coordinates, and relations per branch.',
          tr: 'Tuval seçtiğiniz branch’in şemasını anında yükler; yerleşim ve koordinatlarınızı dal bazında korur.',
        },
      },
      {
        target: '#branch-diff-btn',
        badge: { en: 'Visual Diff', tr: 'Görsel Fark' },
        directive: {
          en: 'Toggle side-by-side visual difference mode on the canvas.',
          tr: 'Tuval üzerinde dallar arası görsel fark karşılaştırma modunu açıp kapatın.',
        },
        title: {
          en: 'Visual Diff Comparison Mode',
          tr: 'Görsel Şema Fark Karşılaştırması',
        },
        description: {
          en: 'Highlights added, modified, and deleted tables directly on the canvas with color-coded badges and change summaries.',
          tr: 'Eklenen, değişen ve silinen tabloları tuval üzerinde renkli rozetlerle ve fark özetleriyle gösterir.',
        },
      },
      {
        target: '#branch-merge-btn, #branch-selector-btn',
        badge: { en: 'Safe Merging', tr: 'Güvenli Birleştirme' },
        directive: {
          en: 'Initiate a 3-way merge review session to integrate branch modifications back to main.',
          tr: 'Branch değişikliklerinizi ana şemaya aktarmak için 3-yollu birleştirme oturumu başlatın.',
        },
        title: {
          en: '3-Way Merge & Conflict Resolution',
          tr: '3-Yollu Birleştirme & Çakışma Çözümü',
        },
        description: {
          en: 'Merges follow the 3-way merge review protocol, ensuring zero data loss and automated test validation before deployment.',
          tr: 'Birleştirme işlemi ortak ata analizi ve otomatik testlerden geçerek canlıya güvenle aktarılır.',
        },
      },
    ],
  },

  // ── 11. Özel Rehber: Namines Hub (/hub) ─────────────────────────────────
  hub: {
    id: 'hub',
    category: { en: 'Namines Hub', tr: 'Namines Hub Mimarisi & Depo Yönetimi' },
    steps: [
      {
        target: 'header',
        badge: { en: 'Hub Repository', tr: 'Hub Deposu' },
        directive: {
          en: 'Explore published architecture repositories, schemas, and blueprints.',
          tr: 'Yayınlanan mimari depolarını, şemaları ve hazır şablonları inceleyin.',
        },
        title: {
          en: 'Architecture Hub & Blueprints',
          tr: 'Mimari Deposu ve Şablonlar',
        },
        description: {
          en: 'GitHub-form repository experience tailored for relational databases, schema visualizers, and team governance.',
          tr: 'İlişkisel veritabanları, görsel şema tuvali ve ekip işbirliği için tasarlanmış GitHub benzeri depo deneyimi.',
        },
      },
      {
        target: 'main',
        badge: { en: 'Schema Visualizer & Fullscreen', tr: 'Şema Görselleştirici & Tam Ekran' },
        directive: {
          en: 'Toggle interactive ER diagrams, flow pipelines, and enter fullscreen mode.',
          tr: 'Etkileşimli ER diyagramlarını, veri akışlarını inceleyin ve tam ekran moduna geçin.',
        },
        title: {
          en: 'Live Canvas & Code Explorer',
          tr: 'Canlı Tuval ve Kod Gezgini',
        },
        description: {
          en: 'Switch branches live, inspect tables, and preview compiled DDL, Prisma, EF Core, and OpenAPI specifications.',
          tr: 'Dallar arasında canlı geçiş yapın, tabloları inceleyin ve derlenen DDL, Prisma, EF Core kodlarını görüntüleyin.',
        },
      },
      {
        target: 'main',
        badge: { en: 'Add Artifacts & Pull Requests', tr: 'Yapıt Ekleme & PR İncelemeleri' },
        directive: {
          en: 'Import Mermaid diagrams, SDKs, or propose branch changes via New Pull Request.',
          tr: 'Mermaid diyagramları ve SDK derlemelerini depoya aktarın veya New Pull Request ile PR açın.',
        },
        title: {
          en: 'Build Artifacts & Governance PRs',
          tr: 'Derleme Yapıtları & Yönetişim PR’ları',
        },
        description: {
          en: 'Integrate 7 Mermaid diagram types, 10 compiler targets, and manage automated schema review requests.',
          tr: '7 farklı Mermaid diyagramını, 10 derleyici hedefini içe aktarın ve otomatik şema inceleme isteklerini yönetin.',
        },
      },
    ],
  },

  // ── 12. Özel Rehber: Namines Desk (/desk) ─────────────────────────────────
  desk: {
    id: 'desk',
    category: { en: 'Namines Desk', tr: 'Namines Desk Veri Stüdyosu' },
    steps: [
      {
        target: 'header',
        badge: { en: 'Global Table Selector', tr: 'Global Tablo Seçici' },
        directive: {
          en: 'Switch between database tables globally across all tabs from the top breadcrumbs bar.',
          tr: 'Üst çubuktan tüm sekmelerde geçerli olacak şekilde tablolar arasında anında geçiş yapın.',
        },
        title: {
          en: 'Global Table Navigation',
          tr: 'Genel Tablo Gezintisi',
        },
        description: {
          en: 'Persists active table selection seamlessly across Data Browser, Schema & DDL, SQL Console, and Gateway API tabs.',
          tr: 'Veri Tarayıcısı, Şema & DDL, SQL Konsolu ve Gateway sekmelerinde seçili tabloyu kesintisiz korur.',
        },
      },
      {
        target: 'main',
        badge: { en: 'Data Browser & Inline Mutation', tr: 'Veri Tarayıcı & Doğrudan Güncelleme' },
        directive: {
          en: 'Inspect records with horizontal scroll indicators, filter queries, and insert new records.',
          tr: 'Kayıtları yatay kaydırma göstergeleriyle ferahça inceleyin, arama yapın ve yeni kayıtlar ekleyin.',
        },
        title: {
          en: 'High-Density Relational Grid',
          tr: 'Yüksek Yoğunluklu İlişkisel Tablo',
        },
        description: {
          en: 'View sample data, mutate columns inline, and inspect foreign key relationships directly in the grid.',
          tr: 'Örnek verileri görüntüleyin, kolonları doğrudan güncelleyin ve yabancı anahtar ilişkilerini takip edin.',
        },
      },
      {
        target: 'main',
        badge: { en: 'SQL Console & Gateway API', tr: 'SQL Konsolu & Gateway API' },
        directive: {
          en: 'Execute SQL queries or inspect REST API endpoints generated for your tables.',
          tr: 'SQL sorgularını çalıştırın veya tablolarınız için otomatik üretilen REST Gateway uçlarını test edin.',
        },
        title: {
          en: 'Interactive SQL & Auto REST Endpoints',
          tr: 'Etkileşimli SQL & Otomatik REST Uçları',
        },
        description: {
          en: 'Test CRUD operations, filter query history, and simulate real API requests against your database.',
          tr: 'CRUD işlemlerini test edin, sorgu geçmişinizi inceleyin ve veritabanınıza karşı gerçek API istekleri simüle edin.',
        },
      },
    ],
  },
};
