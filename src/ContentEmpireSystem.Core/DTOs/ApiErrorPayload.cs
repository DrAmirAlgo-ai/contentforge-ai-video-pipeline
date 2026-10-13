namespace ContentEmpireSystem.Core.DTOs;

/// <summary>
/// Standard API error shape — synced with dashboard.js <c>parseApiError</c>.
/// </summary>
public sealed class ApiErrorPayload
{
    public required string Error { get; init; }
    public string? Hint { get; init; }
    public string? Operation { get; init; }
    public string? CorrelationId { get; init; }
    /// <summary>Where to find file logs on disk, e.g. logs/operations/operations-2026-06-28.log</summary>
    public string? LogHint { get; init; }

    public object ToObject() => new
    {
        error = Error,
        hint = Hint,
        operation = Operation,
        correlationId = CorrelationId,
        logHint = LogHint
    };
}
