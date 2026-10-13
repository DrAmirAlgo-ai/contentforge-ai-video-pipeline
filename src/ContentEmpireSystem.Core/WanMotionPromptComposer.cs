using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core;

/// <summary>Builds Wan TI2V/T2V scene motion prompts when Qwen did not fill userMotionPrompt.</summary>
public static class WanMotionPromptComposer
{
    public static string Compose(ImageSuggestionDto? image, VideoSuggestionDto? video = null)
    {
        var existing = ImagePromptComposer.StripEmbeddedNegative(
            video?.UserMotionPrompt ?? image?.UserMotionPrompt);
        if (!string.IsNullOrEmpty(existing))
            return existing;

        var subject = image?.SubjectDescription?.Trim() ?? "";
        var action = image?.ActionDescription?.Trim() ?? "";
        var camera = image?.CameraAngle?.Trim() ?? "";
        var scene = image?.SceneDescription?.Trim()
            ?? video?.SceneDescription?.Trim()
            ?? video?.NarrationExcerpt?.Trim()
            ?? image?.NarrationExcerpt?.Trim()
            ?? "";

        var sceneParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(subject))
            sceneParts.Add($"Character and scene: {subject}");
        if (!string.IsNullOrWhiteSpace(scene))
            sceneParts.Add(scene);

        var text = $"{subject} {action} {camera} {scene}".ToLowerInvariant();
        var motionParts = new List<string> { InferCameraMotion(text, camera) };
        motionParts.Add(InferSubjectMotion(text, action));

        if (!string.IsNullOrWhiteSpace(action))
            motionParts.Add(action);

        var sceneMotion = string.Join(", ", motionParts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct());
        if (sceneParts.Count > 0)
            return $"{string.Join(". ", sceneParts)}. {sceneMotion}";

        return sceneMotion;
    }

    public static void ApplyToScript(ScriptOutputDto script, bool isTextToVideo)
    {
        var plan = script.ProductionPlan;
        if (plan is null)
            return;

        var count = isTextToVideo
            ? Math.Max(plan.VideoSegmentCount, script.VideoSuggestions.Count)
            : Math.Max(
                Math.Max(plan.ImageSegmentCount, script.ImageSuggestions.Count),
                script.VideoSuggestions.Count);

        if (count <= 0)
            return;
        for (var sort = 1; sort <= count; sort++)
        {
            var image = script.ImageSuggestions.FirstOrDefault(i => i.SortOrder == sort);
            var video = script.VideoSuggestions.FirstOrDefault(v =>
                v.SortOrder == sort || v.LinkedImageSortOrder == sort);

            if (!string.IsNullOrWhiteSpace(ImagePromptComposer.StripEmbeddedNegative(video?.UserMotionPrompt))
                || !string.IsNullOrWhiteSpace(ImagePromptComposer.StripEmbeddedNegative(image?.UserMotionPrompt)))
                continue;

            var motion = Compose(image, video);
            if (video is not null)
                video.UserMotionPrompt = motion;
            if (image is not null)
                image.UserMotionPrompt = motion;
        }
    }

    private static string InferCameraMotion(string text, string camera)
    {
        var cameraText = $"{text} {camera}".ToLowerInvariant();

        if (ContainsAny(cameraText, "close-up", "closeup", "portrait", "face", "macro", "tight",
                "قريب", "وجه", "بورتريه"))
            return "locked stable camera, extremely subtle push-in, preserve face identity";

        if (ContainsAny(cameraText, "wide", "landscape", "panorama", "establishing", "horizon", "skyline",
                "منظر", "أفق", "بانorama"))
            return "slow gentle horizontal pan";

        if (ContainsAny(cameraText, "low angle", "low-angle", "hero shot", "worm"))
            return "locked stable camera, subtle upward tilt";

        if (ContainsAny(cameraText, "high angle", "high-angle", "overhead", "top-down", "bird", "aerial"))
            return "locked stable camera, gentle overhead drift";

        if (ContainsAny(cameraText, "tracking", "follow", "dolly", "تتبع"))
            return "smooth slow tracking shot following the subject";

        return "locked stable camera, extremely subtle ambient drift only";
    }

    private static string InferSubjectMotion(string text, string action)
    {
        if (ContainsAny(text, "walk", "walking", "run", "running", "move", "enter", "exit", "turn", "chase",
                "مشي", "يمشي", "جري", "يجري", "حركة", "يدخل", "يخرج"))
            return "natural subject movement matching the action, slow and smooth";

        if (ContainsAny(text, "talk", "speak", "gesture", "point", "wave", "nod",
                "تحدث", "يتكلم", "إشارة", "يشير"))
            return "subtle micro-gestures only, preserve face identity, no head morphing";

        if (!string.IsNullOrWhiteSpace(action))
            return "minimal motion matching action, preserve identity and face structure";

        return "minimal natural micro-movement, locked identity, stable facial features";
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
