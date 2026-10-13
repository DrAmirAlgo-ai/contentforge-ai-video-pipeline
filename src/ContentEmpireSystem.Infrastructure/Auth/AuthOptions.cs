namespace ContentEmpireSystem.Infrastructure.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public JwtOptions Jwt { get; set; } = new();
    public BootstrapAdminOptions BootstrapAdmin { get; set; } = new();
    public bool AllowPublicRegistration { get; set; }
}

public sealed class JwtOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "ContentForge";
    public string Audience { get; set; } = "ContentForge";
    public int ExpireMinutes { get; set; } = 480;
}

public sealed class BootstrapAdminOptions
{
    public bool Enabled { get; set; } = true;
    public string Email { get; set; } = "admin@contentforge.local";
    public string Password { get; set; } = "ChangeMe123!";
    public string DisplayName { get; set; } = "Admin";
}
