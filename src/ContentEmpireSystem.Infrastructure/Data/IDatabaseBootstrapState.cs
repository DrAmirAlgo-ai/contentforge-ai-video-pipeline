namespace ContentEmpireSystem.Infrastructure.Data;

public interface IDatabaseBootstrapState
{
    bool IsReady { get; }
    bool IsFailed { get; }
    string Provider { get; }
    TimeSpan? Elapsed { get; }
    string? FailureMessage { get; }
    void MarkStarting(string provider);
    void MarkReady(TimeSpan elapsed);
    void MarkFailed(Exception exception);
}
