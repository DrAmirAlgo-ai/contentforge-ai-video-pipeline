using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ContentEmpireSystem.Infrastructure.Services.Local;

/// <summary>
/// Serializes all local GPU workloads (Qwen → FLUX → Wan).
/// Keeps ComfyUI-GGUF models loaded within a batch; only frees VRAM on engine switches with safe soft /free.
/// </summary>
public sealed class LocalGpuOrchestrator(
    IServiceScopeFactory scopeFactory,
    ComfyUiRestartState restartState) : ILocalGpuOrchestrator
{
    private readonly SemaphoreSlim _gpu = new(1, 1);
    private int _waiting;
    private GpuHolder? _holder;
    private string? _lastCompletedWorkload;

    private static bool IsFluxWorkload(string? label) =>
        label is not null
        && label.Contains("FLUX", StringComparison.OrdinalIgnoreCase)
        && !label.Contains("Wan", StringComparison.OrdinalIgnoreCase);

    private static bool IsWanWorkload(string? label) =>
        label is not null && label.Contains("Wan", StringComparison.OrdinalIgnoreCase);

    private static bool IsComfyWorkload(string? label) =>
        label is not null
        && (label.Contains("FLUX", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Wan", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Comfy", StringComparison.OrdinalIgnoreCase));

    private static bool IsLocalLlmWorkload(string? label) =>
        label is not null
        && (label.Contains("Qwen", StringComparison.OrdinalIgnoreCase)
            || label.Contains("LM Studio", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Ollama", StringComparison.OrdinalIgnoreCase));

    public GpuStatusDto GetStatus()
    {
        var holder = Volatile.Read(ref _holder);
        var waiting = Volatile.Read(ref _waiting);
        var busy = holder is not null;
        var elapsed = busy ? (int)(DateTime.UtcNow - holder!.Since).TotalSeconds : 0;

        return new GpuStatusDto
        {
            IsBusy = busy,
            WaitQueueCount = waiting,
            BusyElapsedSec = elapsed,
            CurrentWorkload = holder?.Label,
            EstimatedWaitSec = GpuStatusEstimator.EstimateWaitSeconds(waiting, busy, elapsed, holder?.Label)
        };
    }

    public async Task<IAsyncDisposable> AcquireGpuAsync(string? workloadLabel = null, CancellationToken ct = default)
    {
        var label = workloadLabel ?? "GPU";
        Interlocked.Increment(ref _waiting);
        var acquired = false;
        try
        {
            const int maxWaitSeconds = 900;
            if (!await _gpu.WaitAsync(TimeSpan.FromSeconds(maxWaitSeconds), ct))
                throw new InvalidOperationException(
                    "الـ GPU مشغول بعملية أخرى منذ أكثر من 15 دقيقة. انتظر أو أعد تشغيل الـ API.");

            acquired = true;
            Interlocked.Decrement(ref _waiting);

            await using (var prepScope = scopeFactory.CreateAsyncScope())
            {
                var lm = prepScope.ServiceProvider.GetRequiredService<LmStudioGpuService>();
                var comfy = prepScope.ServiceProvider.GetRequiredService<ComfyUiClient>();

                if (IsComfyWorkload(label))
                {
                    await lm.UnloadAllLoadedModelsAsync(ct);
                    var last = Volatile.Read(ref _lastCompletedWorkload);
                    var wanAfterFlux = IsWanWorkload(label) && IsFluxWorkload(last);
                    var recentlyRestarted = wanAfterFlux
                        && restartState.WasRecentlyRestartedAfterFlux(
                            TimeSpan.FromMinutes(ComfyVramProfiles.FluxRestartGraceMinutes));

                    if (!recentlyRestarted)
                        await lm.WaitForGpuReleaseAsync(minimumFreeMiB: 2000, maxWaitSeconds: 30, ct);
                }

                if (IsComfyWorkload(label) || IsComfyWorkload(Volatile.Read(ref _lastCompletedWorkload)))
                {
                    var last = Volatile.Read(ref _lastCompletedWorkload);
                    var fluxContinuation = IsFluxWorkload(label) && IsFluxWorkload(last);
                    var engineSwitch = IsFluxWorkload(last) && IsWanWorkload(label);
                    var recentlyRestarted = engineSwitch
                        && restartState.WasRecentlyRestartedAfterFlux(
                            TimeSpan.FromMinutes(ComfyVramProfiles.FluxRestartGraceMinutes));

                    if (!fluxContinuation)
                    {
                        if (recentlyRestarted)
                        {
                            await comfy.EnsureAvailableAsync(ct);
                            await comfy.WaitUntilQueueIdleAsync(ct);
                        }
                        else
                        {
                            var deepClean = IsWanWorkload(label);
                            await comfy.PrepareGpuForWorkloadAsync(
                                deepClean,
                                skipVramFree: false,
                                engineSwitch: engineSwitch,
                                ct);
                        }
                    }
                }
            }

            var previous = Volatile.Read(ref _lastCompletedWorkload);
            if (!string.IsNullOrWhiteSpace(previous)
                && !string.Equals(previous, label, StringComparison.OrdinalIgnoreCase))
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var settings = scope.ServiceProvider.GetRequiredService<ISystemSettingsService>();
                var cooldownSec = await ParseCooldownSecondsAsync(settings, ct);
                if (cooldownSec > 0)
                    await Task.Delay(TimeSpan.FromSeconds(cooldownSec), ct);
            }

            Volatile.Write(ref _holder, new GpuHolder(label, DateTime.UtcNow));
            return new GpuLease(_gpu, label, ReleaseGpuAsync);
        }
        catch
        {
            if (acquired)
                _gpu.Release();
            else
                Interlocked.Decrement(ref _waiting);
            throw;
        }
    }

    private async Task ReleaseGpuAsync(string label)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            if (IsLocalLlmWorkload(label))
            {
                var lm = scope.ServiceProvider.GetRequiredService<LmStudioGpuService>();
                await lm.UnloadAllLoadedModelsAsync();
            }

            if (IsComfyWorkload(label))
            {
                var comfy = scope.ServiceProvider.GetRequiredService<ComfyUiClient>();
                var fluxJob = label.Contains("FLUX", StringComparison.OrdinalIgnoreCase)
                    && !label.Contains("Wan", StringComparison.OrdinalIgnoreCase);
                await comfy.ReleaseGpuAfterWorkloadAsync(fluxImageJob: fluxJob);
            }
        }
        catch
        {
            // Best-effort VRAM release.
        }
        finally
        {
            Volatile.Write(ref _holder, null);
            Volatile.Write(ref _lastCompletedWorkload, label);
        }
    }

    private sealed record GpuHolder(string Label, DateTime Since);

    private sealed class GpuLease(
        SemaphoreSlim gate,
        string label,
        Func<string, Task> onReleaseAsync) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            await onReleaseAsync(label);
            gate.Release();
        }
    }

    private static async Task<int> ParseCooldownSecondsAsync(ISystemSettingsService settings, CancellationToken ct)
    {
        var raw = await settings.GetOrDefaultAsync(SettingKeys.LocalGpuCooldownSeconds, "4", ct);
        return int.TryParse(raw, out var sec) ? Math.Clamp(sec, 0, 120) : 3;
    }
}
