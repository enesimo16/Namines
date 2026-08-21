using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.Core.Interfaces;
using Namines.Core.Security;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Generators.PrismaGenerator;
using Namines.Infrastructure.Security;
using Namines.Infrastructure.Services;
using Namines.Mcp;

// ─────────────────────────────────────────────────────────────────────────────
// Namines MCP sunucusu — new-phase/33-MCP-AND-SKILL.md
//
// stdio üzerinden konuşur: Claude Code / Cursor / Zed gibi istemciler bu süreci
// başlatır ve stdin/stdout ile JSON-RPC konuşur.
//
// KRİTİK: stdout PROTOKOL KANALIDIR. Oraya yazılan her serbest metin JSON-RPC
// akışını bozar ve sunucu istemciye kırık görünür. Bu yüzden log'lar stderr'e
// yönlendirilir — Console.WriteLine ASLA kullanılmaz.
// ─────────────────────────────────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// ── SSRF politikası ──────────────────────────────────────────────────────────
// Barındırılan API'de SsrfGuard özel/ayrılmış adresleri reddeder; orada bu doğru,
// çünkü sunucu müşteri ağına sızmamalı. BURADA durum farklı: süreç kullanıcının
// KENDİ makinesinde, kendi DB'sine bakıyor — localhost tam olarak hedeflenen
// kullanım (33 §2). Bu yüzden MCP bağlamında gevşetme varsayılan.
// Yine de kapatılabilir olsun diye yapılandırmaya bağlı bırakıldı.
builder.Configuration["Security:AllowPrivateDbHosts"] ??= "true";
builder.Services.AddSingleton<IDbHostAccessPolicy>(sp =>
    new DbHostAccessPolicy(
        new McpHostEnvironment(),   // her zaman "Development" — bkz. sınıf yorumu
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<ILogger<DbHostAccessPolicy>>()));

builder.Services.AddSingleton<IDbIntrospectionService, DbIntrospectionService>();
builder.Services.AddSingleton<IDdlGeneratorFactory, DdlGeneratorFactory>();
builder.Services.AddSingleton<IBranchTestRunner, BranchTestRunnerService>();
builder.Services.AddSingleton<IPrismaGenerator, PrismaGeneratorService>();

// Faz 2: barındırılan sunucuya CR açmak için. Token yoksa YİNE kaydedilir —
// eksik yapılandırma çağrı anında açık bir mesajla bildirilir, sunucunun geri
// kalan üç aracı da başlatılamaz hâle gelmez (üçü tamamen çevrimdışı çalışır).
builder.Services.AddHttpClient<NaminesCloudClient>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<NaminesTools>();

await builder.Build().RunAsync();

namespace Namines.Mcp
{
    /// <summary>
    /// <see cref="DbHostAccessPolicy"/> bir <c>IHostEnvironment</c> bekliyor ve
    /// gevşetmeyi yalnızca Development'ta uyguluyor. MCP sunucusu tanım gereği
    /// geliştiricinin kendi makinesinde çalışan bir geliştirme aracıdır — burada
    /// "Production" diye bir dağıtım yok. Politikanın barındırılan API'deki çift
    /// kapısını zayıflatmamak için o sınıfa dokunmak yerine bağlamı burada
    /// açıkça beyan ediyoruz.
    /// </summary>
    internal sealed class McpHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Namines.Mcp";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
