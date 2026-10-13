namespace ContentEmpireSystem.Core.Enums;

/// <summary>Per-image motion style for still-frame segments (stored as snake_case in DB/API).</summary>
public enum ImageAnimationMode
{
    ZoomIn,
    ZoomOut,
    PanLeft,
    PanRight,
    PanUp,
    PanDown,
    OversizePanRight,
    OversizePanLeft,
    OversizePanUp,
    OversizePanDown,
    KenBurns,
    KenBurnsOut,
    ZoomPulse,
    DiagonalDr,
    DiagonalDl,
    Static,
    Random
}
