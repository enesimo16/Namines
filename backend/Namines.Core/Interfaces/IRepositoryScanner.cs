using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Analysis;
using Namines.Core.Github;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

/// <param name="Schema">Okunabilen dosyalardan kurulan şema.</param>
/// <param name="Format">Tanınan format: "prisma" | "efcore" | "sql".</param>
/// <param name="Branch">Gerçekten okunan dal (kullanıcı seçmediyse varsayılan).</param>
/// <param name="ParsedFiles">İçeriği gerçekten okunan dosyalar.</param>
/// <param name="Skipped">Okunmayan her şey ve nedeni — kullanıcıya olduğu gibi gösterilir.</param>
/// <param name="TreeTruncated">
/// GitHub ağacı kesti mi. Kestiyse "deponda şema yok" demek yanlış olur:
/// bakmadığımız bir kısım var.
/// </param>
/// <param name="Warnings">
/// Sonuç üretildi ama güvenilirliği hakkında söylenmesi gereken şeyler.
///
/// <b><see cref="Skipped"/>'dan farkı:</b> orası okunAMAYAN dosyalar; burası
/// okunan dosyalardan çıkan şemanın kendisiyle ilgili. Örnek: birbirinden
/// bağımsız projeler taşıyan bir depoda şemalar birleştirildiğinde aynı tablo
/// adı birden çok kez çıkar ve sonuç tek bir uygulamanın şeması değildir.
/// </param>
public sealed record RepositoryScanResult(
    DatabaseSchema Schema,
    string Format,
    string Branch,
    IReadOnlyList<string> ParsedFiles,
    IReadOnlyList<SkippedItem> Skipped,
    bool TreeTruncated,
    IReadOnlyList<string> Warnings);

/// <summary>
/// github/01-DEPO-TARAMA.md — bir depoyu okuyup şemayı çıkarır.
///
/// <b>Yeni bir ayrıştırıcı yok:</b> bu servis yalnızca dosyaları TOPLAR ve var
/// olan <see cref="CodeSchemaExtractor"/>'a verir. Depo, dosya seçicinin yerini
/// alan bir kaynaktan ibaret — format tanıma, sınırlar ve "tanıyamadım" cevabı
/// hâlâ tek bir yerde.
/// </summary>
public interface IRepositoryScanner
{
    Task<RepositoryScanResult> ScanAsync(
        GithubRepository repository, string? branch, long? installationId,
        CancellationToken cancellationToken = default);
}
