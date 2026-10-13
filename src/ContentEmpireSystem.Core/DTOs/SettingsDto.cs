namespace ContentEmpireSystem.Core.DTOs;

public class SettingItemDto
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSecret { get; set; }
    /// <summary>True when a secret key exists in DB but value is not returned to the client.</summary>
    public bool HasValue { get; set; }
}

public class UpdateSettingsRequest
{
    public List<SettingUpdateItem> Settings { get; set; } = [];
}

public class SettingUpdateItem
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class SettingsGroupDto
{
    public Dictionary<string, string> ApiKeys { get; set; } = new();
    public Dictionary<string, string> Prompts { get; set; } = new();
    public Dictionary<string, string> Ffmpeg { get; set; } = new();
    public Dictionary<string, string> Voice { get; set; } = new();
}
