using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Namines.Tests.Security;

/// <summary>
/// Migration'lar İLERİYE UYUMLU olmak zorunda (DEVOPS-005 / B-47).
///
/// <b>Çözdüğü sorun:</b> Geri alma (rollback) kodu eski sürüme döndürür,
/// <b>veritabanını döndürmez</b>. Yani geri alındıktan sonra ESKİ kod YENİ
/// şemaya karşı çalışmak zorunda kalır. Bir migration bunu imkânsız kılıyorsa
/// geri alma diye bir şey kalmaz — bozuk bir dağıtım, düzeltme yazılana kadar
/// üretimde kalır.
///
/// <b>Neden bir test, belge değil:</b> Kural
/// <c>deploy/MIGRATION-VE-GERI-ALMA.md</c>'de yazılı. Ama yazılı bir kuralın
/// tek zayıf noktası insanın hatırlaması; `dotnet ef migrations add` çıktısı
/// da her zaman istenen şeyi üretmiyor. Bu test, kuralı ihlal eden bir
/// migration'ı commit anında değil, <b>testte</b> yakalıyor.
///
/// <b>Ölçüm (10.09.2026):</b> Mevcut 40+ migration'ın <c>Up()</c> metotlarında
/// ihlal <b>bulunmadı</b>. Yani pratik zaten doğruydu, yalnızca yazılı ve
/// zorlanmış değildi. Bu yüzden "geçmişi affetme" (grandfathering) listesine
/// ihtiyaç olmadı — liste boş başlıyor.
/// </summary>
public class MigrationCompatibilityTests
{
    /// <summary>
    /// Tek adımda uygulanması ESKİ kodu bozan işlemler.
    ///
    /// Hepsinin ortak özelliği: eski kodun geçerli bir isteğini veritabanının
    /// REDDETMESİNE ya da eski kodun okuduğu bir şeyin KAYBOLMASINA yol açıyor.
    /// </summary>
    private static readonly Dictionary<string, string> ForbiddenInUp = new()
    {
        ["DropColumn"] = "Eski kod o kolonu hâlâ okuyor → sorgu patlar. Önce kodu kolonsuz hâle getir, dağıt, SONRA ayrı bir migration'da sil.",
        ["RenameColumn"] = "Hem eski hem yeni kod için yarısı kayıp. İki aşamalı desen: yeni kolonu ekle → doldur → eskisini sil (bkz. §3.1).",
        ["DropTable"] = "Geri alınamaz. Önce kodu tabloya hiç dokunmayacak hâle getir ve YEDEK al.",
        ["AddUniqueConstraint"] = "Eski kodun yazdığı çift kayıt reddedilir. Önce kodu tekilliği garanti edecek hâle getir.",
        ["DropPrimaryKey"] = "Eski kodun anahtara dayanan yazmaları bozulur.",
    };

    /// <summary>
    /// BİLEREK istisna tutulan migration'lar. Her satırın gerekçesi yazılı
    /// olmak zorunda.
    ///
    /// <b>Boş olması bir şey kanıtlıyor:</b> bugüne kadar hiçbir migration bu
    /// kuralı ihlal etmedi. Buraya bir satır eklemek, geri almanın o sürüm için
    /// artık mümkün OLMADIĞINI kabul etmek demek — o yüzden gerekçesi
    /// "kolaydı" olamaz.
    /// </summary>
    private static readonly Dictionary<string, string> DeliberateExceptions = new();

    private static readonly Regex UpMethod =
        new(@"protected\s+override\s+void\s+Up\s*\(", RegexOptions.Compiled);

