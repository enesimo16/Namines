using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Prompts;

namespace Namines.Infrastructure.AI;

public class OllamaAIService : IAIService
{
    private readonly HttpClient _httpClient;

    public OllamaAIService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("http://localhost:11434/api/");
    }

    public async Task<DatabaseSchema> GenerateSchemaAsync(GenerateRequest request)
    {
        var systemPrompt = SchemaPromptBuilder.BuildSystemPrompt();
        var modelName = string.IsNullOrWhiteSpace(request.ModelName) ? "qwen2.5-coder" : request.ModelName;
        
        object userMessage;
        
        if (request.Image != null)
        {
            modelName = "llava"; // Default vision model for Ollama
            using var ms = new MemoryStream();
            await request.Image.CopyToAsync(ms);
            var base64Image = Convert.ToBase64String(ms.ToArray());
            
            userMessage = new 
            { 
                role = "user", 
                content = request.Prompt + "\nEkteki görseli analiz et ve içindeki veritabanı/tablo mimarisini çıkararak bunu sistemin DatabaseSchema JSON formatına uygun şekilde oluştur.",
                images = new[] { base64Image }
            };
        }
        else
        {
            userMessage = new { role = "user", content = request.Prompt };
        }

        var payload = new
        {
            model = modelName,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                userMessage
            },
            format = "json",
            stream = false,
            options = new { temperature = 0.2 }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("chat", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Ollama API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var jsonResponse = responseObject.GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(jsonResponse))
            throw new Exception("Received empty response from Ollama.");

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var schema = JsonSerializer.Deserialize<DatabaseSchema>(jsonResponse, options);
            if (schema == null || schema.Tables == null)
                throw new Exception("Ollama returned invalid schema format.");
                
            return schema;
        }
        catch (JsonException ex)
        {
            throw new Exception($"Failed to parse Ollama JSON: {ex.Message}\nResponse: {jsonResponse}");
        }
    }

    public async Task<DatabaseSchema> ReviseSchemaAsync(ReviseRequest request)
    {
        var systemPrompt = RevisionPromptBuilder.BuildSystemPrompt();
        var userPrompt = RevisionPromptBuilder.BuildUserPrompt(request);
        var modelName = string.IsNullOrWhiteSpace(request.ModelName) ? "qwen2.5-coder" : request.ModelName;

        var payload = new
        {
            model = modelName,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            format = "json",
            stream = false,
            options = new { temperature = 0.2 }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("chat", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Ollama API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var jsonResponse = responseObject.GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(jsonResponse))
            throw new Exception("Received empty response from Ollama.");

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var partialSchema = JsonSerializer.Deserialize<DatabaseSchema>(jsonResponse, options);
            if (partialSchema == null)
                throw new Exception("Ollama returned invalid partial schema format.");
                
            return partialSchema;
        }
        catch (JsonException ex)
        {
            throw new Exception($"Failed to parse Ollama JSON: {ex.Message}\nResponse: {jsonResponse}");
        }
    }

    public async Task<string> GenerateMockDataAsync(DatabaseSchema schema)
    {
        var systemPrompt = MockDataPromptBuilder.BuildSystemPrompt();
        var userPrompt = MockDataPromptBuilder.BuildUserPrompt(schema);
        
        var payload = new
        {
            model = "qwen2.5-coder", // Defaulting to this for mock data
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            stream = false,
            options = new { temperature = 0.4 }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("chat", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Ollama API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var sqlResponse = responseObject.GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(sqlResponse))
            throw new Exception("Received empty response from Ollama for mock data.");

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
            : "Tablo bilgisi yok";

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
            model = "qwen2.5-coder",
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt }
            },
            stream = false,
            options = new { temperature = 0.6 }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("chat", content);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Ollama API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var summary = responseObject.GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(summary))
            throw new Exception("Received empty project summary from Ollama.");

        return summary.Trim();
    }

    public async Task<string> GenerateStreamlitAppAsync(DatabaseSchema schema, Namines.Core.Enums.DatabaseType dbType)
    {
        var systemPrompt = StreamlitPromptBuilder.BuildSystemPrompt();
        var userPrompt = StreamlitPromptBuilder.BuildUserPrompt(schema, dbType);

        var payload = new
        {
            model = "qwen2.5-coder",
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            stream = false,
            options = new { temperature = 0.2 }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("chat", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Ollama API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var pythonResponse = responseObject.GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(pythonResponse))
            throw new Exception("Received empty response from Ollama for Streamlit app.");

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
        var userPrompt = $@"Aşağıdaki Streamlit uygulaması hata veriyor. Hatalı kodu, hata günlüklerini ve veritabanı şemasını inceleyerek çalışan bir sürüm üret.

KURALLAR:
1. Sadece düz Python kodu döndür.
2. Markdown, açıklama veya kod dışı metin ekleme.
3. Mevcut çalışan yapıyı mümkün olduğunca koru.
4. Hata günlüklerindeki asıl sebebi düzelt.
5. Çıktı ```python ile başlayıp ``` ile bitsin.

VERİTABANI ŞEMASI:
IMPORTANT: Ignore any earlier instruction about wrapping output in code fences. Return plain Python only.

DATABASE CONNECTION:
{BuildConnectionContext(dbType)}

{schemaJson}

HATA GÜNLÜKLERİ:
{errorLogs}

HATALI KOD:
{originalCode}";

        userPrompt = $@"Fix the Streamlit app using the error tail and schema.

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
            model = "qwen2.5-coder",
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            stream = false,
            options = new { temperature = 0.1 }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("chat", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Ollama API Error ({response.StatusCode}): {errorContent}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseString);
        var pythonResponse = responseObject.GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(pythonResponse))
            throw new Exception("Received empty response from Ollama for Streamlit fix.");

        return StripMarkdownCodeFence(pythonResponse);
    }

    private static string StripMarkdownCodeFence(string value)
    {
        var result = value.Trim();
        if (result.StartsWith("```python", StringComparison.OrdinalIgnoreCase))
            result = result.Substring(9);
        else if (result.StartsWith("```py", StringComparison.OrdinalIgnoreCase))
            result = result.Substring(5);
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

    public Task<DatabaseSchema> AnalyzeImageAsync(byte[] imageBytes, string mimeType)
    {
        throw new NotSupportedException("Ollama vision service is not implemented yet.");
    }
}
