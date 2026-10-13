using ContentEmpireSystem.Core.Interfaces;

namespace ContentEmpireSystem.Infrastructure.Auth;

public sealed class BackgroundJobUserContext : IBackgroundJobUserContext
{
    private string? _userId;
    private bool _isAdmin;

    public void Set(string? userId, bool isAdmin = false)
    {
        _userId = userId;
        _isAdmin = isAdmin;
    }

    public void Clear()
    {
        _userId = null;
        _isAdmin = false;
    }

    public string? UserId => _userId;
    public bool IsAdmin => _isAdmin;
    public bool HasImpersonation => _userId is not null || _isAdmin;
}
