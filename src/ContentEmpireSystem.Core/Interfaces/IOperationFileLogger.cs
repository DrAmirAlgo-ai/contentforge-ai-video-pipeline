namespace ContentEmpireSystem.Core.Interfaces;

/// <summary>
/// Append-only file log for operations and file exchanges — used in troubleshooting.
/// </summary>
public interface IOperationFileLogger
{
    void LogStart(Guid projectId, string operation, string? detail = null);
    void LogProgress(Guid projectId, string operation, string detail);
    void LogComplete(Guid projectId, string operation, string? detail = null);
    void LogFail(Guid projectId, string operation, string error, Exception? ex = null);
    void LogFileExchange(Guid projectId, string operation, string source, string destination, string? note = null);
    string GetLogsDirectory();
    string GetTodayLogPath();
}
