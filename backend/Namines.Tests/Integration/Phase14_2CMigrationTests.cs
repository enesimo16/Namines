using Namines.Identity.Infrastructure.Persistence;
using System.Data.Common;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Namines.Core.Models.Auth;
using Namines.Hub.Domain;
using Namines.Infrastructure.Data;
using Namines.Identity.Infrastructure.Persistence.Migrations;
using Namines.Tests.Services;
using Namines.Tests.TestSupport;
using Npgsql;

namespace Namines.Tests.Integration;

/// <summary>
/// Faz 14.2C migration'ı ve kesintisi GERÇEK PostgreSQL'de: boş kurulum, bir önceki migration'da
/// dağınık eski veriyle yükseltme, C# kurallarıyla alan alan karşılaştırma, eski davranışa karşı
/// izin tablosu, geri alma ve yeniden yükseltme, bozulmuş veride doğrulamanın patlaması.
/// </summary>
[Collection("Docker")]
public sealed class Phase14_2CMigrationTests : IClassFixture<Phase14_2BPostgresFixture>
{
    private const string Before = "20261105105313_AddSubscriptionCancellationOutbox";
    private const string Target = "20261105114438_Phase14_2C_NormalizeCollections";
    private static readonly string LongMask = new('m', 600);

    private readonly Phase14_2BPostgresFixture _pg;

    public Phase14_2CMigrationTests(Phase14_2BPostgresFixture pg) => _pg = pg;

    private static AuthDbContext Context(string cs, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(cs, o => o.CommandTimeout(300)).AddInterceptors(interceptors).Options);

