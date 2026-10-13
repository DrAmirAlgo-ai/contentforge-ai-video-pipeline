using System.Diagnostics;
using System.Text.RegularExpressions;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;

namespace ContentEmpireSystem.Infrastructure.Services.Local;

public sealed class ComfyUiProcessManager(
    ISystemSettingsService settings,
    LocalEndpointResolver endpoints,
    ComfyUiClient comfyUi,
    ComfyUiRestartState restartState) : IComfyUiProcessManager
{
    private static readonly TimeSpan DefaultGraceWindow = TimeSpan.FromMinutes(ComfyVramProfiles.FluxRestartGraceMinutes);

    public bool WasRecentlyRestartedAfterFlux(TimeSpan graceWindow) =>
        restartState.WasRecentlyRestartedAfterFlux(graceWindow);

    public async Task RestartAfterFluxBatchAsync(CancellationToken ct = default)
    {
        if (!await IsAutoRestartEnabledAsync(ct))
            return;

        await RestartComfyUiAsync(ct);
        restartState.MarkFluxBatchRestart();
    }

    public async Task EnsureReadyForWanAsync(CancellationToken ct = default)
    {
        if (!await IsAutoRestartEnabledAsync(ct))
            return;

        if (restartState.WasRecentlyRestartedAfterFlux(DefaultGraceWindow))
            return;

        await RestartComfyUiAsync(ct);
        restartState.MarkFluxBatchRestart();
    }

    private async Task<bool> IsAutoRestartEnabledAsync(CancellationToken ct)
    {
        var raw = await settings.GetOrDefaultAsync(SettingKeys.LocalComfyAutoRestartAfterFlux, "true", ct);
        return !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RestartComfyUiAsync(CancellationToken ct)
    {
        var baseUrl = await endpoints.ResolveComfyUiBaseUrlAsync(ct);
        var port = ParsePortFromBaseUrl(baseUrl);

        if (await comfyUi.PingAsync(ct))
        {
            try
            {
                await comfyUi.WaitUntilQueueIdleAsync(ct);
            }
            catch
            {
                // Proceed with restart even if queue wait fails.
            }
        }

        await StopListenerOnPortAsync(port, ct);
        await Task.Delay(2500, ct);

        await StartComfyUiAsync(port, ct);
        await WaitUntilHealthyAsync(baseUrl, ct);
    }

    private async Task WaitUntilHealthyAsync(string baseUrl, CancellationToken ct)
    {
        var timeoutSec = await ParseRestartTimeoutSecAsync(ct);
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (await comfyUi.PingAsync(ct))
                return;

            await Task.Delay(2000, ct);
        }

        throw new InvalidOperationException(
            $"ComfyUI لم يعد متاحاً بعد إعادة التشغيل خلال {timeoutSec} ثانية. " +
            "تأكد من مسار Comfy Desktop في الإعدادات (Local.ComfyUI.ExecutablePath) ثم حاول مرة أخرى.");
    }

    private async Task StartComfyUiAsync(int port, CancellationToken ct)
    {
        var launch = await ResolveLaunchAsync(port, ct)
            ?? throw new InvalidOperationException(
                "لم يُعثر على ComfyUI لتشغيله. عيّن Local.ComfyUI.ExecutablePath أو Local.ComfyUI.InstallRoot في الإعدادات.");

        if (launch.UsePython)
        {
            var psi = new ProcessStartInfo
            {
                FileName = launch.Executable,
                Arguments =
                    $"\"{launch.Script}\" --listen 127.0.0.1 --port {port} {ComfyVramProfiles.ComfyStabilityLaunchArgs}",
                WorkingDirectory = launch.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (Process.Start(psi) is null)
                throw new InvalidOperationException($"تعذّر تشغيل ComfyUI: {launch.Executable}");
            return;
        }

        var desktopPsi = new ProcessStartInfo
        {
            FileName = launch.Executable,
            WorkingDirectory = launch.WorkingDirectory,
            UseShellExecute = true
        };
        if (Process.Start(desktopPsi) is null)
            throw new InvalidOperationException($"تعذّر تشغيل Comfy Desktop: {launch.Executable}");
    }

    private async Task<LaunchSpec?> ResolveLaunchAsync(int port, CancellationToken ct)
    {
        _ = port;

        var configuredExe = (await settings.GetOrDefaultAsync(SettingKeys.LocalComfyExecutablePath, "", ct)).Trim();
        if (!string.IsNullOrWhiteSpace(configuredExe) && File.Exists(configuredExe))
        {
            return new LaunchSpec(
                configuredExe,
                Path.GetDirectoryName(configuredExe) ?? "",
                UsePython: false,
                Script: null);
        }

        var installRoot = (await settings.GetOrDefaultAsync(SettingKeys.LocalComfyInstallRoot, @"D:\ComfyUI", ct)).Trim();
        if (string.IsNullOrWhiteSpace(installRoot))
            installRoot = @"D:\ComfyUI";

        foreach (var exe in BuildExeCandidates(installRoot))
        {
            if (File.Exists(exe))
            {
                return new LaunchSpec(
                    exe,
                    Path.GetDirectoryName(exe) ?? installRoot,
                    UsePython: false,
                    Script: null);
            }
        }

        var comfyRoot = Path.Combine(installRoot, "ComfyUI");
        var python = Path.Combine(installRoot, "standalone-env", "python.exe");
        var mainPy = Path.Combine(comfyRoot, "main.py");
        if (File.Exists(python) && File.Exists(mainPy))
        {
            return new LaunchSpec(
                python,
                comfyRoot,
                UsePython: true,
                Script: mainPy);
        }

        return null;
    }

    private static IEnumerable<string> BuildExeCandidates(string installRoot)
    {
        yield return Path.Combine(installRoot, "ComfyUI.exe");
        yield return Path.Combine(installRoot, "ComfyUI Desktop.exe");

        var localPrograms = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localPrograms, "Programs", "ComfyUI", "ComfyUI.exe");
        yield return Path.Combine(localPrograms, "ComfyUI", "ComfyUI.exe");
    }

    private static async Task StopListenerOnPortAsync(int port, CancellationToken ct)
    {
        foreach (var pid in GetListeningPids(port))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var proc = Process.GetProcessById(pid);
                proc.Kill(entireProcessTree: true);
                await proc.WaitForExitAsync(ct);
            }
            catch
            {
                // Process may already have exited.
            }
        }
    }

    private static IEnumerable<int> GetListeningPids(int port)
    {
        if (!OperatingSystem.IsWindows())
            return [];

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano -p tcp",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return [];

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            var portToken = $":{port}";
            var pids = new HashSet<int>();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!line.Contains(portToken, StringComparison.Ordinal))
                    continue;

                var match = Regex.Match(line.Trim(), @"\s+(\d+)\s*$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var pid) && pid > 0)
                    pids.Add(pid);
            }

            return pids;
        }
        catch
        {
            return [];
        }
    }

    private static int ParsePortFromBaseUrl(string baseUrl)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && uri.Port > 0)
            return uri.Port;

        return 8188;
    }

    private async Task<int> ParseRestartTimeoutSecAsync(CancellationToken ct)
    {
        var raw = await settings.GetOrDefaultAsync(
            SettingKeys.LocalComfyRestartTimeoutSec,
            ComfyVramProfiles.ComfyRestartTimeoutSecDefault.ToString(),
            ct);
        return int.TryParse(raw, out var sec) ? Math.Clamp(sec, 30, 300) : ComfyVramProfiles.ComfyRestartTimeoutSecDefault;
    }

    private sealed record LaunchSpec(string Executable, string WorkingDirectory, bool UsePython, string? Script);
}
