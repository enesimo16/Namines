namespace Namines.Core.Models;

/// <summary>
/// Şema üretim hattının tek bir adımı — üretim ekranına akış hâlinde
/// gönderiliyor (bkz. second-phase/04-LOADING-EKRANI.md).
///
/// <b>Neden bu tip var:</b> hattın gerçekte ne yaptığı (taslak üret, hedef
/// motorda derle, bulgu bulundu, düzelt) kullanıcıya hiç görünmüyordu — sonuç
/// gelene kadar ekranda dönen bir çark vardı. Oysa bu adımlar ürünün en özgün
/// tarafı: AI üretiyor, kural motoru + gerçek DDL derleyicisi denetliyor. Bu
/// görünmezse ürün "bir de AI şema üretiyor"dan farksız görünüyor.
/// </summary>
/// <param name="Kind">
/// Makine tarafından okunacak durum: "draft" | "inspect" | "finding" |
/// "repair" | "clean" | "done". Ön yüz buna göre ikon seçiyor (⟳/⚠/✓).
/// </param>
/// <param name="Message">İnsan tarafından okunacak, Türkçe/İngilizce karışık olmayan tek cümle.</param>
public sealed record AgentStep(string Kind, string Message)
{
    public static AgentStep Draft(string message) => new("draft", message);
    public static AgentStep Inspect(string message) => new("inspect", message);
    public static AgentStep Finding(string message) => new("finding", message);
    public static AgentStep Repair(string message) => new("repair", message);
    public static AgentStep Clean(string message) => new("clean", message);
}
