using System.Text.Json;

namespace ContentEmpireSystem.Core;

/// <summary>Safe token replacement for ComfyUI workflow JSON templates.</summary>
public static class ComfyWorkflowSubstitutor
{
    /// <summary>
    /// Replaces workflow tokens. Quoted tokens ("{{PROMPT}}") are JSON-escaped;
    /// unquoted tokens ({{SEED}}) are inserted as raw values.
    /// </summary>
    public static string Apply(string workflowJson, IReadOnlyDictionary<string, string> replacements)
    {
        var text = workflowJson;
        foreach (var (token, value) in replacements)
        {
            var quotedToken = $"\"{token}\"";
            if (text.Contains(quotedToken, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Replace(quotedToken, JsonSerializer.Serialize(value), StringComparison.OrdinalIgnoreCase);
            }
            else if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Replace(token, value, StringComparison.OrdinalIgnoreCase);
            }
        }

        return text;
    }

    public static string BuildProbeJson(string workflowJson) =>
        Apply(workflowJson, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{{PROMPT}}"] = "probe prompt — نص عربي للاختبار",
            ["{{OUTPUT_PREFIX}}"] = "contentforge/probe",
            ["{{SEED}}"] = "12345",
            ["{{IMAGE_FILE}}"] = "probe.png",
            ["{{IMAGE_PATH}}"] = "input/probe.png",
            ["{{STEPS}}"] = "8",
            ["{{CFG}}"] = "4.0",
            ["{{DENOISE}}"] = "0.6",
            ["{{WIDTH}}"] = "480",
            ["{{HEIGHT}}"] = "832",
            ["{{LENGTH}}"] = "115",
            ["{{NEGATIVE_PROMPT}}"] = "blurry, low quality",
            ["{{UNET_NAME}}"] = "Wan2.2-TI2V-5B-Q4_K_S.gguf",
            ["{{VAE_NAME}}"] = "wan2.2_vae.safetensors",
        });
}
