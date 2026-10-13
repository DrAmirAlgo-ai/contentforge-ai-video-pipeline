using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Entities;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Infrastructure.Services.Local;
using Microsoft.EntityFrameworkCore;

namespace ContentEmpireSystem.Infrastructure.Data;

public static class DatabaseSeeder
{
    public static async Task SeedDataAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        await MigrateLegacyContentAsync(db, ct);
        await MigrateSplitJsonPromptsAsync(db, ct);
        await MigrateWanMotionPromptContentAsync(db, ct);
        await CleanupLegacyApprovalAndTelegramAsync(db, ct);

        var existingCategoryTypes = await db.ContentCategories
            .Select(c => c.CategoryType)
            .ToHashSetAsync(ct);

        foreach (var cat in GetAllCategoryDefinitions())
        {
            if (!existingCategoryTypes.Contains(cat.CategoryType))
                db.ContentCategories.Add(cat);
        }

        var defaultSettings = GetDefaultSettings();
        var existingSettingKeys = await db.SystemSettings
            .Select(s => s.Key)
            .ToHashSetAsync(ct);
        var existingKeyLookup = existingSettingKeys
            .Select(k => k.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value, category, description, isSecret) in defaultSettings)
        {
            var trimmedKey = key.Trim();
            if (!existingKeyLookup.Contains(trimmedKey))
            {
                db.SystemSettings.Add(new SystemSetting
                {
                    Key = key,
                    Value = value,
                    Category = category,
                    Description = description,
                    IsSecret = isSecret
                });
                existingKeyLookup.Add(trimmedKey);
            }
        }

        await db.SaveChangesAsync(ct);
        await EnvironmentSettingsSync.ApplyAsync(db, ct);
        await EnvironmentSettingsSync.SyncProvidersToDeploymentProfileAsync(db, ct);
    }

    /// <summary>SQL Server only — patches DBs created before EF migrations.</summary>
    public static async Task ApplyLegacySchemaPatchesAsync(ApplicationDbContext db, DatabaseOptions databaseOptions, CancellationToken ct = default)
    {
        if (databaseOptions.IsSqlServer)
        {
            await ApplySchemaPatchesAsync(db, ct);
        }
    }

    internal const string DefaultScriptPrompt =
        JsonPromptDefaults.ScriptInstructions + "\n\n" + JsonPromptDefaults.ScriptJsonSchema;

    internal const string DefaultFluxPrompt = """
        Ultra-detailed cinematic illustration, photorealistic, professional quality, 9:16 vertical composition,
        dramatic lighting, rich colors, sharp focus, depth of field, no text overlay, no watermark.
        Match the scene topic from the prompt — any subject.
        """;

    internal const string DefaultScenePlannerPrompt =
        JsonPromptDefaults.ScenePlannerInstructions + "\n\n" + JsonPromptDefaults.ScenePlannerJsonSchema;

    internal const string DefaultImageScenesPrompt =
        JsonPromptDefaults.ImageScenesInstructions + "\n\n" + JsonPromptDefaults.ImageScenesJsonSchema;

    internal const string DefaultQwenMediaGuidance = """
        Legacy — use Prompt.ScenePlanner instead. Scene count is decided by narrative structure, not time.
        """;

    internal const string DefaultWanVideoPrompt = """
        9:16 vertical cinematic video. Locked stable camera, no camera shake.
        Very slow subtle motion only. Preserve subject identity and facial features exactly.
        Gentle ambient movement, natural lighting, photorealistic, no on-screen text.
        """;

    internal const string DefaultIdeaPrompt =
        JsonPromptDefaults.IdeaInstructions + "\n\n" + JsonPromptDefaults.IdeaJsonSchema;

    private static List<(string Key, string Value, string Category, string Description, bool IsSecret)> GetDefaultSettings()
    {
        return
        [
            (SettingKeys.ScriptProvider, "Auto", "Api", "AI provider: Auto connects Qwen via LM Studio or Ollama", false),
            (SettingKeys.OllamaBaseUrl, "http://localhost:11434", "Api", "Ollama server URL (local, unlimited)", false),
            (SettingKeys.OllamaModel, "qwen2.5:7b", "Api", "Ollama model name (run: ollama pull qwen2.5:7b)", false),
            (SettingKeys.LmStudioBaseUrl, "http://127.0.0.1:1234", "Api", "LM Studio OpenAI API (Qwen3.5-9B)", false),
            (SettingKeys.LmStudioModel, "qwen3.5-9b", "Api", "LM Studio model id (must match /v1/models)", false),
            (SettingKeys.ImageProvider, "ComfyFlux", "Api", "Default image provider: ComfyFlux|HuggingFace", false),
            (SettingKeys.VideoProvider, "WanTI2V", "Api", "Default video provider: WanTI2V|WanT2V", false),
            (SettingKeys.ProductionWorkflowPath, "ImageToVideo", "Api", "Workflow path: ImageToVideo|TextToVideo", false),
            (SettingKeys.DeploymentProfile, "Local", "Api", "Deployment: Local|Online", false),
            (SettingKeys.RequireWanAnimation, "true", "Api", "Require Wan TI2V animation before produce (true=animate images, false=allow static + Ken Burns)", false),
            (SettingKeys.LocalComfyUiBaseUrl, "http://127.0.0.1:8188", "Local", "ComfyUI Desktop URL (this PC)", false),
            (SettingKeys.LocalComfyWorkflowFlux, WorkflowDefaults.FluxRelative, "Local", "Path to ComfyUI API JSON — FLUX ({{PROMPT}}, {{OUTPUT_PREFIX}}, {{SEED}})", false),
            (SettingKeys.LocalComfyWorkflowWanTi2v, WorkflowDefaults.WanTi2vRelative, "Local", "Path to ComfyUI API JSON — Wan TI2V ({{PROMPT}}, {{IMAGE_FILE}}, {{OUTPUT_PREFIX}})", false),
            (SettingKeys.LocalComfyWorkflowWanT2v, WorkflowDefaults.WanT2vRelative, "Local", "Path to ComfyUI API JSON — Wan T2V ({{PROMPT}}, {{OUTPUT_PREFIX}})", false),
            (SettingKeys.LocalComfyOutputPrefix, "contentforge", "Local", "Comfy output subfolder prefix (relative to ComfyUI/output)", false),
            (SettingKeys.LocalComfyPollIntervalMs, "2500", "Local", "ComfyUI poll interval ms", false),
            (SettingKeys.LocalComfyTimeoutMinutes, "90", "Local", "ComfyUI job timeout minutes (Wan TI2V default 90)", false),
            (SettingKeys.LocalGpuCooldownSeconds, "8", "Local", "Seconds between GPU engine switches (Qwen→FLUX→Wan)", false),
            (SettingKeys.LocalFluxQualityPreset, FluxQualityPresets.Default, "Local", "FLUX quality preset: fast|balanced|high|ultra|custom", false),
            (SettingKeys.LocalComfySafeVramFree, "true", "Local", "Safe VRAM free (no unload_models — avoids GGUF crash)", false),
            (SettingKeys.LocalComfyInterImageDelayMs, "2000", "Local", "Delay ms between FLUX/Wan jobs in same batch (queue settle)", false),
            (SettingKeys.LocalComfyEngineSwitchDelaySec, "6", "Local", "Seconds to wait FLUX→Wan before soft VRAM free", false),
            (SettingKeys.LocalComfyAutoRestartAfterFlux, "true", "Local", "Restart ComfyUI once after FLUX batch (frees VRAM for Wan)", false),
            (SettingKeys.LocalWanApplyLowVramLimits, "false", "Local", "Enable auto-applied low VRAM limits for Wan", false),
            (SettingKeys.LocalWanFps, "23.0", "Local", "FPS for SaveWEBM output", false),
            (SettingKeys.LocalWanFrameCount, "73", "Local", "Frame count for Wan video (4n+1 pattern)", false),
            (SettingKeys.LocalComfyInstallRoot, @"D:\ComfyUI", "Local", "Comfy Desktop install folder (auto-find ComfyUI.exe)", false),
            (SettingKeys.LocalComfyExecutablePath, "", "Local", "Optional full path to ComfyUI.exe (overrides InstallRoot)", false),
            (SettingKeys.LocalComfyRestartTimeoutSec, "120", "Local", "Seconds to wait for ComfyUI after auto-restart", false),
            (SettingKeys.LocalFluxSteps, "8", "Local", "FLUX KSampler steps (used when preset=custom)", false),
            (SettingKeys.LocalFluxWidth, "640", "Local", "FLUX output width (9:16, safe on 8GB)", false),
            (SettingKeys.LocalFluxHeight, "1152", "Local", "FLUX output height (9:16, safe on 8GB)", false),
            (SettingKeys.LocalWanSteps, "20", "Local", "Wan TI2V KSampler steps", false),
            (SettingKeys.LocalWanCfg, "4.0", "Local", "Wan TI2V CFG scale (lower = more natural)", false),
            (SettingKeys.LocalWanWidth, "480", "Local", "Wan output width (480-720, default 480)", false),
            (SettingKeys.LocalWanHeight, "720", "Local", "Wan output height (480-720, default 720)", false),
            (SettingKeys.LocalWanNegativePrompt, LocalComfyGenerationSettings.DefaultWanNegativePrompt, "Local", "Wan negative prompt", false),
            (SettingKeys.LocalWanDenoise, "0.6", "Local", "Wan TI2V KSampler denoise (0.6 = identity-preserving motion)", false),
            (SettingKeys.LocalWanUnetName, LocalComfyGenerationSettings.DefaultWanUnetName, "Local", "Wan TI2V GGUF unet filename (models/unet search path)", false),
            (SettingKeys.LocalWanVaeName, LocalComfyGenerationSettings.DefaultWanVaeName, "Local", "Wan VAE filename (models/vae search path)", false),
            (SettingKeys.LocalWanTiledVaeDecode, "true", "Local", "Use wan-ti2v-lowvram.json (auto on 8GB GPUs)", false),
            (SettingKeys.OpenAiApiKey, "", "Api", "OpenAI API Key", true),
            (SettingKeys.OpenAiModel, "gpt-4o-mini", "Api", "OpenAI model ID", false),
            (SettingKeys.GeminiApiKey, "", "Api", "Google Gemini API Key (Free fallback)", true),
            (SettingKeys.GeminiModel, "gemini-2.5-flash-lite", "Api", "Gemini model ID (free tier: gemini-2.5-flash-lite)", false),
            (SettingKeys.ElevenLabsApiKey, "", "Api", "ElevenLabs API Key", true),
            (SettingKeys.HuggingFaceApiKey, "", "Api", "Hugging Face API Key (Inference Providers / FLUX.1-schnell)", true),
            (SettingKeys.HuggingFaceImageModel, "black-forest-labs/FLUX.1-schnell", "Api", "Hugging Face image model ID (prefer free tier model)", false),
            (SettingKeys.HuggingFaceImageEndpoint, "https://router.huggingface.co/hf-inference/models/black-forest-labs/FLUX.1-schnell", "Api", "Hugging Face FLUX.1-schnell inference (free tier)", false),
             (SettingKeys.UnslothBaseUrl, "http://127.0.0.1:8888/v1", "Api", "Unsloth OpenAI-compatible base URL", false),
             (SettingKeys.UnslothApiKey, "", "Api", "Unsloth API Key", true),
             (SettingKeys.UnslothImageModel, "Qwen-Image-2.1", "Api", "Unsloth image model ID", false),

            (SettingKeys.ScriptSystemPrompt, DefaultScriptPrompt, "Prompt", "Legacy combined script prompt (auto-synced)", false),
            (SettingKeys.ScriptSystemPromptInstructions, JsonPromptDefaults.ScriptInstructions, "Prompt", "Script prompt — editable instructions", false),
            (SettingKeys.ScriptSystemPromptJsonSchema, JsonPromptDefaults.ScriptJsonSchema, "Prompt", "Script prompt — JSON response schema", false),
            (SettingKeys.FluxImagePrompt, DefaultFluxPrompt, "Prompt", "Base prompt for FLUX image generation (style/identity)", false),
            (SettingKeys.ScenePlannerPrompt, DefaultScenePlannerPrompt, "Prompt", "Legacy combined scene planner prompt (auto-synced)", false),
            (SettingKeys.ScenePlannerPromptInstructions, JsonPromptDefaults.ScenePlannerInstructions, "Prompt", "Scene planner — how to split script into scenes/clips", false),
            (SettingKeys.ScenePlannerPromptJsonSchema, JsonPromptDefaults.ScenePlannerJsonSchema, "Prompt", "Scene planner — JSON response schema", false),
            (SettingKeys.ScenePlannerUserTemplate, JsonPromptDefaults.ScenePlannerUserTemplateImageToVideo, "Prompt", "Scene planner — user message template (ImageToVideo). Placeholders: {{workflowPathDescription}}, {{targetWordCount}}, {{actualWordCount}}, {{primaryHook}}, {{fullNarrationText}}", false),
            (SettingKeys.ScenePlannerUserTemplateTextToVideo, JsonPromptDefaults.ScenePlannerUserTemplateTextToVideo, "Prompt", "Scene planner — user message template (TextToVideo). Same placeholders as ImageToVideo template.", false),
            (SettingKeys.QwenMediaGuidancePrompt, DefaultQwenMediaGuidance, "Prompt", "Legacy — replaced by Scene Planner prompt", false),
            (SettingKeys.WanVideoPrompt, DefaultWanVideoPrompt, "Prompt", "Global Wan video style (aspect, mood)", false),
            (SettingKeys.WanMotionPositivePrompt, WanMotionPromptDefaults.PositiveRules, "Prompt", "Wan motion — global positive rules appended to every scene prompt", false),
            (SettingKeys.WanMotionNegativePrompt, WanMotionPromptDefaults.NegativeRules, "Prompt", "Wan motion — global negative prompt (ComfyUI)", false),
            (SettingKeys.IdeaGenerationPrompt, DefaultIdeaPrompt, "Prompt", "Legacy combined idea prompt (auto-synced)", false),
            (SettingKeys.IdeaGenerationPromptInstructions, JsonPromptDefaults.IdeaInstructions, "Prompt", "Idea prompt — editable instructions", false),
            (SettingKeys.IdeaGenerationPromptJsonSchema, JsonPromptDefaults.IdeaJsonSchema, "Prompt", "Idea prompt — JSON response schema", false),
            (SettingKeys.DefaultContentLanguage, ContentLanguageCodes.Arabic, "Content", "Default language: ar|en|fr|tr", false),
            (SettingKeys.MaxVideoDurationSec, "600", "Content", "Maximum video duration in seconds (10 minutes)", false),
            (SettingKeys.ProductionDefaultWordCount, "300", "Content", "Default script word count target", false),
            (SettingKeys.ProductionMinWordCount, "75", "Content", "Minimum script word count", false),
            (SettingKeys.ProductionMaxWordCount, "10000", "Content", "Maximum script word count", false),
            (SettingKeys.ProductionMinSegmentsForProduce, "3", "Content", "Minimum scenes/segments for script validation and produce", false),
            (SettingKeys.ProductionDefaultImageSegmentCount, "8", "Content", "Default image/video scene count for script generation", false),
            (SettingKeys.ProductionWordsPerSecond, "2.5", "Content", "Spoken words per second for duration estimate", false),
            (SettingKeys.ImageScenesPrompt, DefaultImageScenesPrompt, "Prompt", "Legacy combined image-scenes prompt (auto-synced)", false),
            (SettingKeys.ImageScenesPromptInstructions, JsonPromptDefaults.ImageScenesInstructions, "Prompt", "Image scenes — how to write per-scene FLUX/Wan prompts", false),
            (SettingKeys.ImageScenesPromptJsonSchema, JsonPromptDefaults.ImageScenesJsonSchema, "Prompt", "Image scenes — JSON response schema", false),
            (SettingKeys.ImageScenesUserTemplate, JsonPromptDefaults.ImageScenesUserTemplateImageToVideo, "Prompt", "Image scenes — user template (ImageToVideo). Placeholders: {{workflowPathDescription}}, {{imageSegmentCount}}, {{targetWordCount}}, {{actualWordCount}}, {{primaryHook}}, {{fullNarrationText}}", false),
            (SettingKeys.ImageScenesUserTemplateTextToVideo, JsonPromptDefaults.ImageScenesUserTemplateTextToVideo, "Prompt", "Image scenes — user template (TextToVideo). Same placeholders as ImageToVideo template.", false),
            (SettingKeys.CaptionFontSize, "48", "FFmpeg", "ASS subtitle font size", false),
            (SettingKeys.SubtitlePrimaryColor, "&H00FFFFFF", "FFmpeg", "Primary subtitle color (ASS format)", false),
            (SettingKeys.SubtitleHighlightColor, "&H0000FFFF", "FFmpeg", "Highlight subtitle color (ASS format)", false),
            (SettingKeys.AudioDuckingLevel, "0.25", "FFmpeg", "Music volume during speech (0-1)", false),
            (SettingKeys.KenBurnsSpeed, "0.0008", "FFmpeg", "Ken Burns zoom speed factor (0.0003-0.02; lower values are auto-boosted)", false),
            (SettingKeys.KenBurnsDirection, "zoom_in", "FFmpeg", "Default motion: zoom_in|zoom_out|pan_right|pan_left|ken_burns|random", false),
            (SettingKeys.ImageMotionAssignment, "auto", "FFmpeg", "Per-image motion: auto (context-based) | manual", false),
            (SettingKeys.TransitionType, "fade", "FFmpeg", "Transition: fade|slide|none", false),
            (SettingKeys.TransitionDurationSec, "0.5", "FFmpeg", "Transition duration in seconds", false),
            (SettingKeys.ImageDurationSec, "5", "FFmpeg", "Default duration per image when not force-syncing", false),
            (SettingKeys.VideoAspectRatio, "9:16", "FFmpeg", "Aspect ratio: 9:16|16:9|1:1", false),
            (SettingKeys.VideoWidth, "480", "FFmpeg", "Output video width", false),
            (SettingKeys.VideoHeight, "832", "FFmpeg", "Output video height", false),
            (SettingKeys.SubtitleFontName, "Arial", "FFmpeg", "ASS subtitle font name", false),
            (SettingKeys.SubtitleBackColor, "&H80000000", "FFmpeg", "Subtitle background color (ASS)", false),
            (SettingKeys.SubtitleOutline, "3", "FFmpeg", "Subtitle outline thickness", false),
            (SettingKeys.SubtitleAlignment, "2", "FFmpeg", "ASS alignment: 8=top 5=center 2=bottom", false),
            (SettingKeys.SubtitlesEnabled, "true", "FFmpeg", "Enable burned-in subtitles", false),
            (SettingKeys.VoiceVolume, "1.0", "FFmpeg", "Voice-over volume multiplier", false),
            (SettingKeys.MusicVolume, "1.0", "FFmpeg", "Background music volume multiplier", false),
            (SettingKeys.BackgroundMusicEnabled, "true", "FFmpeg", "Enable background music when path is set", false),
            (SettingKeys.VideoCrf, "23", "FFmpeg", "H.264 CRF quality (18-28, lower=better)", false),
            (SettingKeys.QuickPreviewMode, "false", "FFmpeg", "Fast low-quality preview render", false),
            (SettingKeys.BackgroundMusicPath, "", "FFmpeg", "Path to background music file", false),
            (SettingKeys.FfmpegPath, "ffmpeg", "FFmpeg", "FFmpeg executable path", false),
            (SettingKeys.OutputDirectory, "output", "FFmpeg", "Video output directory", false),
            (SettingKeys.ForceSyncDuration, "true", "FFmpeg", "Legacy: use FFmpeg.SyncMode instead", false),
            (SettingKeys.SyncMode, "audio_master", "FFmpeg", "Sync mode: audio_master|media_master|shortest", false),
            (SettingKeys.MediaShortfallAction, "freeze_last", "FFmpeg", "When media shorter than audio: stretch|loop_sequence|loop_last|freeze_last", false),
            (SettingKeys.WanMaxStretchFactor, "1.12", "FFmpeg", "Max Wan clip time-stretch before freeze_last (1.12 = 12%)", false),
            (SettingKeys.MediaOverflowAction, "trim", "FFmpeg", "When media longer than audio: trim|keep", false),
            (SettingKeys.ImageTimingMode, "equal_split", "FFmpeg", "Image timing: equal_split|fixed_duration", false),
            (SettingKeys.SubtitleSyncMode, "scale_to_audio", "FFmpeg", "Subtitle sync: scale_to_audio|native|scale_to_video", false),
            (SettingKeys.SubtitleOffsetMs, "0", "FFmpeg", "Subtitle offset in milliseconds (+delay, -advance)", false),
            (SettingKeys.OutputVideoFps, "23", "FFmpeg", "Output video frame rate (FPS)", false),
            (SettingKeys.ElevenLabsVoiceId, "21m00Tcm4TlvDq8ikWAM", "Voice", "ElevenLabs Voice ID", false),
            (SettingKeys.EdgeTtsVoice, "ar-EG-ShakirNeural", "Voice", "Edge TTS voice (free fallback)", false),
        ];
    }

    private static IEnumerable<ContentCategory> GetAllCategoryDefinitions() =>
    [
        new() { CategoryType = ContentCategoryType.Sports, NameEn = "Sports", NameAr = "رياضة", Description = "Athletics, football, fitness, sports news" },
        new() { CategoryType = ContentCategoryType.History, NameEn = "History", NameAr = "تاريخ", Description = "Historical events, figures, civilizations" },
        new() { CategoryType = ContentCategoryType.Geography, NameEn = "Geography", NameAr = "جغرافيا", Description = "Countries, maps, cultures, landmarks" },
        new() { CategoryType = ContentCategoryType.Science, NameEn = "Science", NameAr = "علوم", Description = "Physics, chemistry, biology, space" },
        new() { CategoryType = ContentCategoryType.MedicineHealth, NameEn = "Medicine & Health", NameAr = "طب وصحة", Description = "Health tips, medical facts, wellness" },
        new() { CategoryType = ContentCategoryType.GeneralCulture, NameEn = "General Culture", NameAr = "ثقافة عامة", Description = "General knowledge, trivia, culture" },
        new() { CategoryType = ContentCategoryType.Technology, NameEn = "Technology", NameAr = "تكنولوجيا", Description = "Tech news, gadgets, AI, software" },
        new() { CategoryType = ContentCategoryType.Entertainment, NameEn = "Entertainment", NameAr = "ترفيه", Description = "Movies, music, celebrities, pop culture" },
        new() { CategoryType = ContentCategoryType.FoodCooking, NameEn = "Food & Cooking", NameAr = "طعام وطبخ", Description = "Recipes, food culture, nutrition" },
        new() { CategoryType = ContentCategoryType.Travel, NameEn = "Travel", NameAr = "سفر", Description = "Destinations, travel tips, tourism" },
        new() { CategoryType = ContentCategoryType.Education, NameEn = "Education", NameAr = "تعليم", Description = "Learning, tutorials, academic topics" },
        new() { CategoryType = ContentCategoryType.Business, NameEn = "Business", NameAr = "أعمال", Description = "Entrepreneurship, marketing, startups" },
        new() { CategoryType = ContentCategoryType.Psychology, NameEn = "Psychology", NameAr = "علم نفس", Description = "Behavior, mental health, self-improvement" },
        new() { CategoryType = ContentCategoryType.Nature, NameEn = "Nature", NameAr = "طبيعة", Description = "Wildlife, environment, ecology" },
        new() { CategoryType = ContentCategoryType.ArtDesign, NameEn = "Art & Design", NameAr = "فن وتصميم", Description = "Visual arts, design, creativity" },
        new() { CategoryType = ContentCategoryType.Finance, NameEn = "Finance", NameAr = "مالية", Description = "Personal finance, investing, economics" },
        new() { CategoryType = ContentCategoryType.Politics, NameEn = "Politics", NameAr = "سياسة", Description = "Political analysis, current affairs" },
        new() { CategoryType = ContentCategoryType.Religion, NameEn = "Religion", NameAr = "دين", Description = "Religious topics, spirituality" },
        new() { CategoryType = ContentCategoryType.Lifestyle, NameEn = "Lifestyle", NameAr = "أسلوب حياة", Description = "Daily life, habits, productivity" },
        new() { CategoryType = ContentCategoryType.Motivation, NameEn = "Motivation", NameAr = "تحفيز", Description = "Motivation, success stories, mindset" },
    ];

    private static async Task CleanupLegacyApprovalAndTelegramAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        await db.VideoProjects
            .Where(p => p.Status == (VideoProjectStatus)4 || p.Status == (VideoProjectStatus)5)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, VideoProjectStatus.ScriptGenerated), ct);

        await db.VideoProjects
            .Where(p => p.Status == (VideoProjectStatus)8)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, VideoProjectStatus.Failed), ct);

        await db.SystemSettings
            .Where(s => EF.Functions.Like(s.Key, "%Telegram%") || EF.Functions.Like(s.Key, "%telegram%"))
            .ExecuteDeleteAsync(ct);

        try
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM ApprovalLogs", ct);
        }
        catch
        {
            // Table may already be dropped after migration.
        }
    }

    private const string LegacyContentMigratedKey = "System.LegacyContentMigrated";

    private static async Task MigrateLegacyContentAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        if (await db.SystemSettings.AnyAsync(s => s.Key == LegacyContentMigratedKey, ct))
            return;

        var legacyFinancial = new[]
        {
            ContentCategoryType.AcademicBooks,
            ContentCategoryType.DailyFinancialProblems,
            ContentCategoryType.Macroeconomics,
            ContentCategoryType.InvestmentWorld,
            ContentCategoryType.FinancialPsychology,
            ContentCategoryType.EgyptianContext,
        };

        await db.ContentCategories
            .Where(c => legacyFinancial.Contains(c.CategoryType))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false), ct);

        var legacyMarkers = new[]
        {
            "مالي", "Egyptian urban", "Egyptian Context", "Egyptian Financial",
            "عامية مصرية", "Shorts/TikTok مالية", "financial illustration",
            "Academic Books", "Daily Problems: Debt", "Macroeconomics:",
            "Financial Psychology", "Investment: Stocks", "كتب وأبحاث",
        };

        var promptKeys = new[]
        {
            SettingKeys.ScriptSystemPrompt,
            SettingKeys.FluxImagePrompt,
            SettingKeys.IdeaGenerationPrompt,
        };

        foreach (var key in promptKeys)
        {
            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (setting is null) continue;

            var isLegacy = legacyMarkers.Any(m =>
                setting.Value.Contains(m, StringComparison.OrdinalIgnoreCase));
            if (isLegacy)
            {
                setting.Value = key switch
                {
                    SettingKeys.ScriptSystemPrompt => DefaultScriptPrompt,
                    SettingKeys.FluxImagePrompt => DefaultFluxPrompt,
                    SettingKeys.IdeaGenerationPrompt => DefaultIdeaPrompt,
                    _ => setting.Value
                };

                if (key == SettingKeys.ScriptSystemPrompt)
                    await UpsertSplitJsonPromptAsync(db, SettingKeys.ScriptSystemPromptInstructions,
                        SettingKeys.ScriptSystemPromptJsonSchema,
                        JsonPromptDefaults.ScriptInstructions, JsonPromptDefaults.ScriptJsonSchema, ct);
                else if (key == SettingKeys.IdeaGenerationPrompt)
                    await UpsertSplitJsonPromptAsync(db, SettingKeys.IdeaGenerationPromptInstructions,
                        SettingKeys.IdeaGenerationPromptJsonSchema,
                        JsonPromptDefaults.IdeaInstructions, JsonPromptDefaults.IdeaJsonSchema, ct);
            }
        }

        if (!await db.SystemSettings.AnyAsync(s => s.Key == LegacyContentMigratedKey, ct))
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Key = LegacyContentMigratedKey,
                Value = "1",
                Category = "System",
                Description = "Legacy content migration completed"
            });
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task MigrateSplitJsonPromptsAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        await EnsureSplitJsonPromptAsync(
            db,
            SettingKeys.ScriptSystemPrompt,
            SettingKeys.ScriptSystemPromptInstructions,
            SettingKeys.ScriptSystemPromptJsonSchema,
            JsonPromptDefaults.ScriptInstructions,
            JsonPromptDefaults.ScriptJsonSchema,
            ct);

        await EnsureSplitJsonPromptAsync(
            db,
            SettingKeys.IdeaGenerationPrompt,
            SettingKeys.IdeaGenerationPromptInstructions,
            SettingKeys.IdeaGenerationPromptJsonSchema,
            JsonPromptDefaults.IdeaInstructions,
            JsonPromptDefaults.IdeaJsonSchema,
            ct);

        await EnsureSplitJsonPromptAsync(
            db,
            SettingKeys.ScenePlannerPrompt,
            SettingKeys.ScenePlannerPromptInstructions,
            SettingKeys.ScenePlannerPromptJsonSchema,
            JsonPromptDefaults.ScenePlannerInstructions,
            JsonPromptDefaults.ScenePlannerJsonSchema,
            ct);

        await EnsureSplitJsonPromptAsync(
            db,
            SettingKeys.ImageScenesPrompt,
            SettingKeys.ImageScenesPromptInstructions,
            SettingKeys.ImageScenesPromptJsonSchema,
            JsonPromptDefaults.ImageScenesInstructions,
            JsonPromptDefaults.ImageScenesJsonSchema,
            ct);

        await db.SaveChangesAsync(ct);
    }

    private const string WanMotionPromptMigrationKey = "System.WanMotionPromptMigration";

    private static async Task MigrateWanMotionPromptContentAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        if (await db.SystemSettings.AnyAsync(s => s.Key == WanMotionPromptMigrationKey, ct))
            return;

        const string legacyMarker = "userMotionPrompt stays empty";
        const string legacyTaskMarker = "Leave userMotionPrompt as \"\"";

        foreach (var key in new[]
                 {
                     SettingKeys.ImageScenesPromptInstructions,
                     SettingKeys.ImageScenesUserTemplate,
                     SettingKeys.ImageScenesUserTemplateTextToVideo,
                 })
        {
            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (setting is null || string.IsNullOrWhiteSpace(setting.Value))
                continue;

            if (!setting.Value.Contains(legacyMarker, StringComparison.OrdinalIgnoreCase)
                && !setting.Value.Contains(legacyTaskMarker, StringComparison.OrdinalIgnoreCase))
                continue;

            setting.Value = key switch
            {
                var k when k == SettingKeys.ImageScenesPromptInstructions
                    => JsonPromptDefaults.ImageScenesInstructions,
                var k when k == SettingKeys.ImageScenesUserTemplate
                    => JsonPromptDefaults.ImageScenesUserTemplateImageToVideo,
                var k when k == SettingKeys.ImageScenesUserTemplateTextToVideo
                    => JsonPromptDefaults.ImageScenesUserTemplateTextToVideo,
                _ => setting.Value
            };
        }

        var combined = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == SettingKeys.ImageScenesPrompt, ct);
        if (combined is not null)
        {
            var instr = await db.SystemSettings.FirstOrDefaultAsync(
                s => s.Key == SettingKeys.ImageScenesPromptInstructions, ct);
            var schema = await db.SystemSettings.FirstOrDefaultAsync(
                s => s.Key == SettingKeys.ImageScenesPromptJsonSchema, ct);
            if (instr is not null && schema is not null)
                combined.Value = JsonPromptComposer.Compose(instr.Value, schema.Value);
        }

        db.SystemSettings.Add(new SystemSetting
        {
            Key = WanMotionPromptMigrationKey,
            Value = "1",
            Category = "System",
            Description = "Wan motion prompt settings migrated"
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureSplitJsonPromptAsync(
        ApplicationDbContext db,
        string legacyKey,
        string instructionsKey,
        string jsonSchemaKey,
        string defaultInstructions,
        string defaultJsonSchema,
        CancellationToken ct = default)
    {
        var existingKeys = await db.SystemSettings
            .Where(s => s.Key == instructionsKey || s.Key == jsonSchemaKey || s.Key == legacyKey)
            .ToDictionaryAsync(s => s.Key, s => s, ct);

        if (existingKeys.ContainsKey(instructionsKey) && existingKeys.ContainsKey(jsonSchemaKey))
            return;

        var legacy = existingKeys.TryGetValue(legacyKey, out var legacySetting)
            ? legacySetting.Value
            : JsonPromptComposer.Compose(defaultInstructions, defaultJsonSchema);

        var (instructions, jsonSchema) = JsonPromptComposer.Split(legacy);
        if (string.IsNullOrWhiteSpace(instructions))
            instructions = defaultInstructions;
        if (string.IsNullOrWhiteSpace(jsonSchema))
            jsonSchema = defaultJsonSchema;

        await UpsertSplitJsonPromptAsync(db, instructionsKey, jsonSchemaKey, instructions, jsonSchema, ct);

        if (existingKeys.TryGetValue(legacyKey, out var combined))
            combined.Value = JsonPromptComposer.Compose(instructions, jsonSchema);
    }

    private static async Task UpsertSplitJsonPromptAsync(
        ApplicationDbContext db,
        string instructionsKey,
        string jsonSchemaKey,
        string instructions,
        string jsonSchema,
        CancellationToken ct = default)
    {
        var instrSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == instructionsKey, ct);
        if (instrSetting is null)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Key = instructionsKey,
                Value = instructions,
                Category = "Prompt",
                Description = "Editable prompt instructions",
                IsSecret = false
            });
        }
        else
            instrSetting.Value = instructions;

        var schemaSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == jsonSchemaKey, ct);
        if (schemaSetting is null)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Key = jsonSchemaKey,
                Value = jsonSchema,
                Category = "Prompt",
                Description = "JSON response schema",
                IsSecret = false
            });
        }
        else
            schemaSetting.Value = jsonSchema;
    }

    private static async Task ApplySchemaPatchesAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        const string sql = """
            IF COL_LENGTH('GeneratedImages', 'VisualPartIndex') IS NULL
                ALTER TABLE GeneratedImages ADD VisualPartIndex int NOT NULL CONSTRAINT DF_GeneratedImages_VisualPartIndex DEFAULT 0;
            IF COL_LENGTH('GeneratedImages', 'AnimationMode') IS NULL
                ALTER TABLE GeneratedImages ADD AnimationMode nvarchar(32) NOT NULL CONSTRAINT DF_GeneratedImages_AnimationMode DEFAULT 'zoom_in';
            IF COL_LENGTH('GeneratedImages', 'MotionSpeed') IS NULL
                ALTER TABLE GeneratedImages ADD MotionSpeed float NOT NULL CONSTRAINT DF_GeneratedImages_MotionSpeed DEFAULT 0.002;
            IF COL_LENGTH('GeneratedImages', 'MaxZoom') IS NULL
                ALTER TABLE GeneratedImages ADD MaxZoom float NOT NULL CONSTRAINT DF_GeneratedImages_MaxZoom DEFAULT 1.3;
            IF COL_LENGTH('GeneratedImages', 'ScenePrompt') IS NULL
                ALTER TABLE GeneratedImages ADD ScenePrompt nvarchar(max) NOT NULL CONSTRAINT DF_GeneratedImages_ScenePrompt DEFAULT '';
            IF COL_LENGTH('VideoProjects', 'ContentLanguage') IS NULL
                ALTER TABLE VideoProjects ADD ContentLanguage nvarchar(8) NOT NULL CONSTRAINT DF_VideoProjects_ContentLanguage DEFAULT 'ar';
            IF COL_LENGTH('VideoProjects', 'VideoProviderUsed') IS NULL
                ALTER TABLE VideoProjects ADD VideoProviderUsed nvarchar(64) NULL;
            IF COL_LENGTH('VideoProjects', 'ProductionWorkflowPath') IS NULL
                ALTER TABLE VideoProjects ADD ProductionWorkflowPath nvarchar(32) NOT NULL CONSTRAINT DF_VideoProjects_ProductionWorkflowPath DEFAULT 'ImageToVideo';
            IF COL_LENGTH('VideoProjects', 'TargetDurationSec') IS NULL
                ALTER TABLE VideoProjects ADD TargetDurationSec int NOT NULL CONSTRAINT DF_VideoProjects_TargetDurationSec DEFAULT 120;
            IF COL_LENGTH('VideoProjects', 'ImagesPer15Sec') IS NULL
                ALTER TABLE VideoProjects ADD ImagesPer15Sec int NOT NULL CONSTRAINT DF_VideoProjects_ImagesPer15Sec DEFAULT 1;
            IF COL_LENGTH('VideoProjects', 'VideosPer15Sec') IS NULL
                ALTER TABLE VideoProjects ADD VideosPer15Sec int NOT NULL CONSTRAINT DF_VideoProjects_VideosPer15Sec DEFAULT 1;
            IF COL_LENGTH('VideoProjects', 'TargetWordCount') IS NULL
                ALTER TABLE VideoProjects ADD TargetWordCount int NOT NULL CONSTRAINT DF_VideoProjects_TargetWordCount DEFAULT 300;
            IF COL_LENGTH('VideoProjects', 'ImageSegmentCount') IS NULL
                ALTER TABLE VideoProjects ADD ImageSegmentCount int NOT NULL CONSTRAINT DF_VideoProjects_ImageSegmentCount DEFAULT 8;
            IF COL_LENGTH('VideoProjects', 'VideoSegmentCount') IS NULL
                ALTER TABLE VideoProjects ADD VideoSegmentCount int NOT NULL CONSTRAINT DF_VideoProjects_VideoSegmentCount DEFAULT 8;
            """;

        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
        catch
        {
            // Best-effort schema patch for existing databases.
        }
    }

    private static async Task TryAddSqliteColumnAsync(
        ApplicationDbContext db, string table, string columnDefinition, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {columnDefinition}", ct);
        }
        catch
        {
            // Column may already exist.
        }
    }
}
