using System;

namespace Namines.Core.Security;

/// <summary>
/// Bir veritabanı bağlantı/çalıştırma hatasını, sürücünün HAM mesajını hiç
/// dışarı yansıtmadan birkaç güvenli kategoriye ayırır.
///
/// <b>Neden gerekli:</b> Npgsql, SqlClient, MySqlConnector ve Oracle sürücüleri
/// bağlantı hatalarında hedef host'u, portu ve bazen sunucu sürümünü mesaja
/// gömer. Bu metni istemciye döndüren bir uç, kimliği doğrulanmış herkes için
/// "şu host şu portta veritabanı koşuyor mu?" sorusuna cevap veren bir
/// <b>bağlantı kâşifine</b> dönüşür.
///
/// <b>Neden "bir hata oluştu" da değil:</b> kullanıcı yanlış parola ile
/// erişilemeyen host'u ayırt edemezse sorunu çözemez. Bu yüzden mesaj
/// kategorilere ayrılıyor — kategori bilgisi hedef hakkında bir şey
/// söylemiyor, kullanıcının kendi girdisi hakkında söylüyor.
///
/// <b>Ham mesaj yalnızca loga gider</b> (korelasyon ID'siyle birlikte), asla
/// yanıta.
/// </summary>
public static class DbConnectionFailure
{
    /// <summary>
    /// Bağlantı kurulamadığında döndürülecek güvenli mesaj.
    /// </summary>
    public static string Classify(Exception ex)
    {
        var m = ex.Message;

        if (m.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("login failed", StringComparison.OrdinalIgnoreCase))
            return "Authentication failed. Check the username and password in the connection string.";

        if (m.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            ex is TimeoutException)
            return "Connection timed out. Check the host, port, and firewall rules.";

        if (m.Contains("database", StringComparison.OrdinalIgnoreCase) &&
            (m.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
             m.Contains("cannot open", StringComparison.OrdinalIgnoreCase)))
            return "The database name in the connection string was not found on the server.";

        if (m.Contains("not allowed", StringComparison.OrdinalIgnoreCase))
            return "Connection target is not allowed (private or reserved address).";

        return "Could not connect to the database. Check the connection string and network access.";
    }

    /// <summary>
    /// Bağlantı kurulduktan SONRA, bir SQL ifadesi çalışırken oluşan hata.
    ///
    /// Burada sürücü mesajı <b>korunuyor</b> ve bu bilinçli bir ayrım:
    /// bağlantı kurulabildiyse çağıran zaten o hedefe erişebildiğini biliyor,
    /// dolayısıyla keşif değeri kalmıyor. Buna karşılık "42. satırda sözdizimi
    /// hatası" bilgisi kullanıcının betiğini düzeltebilmesi için şart —
    /// gizlemek, aracı kullanılamaz hâle getirirdi.
    /// </summary>
    public static string DescribeStatementFailure(Exception ex, int statementNumber) =>
        $"Error executing script at statement {statementNumber}: {ex.Message}";
}
