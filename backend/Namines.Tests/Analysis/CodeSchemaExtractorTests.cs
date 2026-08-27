using System.Collections.Generic;
using System.Linq;
using Namines.Core.Analysis;

namespace Namines.Tests.Analysis;

/// <summary>second-phase/11-KODDAN-SEMA.md — format tanıma ve tarama sınırları.</summary>
public class CodeSchemaExtractorTests
{
    [Fact]
    public void A_prisma_file_is_routed_to_the_prisma_parser()
    {
        var files = new Dictionary<string, string>
        {
            ["schema.prisma"] = "model User {\n  id Int @id\n}",
        };

        var result = CodeSchemaExtractor.Extract(files);

        Assert.Equal("prisma", result.Format);
    }

    [Fact]
    public void Csharp_files_are_routed_to_the_efcore_parser()
    {
        var files = new Dictionary<string, string>
        {
            ["User.cs"] = "public class User { public int Id { get; set; } }",
        };

        var result = CodeSchemaExtractor.Extract(files);

        Assert.Equal("efcore", result.Format);
    }

    [Fact]
    public void Prisma_wins_when_both_formats_are_present()
    {
        // .prisma uzantısı kesin bir sinyal; C# taraması buluşsal. Belirsizlikte
        // kesin olan seçilmeli.
        var files = new Dictionary<string, string>
        {
            ["schema.prisma"] = "model User {\n  id Int @id\n}",
            ["User.cs"] = "public class User { public int Id { get; set; } }",
        };

        Assert.Equal("prisma", CodeSchemaExtractor.Extract(files).Format);
    }

    [Fact]
    public void An_unrecognised_format_throws_instead_of_guessing()
    {
        var files = new Dictionary<string, string>
        {
            ["models.py"] = "class User(models.Model):\n    name = models.CharField()",
        };

        var ex = Assert.Throws<CodeSchemaExtractor.UnknownFormatException>(() => CodeSchemaExtractor.Extract(files));
        Assert.Contains("Prisma", ex.Message);
    }

    [Fact]
    public void Empty_input_is_rejected()
    {
        Assert.Throws<CodeSchemaExtractor.UnknownFormatException>(
            () => CodeSchemaExtractor.Extract(new Dictionary<string, string>()));
    }

    [Fact]
    public void Files_beyond_the_count_limit_are_reported_as_skipped_not_dropped_silently()
    {
        var files = new Dictionary<string, string> { ["schema.prisma"] = "model A {\n  id Int @id\n}" };
        for (var i = 0; i < CodeSchemaExtractor.MaxFiles + 20; i++)
            files[$"Filler{i}.cs"] = "// nothing";

        var result = CodeSchemaExtractor.Extract(files);

        Assert.Contains(result.Skipped, s => s.Reason.Contains("file limit reached"));
    }

    [Fact]
    public void The_prisma_file_survives_the_limit_because_relevant_files_are_kept_first()
    {
        // Sınıra takılırsa kesilenler en az bilgi taşıyanlar olmalı — asıl
        // şema dosyası doldurma dosyaları yüzünden düşerse özellik hiç çalışmaz.
        var files = new Dictionary<string, string>();
        for (var i = 0; i < CodeSchemaExtractor.MaxFiles + 50; i++)
            files[$"Filler{i}.cs"] = "// nothing";
        files["zzz-schema.prisma"] = "model A {\n  id Int @id\n}";

        var result = CodeSchemaExtractor.Extract(files);

        Assert.Equal("prisma", result.Format);
        Assert.Single(result.Schema.Tables);
    }
}
