using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Core.Enums;
using Namines.Core.Prompts;
using System.Linq;

namespace Namines.Infrastructure.AI;

public class TolerantStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return reader.GetDouble().ToString();
        if (reader.TokenType == JsonTokenType.True) return "true";
        if (reader.TokenType == JsonTokenType.False) return "false";
        if (reader.TokenType == JsonTokenType.String) return reader.GetString();
        if (reader.TokenType == JsonTokenType.Null) return null;
        
        using (JsonDocument document = JsonDocument.ParseValue(ref reader))
        {
            return document.RootElement.GetRawText();
        }
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}

public class GroqAIService : IAIService
{
    private readonly HttpClient _httpClient;
    private readonly string _modelName;
    private readonly string _groqApiKey;   // Per-request inject edilir — DefaultRequestHeaders'a yazılmaz
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMemoryCache _cache;

    public GroqAIService(
        HttpClient httpClient,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        IMemoryCache cache)
    {
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
        _cache = cache;

        var apiKey = configuration["Groq:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = "MISSING_API_KEY";
        }

        // API key sadece private field'da tutulur.
        // DefaultRequestHeaders.Authorization KULLANILMAZ: thread pool'daki parallel Gemini/OpenAI
        // isteklerinde header mutation race condition riski ve debug/log sızıntısı yaratirdı.
        _groqApiKey = apiKey;
        _modelName = configuration["Groq:Model"] ?? "llama-3.3-70b-versatile";

        _httpClient.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
        // DefaultRequestHeaders.Authorization kasitlı olarak KALDIRILDI.

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new TolerantStringConverter());
    }

    /// <summary>
    /// Kullanıcı politikasını asenkron olarak sorgular ve 60 saniyelik cache'e alır.
    /// Önceki implementasyon synchronous FirstOrDefault kullanıyordu; yüksek traffic'te
    /// thread pool blocking'e yol açıyordu. FirstOrDefaultAsync + IMemoryCache ile giderildi.
    /// </summary>
    private async Task<string> ResolveModelNameAsync(string? requestedModel = null, string? featureName = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel))
        {
            if (string.Equals(requestedModel, "mixtral-8x7b-32768", StringComparison.OrdinalIgnoreCase))
                return "llama-3.3-70b-versatile";
            if (string.Equals(requestedModel, "llama-3.2-3b-preview", StringComparison.OrdinalIgnoreCase))
                return "llama-3.1-8b-instant";
            if (string.Equals(requestedModel, "gemini-1.5-flash", StringComparison.OrdinalIgnoreCase))
                return "gemini-2.5-flash";
            if (string.Equals(requestedModel, "gemini-1.5-pro", StringComparison.OrdinalIgnoreCase))
                return "gemini-2.5-pro";
            return requestedModel;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return "llama-3.1-8b-instant"; // Safe background thread fallback

        var byokProvider = httpContext.Items["ByokProvider"] as string;
        bool isByok = httpContext.Items.ContainsKey("IsByok") == true;

        if (isByok)
        {
            if (string.Equals(byokProvider, "openai", StringComparison.OrdinalIgnoreCase))
                return "gpt-4o";
            if (string.Equals(byokProvider, "gemini", StringComparison.OrdinalIgnoreCase))
                return "gemini-2.5-pro";
            return "llama-3.3-70b-versatile";
        }

        var userId = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            // Cache key: kullanıcı başına + özellik adı başına. 60 sn sliding expiration.
            var cacheKey = $"ai-policy:{userId}:{featureName ?? "default"}";
            if (_cache.TryGetValue(cacheKey, out string? cachedModel) && cachedModel != null)
                return cachedModel;

            try
            {
                var db = httpContext.RequestServices.GetRequiredService<Namines.Infrastructure.Data.AuthDbContext>();
                if (db != null)
                {
                    // FirstOrDefaultAsync: thread pool'u bloklamaz
                    var policy = await db.UserAIPolicies.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (policy != null)
                    {
                        AIMode mode = AIMode.Medium;
                        if (featureName == "SmartSeed") mode = policy.SmartSeed;
                        else if (featureName == "Documentation") mode = policy.Documentation;
                        else if (featureName == "Scaffolding") mode = policy.Scaffolding;
                        else if (featureName == "SchemaGeneration") mode = policy.SchemaGeneration;
                        else if (featureName == "SchemaRevision") mode = policy.SchemaRevision;
                        else if (featureName == "DbaAnalysis") mode = policy.DbaAnalysis;
                        else if (featureName == "Migration") mode = policy.Migration;
                        else if (featureName == "Voice") mode = policy.Voice;

                        string resolvedModel = mode switch
                        {
                            AIMode.Low            => "llama-3.1-8b-instant",
                            AIMode.Medium         => "llama-3.1-8b-instant",
                            AIMode.DefaultNamines => "llama-3.1-8b-instant",
                            AIMode.High           => "llama-3.3-70b-versatile",
                            AIMode.HighMixtral    => "llama-3.3-70b-versatile",
                            AIMode.GeminiFlash    => "gemini-2.5-flash",
                            AIMode.Ultra          => "openai/gpt-oss-120b",
                            AIMode.GeminiPro      => "gemini-2.5-pro",
                            _                     => "llama-3.1-8b-instant"
                        };

                        _cache.Set(cacheKey, resolvedModel, new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromSeconds(60)
                        });
                        return resolvedModel;
                    }
                }
            }
            catch
            {
                // Fallback to user type
            }
        }

        var userType = httpContext.User?.FindFirst("type")?.Value;
        if (userType == "corporate") return "llama-3.3-70b-versatile";

        return "llama-3.1-8b-instant";
    }

    private async Task<HttpResponseMessage> PostAsync(string relativeUri, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, relativeUri) { Content = content };

        var byokKey = _httpContextAccessor.HttpContext?.Items["ByokApiKey"] as string;
        var byokProvider = _httpContextAccessor.HttpContext?.Items["ByokProvider"] as string;

        // Resolve the model name from the payload to detect Gemini routing
        string modelInPayload = "";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("model", out var modelEl))
                modelInPayload = modelEl.GetString() ?? "";
        }
        catch { }

        bool isGeminiModel = modelInPayload.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(byokKey))
        {
            // BYOK: kullanıcının kendi API key'i
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", byokKey);
            if (string.Equals(byokProvider, "openai", StringComparison.OrdinalIgnoreCase))
                request.RequestUri = new Uri("https://api.openai.com/v1/" + relativeUri);
            else if (string.Equals(byokProvider, "gemini", StringComparison.OrdinalIgnoreCase))
                request.RequestUri = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/" + relativeUri);
        }
        else if (isGeminiModel)
        {
            // Sunucu-taraflı Gemini API key'i — configuration'dan al
            var httpContext = _httpContextAccessor.HttpContext;
            var config = httpContext?.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
            var geminiApiKey = config?["Gemini:ApiKey"] ?? "";
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", geminiApiKey);
            request.RequestUri = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/" + relativeUri);
        }
        else
        {
            // Standart Groq isteği: key per-request inject edilir (DefaultRequestHeaders KULLANILMAZ)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _groqApiKey);
        }

        return await _httpClient.SendAsync(request);
    }

    private int CalculateMaxTokens(int tableCount)
    {
        // Safe for standard 12,000 TPM limits on free tier organizations
        if (tableCount <= 10) return 4096;
        return 6000;
    }


    public async Task<List<DbaIssue>> AnalyzeSchemaDbaAsync(DatabaseSchema schema, DatabaseType dbType)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        bool forceLocal = httpContext?.Items.ContainsKey("FallbackToLocal") == true && httpContext.Items["FallbackToLocal"] is true;
        if (forceLocal)
        {
            throw new Exception("Local Fallback mode is active.");
        }

        var systemPrompt = DbaPromptBuilder.BuildSystemPrompt();
        var userPrompt = DbaPromptBuilder.BuildUserPrompt(schema, dbType);
        
        var payload = new
        {
            model = await ResolveModelNameAsync(null, "DbaAnalysis"),
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.0,
            max_tokens = 4096
        };

        using var response = await PostAsync("chat/completions", payload);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Groq API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var jsonResponse = responseObject.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(jsonResponse))
            return new List<DbaIssue>();

        jsonResponse = jsonResponse.Trim();
        int firstBrace = jsonResponse.IndexOf('[');
        int lastBrace = jsonResponse.LastIndexOf(']');
        if (firstBrace != -1 && lastBrace != -1 && lastBrace > firstBrace)
        {
            jsonResponse = jsonResponse.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new TolerantStringConverter());
            jsonResponse = Namines.Infrastructure.Services.JsonSanitizerPreprocessor.Sanitize(jsonResponse);
            var aiIssues = JsonSerializer.Deserialize<List<DbaIssue>>(jsonResponse, options);
            return aiIssues ?? new List<DbaIssue>();
        }
        catch
        {
            // Graceful degradation - if parsing fails, return empty list
            return new List<DbaIssue>();
        }
    }

    public async Task<DatabaseSchema> ParseDbContextToSchemaAsync(string dbContextCode, DatabaseType dbType)
    {
        var systemPrompt = DbContextParsePromptBuilder.BuildSystemPrompt();
        var userPrompt = DbContextParsePromptBuilder.BuildUserPrompt(dbContextCode, dbType.ToString());

        var payload = new
        {
            model = await ResolveModelNameAsync(null, "Migration"),
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.1,
            max_tokens = 4096
        };

        using var response = await PostAsync("chat/completions", payload);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Groq API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var jsonResponse = responseObject.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(jsonResponse))
            throw new Exception("Received empty response from AI for DbContext parsing.");

        jsonResponse = jsonResponse.Trim();
        int firstBrace = jsonResponse.IndexOf('{');
        int lastBrace = jsonResponse.LastIndexOf('}');
        if (firstBrace != -1 && lastBrace != -1 && lastBrace > firstBrace)
        {
            jsonResponse = jsonResponse.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        jsonResponse = Namines.Infrastructure.Services.JsonSanitizerPreprocessor.Sanitize(jsonResponse);
        var schema = JsonSerializer.Deserialize<DatabaseSchema>(jsonResponse, _jsonOptions);
        if (schema == null)
            throw new Exception("Deserialized schema is null.");

        return schema;
    }

    public async Task<MigrationResult> GenerateMigrationCodeAsync(DatabaseSchema oldSchema, DatabaseSchema newSchema, DatabaseType dbType)
    {
        var systemPrompt = MigrationPromptBuilder.BuildSystemPrompt();
        var userPrompt = MigrationPromptBuilder.BuildUserPrompt(oldSchema, newSchema, dbType);

        var payload = new
        {
            model = await ResolveModelNameAsync(null, "Migration"),
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.2,
            max_tokens = 4096
        };

        using var response = await PostAsync("chat/completions", payload);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Groq API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var jsonResponse = responseObject.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(jsonResponse))
            throw new Exception("Received empty response from AI for migration generation.");

        jsonResponse = jsonResponse.Trim();
        int firstBrace = jsonResponse.IndexOf('{');
        int lastBrace = jsonResponse.LastIndexOf('}');
        if (firstBrace != -1 && lastBrace != -1 && lastBrace > firstBrace)
        {
            jsonResponse = jsonResponse.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        jsonResponse = Namines.Infrastructure.Services.JsonSanitizerPreprocessor.Sanitize(jsonResponse);
        var result = JsonSerializer.Deserialize<MigrationResult>(jsonResponse, _jsonOptions);
        if (result == null)
            throw new Exception("Deserialized migration result is null.");

        return result;
    }


    public async Task<string> GenerateSmartSeedSqlAsync(DatabaseSchema schema, DatabaseType dbType, string domain, int rowCount)
    {
        var systemPrompt = SmartSeedPromptBuilder.BuildSystemPrompt();
        var userPrompt = SmartSeedPromptBuilder.BuildUserPrompt(schema, dbType, domain, rowCount);

        var payload = new
        {
            model = await ResolveModelNameAsync(null, "SmartSeed"),
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.4,
            max_tokens = CalculateMaxTokens(schema.Tables?.Count ?? 0)
        };

        using var response = await PostAsync("chat/completions", payload);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Groq API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var sqlResponse = responseObject.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(sqlResponse))
            throw new Exception("Received empty response from AI for smart seed data.");

        return StripMarkdownCodeFence(sqlResponse);
    }

    public async Task<DatabaseSchema> GenerateSchemaAsync(GenerateRequest request)
    {
        int maxRetries = 2;
        int currentAttempt = 0;

        var systemPrompt = SchemaPromptBuilder.BuildSystemPrompt();
        var userPrompt = SchemaPromptBuilder.BuildUserPrompt(request.Prompt, request.DbType);

        while (currentAttempt <= maxRetries)
        {
            try
            {
                var modelToUse = await ResolveModelNameAsync(request.ModelName, "SchemaGeneration");
                
                object userContentObject = userPrompt;

                if (request.Image != null)
                {
                    var byokProvider = _httpContextAccessor.HttpContext?.Items["ByokProvider"] as string;
                    if (string.Equals(byokProvider, "openai", StringComparison.OrdinalIgnoreCase))
                    {
                        modelToUse = "gpt-4o";
                    }
                    else
                    {
                        modelToUse = "meta-llama/llama-4-scout-17b-16e-instruct"; // Use vision model
                    }
                    
                    using var ms = new MemoryStream();
                    await request.Image.CopyToAsync(ms);
                    var base64Image = Convert.ToBase64String(ms.ToArray());
                    var mimeType = request.Image.ContentType;

                    userContentObject = new List<object>
                    {
                        new { type = "text", text = userPrompt + "\nEkteki görseli analiz et ve içindeki veritabanı/tablo mimarisini çıkararak bunu sistemin DatabaseSchema JSON formatına uygun şekilde oluştur." },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:{mimeType};base64,{base64Image}" }
                        }
                    };
                }

                var payload = new
                {
                    model = modelToUse,
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userContentObject }
                    },
                    temperature = 0.1 + (currentAttempt * 0.2),
                    max_tokens = 4096
                };

                using var response = await PostAsync("chat/completions", payload);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    if (errorContent.Contains("invalid_api_key"))
                    {
                        throw new Exception("Groq API Anahtarı eksik veya hatalı. Lütfen User Secrets veya appsettings.json dosyasını kontrol edin.");
                    }
                    throw new Exception($"Groq API Error ({response.StatusCode}): {errorContent}");
                }

                var responseString = await response.Content.ReadAsStringAsync();
                var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
                var jsonResponse = responseObject.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

                if (string.IsNullOrWhiteSpace(jsonResponse))
                {
                     throw new Exception("Received empty response from AI.");
                }

                jsonResponse = jsonResponse.Trim();
                int firstBrace = jsonResponse.IndexOf('{');
                int lastBrace = jsonResponse.LastIndexOf('}');
                if (firstBrace != -1 && lastBrace != -1 && lastBrace > firstBrace)
                {
                    jsonResponse = jsonResponse.Substring(firstBrace, lastBrace - firstBrace + 1);
                }
                
                jsonResponse = Namines.Infrastructure.Services.JsonSanitizerPreprocessor.Sanitize(jsonResponse);
                var schema = JsonSerializer.Deserialize<DatabaseSchema>(jsonResponse, _jsonOptions);
                if (schema == null)
                    throw new Exception("Deserialized schema is null.");

                return schema;
            }
            catch (JsonException)
            {
                currentAttempt++;
                if (currentAttempt > maxRetries)
                {
                    throw new Exception($"Failed to generate a valid JSON schema after {maxRetries} retries.");
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        throw new Exception("Failed to generate schema.");
    }

    public async Task<DatabaseSchema> ReviseSchemaAsync(ReviseRequest request)
    {
        int maxRetries = 2;
        int currentAttempt = 0;
        string lastJsonResponse = string.Empty;

        var systemPrompt = RevisionPromptBuilder.BuildSystemPrompt();
        var userPrompt = RevisionPromptBuilder.BuildUserPrompt(request);

        while (currentAttempt <= maxRetries)
        {
            try
            {
                var modelToUse = await ResolveModelNameAsync(request.ModelName, "SchemaRevision");
                var tableCount = request.SelectedTables?.Count ?? 0;

                var payload = new
                {
                    model = modelToUse,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    temperature = 0.1 + (currentAttempt * 0.2),
                    max_tokens = CalculateMaxTokens(tableCount)
                };

                using var response = await PostAsync("chat/completions", payload);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Groq API Error ({response.StatusCode}): {errorContent}");
                }

                var responseString = await response.Content.ReadAsStringAsync();
                var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
                var jsonResponse = responseObject.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                lastJsonResponse = jsonResponse ?? "";

                if (string.IsNullOrWhiteSpace(jsonResponse))
                {
                     throw new Exception("Received empty response from AI.");
                }

                jsonResponse = jsonResponse.Trim();
                int firstBrace = jsonResponse.IndexOf('{');
                int lastBrace = jsonResponse.LastIndexOf('}');
                if (firstBrace != -1 && lastBrace != -1 && lastBrace > firstBrace)
                {
                    jsonResponse = jsonResponse.Substring(firstBrace, lastBrace - firstBrace + 1);
                }

                jsonResponse = Namines.Infrastructure.Services.JsonSanitizerPreprocessor.Sanitize(jsonResponse);
                var schema = JsonSerializer.Deserialize<DatabaseSchema>(jsonResponse, _jsonOptions);
                if (schema == null)
                    throw new Exception("Deserialized schema is null.");

                return schema;
            }
            catch (JsonException)
            {
                currentAttempt++;
                if (currentAttempt > maxRetries)
                {
                    throw new Exception($"Failed to generate a valid JSON partial schema after {maxRetries} retries. Last Response: {lastJsonResponse}");
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        throw new Exception("Failed to revise schema.");
    }

    public async Task<string> GenerateMockDataAsync(DatabaseSchema schema)
    {
        var systemPrompt = MockDataPromptBuilder.BuildSystemPrompt();
        var userPrompt = MockDataPromptBuilder.BuildUserPrompt(schema);
        var tableCount = schema.Tables?.Count ?? 0;

        var payload = new
        {
            model = await ResolveModelNameAsync(null, "SmartSeed"),
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.4,
            max_tokens = CalculateMaxTokens(tableCount)
        };

        using var response = await PostAsync("chat/completions", payload);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            if (errorContent.Contains("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Groq rate limit exceeded. Retry after {GetRetryAfterSeconds(response, errorContent)} seconds.");
            }
            throw new Exception($"Groq API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var sqlResponse = responseObject.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(sqlResponse))
            throw new Exception("Received empty response from AI for mock data.");

        // Clean markdown
        sqlResponse = sqlResponse.Trim();
        if (sqlResponse.StartsWith("```sql", StringComparison.OrdinalIgnoreCase))
            sqlResponse = sqlResponse.Substring(6);
        else if (sqlResponse.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            sqlResponse = sqlResponse.Substring(3);
            
        if (sqlResponse.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            sqlResponse = sqlResponse.Substring(0, sqlResponse.Length - 3);

        return sqlResponse.Trim();
    }

    public async Task<string> GenerateProjectSummaryAsync(DatabaseSchema schema, string projectName)
    {
        var tableCount = schema.Tables?.Count ?? 0;
        var relationCount = schema.Relations?.Count ?? 0;

        var tableList = schema.Tables != null
            ? string.Join(", ", schema.Tables.Select(t =>
                $"{t.Name} ({string.Join(", ", t.Columns.Select(c => c.Name))})"))
            : "Tablo bilgiisi yok";

        var systemPrompt =
            "Sen kıdemli bir veritabanı mimarı ve teknik yazarsın. " +
            "Sana veritabanı şema bilgisi verilecek. " +
            "Bu veritabanının iş amacını, hangi sektöre/uygulamaya hizmet ettiğini ve " +
            "mimari açıdan güçlü yönlerini anlatan, yöneticilere yönelik, " +
            "profesyonel bir Yönetici Özeti (Executive Summary) yaz. " +
            "Özet 3-5 paragraf olsun, teknik olmayan bir dille başlasın " +
            "ancak sonraki paragraflarda mimari detaylara değinsin. " +
            "Markdown, başlık veya liste kullanma. Sadece düz metin paragraf.";

        var userPrompt =
            $"Proje Adı: {projectName}\n" +
            $"Toplam Tablo Sayısı: {tableCount}\n" +
            $"Toplam İlişki Sayısı: {relationCount}\n\n" +
            $"Tablolar ve Kolonları:\n{tableList}\n\n" +
            "Bu veritabanı şeması için profesyonel bir Yönetici Özeti yaz.";

        var payload = new
        {
            model = await ResolveModelNameAsync(null, "Documentation"),
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt }
            },
            temperature = 0.6,
            max_tokens = CalculateMaxTokens(tableCount)
        };

        using var response = await PostAsync("chat/completions", payload);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            if (errorContent.Contains("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Groq rate limit exceeded. Retry after {GetRetryAfterSeconds(response, errorContent)} seconds.");
            }
            throw new Exception($"Groq API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var summary = responseObject.GetProperty("choices")[0]
                                    .GetProperty("message")
                                    .GetProperty("content")
                                    .GetString();

        if (string.IsNullOrWhiteSpace(summary))
            throw new Exception("Received empty project summary from Groq AI.");

        return summary.Trim();
    }

    public async Task<string> ExplainImpactAsync(ImpactReport impact)
    {
        var (systemPrompt, userPrompt) = ImpactExplainerPromptBuilder.Build(impact);

        var payload = new
        {
            model = await ResolveModelNameAsync(null, "Documentation"),
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt }
            },
            temperature = 0.3, // düşük — bulgu icat etmesin, verilen yapıyı sadakatle özetlesin
            max_tokens = 1024
        };

        using var response = await PostAsync("chat/completions", payload);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            if (errorContent.Contains("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Groq rate limit exceeded. Retry after {GetRetryAfterSeconds(response, errorContent)} seconds.");
            throw new Exception($"Groq API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var explanation = responseObject.GetProperty("choices")[0]
                                        .GetProperty("message")
                                        .GetProperty("content")
                                        .GetString();

        if (string.IsNullOrWhiteSpace(explanation))
            throw new Exception("Received empty impact explanation from Groq AI.");

        return explanation.Trim();
    }

    public async Task<string> GenerateStreamlitAppAsync(DatabaseSchema schema, Namines.Core.Enums.DatabaseType dbType)
    {
        var systemPrompt = StreamlitPromptBuilder.BuildSystemPrompt();
        var userPrompt = StreamlitPromptBuilder.BuildUserPrompt(schema, dbType);
        var tableCount = schema.Tables?.Count ?? 0;

        var payload = new
        {
            model = await ResolveModelNameAsync(null, "Scaffolding"),
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.2,
            max_tokens = CalculateMaxTokens(tableCount)
        };

        using var response = await PostAsync("chat/completions", payload);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            if (errorContent.Contains("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Groq rate limit exceeded. Retry after {GetRetryAfterSeconds(response, errorContent)} seconds.");
            }
            throw new Exception($"Groq API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var pythonResponse = responseObject.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(pythonResponse))
            throw new Exception("Received empty response from Groq AI for Streamlit app.");

        // Clean markdown
        pythonResponse = pythonResponse.Trim();
        if (pythonResponse.StartsWith("```python", StringComparison.OrdinalIgnoreCase))
            pythonResponse = pythonResponse.Substring(9);
        else if (pythonResponse.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            pythonResponse = pythonResponse.Substring(3);
            
        if (pythonResponse.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            pythonResponse = pythonResponse.Substring(0, pythonResponse.Length - 3);

        return pythonResponse.Trim();
    }

    public async Task<string> FixStreamlitAppAsync(string originalCode, string errorLogs, DatabaseSchema schema, Namines.Core.Enums.DatabaseType dbType)
    {
        originalCode = TruncateForPrompt(SanitizeText(originalCode), 12000);
        errorLogs = ExtractRelevantErrorTail(SanitizeText(errorLogs), 3000);
        var schemaJson = TruncateForPrompt(SerializeSchemaForPrompt(schema), 6000);
        var systemPrompt = "Fix a crashing Streamlit Python app. Return only plain Python code.";
        var tableCount = schema.Tables?.Count ?? 0;

        var userPrompt = $@"Fix the Streamlit app using the error tail and schema.
 
Rules:
- Return only plain Python code.
- No markdown, explanations, or code fences.
- Preserve existing behavior where possible.
- Use host db for database connections.
- Do not add imports requiring missing packages.
 
Database:
{BuildConnectionContext(dbType)}
 
Schema JSON:
{schemaJson}
 
Error tail:
{errorLogs}
 
Code:
{originalCode}";

        var payload = new
        {
            model = await ResolveModelNameAsync(null, "Scaffolding"),
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.1,
            max_tokens = CalculateMaxTokens(tableCount)
        };

        using var response = await PostAsync("chat/completions", payload);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            if (errorContent.Contains("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Groq rate limit exceeded. Retry after {GetRetryAfterSeconds(response, errorContent)} seconds.");
            }
            throw new Exception($"Groq API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var pythonResponse = responseObject.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(pythonResponse))
            throw new Exception("Received empty response from Groq AI for Streamlit fix.");

        return StripMarkdownCodeFence(pythonResponse);
    }

    private static string StripMarkdownCodeFence(string value)
    {
        var result = value.Trim();
        if (result.StartsWith("```python", StringComparison.OrdinalIgnoreCase))
            result = result.Substring(9);
        else if (result.StartsWith("```py", StringComparison.OrdinalIgnoreCase))
            result = result.Substring(5);
        else if (result.StartsWith("```sql", StringComparison.OrdinalIgnoreCase))
            result = result.Substring(6);
        else if (result.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            result = result.Substring(3);

        if (result.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            result = result.Substring(0, result.Length - 3);

        return result.Trim();
    }

    private static string TruncateForPrompt(string value, int maxLength)
    {
        value = SanitizeText(value);
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value.Substring(value.Length - maxLength);
    }

    private static string ExtractRelevantErrorTail(string value, int maxLength)
    {
        value = SanitizeText(value);
        var markerIndex = value.LastIndexOf("Traceback", StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
            value = value.Substring(markerIndex);

        return TruncateForPrompt(value, maxLength);
    }

    private static string SanitizeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(char.IsSurrogate(ch) ? '\uFFFD' : ch);

        return builder.ToString();
    }

    public async Task<DatabaseSchema> AnalyzeImageAsync(byte[] imageBytes, string mimeType)
    {
        var base64Image = Convert.ToBase64String(imageBytes);
        var systemPrompt = VisionPromptBuilder.BuildSystemPrompt();
        var userPrompt = VisionPromptBuilder.BuildUserPrompt();

        var byokProvider = _httpContextAccessor.HttpContext?.Items["ByokProvider"] as string;
        var modelToUse = string.Equals(byokProvider, "openai", StringComparison.OrdinalIgnoreCase) 
            ? "gpt-4o" 
            : "meta-llama/llama-4-scout-17b-16e-instruct";

        var payload = new
        {
            model = modelToUse,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new 
                { 
                    role = "user", 
                    content = new object[]
                    {
                        new { type = "text", text = userPrompt },
                        new 
                        { 
                            type = "image_url", 
                            image_url = new { url = $"data:{mimeType};base64,{base64Image}" }
                        }
                    }
                }
            },
            temperature = 0.1,
            max_tokens = 4096
        };

        using var response = await PostAsync("chat/completions", payload);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Groq Vision API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var jsonResponse = responseObject.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(jsonResponse))
        {
             throw new Exception("Received empty response from Groq Vision AI.");
        }

        jsonResponse = jsonResponse.Trim();
        int firstBrace = jsonResponse.IndexOf('{');
        int lastBrace = jsonResponse.LastIndexOf('}');
        if (firstBrace != -1 && lastBrace != -1 && lastBrace > firstBrace)
        {
            jsonResponse = jsonResponse.Substring(firstBrace, lastBrace - firstBrace + 1);
        }
        
        jsonResponse = Namines.Infrastructure.Services.JsonSanitizerPreprocessor.Sanitize(jsonResponse);
        var schema = JsonSerializer.Deserialize<DatabaseSchema>(jsonResponse, _jsonOptions);
        if (schema == null)
            throw new Exception("Deserialized vision schema is null.");

        return schema;
    }

    private static string GetRetryAfterSeconds(HttpResponseMessage response, string errorContent)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return Math.Ceiling(delta.TotalSeconds).ToString();

        var match = System.Text.RegularExpressions.Regex.Match(errorContent, @"try again in ([0-9.]+)s", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "unknown";
    }

    private static string SerializeSchemaForPrompt(DatabaseSchema schema)
    {
        try
        {
            return SanitizeText(JsonSerializer.Serialize(schema));
        }
        catch
        {
            return "Schema serialization failed; use the provided code and error tail as primary context.";
        }
    }

    private static string BuildConnectionContext(Namines.Core.Enums.DatabaseType dbType)
    {
        return dbType switch
        {
            Namines.Core.Enums.DatabaseType.PostgreSQL => "Host: db\nPort: 5432\nUsername: postgres\nPassword: Namines_Secure123!\nDatabase: naminesdb",
            Namines.Core.Enums.DatabaseType.MySQL => "Host: db\nPort: 3306\nUsername: root\nPassword: Namines_Secure123!\nDatabase: naminesdb",
            Namines.Core.Enums.DatabaseType.MSSQL => "Host: db\nPort: 1433\nUsername: sa\nPassword: Namines_Secure123!\nDatabase: naminesdb",
            _ => "Host: db\nDatabase: naminesdb"
        };
    }
}
