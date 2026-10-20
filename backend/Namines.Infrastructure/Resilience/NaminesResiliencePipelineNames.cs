namespace Namines.Infrastructure.Resilience;

/// <summary>
/// Namines Polly v8 dayanıklılık (resilience) hatlarının kanonik adları.
/// </summary>
public static class NaminesResiliencePipelineNames
{
    /// <summary>
    /// LLM ve AI sağlayıcıları (Groq, Ollama, DeepSeek) için dayanıklılık hattı:
    /// Jittered Exponential Backoff retry, Timeout ve Circuit Breaker koruması.
    /// </summary>
    public const string AiPipeline = "AiPipeline";

    /// <summary>
    /// Cloud Database sağlayıcıları (Neon, Supabase REST API) için dayanıklılık hattı:
    /// Hızlı retry, 15s timeout ve kesinti koruması.
    /// </summary>
    public const string CloudDbPipeline = "CloudDbPipeline";

    /// <summary>
    /// Docker Daemon / docker.sock çağrıları için dayanıklılık hattı:
    /// Concurrency limiter (max 3 eşzamanlı işlem) ile WSL2 'read-only filesystem' çökmesini engeller.
    /// </summary>
    public const string DockerSocketPipeline = "DockerSocketPipeline";

    /// <summary>
    /// Standart harici HTTP / Webhook çağrıları için dayanıklılık hattı:
    /// Standart zaman aşımı ve transient network retry.
    /// </summary>
    public const string ExternalHttpPipeline = "ExternalHttpPipeline";
}
