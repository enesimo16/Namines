namespace Namines.Core.Interfaces;

/// <summary>
/// Bir HTTP isteğinin TAMAMI için tek bir hız-sınırı bekleme bütçesi.
///
/// <b>Neden var:</b> bütçe ilk sürümde her sağlayıcı çağrısında yeniden
/// kuruluyordu. Ayarın adı <c>MaxTotalWaitSeconds</c> ve belgesi "bir istek
/// boyunca" diyordu, ama gerçekte sınır çağrı başınaydı. 54 tabloluk bir üretim
/// bir plan turu artı altı parça çağrısı yapıyor; her biri kendi 600 saniyesini
/// harcayabildiği için kullanıcıya söz verilen on dakika pratikte yetmiş dakikaya
/// kadar çıkabiliyordu — üstelik SSE bağlantısı açık dururken.
///
/// <b>Kapsam:</b> Scoped, yani istek başına tek örnek. Aynı istek içinde paralel
/// çalışan parçalar aynı <see cref="Policy"/> nesnesini paylaşıyor; bu yüzden
/// <see cref="AiRetryPolicy"/> iş parçacığı güvenli olmak zorunda.
///
/// <b>İstek bağlamı yoksa</b> (arka plan işleri, otomasyon işçisi) bu servis
/// çözümlenemez ve çağıran kendi yerel bütçesini kurar — orada zaten kullanıcı
/// beklemiyor ve paylaşılacak bir "istek" de yok.
/// </summary>
public sealed class AiRetryBudget
{
    public AiRetryBudget(AiRetryPolicy policy) => Policy = policy;

    public AiRetryPolicy Policy { get; }
}
