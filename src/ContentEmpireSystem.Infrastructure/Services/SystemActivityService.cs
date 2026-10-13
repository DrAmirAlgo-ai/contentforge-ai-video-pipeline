using System.Collections.Concurrent;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Interfaces;

namespace ContentEmpireSystem.Infrastructure.Services;

public sealed class SystemActivityService : ISystemActivityService
{
    private readonly ConcurrentDictionary<Guid, ActivityEntry> _byProject = new();

    public event Action? Changed;

    public void Start(Guid projectId, string action, string detail = "")
    {
        _byProject[projectId] = new ActivityEntry
        {
            ProjectId = projectId,
            Action = action,
            Detail = detail,
            State = "running",
            StartedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Changed?.Invoke();
    }

    public void Complete(Guid projectId, string? detail = null)
    {
        if (!_byProject.TryGetValue(projectId, out var entry))
            return;

        entry.State = "completed";
        entry.Detail = detail ?? entry.Detail;
        entry.UpdatedAt = DateTime.UtcNow;
        entry.CompletedAt = DateTime.UtcNow;
        Changed?.Invoke();

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(8));
            if (_byProject.TryRemove(projectId, out _))
                Changed?.Invoke();
        });
    }

    public void Progress(Guid projectId, string detail)
    {
        if (!_byProject.TryGetValue(projectId, out var entry))
            return;

        entry.Detail = detail;
        entry.UpdatedAt = DateTime.UtcNow;
        Changed?.Invoke();
    }

    public void Fail(Guid projectId, string error)
    {
        if (!_byProject.TryGetValue(projectId, out var entry))
        {
            entry = new ActivityEntry
            {
                ProjectId = projectId,
                Action = "عملية",
                StartedAt = DateTime.UtcNow,
            };
            _byProject[projectId] = entry;
        }

        entry.State = "failed";
        entry.Detail = error;
        entry.UpdatedAt = DateTime.UtcNow;
        entry.CompletedAt = DateTime.UtcNow;
        Changed?.Invoke();

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            if (_byProject.TryRemove(projectId, out _))
                Changed?.Invoke();
        });
    }

    public IReadOnlyList<ActivityEntryDto> GetSnapshot()
    {
        return _byProject.Values
            .OrderByDescending(e => e.UpdatedAt)
            .Select(e => new ActivityEntryDto
            {
                ProjectId = e.ProjectId,
                Action = e.Action,
                Detail = e.Detail,
                State = e.State,
                StartedAt = e.StartedAt,
                UpdatedAt = e.UpdatedAt,
                CompletedAt = e.CompletedAt,
                ElapsedSec = (int)(DateTime.UtcNow - e.StartedAt).TotalSeconds
            })
            .ToList();
    }

    public sealed class ActivityEntry
    {
        public Guid ProjectId { get; init; }
        public string Action { get; set; } = "";
        public string Detail { get; set; } = "";
        public string State { get; set; } = "running";
        public DateTime StartedAt { get; init; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
