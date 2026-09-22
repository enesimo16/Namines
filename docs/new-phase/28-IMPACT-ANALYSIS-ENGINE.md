# 28 — Impact Analysis Engine

> Lifecycle pivot'un ([27](27-LIFECYCLE-PIVOT.md)) merkezi bileşeni. "Bu değişiklik neyi
> etkiler?" sorusuna yapısal, kanıtlanabilir bir cevap üretir — AI'ın SQL yazması değil,
> **değişikliğin sonuçlarını anlaması** burada gerçekleşir.

---

## 1. Neden ayrı bir motor

`FkCascadeAnalyzer` (G3, `Namines.Core/Analysis/FkCascadeAnalyzer.cs`) tek bir soruya
cevap veriyor: *"cascade davranışı çalışır mı?"* Impact Analysis bunu genelleştirir:
*"bu şema değişikliği sistemin geri kalanını nasıl etkiler?"* — tablo/kolon/ilişki/
index/constraint/API/uygulama seviyesinde.

**Girdi:** iki `DatabaseSchema` sürümü (eski, yeni) + opsiyonel canlı DB istatistikleri
**Çıktı:** yapılandırılmış bir `ImpactReport` — AI bunu SONRADAN insan diline çevirir,
kendisi SQL yazmaz, ham analiz deterministiktir.

```
                     ┌─────────────────────────┐
NSL/Schema v(n)  ──▶ │                         │
                     │  SchemaImpactAnalyzer   │──▶ ImpactReport (yapılandırılmış)
NSL/Schema v(n+1)──▶ │                         │         │
                     └─────────────────────────┘         ▼
                                              ┌───────────────────────┐
                                              │ AI Impact Explainer   │──▶ İnsan-okunur özet
                                              │ (ayrı ajan, G8.9)     │
                                              └───────────────────────┘
```

**Kural:** Motor **deterministik**. AI, motorun ürettiği yapıyı süsler/açıklar; asla
kendi başına "şu tablo etkilenir" demez. Bu ayrım güven için zorunlu — kullanıcı "AI
yanıldı" değil "motor kanıtladı, AI özetledi" hissi almalı.

---

## 2. `ImpactReport` yapısı

```csharp
public sealed record ImpactReport(
    IReadOnlyList<AffectedTable> AffectedTables,
    IReadOnlyList<AffectedRelation> AffectedRelations,
    IReadOnlyList<AffectedIndex> AffectedIndexes,
    IReadOnlyList<BreakingChange> BreakingChanges,
    IReadOnlyList<DataLossRisk> DataLossRisks,
    IReadOnlyList<MigrationLockRisk> LockRisks,
    IReadOnlyList<MissingIndexSuggestion> IndexSuggestions,
    RollbackAssessment Rollback,
    RiskLevel OverallRisk);              // Safe | Risky | Destructive | Breaking

public sealed record AffectedTable(
    string TableName, ChangeKind Kind,   // Added | Removed | Modified | RenamedFrom
    IReadOnlyList<string> ChangedColumns,
    int? EstimatedRowCount);             // varsa canlı DB'den

public sealed record BreakingChange(
    string Description, BreakingChangeKind Kind,  // ColumnRemoved | ColumnRenamed | TypeNarrowed | TableRemoved
    string? SuggestedMitigation);        // "30 gün deprecated alias tut" gibi

public sealed record DataLossRisk(
    string TableName, string? ColumnName, string Reason,
    int? EstimatedAffectedRows);

public sealed record MigrationLockRisk(
    string Operation, LockSeverity Severity,  // None | Brief | Blocking
    int? EstimatedDurationMs, string? SaferAlternative); // "CONCURRENTLY kullan" gibi

public sealed record RollbackAssessment(bool IsReversible, string? Reason);
```

