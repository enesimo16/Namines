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

            // BYOK bypass
            if (context.Items.ContainsKey("IsByok") && context.Items["IsByok"] is true)
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
            var db = context.RequestServices.GetRequiredService<AuthDbContext>();

            // Ensure user exists before performing database operations to avoid foreign key errors on orphaned sessions
            var userExists = await db.Users.AnyAsync(u => u.Id == userId);
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
            var config = context.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
            long dailyPool = long.TryParse(config?["AiPool:DailyTokenPool"], out var dp) ? dp : 100_000;
            int perUserCap = int.TryParse(config?["AiPool:PerUserDailyTokens"], out var pu) ? pu : 20_000;

            // ── Kullanıcı kotası (token bazlı) ──
            var quota = await db.UserAIQuotas.FirstOrDefaultAsync(q => q.UserId == userId);
            if (quota == null)
            {
                quota = new UserAIQuota
                {
                    UserId = userId,
                    DailyLimit = perUserCap,
                    DailyUsageCount = 0,
                    LastResetDate = DateTime.UtcNow
                };
                await db.UserAIQuotas.AddAsync(quota);
                await db.SaveChangesAsync();
            }
            else
            {
                // Eski yüzde-tabanlı satırları token tavanına normalize et.
                if (quota.DailyLimit != perUserCap) quota.DailyLimit = perUserCap;
                // Günlük reset (TR saati UTC+3)
                if (quota.LastResetDate.AddHours(3).Date < DateTime.UtcNow.AddHours(3).Date)
                {
                    quota.DailyUsageCount = 0;
                    quota.LastResetDate = DateTime.UtcNow;
                }
                db.UserAIQuotas.Update(quota);
                await db.SaveChangesAsync();
            }

            // ── Özellik başına tahmini token maliyeti + kullanıcı politikası ──
            AIMode selectedMode = AIMode.Medium; // policy yoksa varsayılan
            int estTokens = 2500;

            var policy = await db.UserAIPolicies.FirstOrDefaultAsync(p => p.UserId == userId);
            if (path.StartsWith("/api/aidba")) { estTokens = 3500; if (policy != null) selectedMode = policy.DbaAnalysis; }
            else if (path.StartsWith("/api/schema/revise")) { estTokens = 2500; if (policy != null) selectedMode = policy.SchemaRevision; }
            else if (path.StartsWith("/api/smartseed") || path.StartsWith("/api/schema/mockdata")) { estTokens = 2000; if (policy != null) selectedMode = policy.SmartSeed; }
            else if (path.StartsWith("/api/schema/generate")) { estTokens = 2500; if (policy != null) selectedMode = policy.SchemaGeneration; }
            else if (path.StartsWith("/api/reverseengineer")) { estTokens = 4500; if (policy != null) selectedMode = policy.Scaffolding; }
            else if (path.StartsWith("/api/coderai")) { estTokens = 6000; if (policy != null) selectedMode = policy.Scaffolding; }
            else if (path.StartsWith("/api/documentation")) { estTokens = 3000; if (policy != null) selectedMode = policy.Documentation; }
            else if (path.StartsWith("/api/migration")) { estTokens = 3000; if (policy != null) selectedMode = policy.Migration; }
            else if (path.StartsWith("/api/voice")) { estTokens = 1500; if (policy != null) selectedMode = policy.Voice; }

            // DefaultNamines / BYOK → zaten ücretsiz/yerel; havuzdan düşme.
            if (selectedMode == AIMode.DefaultNamines || selectedMode == AIMode.BYOK)
            {
                context.Items["FallbackToLocal"] = true;
                await _next(context);
                return;
            }

            // ── Global günlük havuz satırı (gün değişince yeni satır) ──
            var today = DateTime.UtcNow.Date;
            var global = await db.GlobalAiUsages.FirstOrDefaultAsync(g => g.Date == today);
            if (global == null)
            {
                global = new GlobalAiUsage { Date = today, TokensUsed = 0 };
                await db.GlobalAiUsages.AddAsync(global);
                try { await db.SaveChangesAsync(); }
                catch { global = await db.GlobalAiUsages.FirstOrDefaultAsync(g => g.Date == today); } // yarış: başka istek oluşturdu
            }
            long globalUsed = global?.TokensUsed ?? 0;

            // ── Havuz VEYA kullanıcı tavanı dolarsa → minimum AI'ya düş (BLOKLAMA YOK) ──
            bool poolExhausted = globalUsed + estTokens > dailyPool;
            bool userExhausted = quota.DailyUsageCount + estTokens > quota.DailyLimit;
            if (poolExhausted || userExhausted)
            {
                context.Items["FallbackToLocal"] = true;
                context.Response.Headers["X-AI-Fallback"] = poolExhausted ? "pool-exhausted" : "quota-exhausted";
                await _next(context);
                return;
            }

            await _next(context);

            // Başarılıysa hem kullanıcı hem global sayacı ATOMİK artır (yarış-güvenli).
            if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
            {
                int cap = quota.DailyLimit;
                int cost = estTokens;
                await db.UserAIQuotas
                    .Where(q => q.UserId == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(
                        q => q.DailyUsageCount,
                        q => q.DailyUsageCount + cost > cap ? cap : q.DailyUsageCount + cost));
                await db.GlobalAiUsages
                    .Where(g => g.Date == today)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.TokensUsed, g => g.TokensUsed + cost));
            }
        }
    }
}
