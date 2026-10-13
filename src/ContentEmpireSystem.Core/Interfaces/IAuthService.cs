using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<UserProfileDto?> GetProfileAsync(string userId, CancellationToken ct = default);
}
