using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth;

/// <summary>
/// Kullanıcının ada kaydettiği SQL sorgusu (F-02).
///
/// <b><see cref="SqlQueryHistoryEntry"/>'den farkı bir alan değil, bir SÖZ:</b>
/// geçmiş kendiliğinden birikir ve kendiliğinden silinir (100 kayıt sınırı);
/// kaydedilmiş sorgu kullanıcının "bunu sakla" dediği şeydir ve <b>silinmez</b>.
/// İkisini tek tabloda bir <c>IsSaved</c> bayrağıyla birleştirmek, temizlik
/// mantığının bir gün o bayrağı atlamasına ve kullanıcının kasten sakladığı
/// sorgunun kaybolmasına bir adım kalması demekti.
///
/// <b>Paylaşım şimdilik YOK.</b> Ürünün organizasyon modeli var ve F-02
/// "ekip paylaşımı eklenirse farklılaştırır" diyor — ama paylaşılan bir sorgu,
/// başkasının çalıştıracağı bir metin demek; kimin hangi projede ne
/// çalıştırabileceği kararı Owner kapısına bağlı ve o kapıyı dolaylı olarak
/// açan bir özelliği ölçmeden eklemek doğru değil. Alan olarak da eklenmedi:
/// kullanılmayan bir <c>IsShared</c> sütunu, "paylaşım var" izlenimi verir.
/// </summary>
public class SavedQuery
{
    /// <summary>Kullanıcı + proje başına en fazla kaydedilmiş sorgu.</summary>
    public const int MaxPerUserPerProject = 200;

    public const int MaxNameLength = 120;
    public const int MaxSqlLength = 20000;

    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ProjectId { get; set; } = null!;

    /// <summary>Sahibi. Listeleme ve silme YALNIZCA buna bağlı.</summary>
    public string UserId { get; set; } = null!;

    [MaxLength(MaxNameLength)]
    public string Name { get; set; } = null!;

    public string Sql { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
