using System.Text.Json;

using System.Text.Json.Nodes;

using System.Text.RegularExpressions;

using ContentEmpireSystem.Core.DTOs;



namespace ContentEmpireSystem.Core;



public static class ScriptJsonNormalizer

{

    private static readonly Regex FullNarrationRegex = new(

        @"""fullNarrationText""\s*:\s*""((?:[^""\\]|\\.)*)""",

        RegexOptions.IgnoreCase | RegexOptions.Compiled);



    private static readonly Regex PrimaryHookRegex = new(

        @"""primaryHook""\s*:\s*""((?:[^""\\]|\\.)*)""",

        RegexOptions.IgnoreCase | RegexOptions.Compiled);



    public static ScriptOutputDto? Parse(string raw, JsonSerializerOptions? options = null)

    {

        options ??= LlmJsonParser.DefaultOptions;

        var seen = new HashSet<string>(StringComparer.Ordinal);



        foreach (var candidate in LlmJsonParser.AllRepairCandidates(raw))

        {

            if (!seen.Add(candidate))

                continue;



            var script = TryDeserializeScript(candidate, options);

            if (script is not null)

            {

                EnrichMissingFields(script);

                return script;

            }

        }



        var salvaged = TrySalvageScript(raw);

        if (salvaged is not null)

        {

            EnrichMissingFields(salvaged);

            return salvaged;

        }



        return null;

    }



    private static ScriptOutputDto? TryDeserializeScript(string json, JsonSerializerOptions options)

    {

        var script = LlmJsonParser.Deserialize<ScriptOutputDto>(json, options);

        if (script is not null)

            return script;



        if (!CanParseJson(json))

            return null;



        var normalized = NormalizeKeys(json);

        return LlmJsonParser.Deserialize<ScriptOutputDto>(normalized, options);

    }



    private static bool CanParseJson(string json)

    {

        try

        {

            JsonNode.Parse(json);

            return true;

        }

        catch

        {

            return false;

        }

    }



    private static ScriptOutputDto? TrySalvageScript(string raw)

    {

        var prepared = LlmJsonParser.Prepare(raw);

        var narration = ExtractStringValue(prepared, FullNarrationRegex, "fullNarrationText");

        if (string.IsNullOrWhiteSpace(narration))

            return null;



        var hook = ExtractStringValue(prepared, PrimaryHookRegex, "primaryHook");

        return new ScriptOutputDto

        {

            FullNarrationText = narration,

            PrimaryHook = hook ?? Truncate(narration, 160),

            ExtendedHook = hook ?? Truncate(narration, 200),

            Body = new ScriptBodyDto

            {

                PartOneProblem = Truncate(narration, 400),

                PartTwoSolution = ""

            },

            ConclusionAndCta = ""

        };

    }



    private static string? ExtractStringValue(string json, Regex regex, string key)

    {

        var match = regex.Match(json);

        if (match.Success)

            return UnescapeJsonString(match.Groups[1].Value);



        return ExtractLooseStringValue(json, key);

    }



    private static string? ExtractLooseStringValue(string json, string key)

    {

        var marker = $"\"{key}\"";

        var idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (idx < 0)

            return null;



        idx = json.IndexOf('"', idx + marker.Length);

        if (idx < 0)

            return null;



        var start = idx + 1;

        var sb = new System.Text.StringBuilder();

        var escape = false;

        for (var i = start; i < json.Length; i++)

        {

            var c = json[i];

            if (escape)

            {

                sb.Append(c);

                escape = false;

                continue;

            }



            if (c == '\\')

            {

                escape = true;

                continue;

            }



            if (c == '"')

                break;



            sb.Append(c);

        }



        var value = sb.ToString().Trim();

        return string.IsNullOrWhiteSpace(value) ? null : UnescapeJsonString(value);

    }



    private static string UnescapeJsonString(string value) =>

        value

            .Replace("\\n", "\n", StringComparison.Ordinal)

            .Replace("\\r", "\r", StringComparison.Ordinal)

            .Replace("\\t", "\t", StringComparison.Ordinal)

            .Replace("\\\"", "\"", StringComparison.Ordinal)

            .Replace("\\\\", "\\", StringComparison.Ordinal);



    public static string NormalizeKeys(string json)

    {

        try

        {

            var node = JsonNode.Parse(json);

            if (node is null)

                return json;



            var renamed = RenameNode(node);

            CoerceSuggestionArrays(renamed);

            return renamed?.ToJsonString() ?? json;

        }

        catch

        {

            return json;

        }

    }



    private static void CoerceSuggestionArrays(JsonNode? node)

    {

        if (node is not JsonObject obj)

            return;



        CoerceStringArrayToSuggestionObjects(obj, "imageSuggestions", "image_suggestions", linkToImage: false);

        CoerceStringArrayToSuggestionObjects(obj, "videoSuggestions", "video_suggestions", linkToImage: true);

    }



    private static void CoerceStringArrayToSuggestionObjects(

        JsonObject obj, string camelKey, string snakeKey, bool linkToImage)

    {

        var key = obj.ContainsKey(camelKey) ? camelKey : snakeKey;

        if (!obj.TryGetPropertyValue(key, out var arrNode) || arrNode is not JsonArray arr || arr.Count == 0)

            return;



        if (arr[0] is not JsonValue)

            return;



        var output = new JsonArray();

        var order = 1;

        foreach (var item in arr)

        {

            if (item is not JsonValue val || !val.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))

                continue;



            var row = new JsonObject

            {

                ["sortOrder"] = order,

                ["narrationExcerpt"] = text

            };

            if (linkToImage)

            {

                row["linkedImageSortOrder"] = order;

                row["userMotionPrompt"] = "";

            }



            output.Add(row);

            order++;

        }



