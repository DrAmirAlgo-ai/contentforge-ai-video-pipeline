namespace ContentEmpireSystem.Api;

/// <summary>Loads KEY=VALUE pairs from .env into environment (does not override existing vars).</summary>
internal static class DotEnvLoader
{
    public static void LoadIfPresent()
    {
        foreach (var path in CandidatePaths())
        {
            if (!File.Exists(path))
                continue;

            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;

                var eq = trimmed.IndexOf('=');
                if (eq <= 0)
                    continue;

                var key = trimmed[..eq].Trim();
                var value = trimmed[(eq + 1)..].Trim().Trim('"');
                if (string.IsNullOrEmpty(key))
                    continue;

                if (Environment.GetEnvironmentVariable(key) is null)
                    Environment.SetEnvironmentVariable(key, value);

                // Map DEPLOYMENT_PROFILE → ASP.NET Core config style for consistency
                if (key.Equals("DEPLOYMENT_PROFILE", StringComparison.OrdinalIgnoreCase)
                    && Environment.GetEnvironmentVariable("Api__DeploymentProfile") is null)
                {
                    Environment.SetEnvironmentVariable("Api__DeploymentProfile", value);
                }

                if ((key.Equals("GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("GOOGLE_API_KEY", StringComparison.OrdinalIgnoreCase))
                    && Environment.GetEnvironmentVariable("Api__Gemini") is null)
                {
                    Environment.SetEnvironmentVariable("Api__Gemini", value);
                }

                if ((key.Equals("HUGGINGFACE_API_KEY", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("HF_API_KEY", StringComparison.OrdinalIgnoreCase))
                    && Environment.GetEnvironmentVariable("Api__HuggingFace") is null)
                {
                    Environment.SetEnvironmentVariable("Api__HuggingFace", value);
                }
            }

            return;
        }
    }

    private static IEnumerable<string> CandidatePaths()
    {
        foreach (var dir in AncestorDirectories(Directory.GetCurrentDirectory()))
        {
            yield return Path.Combine(dir, ".env");
        }

        yield return Path.Combine(AppContext.BaseDirectory, ".env");
    }

    private static IEnumerable<string> AncestorDirectories(string start)
    {
        var dir = start;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            yield return dir;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent is null || parent == dir)
                break;
            dir = parent;
        }
    }
}
