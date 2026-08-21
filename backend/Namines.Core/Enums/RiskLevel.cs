namespace Namines.Core.Enums;

/// <summary>
/// Bir şema değişikliğinin genel risk seviyesi. <see cref="Namines.Core.Analysis.SchemaImpactAnalyzer"/>
/// ve ileride <c>MigrationService</c> (G8.2) tarafından paylaşılan ortak sözlük — bkz.
/// new-phase/11-MIGRATIONS-BRANCHING.md §2 risk sınıflandırma tablosu.
///
/// Sıralama önemli: <see cref="SchemaImpactAnalyzer"/> genel riski MAX ile hesaplar
/// (ortalama değil) — tek bir Breaking bulgu, 10 Safe bulguyu ezer.
/// </summary>
public enum RiskLevel
{
    /// <summary>Veri kaybı yok, geri alınabilir, API/istemci sözleşmesi bozulmaz.</summary>
    Safe = 0,

    /// <summary>Uzun kilit/tarama süresi veya koşullu başarısızlık riski var, ama veri kaybı yok.</summary>
    Risky = 1,

    /// <summary>Veri kalıcı olarak kaybolabilir veya geri alınamaz.</summary>
    Destructive = 2,

    /// <summary>Mevcut API/istemci sözleşmesini kırar (rename, tip daraltma) veya DDL motor tarafından reddedilir.</summary>
    Breaking = 3
}
