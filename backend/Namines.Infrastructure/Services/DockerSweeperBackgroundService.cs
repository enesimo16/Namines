using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Namines.Infrastructure.Services;

public class DockerSweeperBackgroundService : BackgroundService
{
    private readonly ILogger<DockerSweeperBackgroundService> _logger;
    private readonly DockerClient _client;

    public DockerSweeperBackgroundService(ILogger<DockerSweeperBackgroundService> logger)
    {
        _logger = logger;
        
        var dockerUri = Environment.OSVersion.Platform == PlatformID.Win32NT 
            ? "npipe://./pipe/docker_engine" 
            : "unix:///var/run/docker.sock";
            
        _client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Docker Sweeper Background Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepZombieContainersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
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

        foreach (var container in containers)
        {
            // Check if name matches our prefix (Docker names usually start with / in ListContainers)
            bool hasSandboxName = container.Names != null && container.Names.Any(name => 
                name.StartsWith("/namines-sandbox-", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("namines-sandbox-", StringComparison.OrdinalIgnoreCase));

            if (!hasSandboxName)
                continue;

            // Check if it has been running / created for more than 5 minutes
            var age = now - container.Created;
            if (age.TotalMinutes >= 5)
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
