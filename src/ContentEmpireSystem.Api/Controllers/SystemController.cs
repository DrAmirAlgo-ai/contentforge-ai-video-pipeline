using System.Text.Json;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ContentEmpireSystem.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class SystemController(
    ISystemActivityService activity,
    ILocalGpuOrchestrator gpu,
    IOperationFileLogger operationLog) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [HttpGet("activity")]
    public ActionResult<IReadOnlyList<ActivityEntryDto>> GetActivity()
        => Ok(activity.GetSnapshot());

    /// <summary>Paths and hints for troubleshooting — see docs/OPERATIONS_AND_PATHS.md</summary>
    [HttpGet("diagnostics")]
    public ActionResult<object> GetDiagnostics()
        => Ok(new
        {
            logsDirectory = operationLog.GetLogsDirectory(),
            todayLogFile = operationLog.GetTodayLogPath(),
            logHint = OperationHints.LogHintText(),
            activityEndpoint = "/api/system/activity",
            gpuEndpoint = "/api/system/gpu"
        });



    [HttpGet("activity/stream")]

    public async Task StreamActivity(CancellationToken ct)

    {

        Response.Headers.CacheControl = "no-cache";

        Response.ContentType = "text/event-stream";



        async Task PushAsync()

        {

            var snapshot = activity.GetSnapshot();

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);

            await Response.WriteAsync($"data: {json}\n\n", ct);

            await Response.Body.FlushAsync(ct);

        }



        await PushAsync();



        void OnChanged() => _ = PushAsync();



        activity.Changed += OnChanged;

        try

        {

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

            while (await timer.WaitForNextTickAsync(ct))

                await PushAsync();

        }

        finally

        {

            activity.Changed -= OnChanged;

        }

    }



    [HttpGet("gpu")]

    public ActionResult<GpuStatusDto> GetGpuStatus()

        => Ok(gpu.GetStatus());

}


