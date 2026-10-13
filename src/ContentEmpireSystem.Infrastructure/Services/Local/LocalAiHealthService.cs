using ContentEmpireSystem.Core;

using ContentEmpireSystem.Core.DTOs;

using ContentEmpireSystem.Core.Enums;

using ContentEmpireSystem.Core.Interfaces;

using ContentEmpireSystem.Infrastructure.Services.Ai;



namespace ContentEmpireSystem.Infrastructure.Services.Local;



public sealed class LocalAiHealthService(

    LocalEndpointResolver endpoints,

    ComfyUiClient comfyUi,

    ILocalQwenConnector qwenConnector,

    ISystemSettingsService settings) : ILocalAiHealthService

{

    public async Task<LocalAiHealthDto> CheckAsync(CancellationToken ct = default)

    {

        var profile = await DeploymentProfileSource.ResolveAsync(settings, ct);

        var configuredScript = await settings.GetOrDefaultAsync(SettingKeys.ScriptProvider, ScriptProviderNames.LmStudio, ct);

        var configuredImage = await settings.GetOrDefaultAsync(SettingKeys.ImageProvider, ImageProviderNames.ComfyFlux, ct);

        var configuredVideo = await settings.GetOrDefaultAsync(SettingKeys.VideoProvider, "WanTI2V", ct);

        var normalizedScript = ScriptProviderNames.Normalize(configuredScript);

        var explicitScript = normalizedScript is ScriptProviderNames.Gemini or ScriptProviderNames.OpenAi

            or ScriptProviderNames.LmStudio or ScriptProviderNames.Ollama

            ? normalizedScript!

            : null;

        var effectiveScript = explicitScript ?? DeploymentProfileResolver.ResolveScriptProvider(profile, configuredScript);

        var effectiveImage = DeploymentProfileResolver.ResolveImageProvider(profile, configuredImage);



        var lmUrl = await endpoints.ResolveLmStudioBaseUrlAsync(ct);

        var comfyUrl = await endpoints.ResolveComfyUiBaseUrlAsync(ct);



        var geminiConfigured = await settings.HasValueAsync(SettingKeys.GeminiApiKey, ct);

        var openAiConfigured = await settings.HasValueAsync(SettingKeys.OpenAiApiKey, ct);

        var hfConfigured = await settings.HasValueAsync(SettingKeys.HuggingFaceApiKey, ct);



        var lmOk = false;
        string? qwenSource = null;

        var scriptUsesLocalLlm = effectiveScript is ScriptProviderNames.LmStudio
            or ScriptProviderNames.Ollama or ScriptProviderNames.Auto
            || (!DeploymentProfileResolver.UsesCloudApis(profile)
                && effectiveScript is not (ScriptProviderNames.Gemini or ScriptProviderNames.OpenAi));

        if (scriptUsesLocalLlm)
        {
            try
            {
                var live = qwenConnector.GetCached() ?? await qwenConnector.ResolveLiveAsync(ct);
                lmOk = true;
                effectiveScript = live.Provider;
                qwenSource = live.SourceLabel;
            }
            catch
            {
                lmOk = false;
            }
        }

        var comfyOk = await comfyUi.PingAsync(ct);



        var scriptReady = effectiveScript switch

        {

            ScriptProviderNames.Gemini => geminiConfigured,

            ScriptProviderNames.OpenAi => openAiConfigured,

            ScriptProviderNames.LmStudio or ScriptProviderNames.Ollama => lmOk,

            _ => DeploymentProfileResolver.UsesCloudApis(profile) ? geminiConfigured : lmOk

        };

        var imageReady = DeploymentProfileResolver.UsesCloudApis(profile)

            ? hfConfigured

            : comfyOk;

        var videoSupported = DeploymentProfileResolver.SupportsVideoGeneration(profile);

        var videoReady = videoSupported && comfyOk;



        string? message = null;



        if (effectiveScript is ScriptProviderNames.Gemini && !geminiConfigured)

            message = Append(message, "اخترت Gemini للنص — أضف مفتاح Gemini من الإعدادات.");

        else if (effectiveScript is ScriptProviderNames.OpenAi && !openAiConfigured)

            message = Append(message, "اخترت OpenAI للنص — أضف مفتاح OpenAI من الإعدادات.");

        else if (scriptUsesLocalLlm && !lmOk)

            message = Append(message, "Qwen غير متصل — السيستم يحاول LM Studio ثم Ollama تلقائياً. ثبّت Ollama أو شغّل LM Studio.");
        else if (scriptUsesLocalLlm && lmOk && qwenSource is not null)

            message = Append(message, $"Qwen متصل عبر {qwenSource}.");



        if (DeploymentProfileResolver.UsesCloudApis(profile))

        {

            if (!hfConfigured)

                message = Append(message, "وضع أونلاين: أضف مفتاح Hugging Face لتوليد الصور.");

            message = Append(message, "وضع أونلاين: لا توليد Wan — استخدم صور ثابتة + Ken Burns ثم Produce.");

        }

        else if (!comfyOk)

        {

            message = Append(message, "ComfyUI غير متاح — شغّل ComfyUI Desktop وثبّت مسارات الـ workflows.");

        }



        return new LocalAiHealthDto

        {

            DeploymentProfile = profile,

            EndpointMode = DeploymentProfileResolver.UsesCloudApis(profile) ? "Cloud" : "Local",

            EffectiveScriptProvider = effectiveScript,

            EffectiveImageProvider = effectiveImage,

            EffectiveVideoProvider = configuredVideo,

            LmStudioReachable = lmOk,

            ComfyUiReachable = comfyOk,

            GeminiConfigured = geminiConfigured,

            HuggingFaceConfigured = hfConfigured,

            ScriptProviderReady = scriptReady,

            ImageProviderReady = imageReady,

            VideoProviderReady = videoReady,

            VideoGenerationSupported = videoSupported,

            OverallReady = scriptReady && imageReady,

            LmStudioUrl = lmUrl,

            ComfyUiUrl = comfyUrl,

            Message = message

        };

    }



    private static string Append(string? current, string next) =>

        string.IsNullOrWhiteSpace(current) ? next : current + " " + next;

}

