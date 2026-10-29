using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Namines.Core.Interfaces;

namespace Namines.API.Filters;

/// <summary>
/// Admin panelindeki kill-switch'i sunucu tarafında GERÇEKTEN uygular.
///
/// <b>Neden gerekli:</b> bayrak yalnızca arayüzü gizlediğinde, kapatılmış bir özellik
/// (ör. bakıma alınan Vault ya da bütçesi biten AI üretimi) doğrudan HTTP çağrısıyla
/// kullanılmaya devam eder — kill-switch'in tek amacı olan "acil durdur" çalışmaz.
///
/// <b>Hata yönü:</b> yalnızca katalogda TANIMLI ve kapalı olan bayrak 503 döner. Bilinmeyen
/// anahtar (yazım hatası, henüz tohumlanmamış bayrak) isteği geçirir; çünkü katalogdaki bir
/// hata tüm bir özelliği kapatmamalı. Kapalı bayrağın sessizce açık sayılması ise asla olmaz.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class FeatureGateAttribute : Attribute, IAsyncActionFilter
{
    public string FeatureKey { get; }

    public FeatureGateAttribute(string featureKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureKey);
        FeatureKey = featureKey;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var flags = context.HttpContext.RequestServices.GetService<IFeatureFlagService>();
        if (flags is not null)
        {
            // GetFlagAsync önbelleği (gerekirse DB'den) tazeler; sync IsFeatureEnabled tek başına
            // ilk istekte tohumlanmamış varsayılanlara bakardı.
            var flag = await flags.GetFlagAsync(FeatureKey, context.HttpContext.RequestAborted);
            if (flag is { IsEnabled: false })
            {
                var message = string.IsNullOrWhiteSpace(flag.NoticeMessage)
                    ? "This feature is temporarily unavailable."
                    : flag.NoticeMessage;

                context.Result = new ObjectResult(new
                {
                    error = message,
                    message,
                    detail = message,
                    code = "FEATURE_DISABLED",
                    featureKey = flag.FeatureKey,
                    mode = flag.Mode,
                    status = StatusCodes.Status503ServiceUnavailable
                })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                };
                return;
            }
        }

        await next();
    }
}
