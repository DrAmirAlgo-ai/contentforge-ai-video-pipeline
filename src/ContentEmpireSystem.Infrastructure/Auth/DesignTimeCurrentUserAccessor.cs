using ContentEmpireSystem.Core.Interfaces;

namespace ContentEmpireSystem.Infrastructure.Auth;

/// <summary>Design-time / tests — bypasses ownership filter.</summary>
public sealed class DesignTimeCurrentUserAccessor : ICurrentUserAccessor
{
    public string? UserId => null;
    public bool IsAuthenticated => false;
    public bool IsAdmin => true;

    public string RequireUserId() =>
        throw new UnauthorizedAccessException("No user in design-time context.");
}
