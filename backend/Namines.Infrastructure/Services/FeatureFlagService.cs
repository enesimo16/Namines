using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Dinamik Özellik Bayrağı ve Acil Kapatma (Kill-Switch) Servisi.
///
/// <b>İki Çalışma Modu (Admin Tercihi):</b>
/// 1. <b>DisabledNotice:</b> Özellik arayüzde gösterilir ama tıklanamaz; üzerine tıklandığında
///    veya gelindiğinde "Bu özellik şu anda bakım çalışması nedeniyle geçici olarak devre dışıdır" uyarısı verir.
/// 2. <b>Hidden:</b> Özellik menüden, tuvalden ve yönlendirmelerden tamamen silinir, arayüzde hiç gözükmez.
/// </summary>
public sealed class FeatureFlagService : IFeatureFlagService
{
    private readonly ConcurrentDictionary<string, AdminFeatureFlag> _flagsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FeatureFlagService> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public FeatureFlagService(
        IServiceScopeFactory scopeFactory,
        ILogger<FeatureFlagService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        foreach (var flag in GetDefaultFlags())
        {
            _flagsCache[flag.FeatureKey] = flag;
        }
    }

    private static List<AdminFeatureFlag> GetDefaultFlags() => new()
    {
        // ── 1. Çekirdek Platform & Studio ────────────────────────────────────
        new AdminFeatureFlag
        {
            FeatureKey = "canvas",
            DisplayName = "Studio Şema Tuvali (ERD Designer)",
            Description = "Etkileşimli veritabanı şema tasarımı, tablo ve ilişki tuvali.",
            Category = "Platform",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Studio tuvali şu anda sistem yükseltmesi nedeniyle geçici olarak devre dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "review",
            DisplayName = "Veritabanı Değişiklik Yönetişimi (Change Review)",
            Description = "Migration risk sınıflandırması, etki analizi ve DBA onay akışı.",
            Category = "Platform",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Change Review yönetim modülü geçici bakım modundadır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "flow",
            DisplayName = "Namines Flow İş Akışı Otomasyonu",
            Description = "Şema ve tablo bazlı kural, bildirim ve webhook otomasyon hattı.",
            Category = "Platform",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Namines Flow otomasyon motoru bakım nedeniyle geçici olarak kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "ground",
            DisplayName = "Ground Canlı DB Bağlantısı (BYODB)",
            Description = "Kullanıcının kendi canlı veritabanına bağlanma ve şema eşitleme.",
            Category = "Platform",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Ground veritabanı bağlantı servisi planlı bakım nedeniyle geçici olarak kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "vault",
            DisplayName = "Vault Akıllı Snapshot & Yedekleme",
            Description = "Otomatik veritabanı yedeği, snapshot ve geri yükleme merkezi.",
            Category = "Platform",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Vault yedekleme servisi depolama bakımı nedeniyle geçici olarak devre dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "desk",
            DisplayName = "Namines Desk Çok Kiracılı Yönetim Konsolu",
            Description = "SQL sorgulama, veri tablosu ve kullanıcı yönetimi mikroservisi.",
            Category = "Platform",
            IsEnabled = true,
            Mode = "Hidden",
            NoticeMessage = "Desk mikroservisi bakım nedeniyle geçici olarak erişilemez durumdadır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "schema_generator",
            DisplayName = "Yapay Zeka ile Şema Üretimi",
            Description = "Doğal dil prompt'u, resim veya URL'den veritabanı mimarisi üretimi.",
            Category = "AI",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "AI şema üretimi kota/sağlayıcı yükseltmesi nedeniyle geçici olarak kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "ai_dba",
            DisplayName = "AI DBA Optimizasyon & İndeks Danışmanı",
            Description = "Otonom DBA analizi, eksik indeks tespiti ve şema skoru.",
            Category = "AI",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "AI DBA servisi bakım nedeniyle geçici olarak devre dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "smart_seed",
            DisplayName = "Akıllı Sentetik Veri Tohumlayıcı (Smart Seed)",
            Description = "Tablo ilişkilerine ve türlerine duyarlı gerçekçi mock veri üretimi.",
            Category = "AI",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Smart Seed veri üreticisi geçici bakım modundadır."
        },

        // ── 2. Canvas Toolkit (11 Araç) ─────────────────────────────────────
        new AdminFeatureFlag
        {
            FeatureKey = "tool_dba_advisor",
            DisplayName = "Toolkit 1/11 • DBA Denetimi & İndeks Analizi",
            Description = "Tablo ve indeks sağlık skorları, eksik foreign key ve güvenlik denetimleri.",
            Category = "Tools",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "DBA denetim aracı bakım nedeniyle geçici olarak devre dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "tool_schema_explain",
            DisplayName = "Toolkit 2/11 • AI ile Şema Açıklama",
            Description = "Veritabanı ilişkilerinin ve tabloların doğal dil mimari özeti.",
            Category = "Tools",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Şema açıklama servisi geçici olarak kullanım dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "tool_reverse_code",
            DisplayName = "Toolkit 3/11 • Koddan Tersine Mühendislik (Reverse Code)",
            Description = "C# EF Core, TypeScript ve Python sınıflarından veritabanı şeması çıkarma.",
            Category = "Tools",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Koddan şema çıkarma motoru güncelleniyor."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "tool_live_db",
            DisplayName = "Toolkit 4/11 • Canlı DB İçe Aktarımı (Live DB Connect)",
            Description = "Canlı PostgreSQL, MySQL veya SQLite bağlantısı ile şema içe aktarma.",
            Category = "Tools",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Canlı veritabanı bağlantı aracı bakım modundadır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "tool_gateway_viewer",
            DisplayName = "Toolkit 5/11 • Canlı Kayıt Gezgini (Gateway Table Viewer)",
            Description = "Canlı tablolardaki gerçek satırları güvenli salt-okunur arayüzde inceleme.",
            Category = "Tools",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Canlı kayıt gezgini geçici olarak devre dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "tool_migration_engine",
            DisplayName = "Toolkit 6/11 • DDL Migrasyon Motoru",
            Description = "Şema sürümlerini kaydetme, versiyon diff'i ve göç betikleri oluşturma.",
            Category = "Tools",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Migrasyon motoru bakım nedeniyle kullanılamıyor."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "tool_virtual_relations",
            DisplayName = "Toolkit 7/11 • Çapraz Veritabanı Sanal İlişkileri",
            Description = "Ayrı DB motorları ve servisler arasında sanal foreign key tanımlama.",
            Category = "Tools",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Çapraz DB ilişkileri yönetim aracı geçici olarak kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "tool_vision_ocr",
            DisplayName = "Toolkit 8/11 • Görselden / Çizimden Şema Üretimi (Vision)",
            Description = "Beyaz tahta şemaları ve ERD diyagram fotoğraflarından yapay zeka ile şema üretimi.",
            Category = "Tools",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Vision şema tarayıcısı bakım modundadır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "tool_sql_import",
            DisplayName = "Toolkit 9/11 • SQL DDL Dosyası Yükleme (.sql)",
            Description = "Mevcut CREATE TABLE ve SQL DDL betiklerini tuvale yükleme.",
            Category = "Tools",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "SQL dosya yükleme özelliği geçici olarak kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "tool_edit_mode",
            DisplayName = "Toolkit 10/11 • Tuval Görsel Düzenleme Modu",
            Description = "Tablo alanlarını ve tiplerini tuval üzerinde doğrudan interaktif düzenleme modu.",
            Category = "Tools",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Görsel düzenleme modu kilitlenmiştir (yalnızca izleme aktif)."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "tool_export_hub",
            DisplayName = "Toolkit 11/11 • Dışa Aktarım Menüsü (Export Hub)",
            Description = "PNG, SVG, SQL, Prisma, PDF ve Full-Stack .zip paket indirme menüsü.",
            Category = "Tools",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Dışa aktarım menüsü bakım nedeniyle geçici olarak devre dışıdır."
        },

        // ── 3. Compile & Eject Sekmeleri & Çıktıları ─────────────────────────
        new AdminFeatureFlag
        {
            FeatureKey = "compile_launch",
            DisplayName = "Compile • Buluta Tek Tıkla Dağıtım (Launch to Cloud)",
            Description = "Şemayı doğrudan Neon, Supabase veya özel bulut ortamlarına aktarma.",
            Category = "Compile",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Buluta dağıtım servisi bakım modundadır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "compile_sandbox",
            DisplayName = "Compile • Docker Test Sandbox Doğrulaması",
            Description = "DDL betiğini gerçek Docker container'ında otomatik test edip doğrulama.",
            Category = "Compile",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Docker test sandbox altyapısı bakım nedeniyle devre dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "compile_sql",
            DisplayName = "Compile • Çok Motorlu DDL Betiği Üretimi (.sql)",
            Description = "PostgreSQL, MySQL, SQLite, SQL Server, Oracle ve CockroachDB DDL derleyici.",
            Category = "Compile",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "SQL DDL derleyici geçici olarak devre dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "compile_ef",
            DisplayName = "Compile • Entity Framework Core Modelleri (.cs)",
            Description = "C# DbContext sınıfları, POCO modelleri ve Fluent API konfigürasyonları.",
            Category = "Compile",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "EF Core model üreticisi geçici bakım modundadır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "compile_prisma",
            DisplayName = "Compile • Prisma ORM Şeması (.prisma)",
            Description = "Prisma şeması, relation ve model mapping üretimi.",
            Category = "Compile",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Prisma çıktı üreticisi geçici olarak devre dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "compile_eject",
            DisplayName = "Compile • Full-Stack Eject Çıkış Motoru (19 SDK/Framework)",
            Description = "Modern backend kütüphanelerine ve tam tekmil yönetim panellerine export.",
            Category = "Compile",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Eject kod ihracı servisi bakım nedeniyle kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "compile_mermaid",
            DisplayName = "Compile • Mermaid ER & 13 Diyagram Türü",
            Description = "ER, Class, Flowchart, Sequence, State, GitGraph ve Gantt görselleştirmeleri.",
            Category = "Compile",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Diyagram görselleştirme motoru geçici olarak kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "compile_dictionary",
            DisplayName = "Compile • Veri Sözlüğü & PDF Raporu (Data Dictionary)",
            Description = "Tüm tabloların, kısıtların ve kolonların teknik veri sözlüğü çıktısı.",
            Category = "Compile",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Veri sözlüğü üreticisi bakım modundadır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "compile_readme",
            DisplayName = "Compile • Teknik README.md & Mimari Dokümanı",
            Description = "AI destekli proje mimarisi, kurulum yönergeleri ve şema açıklamaları.",
            Category = "Compile",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Dokümantasyon üreticisi bakım nedeniyle devre dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "compile_mock",
            DisplayName = "Compile • Akıllı Sentetik Test Verisi (Smart Seed Mock)",
            Description = "İlişkisel bütünlüğü sağlayan SQL INSERT ve JSON mock veri üretimi.",
            Category = "Compile",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Test verisi üretim modülü geçici bakım modundadır."
        },

        // ── 4. Eject SDK, Framework & Contract Hedefleri ────────────────────
        new AdminFeatureFlag
        {
            FeatureKey = "eject_types_ts",
            DisplayName = "Eject • TypeScript Tipleri (types.typescript)",
            Description = "Tip güvenli TypeScript interface ve type tanımları.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "TypeScript tip üretimi geçici olarak kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_sdk_ts",
            DisplayName = "Eject • TypeScript SDK (sdk.typescript)",
            Description = "Tip güvenli API istemcisi ve veri yönetim SDK paketi.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "TypeScript SDK ihracı geçici olarak kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_zod",
            DisplayName = "Eject • Zod Şema Doğrulayıcı (schemas.zod)",
            Description = "Runtime form ve veri doğrulama için Zod şemaları.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Zod şema üreticisi geçici bakım modundadır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_types_csharp",
            DisplayName = "Eject • C# POCO Tipleri (types.csharp)",
            Description = ".NET POCO sınıfları ve record yapıları.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "C# tip üretimi geçici olarak kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_pydantic",
            DisplayName = "Eject • Python Pydantic Modelleri (types.pydantic)",
            Description = "FastAPI ve Python projeleri için Pydantic v2 modelleri.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Python Pydantic modeli üretimi geçici olarak kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_graphql",
            DisplayName = "Eject • GraphQL SDL Şeması (schema.graphql)",
            Description = "GraphQL Query, Mutation ve Tip tanımlamaları.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "GraphQL şema ihracı geçici bakım modundadır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_json_schema",
            DisplayName = "Eject • JSON Schema Sözleşmesi (schema.json)",
            Description = "Standart JSON Schema v7 doğrulama tanımları.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "JSON Schema üretimi geçici olarak devre dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_protobuf",
            DisplayName = "Eject • Protocol Buffers IDL (schema.proto)",
            Description = "gRPC ve microservice iletişimi için Protobuf v3 tanımları.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Protobuf üretimi bakım nedeniyle kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_drizzle",
            DisplayName = "Eject • Drizzle ORM Modelleri (orm.drizzle)",
            Description = "TypeScript için Drizzle ORM tablo şemaları ve ilişkileri.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Drizzle ORM üretimi geçici olarak kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_typeorm",
            DisplayName = "Eject • TypeORM Varlık Sınıfları (orm.typeorm)",
            Description = "TypeScript / Node.js için TypeORM Entity modelleri.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "TypeORM üretimi geçici bakım modundadır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_sqlalchemy",
            DisplayName = "Eject • SQLAlchemy Python ORM (orm.sqlalchemy)",
            Description = "Python için SQLAlchemy Declarative Base modelleri.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "SQLAlchemy üretimi geçici olarak devre dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_django",
            DisplayName = "Eject • Django ORM Modelleri (orm.django)",
            Description = "Django framework için models.py tanımlamaları.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Django model üretimi bakım modundadır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_sequelize",
            DisplayName = "Eject • Sequelize ORM Modelleri (orm.sequelize)",
            Description = "Node.js için Sequelize model tanımları ve association'lar.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Sequelize modeli üretimi geçici olarak kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_gorm",
            DisplayName = "Eject • Go GORM Yapıları (orm.gorm)",
            Description = "Golang projeleri için GORM struct ve tag tanımları.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Go GORM üretimi bakım nedeniyle devre dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_nextjs_console",
            DisplayName = "Eject • Next.js Yönetim Paneli (console.nextjs)",
            Description = "Şemanıza özel çalışan, hazır Next.js CRUD yönetim paneli kodu.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Next.js yönetim paneli üretimi bakım modundadır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_flyway",
            DisplayName = "Eject • Flyway SQL Migrasyonları (migration.flyway)",
            Description = "Java / Spring Boot uyumlu versiyonlu V1__init.sql betikleri.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Flyway migrasyon üretimi geçici olarak devre dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_liquibase",
            DisplayName = "Eject • Liquibase Değişiklik Seti (migration.liquibase)",
            Description = "Liquibase changelog ve changeset XML/SQL dosyaları.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Liquibase üretimi geçici olarak kapalıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_canonical_ir",
            DisplayName = "Eject • Kanonik JSON IR (spec.ir)",
            Description = "Namines kanonik AST JSON ara temsili (ir.json).",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "Kanonik IR üretimi geçici olarak devre dışıdır."
        },
        new AdminFeatureFlag
        {
            FeatureKey = "eject_nsl",
            DisplayName = "Eject • NSL Namines Şema Dili (spec.nsl)",
            Description = "İnsan tarafından kolay okunabilen Namines şema deklarasyonu.",
            Category = "Eject",
            IsEnabled = true,
            Mode = "DisabledNotice",
            NoticeMessage = "NSL üretimi geçici bakım modundadır."
        }
    };

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetService<AuthDbContext>();
            if (db != null)
            {
                try
                {
                    var flags = await db.FeatureFlags.AsNoTracking().ToListAsync(ct);
                    var defaults = GetDefaultFlags();

                    if (flags.Count > 0)
                    {
                        foreach (var flag in flags)
                        {
                            _flagsCache[flag.FeatureKey] = flag;
                        }

                        // Veritabanında eksik kalmış yeni varsayılan bayrakları senkronize et
                        var existingKeys = flags.Select(f => f.FeatureKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var missing = defaults.Where(d => !existingKeys.Contains(d.FeatureKey)).ToList();
                        if (missing.Count > 0)
                        {
                            db.FeatureFlags.AddRange(missing);
                            await db.SaveChangesAsync(ct);
                            foreach (var m in missing)
                            {
                                _flagsCache[m.FeatureKey] = m;
                            }
                            _logger.LogInformation("{Count} adet yeni özellik bayrağı veritabanı ile senkronize edildi.", missing.Count);
                        }
                    }
                    else
                    {
                        db.FeatureFlags.AddRange(defaults);
                        await db.SaveChangesAsync(ct);
                        foreach (var d in defaults)
                        {
                            _flagsCache[d.FeatureKey] = d;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "FeatureFlags tablosu okunamadı veya henüz migrate edilmedi. Bellek içi bayraklar devrede.");
                }
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<IReadOnlyList<AdminFeatureFlag>> GetAllFlagsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return _flagsCache.Values.OrderBy(f => f.Category).ThenBy(f => f.DisplayName).ToList();
    }

    public async Task<AdminFeatureFlag?> GetFlagAsync(string featureKey, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        _flagsCache.TryGetValue(featureKey, out var flag);
        return flag;
    }

    public async Task UpdateFlagAsync(
        string featureKey,
        bool isEnabled,
        string mode,
        string? noticeMessage = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        if (!_flagsCache.TryGetValue(featureKey, out var flag))
        {
            flag = new AdminFeatureFlag
            {
                FeatureKey = featureKey,
                DisplayName = featureKey,
                Description = "Özel Tanımlı Özellik",
                Category = "Custom"
            };
        }

        flag.IsEnabled = isEnabled;
        flag.Mode = string.Equals(mode, "Hidden", StringComparison.OrdinalIgnoreCase) ? "Hidden" : "DisabledNotice";
        if (!string.IsNullOrWhiteSpace(noticeMessage))
        {
            flag.NoticeMessage = noticeMessage.Trim();
        }
        flag.UpdatedAt = DateTime.UtcNow;

        _flagsCache[featureKey] = flag;

        // DB'ye yaz
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<AuthDbContext>();
        if (db != null)
        {
            try
            {
                var existing = await db.FeatureFlags.FirstOrDefaultAsync(f => f.FeatureKey == featureKey, ct);
                if (existing != null)
                {
                    existing.IsEnabled = flag.IsEnabled;
                    existing.Mode = flag.Mode;
                    existing.NoticeMessage = flag.NoticeMessage;
                    existing.UpdatedAt = flag.UpdatedAt;
                }
                else
                {
                    db.FeatureFlags.Add(flag);
                }
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FeatureFlag veritabanına kaydedilirken hata: {FeatureKey}", featureKey);
            }
        }

        _logger.LogInformation("Özellik bayrağı güncellendi: {FeatureKey} -> IsEnabled: {IsEnabled}, Mode: {Mode}",
            featureKey, flag.IsEnabled, flag.Mode);
    }

    public bool IsFeatureEnabled(string featureKey, out string displayMode, out string noticeMessage)
    {
        if (_flagsCache.TryGetValue(featureKey, out var flag))
        {
            displayMode = flag.Mode;
            noticeMessage = flag.NoticeMessage ?? "Bu özellik geçici olarak devre dışıdır.";
            return flag.IsEnabled;
        }

        // Tanımlı olmayan özellikler varsayılan olarak açıktır
        displayMode = "DisabledNotice";
        noticeMessage = string.Empty;
        return true;
    }
}
