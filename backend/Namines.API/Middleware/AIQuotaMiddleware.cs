using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

namespace Namines.API.Middleware
{
    public class AIQuotaMiddleware
    {
        private readonly RequestDelegate _next;

        private static readonly HashSet<string> AIPathPrefixes = new()
        {
            "/api/aidba", "/api/smartseed", "/api/schema/generate",
            "/api/schema/revise", "/api/schema/mockdata", "/api/coderai",
            "/api/migration/parse", "/api/migration/generate",
            "/api/documentation/pdf", "/api/reverseengineer", "/api/voice"
        };

        public AIQuotaMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

            // Check if path matches any of the AI prefixes
            bool isAIEndpoint = false;
            foreach (var prefix in AIPathPrefixes)
            {
                if (path.StartsWith(prefix))
                {
                    isAIEndpoint = true;
                    break;
                }
            }

            if (!isAIEndpoint)
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
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                var authErr = new { code = "AUTH_REQUIRED", message = "AI features require authentication." };
                await context.Response.WriteAsync(JsonSerializer.Serialize(authErr));
                return;
            }

            // Check if it is a deterministic path requesting mock/seed/coder sandbox
            bool isDeterministicRequest = false;
            if (path.StartsWith("/api/smartseed") || path.StartsWith("/api/coderai"))
            {
                context.Request.EnableBuffering();
                using (var reader = new System.IO.StreamReader(context.Request.Body, System.Text.Encoding.UTF8, true, 1024, true))
                {
                    var bodyText = await reader.ReadToEndAsync();
                    context.Request.Body.Position = 0;
                    if (!string.IsNullOrEmpty(bodyText))
                    {
                        try
                        {
                            using (var doc = JsonDocument.Parse(bodyText))
                            {
                                if (doc.RootElement.TryGetProperty("enhanceWithAI", out var enhanceProp) ||
                                    doc.RootElement.TryGetProperty("EnhanceWithAI", out enhanceProp))
                                {
                                    if (enhanceProp.ValueKind == JsonValueKind.False)
                                    {
                                        isDeterministicRequest = true;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Let the controller handle any parsing issues
                        }
                    }
                }
            }

            if (isDeterministicRequest)
            {
                await _next(context);
                return;
            }

            // Get DB context from request services

            // Ensure user exists before performing database operations to avoid foreign key errors on orphaned sessions
            var userExists = (await context.RequestServices.GetRequiredService<Namines.BuildingBlocks.Contracts.IIdentityDirectory>().GetSessionStateAsync(userId)).Exists;
            if (!userExists)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                var errObj = new { code = "AUTH_REQUIRED", message = "User session is invalid. Please log in again." };
                await context.Response.WriteAsync(JsonSerializer.Serialize(errObj));
                return;
            }

            // ── Token havuzu yapılandırması ────────────────────────────────────────
            // Paylaşımlı günlük havuz (tüm kullanıcılar) + kullanıcı-başı günlük tavan.
            // Kayıtlı kullanıcı sayısına BÖLÜNMEZ (dormant hesaba token israf edilmez):
            // tüketim talep üzerine havuzdan düşülür; tek kullanıcı tavanı havuzu drenlemesini önler.
            // İleride config'ten havuzu büyütmek yeterli (100k → 1M).
            // Kota kuralının TEK sahibi AiQuotaService. Havuz/tavan okuma, gün
            // sıfırlama ve tavan normalizasyonu orada; burada tekrarlanmıyor ki
            // Gateway'in /query/nl ucuyla ayrışmasın (bkz. AiQuotaService).
            var quotaService = context.RequestServices.GetRequiredService<Namines.BuildingBlocks.Contracts.IAiQuota>();
            await quotaService.EnsureAsync(userId);

            // ── Özellik başına tahmini token maliyeti + kullanıcı politikası ──
            AIMode selectedMode = AIMode.Medium; // policy yoksa varsayılan
            int estTokens = 2500;

            var policy = await context.RequestServices.GetRequiredService<Namines.BuildingBlocks.Contracts.IAiPolicies>().FindAsync(userId);
            if (path.StartsWith("/api/aidba")) { estTokens = 3500; if (policy != null) selectedMode = policy.DbaAnalysis; }
            else if (path.StartsWith("/api/schema/revise")) { estTokens = 2500; if (policy != null) selectedMode = policy.SchemaRevision; }
            else if (path.StartsWith("/api/smartseed") || path.StartsWith("/api/schema/mockdata")) { estTokens = 2000; if (policy != null) selectedMode = policy.SmartSeed; }
            else if (path.StartsWith("/api/schema/generate")) { estTokens = 2500; if (policy != null) selectedMode = policy.SchemaGeneration; }
            else if (path.StartsWith("/api/reverseengineer")) { estTokens = 4500; if (policy != null) selectedMode = policy.Scaffolding; }
            else if (path.StartsWith("/api/coderai")) { estTokens = 6000; if (policy != null) selectedMode = policy.Scaffolding; }
            else if (path.StartsWith("/api/documentation")) { estTokens = 3000; if (policy != null) selectedMode = policy.Documentation; }
            else if (path.StartsWith("/api/migration")) { estTokens = 3000; if (policy != null) selectedMode = policy.Migration; }
            else if (path.StartsWith("/api/voice")) { estTokens = 1500; if (policy != null) selectedMode = policy.Voice; }

            // DefaultNamines → zaten ücretsiz/yerel; havuzdan düşme.
            if (selectedMode == AIMode.DefaultNamines)
            {
                context.Items["FallbackToLocal"] = true;
                await _next(context);
                return;
            }

            // ── Bütçe ÇAĞRIDAN ÖNCE ve ATOMİK rezerve edilir ──
            // Eskiden önce okunur (CheckAsync), yanıt başarılıysa sonradan düşülürdü (ConsumeAsync): bütçe tükenmeden önce
            // gelen N paralel istek hepsi "Allowed" görür, havuz ve ekip sayacı sınırsız artardı (spec: kota yarışsız).
            // Şimdi kullanıcı tavanı, paylaşılan havuz ve ekip havuzu tek işlemde koşullu artırılır; sığmayan istek
            // yerel yedeğe düşer (BLOKLAMA YOK), başarısız yanıt rezervasyonu iade eder.
            //
            // /api/schema/generate kendi bütçesini ucun içinde rezerve edip uzlaştırıyor (tur çarpanı, gerçek kullanım):
            // burada ikinci kez düşmek aynı işi İKİ KEZ ücretlendirirdi, o yol eskisi gibi yalnızca karar için okunur.
            var selfReserving = path.StartsWith("/api/schema/generate");
            Namines.BuildingBlocks.Contracts.AiQuotaDecision decision;
            if (selfReserving)
                decision = await quotaService.CheckAsync(userId, estTokens);
            else
                decision = await quotaService.TryReserveAsync(userId, estTokens);

            if (decision != Namines.BuildingBlocks.Contracts.AiQuotaDecision.Allowed)
            {
                context.Items["FallbackToLocal"] = true;
                context.Response.Headers["X-AI-Fallback"] = decision switch
                {
                    Namines.BuildingBlocks.Contracts.AiQuotaDecision.PoolExhausted => "pool-exhausted",
                    Namines.BuildingBlocks.Contracts.AiQuotaDecision.TeamExhausted => "team-exhausted",
                    _ => "quota-exhausted",
                };
                await _next(context);
                return;
            }

            if (selfReserving)
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
            else if (context.Items["AiQuotaActualTokens"] is int actualTokens && actualTokens >= 0 && actualTokens != estTokens)
            {
                // Uç gerçek kullanımı biliyorsa tahmin gerçeğe çekilir (fazla ayrılan iade, eksik ayrılan eklenir).
                await quotaService.ReconcileAsync(userId, estTokens, actualTokens, CancellationToken.None);
            }
        }
    }
}
