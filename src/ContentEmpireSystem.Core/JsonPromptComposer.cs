namespace ContentEmpireSystem.Core;

public static class JsonPromptDefaults
{
    public const string ScriptInstructions = """
        You are a universal short-form video script engine (Shorts/Reels/TikTok).
        Topic, tone, and language come from the user message and category context.
        Do not assume finance or any fixed niche unless the user message specifies it.
        Honor SCRIPT LENGTH in the user message as a flexible guide — approximate target is fine.
        Return narration and metadata ONLY — imageSuggestions and videoSuggestions must be empty arrays [].
        Scene planning is handled in a separate step.
        """;

    public const string ScriptJsonSchema = """
        Return JSON only with this structure:
        {
          "viewerBenefit": "what the viewer gains",
          "seo": { "title": "", "description": "", "hashtags": [] },
          "primaryHook": "first 3 seconds hook",
          "extendedHook": "extended hook",
          "body": { "partOneProblem": "problem/setup", "partTwoSolution": "solution/payload" },
          "conclusionAndCta": "conclusion and CTA",
          "imageSuggestions": [],
          "videoSuggestions": [],
          "productionPlan": { "targetWordCount": 300, "workflowPath": "ImageToVideo" },
          "fullNarrationText": "full voice-over narration text sized to target word count"
        }
        """;

    public const string ScenePlannerInstructions = """
        You are a visual scene planner for short-form video production.
        Read the full narration and decide how many scenes/clips are needed based on narrative structure — NOT time or seconds.
        Split the narration into consecutive logical beats. Each beat becomes one scene.
        For ImageToVideo: fill subjectDescription, actionDescription, cameraAngle for FLUX image generation.
        Never put on-image text. Do not write Wan motion — userMotionPrompt stays empty.
        Honor the rules in the user message and your creative density preferences below.
        """;

    public const string ScenePlannerJsonSchema = """
        Return JSON only with this structure:
        {
          "imageSegmentCount": 0,
          "videoSegmentCount": 0,
          "imageSuggestions": [{ "sortOrder": 1, "narrationExcerpt": "consecutive script excerpt for this scene", "subjectDescription": "precise person/object/thing", "actionDescription": "what they do in frame", "cameraAngle": "e.g. low angle medium shot", "sceneDescription": "editor note in script language", "prompt": "optional combined English scene line" }],
          "videoSuggestions": [{ "sortOrder": 1, "narrationExcerpt": "matching script excerpt", "linkedImageSortOrder": 1, "userMotionPrompt": "English positive motion for this scene only — scene/character/camera; no global quality rules, no negative terms" }]
        }
        For TextToVideo: imageSuggestions = [], videoSegmentCount = videoSuggestions.length.
        For ImageToVideo: imageSegmentCount = imageSuggestions.length, videoSuggestions count must match.
        """;

    public const string ScenePlannerUserTemplateImageToVideo = """
        Workflow path: {{workflowPathDescription}}

        Target narration word count: {{targetWordCount}}
        Actual narration word count: {{actualWordCount}}

        primaryHook: {{primaryHook}}

        fullNarrationText:
        {{fullNarrationText}}

        Task:
        - Decide how many visual scenes (imageSegmentCount) best cover this narration.
        - Split fullNarrationText into consecutive logical narrationExcerpt segments (one per scene).
        - Each narrationExcerpt must be a SHORT consecutive slice — never repeat the full script in every scene.
        - For EACH imageSuggestion fill: subjectDescription, actionDescription, cameraAngle, sceneDescription, optional prompt.
        - Create matching videoSuggestions (same count, linkedImageSortOrder = sortOrder).
        - Leave userMotionPrompt as "" (filled automatically later).
        - Do NOT use time or seconds — split by narrative beats only.
        """;

    public const string ScenePlannerUserTemplateTextToVideo = """
        Workflow path: {{workflowPathDescription}}

        Target narration word count: {{targetWordCount}}
        Actual narration word count: {{actualWordCount}}

        primaryHook: {{primaryHook}}

        fullNarrationText:
        {{fullNarrationText}}

        Task:
        - Decide how many video clips (videoSegmentCount) best cover this narration.
        - Split fullNarrationText into consecutive logical narrationExcerpt segments (one per clip).
        - Return videoSuggestions only; imageSuggestions must be [].
        - Leave userMotionPrompt as "" (filled automatically later).
        - Do NOT use time or seconds — split by narrative beats only.
        """;

    public const string ImageScenesInstructions = """
        You are an image-scene and Wan motion prompt writer for short-form video production.
        The user message specifies an EXACT scene count — you MUST return exactly that many scenes or clips.
        Split the full narration into consecutive, non-overlapping excerpts from start to end (one per scene).
        For ImageToVideo: each scene needs FLUX-ready fields — subjectDescription, actionDescription, cameraAngle.
        Never put readable text on images.
        For EACH matching videoSuggestion write userMotionPrompt: a scene-specific POSITIVE Wan motion prompt in English only.
        Base each userMotionPrompt on the idea, narration excerpt, and linked image fields (subject, action, camera, scene).
        Describe the scene and character (age, clothes, expression, location) and choose locked stable camera OR slow gentle pan as the scene requires.
        Do NOT paste the global positive/negative rule blocks — they are appended automatically at generation time.
        Follow the scene count and task rules in the user message exactly.
        """;

