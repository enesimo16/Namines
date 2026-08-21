namespace Namines.Core.Models;

/// <summary>
/// G13 — "Etkilenen API/UI statik tahmini" (new-phase/28-IMPACT-ANALYSIS-ENGINE.md §5).
/// Dürüstlük notu doc'ta açık: Gateway/Console olmadan bu katman TAHMİNDİR, kesin değil —
/// bir dosyada bir tablo/kolon ADI geçmesi, o dosyanın gerçekten o kolona bağımlı olduğu
/// anlamına gelmez (yanlış pozitif olabilir; yorum, string literal, aynı isimde başka bir
/// şey olabilir). UI bunu "olası etki" diye göstermeli, "kesin etki" değil.
/// </summary>
public sealed record AffectedCodeMatch(
    string FileName,
    int LineNumber,
    string MatchedIdentifier,
    string LineText);
