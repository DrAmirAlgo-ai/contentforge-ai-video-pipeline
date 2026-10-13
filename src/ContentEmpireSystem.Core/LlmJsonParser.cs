using System.Text;

using System.Text.Json;

using System.Text.RegularExpressions;



namespace ContentEmpireSystem.Core;



/// <summary>Repairs common LLM JSON issues (fences, trailing commas, preamble text, Arabic quotes).</summary>

public static class LlmJsonParser

{

    private static readonly Regex TrailingCommaRegex = new(@",(\s*[}\]])", RegexOptions.Compiled);



    public static string Prepare(string raw)

    {

        if (string.IsNullOrWhiteSpace(raw))

            return "{}";



        var text = LlmResponseJsonHelper.StripThinkingBlocks(raw.Trim());

        text = StripMarkdownFences(text);

        text = text.Trim('\uFEFF');



        var start = text.IndexOf('{');

        if (start < 0)

            return text;



        if (TryExtractBalancedObject(text, start, out var balanced))

            return FixTrailingCommas(balanced);



        var last = text.LastIndexOf('}');

        if (last > start)

            return FixTrailingCommas(text[start..(last + 1)]);



        return FixTrailingCommas(text[start..]);

    }



    public static IEnumerable<string> AllRepairCandidates(string raw)

    {

        var prepared = Prepare(raw);

        yield return prepared;



        var normalizedQuotes = prepared

            .Replace('\u201c', '"').Replace('\u201d', '"')

            .Replace('\u2018', '\'').Replace('\u2019', '\'');

        if (!string.Equals(normalizedQuotes, prepared, StringComparison.Ordinal))

            yield return normalizedQuotes;



        foreach (var repaired in RepairVariants(prepared))

            yield return repaired;



        if (!string.Equals(normalizedQuotes, prepared, StringComparison.Ordinal))

        {

            foreach (var repaired in RepairVariants(normalizedQuotes))

                yield return repaired;

        }

    }



    public static T? Deserialize<T>(string raw, JsonSerializerOptions? options = null) where T : class

    {

        options ??= DefaultOptions;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in AllRepairCandidates(raw))

        {

            if (!seen.Add(candidate))

                continue;



            try

            {

                return JsonSerializer.Deserialize<T>(candidate, options);

            }

            catch (JsonException)

            {

                // try next repair variant

            }

        }



