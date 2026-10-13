namespace ContentEmpireSystem.Infrastructure.Services;

internal static class MediaSegmentHelper
{
    public static readonly HashSet<string> VideoExtensions = [".mp4", ".mov", ".webm", ".mkv"];
    public static readonly HashSet<string> ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public static bool IsVideoClip(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    }

    public static bool IsImageClip(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    }
}
