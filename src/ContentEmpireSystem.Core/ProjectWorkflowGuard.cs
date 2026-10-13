using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core;

public static class ProjectWorkflowGuard
{
    public static string? ValidateOperation(ProjectWorkflowStateDto? workflow, ProjectOperationKind kind)
    {
        if (workflow is null)
            return null;

        if (workflow.IsBusy)
            return workflow.ActiveOperation is not null
                ? $"المشروع مشغول حالياً: {workflow.ActiveOperation}"
                : "المشروع مشغول بعملية أخرى — انتظر حتى تنتهي.";

        if (!ProjectWorkflowResolver.CanRunOperation(workflow, kind))
        {
            return workflow.CurrentStep switch
            {
                ProjectWorkflowStep.Done => "المشروع مكتمل — لا حاجة لعملية إضافية.",
                ProjectWorkflowStep.Failed => "المشروع في حالة فشل — راجع سبب الرفض ثم أصلحه.",
                ProjectWorkflowStep.Produce when workflow.IsBusy => "الإنتاج جارٍ — انتظر اكتمال تركيب FFmpeg.",
                _ => $"الخطوة التالية المطلوبة: {workflow.NextActionAr}"
            };
        }

        return null;
    }
}
