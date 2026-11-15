export type AppSlug = 'desk' | 'vault' | 'ground' | 'flow';

export interface AppCatalogEntry {
  slug: AppSlug;
  name: string;
  launchHref: string;
  badge?: { en: string; tr: string };
  tagline: { en: string; tr: string };
  description: { en: string; tr: string };
  ctaLabel: { en: string; tr: string };
  secondaryCta: { labelEn: string; labelTr: string; href: string };
  capabilities: {
    title: { en: string; tr: string };
    detail: { en: string; tr: string };
  }[];
  workflow: {
    step: string;
    title: { en: string; tr: string };
    detail: { en: string; tr: string };
  }[];
  securityNote: { en: string; tr: string };
}

export const APPS_CATALOG: Record<AppSlug, AppCatalogEntry> = {
  desk: {
    slug: 'desk',
    name: 'Namines Desk',
    launchHref: '/desk',
    tagline: {
      en: 'Browser-based SQL workspace & live data studio',
      tr: 'Tarayıcı tabanlı SQL çalışma alanı ve canlı veri stüdyosu',
    },
    description: {
      en: 'Inspect tables, browse and edit rows inline, and run read-only SQL queries directly against your connected database—or preview sample data generated locally from your schema before connecting anything.',
      tr: 'Tabloları inceleyin, satırları satır içi görüntüleyip düzenleyin ve bağlı veritabanınızda salt-okunur SQL sorguları çalıştırın; ya da hiçbir bağlantı kurmadan önce şemanızdan tarayıcıda üretilen örnek verilerle önizleme yapın.',
    },
    ctaLabel: {
      en: 'Launch Namines Desk',
      tr: 'Namines Desk’i Başlat',
    },
    secondaryCta: {
      labelEn: 'Read Gateway API docs',
      labelTr: 'Gateway API dokümanlarını oku',
      href: '/docs#gateway',
    },
    capabilities: [
      {
        title: { en: 'Live Grid & Inline Editing', tr: 'Canlı Tablo ve Satır İçi Düzenleme' },
        detail: {
          en: 'Paginated data browser with primary-key validation, column sorting, and safe row updates.',
          tr: 'Birincil anahtar doğrulamalı, kolon sıralamalı ve güvenli satır güncellemeli sayfalı veri gezgini.',
        },
      },
      {
        title: { en: 'Schema-Aware SQL Console', tr: 'Şema Farkındalıklı SQL Konsolu' },
        detail: {
          en: 'Execute single read-only queries with automatic row limits, execution timing, and error diagnostics.',
          tr: 'Otomatik satır sınırı, çalışma süresi ve hata tanılama ile tekil salt-okunur sorgular çalıştırın.',
        },
      },
      {
        title: { en: 'Zero-Leak Sample Mode', tr: 'Sızıntısız Örnek Veri Modu' },
        detail: {
          en: 'When signed out or without a Vault connection, realistic preview rows are synthesized purely in your browser.',
          tr: 'Oturum açık değilken veya Vault bağlantısı yokken gerçekçi önizleme satırları tamamen tarayıcınızda üretilir.',
        },
      },
    ],
    workflow: [
      {
        step: '01',
        title: { en: 'Pick a Project', tr: 'Projenizi Seçin' },
        detail: {
          en: 'Open any designed or introspected schema in your workspace.',
          tr: 'Çalışma alanınızda tasarladığınız veya içe aktardığınız şemayı seçin.',
        },
      },
      {
        step: '02',
        title: { en: 'Preview or Connect', tr: 'Önizleyin veya Bağlanın' },
        detail: {
          en: 'Explore synthetic rows immediately or attach an encrypted connection via Vault.',
          tr: 'Sentetik satırları anında inceleyin ya da Vault üzerinden şifreli bağlantı ekleyin.',
        },
      },
      {
        step: '03',
        title: { en: 'Query & Govern', tr: 'Sorgulayın ve Yönetin' },
        detail: {
          en: 'Inspect records, edit safely with PK checks, and review audit trails.',
          tr: 'Kayıtları inceleyin, PK kontrolleriyle güvenle düzenleyin ve denetim kayıtlarını izleyin.',
        },
      },
    ],
    securityNote: {
      en: 'All live database requests route through SSRF-hardened Gateway endpoints with project-scoped permissions.',
      tr: 'Tüm canlı veritabanı istekleri, SSRF korumalı Gateway uç noktaları ve proje bazlı yetkiler üzerinden yürütülür.',
    },
  },
  vault: {
    slug: 'vault',
    name: 'Namines Vault',
    launchHref: '/vault',
    tagline: {
      en: 'Encrypted backups, snapshots, and connection keys',
      tr: 'Şifreli yedekler, anlık görüntüler ve bağlantı anahtarları',
    },
    description: {
      en: 'Store database connection strings, point-in-time schema snapshots, and encrypted backup archives in one isolated control plane—never exposed in client code or Git repositories.',
      tr: 'Veritabanı bağlantı dizelerini, belirli zamanlı şema anlık görüntülerini ve şifreli yedek arşivlerini tek bir izole kontrol katmanında saklayın; istemci koduna veya Git depolarına asla sızdırmayın.',
    },
    ctaLabel: {
      en: 'Open Namines Vault',
      tr: 'Namines Vault’u Aç',
    },
    secondaryCta: {
      labelEn: 'Explore security model',
      labelTr: 'Güvenlik mimarisini incele',
      href: '/security',
    },
    capabilities: [
      {
        title: { en: 'Encrypted Connection Store', tr: 'Şifreli Bağlantı Kasası' },
        detail: {
          en: 'Credentials are encrypted at rest with dedicated keys and validated against private-network SSRF rules.',
          tr: 'Kimlik bilgileri özel anahtarlarla şifrelenerek saklanır ve özel ağ SSRF kurallarına karşı doğrulanır.',
        },
      },
      {
        title: { en: 'Automated Snapshots', tr: 'Otomatik Anlık Görüntüler' },
        detail: {
          en: 'Capture deterministic schema and backup checkpoints before running risky migrations.',
          tr: 'Riskli migration adımlarından önce deterministik şema ve yedek kontrol noktaları oluşturun.',
        },
      },
      {
        title: { en: 'Scoped Gateway API Keys', tr: 'Kapsamlı Gateway API Anahtarları' },
        detail: {
          en: 'Issue read-only or write-scoped tokens for CI/CD runners, MCP agents, and Desk studios.',
          tr: 'CI/CD koşucuları, MCP ajanları ve Desk stüdyosu için salt-okunur veya yazma yetkili anahtarlar üretin.',
        },
      },
    ],
    workflow: [
      {
        step: '01',
        title: { en: 'Register Target', tr: 'Hedefi Tanımlayın' },
        detail: {
          en: 'Add your PostgreSQL, MySQL, SQL Server, or SQLite endpoint.',
          tr: 'PostgreSQL, MySQL, SQL Server veya SQLite uç noktanızı ekleyin.',
        },
      },
      {
        step: '02',
        title: { en: 'Encrypt & Verify', tr: 'Şifreleyin ve Doğrulayın' },
        detail: {
          en: 'Test connectivity cleanly without leaking credentials into logs.',
          tr: 'Kimlik bilgilerini loglara sızdırmadan bağlantıyı güvenle test edin.',
        },
      },
      {
        step: '03',
        title: { en: 'Snapshot & Restore', tr: 'Yedekleyin ve Geri Yükleyin' },
        detail: {
          en: 'Trigger encrypted backups or share scoped access with Desk and Flow.',
          tr: 'Şifreli yedekler alın veya Desk ve Flow ile kapsamlı erişim paylaşın.',
        },
      },
    ],
    securityNote: {
      en: 'Secrets are encrypted server-side and masked in all UI responses and audit logs.',
      tr: 'Gizli anahtarlar sunucu tarafında şifrelenir; tüm arayüz yanıtlarında ve denetim kayıtlarında maskelenir.',
    },
  },
  ground: {
    slug: 'ground',
    name: 'Namines Ground',
    launchHref: '/ground',
    tagline: {
      en: 'Managed Postgres provisioning and query telemetry',
      tr: 'Yönetilen Postgres sağlama ve sorgu telemetrisi',
    },
    description: {
      en: 'Spin up isolated PostgreSQL environments for branches, staging previews, and ephemeral DDL verification—complete with connection telemetry, quota tracking, and instant teardown.',
      tr: 'Dallar, önizleme ortamları ve geçici DDL doğrulaması için izole PostgreSQL ortamlarını saniyeler içinde ayağa kaldırın; bağlantı telemetrisi, kota takibi ve anında temizlik dahil.',
    },
    ctaLabel: {
      en: 'Open Namines Ground',
      tr: 'Namines Ground’a Geç',
    },
    secondaryCta: {
      labelEn: 'See sandbox docs',
      labelTr: 'Sandbox dokümanlarına bak',
      href: '/docs#sandbox',
    },
    capabilities: [
      {
        title: { en: 'Instant Branch Databases', tr: 'Anlık Dal Veritabanları' },
        detail: {
          en: 'Provision isolated Postgres instances tied to server-authoritative schema branches.',
          tr: 'Sunucu otoriteli şema dallarına bağlı izole Postgres örnekleri oluşturun.',
        },
      },
      {
        title: { en: 'Live Query & Quota Telemetry', tr: 'Canlı Sorgu ve Kota Telemetrisi' },
        detail: {
          en: 'Monitor active connections, storage footprints, and monthly runtime usage in real time.',
          tr: 'Aktif bağlantıları, depolama kullanımını ve aylık çalışma süresi kotalarını canlı takip edin.',
        },
      },
      {
        title: { en: 'Ephemeral DDL Verification', tr: 'Geçici DDL Doğrulaması' },
        detail: {
          en: 'Validate generated migrations against real engines before anything reaches production.',
          tr: 'Üretilen migration dosyalarını canlı ortama çıkmadan önce gerçek motorlarda doğrulayın.',
        },
      },
    ],
    workflow: [
      {
        step: '01',
        title: { en: 'Provision Instance', tr: 'Ortam Oluşturun' },
        detail: {
          en: 'Select region/tier and spin up a managed Postgres database.',
          tr: 'Planınızı seçip yönetilen Postgres veritabanını başlatın.',
        },
      },
      {
        step: '02',
        title: { en: 'Apply & Benchmark', tr: 'Uygulayın ve Ölçün' },
        detail: {
          en: 'Push your compiled ir.json DDL and inspect execution telemetry.',
          tr: 'Derlenen ir.json DDL çıktısını uygulayın ve sorgu telemetrisini izleyin.',
        },
      },
      {
        step: '03',
        title: { en: 'Promote or Teardown', tr: 'Yükseltin veya Kapatın' },
        detail: {
          en: 'Keep persistent staging environments or dispose branches cleanly.',
          tr: 'Kalıcı önizleme ortamlarını koruyun ya da iş bitince dalları temizleyin.',
        },
      },
    ],
    securityNote: {
      en: 'Every Ground instance runs in network-isolated containers with strict per-project quota enforcement.',
      tr: 'Her Ground örneği ağ izolasyonlu kapsayıcılarda, proje bazlı sıkı kota denetimiyle çalışır.',
    },
  },
  flow: {
    slug: 'flow',
    name: 'Namines Flow',
    launchHref: '/flow',
    badge: { en: 'New', tr: 'Yeni' },
    tagline: {
      en: 'Event-driven schema automation and webhook rules',
      tr: 'Olay güdümlü şema otomasyonu ve webhook kuralları',
    },
    description: {
      en: 'Automate what happens after every schema change: trigger signed webhooks, notify Slack or GitHub PRs on breaking diffs, auto-approve Safe migrations, and orchestrate downstream ejects.',
      tr: 'Her şema değişikliğinden sonra olacakları otomatikleştirin: imzalı webhook tetikleyin, kırıcı farklarda Slack veya GitHub PR bildirimi gönderin, güvenli geçişleri otomatik onaylayın.',
    },
    ctaLabel: {
      en: 'Open Namines Flow',
      tr: 'Namines Flow’u Aç',
    },
    secondaryCta: {
      labelEn: 'Explore CI/CD solutions',
      labelTr: 'CI/CD çözümlerini incele',
      href: '/solutions#ci-cd',
    },
    capabilities: [
      {
        title: { en: 'Risk-Based Rule Engine', tr: 'Risk Tabanlı Kural Motoru' },
        detail: {
          en: 'Route Safe, Warning, and Breaking schema changes to different approval or alert pipelines.',
          tr: 'Safe, Warning ve Breaking risk seviyelerindeki şema değişikliklerini farklı onay hatlarına yönlendirin.',
        },
      },
      {
        title: { en: 'Signed Webhook Delivery', tr: 'İmzalı Webhook İletimi' },
        detail: {
          en: 'Dispatch HMAC-signed payloads to your CI runners, internal tools, or deployment hooks.',
          tr: 'CI koşucularınıza, iç araçlarınıza veya dağıtım kancalarınıza HMAC imzalı yükler gönderin.',
        },
      },
      {
        title: { en: 'Immutable Execution Log', tr: 'Değişmez Çalışma Günlüğü' },
        detail: {
          en: 'Inspect every triggered rule, retry status, and delivery response in one timeline.',
          tr: 'Tetiklenen her kuralı, yeniden deneme durumunu ve yanıt kodunu tek zaman çizelgesinde görün.',
        },
      },
    ],
    workflow: [
      {
        step: '01',
        title: { en: 'Define Trigger', tr: 'Tetikleyici Seçin' },
        detail: {
          en: 'Listen to PR impact checks, branch merges, or Vault snapshot events.',
          tr: 'PR etki analizlerini, dal birleştirmelerini veya Vault yedek olaylarını dinleyin.',
        },
      },
      {
        step: '02',
        title: { en: 'Attach Actions', tr: 'Aksiyon Bağlayın' },
        detail: {
          en: 'Configure webhook URLs, approval policies, or notification channels.',
          tr: 'Webhook adreslerini, onay politikalarını veya bildirim kanallarını ayarlayın.',
        },
      },
      {
        step: '03',
        title: { en: 'Monitor Runs', tr: 'Çalışmaları İzleyin' },
        detail: {
          en: 'Audit every automated run and replay failed deliveries with one click.',
          tr: 'Her otomasyon çalışmasını denetleyin ve başarısız iletileri tek tıkla yeniden deneyin.',
        },
      },
    ],
    securityNote: {
      en: 'Outbound webhooks enforce SSRF host validation and HMAC-SHA256 payload signatures.',
      tr: 'Giden webhook istekleri SSRF alan adı doğrulaması ve HMAC-SHA256 imza koruması uygular.',
    },
  },
};

export const APP_SLUGS: AppSlug[] = ['desk', 'vault', 'ground', 'flow'];
