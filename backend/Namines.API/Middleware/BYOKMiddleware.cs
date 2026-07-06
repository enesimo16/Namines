using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Namines.API.Middleware
{
    public class BYOKMiddleware
    {
        private readonly RequestDelegate _next;

        public BYOKMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Check both naming variants (X-Api-Source/X-User-Api-Key or X-BYOK-Key)
            bool isByok = false;
            string? byokKey = null;
            string? byokProvider = null;

            if (context.Request.Headers.TryGetValue("X-Api-Source", out var source) && source == "byok")
            {
                isByok = true;
            }

            if (context.Request.Headers.TryGetValue("X-BYOK-Key", out var key1) && !string.IsNullOrWhiteSpace(key1))
            {
                isByok = true;
                byokKey = key1;
            }
            else if (context.Request.Headers.TryGetValue("X-User-Api-Key", out var key2) && !string.IsNullOrWhiteSpace(key2))
            {
                isByok = true;
                byokKey = key2;
            }

            if (context.Request.Headers.TryGetValue("X-BYOK-Provider", out var prov1) && !string.IsNullOrWhiteSpace(prov1))
            {
                byokProvider = prov1;
            }
            else if (context.Request.Headers.TryGetValue("X-User-Provider", out var prov2) && !string.IsNullOrWhiteSpace(prov2))
            {
                byokProvider = prov2;
            }

            if (isByok && !string.IsNullOrWhiteSpace(byokKey))
            {
                context.Items["ByokApiKey"] = byokKey;
                context.Items["ByokProvider"] = byokProvider ?? "groq";
                context.Items["IsByok"] = true;
            }

            await _next(context);
        }
    }
}
