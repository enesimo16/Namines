using System.Threading;
using System.Threading.Tasks;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

/// <summary>
/// <see cref="AiQuotaService.TryReserveAsync"/>'in dar arayüzü — yalnızca
/// <see cref="AutomationExecutor"/>'ın ihtiyaç duyduğu tek metod (Bölüm 3 Eki #1).
/// <see cref="AiQuotaService"/> sealed olduğu için testte sahte bir kota
/// kararı vermek doğrudan alt sınıflamayla mümkün değil; bu arayüz o boşluğu
/// kapatır. Aynı projede (Infrastructure) tanımlı çünkü AiQuotaDecision de
/// burada yaşıyor — Core projesine taşımak gereksiz bir bağımlılık ters
/// çevirmesi olurdu.
/// </summary>
public interface IAiQuotaReserver
{
    Task<AiQuotaDecision> TryReserveAsync(string userId, int estimatedTokens, CancellationToken ct = default);
}
