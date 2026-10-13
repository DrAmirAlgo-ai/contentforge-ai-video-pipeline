using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.Interfaces;

public interface IOutputPathService
{
    /// <summary>Application content root â€” moves with the project, never hardcoded.</summary>
    string ContentRootPath { get; }

    /// <summary>Resolves the configured output folder to an absolute path.</summary>
    string GetOutputBaseDirectory(string configuredOutput);

    /// <summary>Absolute project output folder; created if missing.</summary>
    string GetProjectDirectory(string configuredOutput, Guid projectId);

    /// <summary>Normalizes any stored path (absolute or legacy relative) to a full absolute path.</summary>
    string ToAbsolutePath(string path);

    /// <summary>Absolute path formatted for FFmpeg concat demuxer (forward slashes, escaped quotes).</summary>
    string ToFfmpegConcatPath(string absolutePath);

    /// <summary>Absolute path escaped for FFmpeg ass filter on Windows.</summary>
    string EscapeForFfmpegAssFilter(string absolutePath);

    /// <summary>Throws if the resolved file does not exist on disk.</summary>
    void EnsureFileExists(string path, string label);

    /// <summary>Finds a scene image/clip under the project output folder when stored paths are stale.</summary>
    string? FindProjectSceneFile(string configuredOutput, Guid projectId, int sortOrder);

    /// <summary>Resolves a stored scene path, falling back to the project output folder.</summary>
    string ResolveSceneMediaPath(string configuredOutput, Guid projectId, int sortOrder, string storedPath);
}
