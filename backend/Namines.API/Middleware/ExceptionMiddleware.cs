using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Namines.API.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _env;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger,
        IWebHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // Her hata için ASP.NET'in TraceIdentifier'ını korelasyon ID olarak kullan.
            // Bu ID hem logda hem response'ta bulunur; production'da detay sızmaz.
            var correlationId = context.TraceIdentifier;

            _logger.LogError(
                ex,
                "Unhandled exception [CorrelationId={CorrelationId}] Path={Path}",
                correlationId,
                context.Request.Path);

            await HandleExceptionAsync(context, ex, correlationId, _env.IsDevelopment());
        }
    }

    private static Task HandleExceptionAsync(
        HttpContext context,
        Exception exception,
        string correlationId,
        bool isDevelopment)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var response = new
        {
            StatusCode  = context.Response.StatusCode,
            Message     = $"Something went wrong. Reference: {correlationId}",
            // Production'da Detailed = null → iç mimari sızmaz.
            // Development'ta tam hata mesajı döner, kolayca debug edilir.
            Detailed    = isDevelopment ? exception.Message : null,
            CorrelationId = correlationId
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        return context.Response.WriteAsync(json);
    }
}
