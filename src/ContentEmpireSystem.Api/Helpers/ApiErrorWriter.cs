using ContentEmpireSystem.Api.Middleware;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Api.Helpers;

internal static class ApiErrorWriter
{
    public static ApiErrorPayload Enrich(ApiErrorPayload payload, HttpContext? context)
    {
        if (context is null)
            return payload;

        var correlationId = context.Items[CorrelationId.ItemKey]?.ToString();
        if (string.IsNullOrWhiteSpace(correlationId))
            return payload;

        return new ApiErrorPayload
        {
            Error = payload.Error,
            Hint = payload.Hint,
            Operation = payload.Operation,
            CorrelationId = correlationId,
            LogHint = payload.LogHint ?? OperationHints.LogHintText()
        };
    }

    public static ApiErrorPayload FromMessage(string message, HttpContext? context) =>
        Enrich(OperationHints.FromExceptionMessage(message, GetCorrelationId(context)), context);

    public static object ToBody(ApiErrorPayload payload, HttpContext? context) =>
        Enrich(payload, context).ToObject();

    private static string? GetCorrelationId(HttpContext? context) =>
        context?.Items[CorrelationId.ItemKey]?.ToString();
}
