using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.Core.Analysis;
using Namines.Infrastructure.Data;

namespace Namines.API.Services;

/// <summary>
/// Açılışta, ESKİDEN kaydedilmiş "4096" (ya da altı) <c>maxTokens</c> tercihini
/// temizleyen tek seferlik veri düzeltmesi (final whole-branch review I5).
///
/// <b>Çözdüğü sorun:</b> <see cref="AiAdvancedSettings.MaxTokens"/>'ın kod
/// içi varsayılanı "4096"'dan "32000"e çıkarıldı — ama bu yalnızca "Advanced
/// AI Tuning" panelini HİÇ AÇMAMIŞ kullanıcıları kurtarıyor. Panel bir kez
/// bile açılıp kaydedildiyse (ön yüz TÜM alanları gönderir), veritabanında
/// kalıcı <c>"maxTokens":"4096"</c> yazıyor ve <see cref="AiAdvancedSettings.MaxTokensFor"/>
/// bunu `Math.Min(4096, planTavanı)` ile HER ZAMAN 4096'ya kilitliyor — Pro/Team
/// kullanıcısı bile. Bu hesaplar orijinal bildirilen hatayı ("kapsamlı istek,
/// 5-6 tablo") aynen tekrar üretiyor ve parçalı üretimle artık her PARÇA için de
/// aynı şekilde kesiliyor.
///
/// <b>Neden bir "algılama sentinela" yerine tek seferlik düzeltme (denetleyicinin
/// kararı):</b> "4096 = ayarlanmamış" diye okuma anında YORUMLAMAK, 4096'yı
/// BİLEREK seçmiş bir kullanıcıyı sonsuza dek görmezden gelirdi ve bu özel
/// durum koda kalıcı olarak yerleşirdi. Bir kerelik düzeltme "ayarlanmamış =
/// varsayılan" ilkesine sadık kalıyor ve kendi kendini sınırlıyor: bir kez
/// çalışıp biter, kodda kalıcı bir istisna bırakmaz.
///
/// <b>Idempotent:</b> bir satır düzeltildikten sonra <c>MaxTokensValue</c>
/// artık &gt; 4096 olacağından bir sonraki çalıştırmada bir daha eşleşmez.
///
/// <b>Diğer alanlara DOKUNMUYOR:</b> tüm JSON <see cref="AiAdvancedSettings"/>
/// olarak ayrıştırılıp yalnızca <c>MaxTokens</c> alanı değiştirilip yeniden
/// yazılıyor — kullanıcının <c>namingConvention</c>, <c>fkAction</c> vb. diğer
/// tercihleri AYNEN korunuyor.
/// </summary>
public static class AiAdvancedSettingsFixup
{
    /// <summary>
    /// Eski varsayılanın (4096) bu değerin ALTINDA ya da EŞİT olduğu her satır
    /// düzeltilir — kasıtlı olarak yalnızca "== 4096" değil: eski varsayılanın
    /// altında bir değer de aynı "hiç dokunulmamış" izlenimini taşıyabilir ve
    /// denetleyicinin ruling'i "eski 4096 varsayılanında ya da altında" diyor.
    /// </summary>
    private const int StaleDefaultThreshold = 4096;

    public static async Task RunAsync(Namines.BuildingBlocks.Contracts.IAiPolicies policies, ILogger logger, CancellationToken ct = default)
    {
        // Yalnızca kayıtlı bir tercih JSON'u OLAN satırlar aday: null/boş zaten kod içi varsayılanı (32000) kullanıyor.
        var fixedCount = await policies.RewriteAdvancedJsonAsync(json =>
        {
            AiAdvancedSettings settings;
            try { settings = AiAdvancedSettings.Parse(json); }
            catch { return null; } // bozuk JSON: bu satırı atla, bir düzeltme kaydın tamamını riske atmasın

            if (settings.MaxTokensValue > StaleDefaultThreshold)
                return null; // zaten düzeltilmiş ya da kullanıcı yüksek bir değer seçmiş

            // Yalnızca maxTokens değişiyor; diğer tüm alanlar aynı `settings` nesnesinden AYNEN korunuyor.
            return (settings with { MaxTokens = AiAdvancedSettings.Default.MaxTokens }).ToJson();
        }, ct);

        if (fixedCount > 0)
        {
            logger.LogWarning(
                "{Count} kullanıcının kalıcı 'maxTokens' tercihi eski varsayılana ({Threshold}) sıkışmıştı; " +
                "kod içi varsayılana ({New}) sıfırlandı.",
                fixedCount, StaleDefaultThreshold, AiAdvancedSettings.Default.MaxTokens);
        }
        else
        {
            logger.LogDebug("AiAdvancedSettings düzeltmesi: eski 'maxTokens' değerine sahip kayıt yok.");
        }
    }
}
