using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Docker.DotNet;
using Docker.DotNet.Models;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.Services;

public class DockerSweeperBackgroundService : BackgroundService
{
    /// <summary>
    /// Bir container'ın zombi sayılması için gereken en az yaş.
    /// Aktif iş kaydı zaten canlı işleri koruyor; bu süre yalnızca kayıt dışı kalmış
    /// (ör. uygulama yeniden başlamadan önce oluşturulmuş) container'lar için geçerli.
    /// </summary>
    private static readonly TimeSpan ZombieAge = TimeSpan.FromMinutes(30);

    private const string SandboxNamePrefix = "namines-sandbox-";

    private readonly ILogger<DockerSweeperBackgroundService> _logger;
    private readonly ISandboxJobRegistry _jobRegistry;
    private readonly DockerClient _client;
    private readonly bool _enabled;

    public DockerSweeperBackgroundService(
        ILogger<DockerSweeperBackgroundService> logger,
        ISandboxJobRegistry jobRegistry,
        IConfiguration configuration)
    {
        _logger = logger;
        _jobRegistry = jobRegistry;

        // Sandbox özelliği artık host'un docker socket'ine bağlı DEĞİL — compose'da
        // socket mount'u kaldırıldı (host'ta root eşdeğeri yetki veriyordu).
        // Bu servis yalnızca yerel geliştirmede, açıkça etkinleştirildiğinde çalışır.
        // Kalıcı çözüm: ayrı provisioning broker'ı (bkz. new-phase/06-DATA-PLANE.md).
        _enabled = configuration.GetValue("Sandbox:Enabled", defaultValue: false);

        var dockerUri = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock";

        _client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
    }

    public override void Dispose()
    {
        _client?.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation(
                "Docker sandbox sweeper devre dışı (Sandbox:Enabled=false). Host docker socket'i " +
                "artık mount edilmiyor — yerel geliştirmede kullanmak için Sandbox__Enabled=true verin.");
            return;
        }

        _logger.LogInformation("Docker Sweeper Background Service is starting.");

        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepZombieContainersAsync(stoppingToken);
                consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;

                // Docker erişilemiyorsa her 5 dakikada bir hata basmak yerine kendini kapat.
                // Aksi halde socket'i olmayan bir ortamda log ve alert gürültüsü üretir.
                if (consecutiveFailures >= 3)
                {
                    _logger.LogWarning(ex,
                        "Docker'a {Count} kez üst üste erişilemedi — sweeper kapatılıyor. " +
                        "Docker çalışmıyorsa bu beklenen durumdur.", consecutiveFailures);
                    return;
                }

                _logger.LogError(ex, "An error occurred while sweeping Docker containers.");
            }

            // Run every 5 minutes
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }

        _logger.LogInformation("Docker Sweeper Background Service is stopping.");
    }

    private async Task SweepZombieContainersAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Scanning for zombie Namines sandbox containers...");

        var parameters = new ContainersListParameters
        {
            All = true
        };

        var containers = await _client.Containers.ListContainersAsync(parameters, cancellationToken);
        var now = DateTime.UtcNow;

        // Hâlâ süren işler: container adı 'namines-sandbox-{jobId}' olduğundan jobId ile eşleşir.
        // Bu kontrol olmadan sweeper, yavaş bir host'ta hâlâ imaj çeken veya backup alan
        // canlı bir sandbox'ı silip kullanıcıya anlamsız bir Docker hatası döndürür.
        var activeJobIds = _jobRegistry.GetActiveJobIds();

        foreach (var container in containers)
        {
            // Check if name matches our prefix (Docker names usually start with / in ListContainers)
            var sandboxName = container.Names?.FirstOrDefault(name =>
                name.TrimStart('/').StartsWith(SandboxNamePrefix, StringComparison.OrdinalIgnoreCase));

            if (sandboxName == null)
                continue;

            var jobId = sandboxName.TrimStart('/').Substring(SandboxNamePrefix.Length);
            if (activeJobIds.Contains(jobId, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Container {ContainerName} atlanıyor — job {JobId} hâlâ çalışıyor.", sandboxName, jobId);
                continue;
            }

            var age = now - container.Created;
            if (age >= ZombieAge)
            {
                _logger.LogWarning("Found zombie container {ContainerId} (Name: {ContainerName}, Age: {Age:F1}m, Status: {Status}). Attempting cleanup...", 
                    container.ID.Substring(0, 12), 
                    container.Names?.FirstOrDefault() ?? "Unknown", 
                    age.TotalMinutes, 
                    container.Status);

                try
                {
                    // Stop container if it is running
                    if (container.State.Equals("running", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Stopping container {ContainerId}...", container.ID.Substring(0, 12));
                        await _client.Containers.StopContainerAsync(container.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 5 }, cancellationToken);
                    }

                    // Remove container
                    _logger.LogInformation("Removing container {ContainerId}...", container.ID.Substring(0, 12));
                    await _client.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true }, cancellationToken);
                    
                    _logger.LogInformation("Successfully removed zombie container {ContainerId}.", container.ID.Substring(0, 12));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to clean up zombie container {ContainerId}.", container.ID.Substring(0, 12));
                }
            }
        }
    }
}
