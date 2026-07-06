using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
using HealthChecks.UI.Client;

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

    // ── Load appsettings.secrets.json (git-ignored local secrets) ──────────────
    // This file contains Jwt:Key, Groq:ApiKey etc. — NEVER commit it.
    builder.Configuration
        .AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: false);
    // Environment variables override everything (for Docker / cloud deployments)
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
        options.Password.RequiredLength = 4;
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
        // Development fallback — will be overridden by appsettings.secrets.json or env var JWT__KEY
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

    // Register SignalR real-time collaboration services
    builder.Services.AddSignalR();

    // Setup CORS for Next.js frontend
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowNextJs", policy =>
        {
            policy.WithOrigins("http://localhost:3000", "http://localhost:3001", "http://127.0.0.1:3000")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials(); // WebSocket ve SignalR handshake desteği için kritik
        });
    });

    // Force Kestrel to listen on port 5000
    builder.WebHost.UseUrls("http://localhost:5000");

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
