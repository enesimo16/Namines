using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.Core.Interfaces;

/// <param name="ParametersJsonSchema">Aracın parametre şeması, ham JSON metni.</param>
public sealed record AgentToolDefinition(string Name, string Description, string ParametersJsonSchema);

/// <param name="ArgumentsJson">Modelin ürettiği argümanlar, ham JSON metni.</param>
public sealed record AgentToolCall(string Id, string Name, string ArgumentsJson);

/// <param name="Role">"system" | "user" | "assistant" | "tool".</param>
/// <param name="ToolCallId">Yalnızca "tool" rolünde: cevaplanan çağrının id'si.</param>
public sealed record AgentChatMessage(
    string Role,
    string? Content,
    IReadOnlyList<AgentToolCall>? ToolCalls = null,
    string? ToolCallId = null);

public sealed record AgentChatResponse(string? Content, IReadOnlyList<AgentToolCall> ToolCalls);

/// <summary>
/// Araç çağırabilen ham sohbet arayüzü.
///
/// <b>Neden <see cref="IAIService"/>'ten ayrı:</b> IAIService görev odaklı
/// ("şema üret", "veri üret") ve turu kendi içinde bitiriyor. Bu arayüz ise
/// turu ÇAĞIRANIN yönettiği ham bir kanal — mesaj geçmişi ve araç sonuçları
/// dışarıda tutuluyor. İkisini birleştirmek, orkestrasyonu AI servisinin içine
/// gömerdi; oysa turu bitirme kararı deterministik tarafta kalmalı.
/// </summary>
public interface IAgentChatClient
{
    /// <param name="maxOutputTokens">
    /// Bu ÇAĞRININ ihtiyacı kadar çıktı tavanı; <c>null</c> ise planın genel
    /// tavanı kullanılır.
    ///
    /// <b>Neden çağrı başına:</b> sağlayıcı dakika başına token sınırını
    /// harcanan değil <b>İSTENEN</b> <c>max_tokens</c> üzerinden sayıyor.
    /// Dokuz tabloluk bir parça için planın 32.000'lik tavanını istemek,
    /// 8.000'lik dakikalık bütçeyi tek istekte aşıp 429 alıyor — yani parça
    /// üretimi hiç başlamıyor. Canlı testte tam olarak bu oldu. Her çağrı
    /// gerçekten ihtiyacı kadar isteyince aynı bütçeye birkaç parça sığıyor.
    /// </param>
    Task<AgentChatResponse> CompleteAsync(
        IReadOnlyList<AgentChatMessage> messages,
        IReadOnlyList<AgentToolDefinition> tools,
        double temperature,
        CancellationToken cancellationToken = default,
        int? maxOutputTokens = null);
}
