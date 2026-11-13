using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Namines.AI.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Models.Auth;
using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.API.Middleware
{
    /// <summary>
    /// HTTP ile AI kotası arasındaki ADAPTÖR. Hangi ucun AI özelliği olduğu ve ne kadar tuttuğu AI modülünde
    /// (<see cref="AiFeatureCatalog"/>), kota defteri Billing'de (<see cref="IAiQuota"/>), kullanıcı politikası
    /// Governance'ta (<see cref="IAiPolicies"/>). Burada yalnızca istek yaşam döngüsü var: rezerve et, çağır,
    /// başarısızsa iade et, başarılıysa gerçek ölçümle uzlaştır.
    /// </summary>
    public class AIQuotaMiddleware
    {
        private readonly RequestDelegate _next;

        public AIQuotaMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var feature = AiFeatureCatalog.Resolve(context.Request.Path.Value);
            if (feature is null)
            {
                await _next(context);
                return;
            }

            // JWT auth check — deterministic branch'ten ÖNCE olmalı. Aksi halde
            // enhanceWithAI:false gönderen anonim istekler auth'u tamamen atlayıp
            // sunucu-taraflı üretim/paketleme tetikleyebiliyordu (unauth compute/disk DoS).
            var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                await WriteAuthRequiredAsync(context, "AI features require authentication.");
                return;
            }

            // enhanceWithAI:false ile gelen mock/seed/coder sandbox isteği AI çağırmaz: kota yok.
            if (feature.Deterministic && await RequestsDeterministicAsync(context))
            {
                await _next(context);
                return;
            }

            // Yetim oturumda veritabanı işlemi yapılmasın (yabancı anahtar hataları).
            var userExists = (await context.RequestServices.GetRequiredService<IIdentityDirectory>().GetSessionStateAsync(userId)).Exists;
            if (!userExists)
            {
                await WriteAuthRequiredAsync(context, "User session is invalid. Please log in again.");
                return;
            }

            // Kota kuralının TEK sahibi AiQuotaService (Billing). Havuz/tavan okuma, gün sıfırlama ve tavan
            // normalizasyonu orada; burada tekrarlanmıyor ki Gateway'in /query/nl ucuyla ayrışmasın.
            var quotaService = context.RequestServices.GetRequiredService<IAiQuota>();
            await quotaService.EnsureAsync(userId);

            var policy = await context.RequestServices.GetRequiredService<IAiPolicies>().FindAsync(userId);
            var selectedMode = AiFeatureCatalog.ModeFor(feature, policy);
            var estTokens = feature.EstimatedTokens;

            // DefaultNamines → zaten ücretsiz/yerel; havuzdan düşme.
            if (selectedMode == AIMode.DefaultNamines)
            {
                context.Items["FallbackToLocal"] = true;
                await _next(context);
                return;
            }

            // ── Bütçe ÇAĞRIDAN ÖNCE ve ATOMİK rezerve edilir ──
            // Kullanıcı tavanı, paylaşılan havuz ve ekip havuzu tek işlemde koşullu artırılır; sığmayan istek
            // yerel yedeğe düşer (BLOKLAMA YOK), başarısız yanıt rezervasyonu iade eder.
            //
            // Kendi bütçesini ucun içinde rezerve edip uzlaştıran özellik (şema üretimi: tur çarpanı, gerçek
            // kullanım) için burada ikinci kez düşmek aynı işi İKİ KEZ ücretlendirirdi; yalnızca karar için okunur.
            var decision = feature.SelfReserving
                ? await quotaService.CheckAsync(userId, estTokens)
                : await quotaService.TryReserveAsync(userId, estTokens);

            if (decision != AiQuotaDecision.Allowed)
            {
                context.Items["FallbackToLocal"] = true;
                context.Response.Headers["X-AI-Fallback"] = decision switch
                {
                    AiQuotaDecision.PoolExhausted => "pool-exhausted",
                    AiQuotaDecision.TeamExhausted => "team-exhausted",
                    _ => "quota-exhausted",
                };
                await _next(context);
                return;
            }

            if (feature.SelfReserving)
            {
                await _next(context);
                if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300 && !quotaService.HasReservedInScope)
                    await quotaService.ConsumeAsync(userId, estTokens);
                return;
            }

            context.Items["AiQuotaReservedTokens"] = estTokens;
            try
            {
                await _next(context);
            }
            catch
            {
                // Çağrı patladı: kullanıcı hiçbir şey almadı, bütçesinden düşmemeli.
                await quotaService.RefundAsync(userId, estTokens, CancellationToken.None);
                throw;
            }

            // Yalnızca BAŞARILI istek harcanmış sayılır.
            if (context.Response.StatusCode < 200 || context.Response.StatusCode >= 300)
            {
                await quotaService.RefundAsync(userId, estTokens, CancellationToken.None);
            }
            else if (ActualTokens(context) is int actualTokens && actualTokens != estTokens)
            {
                // 0 = uç modele gitmediğini açıkça söyledi: rezervasyonun tamamı iade. (Billing'in ReconcileAsync'i 0'ı
                // bilinçli olarak "bilgi yok" sayar; o savunmayı gevşetmek yerine açık sinyal burada iadeye çevrilir.)
                if (actualTokens == 0)
                    await quotaService.RefundAsync(userId, estTokens, CancellationToken.None);
                else
                    // Gerçek kullanım biliniyorsa tahmin gerçeğe çekilir (fazla ayrılan iade, eksik ayrılan eklenir).
                    await quotaService.ReconcileAsync(userId, estTokens, actualTokens, CancellationToken.None);
            }
        }

        /// <summary>
        /// İsteğin GERÇEK token kullanımı: önce ucun açıkça bildirdiği değer, yoksa sağlayıcı katmanının
        /// istek kapsamlı ölçere (<see cref="Namines.Core.Interfaces.IAiUsageTracker"/>) yazdığı toplam.
        ///
        /// 14.2D-2 kabul edilen borç 4: yalnızca <c>schema/generate</c> gerçek kullanımı bildiriyordu; diğer AI
        /// uçları (aidba, smartseed, coderai, revise, mockdata, documentation, migration, reverseengineer, voice)
        /// sağlayıcı <c>usage</c> bloğunu ölçere yazdığı hâlde sabit tahminle ücretlendiriliyordu.
        /// Ölçüm yoksa (yerel/ölçümsüz sağlayıcı) tahmin kalır: eksik ölçümü "0 token" saymak bedava kullanım olurdu.
        /// </summary>
        internal static int? ActualTokens(HttpContext context)
        {
            if (context.Items["AiQuotaActualTokens"] is int reported && reported >= 0)
                return reported;
            var tracker = context.RequestServices.GetService<Namines.Core.Interfaces.IAiUsageTracker>();
            if (tracker is null) return null;
            if (tracker.HasMeasurement) return tracker.TotalTokens;
            // Uç modele gitmediğini AÇIKÇA söyledi (önbellek isabeti, boş şema, deterministik üretici): ücret yok.
            return tracker.ServedWithoutModel ? 0 : null;
        }

        private static async Task<bool> RequestsDeterministicAsync(HttpContext context)
        {
            context.Request.EnableBuffering();
            using var reader = new System.IO.StreamReader(context.Request.Body, System.Text.Encoding.UTF8, true, 1024, true);
            var bodyText = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            if (string.IsNullOrEmpty(bodyText)) return false;
            try
            {
                using var doc = JsonDocument.Parse(bodyText);
                return (doc.RootElement.TryGetProperty("enhanceWithAI", out var enhance) ||
                        doc.RootElement.TryGetProperty("EnhanceWithAI", out enhance))
                       && enhance.ValueKind == JsonValueKind.False;
            }
            catch (JsonException)
            {
                return false; // ayrıştırma sorununu denetleyici ele alır
            }
        }

        private static async Task WriteAuthRequiredAsync(HttpContext context, string message)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { code = "AUTH_REQUIRED", message }));
        }
    }
}
