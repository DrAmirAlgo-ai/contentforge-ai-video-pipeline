namespace ContentEmpireSystem.Infrastructure.Services;

/// <summary>
/// Portable path resolution anchored to <see cref="ContentRootPath"/>.
/// Relative settings (e.g. "output") resolve under the content root so the app
/// works on any machine or folder without hardcoded drive letters.
/// </summary>
public sealed class OutputPathService(string contentRootPath) : Core.Interfaces.IOutputPathService
{
    private readonly string _contentRootPath = Path.GetFullPath(contentRootPath);

    public string ContentRootPath => _contentRootPath;

    public string GetOutputBaseDirectory(string configuredOutput)
    {
        if (string.IsNullOrWhiteSpace(configuredOutput))
            configuredOutput = "output";

        if (Path.IsPathRooted(configuredOutput))
            return Path.GetFullPath(configuredOutput);

        return Path.GetFullPath(Path.Combine(_contentRootPath, configuredOutput));
    }

    public string GetProjectDirectory(string configuredOutput, Guid projectId)
    {
        var dir = Path.Combine(GetOutputBaseDirectory(configuredOutput), projectId.ToString("D"));
        Directory.CreateDirectory(dir);
        return Path.GetFullPath(dir);
    }

    public string ToAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty.", nameof(path));

        var normalized = path.Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3
            && Guid.TryParse(parts[0], out _)
            && (string.Equals(parts[1], "images", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[1], "clips", StringComparison.OrdinalIgnoreCase)))
        {
            var portable = Path.Combine(parts);
            var underOutput = Path.GetFullPath(Path.Combine(GetOutputBaseDirectory("output"), portable));
            if (File.Exists(underOutput) || Directory.Exists(underOutput))
                return underOutput;
        }

        if (Path.IsPathRooted(path))
        {
            var absolute = Path.GetFullPath(path);
            if (File.Exists(absolute) || Directory.Exists(absolute))
                return absolute;
        }

        foreach (var root in SearchRoots())
        {
            var candidate = Path.GetFullPath(Path.Combine(root, path));
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;
        }

        return Path.GetFullPath(Path.Combine(_contentRootPath, path));
    }

    private IEnumerable<string> SearchRoots()
    {
        yield return _contentRootPath;
        yield return AppContext.BaseDirectory;

        var dir = _contentRootPath;
        for (var i = 0; i < 8; i++)
        {
            var parent = Directory.GetParent(dir)?.FullName;
            if (string.IsNullOrEmpty(parent) || parent == dir)
                break;
            dir = parent;
            yield return dir;
        }
    }

    public string ToFfmpegConcatPath(string absolutePath)
    {
        var normalized = Path.GetFullPath(absolutePath).Replace('\\', '/');
        return normalized.Replace("'", "'\\''");
    }

    public string EscapeForFfmpegAssFilter(string absolutePath)
    {
        var normalized = Path.GetFullPath(absolutePath).Replace('\\', '/');
        if (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':')
            normalized = $"{normalized[0]}\\:{normalized[2..]}";

        return normalized.Replace("'", "'\\''");
    }

    public void EnsureFileExists(string path, string label)
    {
        var full = ToAbsolutePath(path);
        if (!File.Exists(full))
            throw new InvalidOperationException($"{label} not found: {full}");
    }

    public string? FindProjectSceneFile(string configuredOutput, Guid projectId, int sortOrder)
    {
        var imagesDir = Path.Combine(GetProjectDirectory(configuredOutput, projectId), "images");
        if (!Directory.Exists(imagesDir))
            return null;

        var prefix = $"scene_{sortOrder:D2}";
        foreach (var ext in new[] { ".png", ".webp", ".jpg", ".jpeg", ".mp4", ".webm" })
        {
            var exact = Path.Combine(imagesDir, prefix + ext);
            if (File.Exists(exact))
                return exact;
        }

        var replaced = Directory.GetFiles(imagesDir, prefix + "_replace*")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        return replaced;
    }

    public string ResolveSceneMediaPath(string configuredOutput, Guid projectId, int sortOrder, string storedPath)
    {
        if (!string.IsNullOrWhiteSpace(storedPath))
        {
            try
            {
                var resolved = ToAbsolutePath(storedPath);
                if (File.Exists(resolved))
                    return resolved;
            }
            catch
            {
                // try project folder fallback
            }
        }

        return FindProjectSceneFile(configuredOutput, projectId, sortOrder)
            ?? throw new FileNotFoundException(
                $"Scene file not found for project {projectId:D}, sort {sortOrder}.");
    }
}
