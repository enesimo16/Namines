using System.Diagnostics;

namespace Namines.Tests.RunTests;

/// <summary>Namines.Tests/Integration/DockerAvailable.cs'in bu izole projedeki küçük kopyası —
/// bkz. Namines.Tests.RunTests.csproj'daki izolasyon gerekçesi.</summary>
public sealed class RequiresDockerFactAttribute : FactAttribute
{
    public RequiresDockerFactAttribute()
    {
        if (!DockerAvailable.Value)
            Skip = "Docker çalışmıyor — integration testi atlandı.";
    }
}

internal static class DockerAvailable
{
    private static readonly Lazy<bool> Lazy = new(Probe);
    public static bool Value => Lazy.Value;

    private static bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info --format {{.ServerVersion}}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null) return false;
            if (!process.WaitForExit(10_000)) { process.Kill(true); return false; }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
