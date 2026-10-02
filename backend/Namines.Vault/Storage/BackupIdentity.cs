namespace Namines.Vault.Storage;

/// <summary>
/// Bir yedeğin kimliği — şifrelemenin AAD'sine giren ve dosyayı BİR KAYDA bağlayan
/// değer (NVLT3, bkz. <see cref="BackupCipher"/>).
///
/// <b>Neden gerekli (docs/last-review M-8):</b> şifreleme parolası SUNUCU GENELİ.
/// Bu, A projesinin şifreli yedeğinin B projesinin kaydındaki anahtarın yerine
/// konabilmesi demekti: dosya sorunsuz çözülüyordu, çünkü şifreleme dosyanın hangi
/// kayda ait olduğunu hiç kanıtlamıyordu. Bir kiracı, başka bir kiracının yedeğini
/// kendi "geri yükleme"sinde kullanabilirdi ve bu hiçbir yerde fark edilmezdi.
///
/// <b>Dosyaya YAZILMIYOR.</b> Kimlik kayıttan geliyor ve yalnızca AAD'de kullanılıyor;
/// dosyaya yazılsa onu değiştiren biri AAD'yi de kendi lehine kurar ve bağlama
/// anlamsız olurdu. Yanlış kimlikle çözme denemesi GCM etiketini bozuyor.
/// </summary>
/// <param name="ProjectId">Yedeğin ait olduğu proje.</param>
/// <param name="BackupId">
/// Yedek kaydının kimliği. Proje tek başına yetmez: aynı projenin iki yedeği
/// birbirinin yerine konabilirdi ve "dünkü yedeği bugünün kaydına koymak" da
/// sessiz bir veri değişimidir.
/// </param>
public sealed record BackupIdentity(string ProjectId, string BackupId);
