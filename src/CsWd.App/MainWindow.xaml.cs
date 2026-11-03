using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace CsWd;

public partial class MainWindow : Window
{
    private HotkeyService? _hotkey;
    private bool _hooked;
    private int _seq;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>按设置注册/更换全局热键。失败时回退 Alt+Space。</summary>
    public void ApplyHotkey(AppSettings settings)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();

        if (!_hooked)
        {
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProcHook);
            _hooked = true;
        }

        _hotkey?.Dispose();
        _hotkey = null;

        if (!HotkeyPresets.TryParse(settings.Hotkey, out var mods, out var vk))
            (mods, vk) = (HotkeyService.ModAlt, 0x20u);
        try
        {
            _hotkey = new HotkeyService(hwnd, mods, vk);
        }
        catch
        {
            // 组合被占用：回退默认并提示
            _hotkey = new HotkeyService(hwnd, HotkeyService.ModAlt, 0x20u);
            StatusText.Text = $"热键 {settings.Hotkey} 被占用，已回退 Alt+Space";
        }
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyService.WmHotkey)
        {
            ToggleVisibility();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ---- 唤起 / 隐藏 ----

    private void ToggleVisibility()
    {
        if (IsVisible) Hide();
        else ShowAndFocus();
    }

    public void ShowAndFocus()
    {
        PositionOnActiveScreen();
        Show();
        Activate();
        InputBox.Clear();
        StatusText.Text = DaemonClient.Alive ? "ready" : "daemon 未启动";
        _ = RefreshAsync();
        InputBox.Focus();
    }

    private void PositionOnActiveScreen()
    {
        var wa = SystemParameters.WorkArea; // MVP：主屏工作区
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + wa.Height * 0.18;
    }

    private void Window_Deactivated(object sender, EventArgs e) => Hide();

    // ---- 输入与搜索 ----

    private async void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = string.IsNullOrEmpty(InputBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ModeHint.Text = InputBox.Text.StartsWith(">") ? "后台执行" : "";
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        int seq = ++_seq;
        string q = InputBox.Text.Trim();
        var items = new List<ResultItem>();
        try
        {
            if (q.StartsWith(">"))
            {
                var cmd = q[1..].Trim();
                if (cmd.Length > 0) items.Add(ResultItem.RunCommand(cmd));
            }
            else
            {
                var data = await DaemonClient.RequestAsync(new { op = "search", query = q });
                foreach (var it in data.GetProperty("items").EnumerateArray())
                {
                    var path = it.GetProperty("path").GetString() ?? "";
                    var count = it.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
                    items.Add(ResultItem.Directory(path, count));
                }
            }
        }
        catch (Exception ex)
        {
            items.Add(ResultItem.Error($"daemon 不可用：{ex.Message}"));
        }
        if (seq != _seq) return; // 过期响应

        ResultList.ItemsSource = items;
        if (items.Count > 0) ResultList.SelectedIndex = 0;
    }

    // ---- 键盘 ----

    private void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                _ = ExecuteCurrent();
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (ResultList.Items.Count == 0) return;
        int i = Math.Clamp(ResultList.SelectedIndex + delta, 0, ResultList.Items.Count - 1);
        ResultList.SelectedIndex = i;
        ResultList.ScrollIntoView(ResultList.SelectedItem);
    }

    private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => _ = ExecuteCurrent();

    // ---- 执行 ----

    private async Task ExecuteCurrent()
    {
        if (ResultList.SelectedItem is not ResultItem it) return;
        try
        {
            switch (it.Kind)
            {
                case ResultKind.Directory:
                {
                    // 打开交互终端 + 记录一次使用
                    var _ = DaemonClient.RequestAsync(new { op = "record", path = it.Path });
                    OpenTerminal(it.Path);
                    Hide();
                    break;
                }
                case ResultKind.RunCommand:
                {
                    var data = await DaemonClient.RequestAsync(new { op = "run", cmd = it.Cmd });
                    var job = data.GetProperty("job");
                    int id = job.GetProperty("id").GetInt32();
                    int? pid = job.TryGetProperty("pid", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
                    string log = job.TryGetProperty("log", out var l) ? l.GetString() ?? "" : "";
                    StatusText.Text = $"job #{id} 已转后台 (pid {pid})  日志: {log}";
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"错误：{ex.Message}";
        }
    }

    private static void OpenTerminal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/K cd /d \"{path}\"") { UseShellExecute = true });
        }
    }
}
