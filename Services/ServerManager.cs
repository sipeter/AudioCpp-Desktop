using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AudioCppDesktop.Models;

namespace AudioCppDesktop.Services;

public sealed class ServerManager : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private Process? _process;
    private readonly object _lock = new();

    public event Action<string>? LogReceived;
    public event Action<bool>? RunningStateChanged;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public static string? DetectPortableRoot(string? savedRoot)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(savedRoot)) candidates.Add(savedRoot);
        candidates.Add(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        candidates.Add(Environment.CurrentDirectory);

        foreach (var dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsValidPortableRoot(dir)) return Path.GetFullPath(dir);
        }
        return null;
    }

    public static bool IsValidPortableRoot(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
        return File.Exists(Path.Combine(dir, "bin", "audiocpp_server.exe"));
    }

    public static string ResolveModelsDir(string portableRoot, string? savedModelsDir)
    {
        if (!string.IsNullOrWhiteSpace(savedModelsDir) && Directory.Exists(savedModelsDir))
            return Path.GetFullPath(savedModelsDir);

        var local = Path.Combine(portableRoot, "models");
        if (Directory.Exists(local) && Directory.GetFiles(local, "*.gguf").Length > 0)
            return Path.GetFullPath(local);

        var parent = Directory.GetParent(portableRoot)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            var sibling = Path.Combine(parent, "models");
            if (Directory.Exists(sibling) && Directory.GetFiles(sibling, "*.gguf").Length > 0)
                return Path.GetFullPath(sibling);
        }

        return Path.GetFullPath(local);
    }

    public static string ResolveVoiceDir(string portableRoot, string? savedVoiceDir)
    {
        if (!string.IsNullOrWhiteSpace(savedVoiceDir) && Directory.Exists(savedVoiceDir))
            return Path.GetFullPath(savedVoiceDir);

        var local = Path.Combine(portableRoot, "voice-library");
        if (Directory.Exists(local) && Directory.GetFiles(local, "*.wav").Length > 0)
            return Path.GetFullPath(local);

        var parent = Directory.GetParent(portableRoot)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            var sibling = Path.Combine(parent, "voice-library");
            if (Directory.Exists(sibling) && Directory.GetFiles(sibling, "*.wav").Length > 0)
                return Path.GetFullPath(sibling);
        }

        return Path.GetFullPath(local);
    }

    public static string GetLanIpAddress()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var name = ni.Name;
                if (name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("WSL", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("VMware", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = ua.Address.ToString();
                    if (!ip.StartsWith("169.254.") && !IPAddress.IsLoopback(ua.Address))
                    {
                        return ip;
                    }
                }
            }
        }
        catch
        {
            // Fallback below
        }
        return "127.0.0.1";
    }

    public string WriteServerConfig(string portableRoot, string modelsDir, string voiceDir, GuiSettings settings, List<InstalledModelInfo> models)
    {
        var voiceDirPosix = Path.GetFullPath(voiceDir).Replace('\\', '/');
        Directory.CreateDirectory(voiceDir);
        Directory.CreateDirectory(modelsDir);

        var configObj = new Dictionary<string, object>
        {
            ["host"] = "0.0.0.0",
            ["port"] = settings.Port,
            ["backend"] = settings.Backend,
            ["device"] = 0,
            ["threads"] = Math.Max(1, settings.Threads),
            ["lazy_load"] = true,
            ["max_loaded_models"] = Math.Max(1, settings.MaxLoadedModels),
            ["ui"] = true,
            ["ui_management"] = false,
            ["voice_dir"] = voiceDirPosix,
            ["models"] = models.Select(m => new Dictionary<string, object>
            {
                ["id"] = m.Id,
                ["family"] = m.Family,
                ["path"] = Path.GetFullPath(m.FullPath).Replace('\\', '/'),
                ["task"] = m.Task,
                ["mode"] = "offline",
                ["default_request_options"] = m.DefaultRequestOptions
            }).ToList()
        };

        var json = JsonSerializer.Serialize(configObj, new JsonSerializerOptions { WriteIndented = true });
        var configPath = Path.Combine(portableRoot, "server.json");
        File.WriteAllText(configPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return configPath;
    }

    public async Task<bool> StartAsync(string portableRoot, string modelsDir, string voiceDir, GuiSettings settings, CancellationToken ct = default)
    {
        if (IsRunning) return true;

        var exePath = Path.Combine(portableRoot, "bin", "audiocpp_server.exe");
        if (!File.Exists(exePath))
        {
            LogReceived?.Invoke($"[ERROR] 未找到服务程序: {exePath}");
            return false;
        }

        var models = ModelCatalog.ScanInstalledModels(modelsDir);
        if (models.Count == 0)
        {
            LogReceived?.Invoke($"[ERROR] 模型目录 ({modelsDir}) 中还没有已识别的 .gguf 模型，请先在「模型管理」中下载或更换模型目录。");
            return false;
        }

        var configPath = WriteServerConfig(portableRoot, modelsDir, voiceDir, settings, models);
        LogReceived?.Invoke($"[INFO] 已生成配置: {configPath} ({models.Count} 个模型, 后端={settings.Backend.ToUpperInvariant()}, 端口={settings.Port})");

        var binDir = Path.Combine(portableRoot, "bin");
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"--config \"{configPath}\"",
            WorkingDirectory = portableRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        psi.Environment["PATH"] = $"{binDir};{existingPath}";

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) LogReceived?.Invoke(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) LogReceived?.Invoke(e.Data); };
        proc.Exited += (_, _) =>
        {
            LogReceived?.Invoke($"[INFO] audiocpp_server 已退出 (code={proc.ExitCode})");
            RunningStateChanged?.Invoke(false);
        };

        lock (_lock)
        {
            _process = proc;
        }

        if (!proc.Start())
        {
            LogReceived?.Invoke("[ERROR] 无法启动 audiocpp_server.exe");
            RunningStateChanged?.Invoke(false);
            return false;
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        RunningStateChanged?.Invoke(true);

        // Wait up to 15s for HTTP readiness
        var probeUrl = $"http://127.0.0.1:{settings.Port}/v1/models";
        for (var i = 0; i < 30; i++)
        {
            if (ct.IsCancellationRequested || proc.HasExited) return false;
            try
            {
                using var resp = await _http.GetAsync(probeUrl, ct);
                if ((int)resp.StatusCode < 500)
                {
                    LogReceived?.Invoke($"[READY] 服务已就绪: http://127.0.0.1:{settings.Port} (局域网: http://{GetLanIpAddress()}:{settings.Port})");
                    return true;
                }
            }
            catch
            {
                // Keep polling while server binds socket
            }
            await Task.Delay(500, ct);
        }

        LogReceived?.Invoke("[WARN] 服务进程已启动，但端口响应较慢，请留意下方日志输出。");
        return !proc.HasExited;
    }

    public void Stop(bool silent = false)
    {
        Process? proc;
        lock (_lock)
        {
            proc = _process;
            _process = null;
        }

        if (proc is null) return;
        try
        {
            proc.EnableRaisingEvents = false;
            if (!proc.HasExited)
            {
                if (!silent) LogReceived?.Invoke("[INFO] 正在停止 audiocpp_server 并释放显存...");
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(1500);
            }
        }
        catch (Exception ex)
        {
            if (!silent) LogReceived?.Invoke($"[WARN] 停止进程时异常: {ex.Message}");
        }
        finally
        {
            try { proc.Dispose(); } catch { }
            if (!silent) RunningStateChanged?.Invoke(false);
        }
    }

    public async Task<byte[]> SynthesizeTestWavAsync(int port, string modelId, string voiceName, string text, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = modelId,
            ["input"] = text,
            ["voice"] = voiceName,
            ["response_format"] = "wav"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"http://127.0.0.1:{port}/v1/audio/speech", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {errBody}");
        }
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<string> TranscribeWavAsync(int port, string asrModelId, string wavPath, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(asrModelId), "model");
        var bytes = await File.ReadAllBytesAsync(wavPath, ct);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", Path.GetFileName(wavPath));

        using var resp = await _http.PostAsync($"http://127.0.0.1:{port}/v1/audio/transcriptions", form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {body}");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
            {
                return (textProp.GetString() ?? string.Empty).Trim();
            }
        }
        catch
        {
            // Fallback to raw response text
        }
        return body.Trim();
    }

    public void Dispose()
    {
        Stop(silent: true);
        _http.Dispose();
    }
}