        obj[camelKey] = output;

        if (!string.Equals(key, camelKey, StringComparison.Ordinal))

            obj.Remove(snakeKey);

    }



    public static void EnrichMissingFields(ScriptOutputDto script)

    {

        var narration = script.FullNarrationText?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(narration))

        {

            narration = string.Join("\n\n", new[]

            {

                script.Body.PartOneProblem,

                script.Body.PartTwoSolution,

                script.ConclusionAndCta

            }.Where(s => !string.IsNullOrWhiteSpace(s)));



            if (string.IsNullOrWhiteSpace(narration) && script.ImageSuggestions.Count > 0)

            {

                narration = string.Join(" ",

                    script.ImageSuggestions

                        .OrderBy(i => i.SortOrder)

                        .Select(i => i.NarrationExcerpt)

                        .Where(s => !string.IsNullOrWhiteSpace(s)));

            }



            if (string.IsNullOrWhiteSpace(narration) && script.VideoSuggestions.Count > 0)

            {

                narration = string.Join(" ",

                    script.VideoSuggestions

                        .OrderBy(v => v.SortOrder)

                        .Select(v => v.NarrationExcerpt)

                        .Where(s => !string.IsNullOrWhiteSpace(s)));

            }



            if (!string.IsNullOrWhiteSpace(narration))

                script.FullNarrationText = narration;

        }



        if (string.IsNullOrWhiteSpace(script.ViewerBenefit) && !string.IsNullOrWhiteSpace(script.PrimaryHook))

            script.ViewerBenefit = script.PrimaryHook;



        if (string.IsNullOrWhiteSpace(script.PrimaryHook) && !string.IsNullOrWhiteSpace(narration))

            script.PrimaryHook = Truncate(narration, 160);



        if (string.IsNullOrWhiteSpace(script.ExtendedHook) && !string.IsNullOrWhiteSpace(script.PrimaryHook))

            script.ExtendedHook = script.PrimaryHook;



        if (string.IsNullOrWhiteSpace(script.Body.PartOneProblem) && !string.IsNullOrWhiteSpace(narration))

            script.Body.PartOneProblem = Truncate(narration, 400);



        if (string.IsNullOrWhiteSpace(script.Seo.Title) && !string.IsNullOrWhiteSpace(script.PrimaryHook))

            script.Seo.Title = Truncate(script.PrimaryHook, 100);



        if (string.IsNullOrWhiteSpace(script.Seo.Description) && !string.IsNullOrWhiteSpace(narration))

            script.Seo.Description = Truncate(narration, 200);

    }



    public static bool HasUsableContent(ScriptOutputDto script) =>

        !string.IsNullOrWhiteSpace(script.FullNarrationText)

        || !string.IsNullOrWhiteSpace(script.PrimaryHook)

        || script.ImageSuggestions.Count > 0

        || script.VideoSuggestions.Count > 0;



    public static bool HasRequiredScenes(ScriptOutputDto script, ScriptProductionPlanDto plan, int? minSegments = null)
    {
        var min = minSegments ?? ProductionRulesDto.Defaults.MinSegmentsForProduce;

        if (ProductionPlanResolver.IsTextToVideo(plan.WorkflowPath))
        {
            return script.VideoSuggestions.Count >= min
                   && script.VideoSuggestions.All(v => !string.IsNullOrWhiteSpace(v.NarrationExcerpt));
        }

        return script.ImageSuggestions.Count >= min
               && script.VideoSuggestions.Count >= min
               && script.ImageSuggestions.All(i => !string.IsNullOrWhiteSpace(i.NarrationExcerpt))
               && script.ImageSuggestions.All(i =>
                   !string.IsNullOrWhiteSpace(i.SubjectDescription)
                   || !string.IsNullOrWhiteSpace(i.ActionDescription)
                   || !string.IsNullOrWhiteSpace(i.Prompt));
    }



    private static JsonNode? RenameNode(JsonNode node)

    {

        if (node is JsonObject obj)

        {

            var output = new JsonObject();

            foreach (var (key, value) in obj)

            {

                if (value is null)

                    continue;

                output[ToCamelCase(key)] = RenameNode(value);

            }



            if (output.Count == 1)

            {

                if (output["script"] is JsonObject scriptObj)

                    return scriptObj;

                if (output["data"] is JsonObject dataObj)

                    return dataObj;

            }



            return output;

        }



        if (node is JsonArray arr)

        {

            var output = new JsonArray();

            foreach (var item in arr)

            {

                if (item is null)

                    continue;

                output.Add(RenameNode(item));

            }



            return output;

        }



        return node.DeepClone();

    }



    private static string ToCamelCase(string key)

    {

        if (!key.Contains('_'))

            return key;



        var parts = key.Split('_', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)

            return key;



        return parts[0] + string.Concat(parts.Skip(1).Select(static p =>

            p.Length == 0 ? "" : char.ToUpperInvariant(p[0]) + p[1..]));

    }



    private static string Truncate(string text, int max) =>

        text.Length <= max ? text : text[..max].Trim() + "…";

}


