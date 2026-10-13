using ContentEmpireSystem.Api.Helpers;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ContentEmpireSystem.Api.Controllers;

internal static class QueueEnqueueResponses
{
    public static IActionResult AcceptedJob(HttpContext http, Guid projectId, ProjectOperationKind kind)
    {
        var payload = ApiErrorWriter.Enrich(OperationHints.Queued(OperationHints.Describe(kind)), http);
        return new ObjectResult(new
        {
            accepted = true,
            projectId,
            message = payload.Error,
            hint = payload.Hint,
            operation = payload.Operation,
            correlationId = payload.CorrelationId,
            logHint = payload.LogHint
        })
        {
            StatusCode = StatusCodes.Status202Accepted
        };
    }

    public static IActionResult AlreadyProcessing(HttpContext http, Guid projectId, ProjectOperationKind kind)
    {
        var payload = ApiErrorWriter.Enrich(OperationHints.AlreadyRunning(OperationHints.Describe(kind)), http);
        return new ConflictObjectResult(new
        {
            accepted = true,
            alreadyProcessing = true,
            projectId,
            message = payload.Error,
            error = payload.Error,
            hint = payload.Hint,
            operation = payload.Operation,
            correlationId = payload.CorrelationId,
            logHint = payload.LogHint
        });
    }

    public static IActionResult AlreadyProcessingWithError(HttpContext http, Guid projectId, ProjectOperationKind kind)
        => AlreadyProcessing(http, projectId, kind);

    public static IActionResult QueueFull(HttpContext http, ProjectOperationKind kind) =>
        new ObjectResult(ApiErrorWriter.ToBody(OperationHints.QueueFull(OperationHints.Describe(kind)), http))
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable
        };

    public static IActionResult WorkflowConflict(HttpContext http, Guid projectId, string error, ProjectOperationKind kind)
    {
        var payload = ApiErrorWriter.Enrich(OperationHints.WorkflowBlocked(error, kind), http);
        return new ConflictObjectResult(new
        {
            error = payload.Error,
            hint = payload.Hint,
            operation = payload.Operation,
            projectId,
            correlationId = payload.CorrelationId,
            logHint = payload.LogHint
        });
    }
}
