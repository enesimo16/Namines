namespace Namines.Core.Models;

/// <summary>
/// "Run Tests" (new-phase/29-DATABASE-CHANGE-REVIEW.md §4) sonucu — üretilen DDL'in
/// gerçek, ephemeral bir motor container'ında çalıştırılmasının kanıtı. Impact Analysis
/// bir TAHMİNdir; bu KANITTIR (bkz. G5'in "golden-file metni doğrular, Testcontainers
/// çalıştığını doğrular" ayrımı).
/// </summary>
public sealed record TestRunResult(
    bool Supported,
    bool Success,
    string? EngineMessage,
    string? FailedStatement,
    long DurationMs);
