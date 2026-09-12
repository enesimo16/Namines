using System.IO.Compression;
using System.Text;
using Namines.Core.Models;
using Namines.Infrastructure.Services;
using Namines.Tests.Fixtures;
using static VerifyXunit.Verifier;

namespace Namines.Tests.Generators;

/// <summary>
/// <see cref="ScaffolderService"/> çıktısının karakterizasyon (snapshot) testi
/// (ARCH-003 / B-37).
///
/// <b>Neden yazıldı:</b> Servis 1.760 satırdı ve bölünecekti — ama çıktısını
/// kapsayan TEK bir test yoktu. Test olmadan yapılan bir bölme, üretilen
/// projelerden birinde bir satırın kaybolmasını ancak bir kullanıcının
/// indirdiği projenin derlenmemesiyle fark ettirirdi.
///
/// Snapshot, zip'in İÇİNDEKİ her dosyanın yolunu ve içeriğini tek bir metin
/// dosyasında tutuyor. Zip baytları değil içerik karşılaştırılıyor: zip
/// başlıklarındaki zaman damgaları her üretimde değişir ve gürültü olur.
///
/// <b>Bu snapshot'lar "doğru" çıktıyı değil, BUGÜNKÜ çıktıyı temsil eder</b>
/// (<c>DdlGoldenTests</c> ile aynı ilke). Amaç, yeniden düzenlemenin davranışı
/// değiştirmediğini kanıtlamak.
/// </summary>
public class ScaffolderSnapshotTests
{
    public static TheoryData<string> Variants() => new()
    {
        "dotnet-plain",
        "dotnet-bi-aws",
        "dotnet-azure",
        "python",
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task Scaffolder_output_matches_snapshot(string variant)
    {
        var service = new ScaffolderService();
        var schema = SchemaFixtures.ECommerce();

        byte[] zip;
        switch (variant)
        {
            case "dotnet-plain":
                zip = await service.GenerateFullStackProjectAsync(schema);
                break;
            case "dotnet-bi-aws":
                schema.IncludeBiModule = true;
                schema.CloudProvider = "AWS";
                zip = await service.GenerateFullStackProjectAsync(schema);
                break;
            case "dotnet-azure":
                schema.CloudProvider = "Azure";
                zip = await service.GenerateFullStackProjectAsync(schema);
                break;
            case "python":
                zip = await service.GeneratePythonFreemiumProjectAsync(schema);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(variant), variant, null);
        }

        var settings = new VerifySettings();
        settings.UseDirectory(Path.Combine("..", "Golden", "Scaffolder"));
        settings.UseFileName(variant);
        settings.DisableDiff();

        await Verify(new Target("txt", Flatten(zip)), settings);
    }

    /// <summary>
    /// Zip'i tek bir okunabilir metne çevirir: dosyalar YOL SIRASINA göre,
    /// her birinin başında yolu. Sıra zip'teki ekleme sırasına bırakılmıyor —
    /// bir yeniden düzenleme dosyaları farklı sırada ekleyebilir ve bu
    /// davranış değişikliği DEĞİLDİR.
    /// </summary>
    internal static string Flatten(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var sb = new StringBuilder();

        foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            sb.Append("=== ").Append(entry.FullName).Append(" ===").Append('\n');
            sb.Append(reader.ReadToEnd().Replace("\r\n", "\n")).Append('\n');
        }

        return sb.ToString();
    }
}
