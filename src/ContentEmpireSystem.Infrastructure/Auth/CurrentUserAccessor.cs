using System.Security.Claims;
using ContentEmpireSystem.Core.Interfaces;
using Microsoft.AspNetCore.Http;

namespace ContentEmpireSystem.Infrastructure.Auth;

public sealed class CurrentUserAccessor(
    IHttpContextAccessor httpContextAccessor,
    IBackgroundJobUserContext backgroundJobUser) : ICurrentUserAccessor
{
    public string? UserId =>
        backgroundJobUser.HasImpersonation
            ? backgroundJobUser.UserId
            : httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    public bool IsAuthenticated =>
        backgroundJobUser.HasImpersonation
            ? backgroundJobUser.UserId is not null || backgroundJobUser.IsAdmin
            : httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public bool IsAdmin =>
        backgroundJobUser.HasImpersonation
            ? backgroundJobUser.IsAdmin
            : httpContextAccessor.HttpContext?.User.IsInRole(AuthRoles.Admin) == true;

    public string RequireUserId() =>
        UserId ?? throw new UnauthorizedAccessException("يجب تسجيل الدخول أولاً.");
}

public static class AuthRoles
{
    public const string Admin = "Admin";
    public const string User = "User";
}
