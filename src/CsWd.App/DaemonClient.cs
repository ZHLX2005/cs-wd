using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace CsWd;

/// <summary>
/// named pipe 客户端：一次请求一条连接（与 daemon 协议一致：JSON Lines）。
/// </summary>
public static class DaemonClient
{
    private const string PipeName = "cs-wd-daemon";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static bool Alive
    {
        get
        {
            try { using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous); c.Connect(150); return c.IsConnected; }
            catch { return false; }
        }
    }

    /// <summary>daemon 不在则拉起（幂等，daemon 自带单实例探测）。</summary>
    public static void EnsureDaemon()
    {
        if (Alive) return;
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cs-wd", "cs-wd-daemon.exe"),
            // 开发期：仓库根 bin/cs-wd-daemon.exe（BaseDirectory = src/CsWd.App/bin/<cfg>/<tfm>/）
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "bin", "cs-wd-daemon.exe")),
        };
        foreach (var exe in candidates.Where(File.Exists))
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                return;
            }
            catch { /* 忽略，继续找下一个 */ }
        }
    }

    /// <summary>发送一个请求对象，返回响应 JSON 文档。失败抛异常。</summary>
    public static async Task<JsonElement> RequestAsync(object request, CancellationToken ct = default)
    {
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(2000, ct);
        var json = JsonSerializer.Serialize(request, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await pipe.WriteAsync(bytes, ct);
        await pipe.FlushAsync(ct);

        using var reader = new StreamReader(pipe, Encoding.UTF8);
        var line = await reader.ReadLineAsync(ct) ?? throw new IOException("daemon closed");
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            var err = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown error";
            throw new InvalidOperationException(err);
        }
        // 必须在 doc 释放前克隆
        return root.GetProperty("data").Clone();
    }
}
