using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;
using ContentEmpireSystem.Core.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ContentEmpireSystem.Infrastructure.Auth;

public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    JwtTokenService jwtTokenService,
    IOptions<AuthOptions> authOptions) : IAuthService
{
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = NormalizeEmail(request.Email);
        var user = await userManager.FindByEmailAsync(email)
            ?? throw new UnauthorizedAccessException("البريد أو كلمة المرور غير صحيحة.");

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            throw new UnauthorizedAccessException("البريد أو كلمة المرور غير صحيحة.");

        return await BuildAuthResponseAsync(user);
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var options = authOptions.Value;
        var hasUsers = await userManager.Users.AnyAsync(ct);
        if (!hasUsers)
            return await CreateUserAsync(request, isAdmin: true);

        if (!options.AllowPublicRegistration)
            throw new InvalidOperationException("التسجيل مغلق. اطلب حساباً من المسؤول.");

        return await CreateUserAsync(request, isAdmin: false);
    }

    public async Task<UserProfileDto?> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return null;

        var roles = await userManager.GetRolesAsync(user);
        return MapProfile(user, roles);
    }

    private async Task<AuthResponse> CreateUserAsync(RegisterRequest request, bool isAdmin)
    {
        var email = NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new InvalidOperationException("كلمة المرور يجب أن تكون 8 أحرف على الأقل.");

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
            throw new InvalidOperationException("البريد مستخدم بالفعل.");

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? email.Split('@')[0]
                : request.DisplayName.Trim(),
        };

        var create = await userManager.CreateAsync(user, request.Password);
        if (!create.Succeeded)
            throw new InvalidOperationException(string.Join(" ", create.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, isAdmin ? AuthRoles.Admin : AuthRoles.User);
        if (isAdmin)
            await userManager.AddToRoleAsync(user, AuthRoles.User);

        return await BuildAuthResponseAsync(user);
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var (token, expires) = jwtTokenService.CreateToken(user, roles);
        return new AuthResponse
        {
            Token = token,
            ExpiresAt = expires,
            User = MapProfile(user, roles)
        };
    }

    private static UserProfileDto MapProfile(ApplicationUser user, IList<string> roles) => new()
    {
        Id = user.Id,
        Email = user.Email ?? string.Empty,
        DisplayName = user.DisplayName,
        Roles = roles.ToList()
    };

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();
}
