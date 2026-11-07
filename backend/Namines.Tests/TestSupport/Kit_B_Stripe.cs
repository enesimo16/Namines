using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.Billing.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.Identity.Infrastructure.Persistence;

namespace Namines.Tests.TestSupport;

/// <summary>
/// Stripe ağ çağrısının sahtesi: webhook testleri gerçek Stripe'a çıkmaz. Çağrı sayısı tutulur ki
/// "olay tekrar işlenmedi" iddiası dış çağrı üzerinden de doğrulanabilsin.
/// </summary>
internal sealed class FakeStripeGateway : IStripeGateway
{
    private int _calls;

    public FakeStripeGateway(DateTime? periodEnd = null, string? priceId = null)
    {
        PeriodEnd = periodEnd ?? DateTime.UtcNow.AddDays(30);
        PriceId = priceId;
    }

    public DateTime? PeriodEnd { get; }
    public string? PriceId { get; }
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>İlk N çağrı patlar: Stripe ağ hatasının webhook'u 500'e düşürüp geri aldığını sınamak için.</summary>
    public int FailFirst { get; set; }

    public Task<StripeSubscriptionInfo> GetSubscriptionAsync(string subscriptionId, CancellationToken ct)
    {
        var call = Interlocked.Increment(ref _calls);
        if (call <= FailFirst) throw new HttpRequestException("stripe unreachable");
        return Task.FromResult(new StripeSubscriptionInfo(PeriodEnd, PriceId));
    }
}

/// <summary>
/// Kimlik dizinini sarar. İki işi var: (1) adı verilen yöntemde patlar, "işleyici başarısız" durumunu gerçek
/// servisi değiştirmeden üretir; (2) SQLite'ta <c>SetUserTypeAsync</c>'i aynı anlamla elle çalıştırır, çünkü
/// üretimdeki ExecuteUpdate içindeki <c>Guid.NewGuid()</c> yalnızca Npgsql'de SQL'e çevrilir (gen_random_uuid).
/// PostgreSQL testleri sarmalayıcı OLMADAN gerçek yolu sınar.
/// </summary>
internal class FailingIdentityProxy : System.Reflection.DispatchProxy
{
    private IIdentityDirectory _inner = null!;
    private AuthDbContext? _db;
    private string? _failOn;

    public static IIdentityDirectory Wrap(IIdentityDirectory inner, AuthDbContext? db, string? failOnMethod)
    {
        var proxy = Create<IIdentityDirectory, FailingIdentityProxy>();
        var typed = (FailingIdentityProxy)(object)proxy;
        typed._inner = inner;
        typed._db = db;
        typed._failOn = failOnMethod;
        return proxy;
    }

    protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name == _failOn) throw new InvalidOperationException("forced failure in " + _failOn);
        if (targetMethod.Name == nameof(IIdentityDirectory.SetUserTypeAsync) && _db is not null && _db.Database.IsSqlite())
        {
            var userId = (string)args![0]!;
            var type = (Namines.Core.Models.Auth.UserType)args[1]!;
            var stamp = Guid.NewGuid().ToString();
            return _db.Users.Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Type, type).SetProperty(u => u.ConcurrencyStamp, stamp), (CancellationToken)args[2]!);
        }
        try { return targetMethod.Invoke(_inner, args); }
        catch (System.Reflection.TargetInvocationException ex) { throw ex.InnerException!; }
    }
}

/// <summary>Webhook testlerinin ortak kurulumu: imza üretimi, JSON gövdeleri, gerçek servisle denetleyici.</summary>
internal static class KitBStripe
{
    public const string Secret = "whsec_test_secret_for_unit_tests";

    /// <summary>
    /// Stripe'ın imza başlığı: <c>t=&lt;unix&gt;,v1=&lt;HMAC-SHA256(t + "." + gövde)&gt;</c>. Doğrulamayı SDK yapıyor;
    /// elle hesaplamak testin kanıt değerini korur (SDK'yı atlatmak uçun kendisini sınamazdı).
    /// </summary>
    public static string SignatureHeader(string payload, DateTime signedAt, string secret = Secret)
    {
        var timestamp = ((long)(signedAt - DateTime.UnixEpoch).TotalSeconds).ToString(CultureInfo.InvariantCulture);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
        return $"t={timestamp},v1={signature}";
    }

