using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Namines.API.Extensions;
using Namines.API.Middleware;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Serilog;
using Serilog.Events;
using System.Text;
using System.Threading.RateLimiting;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.HttpOverrides;

// ── Serilog Bootstrap (before WebApplication.CreateBuilder) ─────────────────
// İki sink: Console (human-readable) ve günlük dönen dosya (logs/).
// Seq şimdilik devre dışı — ileride Seq:Url config key'i eklendiğinde aktive edilir.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/namines-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

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
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/namines-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");
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
    builder.Services.AddDbContext<AuthDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=namines_auth.db"));

    // Configure Identity Core
    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
    })
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders();

    // Configure JWT Authentication
    var secretKey = builder.Configuration["Jwt:Key"];
    if (string.IsNullOrWhiteSpace(secretKey))
    {
        // Production'da fail-closed: sabit fallback key ile JWT sahteciliğini engelle.
        if (builder.Environment.IsProduction())
            throw new InvalidOperationException("Jwt:Key production ortamında zorunludur (env var JWT__KEY veya appsettings.secrets.json).");

        secretKey = "NaminesDevFallbackKey_Change_In_Production_Min32Chars!";
        Log.Warning("Jwt:Key tanımlanmamış — geliştirme fallback key'i kullanılıyor. Production'da mutlaka ortam değişkeni ile override edin.");
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
        };
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
    });

    // Reverse proxy (Nginx / Railway / Render / Fly) arkasında TLS proxy'de sonlanır ve
    // uygulamaya düz HTTP gelir. Bu header'lar işlenmezse Request.IsHttps=false olur
    // (auth cookie'sinin Secure bayrağı düşer) ve RemoteIpAddress proxy'yi gösterir
    // (rate limit tüm kullanıcıları tek partition'a toplar).
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Proxy IP'leri PaaS'te dinamiktir; bilinen ağ kısıtını kaldırıyoruz.
        // Güvenlik notu: uygulama yalnızca güvenilen bir proxy arkasında yayınlanmalıdır.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // Register SignalR real-time collaboration services
    builder.Services.AddSignalR();

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
            "http://localhost:3003", "http://127.0.0.1:3000", "http://127.0.0.1:3002"
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
    builder.Services.AddNaminesServices();

    // ── HealthChecks ──────────────────────────────────────────────────────────
    // /health      → Tüm check'ler (Kubernetes readiness probe için detaylı)
    // /health/ready → Sadece "critical" tag'li check'ler (DB + AI gateway)
    // /health/live  → Her zaman Healthy döner (Kubernetes liveness probe)
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=namines_auth.db";
    builder.Services.AddHealthChecks()
        .AddSqlite(
            connectionString: connectionString,
            name: "sqlite-auth-db",
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

    // Auto-create/migrate database on startup
    using (var scope = app.Services.CreateScope())
    {
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

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    app.UseMiddleware<BYOKMiddleware>();
    app.UseMiddleware<AIQuotaMiddleware>();

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
