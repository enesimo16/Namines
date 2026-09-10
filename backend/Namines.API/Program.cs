using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Namines.API.Extensions;
using Namines.API.Middleware;
using Namines.API.Security;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Core.Security;
using Namines.Infrastructure.Observability;
using Serilog;
using Serilog.Events;
using System.Linq;
using System.Text;
using System.Threading.RateLimiting;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.HttpOverrides;

// ── Serilog Bootstrap (before WebApplication.CreateBuilder) ─────────────────
// Birincil sink Console (stdout) — container/PaaS ortamlarında logları toplayan
// tek güvenilir kanal budur.
//
// Dosya sink'i VARSAYILAN OLARAK KAPALIDIR. Efemer dosya sistemlerinde (Railway,
// Render, Fly, Kubernetes) her deploy'da loglar kaybolur, çok instance'lı kurulumda
// log parçalanır ve disk sessizce dolabilir. Yerel geliştirmede açmak için:
//   Serilog__WriteTo__File=true
// Seq şimdilik devre dışı — ileride Seq:Url config key'i eklendiğinde aktive edilir.
const string ConsoleTemplate =
    "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";
const string FileTemplate =
    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

static bool FileLoggingEnabled(IConfiguration? configuration = null)
{
    var raw = configuration?["Serilog:WriteTo:File"]
              ?? Environment.GetEnvironmentVariable("Serilog__WriteTo__File");
    return bool.TryParse(raw, out var enabled) && enabled;
}

var bootstrapConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    // 21 §2: gizli bilgi log'a DÜŞMEDEN önceki son kapı. "Connection string'i
    // loglamayın" bir kural olarak uygulanamaz — bir istisna mesajı ya da üçüncü
    // taraf bir kütüphane onu her an düşürebilir, ve log'lar uygulamadan uzun yaşar.
    .Enrich.With<PiiRedactionEnricher>()
    .WriteTo.Console(outputTemplate: ConsoleTemplate);

