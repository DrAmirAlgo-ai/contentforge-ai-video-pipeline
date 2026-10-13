using System.Text.Json;
using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core;

/// <summary>نقطة دخول موحّدة لقراءة السكريبت المخزّن كنص JSON.</summary>
public static class ScriptJsonReader
{
    public static ScriptOutputDto? TryParse(string? raw, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        options ??= LlmJsonParser.DefaultOptions;
        return ScriptJsonNormalizer.Parse(raw, options);
    }
}
