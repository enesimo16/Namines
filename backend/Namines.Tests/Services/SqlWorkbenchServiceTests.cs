using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Services;

/// <summary>
/// F-01 (sorgu geçmişi) ve F-02 (kaydedilmiş sorgular).
///
/// <b>Testlerin ağırlığı GİZLİLİKTE:</b> Bu iki tablo, ürünün başka hiçbir
/// yerinde saklanmayan bir şeyi saklıyor — ham SQL metni. O metin
/// <c>WHERE email = '…'</c> içerebilir. Yani buradaki bir sızıntı, denetim
/// kaydının bilerek kaçındığı riski geri getirir.
///
/// Kilitlenen üç şey:
/// 1. Kullanıcı A, kullanıcı B'nin geçmişini/kayıtlarını GÖREMEZ.
/// 2. Kullanıcı A, id'sini bilse bile B'nin kaydını SİLEMEZ.
/// 3. Geçmiş sınırsız BÜYÜMEZ (100 kayıt) — sonsuza kadar biriken hassas metin.
/// </summary>
public sealed class SqlWorkbenchServiceTests : IAsyncLifetime
{
    private const string Project = "p1";
    private const string OtherProject = "p2";
    private const string UserA = "user-a";
    private const string UserB = "user-b";

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

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    /// <summary>Her çağrı KENDİ context'iyle: gerçek istekler de öyle.</summary>
    private SqlWorkbenchService NewService() => new(new AuthDbContext(_options));

    private static CancellationToken Ct => CancellationToken.None;

    // ── Geçmiş ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Calistirilan_sorgu_gecmise_yazilir()
    {
        await NewService().RecordAsync(Project, UserA, "SELECT 1", true, 1, null, 12, Ct);

        var history = await NewService().GetHistoryAsync(Project, UserA, 50, Ct);

        var item = Assert.Single(history);
        Assert.Equal("SELECT 1", item.Sql);
        Assert.True(item.Succeeded);
        Assert.Equal(1, item.RowCount);
        Assert.Equal(12, item.DurationMs);
    }

    /// <summary>
    /// Başarısız sorgu da kaydedilmeli — kullanıcının aradığı çoğu zaman tam
    /// odur ("hata veren sorguyu düzeltip tekrar deneyeceğim").
    /// </summary>
    [Fact]
    public async Task Basarisiz_sorgu_da_hata_mesajiyla_kaydedilir()
    {
        await NewService().RecordAsync(
            Project, UserA, "SELECT * FROM yok", false, 0, "relation \"yok\" does not exist", 4, Ct);

        var item = Assert.Single(await NewService().GetHistoryAsync(Project, UserA, 50, Ct));

        Assert.False(item.Succeeded);
        Assert.Contains("does not exist", item.ErrorMessage);
    }

    [Fact]
    public async Task Baskasinin_gecmisi_GORUNMEZ()
    {
        await NewService().RecordAsync(Project, UserB, "SELECT secret FROM customers", true, 1, null, 1, Ct);

        var history = await NewService().GetHistoryAsync(Project, UserA, 50, Ct);

        Assert.Empty(history);
    }

    [Fact]
    public async Task Baska_projenin_gecmisi_GORUNMEZ()
    {
        await NewService().RecordAsync(OtherProject, UserA, "SELECT 1", true, 1, null, 1, Ct);

        Assert.Empty(await NewService().GetHistoryAsync(Project, UserA, 50, Ct));
    }

    [Fact]
    public async Task En_yeni_kayit_ustte()
    {
        await NewService().RecordAsync(Project, UserA, "ilk", true, 0, null, 1, Ct);
        await Task.Delay(5, Ct);
        await NewService().RecordAsync(Project, UserA, "ikinci", true, 0, null, 1, Ct);

        var history = await NewService().GetHistoryAsync(Project, UserA, 50, Ct);

        Assert.Equal("ikinci", history[0].Sql);
    }

    /// <summary>
    /// Saklama sınırı. Sınırsız büyüyen bir tablo, sonsuza kadar biriken hassas
    /// metin demek — ve kimse 500 sorgu öncesine bakmıyor.
    /// </summary>
    [Fact]
    public async Task Gecmis_100_kayitta_budaniyor()
    {
        var service = NewService();
        for (var i = 0; i < SqlQueryHistoryEntry.RetentionPerUserPerProject + 15; i++)
            await service.RecordAsync(Project, UserA, $"SELECT {i}", true, 0, null, 1, Ct);

        await using var db = new AuthDbContext(_options);
        var stored = await db.SqlQueryHistory
            .CountAsync(h => h.UserId == UserA && h.ProjectId == Project, Ct);

        Assert.Equal(SqlQueryHistoryEntry.RetentionPerUserPerProject, stored);
    }

    /// <summary>
    /// Budama BAŞKA kullanıcının kaydına dokunmamalı. `Skip` sorgusundan
    /// `UserId` filtresi düşerse test buradan kırılır.
    /// </summary>
    [Fact]
    public async Task Budama_baskasinin_kaydini_silmez()
    {
        await NewService().RecordAsync(Project, UserB, "B'nin sorgusu", true, 0, null, 1, Ct);

        var service = NewService();
        for (var i = 0; i < SqlQueryHistoryEntry.RetentionPerUserPerProject + 5; i++)
            await service.RecordAsync(Project, UserA, $"SELECT {i}", true, 0, null, 1, Ct);

        Assert.Single(await NewService().GetHistoryAsync(Project, UserB, 50, Ct));
    }

