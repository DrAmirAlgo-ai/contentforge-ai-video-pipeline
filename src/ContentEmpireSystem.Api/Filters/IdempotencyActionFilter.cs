using System.Text;
using ContentEmpireSystem.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ContentEmpireSystem.Api.Filters;

[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsPost(request.Method)
            && !HttpMethods.IsPut(request.Method))
        {
            await next();
            return;
        }

        if (!request.Headers.TryGetValue(HeaderName, out var keyValues))
        {
            await next();
            return;
        }

        var idempotencyKey = keyValues.ToString().Trim();
        if (idempotencyKey.Length is < 8 or > 128)
        {
            context.Result = new BadRequestObjectResult(new
            {
                error = "Idempotency-Key يجب أن يكون بين 8 و 128 حرفاً."
            });
            return;
        }

        var cache = context.HttpContext.RequestServices.GetRequiredService<IdempotencyCache>();
        var cacheKey = IdempotencyCache.BuildKey(request.Method, request.Path.Value ?? "", idempotencyKey);

        if (cache.TryGet(cacheKey, out var cached) && cached is not null)
        {
            context.HttpContext.Response.Headers["X-Idempotent-Replay"] = "true";
            context.Result = new ContentResult
            {
                StatusCode = cached.StatusCode,
                ContentType = cached.ContentType,
                Content = Encoding.UTF8.GetString(cached.Body)
            };
            return;
        }

        var executed = await next();
        if (executed.Exception is not null || executed.Result is null)
            return;

        var (statusCode, contentType, body) = ExtractResult(executed.Result);
        if (body.Length == 0 || statusCode is not (StatusCodes.Status202Accepted or StatusCodes.Status201Created))
            return;

        cache.Store(cacheKey, statusCode, contentType, body);
    }

    private static (int StatusCode, string ContentType, byte[] Body) ExtractResult(IActionResult result)
    {
        return result switch
        {
            ObjectResult obj => (
                obj.StatusCode ?? StatusCodes.Status200OK,
                "application/json; charset=utf-8",
                Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(obj.Value))),
            StatusCodeResult code => (
                code.StatusCode,
                "application/json; charset=utf-8",
                Encoding.UTF8.GetBytes("{}")),
            _ => (StatusCodes.Status200OK, "application/json; charset=utf-8", Array.Empty<byte>())
        };
    }
}
