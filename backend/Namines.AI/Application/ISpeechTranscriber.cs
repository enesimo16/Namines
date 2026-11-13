namespace Namines.AI.Application;

/// <summary>Sesten metne. Bütün LLM/ses sağlayıcı trafiği AI modülünden geçer (bkz. Ai_provider_hosts_live_only_in_the_AI_module).</summary>
public interface ISpeechTranscriber
{
    /// <summary>Yazıya çevrilmiş metin. Sağlayıcı yapılandırılmamışsa <see cref="Namines.Core.Interfaces.AiNotConfiguredException"/> (503).</summary>
    Task<string?> TranscribeAsync(Stream audio, string fileName, string contentType, string language, CancellationToken ct = default);
}
