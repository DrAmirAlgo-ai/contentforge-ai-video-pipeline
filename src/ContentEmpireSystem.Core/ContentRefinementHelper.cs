using System.Text;
using System.Text.Json;
using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core;

/// <summary>Prompts for user-directed idea/script edits via the script LLM provider.</summary>
public static class ContentRefinementHelper
{
    public const string IdeaRefineInstructions = """
        أنت محرر أفكار فيديو قصيرة بالعربية.
        المستخدم يرسل فكرة حالية وطلب تعديل محدد.
        طبّق التعديل المطلوب فقط — لا تعيد اختراع الفكرة من الصفر إلا إذا طُلب صراحة.
        أرجع الفكرة كاملة بنفس التنسيق (نوع المصدر، أ. العنوان، ب. التطبيق، ج. شرح مختصر، د. المراجع).
        كل النص عربي. لا markdown ولا JSON ولا شرح لما فعلت.
        """;

    public const string ScriptRefineInstructions = """
        You are an Arabic short-form video script editor.
        The user sends the CURRENT script as JSON and a specific edit request in Arabic or English.
        Apply ONLY the requested change. Keep the same dialect, tone, and structure unless asked otherwise.
        Return the FULL updated script as valid JSON (same fields as input: fullNarrationText, primaryHook, body, seo, imageSuggestions, videoSuggestions, productionPlan when present).
        If the edit touches narration only, keep imageSuggestions and videoSuggestions unless scene splits must change.
        Write narration in Egyptian Arabic dialect. No markdown fences.
        """;

    public static string BuildIdeaRefineUserPrompt(string currentIdea, string instruction) =>
        $"""
        الفكرة الحالية:
        ---
        {currentIdea.Trim()}
        ---

        طلب التعديل:
        {instruction.Trim()}
        """;

    public static string BuildScriptRefineUserPrompt(ScriptOutputDto script, string instruction)
    {
        var json = JsonSerializer.Serialize(script, LlmJsonParser.DefaultOptions);
        if (json.Length > 48_000)
            json = json[..48_000] + "…";

        return $"""
            CURRENT SCRIPT JSON:
            {json}

            USER EDIT REQUEST:
            {instruction.Trim()}

            Return the complete updated script JSON only.
            """;
    }

    public static void MergeRefinedScript(ScriptOutputDto existing, ScriptOutputDto refined)
    {
        if (refined.ProductionPlan is null && existing.ProductionPlan is not null)
            refined.ProductionPlan = existing.ProductionPlan;

        if (refined.ImageSuggestions.Count == 0 && existing.ImageSuggestions.Count > 0)
            refined.ImageSuggestions = existing.ImageSuggestions;

        if (refined.VideoSuggestions.Count == 0 && existing.VideoSuggestions.Count > 0)
            refined.VideoSuggestions = existing.VideoSuggestions;

        if (string.IsNullOrWhiteSpace(refined.VoiceCodeRawText))
            refined.VoiceCodeRawText = existing.VoiceCodeRawText;

        if (refined.VoiceSegments.Count == 0 && existing.VoiceSegments.Count > 0)
            refined.VoiceSegments = existing.VoiceSegments;
    }
}
