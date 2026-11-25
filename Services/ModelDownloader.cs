using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace AudioCppDesktop.Services;

public sealed class ModelDownloader
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.None
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public async Task DownloadAsync(
        string url,
        string destinationPath,
        Action<double, string> onProgress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var partPath = destinationPath + ".part";
        long existingBytes = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("AudioCpp-Desktop/0.1");
        if (existingBytes > 0)
        {
            req.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            existingBytes = 0;
        }
        else if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"下载失败: HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase})");
        }

        bool isPartial = resp.StatusCode == HttpStatusCode.PartialContent;
        long totalBytes = isPartial && resp.Content.Headers.ContentRange?.Length is long fullLen
            ? fullLen
            : (resp.Content.Headers.ContentLength ?? -1);

        if (!isPartial) existingBytes = 0;

        var mode = isPartial ? FileMode.Append : FileMode.Create;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = new FileStream(partPath, mode, FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true))
        {
            var buffer = new byte[256 * 1024];
            long downloaded = existingBytes;
            long sessionBytes = 0;
            var sw = Stopwatch.StartNew();
            var lastUi = TimeSpan.Zero;

            while (true)
            {
                int read = await src.ReadAsync(buffer, ct);
                if (read == 0) break;
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                sessionBytes += read;

                if (sw.Elapsed - lastUi >= TimeSpan.FromMilliseconds(250))
                {
                    lastUi = sw.Elapsed;
                    double speedMb = sessionBytes / 1024.0 / 1024.0 / Math.Max(sw.Elapsed.TotalSeconds, 0.1);
                    double pct = totalBytes > 0 ? Math.Min(99.9, downloaded * 100.0 / totalBytes) : 0;
                    string text = totalBytes > 0
                        ? $"下载中 {pct:F1}% ({downloaded / 1024.0 / 1024.0:F0}/{totalBytes / 1024.0 / 1024.0:F0} MB, {speedMb:F1} MB/s)"
                        : $"下载中 {downloaded / 1024.0 / 1024.0:F1} MB ({speedMb:F1} MB/s)";
                    onProgress(pct, text);
                }
            }
        }

        // Validate GGUF header before moving into place
        var magicBuf = new byte[4];
        await using (var check = File.OpenRead(partPath))
        {
            if (await check.ReadAsync(magicBuf, ct) < 4 || Encoding.ASCII.GetString(magicBuf) != "GGUF")
            {
                throw new InvalidDataException("文件头校验失败：下载内容不是有效的 GGUF 模型文件。");
            }
        }

        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        File.Move(partPath, destinationPath);
        onProgress(100, "已安装");
    }
}
