using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Namines.API.Middleware;
using Namines.Core.Interfaces;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// 14.2D-2 kabul edilen borç 4: AI uçları sabit tahminle ücretlendiriliyordu. Middleware artık sağlayıcının
/// istek kapsamlı ölçere yazdığı GERÇEK kullanımla uzlaştırır; uç açıkça bildirirse o önceliklidir.
/// </summary>
public sealed class AiQuotaReconciliationTests
{
    private static HttpContext ContextWith(IAiUsageTracker tracker)
    {
        var services = new ServiceCollection().AddSingleton(tracker).BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services };
    }

    [Fact]
    public void Measured_provider_usage_is_used_when_the_endpoint_reports_nothing()
    {
        var tracker = new AiUsageTrackerForTests();
        tracker.Record(1200);
        tracker.Record(800);

        Assert.Equal(2000, AIQuotaMiddleware.ActualTokens(ContextWith(tracker)));
    }

    [Fact]
    public void An_explicit_endpoint_report_wins_over_the_tracker()
    {
        var tracker = new AiUsageTrackerForTests();
        tracker.Record(9999);
        var context = ContextWith(tracker);
        context.Items["AiQuotaActualTokens"] = 1500;

        Assert.Equal(1500, AIQuotaMiddleware.ActualTokens(context));
    }

    [Fact]
    public void No_measurement_keeps_the_estimate_instead_of_charging_zero()
    {
        // Ölçümsüz sağlayıcı (ör. yerel) "0 token" sayılırsa kullanım bedava olurdu.
        Assert.Null(AIQuotaMiddleware.ActualTokens(ContextWith(new AiUsageTrackerForTests())));
    }

    [Fact]
    public void An_endpoint_that_says_it_never_called_a_model_is_charged_nothing()
    {
        // Önbellek isabeti, boş şema, deterministik üretici: tahmin (ör. 3500) iade edilmeli.
        var tracker = new AiUsageTrackerForTests();
        tracker.RecordServedWithoutModel();

        Assert.Equal(0, AIQuotaMiddleware.ActualTokens(ContextWith(tracker)));
    }

    [Fact]
    public void A_real_measurement_wins_over_the_served_without_model_signal()
    {
        // Bir kısmı önbellekten, bir kısmı modelden geldiyse model kullanımı ücretlendirilir.
        var tracker = new AiUsageTrackerForTests();
        tracker.RecordServedWithoutModel();
        tracker.Record(700);

        Assert.Equal(700, AIQuotaMiddleware.ActualTokens(ContextWith(tracker)));
    }

    private sealed class AiUsageTrackerForTests : IAiUsageTracker
    {
        public int TotalTokens { get; private set; }
        public bool HasMeasurement { get; private set; }
        public bool ServedWithoutModel { get; private set; }
        public void Record(int totalTokens)
        {
            if (totalTokens <= 0) return;
            TotalTokens += totalTokens;
            HasMeasurement = true;
        }
        public void RecordServedWithoutModel() => ServedWithoutModel = true;
    }
}
