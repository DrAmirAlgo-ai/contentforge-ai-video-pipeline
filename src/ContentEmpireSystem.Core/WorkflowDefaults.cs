using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core;

public static class WorkflowDefaults
{
    public const string FluxRelative = "deploy/workflows/flux.json";
    public const string WanTi2vRelative = "deploy/workflows/wan-ti2v.json";
    public const string WanTi2vLowVramRelative = "deploy/workflows/wan-ti2v-lowvram.json";
    public const string WanT2vRelative = "deploy/workflows/wan-t2v.json";

    public static IReadOnlyDictionary<string, string> All => new Dictionary<string, string>
    {
        [SettingKeys.LocalComfyWorkflowFlux] = FluxRelative,
        [SettingKeys.LocalComfyWorkflowWanTi2v] = WanTi2vRelative,
        [SettingKeys.LocalComfyWorkflowWanT2v] = WanT2vRelative,
    };
}
