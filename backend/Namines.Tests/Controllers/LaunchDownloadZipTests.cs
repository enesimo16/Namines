using System.IO;
using System.IO.Compression;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// AppendGatewayReadme'nin sözleşmesi: mevcut zip içeriği KORUNUR, tek bir
/// yeni kök dosya eklenir, ve o dosya ham bir DB parolası DEĞİL bir Gateway
/// anahtarı taşır.
/// </summary>
public class LaunchDownloadZipTests
{
    private static byte[] BuildMinimalZip(string entryName, string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
        return stream.ToArray();
    }

    [Fact]
    public void Appends_gateway_readme_without_losing_existing_entries()
    {
        var original = BuildMinimalZip("backend/Program.cs", "// existing scaffolded file");

        var method = typeof(Namines.API.Controllers.LaunchController).GetMethod(
            "AppendGatewayReadme",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var result = (byte[])method.Invoke(null, new object[]
        {
            original, "http://localhost:5000/api/gateway", "ngw_test_raw_key_value",
        })!;

        using var stream = new MemoryStream(result);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("backend/Program.cs"));

        var readme = archive.GetEntry("NAMINES-GATEWAY.md");
        Assert.NotNull(readme);
        using var reader = new StreamReader(readme!.Open());
        var text = reader.ReadToEnd();

        Assert.Contains("ngw_test_raw_key_value", text);
        Assert.Contains("NAMINES_GATEWAY_URL=http://localhost:5000/api/gateway", text);
        Assert.DoesNotContain("Password=", text);
    }
}
