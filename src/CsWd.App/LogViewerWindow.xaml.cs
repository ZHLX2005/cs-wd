using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace CsWd;

public partial class LogViewerWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly long _jobId;
    private readonly string _cmd;
    private readonly string _logPath;

    public LogViewerWindow(long jobId, string cmd, string logPath)
    {
        InitializeComponent();
        _jobId = jobId;
        _cmd = cmd;
        _logPath = logPath;
        Title = $"日志 #{jobId} — {cmd}";
        MetaText.Text = logPath;
        Loaded += (_, _) => _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var data = await DaemonClient.RequestAsync(new { op = "log", id = _jobId, tail = 1000 });
            var lines = data.GetProperty("lines");
            LogBox.Text = lines.ValueKind == JsonValueKind.Array
                ? string.Join('\n', lines.EnumerateArray().Select(l => l.GetString()))
                : "";
        }
        catch (Exception ex)
        {
            // daemon 没这条记录了（job 表被清）——直接读日志文件兜底
            try
            {
                LogBox.Text = File.Exists(_logPath)
                    ? File.ReadAllText(_logPath)
                    : $"读取失败：{ex.Message}\n日志文件：{_logPath}";
            }
            catch (Exception ex2)
            {
                LogBox.Text = $"读取失败：{ex.Message} / {ex2.Message}";
            }
        }
        LogBox.ScrollToEnd();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    private void Notepad_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_logPath))
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_logPath}\"") { UseShellExecute = true });
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(LogBox.Text);
        MetaText.Text = "已复制到剪贴板 · " + _logPath;
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e) => LogBox.ScrollToEnd();
}
