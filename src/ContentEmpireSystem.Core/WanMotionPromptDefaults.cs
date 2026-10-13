namespace ContentEmpireSystem.Core;

public static class WanMotionPromptDefaults
{
    public const string PositiveRules = """
        subtle cinematic motion, slow and smooth
        colors locked, lighting consistent throughout
        illustration style preserved, flat shading consistent
        locked stable camera or slow gentle pan as scene requires
        no morphing, no distortion, no flickering
        minimal ambient movement only (hair, breeze, smoke)
        """;

    public const string NegativeRules = """
        morphing, melting, warping
        flickering, color shift, lighting change
        jitter, jumping, teleporting, sudden movement
        face distortion, body distortion, head morphing
        realistic skin, photorealistic, 3D render
        artifacts, noise, blurry
        fast motion, unnatural movement
        ghosting, smearing, temporal blur, hazy overlay, foggy layer, double exposure
        """;
}
