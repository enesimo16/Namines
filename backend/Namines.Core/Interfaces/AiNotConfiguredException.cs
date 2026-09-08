using System;

namespace Namines.Core.Interfaces;

/// <summary>
/// AI sağlayıcısının anahtarı hiç tanımlanmamış.
///
/// <b>Ayrı bir tip olmasının sebebi:</b> bu bir arıza değil, eksik kurulum — ve
/// ikisi kullanıcı için taban tabana zıt. Genel bir <see cref="Exception"/> olarak
/// fırlatıldığında istek 500'e dönüşüyordu; arayüz de 500'ü "beklenmedik hata"
/// sayıp sessizce yutuyordu, yani kullanıcı hiçbir şey görmüyordu.
///
/// <b>Ağ çağrısından ÖNCE fırlatılır.</b> Anahtar yokken istek yine de Groq'a
/// gidiyordu: yaklaşık 10 saniye bekleniyor, dış servise gereksiz yük biniyor ve
/// sonuç yine hataydı. Yapılandırma eksikliği yerel olarak bilinen bir şey.
/// </summary>
public sealed class AiNotConfiguredException : Exception
{
    public AiNotConfiguredException(string provider)
        : base($"{provider} is not configured on this server. " +
               "Add an API key to enable AI features, or pick a model that runs locally.")
    {
        Provider = provider;
    }

    public string Provider { get; }
}
