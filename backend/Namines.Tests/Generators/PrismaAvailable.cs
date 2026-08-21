using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Namines.Tests.Generators;

/// <summary>
/// Prisma CLI yoksa doğrulama testleri ATLANIR, kırmızı olmaz —
/// <see cref="Namines.Tests.Integration.RequiresDockerFactAttribute"/> ile aynı gerekçe.
///
/// Neden gerçek CLI'a doğrulatıyoruz: G5'in dersi ("çalışıyor görünüyor" ≠ "gerçekten
/// çalışıyor"). Üretilen <c>schema.prisma</c>'nın metin olarak makul görünmesi, Prisma'nın
/// onu KABUL ETTİĞİ anlamına gelmez. Bu üreticinin en kırılgan yeri de tam olarak
/// ayrıştırıcının kabul ettiği ince kurallar: aynı iki model arasında birden fazla
/// ilişki varsa adlandırılmış ilişki ZORUNLUDUR, SQLite native tip niteleyicisi
/// KABUL ETMEZ. İkisi de golden-file testinden geçer ama gerçek Prisma'da patlar.
/// </summary>
public sealed class RequiresPrismaFactAttribute : FactAttribute
{
    public RequiresPrismaFactAttribute()
    {
        if (!PrismaAvailable.Value)
            Skip = "Prisma CLI (npx) bulunamadı — doğrulama testi atlandı.";
    }
}

public sealed class RequiresPrismaTheoryAttribute : TheoryAttribute
{
    public RequiresPrismaTheoryAttribute()
    {
        if (!PrismaAvailable.Value)
            Skip = "Prisma CLI (npx) bulunamadı — doğrulama testi atlandı.";
    }
}

internal static class PrismaAvailable
{
    private static readonly Lazy<bool> Lazy = new(Probe);
    public static bool Value => Lazy.Value;

    private static bool Probe()
    {
        try
        {
            return Run("--version", TimeSpan.FromMinutes(3)).exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Bir <c>schema.prisma</c> dosyasını gerçek Prisma ayrıştırıcısına doğrulatır.
    /// Dönen stderr+stdout, hata hâlinde OLDUĞU GİBİ teste aktarılır — motorun ham
    /// mesajını özetlemek G5/G12'de bilerek yasaklandı.
    /// </summary>
    public static (int exitCode, string output) Validate(string schemaContent)
    {
        var dir = Path.Combine(Path.GetTempPath(), "namines-prisma-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "schema.prisma");
            File.WriteAllText(path, schemaContent);

            // `prisma validate` yalnızca sözdizimini değil datasource'u da çözer:
            // DATABASE_URL yoksa şema kusursuz olsa bile P1012 ile düşer. Şemanın
            // ilan ettiği provider'a UYAN bir sahte URL veriyoruz — şeması yanlış
            // olan bir URL de reddedilirdi, yani bu adım kontrolü zayıflatmıyor.
            return Run($"validate --schema \"{path}\"", TimeSpan.FromMinutes(3),
                DummyUrlFor(ProviderOf(schemaContent)));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* geçici dizin */ }
        }
    }

    /// <summary>datasource bloğundaki provider. generator bloğununkiyle karışmasın
    /// diye yalnızca bilinen datasource provider adları kabul edilir.</summary>
    private static string ProviderOf(string schemaContent)
    {
        foreach (Match match in Regex.Matches(schemaContent, @"provider\s*=\s*""([a-z]+)"""))
        {
            var value = match.Groups[1].Value;
            if (value is "postgresql" or "mysql" or "sqlserver" or "sqlite" or "cockroachdb")
                return value;
        }
        return "postgresql";
    }

    private static string DummyUrlFor(string provider) => provider switch
    {
        "mysql" => "mysql://user:pass@localhost:3306/db",
        "sqlserver" => "sqlserver://localhost:1433;database=db;user=sa;password=pass;encrypt=true",
        "sqlite" => "file:./dev.db",
        _ => "postgresql://user:pass@localhost:5432/db?schema=public",
    };

    private static (int exitCode, string output) Run(
        string arguments, TimeSpan timeout, string? databaseUrl = null)
    {
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            // Windows'ta npx bir .cmd'dir ve doğrudan başlatılamaz.
            FileName = isWindows ? "cmd.exe" : "npx",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (isWindows)
        {
            psi.Arguments = $"/c npx --yes prisma@5 {arguments}";
        }
        else
        {
            foreach (var part in new[] { "--yes", "prisma@5" }) psi.ArgumentList.Add(part);
            foreach (var part in SplitArguments(arguments)) psi.ArgumentList.Add(part);
        }

        if (databaseUrl is not null) psi.Environment["DATABASE_URL"] = databaseUrl;

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* zaten bitmiş olabilir */ }
            return (-1, "timeout");
        }

        return (process.ExitCode, stdout + stderr);
    }

    /// <summary>Yalnızca test argümanları için — tırnaklı tek bir yol parçası taşır.</summary>
    private static IEnumerable<string> SplitArguments(string arguments)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in arguments)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }
}