        return null;

    }



    public static readonly JsonSerializerOptions DefaultOptions = new()

    {

        PropertyNameCaseInsensitive = true,

        ReadCommentHandling = JsonCommentHandling.Skip,

        AllowTrailingCommas = true

    };



    private static IEnumerable<string> RepairVariants(string json)

    {

        var escapedQuotes = RepairUnescapedQuotesInStrings(json);

        if (!string.Equals(escapedQuotes, json, StringComparison.Ordinal))

            yield return FixTrailingCommas(escapedQuotes);



        var literalNewlines = RepairLiteralNewlinesInStrings(json);

        if (!string.Equals(literalNewlines, json, StringComparison.Ordinal))

            yield return FixTrailingCommas(literalNewlines);



        var combined = RepairLiteralNewlinesInStrings(RepairUnescapedQuotesInStrings(json));

        if (!string.Equals(combined, json, StringComparison.Ordinal))

            yield return FixTrailingCommas(combined);



        var closed = CloseTruncatedJson(combined);

        if (!string.Equals(closed, json, StringComparison.Ordinal))

            yield return FixTrailingCommas(closed);



        var closedQuotes = CloseTruncatedJson(escapedQuotes);

        if (!string.Equals(closedQuotes, json, StringComparison.Ordinal))

            yield return FixTrailingCommas(closedQuotes);

    }



    private static string StripMarkdownFences(string text)

    {

        if (!text.StartsWith("```", StringComparison.Ordinal))

            return text;



        var firstLineEnd = text.IndexOf('\n');

        if (firstLineEnd < 0)

            return text;



        text = text[(firstLineEnd + 1)..];

        var fenceEnd = text.LastIndexOf("```", StringComparison.Ordinal);

        if (fenceEnd > 0)

            text = text[..fenceEnd];



        return text.Trim();

    }



    private static bool TryExtractBalancedObject(string text, int start, out string json)

    {

        var depth = 0;

        var inString = false;

        var escape = false;



        for (var i = start; i < text.Length; i++)

        {

            var c = text[i];

            if (inString)

            {

                if (escape)

                    escape = false;

                else if (c == '\\')

                    escape = true;

                else if (c == '"')

                    inString = false;



                continue;

            }



            if (c == '"')

            {

                inString = true;

                continue;

            }



            if (c == '{')

                depth++;

            else if (c == '}')

            {

                depth--;

                if (depth == 0)

                {

                    json = text[start..(i + 1)];

                    return true;

                }

            }

        }



        json = "";

        return false;

    }



    private static string FixTrailingCommas(string json) =>

        TrailingCommaRegex.Replace(json, "$1");



    /// <summary>Escapes interior double-quotes that break JSON (common with Arabic narration).</summary>

    internal static string RepairUnescapedQuotesInStrings(string json)

    {

        if (string.IsNullOrEmpty(json))

            return json;



        var sb = new StringBuilder(json.Length + 64);

        var inString = false;

        var escape = false;



        for (var i = 0; i < json.Length; i++)

        {

            var c = json[i];

            if (!inString)

            {

                sb.Append(c);

                if (c == '"')

                {

                    inString = true;

                    escape = false;

                }



                continue;

            }



            if (escape)

            {

                sb.Append(c);

                escape = false;

                continue;

            }



            if (c == '\\')

            {

                sb.Append(c);

                escape = true;

                continue;

            }



            if (c == '"')

            {

                if (IsClosingStringQuote(json, i))

                {

                    sb.Append(c);

                    inString = false;

                }

                else

                {

                    sb.Append('\\');

                    sb.Append('"');

                }



                continue;

            }



            if (c < 32 && c is not '\t')

            {

                if (c is '\r' or '\n')

                    sb.Append("\\n");

                continue;

            }



            sb.Append(c);

        }



        if (inString)

            sb.Append('"');

        return sb.ToString();

    }



    internal static string RepairLiteralNewlinesInStrings(string json)

    {

        if (string.IsNullOrEmpty(json))

            return json;



        var sb = new StringBuilder(json.Length);

        var inString = false;

        var escape = false;



        for (var i = 0; i < json.Length; i++)

        {

            var c = json[i];

            if (!inString)

            {

                sb.Append(c);

                if (c == '"')

                {

                    inString = true;

                    escape = false;

                }



                continue;

            }



            if (escape)

            {

                sb.Append(c);

                escape = false;

                continue;

            }



            if (c == '\\')

            {

                sb.Append(c);

                escape = true;

                continue;

            }



            if (c == '"')

            {

                sb.Append(c);

                inString = false;

                continue;

            }



            if (c == '\n')

            {

                sb.Append("\\n");

                continue;

            }



            if (c == '\r')

            {

                if (i + 1 < json.Length && json[i + 1] == '\n')

                    i++;

                sb.Append("\\n");

                continue;

            }



            sb.Append(c);

        }



        return sb.ToString();

    }



    internal static string CloseTruncatedJson(string json)

    {

        if (string.IsNullOrWhiteSpace(json))

            return "{}";



        var inString = false;

        var escape = false;

        var stack = new Stack<char>();



        for (var i = 0; i < json.Length; i++)

        {

            var c = json[i];

            if (inString)

            {

                if (escape)

                    escape = false;

                else if (c == '\\')

                    escape = true;

                else if (c == '"')

                    inString = false;



                continue;

            }



            if (c == '"')

            {

                inString = true;

                continue;

            }



            switch (c)

            {

                case '{':

                case '[':

                    stack.Push(c);

                    break;

                case '}':

                    if (stack.Count > 0 && stack.Peek() == '{')

                        stack.Pop();

                    break;

                case ']':

                    if (stack.Count > 0 && stack.Peek() == '[')

                        stack.Pop();

                    break;

            }

        }



        if (!inString && stack.Count == 0)

            return json;



        var sb = new StringBuilder(json);

        if (inString)

            sb.Append('"');

        while (stack.Count > 0)

        {

            sb.Append(stack.Pop() switch

            {

                '{' => '}',

                '[' => ']',

                _ => '}'

            });

        }



        return sb.ToString();

    }



    private static bool IsClosingStringQuote(string json, int quoteIndex)

    {

        var j = quoteIndex + 1;

        while (j < json.Length && char.IsWhiteSpace(json[j]))

            j++;



        if (j >= json.Length)

            return true;



        return json[j] is ',' or '}' or ']' or ':';

    }

}


