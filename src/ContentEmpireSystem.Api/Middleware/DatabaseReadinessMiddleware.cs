using System.Text.Json;
using ContentEmpireSystem.Infrastructure.Data;

namespace ContentEmpireSystem.Api.Middleware;

/// <summary>Returns 503 for API calls until background DB bootstrap completes.</summary>
public sealed class DatabaseReadinessMiddleware(
    RequestDelegate next,
    IDatabaseBootstrapState bootstrapState)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task InvokeAsync(HttpContext context)
    {
        if (bootstrapState.IsReady || IsExemptPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = bootstrapState.IsFailed
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = bootstrapState.IsFailed ? "unhealthy" : "starting",
            message = bootstrapState.IsFailed
                ? "فشل تهيئة قاعدة البيانات. راجع سجلات الخادم."
                : "جاري تهيئة قاعدة البيانات. أعد المحاولة خلال ثوانٍ.",
            provider = bootstrapState.Provider,
            error = bootstrapState.IsFailed ? bootstrapState.FailureMessage : null
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static bool IsExemptPath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        return value.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/index.html", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/css/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/js/", StringComparison.OrdinalIgnoreCase)
            || value.Equals("/", StringComparison.OrdinalIgnoreCase);
    }
}
