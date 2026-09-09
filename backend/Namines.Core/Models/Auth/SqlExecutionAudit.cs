using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth;

/// <summary>
/// `/api/executor/execute` üzerinden çalıştırılan her SQL betiğinin kaydı.
///
/// <b>Neden gerekli:</b> Bu uç, ürünün geri kalanının bilerek kurduğu
/// yönetişim zincirinin (ChangeRequest → risk sınıflandırması → onay →
/// <see cref="ChangeRequestAuditLog"/>) yanından geçiyor: kullanıcı kendi
/// bağlantı dizesini verip keyfi DDL çalıştırabiliyor. Bu yeteneğin kendisi
/// meşru — "ürettiğim şemayı kendi veritabanıma bas" ürünün çekirdek akışı.
/// Meşru olmayan, o işlemin <b>hiçbir iz bırakmaması</b>ydı.
///
/// Yıkıcı bir DDL'den sonra "kim, ne zaman, hangi sunucuda çalıştırdı"
/// sorusunun cevabı olmadan olay incelemesi yapılamaz.
///
/// <b>Betiğin TAMAMI saklanmıyor.</b> Betik kullanıcının şeması — çoğu zaman
/// ticari sır, bazen içinde veri. Onu kendi veritabanımıza kopyalamak, tek bir
/// denetim özelliği uğruna yeni bir sızıntı yüzeyi açmak olurdu. Bunun yerine:
/// <b>hash</b> (aynı betiğin tekrar çalıştığını görmek için) ve
/// <b>kısaltılmış ön ek</b> (ne tür bir işlem olduğunu anlamak için).
///
/// <b>Bağlantı dizesi HİÇ saklanmıyor</b> — yalnızca host ve veritabanı adı.
/// Parola bir denetim kaydında bulunmamalı; kayıt genellikle ana veriden daha
/// uzun yaşar ve daha çok kişi tarafından okunur.
/// </summary>
public class SqlExecutionAudit
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Çalıştıran kullanıcı. Kullanıcı silinse bile kayıt kalmalı, bu yüzden
    /// yabancı anahtar değil düz metin — denetim kaydının tam olarak lazım
    /// olduğu anda kaybolması, hiç tutulmamasıyla aynı kapıya çıkar.
    /// </summary>
    public string UserId { get; set; } = null!;

    /// <summary>Biliniyorsa ilgili proje; bu uç proje bağı zorunlu kılmıyor.</summary>
    public string? ProjectId { get; set; }

    /// <summary>Hedef sunucu — parola ve kullanıcı adı HARİÇ.</summary>
    public string? TargetHost { get; set; }

    public string? TargetDatabase { get; set; }

    public string DbType { get; set; } = null!;

    /// <summary>Betiğin SHA-256 hash'i — aynı betiğin tekrarını tanımak için.</summary>
    public string ScriptHash { get; set; } = null!;

    /// <summary>Betiğin ilk 500 karakteri. Tamamı bilerek saklanmıyor (bkz. sınıf notu).</summary>
    public string ScriptPreview { get; set; } = null!;

    public int ScriptLength { get; set; }

    /// <summary>
    /// Betikte yıkıcı bir anahtar kelime (DROP / TRUNCATE / DELETE …) geçiyor mu.
    ///
    /// Kaba bir sinyal ve öyle olduğu biliniyor — bir yorumun içindeki "DROP"
    /// da bunu işaretler. Yine de değerli: denetim kaydını sonradan tararken
    /// "önce riskli olanlara bak" demeyi mümkün kılıyor. Yetki kararı buna
    /// dayanmıyor, yalnızca görünürlük.
    /// </summary>
    public bool ContainsDestructiveKeyword { get; set; }

    public bool Success { get; set; }

    public int StatementsExecuted { get; set; }

    /// <summary>Kısmî uygulama mümkün müydü (motor DDL'i transaction dışı işliyorsa).</summary>
    public bool PartialApplyPossible { get; set; }

    /// <summary>Sınıflandırılmış hata mesajı — ham sürücü metni değil.</summary>
    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
