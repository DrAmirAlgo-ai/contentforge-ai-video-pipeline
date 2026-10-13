using System.Collections.Concurrent;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Infrastructure.Services;

public interface IProjectOperationCoordinator
{
    bool TryBegin(Guid projectId, ProjectOperationKind kind, out string? blockedBy);
    void End(Guid projectId, ProjectOperationKind kind);
    void ForceClear(Guid projectId);
    void ClearAll();
    bool IsBusy(Guid projectId, out ProjectOperationKind? active);
    string Describe(ProjectOperationKind kind);
}

/// <summary>One heavy operation per project across all queues (images / video / produce).</summary>
public sealed class ProjectOperationCoordinator : IProjectOperationCoordinator
{
    private readonly ConcurrentDictionary<Guid, ProjectOperationKind> _active = new();

    public bool TryBegin(Guid projectId, ProjectOperationKind kind, out string? blockedBy)
    {
        if (_active.TryGetValue(projectId, out var existing) && existing != kind)
        {
            blockedBy = $"المشروع مشغول بـ {Describe(existing)} — انتظر حتى ينتهي.";
            return false;
        }

        _active[projectId] = kind;
        blockedBy = null;
        return true;
    }

    public void End(Guid projectId, ProjectOperationKind kind)
    {
        if (_active.TryGetValue(projectId, out var existing) && existing == kind)
            _active.TryRemove(projectId, out _);
    }

    public void ForceClear(Guid projectId) => _active.TryRemove(projectId, out _);

    public void ClearAll() => _active.Clear();

    public bool IsBusy(Guid projectId, out ProjectOperationKind? active)
    {
        if (_active.TryGetValue(projectId, out var kind))
        {
            active = kind;
            return true;
        }

        active = null;
        return false;
    }

    public string Describe(ProjectOperationKind kind) => kind switch
    {
        ProjectOperationKind.IdeaGeneration => "توليد الفكرة",
        ProjectOperationKind.ScriptGeneration => "توليد السكريبت",
        ProjectOperationKind.ImageGeneration => "توليد الصور",
        ProjectOperationKind.VideoAnimation => "تحريك الصور (Wan TI2V)",
        ProjectOperationKind.VideoGeneration => "توليد فيديو (Wan T2V)",
        ProjectOperationKind.AudioGeneration => "توليد الصوت",
        ProjectOperationKind.Production => "تركيب FFmpeg",
        _ => kind.ToString()
    };
}
