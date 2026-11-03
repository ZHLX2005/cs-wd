using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CsWd;

public class JobRow
{
    public long Id { get; init; }
    public string Cmd { get; init; } = "";
    public string Log { get; init; } = "";
    public bool Running { get; init; }
    public string StatusText { get; init; } = "";
    public string MetaText { get; init; } = "";
    public string KillLabel { get; init; } = "结束";
    public Wpf.Ui.Controls.ControlAppearance KillAppearance { get; init; } = Wpf.Ui.Controls.ControlAppearance.Danger;
    public System.Windows.Media.Brush StatusBrush { get; init; } = System.Windows.Media.Brushes.DimGray;
}

public partial class JobsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly DispatcherTimer _timer;
    private int _seq;

    public JobsWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();
        Loaded += (_, _) => _ = RefreshAsync();
        Closed += (_, _) => _timer.Stop();
    }

    private async Task RefreshAsync()
    {
        if (AutoRefreshTgl.IsChecked != true && _seq > 0) return;
        int seq = ++_seq;

        var rows = new List<JobRow>();
        string err = "";
        try
        {
            var data = await DaemonClient.RequestAsync(new { op = "jobs" });
            foreach (var j in data.GetProperty("jobs").EnumerateArray())
            {
                long id = j.GetProperty("id").GetInt64();
                string cmd = j.GetProperty("cmd").GetString() ?? "";
                bool exited = j.TryGetProperty("exited", out var ex) && ex.GetBoolean();
                long? code = j.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt64() : (long?)null;
                long? pid = j.TryGetProperty("pid", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : (long?)null;
                string log = j.TryGetProperty("log", out var lg) ? lg.GetString() ?? "" : "";
                long startedMs = j.TryGetProperty("startedMs", out var st) ? st.GetInt64() : 0;
                rows.Add(new JobRow
                {
                    Id = id,
                    Cmd = cmd,
                    Log = log,
                    Running = !exited,
                    StatusText = exited ? $"已退出 (code {code})" : "运行中",
                    StatusBrush = exited ? System.Windows.Media.Brushes.DimGray : System.Windows.Media.Brushes.MediumSeaGreen,
                    MetaText = $"#{id} · PID {pid} · {DateTimeOffset.FromUnixTimeMilliseconds(startedMs).LocalDateTime:HH:mm:ss} · {log}",
                    KillLabel = exited ? "清除" : "结束",
                    KillAppearance = exited ? Wpf.Ui.Controls.ControlAppearance.Secondary : Wpf.Ui.Controls.ControlAppearance.Danger,
                });
            }
        }
        catch (Exception ex)
        {
            err = ex.Message;
        }
        if (seq != _seq) return;

        JobList.ItemsSource = rows;
        StatsText.Text = err != "" ? $"daemon 不可用：{err}"
            : rows.Count == 0 ? "暂无后台任务 —— 在搜索框输入 > 命令 回车即可转入后台"
            : $"{rows.Count} 个任务";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    private async void Kill_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not JobRow row) return;
        try
        {
            if (row.Running)
            {
                await DaemonClient.RequestAsync(new { op = "kill", id = row.Id });
                StatusText.Text = $"已发送结束指令：#{row.Id}";
            }
            else
            {
                await DaemonClient.RequestAsync(new { op = "forget", id = row.Id });
                StatusText.Text = $"已清除记录：#{row.Id}";
            }
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"错误：{ex.Message}";
        }
    }

    private void Log_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not JobRow row) return;
        new LogViewerWindow(row.Id, row.Cmd, row.Log).Show();
    }
}
