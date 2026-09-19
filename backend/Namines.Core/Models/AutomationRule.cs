using System;
using System.Collections.Generic;

namespace Namines.Core.Models;

/// <summary>
/// Namines Flow kuralının sunucu tarafı kaydı (Bölüm 3).
///
/// İstemcideki <c>useAutomationStore.ts</c>'in kalıcı karşılığı — ScopeTableId
/// istemcinin stabil <c>SchemaTable.Id</c>'sini tutar, TABLO ADINI DEĞİL
/// (bkz. spec "Bölüm 3 Eki #2" — SchemaDiffResult ada göre çalışır, eşleştirme
/// bu ikisini AutomationRuleMatcher içinde birbirine çevirir).
///
/// <b>Bir kural = bir tetikleyici + koşullar + SIRALI aksiyon listesi.</b>
/// Aksiyon önceden kuralın kendi alanlarındaydı (tek aksiyon); "flow" adını
/// hak etmesi için ayrı bir varlığa taşındı.
/// </summary>
public class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Kullanıcının verdiği ad. Boşsa arayüz tetikleyiciden bir özet üretir.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Null = proje genelinde. Doluysa, o TABLO'ya ait diff girdileriyle sınırlı.</summary>
    public string? ScopeTableId { get; set; }

    /// <summary>"TableAdded" | "TableDeleted" | "ColumnAdded" | "ColumnDeleted" | "ColumnChanged" | "RelationAdded" | "RelationDeleted"</summary>
    public string TriggerType { get; set; } = string.Empty;

    /// <summary>
    /// <c>[{"field":"columnName","op":"endsWith","value":"_id"}]</c> biçiminde
    /// koşul dizisi; aralarında VE mantığı. Boş dizi = koşulsuz, her zaman eşleşir
    /// (koşul kavramından önce oluşturulmuş kurallar bu yüzden aynen çalışmaya
    /// devam ediyor). Yorumlanması: <see cref="Analysis.AutomationConditionEvaluator"/>.
    /// </summary>
    public string ConditionsJson { get; set; } = "[]";

    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Sıralı aksiyon zinciri. Bir adımın hatası zinciri kesmez.</summary>
    public List<AutomationAction> Actions { get; set; } = new();
}

/// <summary>
/// Bir kuralın tetiklendiğinde çalıştıracağı tek bir adım.
///
/// Ayrı varlık olmasının sebebi çoklu aksiyon: "kolon silinirse beni uyar VE
/// webhook at VE DBA kontrolü çalıştır" tek bir kuralla ifade edilebilsin.
/// </summary>
public class AutomationAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RuleId { get; set; } = string.Empty;

    /// <summary>Zincirdeki sıra. ("Order" PostgreSQL'de ayrılmış sözcük.)</summary>
    public int SortOrder { get; set; }

    /// <summary>"Webhook" | "DbaCheck" | "SeedData" | "Toast"</summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>Örn. {"url": "https://..."}. Serbest JSON — aksiyon tipine göre yorumlanır.</summary>
    public string ActionConfigJson { get; set; } = "{}";
}

/// <summary>Bir aksiyonun tek bir çalıştırmasının kaydı — teşhis ve kullanıcıya sonuç göstermek için.</summary>
public class AutomationRunLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RuleId { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Kaydın hangi adıma ait olduğu. Bir kuralda artık birden fazla aksiyon
    /// olabildiği için, bu olmadan geçmişte hangi satırın neyi anlattığı
    /// ayırt edilemezdi.
    /// </summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>"Success" | "Failed" | "Skipped"</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Kullanıcının "şimdi test et" ile elle tetiklediği çalıştırma mı?</summary>
    public bool IsTest { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>DBA/seed sonucunun kısa özeti. Webhook/Toast için null.</summary>
    public string? ResultSummary { get; set; }
}
