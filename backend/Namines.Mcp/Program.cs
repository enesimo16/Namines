using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.Core.Interfaces;
using Namines.Core.Security;
using Namines.Flow.Abstractions;
using Namines.Flow.Services;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Generators.Eject;
using Namines.Infrastructure.Generators.PrismaGenerator;
using Namines.Infrastructure.Security;
using Namines.Infrastructure.Services;
using Namines.Mcp;

// ─────────────────────────────────────────────────────────────────────────────
// Namines Universal Database Lifecycle MCP Server
//
// stdio üzerinden konuşur: Claude Code / Cursor / Zed / Antigravity gibi
// istemciler bu süreci başlatır ve stdin/stdout ile JSON-RPC konuşur.
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

// Bağlantı dizesi modelden geldiği için hedef allowlist kapısı
builder.Services.AddSingleton<Namines.Mcp.McpConnectionGuard>();

// Çekirdek şema ve DDL servisleri
builder.Services.AddSingleton<IDbIntrospectionService, DbIntrospectionService>();
builder.Services.AddSingleton<IDdlGeneratorFactory, DdlGeneratorFactory>();
builder.Services.AddSingleton<IBranchTestRunner, BranchTestRunnerService>();
builder.Services.AddSingleton<IPrismaGenerator, PrismaGeneratorService>();

// Kod üretimi ve Eject hedefleri (19 hedef)
builder.Services.AddSingleton<IEjectGeneratorRegistry, EjectGeneratorRegistry>();

// Gateway ve Desk veri erişimi
builder.Services.AddSingleton<IGatewayService, GatewayService>();
builder.Services.AddSingleton<IDatabaseExecutor, DatabaseExecutorService>();

// Namines Flow kural eşleme motoru
builder.Services.AddSingleton<IFlowRuleMatcher, FlowRuleMatcher>();

// Bulut ve sunucu iletişimi
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
    /// geliştiricinin kendi makinesinde çalışan bir geliştirme aracıdır.
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