    private static readonly Regex DownMethod =
        new(@"protected\s+override\s+void\s+Down\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// <c>AlterColumn</c> + <c>nullable: false</c>: eski kodun o kolonu
    /// vermeyen <c>INSERT</c>'i reddedilir. Aynı çağrının içinde geçmesi
    /// gerektiği için ayrı bir desenle aranıyor.
    /// </summary>
    private static readonly Regex TighteningAlter =
        new(@"AlterColumn[\s\S]{0,600}?nullable:\s*false", RegexOptions.Compiled);

    [Fact]
    public void Hicbir_migration_Up_metodunda_geriye_uyumsuz_islem_yapmiyor()
    {
        var files = MigrationFiles();
        Assert.NotEmpty(files);

        var violations = new List<string>();

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (DeliberateExceptions.ContainsKey(name)) continue;

            var up = ExtractUp(File.ReadAllText(file));
            if (up is null) continue;   // Ham SQL migration'ı; Up() imzası yok.

            foreach (var (op, why) in ForbiddenInUp)
            {
                if (up.Contains(op + "(", StringComparison.Ordinal))
                    violations.Add($"{name}: {op} — {why}");
            }

            if (TighteningAlter.IsMatch(up))
            {
                violations.Add(
                    $"{name}: AlterColumn(nullable: false) — Eski kodun o kolonu vermeyen " +
                    "INSERT'i reddedilir. İki aşamalı desen: önce kod her zaman doldursun, " +
                    "SONRAKİ dağıtımda NOT NULL yap (bkz. §3.2).");
            }
        }

        Assert.True(violations.Count == 0,
            "Bu migration'lar ileriye uyumluluk kuralını ihlal ediyor " +
            "(deploy/MIGRATION-VE-GERI-ALMA.md §2):\n  " +
            string.Join("\n  ", violations) +
            "\n\nGeri alma kodu döndürür, VERİTABANINI DÖNDÜRMEZ. Bu işlemlerden " +
            "biri üretime çıktıktan sonra geri alma veriyi bozar.\n" +
            "Ya §3'teki iki aşamalı desene böl, ya DeliberateExceptions'a " +
            "GEREKÇESİYLE ekle.");
    }

    /// <summary>
    /// <c>Down()</c>'da <c>DropColumn</c> DOĞRU ve beklenen: bir
    /// <c>AddColumn</c>'u geri alıyor. Test yalnızca <c>Up()</c>'a bakmalı;
    /// tüm dosyada arayan bir kontrol her migration'da yanlış alarm verirdi.
    ///
    /// Bu testin varlığı, yukarıdaki testin doğru yeri okuduğunu kanıtlıyor:
    /// <c>Down()</c>'ında silme yapan migration'lar VAR ve yukarıdaki test
    /// onlardan şikâyet etmiyor.
    /// </summary>
    [Fact]
    public void Down_metodundaki_silmeler_ihlal_sayilmiyor()
    {
        var withDropInDown = MigrationFiles()
            .Select(File.ReadAllText)
            .Count(source =>
            {
                var down = ExtractDown(source);
                return down is not null && down.Contains("DropColumn(", StringComparison.Ordinal);
            });

        Assert.True(withDropInDown > 0,
            "Down() icinde DropColumn yapan hic migration bulunamadi. Bu, testin " +
            "yanlis yeri okudugu anlamina gelebilir — kontrolun kendisi dogrulanamiyor.");
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────────

    private static string? ExtractUp(string source) => Between(source, UpMethod, DownMethod);

    private static string? ExtractDown(string source) => Between(source, DownMethod, null);

    /// <summary>
    /// Kabaca: <paramref name="from"/> imzasından <paramref name="to"/>
    /// imzasına (ya da dosya sonuna) kadar olan metin. Tam bir C# ayrıştırıcısı
    /// değil ve olması da gerekmiyor — aranan tek şey, o aralıkta bir çağrının
    /// geçip geçmediği. Üretilen migration dosyaları tek `Up`/`Down` çifti
    /// içerdiği için bu sınır güvenli.
    /// </summary>
    private static string? Between(string source, Regex from, Regex? to)
    {
        var start = from.Match(source);
        if (!start.Success) return null;

        var end = source.Length;
        if (to is not null)
        {
            var stop = to.Match(source, start.Index + start.Length);
            if (stop.Success) end = stop.Index;
        }

        return source[(start.Index + start.Length)..end];
    }

    /// <summary>
    /// Migration klasörünün yolu BU dosyanın derleme zamanındaki yolundan
    /// türetiliyor. Çalışma dizinine bağlı olsaydı test, nereden koşturulduğuna
    /// göre farklı davranırdı — ve bulamadığı için sessizce geçerdi.
    /// </summary>
    private static string[] MigrationFiles([CallerFilePath] string thisFile = "")
    {
        // .../backend/Namines.Tests/Security/MigrationCompatibilityTests.cs
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!;
        var dir = Path.Combine(backend.FullName, "Namines.Infrastructure", "Migrations");

        Assert.True(Directory.Exists(dir), $"Migration klasoru bulunamadi: {dir}");

        return Directory.GetFiles(dir, "*.cs")
            .Where(f => !f.EndsWith("Designer.cs", StringComparison.Ordinal))
            .Where(f => !f.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
    }
}
