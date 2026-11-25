using System.Diagnostics;
using System.IO;
using System.Text;
using AudioCppDesktop.Models;

namespace AudioCppDesktop.Services;

public static class VoiceLibraryManager
{
    public static List<VoiceItem> LoadVoices(string voiceDir)
    {
        Directory.CreateDirectory(voiceDir);

        var promptMap = LoadPromptMap(Path.Combine(voiceDir, "prompt_text"));
        var items = new List<VoiceItem>();

        foreach (var wav in Directory.GetFiles(voiceDir, "*.wav", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(wav);
            var info = new FileInfo(wav);
            promptMap.TryGetValue(name, out var prompt);
            items.Add(new VoiceItem
            {
                Name = name,
                FilePath = wav,
                SizeText = info.Length >= 1024 * 1024
                    ? $"{info.Length / 1024.0 / 1024.0:F2} MB"
                    : $"{info.Length / 1024.0:F0} KB",
                PromptText = prompt ?? string.Empty
            });
        }

        return items;
    }

    public static Dictionary<string, string> LoadPromptMap(string promptFilePath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(promptFilePath)) return map;

        foreach (var rawLine in File.ReadAllLines(promptFilePath, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
            var sep = line.IndexOf('|');
            if (sep <= 0) continue;
            var key = line[..sep].Trim();
            var val = line[(sep + 1)..].Trim();
            if (!string.IsNullOrEmpty(key)) map[key] = val;
        }
        return map;
    }

    public static void SavePromptText(string voiceDir, IEnumerable<VoiceItem> voices)
    {
        Directory.CreateDirectory(voiceDir);
        var promptFilePath = Path.Combine(voiceDir, "prompt_text");

        var sb = new StringBuilder();
        sb.AppendLine("# audio.cpp voice-library reference transcripts (format: voice_name|exact spoken text)");
        sb.AppendLine("# 注意: Qwen3-TTS / CosyVoice 3 / VoxCPM 2 / M5-TTS 必须逐字准确填写；IndexTTS 2.5 可留空");
        foreach (var v in voices)
        {
            if (!string.IsNullOrWhiteSpace(v.PromptText))
            {
                sb.AppendLine($"{v.Name}|{v.PromptText.Trim().Replace("\r", "").Replace("\n", " ")}");
            }
        }

        File.WriteAllText(promptFilePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static async Task<string> ImportAudioAsync(string portableRoot, string voiceDir, string sourceAudioPath)
    {
        Directory.CreateDirectory(voiceDir);

        var baseName = Path.GetFileNameWithoutExtension(sourceAudioPath);
        var ext = Path.GetExtension(sourceAudioPath).ToLowerInvariant();
        var targetWav = Path.Combine(voiceDir, baseName + ".wav");

        if (ext == ".wav")
        {
            File.Copy(sourceAudioPath, targetWav, overwrite: true);
            return targetWav;
        }

        var ffmpeg = FindFfmpeg(portableRoot);
        if (ffmpeg is null)
        {
            throw new InvalidOperationException(
                $"audio.cpp 仅识别 .wav 文件。当前文件为 {ext}，但本机未检测到 ffmpeg，无法自动转码。请先转为 .wav 后再导入。");
        }

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-y -i \"{sourceAudioPath}\" -ac 1 -ar 24000 -c:a pcm_s16le \"{targetWav}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 ffmpeg 转码进程。");
        var err = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0 || !File.Exists(targetWav))
        {
            throw new InvalidOperationException($"ffmpeg 转码失败 (exit={proc.ExitCode}): {err}");
        }
        return targetWav;
    }

    private static string? FindFfmpeg(string portableRoot)
    {
        var local = Path.Combine(portableRoot, "bin", "ffmpeg.exe");
        if (File.Exists(local)) return local;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
