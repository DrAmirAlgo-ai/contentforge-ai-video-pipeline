namespace ContentEmpireSystem.Infrastructure.Data;

public sealed class DatabaseBootstrapState : IDatabaseBootstrapState
{
    private volatile bool _isReady;
    private volatile bool _isFailed;
    private string _provider = "Unknown";
    private TimeSpan? _elapsed;
    private string? _failureMessage;

    public bool IsReady => _isReady;
    public bool IsFailed => _isFailed;
    public string Provider => _provider;
    public TimeSpan? Elapsed => _elapsed;
    public string? FailureMessage => _failureMessage;

    public void MarkStarting(string provider) => _provider = provider;

    public void MarkReady(TimeSpan elapsed)
    {
        _elapsed = elapsed;
        _isReady = true;
        _isFailed = false;
        _failureMessage = null;
    }

    public void MarkFailed(Exception exception)
    {
        _isFailed = true;
        _isReady = false;
        _failureMessage = exception.Message;
    }
}