Bu model [04-NSL-SCHEMA-IR.md §7](04-NSL-SCHEMA-IR.md)'deki Migration IR ile aynı aileden
— NSL geldiğinde `SchemaDiff`'in üstüne oturacak. NSL'den önce, mevcut `DatabaseSchema`
modeli üzerinde çalışacak şekilde yazılır (Faz 0'ın "mevcut modeli genişlet" prensibi).

---

## 3. Analiz katmanları (öncelik sırasıyla)

| # | Katman | Girdi | Faz 0'dan yeniden kullanılan |
|---|---|---|---|
| 1 | **Yapısal diff** | iki `DatabaseSchema` | `StableUuid` karşılaştırması (rename tespiti) |
| 2 | **Cascade/FK etkisi** | ilişki grafiği | `FkCascadeAnalyzer` doğrudan |
| 3 | **Kilit/süre tahmini** | operasyon türü + tahmini satır sayısı | yeni, ama [11-MIGRATIONS-BRANCHING.md §2](11-MIGRATIONS-BRANCHING.md) tablosu zaten var |
| 4 | **Veri kaybı riski** | DROP COLUMN/TABLE, tip daraltma | yeni |
| 5 | **Index önerisi** | FK kolonunda index var mı | `ConstraintSql`'in index modeliyle aynı veri yapısı |
| 6 | **Etkilenen dışa aktarımlar** | hangi export edilen tip/entity bu kolonu kullanıyor | yeni, statik — bkz. §5 |
| 7 | **Geri alınabilirlik** | her operasyon için ters operasyon var mı | [11 §4](11-MIGRATIONS-BRANCHING.md) rollback tablosu |

Katman 1-5 **Faz 0'ın üstüne doğrudan inşa edilir** — yeni altyapı gerektirmez, sadece
`FkCascadeAnalyzer`'ın kapsamı genişletilir ve yeni `MigrationLockRisk`/`DataLossRisk`
hesaplayıcıları eklenir. Katman 6 daha büyük bir iş (bkz. §5) ve minimal Gateway'den
(G8.8) sonra anlamlı hale gelir.

---

## 4. Risk skorlama

`OverallRisk` tek bir değer değil — **en kötü tekil bulgudan türetilir** (ortalama değil):

```
OverallRisk =
    max(
        BreakingChanges.Any() ? Breaking : Safe,
        DataLossRisks.Any()   ? Destructive : Safe,
        LockRisks.Any(l => l.Severity == Blocking) ? Risky : Safe,
        ...
    )
```

**Neden max, ortalama değil:** 10 güvenli değişiklik + 1 veri kaybı riski "ortalama
düşük risk" değil, "1 yüksek risk" olarak gösterilmeli. Bu, [11-MIGRATIONS-BRANCHING.md §2](11-MIGRATIONS-BRANCHING.md)'deki risk sınıflandırma tablosuyla birebir tutarlı.

---

## 5. Etkilenen API/UI tahmini (Katman 6 — G8.7)

Gateway tam olarak var olmadan bile (minimal salt-okunur REST, G8.8) faydalı bir
yaklaşım mümkün:

```
1. Proje için üretilmiş TypeScript tipleri / EF Core entity'leri varsa
   (Compile geçmişinden veya proje ayarlarından) tara.
2. Değişen kolon adı, o dosyalarda referans veriliyor mu? (basit metin/AST taraması)
3. "Bu değişiklik 3 dosyada derleme hatası üretebilir" gibi bir tahmin döndür.
```

Bu, [11-MIGRATIONS-BRANCHING.md §7](11-MIGRATIONS-BRANCHING.md)'deki Namines Bot'un
"kırılma analizi" özelliğiyle aynı fikir — GitHub entegrasyonu olmadan da, proje
içi export geçmişine bakarak basit bir versiyonu yapılabilir.

**Dürüstlük notu:** Gateway/Console olmadan bu katman **tahminidir**, kesin değil.
UI'da "olası etki" diye işaretlenmeli, "kesin etki" değil — yanlış güven vermemeli.

---

## 6. Test planı

Golden-file deseni ([20-TESTING-EVALS.md](20-TESTING-EVALS.md)) burada da geçerli:

```
tests/Namines.Tests/Analysis/ImpactAnalyzerTests.cs
  - Kolon eklendi (nullable)          → Safe, BreakingChanges boş
  - Kolon silindi                     → Destructive, DataLossRisks dolu
  - Kolon yeniden adlandırıldı        → Breaking (StableUuid ile rename tespiti)
  - Tip daraltıldı (VARCHAR(255)→(50))→ Risky/Destructive (veri kesilebilir)
  - FK eklendi, kolon index'siz       → IndexSuggestions dolu
  - Bileşik cascade yolu              → FkCascadeAnalyzer'dan devralınan Breaking
  - CONCURRENTLY olmadan index        → LockRisks: Blocking
```

Entegrasyon seviyesinde (G5 desenine uygun): tahmini kilit süresi gerçek DB'de
ölçülen süreyle karşılaştırılabilir (opsiyonel, ileri faz — kalibrasyon verisi
`costPerRow` sabitlerini gerçekçileştirir, bkz. [11 §2](11-MIGRATIONS-BRANCHING.md)).
