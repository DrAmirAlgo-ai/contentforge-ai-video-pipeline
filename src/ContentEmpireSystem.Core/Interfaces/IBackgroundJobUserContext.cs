namespace ContentEmpireSystem.Core.Interfaces;

/// <summary>Scoped impersonation for background queue workers (no HTTP context).</summary>
public interface IBackgroundJobUserContext
{
    void Set(string? userId, bool isAdmin = false);
    void Clear();
    string? UserId { get; }
    bool IsAdmin { get; }
    bool HasImpersonation { get; }
}
