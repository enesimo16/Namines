using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Analysis;

namespace Namines.Core.Interfaces;

/// <param name="Success">Herhangi bir kaynak işe yaradıysa true.</param>
/// <param name="SourceKind">"graphql" | "openapi" | "none" — hangi kademenin işe yaradığı.</param>
/// <param name="SourceUrl">Gerçekten veri döndüren URL.</param>
/// <param name="Tables">Çıkarılan varlık adayları. Başarısızsa boş.</param>
/// <param name="FailureReason">Başarısızsa neden.</param>
public sealed record ApiSpecExtractionResult(
    bool Success,
    string SourceKind,
    string? SourceUrl,
    IReadOnlyList<PlannedTable> Tables,
    string? FailureReason);

/// <summary>
/// GraphQL introspection veya OpenAPI/Swagger dokümanından veri modeli çıkaran sözleşme.
/// </summary>
public interface IApiSpecExtractor
{
    Task<ApiSpecExtractionResult> ExtractAsync(string userUrl, CancellationToken ct = default);
}
