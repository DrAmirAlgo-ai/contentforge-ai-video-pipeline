namespace ContentEmpireSystem.Core.DTOs;

public sealed class ActivityEntryDto
{
    public Guid ProjectId { get; init; }
    public string Action { get; init; } = "";
    public string Detail { get; init; } = "";
    public string State { get; init; } = "";
    public DateTime StartedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int ElapsedSec { get; init; }
}
