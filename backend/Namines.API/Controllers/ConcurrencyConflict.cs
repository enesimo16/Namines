using Microsoft.AspNetCore.Mvc;

namespace Namines.API.Controllers;

/// <summary>
/// CloudProject satırı (xmin eşzamanlılık jetonu) okuma ile yazma arasında başka bir
/// istek tarafından değiştirildiğinde DbUpdateConcurrencyException fırlar. Bu bir
/// sunucu hatası değil, istemcinin yeniden yükleyip tekrar denemesi gereken bir
/// çakışmadır: 500 yerine açık bir 409 döndürülür.
/// </summary>
internal static class ConcurrencyConflict
{
    public static ConflictObjectResult Result() => new(new
    {
        error = "This project was changed by someone else while your request was being processed. " +
                "Reload it to get the latest version, then re-apply your change.",
        code = "CONCURRENT_MODIFICATION",
    });
}
