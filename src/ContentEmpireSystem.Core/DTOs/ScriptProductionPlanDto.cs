namespace ContentEmpireSystem.Core.DTOs;

/// <summary>Production plan snapshot — stored on project columns and embedded in script JSON.</summary>
public class ScriptProductionPlanDto
{
    public int TargetWordCount { get; set; }

    /// <summary>Actual words in fullNarrationText after script generation.</summary>
    public int ActualWordCount { get; set; }

    /// <summary>Estimated narration duration from word count (~2.5 words/sec).</summary>
    public int EstimatedDurationSec { get; set; }

    /// <summary>Same as EstimatedDurationSec — kept for backward-compatible JSON/clients.</summary>
    public int TargetDurationSec { get; set; }

    public int ImageSegmentCount { get; set; }

    public int VideoSegmentCount { get; set; }

    public double SegmentDurationSec { get; set; }

    public string WorkflowPath { get; set; } = "ImageToVideo";

    /// <summary>Policy cap at plan creation time (Content.MaxVideoDurationSec).</summary>
    public int MaxVideoDurationSec { get; set; }
}
