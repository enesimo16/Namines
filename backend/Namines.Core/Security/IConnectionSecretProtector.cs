namespace Namines.Core.Security;

/// <summary>
/// Veritabanı bağlantı dizesini <b>at-rest şifreler</b>.
///
/// <b>Neden gerekti:</b> Namines'in tasarım düzlemi bağlantı dizesini bilinçli
/// olarak HİÇ saklamıyordu — her istekte bir kez kullanılıp atılıyordu (bkz.
/// <c>GatewayController</c>). Bu, tasarım aracı için doğru bir güvenlik
/// özelliğiydi. Ama <b>Namines Desk</b> barındırılan bir panel
/// (<c>/&lt;kullanıcı&gt;/&lt;proje&gt;</c>): bağlantı saklanmazsa tarayıcının her
/// istekte veritabanı parolasını göndermesi gerekirdi — yani parola istemcide
/// yaşardı. Kabul edilemez.
///
/// Çözüm: bağlantı sunucuda şifreli durur, API anahtarından çözülür, tarayıcı
/// hiç görmez.
///
/// <b>Neden ayrı bir arayüz:</b> anahtar yönetimi (bugün yapılandırmadan gelen
/// bir sır, yarın KMS / ASP.NET Data Protection) değişecek bir karar. Çağıran
/// kod bunu bilmemeli — uygulama değişse de <see cref="Protect"/> /
/// <see cref="Unprotect"/> sözleşmesi sabit kalır.
/// </summary>
public interface IConnectionSecretProtector
{
    /// <summary>Düz metni şifreler. Çıktı, sürüm etiketi taşır (algoritma değişimi mümkün olsun diye).</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Şifreli metni çözer. Kurcalanmış/bozuk veri veya anahtar değişmişse
    /// <see cref="System.Security.Cryptography.CryptographicException"/> fırlatır —
    /// sessizce boş dönmez: yanlış bağlantıyla yanlış veritabanına bağlanmak,
    /// hata vermekten çok daha kötüdür.
    /// </summary>
    string Unprotect(string ciphertext);
}
