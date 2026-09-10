using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Core.Models.Auth;

namespace Namines.API.Security;

/// <summary>
/// Parolanın bilinen bir veri sızıntısında geçip geçmediğini kontrol eder
/// (Have I Been Pwned — "Pwned Passwords" aralık API'si).
///
/// <b>Neden karmaşıklık kuralı değil bu:</b> `RequireUppercase`,
/// `RequireDigit` gibi kurallar kullanıcıyı <c>Parola1!</c> yazmaya iter —
/// yani kuralı sağlayan ama saldırganın sözlüğünde ilk sıralarda olan bir
/// parolaya. NIST SP 800-63B bu yüzden karmaşıklık zorunluluklarını
/// ÖNERMİYOR; onun yerine uzunluk ve <b>sızdırılmış parola listesi kontrolü</b>
/// öneriyor. Uzunluk 12'ye çıkarıldı; bu sınıf ikinci yarısı.
///
/// <b>Parola sunucudan ÇIKMIYOR — k-anonimlik:</b> parolanın SHA-1'i alınıp
/// yalnızca <b>ilk 5 karakteri</b> gönderiliyor. API o ön eke uyan bütün
/// hash'lerin son 35 karakterini döndürüyor (tipik olarak ~800 kayıt) ve
/// eşleştirme <b>burada, yerelde</b> yapılıyor. HIBP hangi parolanın
/// sorulduğunu bilemiyor.
///
/// <b>SHA-1 kullanılması bir güvenlik tercihi değil, protokol zorunluluğu.</b>
/// Parola saklama burada SHA-1'e geçmiyor — o hâlâ Identity'nin PBKDF2'si.
/// SHA-1 yalnızca HIBP'nin veri kümesinin biçimi olduğu için, tek seferlik ve
/// bellekte kullanılıyor.
///
/// <b>Ağ hatasında AÇIK kalıyor (fail-open) ve bu bilinçli:</b> kapalı kalsaydı
/// HIBP'nin kesintisi Namines'in kayıt akışını durdururdu — üçüncü bir tarafın
/// çalışma süresine kimlik sistemimizi bağlamak, engellediği riskten büyük bir
/// risk. Ama sessiz kalmıyor: her başarısızlık <c>LogWarning</c> ile
/// kaydediliyor, çünkü sessizce devre dışı kalan bir kontrol, olmayan bir
/// kontroldür.
/// </summary>
public sealed class PwnedPasswordValidator : IPasswordValidator<ApplicationUser>
{
    /// <summary>Identity'nin kullanıcıya gösterdiği hata kodu.</summary>
    public const string ErrorCode = "PwnedPassword";

    /// <summary>
    /// HIBP aralık API'si tek bir istekte ~800 hash döndürüyor; kısa bir zaman
    /// aşımı yeterli. Uzun tutmak, kayıt akışını bekletmek demek olurdu.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// İstemcinin adı. <b>Adlandırılmış istemci ZORUNLU, tipli istemci
    /// yetmiyor</b> — ve bu, canlı denemede öğrenildi:
    ///
    /// <c>AddPasswordValidator&lt;T&gt;</c>, T'yi Identity'nin kendi
    /// kaydıyla (<c>AddScoped&lt;IPasswordValidator&lt;TUser&gt;, T&gt;</c>)
    /// oluşturuyor — <c>AddHttpClient&lt;T&gt;</c>'nin tipli fabrikasıyla
    /// DEĞİL. Sonuç: kurucuya enjekte edilen <c>HttpClient</c>, isimsiz
    /// varsayılan istemci oluyor ve <c>BaseAddress</c>'i YOK.
    ///
    /// Bunun bedeli sessizlikti: kod derlendi, 7 birim testi geçti, uygulama
    /// açıldı — ve her istek <c>"BaseAddress must be set"</c> ile fail-open'a
    /// düşüp <b>her parolayı kabul etti</b>. Özellik tamamen ölüydü ve
    /// yalnızca gerçek bir kayıt denemesi bunu gösterdi.
    /// </summary>
    internal const string HttpClientName = "pwned-passwords";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<PwnedPasswordValidator> _logger;
    private readonly bool _enabled;

