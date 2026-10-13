using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ContentEmpireSystem.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class SettingsController(
    ISystemSettingsService settingsService,
    ILocalAiHealthService healthService) : ControllerBase
{
    [HttpGet("admin")]
    public async Task<ActionResult<List<SettingItemDto>>> GetAllForAdmin(CancellationToken ct)
        => Ok(await settingsService.GetAllForAdminAsync(ct));

    [HttpGet("grouped")]
    public async Task<ActionResult<SettingsGroupDto>> GetGrouped(CancellationToken ct)
        => Ok(await settingsService.GetGroupedAsync(ct));

    /// <summary>Effective deployment profile, providers, and readiness — single source for dashboard status bar.</summary>
    [HttpGet("runtime-summary")]
    public async Task<ActionResult<LocalAiHealthDto>> GetRuntimeSummary(CancellationToken ct)
        => Ok(await healthService.CheckAsync(ct));

    [HttpGet("production-rules")]
    public async Task<ActionResult<ProductionRulesDto>> GetProductionRules(CancellationToken ct)
        => Ok(await settingsService.GetProductionRulesAsync(ct));

    [HttpPut]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Update([FromBody] UpdateSettingsRequest request, CancellationToken ct)
    {
        var updates = request.Settings.Select(s => (s.Key, s.Value)).ToList();
        await settingsService.SaveBulkAsync(updates, ct);
        return NoContent();
    }
}
