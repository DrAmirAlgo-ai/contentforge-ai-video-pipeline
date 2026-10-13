using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EdgeTTS.DotNet;
using EdgeTTS.DotNet.Models;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Data;
using ContentEmpireSystem.Infrastructure.Services.Ai;
using ContentEmpireSystem.Infrastructure.Services.Local;
using Microsoft.EntityFrameworkCore;

namespace ContentEmpireSystem.Infrastructure.Services;
public class TextToSpeechService(
    ApplicationDbContext db,
    ISystemSettingsService settings,
    IOutputPathService paths,
    IProjectWorkflowBuilder workflowBuilder,
    AiProviderResolver resolver,
    IHttpClientFactory httpClientFactory) : ITextToSpeechService
{
    public async Task<(string AudioPath, string TimestampsJson, string ProviderUsed)> GenerateSpeechAsync(
        Guid projectId, string text, CancellationToken ct = default)
    {
        var (provider, apiKey) = await resolver.ResolveTtsProviderAsync(ct);
        var configuredOutput = await settings.GetOrDefaultAsync(SettingKeys.OutputDirectory, "output", ct);
        var outputDir = paths.GetProjectDirectory(configuredOutput, projectId);

        var audioPath = paths.ToAbsolutePath(Path.Combine(outputDir, "narration.mp3"));
        string timestampsJson;

        if (provider == "ElevenLabs")
        {
            (audioPath, timestampsJson) = await GenerateElevenLabsAsync(apiKey, text, audioPath, ct);
        }
        else
        {
            (audioPath, timestampsJson) = await GenerateEdgeTtsAsync(text, audioPath, ct);
        }

        var project = await db.VideoProjects.FirstAsync(p => p.Id == projectId, ct);
        project.AudioFilePath = audioPath;
        project.WordTimestampsJson = timestampsJson;
        project.TtsProviderUsed = provider;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return (audioPath, timestampsJson, provider);
    }

    public async Task<VideoProjectDto> GenerateProjectAudioAsync(
        Guid projectId, GenerateAudioRequest? options, CancellationToken ct = default)
    {
        var project = await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);

        if (string.IsNullOrWhiteSpace(project.ScriptJson))
            throw new InvalidOperationException("ولّد السكريبت أولاً.");

        var script = ScriptJsonReader.TryParse(project.ScriptJson)
            ?? throw new InvalidOperationException("بيانات السكريبت غير صالحة. أعد توليد السكريبت.");
        var text = script.IsVoiceCode
            ? string.Join("\n", script.VoiceSegments.OrderBy(v => v.SortOrder).Select(v => v.Text))
            : script.FullNarrationText;

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("لا يوجد نص تعليق في السكريبت.");

        var rules = ProductionRulesDto.FromSettings(await settings.GetAllAsDictionaryAsync(ct));
        var sceneSecRaw = await settings.GetOrDefaultAsync(SettingKeys.ImageDurationSec, "5", ct);
        var sceneSec = double.TryParse(sceneSecRaw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 5;
        var maxWords = NarrationWordLimiter.ResolveMaxWords(script, rules.WordsPerSecond, sceneSec);
        text = NarrationWordLimiter.Enforce(text, maxWords);

        if (!string.IsNullOrWhiteSpace(options?.VoiceId))
            await settings.SetAsync(SettingKeys.ElevenLabsVoiceId, options.VoiceId, ct);

        if (!string.IsNullOrWhiteSpace(options?.EdgeVoice))
            await settings.SetAsync(SettingKeys.EdgeTtsVoice, options.EdgeVoice, ct);

        await GenerateSpeechAsync(projectId, text, ct);
        return await ReloadProjectDtoAsync(projectId, ct);
    }

    public async Task<VideoProjectDto> UploadProjectAudioAsync(
        Guid projectId, Stream audioStream, string fileName, CancellationToken ct = default)
    {
        if (audioStream is null || !audioStream.CanRead)
            throw new InvalidOperationException("لم يتم رفع ملف صوت.");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        string[] allowed = [".mp3", ".wav", ".m4a", ".ogg", ".aac", ".webm", ".flac"];
        if (!allowed.Contains(ext))
            throw new InvalidOperationException("صيغة غير مدعومة. استخدم MP3 أو WAV أو M4A أو OGG.");

        var project = await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);

        if (string.IsNullOrWhiteSpace(project.ScriptJson))
            throw new InvalidOperationException("ولّد السكريبت أولاً.");

        var configuredOutput = await settings.GetOrDefaultAsync(SettingKeys.OutputDirectory, "output", ct);
        var projectDir = paths.GetProjectDirectory(configuredOutput, projectId);
        var audioFileName = $"narration{ext}";
        var audioPath = Path.Combine(projectDir, audioFileName);

        if (audioStream.CanSeek)
            audioStream.Position = 0;

        await using (var fs = File.Create(audioPath))
            await audioStream.CopyToAsync(fs, ct);

        project.AudioFilePath = paths.ToAbsolutePath(audioPath);
        project.TtsProviderUsed = "Manual";
        project.WordTimestampsJson = null;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await ReloadProjectDtoAsync(projectId, ct);
    }

    public async Task<(string AbsolutePath, string ContentType)?> TryResolveProjectAudioFileAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var project = await db.VideoProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null || string.IsNullOrWhiteSpace(project.AudioFilePath))
            return null;

        var absolute = paths.ToAbsolutePath(project.AudioFilePath);
        if (!File.Exists(absolute))
            return null;

        var contentType = Path.GetExtension(absolute).ToLowerInvariant() switch
        {
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".aac" => "audio/aac",
            ".webm" => "audio/webm",
            ".flac" => "audio/flac",
            _ => "audio/mpeg"
        };

        return (absolute, contentType);
    }

    private async Task<VideoProjectDto> ReloadProjectDtoAsync(Guid projectId, CancellationToken ct) =>
        await VideoProjectMapper.MapWithWorkflowAsync(await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .AsNoTracking()
            .FirstAsync(p => p.Id == projectId, ct), workflowBuilder, paths, ct);

    private async Task<(string AudioPath, string TimestampsJson)> GenerateElevenLabsAsync(
        string apiKey, string text, string audioPath, CancellationToken ct)
    {
        var voiceId = await settings.GetOrDefaultAsync(SettingKeys.ElevenLabsVoiceId, "21m00Tcm4TlvDq8ikWAM", ct);
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("xi-api-key", apiKey);

        var response = await client.PostAsJsonAsync(
            $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}/with-timestamps",
            new { text, model_id = "eleven_multilingual_v2" }, ct);

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        var audioBase64 = doc.RootElement.GetProperty("audio_base64").GetString()!;
        await File.WriteAllBytesAsync(audioPath, Convert.FromBase64String(audioBase64), ct);

        var words = new List<WordTimestampDto>();
        if (doc.RootElement.TryGetProperty("alignment", out var alignment))
        {
            var chars = alignment.GetProperty("characters").EnumerateArray().Select(c => c.GetString()).ToList();
            var starts = alignment.GetProperty("character_start_times_seconds").EnumerateArray().Select(c => c.GetDouble()).ToList();
            var ends = alignment.GetProperty("character_end_times_seconds").EnumerateArray().Select(c => c.GetDouble()).ToList();

            var currentWord = new StringBuilder();
            double wordStart = 0;
            for (var i = 0; i < chars.Count; i++)
            {
                var ch = chars[i] ?? "";
                if (ch == " " || ch == "\n")
                {
                    if (currentWord.Length > 0)
                    {
                        words.Add(new WordTimestampDto
                        {
                            Word = currentWord.ToString(),
                            StartMs = wordStart * 1000,
                            EndMs = (ends[i - 1]) * 1000
                        });
                        currentWord.Clear();
                    }
                }
                else
                {
                    if (currentWord.Length == 0) wordStart = starts[i];
                    currentWord.Append(ch);
                }
            }
            if (currentWord.Length > 0)
                words.Add(new WordTimestampDto { Word = currentWord.ToString(), StartMs = wordStart * 1000, EndMs = ends[^1] * 1000 });
        }

        return (audioPath, JsonSerializer.Serialize(words));
    }

    private async Task<(string AudioPath, string TimestampsJson)> GenerateEdgeTtsAsync(
        string text, string audioPath, CancellationToken ct)
    {
        var voice = await settings.GetOrDefaultAsync(SettingKeys.EdgeTtsVoice, "ar-EG-ShakirNeural", ct);
        var communicate = new Communicate(text, voice: voice, boundaryType: "WordBoundary");
        var subMaker = new SubMaker();

        await using (var fs = File.Create(audioPath))
        {
            await foreach (var chunk in communicate.StreamAsync().WithCancellation(ct))
            {
                if (chunk is AudioChunk audio)
                    await fs.WriteAsync(audio.Data, ct);
                else if (chunk is MetadataChunk metadata)
                    subMaker.Feed(metadata);
            }
        }

        var srt = subMaker.GetSrt();
        var words = ParseSrtToWordTimestamps(srt, text);
        return (audioPath, JsonSerializer.Serialize(words));
    }

    private static List<WordTimestampDto> ParseSrtToWordTimestamps(string srt, string fallbackText)
    {
        var words = new List<WordTimestampDto>();
        if (string.IsNullOrWhiteSpace(srt))
            return EstimateWordTimestamps(fallbackText);

        var blocks = Regex.Split(srt.Trim(), @"\r?\n\r?\n");
        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length < 3) continue;

            var timing = lines[1].Split("-->", StringSplitOptions.TrimEntries);
            if (timing.Length != 2) continue;

            var word = string.Join(' ', lines.Skip(2)).Trim();
            if (string.IsNullOrWhiteSpace(word)) continue;

            words.Add(new WordTimestampDto
            {
                Word = word,
                StartMs = ParseSrtTime(timing[0]),
                EndMs = ParseSrtTime(timing[1])
            });
        }

        return words.Count > 0 ? words : EstimateWordTimestamps(fallbackText);
    }

    private static double ParseSrtTime(string t)
    {
        // 00:00:01,500
        if (TimeSpan.TryParseExact(t.Trim(), @"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture, out var ts))
            return ts.TotalMilliseconds;

        t = t.Replace(',', '.');
        return TimeSpan.TryParse(t, CultureInfo.InvariantCulture, out var ts2)
            ? ts2.TotalMilliseconds
            : 0;
    }

    private static List<WordTimestampDto> EstimateWordTimestamps(string text)
    {
        var textWords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var msPerWord = 400.0;
        var words = new List<WordTimestampDto>();
        for (var i = 0; i < textWords.Length; i++)
        {
            words.Add(new WordTimestampDto
            {
                Word = textWords[i],
                StartMs = i * msPerWord,
                EndMs = (i + 1) * msPerWord
            });
        }
        return words;
    }
}