    [Fact]
    public async Task Cok_uzun_SQL_kirpilir_ve_kirpildigi_SOYLENIR()
    {
        var longSql = new string('x', SqlQueryHistoryEntry.MaxSqlLength + 500);

        await NewService().RecordAsync(Project, UserA, longSql, true, 0, null, 1, Ct);

        var item = Assert.Single(await NewService().GetHistoryAsync(Project, UserA, 50, Ct));
        Assert.True(item.Truncated);
        Assert.Equal(SqlQueryHistoryEntry.MaxSqlLength, item.Sql.Length);
    }

    [Fact]
    public async Task Gecmis_temizlenince_yalnizca_cagiranin_kaydi_gider()
    {
        await NewService().RecordAsync(Project, UserA, "A", true, 0, null, 1, Ct);
        await NewService().RecordAsync(Project, UserB, "B", true, 0, null, 1, Ct);

        var deleted = await NewService().ClearHistoryAsync(Project, UserA, Ct);

        Assert.Equal(1, deleted);
        Assert.Empty(await NewService().GetHistoryAsync(Project, UserA, 50, Ct));
        Assert.Single(await NewService().GetHistoryAsync(Project, UserB, 50, Ct));
    }

    // ── Kaydedilmiş sorgular ────────────────────────────────────────────────

    [Fact]
    public async Task Sorgu_kaydedilir_ve_listelenir()
    {
        await NewService().SaveAsync(Project, UserA, "Aylık rapor", "SELECT 1", Ct);

        var item = Assert.Single(await NewService().GetSavedAsync(Project, UserA, Ct));
        Assert.Equal("Aylık rapor", item.Name);
    }

    /// <summary>
    /// Aynı ad = GÜNCELLEME, ikinci kayıt DEĞİL. Hata dönmek kullanıcıyı önce
    /// silmeye zorlardı; "kaydet"e ikinci kez basmak en doğal düzeltme hareketi.
    /// </summary>
    [Fact]
    public async Task Ayni_ad_ikinci_kayit_degil_GUNCELLEME()
    {
        var first = await NewService().SaveAsync(Project, UserA, "Rapor", "SELECT 1", Ct);
        var second = await NewService().SaveAsync(Project, UserA, "Rapor", "SELECT 2", Ct);

        Assert.Equal(first.Id, second.Id);
        var item = Assert.Single(await NewService().GetSavedAsync(Project, UserA, Ct));
        Assert.Equal("SELECT 2", item.Sql);
    }

    /// <summary>Ad boşlukla farklılaştırılarak ikinci kayıt yapılamaz.</summary>
    [Fact]
    public async Task Ad_bosluklari_kirpilarak_karsilastirilir()
    {
        await NewService().SaveAsync(Project, UserA, "Rapor", "SELECT 1", Ct);
        await NewService().SaveAsync(Project, UserA, "  Rapor  ", "SELECT 2", Ct);

        Assert.Single(await NewService().GetSavedAsync(Project, UserA, Ct));
    }

    [Fact]
    public async Task Ayni_ad_BASKA_kullanicida_serbest()
    {
        await NewService().SaveAsync(Project, UserA, "Rapor", "SELECT 1", Ct);
        await NewService().SaveAsync(Project, UserB, "Rapor", "SELECT 2", Ct);

        Assert.Single(await NewService().GetSavedAsync(Project, UserA, Ct));
        Assert.Single(await NewService().GetSavedAsync(Project, UserB, Ct));
    }

    [Fact]
    public async Task Baskasinin_kaydedilmis_sorgusu_GORUNMEZ()
    {
        await NewService().SaveAsync(Project, UserB, "B'nin raporu", "SELECT 1", Ct);

        Assert.Empty(await NewService().GetSavedAsync(Project, UserA, Ct));
    }

    /// <summary>
    /// Id'yi bilmek yetmiyor. Silme koşulunda <c>UserId</c> olmasaydı, listeden
    /// hiç göremeyeceğiniz bir kaydı silebilirdiniz.
    /// </summary>
    [Fact]
    public async Task Baskasinin_kaydi_id_bilinse_bile_SILINEMEZ()
    {
        var mine = await NewService().SaveAsync(Project, UserB, "B'nin raporu", "SELECT 1", Ct);

        var deleted = await NewService().DeleteSavedAsync(Project, UserA, mine.Id, Ct);

        Assert.False(deleted);
        Assert.Single(await NewService().GetSavedAsync(Project, UserB, Ct));
    }

    [Fact]
    public async Task Kendi_kaydi_silinebilir()
    {
        var item = await NewService().SaveAsync(Project, UserA, "Rapor", "SELECT 1", Ct);

        Assert.True(await NewService().DeleteSavedAsync(Project, UserA, item.Id, Ct));
        Assert.Empty(await NewService().GetSavedAsync(Project, UserA, Ct));
    }

    /// <summary>
    /// Mesaj kullaniciya AYNEN gosteriliyor, o yuzden icinde kodun degisken adi
    /// OLMAMALI. `ArgumentException`'a `paramName` verilirse .NET mesajin sonuna
    /// " (Parameter 'name')" ekliyor ve kullanici bunu okuyordu.
    /// </summary>
    [Fact]
    public async Task Bos_ad_reddedilir_ve_mesaj_parametre_adi_SIZDIRMAZ()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewService().SaveAsync(Project, UserA, "   ", "SELECT 1", Ct));

        Assert.Equal("A name is required.", ex.Message);
        Assert.DoesNotContain("Parameter", ex.Message);
    }

    [Fact]
    public async Task Sinir_asilinca_aciklayici_hata_doner()
    {
        var service = NewService();
        for (var i = 0; i < SavedQuery.MaxPerUserPerProject; i++)
            await service.SaveAsync(Project, UserA, $"q{i}", "SELECT 1", Ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewService().SaveAsync(Project, UserA, "bir fazla", "SELECT 1", Ct));

        Assert.Contains("Delete one", ex.Message);
    }
}
