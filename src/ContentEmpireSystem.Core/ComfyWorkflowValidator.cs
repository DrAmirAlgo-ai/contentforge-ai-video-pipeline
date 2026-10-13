using System.Text.Json;

using System.Text.RegularExpressions;



namespace ContentEmpireSystem.Core;



/// <summary>Validates ComfyUI workflow templates before token substitution.</summary>

public static class ComfyWorkflowValidator

{

    private static readonly Regex UnquotedPromptToken = new(

        @"(?<![""']){{PROMPT}}(?![""'])",

        RegexOptions.IgnoreCase | RegexOptions.Compiled);



    private static readonly Regex QuotedSeedToken = new(

        @"""{{SEED}}""",

        RegexOptions.IgnoreCase | RegexOptions.Compiled);



    public static IReadOnlyList<string> Validate(string workflowJson, string? workflowPath = null)

    {

        var errors = new List<string>();

        var label = string.IsNullOrWhiteSpace(workflowPath) ? "workflow" : workflowPath;



        if (string.IsNullOrWhiteSpace(workflowJson))

        {

            errors.Add($"{label}: الملف فارغ.");

            return errors;

        }



        if (UnquotedPromptToken.IsMatch(workflowJson))

            errors.Add($"{label}: يجب أن يكون {{PROMPT}} بين علامات اقتباس مثل \"{{PROMPT}}\".");



        if (QuotedSeedToken.IsMatch(workflowJson))

            errors.Add($"{label}: يجب أن يكون {{SEED}} رقماً بدون اقتباس — مثل \"seed\": {{{{SEED}}}}.");



        var probe = ComfyWorkflowSubstitutor.BuildProbeJson(workflowJson);



        try

        {

            using var _ = JsonDocument.Parse(probe);

        }

        catch (JsonException ex)

        {

            errors.Add($"{label}: JSON غير صالح بعد استبدال المتغيرات — {ex.Message}");

        }



        return errors;

    }



    public static void ValidateOrThrow(string workflowJson, string workflowPath)

    {

        var errors = Validate(workflowJson, workflowPath);

        if (errors.Count == 0)

            return;



        throw new InvalidOperationException(string.Join(" ", errors));

    }

}

