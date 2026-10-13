using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ContentEmpireSystem.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class LocalController(ILocalAiHealthService health) : ControllerBase
{
    [HttpGet("health")]
    public async Task<ActionResult<LocalAiHealthDto>> Health(CancellationToken ct)
        => Ok(await health.CheckAsync(ct));
}
