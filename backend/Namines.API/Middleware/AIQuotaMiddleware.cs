using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
            var path = context.Request.Path.Value?.ToLower() ?? "";

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

            // Quota check
            var quota = await db.UserAIQuotas.FirstOrDefaultAsync(q => q.UserId == userId);
            if (quota == null)
            {
                quota = new UserAIQuota
                {
                    UserId = userId,
                    DailyLimit = 100, // 100% percentage-based credits
                    DailyUsageCount = 0,
                    LastResetDate = DateTime.UtcNow
                };
                await db.UserAIQuotas.AddAsync(quota);
                await db.SaveChangesAsync();
            }
            else
            {
                if (quota.DailyLimit < 100)
                {
                    quota.DailyLimit = 100;
                }

                // Daily reset check (Turkey Time UTC+3)
                if (quota.LastResetDate.AddHours(3).Date < DateTime.UtcNow.AddHours(3).Date)
                {
                    quota.DailyUsageCount = 0;
                    quota.LastResetDate = DateTime.UtcNow;
                }
                db.UserAIQuotas.Update(quota);
                await db.SaveChangesAsync();
            }

            // NOT: Eskiden client'ın gönderdiği "X-AI-Provider: Ollama" header'ı tüm
            // quota'yı bypass ediyordu (spoof edilebilir → ücretli kullanıcı bedava Groq).
            // Kaldırıldı. Ücretsiz/yerel kullanım zaten sunucu-taraflı AIMode.DefaultNamines
            // (multiplier 0) ile ele alınıyor; sağlayıcı client header'ından türetilmez.

            // Map requested path to feature base costs & policy modes
            AIMode selectedMode = AIMode.Medium; // default fallback if no policy
            int baseCost = 5;

            var policy = await db.UserAIPolicies.FirstOrDefaultAsync(p => p.UserId == userId);
            if (path.StartsWith("/api/aidba"))
            {
                baseCost = 8;
                if (policy != null) selectedMode = policy.DbaAnalysis;
            }
            else if (path.StartsWith("/api/schema/revise"))
            {
                baseCost = 8;
                if (policy != null) selectedMode = policy.SchemaRevision;
            }
            else if (path.StartsWith("/api/smartseed") || path.StartsWith("/api/schema/mockdata"))
            {
                baseCost = 5;
                if (policy != null) selectedMode = policy.SmartSeed;
            }
            else if (path.StartsWith("/api/schema/generate"))
            {
                baseCost = 5;
                if (policy != null) selectedMode = policy.SchemaGeneration;
            }
            else if (path.StartsWith("/api/coderai") || path.StartsWith("/api/reverseengineer"))
            {
                baseCost = path.StartsWith("/api/reverseengineer") ? 15 : 10;
                if (policy != null) selectedMode = policy.Scaffolding;
            }
            else if (path.StartsWith("/api/documentation"))
            {
                baseCost = 10;
                if (policy != null) selectedMode = policy.Documentation;
            }
            else if (path.StartsWith("/api/migration"))
            {
                baseCost = 10;
                if (policy != null) selectedMode = policy.Migration;
            }
            else if (path.StartsWith("/api/voice"))
            {
                baseCost = 5;
                if (policy != null) selectedMode = policy.Voice;
            }

            int multiplier = selectedMode switch
            {
                AIMode.DefaultNamines => 0,
                AIMode.Low => 1,
                AIMode.Medium => 2,
                AIMode.High => 4,
                AIMode.HighMixtral => 5,
                AIMode.GeminiFlash => 5,   // Gemini 1.5 Flash — High+ tier
                AIMode.Ultra => 6,
                AIMode.GeminiPro => 6,     // Gemini 1.5 Pro — Ultra tier
                AIMode.BYOK => 0,
                _ => 2
            };

            int totalCost = baseCost * multiplier;

            if (selectedMode == AIMode.DefaultNamines || selectedMode == AIMode.BYOK || totalCost == 0)
            {
                context.Items["FallbackToLocal"] = true;
                await _next(context);
                return;
            }

            if (quota.DailyUsageCount >= quota.DailyLimit)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json";
                var errObj = new { code = "QUOTA_EXCEEDED", message = "Daily AI credit limit reached. Please upgrade your plan or switch to Default (Free) engine in preferences." };
                await context.Response.WriteAsync(JsonSerializer.Serialize(errObj));
                return;
            }

            await _next(context);

            // Increment quota only on successful AI generation response.
            // Atomik UPDATE: eşzamanlı isteklerde read-modify-write kaynaklı kayıp artışı önler.
            if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
            {
                int cap = quota.DailyLimit;
                await db.UserAIQuotas
                    .Where(q => q.UserId == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(
                        q => q.DailyUsageCount,
                        q => q.DailyUsageCount + totalCost > cap ? cap : q.DailyUsageCount + totalCost));
            }
        }
    }
}
