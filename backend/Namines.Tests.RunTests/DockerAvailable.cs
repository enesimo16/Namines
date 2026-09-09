using System.Diagnostics;
using System.Globalization;
using Namines.Core.Enums;
using Namines.Core.Models;

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

/// <summary>
/// Docker VAR ama bu motoru barındıramıyorsa testi atlar.
///
/// <b>Neden gerekli:</b> SQL Server 2022 açılışta makinenin belleğine bakıyor
/// ve 2000 MB'ın altındaysa kendini kapatıyor. Küçük belleğe ayarlanmış bir
/// Docker Desktop'ta bu testler KIRMIZI yanıyordu — oysa kodda bir hata yok,
/// makine o motoru çalıştıramıyor. Ölçüldü: VM 1.9 GB, konteynere 4 GB limit
/// vermek de kurtarmıyor.
///
/// <c>DockerAvailable.cs</c>'in kendi yorumundaki gerekçe birebir geçerli:
/// çevresel bir kısıt için paketi kırmızıya çevirmek "testler zaten kırmızı"
/// alışkanlığı yaratır ve GERÇEK hataları görünmez kılar.
///
/// CI'da bu atlama bir uyarıdır, kabul değil — orada Docker'a yeterli bellek
/// verilmelidir.
/// </summary>
public sealed class RequiresEngineFactAttribute : FactAttribute
{
    public RequiresEngineFactAttribute(DatabaseType engine)
    {
        if (!DockerAvailable.Value)
        {
            Skip = "Docker çalışmıyor — integration testi atlandı.";
            return;
        }

        var profile = ContainerProfiles.GetProfile(engine);
        if (profile.MinimumMemoryBytes <= 0) return;

        var available = DockerHostMemory.Bytes;
        // 0 = ölçülemedi. Ölçemediğimiz için atlamak, çalışabilecek bir testi
        // sessizce kaçırmak olurdu; şüphede test KOŞAR.
        if (available > 0 && available < profile.MinimumMemoryBytes)
        {
            const long megabyte = 1024 * 1024;
            Skip = $"{engine} en az {profile.MinimumMemoryBytes / megabyte} MB bellek istiyor, " +
                   $"Docker sunucusunda {available / megabyte} MB var — test atlandı.";
        }
    }
}

/// <summary>Docker sunucusunun toplam belleği (bayt). Ölçülemezse 0.</summary>
internal static class DockerHostMemory
{
    private static readonly Lazy<long> Lazy = new(Probe);
    public static long Bytes => Lazy.Value;

    private static long Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info --format {{.MemTotal}}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null) return 0;
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(10_000)) { process.Kill(true); return 0; }
            if (process.ExitCode != 0) return 0;

            return long.TryParse(output.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes)
                ? bytes
                : 0;
        }
        catch
        {
            return 0;
        }
    }
}

internal static class DockerAvailable
{
    private static readonly Lazy<bool> Lazy = new(Probe);
    public static bool Value => Lazy.Value;

    /// <summary>
    /// <c>docker info</c> icin bekleme suresi.
    ///
    /// <b>10 saniye YETMIYORDU ve bedeli agirdi:</b> derleme hemen oncesinde
    /// kostugu icin makine yuklu oluyor, <c>docker info</c> zaman asimina
    /// ugruyor, prob "Docker yok" diyor ve BUTUN integration testleri
    /// (127 adet) sessizce atlaniyordu -- suite yine "Basarili!" yaziyordu.
    /// Yesil ama hicbir sey kanitlamayan bir kosu, kirmizi bir kosudan daha
    /// tehlikeli.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Gecici bir aksaklik butun paketi kor birakmasin diye deneme sayisi.</summary>
    private const int ProbeAttempts = 2;

    private static bool Probe()
    {
        for (var attempt = 1; attempt <= ProbeAttempts; attempt++)
        {
            if (ProbeOnce()) return true;
        }
        return false;
    }

    private static bool ProbeOnce()
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
            if (!process.WaitForExit((int)ProbeTimeout.TotalMilliseconds)) { process.Kill(true); return false; }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