    public const string ImageScenesJsonSchema = """
        Return JSON only with this structure:
        {
          "imageSegmentCount": 0,
          "videoSegmentCount": 0,
          "imageSuggestions": [{ "sortOrder": 1, "narrationExcerpt": "consecutive script excerpt for this scene", "subjectDescription": "precise person/object/thing", "actionDescription": "what they do in frame", "cameraAngle": "e.g. low angle medium shot", "sceneDescription": "editor note in script language", "prompt": "optional combined English scene line for FLUX" }],
          "videoSuggestions": [{ "sortOrder": 1, "narrationExcerpt": "matching script excerpt", "linkedImageSortOrder": 1, "userMotionPrompt": "English positive motion for this scene only — scene/character/camera; no global quality rules, no negative terms" }]
        }
        imageSegmentCount / videoSegmentCount must match the required count in the user message.
        For TextToVideo: imageSuggestions = [], videoSegmentCount = videoSuggestions.length.
        For ImageToVideo: imageSegmentCount = imageSuggestions.length, videoSuggestions count must match.
        """;

    public const string ImageScenesUserTemplateImageToVideo = """
        Workflow path: {{workflowPathDescription}}

        Required image scene count: exactly {{imageSegmentCount}} (mandatory — no more, no less)

        Target narration word count: {{targetWordCount}}
        Actual narration word count: {{actualWordCount}}

        primaryHook: {{primaryHook}}

        fullNarrationText:
        {{fullNarrationText}}

        Task:
        - Produce EXACTLY {{imageSegmentCount}} consecutive visual scenes covering the narration from start to end.
        - Split fullNarrationText into {{imageSegmentCount}} narrationExcerpt slices — each a SHORT consecutive slice; never repeat the full script in every scene.
        - For EACH imageSuggestion fill: subjectDescription, actionDescription, cameraAngle, sceneDescription, optional English prompt for FLUX.
        - Create exactly {{imageSegmentCount}} matching videoSuggestions (linkedImageSortOrder = sortOrder).
        - For EACH videoSuggestion write userMotionPrompt (English positive only): scene-specific Wan motion from the narration excerpt and linked image fields.
        - Global positive motion rules (appended automatically — do NOT repeat in userMotionPrompt):
        {{wanMotionPositiveRules}}
        - Global negative rules (handled separately — do NOT put in userMotionPrompt):
        {{wanMotionNegativeRules}}
        - Set imageSegmentCount and videoSegmentCount in JSON to {{imageSegmentCount}}.
        - Do NOT use time or seconds — split by narrative beats only.
        """;

    public const string ImageScenesUserTemplateTextToVideo = """
        Workflow path: {{workflowPathDescription}}

        Required video clip count: exactly {{imageSegmentCount}} (mandatory — no more, no less)

        Target narration word count: {{targetWordCount}}
        Actual narration word count: {{actualWordCount}}

        primaryHook: {{primaryHook}}

        fullNarrationText:
        {{fullNarrationText}}

        Task:
        - Produce EXACTLY {{imageSegmentCount}} consecutive video clips covering the narration from start to end.
        - Split fullNarrationText into {{imageSegmentCount}} narrationExcerpt slices — each a SHORT consecutive slice.
        - Return videoSuggestions only; imageSuggestions must be [].
        - For EACH videoSuggestion write userMotionPrompt (English positive only): scene-specific Wan motion from narration excerpt and scene context.
        - Global positive motion rules (appended automatically — do NOT repeat in userMotionPrompt):
        {{wanMotionPositiveRules}}
        - Global negative rules (handled separately — do NOT put in userMotionPrompt):
        {{wanMotionNegativeRules}}
        - Set videoSegmentCount in JSON to {{imageSegmentCount}}.
        - Do NOT use time or seconds — split by narrative beats only.
        """;

    public const string IdeaInstructions = """
        You are a universal short-form video idea generator.
        Use ONLY the category/topic in the user message. Do not default to finance or any fixed niche.
        """;

    public const string IdeaJsonSchema = """
        Return JSON only: { "idea": "idea text", "angle": "angle", "trendHook": "why it works now" }
        """;
}

public static class JsonPromptComposer
{
    private static readonly string[] JsonSplitMarkers =
    [
        "Return JSON only with this structure:",
        "Return JSON only:",
        "Return JSON only",
    ];

    public static string Compose(string? instructions, string? jsonSchema)
    {
        var instr = (instructions ?? string.Empty).Trim();
        var schema = (jsonSchema ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(instr) && string.IsNullOrEmpty(schema))
            return "Return valid JSON only.";

        if (string.IsNullOrEmpty(schema))
            return instr;

        if (string.IsNullOrEmpty(instr))
            return schema;

        return instr + "\n\n" + schema;
    }

    public static (string Instructions, string JsonSchema) Split(string? combined)
    {
        var text = (combined ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
            return (string.Empty, string.Empty);

        foreach (var marker in JsonSplitMarkers)
        {
            var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;

            return (text[..idx].TrimEnd(), text[idx..].TrimStart());
        }

        return (text, string.Empty);
    }
}
