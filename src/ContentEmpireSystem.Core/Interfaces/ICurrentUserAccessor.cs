namespace ContentEmpireSystem.Core.Interfaces;

public interface ICurrentUserAccessor
{
    string? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }
    string RequireUserId();
}
