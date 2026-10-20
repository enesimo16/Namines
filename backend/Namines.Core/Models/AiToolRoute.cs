using System;

namespace Namines.Core.Models;

/// <summary>
/// Bir AI aracının (Tool) hangi LLM sağlayıcısı ve modeli üzerinden çalışacağını belirten yönlendirme kuralı.
/// (Admin Panelinden dinamik olarak değiştirilebilir).
/// </summary>
public sealed class AiToolRoute
{
    /// <summary>
    /// Aracın sistemdeki benzersiz anahtarı.
    /// Örn: "schema_generator", "ai_dba", "flow_automation", "smart_seed", "migration_explainer", "code_schema"
    /// </summary>
    public string ToolKey { get; set; } = string.Empty;

    /// <summary>
    /// Aracın kullanıcıya/ekrana gösterilen adı.
    /// Örn: "Prompt'tan Şema Üretimi (Schema Generator)"
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Aktif LLM Sağlayıcısı: "groq", "deepseek", "openai", "anthropic" vb.
    /// </summary>
    public string Provider { get; set; } = "groq";

    /// <summary>
    /// Sağlayıcı altındaki spesifik model kimliği:
    /// Örn: "qwen-2.5-coder-32b", "deepseek-chat", "deepseek-reasoner", "llama-3.3-70b-versatile"
    /// </summary>
    public string ModelName { get; set; } = "qwen-2.5-coder-32b";

    /// <summary>
    /// Model üretim sıcaklığı (0.0 - 1.0). Kod ve DDL üretiminde düşük (0.1 - 0.2), yaratıcı işlerde (0.5 - 0.7).
    /// </summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>
    /// Birincil sağlayıcı hata verdiğinde veya hız sınırına takıldığında devreye girecek yedek sağlayıcı.
    /// </summary>
    public string? FallbackProvider { get; set; }

    /// <summary>
    /// Son güncelleme zamanı (UTC).
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
