using System.Net;
using System.Text.Json;
using ContentEmpireSystem.Api.Helpers;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            await WritePayloadAsync(context, (HttpStatusCode)499, OperationHints.ConnectionLost());
        }
        catch (InvalidOperationException ex)
        {
            var correlationId = context.Items[CorrelationId.ItemKey]?.ToString() ?? "-";
            logger.LogWarning(ex, "Request rejected [{CorrelationId}]: {Path}", correlationId, context.Request.Path);
            await WritePayloadAsync(context, HttpStatusCode.Conflict,
                OperationHints.FromExceptionMessage(ex.Message, correlationId));
        }
        catch (Exception ex)
        {
            var correlationId = context.Items[CorrelationId.ItemKey]?.ToString() ?? "-";
            logger.LogError(ex, "Unhandled error [{CorrelationId}] {Method} {Path}", correlationId, context.Request.Method, context.Request.Path);
            await WritePayloadAsync(context, HttpStatusCode.InternalServerError,
                OperationHints.InternalError(correlationId));
        }
    }

    private static async Task WritePayloadAsync(HttpContext context, HttpStatusCode code, ApiErrorPayload payload)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.StatusCode = (int)code;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(ApiErrorWriter.ToBody(payload, context), JsonOptions));
    }
}