    public static IConfiguration Config(string? secret = Secret, IDictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>();
        if (secret is not null) values["Stripe:WebhookSecret"] = secret;
        if (extra is not null) foreach (var pair in extra) values[pair.Key] = pair.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    public static StripeWebhookService Service(
        AuthDbContext db, IConfiguration config, IStripeGateway? gateway = null, IIdentityDirectory? identity = null)
    {
        var kit = TestModules.For(db);
        return new StripeWebhookService(
            kit.Billing, identity ?? FailingIdentityProxy.Wrap(kit.Identity, db, failOnMethod: null), kit.Transaction, config, gateway ?? new FakeStripeGateway(),
            NullLogger<StripeWebhookService>.Instance);
    }

    /// <summary>Gövde ve başlıkla kurulmuş denetleyici: ham gövde okuma yolu da sınanır.</summary>
    public static StripeWebhookController Controller(IStripeWebhookService service, string payload, string? signatureHeader)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        httpContext.Request.ContentType = "application/json";
        if (signatureHeader is not null) httpContext.Request.Headers["Stripe-Signature"] = signatureHeader;
        return new StripeWebhookController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    /// <summary>Bir <c>customer.subscription.*</c> olayının JSON gövdesi (isteğe bağlı tek fiyat kalemiyle).</summary>
    public static string SubscriptionEventJson(
        string eventId, string type, string status, DateTime created, string customerId, string subscriptionId = "sub_1", string? priceId = null)
    {
        var items = priceId is null
            ? @"{ ""object"": ""list"", ""data"": [] }"
            : $@"{{ ""object"": ""list"", ""data"": [ {{ ""id"": ""si_1"", ""object"": ""subscription_item"", ""price"": {{ ""id"": ""{priceId}"", ""object"": ""price"" }} }} ] }}";
        return $@"{{
  ""id"": ""{eventId}"",
  ""object"": ""event"",
  ""api_version"": ""2024-06-20"",
  ""livemode"": false,
  ""pending_webhooks"": 1,
  ""request"": null,
  ""type"": ""{type}"",
  ""created"": {(long)(created - DateTime.UnixEpoch).TotalSeconds},
  ""data"": {{
    ""object"": {{
      ""id"": ""{subscriptionId}"",
      ""object"": ""subscription"",
      ""customer"": ""{customerId}"",
      ""status"": ""{status}"",
      ""current_period_end"": {(long)(created.AddDays(30) - DateTime.UnixEpoch).TotalSeconds},
      ""items"": {items}
    }}
  }}
}}";
    }

    /// <summary>Bir <c>checkout.session.completed</c> olayının JSON gövdesi.</summary>
    public static string CheckoutCompletedJson(
        string eventId, DateTime created, string customerId, string userId, string subscriptionId)
    {
        return $@"{{
  ""id"": ""{eventId}"",
  ""object"": ""event"",
  ""api_version"": ""2024-06-20"",
  ""livemode"": false,
  ""pending_webhooks"": 1,
  ""request"": null,
  ""type"": ""checkout.session.completed"",
  ""created"": {(long)(created - DateTime.UnixEpoch).TotalSeconds},
  ""data"": {{
    ""object"": {{
      ""id"": ""cs_test_1"",
      ""object"": ""checkout.session"",
      ""customer"": ""{customerId}"",
      ""client_reference_id"": ""{userId}"",
      ""subscription"": ""{subscriptionId}"",
      ""mode"": ""subscription""
    }}
  }}
}}";
    }

    public static int StatusOf(IActionResult result) => Assert.IsAssignableFrom<ObjectResult>(result).StatusCode ?? 200;

    public static string BodyOf(IActionResult result) => Assert.IsAssignableFrom<ObjectResult>(result).Value!.ToString()!;
}
