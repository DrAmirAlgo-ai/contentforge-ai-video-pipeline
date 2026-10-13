using System.Globalization;
using System.Text;

namespace ContentEmpireSystem.Infrastructure.Services;

/// <summary>
/// Normalizes rendered MP4 for Telegram, Messenger, iOS, and Android in-app players.
/// </summary>
internal static class MobileVideoExporter
{
    public static string BuildMontageEncodeArgs(int outputFps, string preset, int crf)
    {
        var fps = outputFps.ToString(CultureInfo.InvariantCulture);
        var gop = Math.Max(outputFps * 2, 25).ToString(CultureInfo.InvariantCulture);
        var crfStr = crf.ToString(CultureInfo.InvariantCulture);
        return
            " -f mp4" +
            " -c:v libx264 -pix_fmt yuv420p -profile:v baseline -level 4.0" +
            $" -preset {preset} -crf {crfStr}" +
            $" -r {fps} -g {gop} -keyint_min {fps}" +
            " -x264-params bframes=0:ref=3" +
            " -tag:v avc1" +
            " -c:a aac -b:a 128k -ar 44100 -ac 2" +
            " -tag:a mp4a" +
            " -vsync cfr";
    }

    public static string BuildDeliveryPassArgs(string sourcePath, string destPath, int outputFps, int crf)
    {
        var fps = outputFps.ToString(CultureInfo.InvariantCulture);
        var gop = Math.Max(outputFps * 2, 25).ToString(CultureInfo.InvariantCulture);
        var crfStr = crf.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append($"-y -fflags +genpts -i \"{sourcePath}\" ");
        sb.Append("-map 0:v:0 -map 0:a:0? ");
        sb.Append($"-vf fps={fps},format=yuv420p,setsar=1 ");
        sb.Append("-c:v libx264 -pix_fmt yuv420p -profile:v baseline -level 4.0 ");
        sb.Append($"-preset fast -crf {crfStr} -maxrate 6M -bufsize 12M ");
        sb.Append($"-r {fps} -g {gop} -keyint_min {fps} ");
        sb.Append("-x264-params bframes=0:ref=3 ");
        sb.Append("-tag:v avc1 -movflags +faststart ");
        sb.Append("-c:a aac -b:a 128k -ar 44100 -ac 2 -tag:a mp4a ");
        sb.Append("-vsync cfr -f mp4 ");
        sb.Append($"\"{destPath}\"");
        return sb.ToString();
    }
}
