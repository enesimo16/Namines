using System.Diagnostics;

namespace Namines.Tests.Integration;

/// <summary>
/// Docker çalışmıyorsa integration testleri sessizce ATLANIR, KIRMIZI OLMAZ.
///
/// Gerekçe: bu testler gerçek veritabanı container'ları başlatır. Docker'ı olmayan
/// bir geliştiricide (veya Docker'sız bir CI adımında) tüm paketi kırmızıya çevirmek,
/// "testler zaten kırmızı" alışkanlığı yaratır ve gerçek hataları görünmez kılar.
///
/// CI'da Docker HER ZAMAN olmalıdır — orada atlanan test bir uyarıdır, kabul değil.
/// </summary>
public sealed class RequiresDockerFactAttribute : FactAttribute
{
    public RequiresDockerFactAttribute()
    {
        if (!DockerAvailable.Value)
            Skip = "Docker çalışmıyor — integration testi atlandı.";
    }
}

public sealed class RequiresDockerTheoryAttribute : TheoryAttribute
{
    public RequiresDockerTheoryAttribute()
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
