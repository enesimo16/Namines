using System.Diagnostics;
using System.Globalization;
using Namines.Core.Enums;
using Namines.Core.Models;

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

/// <summary>
/// Docker VAR ama bu motoru barındıramıyorsa testi atlar.
///
/// <b>Neden gerekli:</b> SQL Server 2022 açılışta makinenin belleğine bakıyor
/// ve 2000 MB'ın altındaysa kendini kapatıyor. Küçük belleğe ayarlanmış bir
/// Docker Desktop'ta bu testler KIRMIZI yanıyordu (ölçüldü: VM 1.9 GB;
/// konteynere 4 GB limit vermek de kurtarmıyor, çünkü sınır VM'in kendisinde).
///
/// Yukarıdaki <see cref="RequiresDockerFactAttribute"/> ile aynı gerekçe:
/// çevresel bir kısıt için paketi kırmızıya çevirmek "testler zaten kırmızı"
/// alışkanlığı yaratır ve GERÇEK hataları görünmez kılar.
///
/// CI'da bu atlama bir uyarıdır, kabul değil.
/// </summary>
public sealed class RequiresEngineTheoryAttribute : TheoryAttribute
{
    public RequiresEngineTheoryAttribute(DatabaseType engine)
    {
        Skip = EngineAvailable.SkipReason(engine);
    }
}

/// <inheritdoc cref="RequiresEngineTheoryAttribute"/>
public sealed class RequiresEngineFactAttribute : FactAttribute
{
    public RequiresEngineFactAttribute(DatabaseType engine)
    {
        Skip = EngineAvailable.SkipReason(engine);
    }
}

/// <summary>
/// Bir motorun bu makinede çalıştırılabilir olup olmadığı.
///
/// <b>Ayrı bir sınıf, çünkü iki yerden sorulması gerekiyor:</b> testi atlayan
/// öznitelik ve konteyneri BAŞLATAN <c>IAsyncLifetime.InitializeAsync</c>.
/// İkincisi olmadan konteyner atlama kararından önce ayağa kaldırılmaya
/// çalışılır ve fixture hatası bütün teoriyi kırmızıya çevirir — bu sınıf
/// yazılmadan önce tam olarak bu oluyordu.
/// </summary>
public static class EngineAvailable
{
    /// <summary>Motor çalıştırılabiliyorsa true.</summary>
    public static bool For(DatabaseType engine) => SkipReason(engine) is null;

    /// <summary>Atlama gerekçesi; motor çalıştırılabiliyorsa null.</summary>
    public static string? SkipReason(DatabaseType engine)
    {
        if (!DockerAvailable.Value) return "Docker çalışmıyor — integration testi atlandı.";

        var profile = ContainerProfiles.GetProfile(engine);
        if (profile.MinimumMemoryBytes <= 0) return null;

        var available = DockerHostMemory.Bytes;
        // 0 = ölçülemedi. Şüphede test KOŞAR: ölçemediğimiz için atlamak,
        // çalışabilecek bir testi sessizce kaçırmak olurdu.
        if (available <= 0 || available >= profile.MinimumMemoryBytes) return null;

        const long megabyte = 1024 * 1024;
        return $"{engine} en az {profile.MinimumMemoryBytes / megabyte} MB bellek istiyor, " +
               $"Docker sunucusunda {available / megabyte} MB var — test atlandı.";
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
