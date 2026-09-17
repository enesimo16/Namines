using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Services;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Final whole-branch review I5: "maxTokens" varsayılanı kod içinde
/// "4096"'dan "32000"e çıkarıldı, ama Advanced AI Tuning panelini bir kez
/// bile kaydetmiş bir kullanıcının veritabanında kalıcı "4096" yazıyor ve
/// <see cref="AiAdvancedSettings.MaxTokensFor"/> onu SONSUZA DEK o tavana
/// kilitliyor — Pro/Team kullanıcısı bile. <see cref="AiAdvancedSettingsFixup"/>
/// bunu tek seferlik, idempotent bir veri düzeltmesiyle çözüyor.
/// </summary>
public sealed class AiAdvancedSettingsFixupTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;

        await using var db = new AuthDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private AuthDbContext Context() => new(_options);

    /// <summary>
    /// <c>UserAIPolicy.UserId</c> → <c>ApplicationUser.Id</c> yabancı anahtarı
    /// SQLite'ta da uygulanıyor; bu yüzden her policy satırından önce kullanıcı
    /// satırı tohumlanıyor.
    /// </summary>
    private async Task SeedUserPolicyAsync(string userId, string? advancedJson)
    {
        await using var db = Context();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = userId });
        db.UserAIPolicies.Add(new UserAIPolicy { UserId = userId, AdvancedJson = advancedJson });
        await db.SaveChangesAsync();
    }

    private async Task<string?> AdvancedJsonOfAsync(string userId)
    {
        await using var db = Context();
        return await db.UserAIPolicies.Where(p => p.UserId == userId).Select(p => p.AdvancedJson).SingleAsync();
    }

    [Fact]
    public async Task Persisted_stale_4096_no_longer_caps_a_pro_user()
    {
        // Kullanıcı Advanced paneli bir kez kaydetmiş — eski (kod içi)
        // varsayılan "4096" o zamanki hâliyle diske yazılmış.
        await SeedUserPolicyAsync("pro-user", new AiAdvancedSettings { MaxTokens = "4096" }.ToJson());

        await using (var db = Context())
            await AiAdvancedSettingsFixup.RunAsync(db, NullLogger.Instance);

        var settings = AiAdvancedSettings.Parse(await AdvancedJsonOfAsync("pro-user"));

        // Artık Pro tavanına (16.000) sığıyor, 4096'ya kilitli değil.
        Assert.Equal(16_000, settings.MaxTokensFor(PlanTier.Pro));
    }

    [Fact]
    public async Task Is_idempotent_running_twice_does_not_change_the_result_again()
    {
        await SeedUserPolicyAsync("u1", new AiAdvancedSettings { MaxTokens = "4096" }.ToJson());

        await using (var db = Context())
            await AiAdvancedSettingsFixup.RunAsync(db, NullLogger.Instance);

        var afterFirstRun = await AdvancedJsonOfAsync("u1");

        // İkinci çalıştırma: satır artık > 4096 olduğundan eşleşmemeli, hiçbir
        // şey değişmemeli.
        await using (var db = Context())
            await AiAdvancedSettingsFixup.RunAsync(db, NullLogger.Instance);

        var afterSecondRun = await AdvancedJsonOfAsync("u1");

        Assert.Equal(afterFirstRun, afterSecondRun);
    }

    [Fact]
    public async Task Does_not_touch_other_advanced_settings_in_the_same_json_blob()
    {
        await SeedUserPolicyAsync("u1", new AiAdvancedSettings
        {
            MaxTokens = "4096",
            NamingConvention = "PascalCase",
            FkAction = "cascade",
            Temperature = "0.9",
        }.ToJson());

        await using (var db = Context())
            await AiAdvancedSettingsFixup.RunAsync(db, NullLogger.Instance);

        var settings = AiAdvancedSettings.Parse(await AdvancedJsonOfAsync("u1"));

        Assert.Equal("PascalCase", settings.NamingConvention);
        Assert.Equal("cascade", settings.FkAction);
        Assert.Equal("0.9", settings.Temperature);
        Assert.NotEqual("4096", settings.MaxTokens);
    }

    [Fact]
    public async Task Does_not_touch_a_deliberately_higher_value()
    {
        // Kullanıcı bilerek 8000 seçmiş — eski varsayılanın (4096) ÜSTÜNDE,
        // dokunulmamalı.
        await SeedUserPolicyAsync("u1", new AiAdvancedSettings { MaxTokens = "8000" }.ToJson());

        await using (var db = Context())
            await AiAdvancedSettingsFixup.RunAsync(db, NullLogger.Instance);

        Assert.Equal("8000", AiAdvancedSettings.Parse(await AdvancedJsonOfAsync("u1")).MaxTokens);
    }

    [Fact]
    public async Task Rows_with_no_advanced_json_are_left_alone()
    {
        await SeedUserPolicyAsync("u1", advancedJson: null);

        await using (var db = Context())
            await AiAdvancedSettingsFixup.RunAsync(db, NullLogger.Instance);

        Assert.Null(await AdvancedJsonOfAsync("u1"));
    }
}
