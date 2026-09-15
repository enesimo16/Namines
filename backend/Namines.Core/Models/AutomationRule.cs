using System;

namespace Namines.Core.Models;

/// <summary>
/// Namines Flow kuralının sunucu tarafı kaydı (Bölüm 3).
///
/// İstemcideki <c>useAutomationStore.ts</c>'in kalıcı karşılığı — ScopeTableId
/// istemcinin stabil <c>SchemaTable.Id</c>'sini tutar, TABLO ADINI DEĞİL
/// (bkz. spec "Bölüm 3 Eki #2" — SchemaDiffResult ada göre çalışır, eşleştirme
/// bu ikisini AutomationRuleMatcher içinde birbirine çevirir).
/// </summary>
public class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Null = proje genelinde. Doluysa, o TABLO'ya ait diff girdileriyle sınırlı.</summary>
    public string? ScopeTableId { get; set; }

    /// <summary>"TableAdded" | "TableDeleted" | "ColumnAdded" | "ColumnDeleted" | "ColumnChanged" | "RelationAdded" | "RelationDeleted"</summary>
    public string TriggerType { get; set; } = string.Empty;

    /// <summary>"Webhook" | "DbaCheck" | "SeedData" | "Toast"</summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>Örn. {"url": "https://..."}. Serbest JSON — aksiyon tipine göre yorumlanır.</summary>
    public string ActionConfigJson { get; set; } = "{}";

    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Bir kuralın tek bir çalıştırmasının kaydı — teşhis ve kullanıcıya sonuç göstermek için.</summary>
public class AutomationRunLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RuleId { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;

    /// <summary>"Success" | "Failed" | "Skipped"</summary>
    public string Status { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    /// <summary>DBA/seed sonucunun kısa özeti. Webhook/Toast için null.</summary>
    public string? ResultSummary { get; set; }
}
