using Namines.Billing.Domain;
using Namines.Identity.Infrastructure.Persistence;
using Namines.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Models.Auth;
using Namines.Desk.Application;
using Namines.Desk.Infrastructure.Persistence;
using Namines.Desk.Services;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;
using Namines.Desk.Domain;
using Namines.Identity.Domain;

namespace Namines.Tests.Services;

public sealed class DataRetentionServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;
    private DbContextOptions<DeskDbContext> _deskOptions = null!;
    private static readonly DateTime Now = new(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        // Test yalnızca saklama mantığını sınıyor; FK zincirini kurmak gürültü olurdu.
        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys=OFF;";
            await cmd.ExecuteNonQueryAsync();
        }
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;
        _deskOptions = new DbContextOptionsBuilder<DeskDbContext>().UseSqlite(_connection).Options;
        await using var db = new AuthDbContext(_options);
        await db.EnsureModulesCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static DataRetentionService.RetentionOptions Defaults() =>
        DataRetentionService.RetentionOptions.From(new ConfigurationBuilder().Build());

    [Fact]
    public async Task Purge_deletes_only_expired_rows_per_table()
    {
        await using (var db = new AuthDbContext(_options))
        await using (var deskDb = new DeskDbContext(_deskOptions))
        {
            db.Gateway().GatewayAuditEntries.Add(new GatewayAuditEntry { ProjectId = "p", CreatedAt = Now.AddDays(-400) });
            db.Gateway().GatewayAuditEntries.Add(new GatewayAuditEntry { ProjectId = "p", CreatedAt = Now.AddDays(-10) });
            db.Billing().StripeWebhookEvents.Add(new StripeWebhookEvent { Id = "evt_old", EventType = "x", ProcessedAt = Now.AddDays(-200) });
            db.Billing().StripeWebhookEvents.Add(new StripeWebhookEvent { Id = "evt_new", EventType = "x", ProcessedAt = Now.AddDays(-1) });
            deskDb.DeskHandoffTokens.Add(new DeskHandoffToken { TokenHash = "a", UserId = "u", ExpiresAt = Now.AddDays(-30) });
            deskDb.DeskHandoffTokens.Add(new DeskHandoffToken { TokenHash = "b", UserId = "u", ExpiresAt = Now.AddMinutes(5) });
            db.TeamInvites.Add(new TeamInvite { TokenHash = "i1", OrganizationId = "o", CreatedByUserId = "u", ExpiresAt = Now.AddDays(-30) });
            db.TeamInvites.Add(new TeamInvite { TokenHash = "i2", OrganizationId = "o", CreatedByUserId = "u", ExpiresAt = Now.AddDays(-30), AcceptedAt = Now.AddDays(-31) });
            await db.SaveChangesAsync();
            await deskDb.SaveChangesAsync();
        }

        await using (var db = new AuthDbContext(_options))
        await using (var deskDb = new DeskDbContext(_deskOptions))
        {
            var deskApp = new DeskApplicationService(deskDb, new DeskHandoffTokenValidator());
            var r = await DataRetentionService.PurgeAsync(
                Defaults(), NullLogger.Instance, Now, TimeSpan.Zero, CancellationToken.None, TestModules.For(db).OpsRetention, TestModules.For(db).GatewayMaintenance,
                TestModules.For(db).BillingAccounts, TestModules.For(db).GovernanceAudit, TestModules.For(db).IdentityMaintenance(), deskApp);
            Assert.Equal(1, r["GatewayAuditEntries"]);
            Assert.Equal(1, r["StripeWebhookEvents"]);
            Assert.Equal(1, r["desk_handoff_tokens"]);
            Assert.Equal(1, r["TeamInvites"]);
        }

        await using var check = new AuthDbContext(_options);
        await using var checkDesk = new DeskDbContext(_deskOptions);
        Assert.Equal(1, await check.Gateway().GatewayAuditEntries.CountAsync());
        Assert.Equal("evt_new", (await check.Billing().StripeWebhookEvents.SingleAsync()).Id);
        Assert.Equal("b", (await checkDesk.DeskHandoffTokens.SingleAsync()).TokenHash);
        Assert.Equal("i2", (await check.TeamInvites.SingleAsync()).TokenHash);
    }

    [Fact]
    public async Task Purge_batches_over_batch_size()
    {
        await using (var db = new AuthDbContext(_options))
        {
            for (var i = 0; i < DataRetentionService.BatchSize + 50; i++)
                db.Gateway().GatewayAuditEntries.Add(new GatewayAuditEntry { ProjectId = "p", CreatedAt = Now.AddDays(-999) });
            await db.SaveChangesAsync();
        }
        await using var db2 = new AuthDbContext(_options);
        await using var deskDb2 = new DeskDbContext(_deskOptions);
        var deskApp = new DeskApplicationService(deskDb2, new DeskHandoffTokenValidator());
        var r = await DataRetentionService.PurgeAsync(
            Defaults(), NullLogger.Instance, Now, TimeSpan.Zero, CancellationToken.None, TestModules.For(db2).OpsRetention, TestModules.For(db2).GatewayMaintenance,
            TestModules.For(db2).BillingAccounts, TestModules.For(db2).GovernanceAudit, TestModules.For(db2).IdentityMaintenance(), deskApp);
        Assert.Equal(DataRetentionService.BatchSize + 50, r["GatewayAuditEntries"]);
        Assert.Equal(0, await db2.Gateway().GatewayAuditEntries.CountAsync());
    }

    [Fact]
    public void Usage_event_retention_cannot_be_shortened_below_billing_floor_but_can_be_disabled()
    {
        var short_ = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Retention:UsageEventDays"] = "30" }).Build();
        Assert.Equal(DataRetentionService.MinUsageEventDays, DataRetentionService.RetentionOptions.From(short_).UsageEventDays);
        var off = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Retention:UsageEventDays"] = "0" }).Build();
        Assert.Equal(0, DataRetentionService.RetentionOptions.From(off).UsageEventDays);
        Assert.Equal(0, Defaults().FeedbackDays); // varsayılan: feedback saklanır
    }

    [Fact]
    public void Pool_defaults_apply_but_explicit_connection_string_values_win()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:MaxPoolSize"] = "77" }).Build();
        var a = new Npgsql.NpgsqlConnectionStringBuilder(ControlDbConnection.ApplyPoolDefaults("Host=h;Database=d", cfg));
        Assert.Equal(77, a.MaxPoolSize);
        Assert.Equal(30, a.CommandTimeout);
        Assert.Equal(30, a.KeepAlive);
        var b = new Npgsql.NpgsqlConnectionStringBuilder(ControlDbConnection.ApplyPoolDefaults("Host=h;Database=d;Maximum Pool Size=5;Command Timeout=90", cfg));
        Assert.Equal(5, b.MaxPoolSize);
        Assert.Equal(90, b.CommandTimeout);
    }
}