    private static async Task ExecAsync(string cs, string sql, params (string Name, object? Value)[] args)
    {
        await using var conn = new NpgsqlConnection(Sp(cs));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 120 };
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(Sp(cs));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<List<string>> DumpAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(Sp(cs));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT row_to_json(t)::text FROM ({sql}) t ORDER BY 1", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return rows;
    }

    private static async Task<string?> ScalarTextAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(Sp(cs));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? null : (string)value;
    }

    /// <summary>
    /// Hedefsiz: üretimdeki tek yol, tüm zincirler. Hedefli: 14.2C'nin kendi adımları Auth zincirinde; modül
    /// zincirleri uygulanmışsa önce geri alınır (runbook sırası), tablolar eski yerine döner.
    /// </summary>
    private static async Task MigrateAsync(string cs, string? target = null)
    {
        if (target is null)
        {
            await TestModules.MigrateAllAsync(cs);
            return;
        }
        foreach (var module in TestModules.Descriptors.Where(d => d.Module != "Auth").OrderByDescending(d => d.Order))
        {
            await using var moduleContext = module.CreateContext(cs);
            await moduleContext.GetService<IMigrator>().MigrateAsync("0");
        }
        await using var ctx = Context(cs);
        await ctx.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>Ham SQL dizgeleri şema adı yazmıyor: tablo hangi şemadaysa orada bulunur.</summary>
    private static string Sp(string cs) =>
        new NpgsqlConnectionStringBuilder(cs) { SearchPath = "public,gateway,hub,studio,ops" }.ConnectionString;

    private static readonly string[] Labels =
    {
        "dba-risk,performance", " Bug, bug ,BUG,perf ", "", "   ", ",,", "İstanbul,istanbul,ISI,ışık", "tek", "ΑΣ,ασ",
        "a,b,a,b,a", "\tTab\n, Nbsp ", "perf,Perf",
    };

    private static readonly string?[] Masks =
    {
        null, "", "   ", ",", "email", "email, phone", "email,email, email ", "Email,email", "\tİsim\n,isim", "a b,c-d,\"quoted\"",
        "keep," + LongMask,
    };

    private static string KeyId(int i) => "key-" + i;

    /// <summary>
    /// Her dağınık değer kendi anahtarında. Aynı satırda ikisi birden: origin listesi i, IP listesi i.
    /// NOT VALID tavanı aşan eski satırlar da var; doldurma onlara takılmamalı.
    /// </summary>
    private async Task SeedLegacyAsync(string cs)
    {
        await MigrateAsync(cs, Before);

        const string T = "'2026-01-02 03:04:05+00'";
        await ExecAsync(cs, $"""
            INSERT INTO "AspNetUsers" ("Id","Type","CreatedAt","IsDev","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount","UserName","NormalizedUserName")
            VALUES ('u1',1,{T},false,true,false,false,true,0,'una','UNA');
            INSERT INTO studio.projects ("Id","Name","DbType","SchemaJson","NodePositionsJson","UserId","CreatedAt","UpdatedAt","AutoApproveSafeChanges")
            VALUES ('p1','P1','PostgreSQL','{"{"}{"}"}','{"{"}{"}"}','u1',{T},{T},false),
                   ('p2','P2','PostgreSQL','{"{"}{"}"}','{"{"}{"}"}','u1',{T},{T},false);
            ALTER TABLE "GatewayApiKeys" DROP CONSTRAINT IF EXISTS "{CheckConstraintSql.Name("GatewayApiKeys", "RateLimitPerMinute")}";
            ALTER TABLE "RepoIssues" DROP CONSTRAINT IF EXISTS "{CheckConstraintSql.Name("RepoIssues", "Body", "Length")}";
            """);

        var total = Math.Max(GatewayAllowlistCases.OriginLists.Length, GatewayAllowlistCases.IpLists.Length);
        for (var i = 0; i < total; i++)
        {
            await ExecAsync(cs, """
                INSERT INTO "GatewayApiKeys" ("Id","ProjectId","Name","Prefix","KeyHash","CanWrite","CanExecuteSql","CanExport","AllowedOrigins","AllowedIps","RateLimitPerMinute","CreatedByUserId","CreatedAt")
                VALUES (@id,'p1',@id,'nmn_legacy',@hash,false,false,false,@origins,@ips,@rate,'u1',now())
                """,
                ("id", KeyId(i)), ("hash", "hash-" + i),
                ("origins", i < GatewayAllowlistCases.OriginLists.Length ? GatewayAllowlistCases.OriginLists[i] : null),
                ("ips", i < GatewayAllowlistCases.IpLists.Length ? GatewayAllowlistCases.IpLists[i] : null),
                // 0: kısıttan önceki eski satır.
                ("rate", i == 1 ? 0 : 60));
        }

        for (var i = 0; i < Masks.Length; i++)
        {
            await ExecAsync(cs, """
                INSERT INTO "GatewayTablePermissions" ("Id","ProjectId","TableName","CanRead","CanWrite","CanExport","MaskedColumns","UpdatedAt")
                VALUES (@id,'p1',@table,true,false,false,@mask,'2026-01-02 03:04:05+00')
                """, ("id", "perm-" + i), ("table", "t" + i), ("mask", Masks[i]));
        }

        for (var i = 0; i < Labels.Length; i++)
        {
            // p1 ve p2: aynı etiket metni kiracılar arasında karışmamalı.
            var project = i % 2 == 0 ? "p1" : "p2";
            await ExecAsync(cs, """
                INSERT INTO "RepoIssues" ("Id","ProjectId","IssueNumber","Title","Body","IsClosed","Labels","CreatedAt")
                VALUES (@id,@project,@number,'t',@body,false,@labels,'2026-01-02 03:04:05+00')
                """, ("id", "issue-" + i), ("project", project), ("number", i + 1),
                ("body", i == 0 ? new string('b', 20_001) : "body"), ("labels", Labels[i]));
        }

        await ExecAsync(cs, $"""
            ALTER TABLE "GatewayApiKeys" ADD CONSTRAINT "{CheckConstraintSql.Name("GatewayApiKeys", "RateLimitPerMinute")}" CHECK ("RateLimitPerMinute" >= 1) NOT VALID;
            ALTER TABLE "RepoIssues" ADD CONSTRAINT "{CheckConstraintSql.Name("RepoIssues", "Body", "Length")}" CHECK (char_length("Body") <= 20000) NOT VALID;
            """);
    }

    private static async Task<List<GatewayApiKey>> LoadKeysAsync(string cs)
    {
        await using var ctx = Context(cs);
        return await ctx.Gateway().GatewayApiKeys.AsNoTracking().Include(k => k.OriginRules).Include(k => k.IpRules).AsSplitQuery().ToListAsync();
    }

    [RequiresDockerFact]
    public async Task Bos_kurulumda_kolonlar_yok_tablolar_ve_kisitlar_var()
    {
        var cs = await _pg.CreateDatabaseAsync("c_fresh");
        await MigrateAsync(cs);

        Assert.Equal(0, await CountAsync(cs, """
            SELECT count(*) FROM information_schema.columns
            WHERE (table_name = 'GatewayApiKeys' AND column_name IN ('AllowedOrigins','AllowedIps'))
               OR (table_name = 'GatewayTablePermissions' AND column_name = 'MaskedColumns')
               OR (table_name = 'RepoIssues' AND column_name = 'Labels')
            """));
        Assert.Equal(5, await CountAsync(cs, """
            SELECT count(*) FROM information_schema.tables
            WHERE (table_schema, table_name) IN (('gateway','api_key_allowed_origins'),('gateway','api_key_allowed_ips'),('gateway','table_masked_columns'),
                                                 ('hub','issue_labels'),('hub','repo_issue_labels'))
            """));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM pg_trigger WHERE tgname = 'trg_repo_issue_labels_gc'"));
        Assert.Equal(5, await CountAsync(cs, """
            SELECT count(*) FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace
            WHERE c.contype = 'f' AND ((n.nspname = 'gateway' AND c.conrelid::regclass::text ~ '(api_key_allowed_origins|api_key_allowed_ips|table_masked_columns)$')
                OR c.conname IN ('FK_issue_labels_projects_ProjectId','FK_repo_issue_labels_RepoIssues_Project'))
            """));
    }

    [RequiresDockerFact]
    public async Task Eski_veri_C_sharp_kurallariyla_alan_alan_ayni()
    {
        var cs = await _pg.CreateDatabaseAsync("c_field");
        await SeedLegacyAsync(cs);
        await MigrateAsync(cs);

        var keys = (await LoadKeysAsync(cs)).ToDictionary(k => k.Id);

        for (var i = 0; i < GatewayAllowlistCases.OriginLists.Length; i++)
        {
            var raw = GatewayAllowlistCases.OriginLists[i];
            var expected = GatewayAllowlistRules.ParseOrigins(raw);
            var key = keys[KeyId(i)];
            Assert.Equal(expected.Entries, key.OriginRules.OrderBy(o => o.Position).Select(o => o.Origin));
            Assert.True(expected.HasAnyEntry == key.RestrictOrigins, $"flag for '{raw}'");
        }

        for (var i = 0; i < GatewayAllowlistCases.IpLists.Length; i++)
        {
            var raw = GatewayAllowlistCases.IpLists[i];
            var expected = GatewayAllowlistRules.ParseIps(raw);
            var key = keys[KeyId(i)];
            // Metin biçimi motorlar arasında farklı olabilir; aynı ağ olmalı.
            Assert.Equal(
                expected.Entries.Select(e => IPNetworkText(e)),
                key.IpRules.OrderBy(o => o.Position).Select(o => IPNetworkText(o.Cidr)));
            Assert.True(expected.HasAnyEntry == key.RestrictIps, $"flag for '{raw}'");
        }

        await using var ctx = Context(cs);
        for (var i = 0; i < Masks.Length; i++)
        {
            var expected = GatewayAllowlistRules.ParseMaskedColumns(Masks[i])
                .Where(m => m.Length <= GatewayAllowlistRules.MaxMaskedColumnLength);
            Assert.Equal(expected, await ctx.MaskedColumnsAsync("p1", "t" + i));
        }

        var links = await ctx.Hub().RepoIssues.AsNoTracking().Include(r => r.LabelLinks).ThenInclude(l => l.Label).ToListAsync();
        for (var i = 0; i < Labels.Length; i++)
        {
            var parsed = IssueLabelRules.Parse(Labels[i]);
            var issue = links.Single(r => r.Id == "issue-" + i);
            var rows = issue.LabelLinks.OrderBy(l => l.Position).ToList();
            Assert.Equal(parsed.Select(p => p.Display), rows.Select(l => l.DisplayName));
            Assert.Equal(parsed.Select(p => p.Key), rows.Select(l => l.Label.NormalizedName));
            Assert.All(rows, l => Assert.Equal(issue.ProjectId, l.Label.ProjectId));
        }
    }

    /// <summary>İki motorun metni aynı olmasa da aynı ağ mı: adres baytları + önek.</summary>
    private static string IPNetworkText(string cidr)
    {
        var slash = cidr.IndexOf('/');
        return Convert.ToHexString(IPAddress.Parse(cidr[..slash]).GetAddressBytes()) + "/" + cidr[(slash + 1)..];
    }

    [RequiresDockerFact]
    public async Task Migrate_edilmis_veri_eski_davranisla_ayni_ve_hicbir_zaman_genislemiyor()
    {
        var cs = await _pg.CreateDatabaseAsync("c_parity");
        await SeedLegacyAsync(cs);
        await MigrateAsync(cs);

        var keys = (await LoadKeysAsync(cs)).ToDictionary(k => k.Id);

        for (var i = 0; i < GatewayAllowlistCases.OriginLists.Length; i++)
        {
            var raw = GatewayAllowlistCases.OriginLists[i];
            var invalid = GatewayAllowlistRules.ParseOrigins(raw).Invalid.Count > 0;
            foreach (var probe in GatewayAllowlistCases.OriginProbes)
            {
                var legacy = LegacyGatewayMatching.IsOriginAllowed(raw, probe);
                var current = GatewayKeyRestrictions.IsOriginAllowed(keys[KeyId(i)], probe);
                if (!invalid) Assert.True(legacy == current, $"origin '{raw}' vs '{probe}': legacy={legacy}, current={current}");
                else if (current) Assert.True(legacy, $"origin '{raw}' widened for '{probe}'");
            }
        }

        for (var i = 0; i < GatewayAllowlistCases.IpLists.Length; i++)
        {
            var raw = GatewayAllowlistCases.IpLists[i];
            var invalid = GatewayAllowlistRules.ParseIps(raw).Invalid.Count > 0;
            foreach (var probe in GatewayAllowlistCases.IpProbes)
            {
                var address = IPAddress.Parse(probe);
                var legacy = LegacyGatewayMatching.IsIpAllowed(raw, address, true);
                var current = GatewayKeyRestrictions.IsIpAllowed(keys[KeyId(i)], address, true, out _);
                if (!invalid) Assert.True(legacy == current, $"ip '{raw}' vs {probe}: legacy={legacy}, current={current}");
                else if (current) Assert.True(legacy, $"ip '{raw}' widened for {probe}");
            }
        }
    }

    [RequiresDockerFact]
    public async Task Tum_girdileri_gecersiz_liste_serbest_kalmiyor_ve_karantinaya_dusuyor()
    {
        var cs = await _pg.CreateDatabaseAsync("c_quarantine");
        await SeedLegacyAsync(cs);
        await MigrateAsync(cs);

        var keys = (await LoadKeysAsync(cs)).ToDictionary(k => k.Id);
        var star = keys[KeyId(Array.IndexOf(GatewayAllowlistCases.OriginLists, "*"))];
        Assert.True(star.RestrictOrigins);
        Assert.Empty(star.OriginRules);
        foreach (var probe in GatewayAllowlistCases.OriginProbes)
            Assert.False(GatewayKeyRestrictions.IsOriginAllowed(star, probe), $"'*' allowed '{probe}'");

        var badIp = keys[KeyId(Array.IndexOf(GatewayAllowlistCases.IpLists, "not-an-ip"))];
        Assert.True(badIp.RestrictIps);
        Assert.Empty(badIp.IpRules);
        foreach (var probe in GatewayAllowlistCases.IpProbes)
            Assert.False(GatewayKeyRestrictions.IsIpAllowed(badIp, IPAddress.Parse(probe), true, out _), probe);

        // PostgreSQL inet'in kabul ettiği, .NET'in reddettiği biçimler: kural olmaz, liste kapalıya düşer.
        foreach (var truncated in new[] { "::ffff:1.2.3", "::ffff:1.23", "::.2.3.4/0", "::ffff:62:.253.88.177/46", "::ffff:1.2.3.07" })
        {
            var row = keys[KeyId(Array.IndexOf(GatewayAllowlistCases.IpLists, truncated))];
            Assert.True(row.RestrictIps, truncated);
            Assert.Empty(row.IpRules);
            foreach (var probe in GatewayAllowlistCases.IpProbes.Append("1.2.3.0").Append("2600:1f18::77"))
                Assert.False(GatewayKeyRestrictions.IsIpAllowed(row, IPAddress.Parse(probe), true, out _), $"{truncated} allowed {probe}");
        }

        // Yalnızca virgül: eski kodda da kısıt yok, korunur ve iz bırakır.
        var blank = keys[KeyId(Array.IndexOf(GatewayAllowlistCases.OriginLists, " , ,"))];
        Assert.False(blank.RestrictOrigins);

        var expectedOrigins = GatewayAllowlistCases.OriginLists.Sum(l => GatewayAllowlistRules.ParseOrigins(l).Invalid.Count);
        var expectedIps = GatewayAllowlistCases.IpLists.Sum(l => GatewayAllowlistRules.ParseIps(l).Invalid.Count);
        Assert.Equal(expectedOrigins, await CountAsync(cs, """
            SELECT count(*) FROM migration_quarantine.orphaned_rows WHERE source_table = 'public.GatewayApiKeys.AllowedOrigins' AND reason <> 'blank_list_means_no_restriction'
            """));
        Assert.Equal(expectedIps, await CountAsync(cs, """
            SELECT count(*) FROM migration_quarantine.orphaned_rows WHERE source_table = 'public.GatewayApiKeys.AllowedIps' AND reason <> 'blank_list_means_no_restriction'
            """));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM migration_quarantine.orphaned_rows WHERE reason = 'wildcard_not_supported' AND row_data->>'entry' = '*' AND row_data->>'raw' = '*'"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM migration_quarantine.orphaned_rows WHERE reason = 'column_name_too_long'"));
        Assert.Equal(1, await CountAsync(cs, """SELECT count(*) FROM gateway.table_masked_columns WHERE "ColumnName" = 'keep'"""));
    }

    [RequiresDockerFact]
    public async Task Geri_alma_veriyi_davranisla_birlikte_geri_koyuyor_ve_yeniden_yukseltme_ayni()
    {
        var cs = await _pg.CreateDatabaseAsync("c_downup");
        await SeedLegacyAsync(cs);
        await MigrateAsync(cs);

        const string Origins = """SELECT "ApiKeyId","Origin","Position" FROM gateway.api_key_allowed_origins""";
        const string Ips = """SELECT "ApiKeyId","Cidr","Position" FROM gateway.api_key_allowed_ips""";
        const string Masked = """SELECT "TablePermissionId","ColumnName","Position" FROM gateway.table_masked_columns""";
        const string Linked = """SELECT l."RepoIssueId",c."ProjectId",c."Name",c."NormalizedName",l."DisplayName",l."Position" FROM hub.repo_issue_labels l JOIN hub.issue_labels c ON c."Id" = l."IssueLabelId" """;
        const string Flags = """SELECT "Id","RestrictOrigins","RestrictIps" FROM "GatewayApiKeys" """;
        const string Quarantine = "SELECT source_table, reason, row_data FROM migration_quarantine.orphaned_rows";

        var first = new[]
        {
            await DumpAsync(cs, Origins), await DumpAsync(cs, Ips), await DumpAsync(cs, Masked),
            await DumpAsync(cs, Linked), await DumpAsync(cs, Flags), await DumpAsync(cs, Quarantine),
        };

        await MigrateAsync(cs, Before);

        // Kolonlar geri geldi, tablolar gitti.
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('api_key_allowed_origins','api_key_allowed_ips','table_masked_columns','issue_labels','repo_issue_labels')"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM migration_quarantine.orphaned_rows WHERE source_table LIKE 'public.Gateway%'"));

        // Eski kod geri alınmış metni okuyunca, tablodaki her girdi için orijinal metinle aynı kararı vermeli.
        for (var i = 0; i < GatewayAllowlistCases.OriginLists.Length; i++)
        {
            var raw = GatewayAllowlistCases.OriginLists[i];
            var restored = await ScalarTextAsync(cs, $"""SELECT "AllowedOrigins" FROM "GatewayApiKeys" WHERE "Id" = '{KeyId(i)}'""");
            foreach (var probe in GatewayAllowlistCases.OriginProbes)
                Assert.True(
                    LegacyGatewayMatching.IsOriginAllowed(raw, probe) == LegacyGatewayMatching.IsOriginAllowed(restored, probe),
                    $"origin '{raw}' -> '{restored}' vs '{probe}'");
        }

        for (var i = 0; i < GatewayAllowlistCases.IpLists.Length; i++)
        {
            var raw = GatewayAllowlistCases.IpLists[i];
            var restored = await ScalarTextAsync(cs, $"""SELECT "AllowedIps" FROM "GatewayApiKeys" WHERE "Id" = '{KeyId(i)}'""");
            foreach (var probe in GatewayAllowlistCases.IpProbes)
                Assert.True(
                    LegacyGatewayMatching.IsIpAllowed(raw, IPAddress.Parse(probe), true) == LegacyGatewayMatching.IsIpAllowed(restored, IPAddress.Parse(probe), true),
                    $"ip '{raw}' -> '{restored}' vs {probe}");
        }

        for (var i = 0; i < Masks.Length; i++)
        {
            var restored = await ScalarTextAsync(cs, $"""SELECT "MaskedColumns" FROM "GatewayTablePermissions" WHERE "Id" = 'perm-{i}'""");
            Assert.Equal(GatewayAllowlistRules.ParseMaskedColumns(Masks[i]), GatewayAllowlistRules.ParseMaskedColumns(restored));
        }

        for (var i = 0; i < Labels.Length; i++)
        {
            var restored = await ScalarTextAsync(cs, $"""SELECT "Labels" FROM "RepoIssues" WHERE "Id" = 'issue-{i}'""");
            Assert.Equal(IssueLabelRules.Parse(Labels[i]).Select(p => p.Display), IssueLabelRules.Parse(restored).Select(p => p.Display));
        }

        await MigrateAsync(cs);
        var second = new[]
        {
            await DumpAsync(cs, Origins), await DumpAsync(cs, Ips), await DumpAsync(cs, Masked),
            await DumpAsync(cs, Linked), await DumpAsync(cs, Flags), await DumpAsync(cs, Quarantine),
        };

        for (var i = 0; i < first.Length; i++) Assert.Equal(first[i], second[i]);
    }

    /// <summary>Tek oturumda: hazırlık, bozma, doğrulama. Doğrulama patlamalı, işlem geri alınır.</summary>
    private static async Task AssertVerifyRaisesAsync(string cs, string prepare, string corrupt, string verify, string expected)
    {
        await using var conn = new NpgsqlConnection(Sp(cs));
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        foreach (var sql in new[] { prepare, corrupt })
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync();
        }
        await using var check = new NpgsqlCommand(verify, conn, tx) { CommandTimeout = 120 };
        var ex = await Assert.ThrowsAsync<PostgresException>(() => check.ExecuteNonQueryAsync());
        Assert.Contains(expected, ex.MessageText);
        await tx.RollbackAsync();
    }

    [RequiresDockerFact]
    public async Task Up_dogrulamasi_bozulmus_veriyi_yakaliyor()
    {
        var cs = await _pg.CreateDatabaseAsync("c_verify_up");
        await SeedLegacyAsync(cs);

        var prepare = Phase14_2CNormalizationScripts.UpPrepare;
        var verify = Phase14_2CNormalizationScripts.UpVerify;

        // Bozulmamış hâlde geçer: aşağıdakiler gerçekten bozmanın yakalandığını gösterir.
        await using (var conn = new NpgsqlConnection(Sp(cs)))
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using (var p = new NpgsqlCommand(prepare, conn, tx) { CommandTimeout = 120 }) await p.ExecuteNonQueryAsync();
            await using (var v = new NpgsqlCommand(verify, conn, tx) { CommandTimeout = 120 }) await v.ExecuteNonQueryAsync();
            await tx.RollbackAsync();
        }

        var origin = KeyId(Array.IndexOf(GatewayAllowlistCases.OriginLists, "https://a.com, https://b.com"));
        var star = KeyId(Array.IndexOf(GatewayAllowlistCases.OriginLists, "*"));
        var ip = KeyId(Array.IndexOf(GatewayAllowlistCases.IpLists, "10.0.0.0/8"));
        var badIp = KeyId(Array.IndexOf(GatewayAllowlistCases.IpLists, "not-an-ip"));
        var maskPerm = "perm-" + Array.IndexOf(Masks, "email, phone");
        var cases = new (string Corrupt, string Expected)[]
        {
            // Origin: eksik, fazla, yineleme, yetim, kapalıya düşmeyen bayrak, sıra, kanonik, karantina.
            ($"""DELETE FROM gateway.api_key_allowed_origins WHERE "ApiKeyId" = '{origin}' AND "Position" = 0""", "verify origins"),
            ($"""INSERT INTO gateway.api_key_allowed_origins ("ApiKeyId","Origin","Position") VALUES ('{origin}','https://evil.example',2)""", "verify origins"),
            ("""DROP INDEX gateway."IX_api_key_allowed_origins_ApiKeyId_Origin"; INSERT INTO gateway.api_key_allowed_origins ("ApiKeyId","Origin","Position") SELECT "ApiKeyId","Origin",99 FROM gateway.api_key_allowed_origins LIMIT 1""", "verify origins"),
            ("""ALTER TABLE gateway.api_key_allowed_origins DROP CONSTRAINT "FK_api_key_allowed_origins_GatewayApiKeys_ApiKeyId"; INSERT INTO gateway.api_key_allowed_origins ("ApiKeyId","Origin","Position") VALUES ('ghost','https://ghost.example',0)""", "verify origins"),
            ($"""UPDATE "GatewayApiKeys" SET "RestrictOrigins" = false WHERE "Id" = '{star}'""", "verify origins"),
            ($"""UPDATE gateway.api_key_allowed_origins SET "Position" = 7 WHERE "ApiKeyId" = '{origin}' AND "Position" = 1""", "verify origins"),
            ($"""DELETE FROM migration_quarantine.orphaned_rows WHERE row_data->>'key_id' = '{star}'""", "verify origins"),
            // IP
            ($"""DELETE FROM gateway.api_key_allowed_ips WHERE "ApiKeyId" = '{ip}'""", "verify ips"),
            ($"""INSERT INTO gateway.api_key_allowed_ips ("ApiKeyId","Cidr","Position") VALUES ('{ip}','0.0.0.0/0',1)""", "verify ips"),
            ($"""UPDATE "GatewayApiKeys" SET "RestrictIps" = false WHERE "Id" = '{badIp}'""", "verify ips"),
            ($"""UPDATE gateway.api_key_allowed_ips SET "Cidr" = '11.0.0.0/8' WHERE "ApiKeyId" = '{ip}'""", "verify ips"),
            ($"""DELETE FROM migration_quarantine.orphaned_rows WHERE row_data->>'key_id' = '{badIp}' AND source_table LIKE '%AllowedIps'""", "verify ips"),
            ("""ALTER TABLE gateway.api_key_allowed_ips DROP CONSTRAINT "FK_api_key_allowed_ips_GatewayApiKeys_ApiKeyId"; INSERT INTO gateway.api_key_allowed_ips ("ApiKeyId","Cidr","Position") VALUES ('ghost','1.1.1.1/32',0)""", "verify ips"),
            // Maskeler
            ($"""DELETE FROM gateway.table_masked_columns WHERE "TablePermissionId" = '{maskPerm}' AND "ColumnName" = 'phone'""", "verify masked columns"),
            ($"""UPDATE gateway.table_masked_columns SET "ColumnName" = 'EMAIL' WHERE "TablePermissionId" = '{maskPerm}' AND "ColumnName" = 'email'""", "verify masked columns"),
            ($"""INSERT INTO gateway.table_masked_columns ("TablePermissionId","ColumnName","Position") VALUES ('{maskPerm}','extra',2)""", "verify masked columns"),
            ("""ALTER TABLE gateway.table_masked_columns DROP CONSTRAINT "FK_table_masked_columns_GatewayTablePermissions_TablePermissio~"; INSERT INTO gateway.table_masked_columns ("TablePermissionId","ColumnName","Position") VALUES ('ghost','x',0)""", "verify masked columns"),
            // Etiketler: eksik, yazım değişmiş, sıra, başka projenin etiketi, katalog.
            ("""DELETE FROM hub.repo_issue_labels WHERE "RepoIssueId" = 'issue-0' AND "Position" = 1""", "verify labels"),
            ("""UPDATE hub.repo_issue_labels SET "DisplayName" = 'BUG' WHERE "RepoIssueId" = 'issue-1' AND "Position" = 0""", "verify labels"),
            ("""UPDATE hub.repo_issue_labels SET "Position" = 9 WHERE "RepoIssueId" = 'issue-0' AND "Position" = 1""", "verify labels"),
            ("""UPDATE hub.repo_issue_labels l SET "IssueLabelId" = (SELECT c."Id" FROM hub.issue_labels c WHERE c."ProjectId" = 'p2' AND c."NormalizedName" = 'perf') WHERE l."RepoIssueId" = 'issue-10' AND l."Position" = 0""", "verify labels"),
            ("""UPDATE hub.issue_labels SET "NormalizedName" = 'zzz' WHERE "NormalizedName" = 'tek'""", "verify labels"),
            ("""INSERT INTO hub.issue_labels ("ProjectId","Name","NormalizedName") VALUES ('p1','unused','unused')""", "verify labels"),
        };

        foreach (var (corrupt, expected) in cases)
            await AssertVerifyRaisesAsync(cs, prepare, corrupt, verify, expected);
    }

    [RequiresDockerFact]
    public async Task Dogrulama_doldurma_fonksiyonunun_hatasini_gorebiliyor()
    {
        // Gömülü IPv4 korumasını sökünce doldurma açığı yeniden üretir. Doğrulama aynı fonksiyonu paylaşsaydı
        // geçerdi (denetim B-1 tam böyle kaçtı); kendi ayrıştırıcısıyla patlamalı.
        var cs = await _pg.CreateDatabaseAsync("c_verify_independent");
        await SeedLegacyAsync(cs);

        var buggy = Phase14_2CNormalizationScripts.UpPrepare.Replace(Phase14_2CNormalizationScripts.DottedTailGuardSql, "");
        Assert.NotEqual(Phase14_2CNormalizationScripts.UpPrepare, buggy);

        await AssertVerifyRaisesAsync(cs, buggy, "SELECT 1", Phase14_2CNormalizationScripts.UpVerify, "verify ips");
    }

    /// <summary>Sabit tohumlu üreteç: geçerli örneklerin bozulmuş hâlleri, ayrıştırıcıların sınırına yakın girdiler.</summary>
    private static List<string> Fuzz(int seed, int count, string[] valid, string alphabet)
    {
        var random = new Random(seed);
        var result = new HashSet<string>(StringComparer.Ordinal);
        while (result.Count < count)
        {
            var text = valid[random.Next(valid.Length)];
            for (var edits = random.Next(0, 4); edits > 0 && text.Length > 0; edits--)
            {
                var at = random.Next(text.Length);
                text = random.Next(3) switch
                {
                    0 => text.Remove(at, 1),
                    1 => text.Insert(at, alphabet[random.Next(alphabet.Length)].ToString()),
                    _ => text[..at] + alphabet[random.Next(alphabet.Length)] + text[(at + 1)..],
                };
            }

            // Virgül ve boşluk CSV ayrıştırmasının işi; burada tek girdi sınanıyor. NUL PostgreSQL metni olamaz.
            text = text.Trim();
            if (text.Length > 0 && !text.Contains(',') && !text.Contains('\0')) result.Add(text);
        }
        return result.ToList();
    }

    /// <summary>C# kanonik CIDR'ını doğrulama ayrıştırıcısının biçimine çevirir: baytlar + önek.</summary>
    private static string? BytesForm(string? cidr)
    {
        if (cidr is null) return null;
        var slash = cidr.IndexOf('/');
        return Convert.ToHexString(IPAddress.Parse(cidr[..slash]).GetAddressBytes()).ToLowerInvariant() + cidr[slash..];
    }

    private static async Task<List<(string Input, string? A, string? B)>> EvalAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, List<string> inputs)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx) { CommandTimeout = 300 };
        cmd.Parameters.AddWithValue("inputs", inputs.ToArray());
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<(string, string?, string?)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return rows;
    }

    [RequiresDockerFact]
    public async Task Sql_ve_csharp_ayristiricilari_fuzz_ile_ayni()
    {
        var cs = await _pg.CreateDatabaseAsync("c_fuzz");
        await MigrateAsync(cs, Before);

        await using var conn = new NpgsqlConnection(Sp(cs));
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var prepare = new NpgsqlCommand(Phase14_2CNormalizationScripts.UpPrepare, conn, tx) { CommandTimeout = 120 })
            await prepare.ExecuteNonQueryAsync();

        var ips = Fuzz(20261105, 30_000, new[]
        {
            "1.2.3.4", "10.0.0.0/8", "192.168.1.77/24", "0.0.0.0/0", "255.255.255.255/32", "::1", "::", "::/0", "2001:db8::1", "2001:db8::/32",
            "2001:0db8:0:0:0:0:0:1/128", "1:2:3:4:5:6:7:8", "1:2:3:4:5:6:7::", "::2:3:4:5:6:7:8", "::ffff:1.2.3.4", "::ffff:102:304", "::1.2.3.4",
            "1:2:3:4:5:6:1.2.3.4", "fe80::1", "fe80::1%3", "::ffff:1.2.3.4/128", "[::1]", "1.2", "0x7f.0.0.1", "010.0.0.1", "::ffff:1.2.3", "::.2.3.4/0",
        }, "0123456789abcdefABCDEFgx:./%[]- +");
        var ipRows = await EvalAsync(conn, tx,
            "SELECT x, pg_temp.namines_2c_ip(x), pg_temp.namines_2c_check_ip(x) FROM unnest(@inputs) AS x", ips);

        var accepted = 0;
        var ipDiffs = new List<string>();
        foreach (var (input, filled, check) in ipRows)
        {
            var expected = BytesForm(GatewayAllowlistRules.NormalizeIp(input));
            if (expected is not null) accepted++;
            // Doldurma, doğrulama ve C# aynı ağı vermeli; metin biçimi motora göre değişebilir.
            if (BytesForm(filled) != expected || check != expected)
                ipDiffs.Add($"'{input}': csharp={expected}, fill={BytesForm(filled)}, check={check}");
        }
        Assert.True(ipDiffs.Count == 0, $"{ipDiffs.Count} IP differences:\n" + string.Join("\n", ipDiffs.Take(40)));
        // Üreteç gerçekten iki tarafı da denemeli, yoksa eşitlik boş bir iddia olur.
        Assert.InRange(accepted, 1000, ips.Count - 1000);

        var origins = Fuzz(20261106, 30_000, new[]
        {
            "https://app.example.com", "HTTPS://App.Example.COM", "http://localhost:3000", "https://a.com:443", "https://a.com:65535", "capacitor://localhost",
            "chrome-extension://abc", "https://foo_bar.example.com", "https://[::1]:8443", "https://[2001:db8::1]", "https://[::ffff:1.2.3.4]:1", "https://[::ffff:1.2.3]",
            "https://[1.2.3.4]", "*", "https://*.example.com", "https://a.com/", "https://a.com/path", "null", "https://user@a.com", "https://a.com:99999", "https://a.com:080",
        }, "abcxyzABC0189:/.[]-_+*@?#% ü");
        var originRows = await EvalAsync(conn, tx,
            "SELECT x, pg_temp.namines_2c_origin(x), pg_temp.namines_2c_check_origin(x) FROM unnest(@inputs) AS x", origins);

        accepted = 0;
        var originDiffs = new List<string>();
        foreach (var (input, filled, check) in originRows)
        {
            var expected = GatewayAllowlistRules.NormalizeOrigin(input);
            if (expected is not null) accepted++;
            if (filled != expected || check != expected)
                originDiffs.Add($"'{input}': csharp={expected}, fill={filled}, check={check}");
        }
        Assert.True(originDiffs.Count == 0, $"{originDiffs.Count} origin differences:\n" + string.Join("\n", originDiffs.Take(40)));
        Assert.InRange(accepted, 1000, origins.Count - 1000);

        await tx.RollbackAsync();
    }

    [RequiresDockerFact]
    public async Task Etiket_temizligi_eszamanli_baglanan_issue_i_silmiyor()
    {
        var cs = await _pg.CreateDatabaseAsync("c_gc_race");
        await MigrateAsync(cs);
        await SeedOwnedKeyAsync(cs, null, null);
        await ExecAsync(cs, """
            INSERT INTO "RepoIssues" ("Id","ProjectId","IssueNumber","Title","Body","IsClosed","CreatedAt")
            VALUES ('gc-a','proj',1,'a','',false,now()), ('gc-b','proj',2,'b','',false,now());
            INSERT INTO hub.issue_labels ("Id","ProjectId","Name","NormalizedName") VALUES ('gc-l','proj','x','x');
            INSERT INTO hub.repo_issue_labels ("RepoIssueId","IssueLabelId","ProjectId","DisplayName","Position") VALUES ('gc-a','gc-l','proj','x',0);
            """);

        // T1 var olan etikete yeni bağ ekler, henüz commit etmez.
        await using var t1 = new NpgsqlConnection(Sp(cs));
        await t1.OpenAsync();
        await using var tx1 = await t1.BeginTransactionAsync();
        await using (var link = new NpgsqlCommand(
            """INSERT INTO hub.repo_issue_labels ("RepoIssueId","IssueLabelId","ProjectId","DisplayName","Position") VALUES ('gc-b','gc-l','proj','x',0)""", t1, tx1))
            await link.ExecuteNonQueryAsync();

        // T2 etiketin son görünen issue'sunu siler: tetikleyici T1'i beklemeli.
        var deleting = ExecAsync(cs, """DELETE FROM "RepoIssues" WHERE "Id" = 'gc-a'""");
        await Task.Delay(1000);
        Assert.False(deleting.IsCompleted, "the trigger should wait for the concurrent link");

        await tx1.CommitAsync();
        await deleting;

        Assert.Equal(1, await CountAsync(cs, """SELECT count(*) FROM hub.issue_labels WHERE "Id" = 'gc-l'"""));
        Assert.Equal(1, await CountAsync(cs, """SELECT count(*) FROM hub.repo_issue_labels WHERE "RepoIssueId" = 'gc-b'"""));

        // Son bağ gerçekten gidince katalog da gider.
        await ExecAsync(cs, """DELETE FROM "RepoIssues" WHERE "Id" = 'gc-b'""");
        Assert.Equal(0, await CountAsync(cs, """SELECT count(*) FROM hub.issue_labels WHERE "Id" = 'gc-l'"""));
    }

    [RequiresDockerFact]
    public async Task Migration_acik_kilit_karsisinda_sonsuza_dek_beklemiyor()
    {
        Assert.Contains("SET LOCAL lock_timeout", Phase14_2CNormalizationScripts.UpPrepare);
        Assert.Contains("SET LOCAL lock_timeout", Phase14_2CNormalizationScripts.DownRestore);

        var cs = await _pg.CreateDatabaseAsync("c_lock");
        await MigrateAsync(cs, Before);

        await using var holder = new NpgsqlConnection(Sp(cs));
        await holder.OpenAsync();
        await using var holderTx = await holder.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("""SELECT * FROM "GatewayApiKeys" LIMIT 1""", holder, holderTx))
        await using (var r = await hold.ExecuteReaderAsync()) { }

        await using var conn = new NpgsqlConnection(Sp(cs));
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var cmd = new NpgsqlCommand("""
            SET LOCAL lock_timeout = '500ms';
            LOCK TABLE "RepoIssues", "GatewayApiKeys", "GatewayTablePermissions" IN ACCESS EXCLUSIVE MODE;
            """, conn, tx);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("55P03", ex.SqlState);
    }

    [RequiresDockerFact]
    public async Task Down_dogrulamasi_bozulmus_veriyi_yakaliyor()
    {
        var cs = await _pg.CreateDatabaseAsync("c_verify_down");
        await SeedLegacyAsync(cs);
        await MigrateAsync(cs);

        var restore = Phase14_2CNormalizationScripts.DownRestore;
        var verify = Phase14_2CNormalizationScripts.DownVerify;
        var origin = KeyId(Array.IndexOf(GatewayAllowlistCases.OriginLists, "https://a.com, https://b.com"));
        var star = KeyId(Array.IndexOf(GatewayAllowlistCases.OriginLists, "*"));
        var ip = KeyId(Array.IndexOf(GatewayAllowlistCases.IpLists, "10.0.0.0/8"));
        var badIp = KeyId(Array.IndexOf(GatewayAllowlistCases.IpLists, "not-an-ip"));
        var maskPerm = "perm-" + Array.IndexOf(Masks, "email, phone");

        var cases = new (string Corrupt, string Expected)[]
        {
            ($"""UPDATE "GatewayApiKeys" SET "AllowedOrigins" = 'https://other.example' WHERE "Id" = '{origin}'""", "down verify origins"),
            ($"""UPDATE "GatewayApiKeys" SET "AllowedOrigins" = NULL WHERE "Id" = '{star}'""", "down verify origins"),
            ($"""UPDATE "GatewayApiKeys" SET "AllowedIps" = '11.0.0.0/8' WHERE "Id" = '{ip}'""", "down verify ips"),
            ($"""UPDATE "GatewayApiKeys" SET "AllowedIps" = ' , ' WHERE "Id" = '{badIp}'""", "down verify ips"),
            ($"""UPDATE "GatewayTablePermissions" SET "MaskedColumns" = 'email' WHERE "Id" = '{maskPerm}'""", "down verify masked columns"),
            ("""UPDATE "RepoIssues" SET "Labels" = 'other' WHERE "Id" = 'issue-1'""", "down verify labels"),
        };

        foreach (var (corrupt, expected) in cases)
            await AssertVerifyRaisesAsync(cs, restore, corrupt, verify, expected);
    }

    [RequiresDockerFact]
    public async Task Kisitlar_yazmada_gercek_ve_cascade_silme_temiz()
    {
        var cs = await _pg.CreateDatabaseAsync("c_constraints");
        await SeedLegacyAsync(cs);
        await MigrateAsync(cs);

        var key = KeyId(Array.IndexOf(GatewayAllowlistCases.IpLists, "10.0.0.0/8"));

        async Task Rejected(string sql, string state)
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(cs, sql));
            Assert.Equal(state, ex.SqlState);
        }

        // Host biti açık, bozuk, öneksiz: PostgreSQL'in cidr ayrıştırıcısı reddeder.
        await Rejected($"""INSERT INTO gateway.api_key_allowed_ips ("ApiKeyId","Cidr","Position") VALUES ('{key}','10.1.2.3/8',9)""", "22P02");
        await Rejected($"""INSERT INTO gateway.api_key_allowed_ips ("ApiKeyId","Cidr","Position") VALUES ('{key}','not-an-ip/8',9)""", "22P02");
        await Rejected($"""INSERT INTO gateway.api_key_allowed_ips ("ApiKeyId","Cidr","Position") VALUES ('{key}','1.2.3.4',9)""", "23514");
        await Rejected($"""INSERT INTO gateway.api_key_allowed_ips ("ApiKeyId","Cidr","Position") VALUES ('{key}','10.0.0.0/8',9)""", "23505");
        await Rejected($"""INSERT INTO gateway.api_key_allowed_origins ("ApiKeyId","Origin","Position") VALUES ('{key}','*',9)""", "23514");
        await Rejected($"""INSERT INTO gateway.api_key_allowed_origins ("ApiKeyId","Origin","Position") VALUES ('{key}','https://a.com/',9)""", "23514");
        await Rejected($"""INSERT INTO gateway.api_key_allowed_origins ("ApiKeyId","Origin","Position") VALUES ('{key}','HTTPS://A.COM',9)""", "23514");
        await Rejected("""INSERT INTO gateway.api_key_allowed_origins ("ApiKeyId","Origin","Position") VALUES ('ghost','https://a.com',0)""", "23503");
        await Rejected("""INSERT INTO gateway.table_masked_columns ("TablePermissionId","ColumnName","Position") VALUES ('perm-4','',9)""", "23514");
        await Rejected("""INSERT INTO gateway.table_masked_columns ("TablePermissionId","ColumnName","Position") VALUES ('perm-4','email',9)""", "23505");
        await Rejected("""INSERT INTO hub.issue_labels ("ProjectId","Name","NormalizedName") VALUES ('p1','x','tek')""", "23505");

        // Etiket yaşam döngüsü: son bağ gidince katalog satırı da gider, başkası kullanıyorsa kalır.
        await ExecAsync(cs, """
            INSERT INTO "RepoIssues" ("Id","ProjectId","IssueNumber","Title","Body","IsClosed","CreatedAt") VALUES ('issue-x','p1',500,'t','b',false,now());
            INSERT INTO hub.repo_issue_labels ("RepoIssueId","IssueLabelId","ProjectId","DisplayName","Position")
            SELECT 'issue-x', "Id", "ProjectId", "Name", 0 FROM hub.issue_labels WHERE "ProjectId" = 'p1' AND "NormalizedName" = 'tek';
            """);
        const string Tek = """SELECT count(*) FROM hub.issue_labels WHERE "ProjectId" = 'p1' AND "NormalizedName" = 'tek'""";
        var original = "issue-" + Array.IndexOf(Labels, "tek");
        await ExecAsync(cs, $"""DELETE FROM "RepoIssues" WHERE "Id" = '{original}'""");
        Assert.Equal(1, await CountAsync(cs, Tek));
        await ExecAsync(cs, """DELETE FROM "RepoIssues" WHERE "Id" = 'issue-x'""");
        Assert.Equal(0, await CountAsync(cs, Tek));
        Assert.Equal(0, await CountAsync(cs, $"""SELECT count(*) FROM hub.repo_issue_labels WHERE "RepoIssueId" IN ('issue-x','{original}')"""));

        // Proje silme: çocuk tablolarda iz kalmaz, diğer kiracı etkilenmez.
        var p2Labels = await CountAsync(cs, """SELECT count(*) FROM hub.issue_labels WHERE "ProjectId" = 'p2'""");
        Assert.True(p2Labels > 0);
        await ExecAsync(cs, """DELETE FROM studio.projects WHERE "Id" = 'p1'""");
        Assert.Equal(0, await CountAsync(cs, """SELECT count(*) FROM gateway.api_key_allowed_origins"""));
        Assert.Equal(0, await CountAsync(cs, """SELECT count(*) FROM gateway.api_key_allowed_ips"""));
        Assert.Equal(0, await CountAsync(cs, """SELECT count(*) FROM gateway.table_masked_columns"""));
        Assert.Equal(0, await CountAsync(cs, """SELECT count(*) FROM hub.issue_labels WHERE "ProjectId" = 'p1'"""));
        Assert.Equal(p2Labels, await CountAsync(cs, """SELECT count(*) FROM hub.issue_labels WHERE "ProjectId" = 'p2'"""));
    }

    private sealed class CommandLog : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, ct);
        }
    }

    private static async Task<string> SeedOwnedKeyAsync(string cs, string? origins, string? ips)
    {
        await using var ctx = Context(cs);
        var user = new ApplicationUser { Id = "owner", UserName = "owner", NormalizedUserName = "OWNER" };
        ctx.Users.Add(user);
        ctx.Studio().Projects.Add(new CloudProject { Id = "proj", Name = "P", DbType = "PostgreSQL", SchemaJson = "{}", NodePositionsJson = "{}", UserId = "owner" });
        var (key, raw) = GatewayKeys.CreateKey("proj", "k", "owner", false, null);
        GatewayAllowlistRules.ApplyOrigins(key, GatewayAllowlistRules.ParseOrigins(origins).Entries);
        GatewayAllowlistRules.ApplyIps(key, GatewayAllowlistRules.ParseIps(ips).Entries);
        ctx.Gateway().GatewayApiKeys.Add(key);
        await ctx.SaveChangesAsync();
        return raw;
    }

    [RequiresDockerFact]
    public async Task Anahtar_dogrulama_listeleri_tek_sorguda_yukluyor()
    {
        var cs = await _pg.CreateDatabaseAsync("c_queries");
        await MigrateAsync(cs);
        var raw = await SeedOwnedKeyAsync(cs, "https://a.com,https://b.com", "1.2.3.4,10.0.0.0/8");

        var log = new CommandLog();
        await using var ctx = Context(cs);
        var key = await new ModuleKit(ctx, cs, log).GatewayAccess.AuthenticateAsync(raw);

        Assert.NotNull(key);
        Assert.Equal(2, key!.OriginRules.Count);
        Assert.Equal(2, key.IpRules.Count);
        Assert.Single(log.Commands, c => c.Contains("api_key_allowed_origins"));
        Assert.Single(log.Commands, c => c.Contains("api_key_allowed_ips"));
        // Bölünmüş sorgu: iki listenin çarpımı yok (BL-C3).
        Assert.DoesNotContain(log.Commands, c => c.Contains("api_key_allowed_origins") && c.Contains("api_key_allowed_ips"));
        Assert.True(GatewayKeyRestrictions.IsOriginAllowed(key, "https://b.com"));
        Assert.False(GatewayKeyRestrictions.IsOriginAllowed(key, "https://c.com"));
        Assert.True(GatewayKeyRestrictions.IsIpAllowed(key, IPAddress.Parse("10.9.9.9"), true, out _));
    }

    [RequiresDockerFact]
    public async Task Maske_okuma_tek_sorgu_ve_hata_bos_liste_donmuyor()
    {
        var cs = await _pg.CreateDatabaseAsync("c_mask");
        await MigrateAsync(cs);
        await SeedOwnedKeyAsync(cs, null, null);

        await using (var ctx = Context(cs))
        {
            ctx.Gateway().GatewayTablePermissions.Add(new GatewayTablePermission
            {
                ProjectId = "proj", TableName = "users", CanRead = true,
                MaskedColumnRules = new List<GatewayTableMaskedColumn>
                {
                    new() { ColumnName = "phone", Position = 1 },
                    new() { ColumnName = "email", Position = 0 },
                },
            });
            await ctx.SaveChangesAsync();
        }

        var log = new CommandLog();
        await using (var ctx = Context(cs))
        {
            var access = new ModuleKit(ctx, cs, log).GatewayAccess;
            Assert.Equal(new[] { "email", "phone" }, await access.MaskedColumnsAsync("proj", "users"));
            Assert.Empty(await access.MaskedColumnsAsync("proj", "nope"));
            Assert.Equal(2, log.Commands.Count);
        }

        // Tablo okunamıyorsa istek patlamalı; boş liste maskesiz okuma demek olurdu.
        await ExecAsync(cs, "ALTER TABLE gateway.table_masked_columns RENAME TO table_masked_columns_x");
        await using (var ctx = Context(cs))
            await Assert.ThrowsAsync<PostgresException>(() => ctx.MaskedColumnsAsync("proj", "users"));
    }

    [RequiresDockerFact]
    public async Task Ayni_yeni_etiketi_eszamanli_acanlar_yeniden_denemeyle_birlesiyor()
    {
        var cs = await _pg.CreateDatabaseAsync("c_race");
        await MigrateAsync(cs);
        await SeedOwnedKeyAsync(cs, null, null);

        await using var first = Context(cs);
        await using var second = Context(cs);
        var a = new RepoIssue { ProjectId = "proj", IssueNumber = 1, Title = "a", Body = "" };
        var b = new RepoIssue { ProjectId = "proj", IssueNumber = 2, Title = "b", Body = "" };

        // İkisi de katalogda etiketi göremez, ikisi de açmaya çalışır.
        await RepoIssueLabelStore.AttachAsync(first.Hub(), a, "Bug");
        await RepoIssueLabelStore.AttachAsync(second.Hub(), b, "BUG, other");
        first.Hub().RepoIssues.Add(a);
        second.Hub().RepoIssues.Add(b);
        await first.Hub().SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateException>(() => second.Hub().SaveChangesAsync());

        // Controller'daki yeniden deneme yolu.
        second.Hub().ChangeTracker.Clear();
        var retry = new RepoIssue { ProjectId = "proj", IssueNumber = 2, Title = "b", Body = "" };
        await RepoIssueLabelStore.AttachAsync(second.Hub(), retry, "BUG, other");
        second.Hub().RepoIssues.Add(retry);
        await second.Hub().SaveChangesAsync();

        Assert.Equal(1, await CountAsync(cs, """SELECT count(*) FROM hub.issue_labels WHERE "NormalizedName" = 'bug'"""));
        Assert.Equal(2, await CountAsync(cs, "SELECT count(*) FROM hub.issue_labels"));
        var spelling = await DumpAsync(cs, """SELECT "DisplayName" FROM hub.repo_issue_labels""");
        Assert.Equal(3, spelling.Count);
        Assert.Contains(spelling, s => s.Contains("\"Bug\""));
        Assert.Contains(spelling, s => s.Contains("\"BUG\""));

        await using var read = Context(cs);
        var filtered = await read.Hub().RepoIssues.Where(i => i.ProjectId == "proj").WithLabel("proj", " bUg ").CountAsync();
        Assert.Equal(2, filtered);
        Assert.Equal(1, await read.Hub().RepoIssues.Where(i => i.ProjectId == "proj").WithLabel("proj", "other").CountAsync());
        // Eski LIKE alt dizge eşleşmesi bitti.
        Assert.Equal(0, await read.Hub().RepoIssues.Where(i => i.ProjectId == "proj").WithLabel("proj", "bu").CountAsync());
    }

    [RequiresDockerFact]
    public async Task Gateway_baglaminin_esledigi_her_kolon_veritabaninda_var()
    {
        var cs = await _pg.CreateDatabaseAsync("c_gateway_ctx");
        await MigrateAsync(cs);
        await SeedOwnedKeyAsync(cs, "https://a.com", "1.2.3.4");

        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(cs).Options;
        await using var gateway = new GatewayDbContext(options);

        foreach (var entity in gateway.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            var schema = entity.GetSchema();
            Assert.False(string.IsNullOrEmpty(table));
            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName();
                Assert.Equal(1, await CountAsync(cs, $"""
                    SELECT count(*) FROM information_schema.columns
                    WHERE table_schema = '{schema}' AND table_name = '{table}' AND column_name = '{column}'
                    """));
            }
        }

        var key = await gateway.GatewayApiKeys.Include(k => k.OriginRules).Include(k => k.IpRules).AsSplitQuery().SingleAsync();
        Assert.Equal("https://a.com", key.OriginRules.Single().Origin);
        Assert.Equal("1.2.3.4/32", key.IpRules.Single().Cidr);
    }

    [Fact]
    public void Dondurulmus_migration_kaliplari_kural_kaynagiyla_ayni()
    {
        // Migration kendi kopyasını taşıyor; bugün kural kaynağıyla aynı olmak zorunda.
        var up = Phase14_2CNormalizationScripts.UpPrepare;
        Assert.Contains(GatewayAllowlistRules.OriginPattern, up);
        Assert.Contains(GatewayAllowlistRules.Ipv4Pattern, up);
        Assert.Contains(GatewayAllowlistRules.Ipv6CharsPattern, up);
        Assert.Contains(GatewayAllowlistRules.PrefixPattern, up);
        Assert.Contains(GatewayAllowlistRules.EmbeddedIpv4Pattern, up);
        Assert.Contains(Target.Split('_', 2)[1], typeof(Phase14_2C_NormalizeCollections).Name);
    }
}