    public PwnedPasswordValidator(
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        ILogger<PwnedPasswordValidator> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;

        // Kapatılabilir olmalı: air-gapped kurulumlarda dış ağ yok ve orada
        // kontrol her seferinde fail-open'a düşüp gereksiz uyarı üretirdi.
        // Varsayılan AÇIK — güvenlik özelliği açıkça kapatılmalı, açıkça
        // açılmamalı.
        _enabled = configuration.GetValue("Security:PwnedPasswordCheck:Enabled", defaultValue: true);
    }

    public async Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (!_enabled || string.IsNullOrEmpty(password))
            return IdentityResult.Success;

        var breachCount = await GetBreachCountAsync(password);

        // -1 = kontrol yapılamadı (ağ hatası). Fail-open — bkz. sınıf notu.
        if (breachCount <= 0)
            return IdentityResult.Success;

        return IdentityResult.Failed(new IdentityError
        {
            Code = ErrorCode,
            // Kaç kez göründüğü SÖYLENİYOR: "bu parola zayıf" soyut bir uyarı,
            // "bu parola sızıntılarda 47.000 kez göründü" davranış değiştiren
            // bir bilgi.
            // InvariantCulture: mesaj Ingilizce, sayi da Ingilizce bicimde
            // olmali. Sunucunun kulturu tr-TR oldugunda "N0" bicimi 42.513
            // uretiyordu -- Ingilizce bir cumlenin ortasinda kirk iki nokta
            // bes yuz on uc gibi okunan bir sayi. Bu depoda daha once bir
            // Turkce kultur hatasi uretime kadar gitti (bkz. AGENTS.md G44);
            // kullaniciya donen her metinde kultur ACIKCA secilmeli.
            Description = string.Format(
                CultureInfo.InvariantCulture,
                "This password has appeared in {0:N0} known data breaches and cannot be used. " +
                "Choose a different password — length matters more than symbols.",
                breachCount),
        });
    }

    /// <summary>
    /// Parolanın kaç sızıntıda geçtiğini döndürür.
    /// 0 = bulunamadı · &gt;0 = bulundu · <b>-1 = kontrol edilemedi</b>.
    /// </summary>
    private async Task<int> GetBreachCountAsync(string password)
    {
        // SHA-1 yalnızca HIBP protokolü için; parola saklama bununla YAPILMIYOR.
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var prefix = hash[..5];
        var suffix = hash[5..];

        try
        {
            using var cts = new CancellationTokenSource(RequestTimeout);
            var http = _httpFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"range/{prefix}");

            // Add-Padding: yanıtı sabit boyuta yaklaştırıyor. Olmadan, yanıtın
            // uzunluğu hangi ön ekin sorulduğu hakkında bilgi sızdırabiliyor —
            // k-anonimliğin trafik analiziyle zayıflatılmasına karşı önlem.
            request.Headers.Add("Add-Padding", "true");

            using var response = await http.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cts.Token);
            return FindCount(body, suffix);
        }
        catch (Exception ex)
        {
            // Sessiz kalmıyor: devre dışı kalan bir güvenlik kontrolü
            // görünmezse hiç yok demektir.
            // Error seviyesi, Warning DEGIL. Bu kontrol bir kez sessizce
            // devre disi kaldi (yanlis HttpClient kaydi) ve fark edilmesi
            // canli bir kayit denemesi gerektirdi. Devre disi kalan bir
            // guvenlik kontrolu, gurultu yapmali.
            _logger.LogError(ex,
                "Sizdirilmis parola kontrolu YAPILAMADI; parola KABUL EDILDI (fail-open). " +
                "Bu satiri goruyorsaniz kontrol calismiyor demektir. Dis ag erisimini " +
                "kontrol edin ya da Security:PwnedPasswordCheck:Enabled=false ile ACIKCA kapatin.");
            return -1;
        }
    }

    /// <summary>
    /// Yanıt gövdesi <c>SUFFIX:COUNT</c> satırlarından oluşuyor. Padding
    /// eklenmiş sahte kayıtların sayısı 0'dır ve doğal olarak elenirler.
    /// </summary>
    internal static int FindCount(string body, string suffix)
    {
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;

            if (!suffix.AsSpan().Equals(line.AsSpan(0, separator).Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            return int.TryParse(line.AsSpan(separator + 1).Trim(), out var count) ? count : 0;
        }

        return 0;
    }
}
