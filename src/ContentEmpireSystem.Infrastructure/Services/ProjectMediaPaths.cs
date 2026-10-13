using ContentEmpireSystem.Core.Entities;
using ContentEmpireSystem.Core.Interfaces;

namespace ContentEmpireSystem.Infrastructure.Services;

internal static class ProjectMediaPaths
{
    public static bool HasAudioFile(VideoProject project, IOutputPathService paths)
    {
        if (string.IsNullOrWhiteSpace(project.AudioFilePath))
            return false;

        try
        {
            return File.Exists(paths.ToAbsolutePath(project.AudioFilePath));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
