using System.Text;

namespace ContentEmpireSystem.Core;

/// <summary>Hardcoded user-message directives — forces fresh topics and non-repetitive LLM output.</summary>
public static class PromptVariationHelper
{
    private static readonly string[] IdeaAngles =
    [
        "مقارنة تاريخية غير متوقعة",
        "آلية نفسية خفية في سلوك يومي",
        "نمط اجتماعي في ثقافة الشباب",
        "أسطورة مقابل علم",
        "تقليد منسي يُعاد تفسيره لليوم",
        "تحيز معرفي يظهر في موقف معاصر",
        "عادة اقتصادية أو اجتماعية تحت المجهر",
        "رمز أدبي أو فولكلوري يُطبَّق على الحياة المعاصرة",
        "ربط غير متوقع بين مجالين بعيدين",
        "سلوك صغير يكشف حقيقة كبيرة",
    ];

    public static string BuildIdeaUserPrompt(IReadOnlyList<string>? avoidIdeas = null)
    {
        var seed = Random.Shared.Next(100_000, 999_999);
        var angle = IdeaAngles[Random.Shared.Next(IdeaAngles.Length)];
        var sb = new StringBuilder();
        sb.AppendLine("تعليمات إلزامية للتنويع:");
        sb.AppendLine($"- بذرة إبداعية: {seed} (استخدمها لاختيار موضوع جديد).");
        sb.AppendLine($"- زاوية الاستكشاف المطلوبة: {angle}.");
        sb.AppendLine("- قدّم فكرة واحدة جديدة تماماً — عنوان جديد، مراجع جديدة، تطبيق جديد.");
        sb.AppendLine("- لا تعيد مواضيع أو عناوين أو زوايا من تشغيلات سابقة.");
        sb.AppendLine("- تجنّب التكرار: قوائم امتنان، نصائح عادات عامة، تحفيز مكرر، مساعدة ذاتية كليشيه.");
        sb.AppendLine("- اكتب كل المخرجات بالعربية فقط (ما عدا أسماء أعمال أجنبية أصلية).");
        if (avoidIdeas is { Count: > 0 })
        {
            sb.AppendLine("- لا تكرر أو تعيد صياغة هذه الأفكار السابقة:");
            foreach (var idea in avoidIdeas.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal))
                sb.AppendLine($"---\n{idea.Trim()}\n---");
        }

        return sb.ToString().Trim();
    }

    public static string BuildScriptUserPrompt(string confirmedIdea, string? previousScript = null)
    {
        var seed = Random.Shared.Next(100_000, 999_999);
        var sb = new StringBuilder();
        sb.AppendLine("MANDATORY VARIATION DIRECTIVE:");
        sb.AppendLine($"- Creative seed: {seed}.");
        sb.AppendLine("- Write a fresh script for the confirmed idea below — unique wording, fresh hook, no template recycling.");
        if (!string.IsNullOrWhiteSpace(previousScript))
        {
            sb.AppendLine("- Do NOT copy phrasing from this previous script on the same project:");
            sb.AppendLine($"---\n{Truncate(previousScript, 1200)}\n---");
        }

        sb.AppendLine();
        sb.AppendLine("CONFIRMED IDEA:");
        sb.AppendLine(confirmedIdea.Trim());
        return sb.ToString().Trim();
    }

    private static string Truncate(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars] + "…";
}