if (FileLoggingEnabled())
{
    bootstrapConfig.WriteTo.File(
        path: "logs/namines-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: FileTemplate);
}

Log.Logger = bootstrapConfig.CreateBootstrapLogger();

try
{
    Log.Information("Namines API başlatılıyor...");

    // ── Tüm sırların TEK kaynağı: .env (git-ignored) ───────────────────────────
    // Yukarı doğru gezerek en yakın .env'i bulur ve ortam değişkenlerine yükler.
    // '__' ayracı .NET hiyerarşisine map olur (ör. Jwt__Key => Jwt:Key).
    // Böylece user-secrets / appsettings.secrets.json'a gerek kalmaz.
    //
    // Container/PaaS ortamlarında .env dosyası YOKTUR — sırlar gerçek ortam
    // değişkeni olarak enjekte edilir. Bu yüzden dosyanın yokluğu hata değildir.
    try
    {
        DotNetEnv.Env.TraversePath().Load();
    }
    catch (Exception ex)
    {
        Log.Information("Yerel .env yüklenmedi ({Reason}) — ortam değişkenleri kullanılacak.", ex.Message);
    }

    var builder = WebApplication.CreateBuilder(args);

    // Serilog'u ASP.NET Host'a entegre et (bootstrap logger'ı tam config ile günceller)
    builder.Host.UseSerilog((ctx, services, config) =>
    {
        config
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            // Bootstrap logger'daki ile aynı kapı; ikisinde de olmalı, çünkü
            // başlangıç hataları da bağlantı dizesi taşıyabilir.
            .Enrich.With<PiiRedactionEnricher>()
            .WriteTo.Console(outputTemplate: ConsoleTemplate);

        if (FileLoggingEnabled(ctx.Configuration))
        {
            config.WriteTo.File(
                path: "logs/namines-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: FileTemplate);
        }
    });

    // Sırların birincil kaynağı .env (yukarıda DotNetEnv ile yüklendi).
    // appsettings.secrets.json yalnızca opsiyonel eski fallback olarak kalır (varsa).
    builder.Configuration
        .AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: false);
    // Ortam değişkenleri (.env dahil) her şeyi override eder.
    builder.Configuration.AddEnvironmentVariables();

    // Configure QuestPDF license
    QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    // Configure Stripe global API key
    var stripeSecretKey = builder.Configuration["Stripe:SecretKey"];
    if (!string.IsNullOrWhiteSpace(stripeSecretKey))
    {
        Stripe.StripeConfiguration.ApiKey = stripeSecretKey;
    }
    else
    {
        Log.Warning("Stripe:SecretKey yapılandırılmamış. Ödeme özellikleri çalışmayacak.");
    }

    // Configure Database
    //
    // G7: control DB SQLite'tan PostgreSQL'e tam geçiş. Gerekçe: SQLite tek dosya,
    // eşzamanlı yazmayı desteklemez — yatay ölçeklemede (2+ API instance) veri
    // bozulması riski taşır. Ayrıca G9-G10'daki sunucu-taraflı branch tabloları
    // (new-phase/30-SERVER-SIDE-BRANCHING.md) gerçek eşzamanlı yazma gerektiriyor.
    //
    // NOT: Bu, kullanıcının HEDEF motor olarak SQLite seçme özelliğini etkilemez —
    // DatabaseExecutorService/ScaffolderService'teki Microsoft.Data.Sqlite kullanımı
    // ayrı bir şey (BYODB/execute özelliği), buradan kaldırılmadı.
    var controlDbConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5432;Database=namines_control;Username=postgres;Password=postgres";
    builder.Services.AddDbContext<AuthDbContext>(options =>
        options.UseNpgsql(controlDbConnectionString, npgsql =>
            // Gecici ag hatalari bulut ortamlarinda olagan; bunlarda istegi
            // dusurmek yerine yeniden denemek dogru.
            //
            // YALNIZCA control DB icin. Kullanicinin veritabanina giden
            // yollarda (DatabaseExecutorService, GatewayService) bu ISTENMEZ:
            // yarim uygulanmis bir DDL'i yeniden denemek, "zaten var"
            // hatalarina ya da daha kotusune yol acar. O yollar EF Core
            // kullanmadigi icin bu ayar oralara sizmiyor.
            npgsql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null)));

    // Configure Identity Core
    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // NIST SP 800-63B: karmaşıklık kuralları yerine UZUNLUK.
        // Karmaşıklık zorunlulukları kullanıcıyı `Parola1!` gibi tahmin
        // edilebilir kalıplara iter; uzunluk gerçek entropi ekler.
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 12;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;

        // Kaba kuvvete karşı hesap kilitleme. AuthController.Login artık
        // AccessFailedAsync çağırıyor; bu ayarlar olmadan o çağrı hiçbir işe
        // yaramazdı.
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders()
    // Sizdirilmis parola kontrolu. IPasswordValidator olarak kayitli oldugu
    // icin Identity'nin BUTUN parola yollarinda (kayit, parola degistirme,
    // sifirlama) kendiliginden calisiyor -- her cagri noktasina elle eklemek
    // gerekmiyor ve biri unutulamiyor.
    .AddPasswordValidator<PwnedPasswordValidator>();

// HIBP aralik API'si. ADLANDIRILMIS istemci -- tipli istemci DEGIL.
//
// AddHttpClient<T> T'yi kendi fabrikasiyla olustururken, AddPasswordValidator<T>
// T'yi Identity'nin kaydiyla olusturuyor ve o kayit tipli fabrikayi bilmiyor:
// kurucuya isimsiz varsayilan HttpClient dusuyor, BaseAddress bos kaliyor ve
// kontrol her istekte sessizce fail-open'a gidiyor. Canli denemede oldu.
builder.Services.AddHttpClient(PwnedPasswordValidator.HttpClientName, client =>
{
    client.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
    // HIBP User-Agent ZORUNLU tutuyor; gondermeyen istekler reddedilebiliyor.
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Namines-PasswordCheck");
});

    // Configure JWT Authentication
    //
    // Bu fallback anahtar DEPODA ve GitHub'da AÇIK. Onunla imzalanan bir JWT'yi
    // herkes üretebilir — yani kullanıldığı her ortamda kimlik doğrulaması
    // fiilen yoktur.
    const string DevFallbackJwtKey = "NaminesDevFallbackKey_Change_In_Production_Min32Chars!";

    var secretKey = builder.Configuration["Jwt:Key"];

    // Kapı `IsProduction()` DEĞİL `IsDevelopment()` üzerinden: ortam adı
    // `Staging`, `prod`, küçük harfli `production` ya da konteynerde unutulmuş
    // bir değer olduğunda `IsProduction()` false döner ve uygulama sessizce
    // herkesin bildiği anahtarla imzalamaya başlardı. Varsayılan güvenli tarafta
    // olmalı: Development DIŞINDA anahtar zorunlu.
    if (string.IsNullOrWhiteSpace(secretKey))
    {
        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException(
                $"Jwt:Key '{builder.Environment.EnvironmentName}' ortamında zorunludur " +
                "(env var JWT__KEY veya appsettings.secrets.json).");

        secretKey = DevFallbackJwtKey;
        Log.Warning("Jwt:Key tanımlanmamış — geliştirme fallback key'i kullanılıyor. Development DIŞINDA uygulama açılmaz.");
    }
    // Anahtar VERİLMİŞ ama fallback'in kopyası olabilir: .env dosyaları kopyala-
    // yapıştır ile çoğaldığı için bu, tanımsız bırakmaktan daha olası bir hata.
    // Tanımsızlık kontrolü onu yakalayamaz, bu yüzden ayrıca karşılaştırılıyor.
    else if (!builder.Environment.IsDevelopment() &&
             string.Equals(secretKey, DevFallbackJwtKey, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "Jwt:Key, depoda açıkça yazılı olan geliştirme anahtarıyla aynı. " +
            "Bu anahtarla imzalanan jetonları herkes üretebilir — gerçek bir anahtar üretin " +
            "(ör. `openssl rand -base64 48`).");
    }

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "NaminesServer",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "NaminesClient",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
        };

        // Authorization header yoksa token'ı httpOnly cookie'den al (XSS'e karşı localStorage'sız akış).
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (string.IsNullOrEmpty(ctx.Token))
                {
                    var cookieToken = ctx.Request.Cookies["namines_token"];
                    if (!string.IsNullOrEmpty(cookieToken))
                        ctx.Token = cookieToken;
                }
                return Task.CompletedTask;
            }
        // Jeton iptali: jetondaki SecurityStamp kopyası ile kullanıcının güncel
        // damgası karşılaştırılıyor. Bu olmadan çalınmış bir jetonu iptal etmenin
        // tek yolu imzalama anahtarını değiştirip HERKESİ çıkışa zorlamaktı.
        }.AddSecurityStampValidation();
    });

    // Add services to the container.
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // Rate limiting: pahalı/tehlikeli uçlar (Docker sandbox, DB execute) için istismarı sınırla.
    // KRİTİK: partition'sız bir limiter TÜM kullanıcılar için ortak sayaç tutar; tek kullanıcı
    // limiti doldurunca herkes 429 alır. Bu yüzden kullanıcı kimliği (yoksa IP) ile bölünür.
    // 21 §1/§3 — metrik enstrümantasyonu. İz (trace) henüz yok, gerekçesi
    // ObservabilityExtensions'ta yazılı.
    builder.Services.AddNaminesObservability(builder.Configuration, builder.Environment);

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("sensitive", httpContext =>
        {
            var partitionKey =
                httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";

            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 5,
                QueueLimit = 0
            });
        });

        // ── AI üretimi: kullanıcı başına EŞZAMANLILIK sınırı ─────────────────
        //
        // Kota rezervasyonu (AiQuotaService.TryReserveAsync) bütçe aşımını zaten
        // atomik olarak kapatıyor. Bu politika farklı bir şeyi koruyor:
        // sağlayıcının dakikalık token limitini (TPM). Bir kullanıcı 50 istek
        // birden gönderirse bütçesi yetse bile Groq'un TPM duvarına çarpar ve o
        // duvar TÜM kullanıcıları etkiler — yani tek kişi herkesin hizmetini
        // bozabilir.
        //
        // Eşzamanlılık sınırı (pencere değil) bilinçli: şema üretimi uzun süren
        // bir iş. Sabit pencere, 10 saniyelik bir üretimden sonra kullanıcıyı
        // gereksiz yere bekletirdi; eşzamanlılık ise "aynı anda ikiden fazla
        // üretim yapma" diyor ve iş bitince hemen serbest bırakıyor.
        options.AddPolicy("ai-generation", httpContext =>
        {
            var partitionKey =
                httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";

            return RateLimitPartition.GetConcurrencyLimiter(partitionKey, _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 2,
                // Bir istek kuyrukta bekleyebilir: kullanıcı iki sekmede
                // çalışıyorsa üçüncüsünü reddetmek yerine sıraya almak daha az
                // sürtünme, ama kuyruk kısa tutuluyor ki yığılma olmasın.
                QueueLimit = 1,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });
        });

        // ── Gateway veri düzlemi: AYRI politika ──────────────────────────────
        //
        // BULUNMA YERİ: /import, /rpc ve /query canlı denenirken beşinci istekten
        // sonra gövdesiz 429'lar başladı. Sebep, Gateway'in "sensitive" politikayı
        // paylaşması: dakikada 5 istek. Bu, Gateway'i normal bir uygulama için
        // KULLANILAMAZ kılıyordu (bir sayfa açılışı bile birkaç istek eder) ve
        // anahtar başına ayarlanan 600-10.000 rpm limitini (08 §5) tamamen ölü
        // koda çeviriyordu — o sayıya ulaşmanın yolu yoktu.
        //
        // Buradaki limit bir SON ÇARE: asıl sınır, anahtarın kendi
        // RateLimitPerMinute değeriyle GatewayRateLimiter'da uygulanıyor. Bu
        // politika yalnızca kimliği hiç doğrulanmamış trafiğin (geçersiz anahtar
        // deneyen bir istemci gibi) sunucuyu meşgul etmesini engelliyor.
        options.AddPolicy("gateway", httpContext =>
        {
            // Anahtarın ÖN EKİ kullanılıyor, anahtarın kendisi değil: bölüm anahtarı
            // bellekte tutulan bir sözlük anahtarıdır ve oraya bir sırrı koymak,
            // bir bellek dökümünü anahtar listesine çevirirdi. Ön ek gizli değil.
            var apiKey = httpContext.Request.Headers["X-Namines-Key"].ToString();
            var keyPrefix = apiKey.Length >= 12 ? apiKey[..12] : null;

            var partitionKey =
                httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? keyPrefix
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";

            return RateLimitPartition.GetFixedWindowLimiter("gw:" + partitionKey, _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 1200,
                QueueLimit = 0
            });
        });
    });

    // Reverse proxy (Nginx / Railway / Render / Fly) arkasında TLS proxy'de sonlanır ve
    // uygulamaya düz HTTP gelir. Bu header'lar işlenmezse Request.IsHttps=false olur
    // (auth cookie'sinin Secure bayrağı düşer) ve RemoteIpAddress proxy'yi gösterir
    // (rate limit tüm kullanıcıları tek partition'a toplar).
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // GÜVENLİK: Bilinen proxy listesini koşulsuz temizlemek, X-Forwarded-For
        // header'ını herkesin sahte doldurabilmesi demektir. Bu durumda saldırgan
        // her istekte farklı bir IP göstererek rate limit partition'ını atlatabilir.
        // Bu yüzden güvenilen ağlar config'den okunur:
        //   ForwardedHeaders__KnownNetworks__0=10.0.0.0/8
        //   ForwardedHeaders__KnownNetworks__1=100.64.0.0/10
        var knownNetworks = builder.Configuration
            .GetSection("ForwardedHeaders:KnownNetworks")
            .Get<string[]>() ?? Array.Empty<string>();

        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();

        var parsed = 0;
        foreach (var cidr in knownNetworks.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            var parts = cidr.Split('/', 2);
            if (parts.Length == 2
                && System.Net.IPAddress.TryParse(parts[0], out var prefix)
                && int.TryParse(parts[1], out var prefixLength))
            {
                options.KnownNetworks.Add(new IPNetwork(prefix, prefixLength));
                parsed++;
            }
            else if (System.Net.IPAddress.TryParse(cidr, out var proxyIp))
            {
                options.KnownProxies.Add(proxyIp);
                parsed++;
            }
            else
            {
                Log.Warning("ForwardedHeaders:KnownNetworks içindeki '{Value}' ayrıştırılamadı, yok sayıldı.", cidr);
            }
        }

        if (parsed == 0)
        {
            // PaaS'te (Railway/Render/Fly) proxy IP'si dinamiktir ve bilinmeyebilir.
            // Bu durumda header'a güvenmek zorundayız — ama bunu sessizce yapmıyoruz.
            if (builder.Environment.IsProduction())
            {
                Log.Warning(
                    "ForwardedHeaders:KnownNetworks tanımlı değil — X-Forwarded-For header'ı doğrulanmadan " +
                    "kabul edilecek. Uygulama yalnızca güvenilen bir proxy arkasında yayınlanmalıdır, " +
                    "aksi halde rate limit atlatılabilir. Proxy CIDR'larını tanımlayın.");
            }
        }
        else
        {
            Log.Information("ForwardedHeaders: {Count} güvenilen ağ/proxy tanımlandı.", parsed);
        }
    });

    // Register SignalR real-time collaboration services.
    //
    // MaximumReceiveMessageSize varsayılanı (32 KB) UpdateSchema'nın taşıdığı tam
    // şema JSON'ı için yetersiz — orta büyüklükte bir şema (30-40 tablo) bu sınırı
    // kolayca aşar ve mesaj sessizce reddedilir. 512 KB'ye çıkarıyoruz; büyük ama
    // sınırsız değil (DoS koruması hâlâ var).
    var signalRBuilder = builder.Services.AddSignalR(options =>
    {
        options.MaximumReceiveMessageSize = 512 * 1024;
    });

    // Redis backplane: yapılandırılmışsa çok instance'lı dağıtımda grup yayınları
    // (JoinRoom/MoveCursor/UpdateSchema) tüm instance'lara ulaşır. Yapılandırılmamışsa
    // (yerel geliştirme, tek instance) SignalR varsayılan bellek-içi davranışıyla
    // çalışmaya devam eder — davranış değişikliği yok, sadece ölçeklenme sınırı var.
    var redisConnectionStringForSignalR = builder.Configuration["Redis:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(redisConnectionStringForSignalR))
    {
        signalRBuilder.AddStackExchangeRedis(redisConnectionStringForSignalR, options =>
        {
            options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("namines-signalr");
        });
        Log.Information("SignalR Redis backplane etkin — çok instance'lı dağıtımda canlı işbirliği senkron kalır.");
    }
    else
    {
        Log.Warning(
            "Redis:ConnectionString tanımlı değil — SignalR tek instance modunda çalışıyor. " +
            "Yatay ölçeklemede (2+ API instance) canlı işbirliği (Studio çoklu kullanıcı) " +
            "instance'lar arasında senkron kalmaz. Yerel geliştirme ve tek instance dağıtımda sorun değildir.");
    }

    // Setup CORS for Next.js frontend.
    // İzinli origin'ler config'den gelir; localhost yalnızca Production DIŞINDA eklenir.
    // Cors:AllowedOrigins (dizi) veya App:FrontendUrl (tek değer) kullanılabilir.
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
    var frontendUrl = builder.Configuration["App:FrontendUrl"];
    if (!string.IsNullOrWhiteSpace(frontendUrl))
        allowedOrigins = allowedOrigins.Append(frontendUrl).ToArray();
    if (!builder.Environment.IsProduction())
        allowedOrigins = allowedOrigins.Concat(new[] {
            "http://localhost:3000", "http://localhost:3001", "http://localhost:3002",
            "http://localhost:3003", "http://127.0.0.1:3000", "http://127.0.0.1:3002",
            // Namines Desk (services/desk) — AYRI mikroservis, ayrı port.
            // Yalnızca Production DIŞINDA; üretimde Cors:AllowedOrigins'e
            // gerçek alan adı yazılır (bu blok Production'da hiç çalışmaz).
            "http://localhost:3200", "http://127.0.0.1:3200"
        }).ToArray();

    allowedOrigins = allowedOrigins
        .Where(o => !string.IsNullOrWhiteSpace(o))
        .Select(o => o.TrimEnd('/'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (allowedOrigins.Length == 0)
        Log.Warning("CORS izinli origin listesi boş. Production'da App:FrontendUrl veya Cors:AllowedOrigins tanımlayın, aksi halde tarayıcı istekleri bloklanır.");
    else
        Log.Information("CORS izinli origin'ler: {Origins}", string.Join(", ", allowedOrigins));

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowNextJs", policy =>
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .WithExposedHeaders("X-AI-Fallback") // JS'in token-bitti header'ını okuyabilmesi için
                  .AllowCredentials(); // WebSocket ve SignalR handshake desteği için kritik
        });
    });

    // Kestrel bind adresi.
    // Container/PaaS: ASPNETCORE_URLS (ör. http://+:8080) veya PORT ile dışarıdan verilir —
    // burada override ETMEYİZ, yoksa container dışarıdan erişilemez hale gelir.
    // Yerel geliştirme: alışıldık http://localhost:5000 adresine sabitle.
    var urlsFromEnv = builder.Configuration["ASPNETCORE_URLS"];
    var portFromEnv = builder.Configuration["PORT"]; // Railway/Render/Heroku bu değişkeni verir
    if (string.IsNullOrWhiteSpace(urlsFromEnv))
    {
        if (!string.IsNullOrWhiteSpace(portFromEnv))
            builder.WebHost.UseUrls($"http://+:{portFromEnv}");
        else
            builder.WebHost.UseUrls("http://localhost:5000");
    }

    // Setup custom services
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddMemoryCache();
    builder.Services.AddNaminesServices(builder.Configuration);

    // ── HealthChecks ──────────────────────────────────────────────────────────
    // /health      → Tüm check'ler (Kubernetes readiness probe için detaylı)
    // /health/ready → Sadece "critical" tag'li check'ler (DB + AI gateway)
    // /health/live  → Her zaman Healthy döner (Kubernetes liveness probe)
    builder.Services.AddHealthChecks()
        .AddNpgSql(
            connectionString: controlDbConnectionString,
            name: "postgres-control-db",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "db", "critical" })
        .AddCheck("memory", () =>
        {
            var allocated = GC.GetTotalMemory(false);
            const long threshold = 512L * 1024 * 1024; // 512 MB
            return allocated < threshold
                ? HealthCheckResult.Healthy($"Memory: {allocated / 1024 / 1024} MB")
                : HealthCheckResult.Degraded($"Yüksek bellek kullanımı: {allocated / 1024 / 1024} MB");
        }, tags: new[] { "system" });
    // ─────────────────────────────────────────────────────────────────────────

    var app = builder.Build();

    // ── Veritabanı migration ──────────────────────────────────────────────────
    // İki çalışma biçimi:
    //   1) `dotnet run -- --migrate`  → yalnızca migration uygular ve çıkar.
    //      Deploy pipeline'ında ayrı bir adım/Job olarak çalıştırılır. Birden fazla
    //      uygulama instance'ı aynı anda migration denemez.
    //   2) Startup'ta otomatik (Database:MigrateOnStartup, varsayılan true).
    //      Tek instance için pratiktir; yatay ölçeklemede yarış koşulu üretir,
    //      bu yüzden Production'da false yapılıp (1) tercih edilmelidir.
    var migrateOnly = args.Contains("--migrate", StringComparer.OrdinalIgnoreCase);
    var migrateOnStartup = app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true);

    if (migrateOnly || migrateOnStartup)
    {
        if (!migrateOnly && app.Environment.IsProduction())
        {
            Log.Warning(
                "Database:MigrateOnStartup Production'da açık. Birden fazla instance çalıştırıyorsanız " +
                "bunu false yapıp migration'ı ayrı bir adımda `--migrate` ile çalıştırın.");
        }

        using var scope = app.Services.CreateScope();
        try
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            dbContext.Database.Migrate();
            Log.Information("Veritabanı migration başarıyla tamamlandı.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Veritabanı migration hatası. Uygulama başlatma duraksatılıyor.");
            // Migration hatası kritik — uygulamayı devam ettirmek tehlikelidir.
            throw;
        }
    }
    else
    {
        // Migration kapalıysa şemanın gerçekten güncel olduğu DOĞRULANIYOR.
        //
        // Önceden yalnızca bir bilgi satırı yazılıyordu ve uygulama devam
        // ediyordu. Bu, üretimdeki en sinsi arıza biçimini mümkün kılar:
        // deploy'da migration adımı atlanır, uygulama eski şemaya karşı
        // açılır ve hata ancak eksik kolona dokunan İLK istekte — çoğu zaman
        // saatler sonra, rastgele bir kullanıcıda — ortaya çıkar.
        //
        // Development'ta uyarıyla geçiliyor: geliştirici migration'ı henüz
        // uygulamamış olabilir ve uygulamanın hiç açılmaması işi zorlaştırır.
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var pending = dbContext.Database.GetPendingMigrations().ToList();

        if (pending.Count == 0)
        {
            Log.Information("Startup migration atlandı (Database:MigrateOnStartup=false); şema güncel.");
        }
        else if (app.Environment.IsDevelopment())
        {
            Log.Warning(
                "{Count} migration uygulanmamış: {Migrations}. `dotnet run -- --migrate` çalıştırın.",
                pending.Count, string.Join(", ", pending));
        }
        else
        {
            throw new InvalidOperationException(
                $"{pending.Count} migration uygulanmamış ({string.Join(", ", pending)}). " +
                "Uygulama eski bir şemaya karşı açılmayacak — deploy hattında `--migrate` adımını çalıştırın.");
        }
    }

    if (migrateOnly)
    {
        Log.Information("--migrate modu: migration tamamlandı, uygulama başlatılmadan çıkılıyor.");
        return 0;
    }

    // Sahip/geliştirici hesabı .env'den tohumlanıyor. Migration'dan SONRA olmalı:
    // IsDev kolonu henüz yokken kullanıcı yazmaya çalışmak açılışı kırardı.
    //
    // Hata FIRLATMIYOR: sahip hesabı bir kolaylık, uygulamanın çalışma şartı
    // değil — tohumlama takılırsa uygulamanın tamamen açılmaması orantısız olurdu.
    using (var ownerScope = app.Services.CreateScope())
    {
        try
        {
            await Namines.API.Services.DevAccountSeeder.SeedAsync(
                ownerScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                app.Configuration,
                ownerScope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(nameof(Namines.API.Services.DevAccountSeeder)));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sahip hesabı tohumlanamadı; uygulama sahip hesabı olmadan devam ediyor.");
        }

        // Canlı bir veritabanına bağlı hazır deneme projesi — YALNIZCA Development.
        //
        // Desk'in, Gateway'in ve Vault'un çekirdek akışları canlı bağlantı
        // olmadan denenemiyor; onu her temiz kurulumda elle kurmak unutulmaya
        // açık, tekrarlayan bir işti. Üretimde kendiliğinden veritabanı
        // yaratmak ise kabul edilemez — bu yüzden ortam kapısı burada.
        if (app.Environment.IsDevelopment())
        {
            try
            {
                await Namines.API.Services.DevSandboxSeeder.SeedAsync(
                    ownerScope.ServiceProvider.GetRequiredService<AuthDbContext>(),
                    app.Configuration,
                    ownerScope.ServiceProvider.GetRequiredService<IConnectionSecretProtector>(),
                    ownerScope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                        .CreateLogger(nameof(Namines.API.Services.DevSandboxSeeder)));
            }
            catch (Exception ex)
            {
                // Dev hesabıyla aynı ilke: bir kolaylık, çalışma şartı değil.
                Log.Error(ex, "Deneme projesi tohumlanamadı; uygulama onsuz devam ediyor.");
            }
        }
    }

    // X-Forwarded-* header'larını en başta işle: sonraki tüm middleware'ler (cookie Secure
    // bayrağı, rate limit partition'ı, redirect URL'leri) doğru şema/IP görsün.
    app.UseForwardedHeaders();

    // Use global exception handler (üretimde stack trace sızdırmaz)
    app.UseMiddleware<ExceptionMiddleware>();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseWebSockets();
    app.UseCors("AllowNextJs");

    // CORS'tan SONRA: preflight (OPTIONS) isteklerinin CORS tarafından
    // cevaplanması gerekiyor; ondan önce çalışsaydı tarayıcı preflight'ı
    // reddedilmiş sanardı. Authentication'dan ÖNCE: cookie'nin varlığı yeterli,
    // jetonun çözülmesini beklemeye gerek yok.
    app.UseMiddleware<CsrfProtectionMiddleware>();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    app.UseMiddleware<AIQuotaMiddleware>();

    app.UseNaminesObservability();
    app.MapControllers();
    app.MapHub<Namines.API.Hubs.CanvasHub>("/hubs/canvas");

    // ── HealthCheck Endpoint'leri ─────────────────────────────────────────────
    // /health       → UI-formatted tam rapor (geliştirici / monitoring dashboard)
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        Predicate = _ => true,
    });

    // /health/ready → Sadece kritik bağımlılıklar (DB). Kubernetes readiness probe'u buraya bakar.
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        Predicate = check => check.Tags.Contains("critical"),
    });

    // /health/live  → Her zaman 200 döner. Kubernetes liveness probe'u buraya bakar.
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false, // Hiçbir check çalıştırma — sadece "uygulama ayakta" demek yeterli
    });
    // ─────────────────────────────────────────────────────────────────────────

    Log.Information("Namines API hazır. http://localhost:5000 adresinde dinleniyor.");
    app.Run();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Namines API başlatma sırasında kritik hata oluştu.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
