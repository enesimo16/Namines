using System;
using Namines.Core.Models.Auth;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// <see cref="VaultSchedule.IsDue"/> — zamanlamanın tek karar noktası.
///
/// Docker gerekmiyor: bu saf mantık ve her koşuda çalışıyor. Zamanlamanın
/// yanlış olması "yedek sessizce hiç alınmadı" demek, yani hatanın ancak
/// yedeğe gerçekten ihtiyaç duyulduğunda fark edildiği sınıf.
/// </summary>
public class VaultScheduleTests
{
    private static VaultSchedule Daily(int hourUtc = 3, DateTime? lastRun = null) => new()
    {
        ProjectId = "p1",
        Enabled = true,
        Cadence = VaultCadence.Daily,
        HourUtc = hourUtc,
        LastRunAt = lastRun,
    };

    [Fact]
    public void Disabled_schedule_never_runs()
    {
        var schedule = Daily();
        schedule.Enabled = false;

        Assert.False(schedule.IsDue(new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Does_not_run_before_the_configured_hour()
    {
        var schedule = Daily(hourUtc: 3);

        Assert.False(schedule.IsDue(new DateTime(2026, 1, 1, 2, 59, 0, DateTimeKind.Utc)));
        Assert.True(schedule.IsDue(new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Runs_only_once_per_day()
    {
        var today = new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc);
        var schedule = Daily(lastRun: today);

        // Aynı gün içinde ikinci bir kontrol yedek almamalı.
        Assert.False(schedule.IsDue(today.AddHours(5)));
        // Ertesi gün aynı saatte almalı.
        Assert.True(schedule.IsDue(today.AddDays(1)));
    }

    [Fact]
    public void Weekly_runs_only_on_its_day()
    {
        var schedule = new VaultSchedule
        {
            ProjectId = "p1",
            Enabled = true,
            Cadence = VaultCadence.Weekly,
            HourUtc = 3,
            DayOfWeek = (int)DayOfWeek.Monday,
        };

        // 2026-01-01 Perşembe; 2026-01-05 Pazartesi.
        Assert.False(schedule.IsDue(new DateTime(2026, 1, 1, 4, 0, 0, DateTimeKind.Utc)));
        Assert.True(schedule.IsDue(new DateTime(2026, 1, 5, 4, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Weekly_without_a_day_never_runs()
    {
        var schedule = new VaultSchedule
        {
            ProjectId = "p1",
            Enabled = true,
            Cadence = VaultCadence.Weekly,
            HourUtc = 3,
            DayOfWeek = null,
        };

        // Gün seçilmemiş haftalık ayar, "her gün" anlamına GELMEMELİ. Sunucu
        // bunu zaten reddediyor; burası ikinci hattı da tutuyor.
        Assert.False(schedule.IsDue(new DateTime(2026, 1, 5, 4, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Missed_runs_are_not_backfilled()
    {
        // Sunucu üç gün kapalı kaldı. Açılışta ÜÇ yedek değil, BİR yedek alınmalı:
        // geçmişi tamamlamak dolu bir diski daha da doldurmaktan başka işe yaramaz.
        var schedule = Daily(lastRun: new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc));
        var now = new DateTime(2026, 1, 4, 3, 0, 0, DateTimeKind.Utc);

        Assert.True(schedule.IsDue(now));

        schedule.LastRunAt = now;
        Assert.False(schedule.IsDue(now));
    }
}
