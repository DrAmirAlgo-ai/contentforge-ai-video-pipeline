using System.Text;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Interfaces;

namespace ContentEmpireSystem.Infrastructure.Services;

public sealed class OperationFileLogger(IOutputPathService paths) : IOperationFileLogger
{
    private readonly string _logsDir = Path.Combine(paths.ContentRootPath, OperationHints.LogsFolderRelative);
    private readonly object _writeLock = new();

    public string GetLogsDirectory()
    {
        Directory.CreateDirectory(_logsDir);
        return _logsDir;
    }

    public string GetTodayLogPath() =>
        Path.Combine(GetLogsDirectory(), OperationHints.TodayLogFileName());

    public void LogStart(Guid projectId, string operation, string? detail = null) =>
        Write("START", projectId, operation, detail);

    public void LogProgress(Guid projectId, string operation, string detail) =>
        Write("PROGRESS", projectId, operation, detail);

    public void LogComplete(Guid projectId, string operation, string? detail = null) =>
        Write("OK", projectId, operation, detail);

    public void LogFail(Guid projectId, string operation, string error, Exception? ex = null)
    {
        var detail = ex is null ? error : $"{error} | {ex.GetType().Name}: {ex.Message}";
        Write("FAIL", projectId, operation, detail);
    }

    public void LogFileExchange(Guid projectId, string operation, string source, string destination, string? note = null)
    {
        var detail = note is null
            ? $"{source} → {destination}"
            : $"{source} → {destination} ({note})";
        Write("FILE", projectId, operation, detail);
    }

    private void Write(string level, Guid projectId, string operation, string? detail)
    {
        var line = new StringBuilder()
            .Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
            .Append('[').Append(level).Append("] ")
            .Append("[project=").Append(projectId.ToString("D")).Append("] ")
            .Append("[op=").Append(operation).Append("] ");

        if (!string.IsNullOrWhiteSpace(detail))
            line.Append(detail);

        lock (_writeLock)
        {
            try
            {
                File.AppendAllText(GetTodayLogPath(), line.AppendLine().ToString(), Encoding.UTF8);
            }
            catch
            {
                // Best-effort — never fail the pipeline because of log I/O.
            }
        }
    }
}
