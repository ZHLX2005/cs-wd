using System.Diagnostics;
using System.IO;
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
                    // daemon 未带 kind 时按旧的「目录」处理，保持向后兼容
                    var kind = it.TryGetProperty("kind", out var k) ? k.GetString() ?? "directory" : "directory";
                    if (kind == "link")
                    {
                        var title = it.GetProperty("title").GetString() ?? "";
                        var url = it.GetProperty("url").GetString() ?? "";
                        var subtitle = it.TryGetProperty("subtitle", out var sb) ? sb.GetString() ?? "" : "";
                        if (url.Length > 0) items.Add(ResultItem.Link(title, url, subtitle));
                    }
                    else
                    {
                        var path = it.GetProperty("path").GetString() ?? "";
                        var count = it.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
                        items.Add(ResultItem.Directory(path, count));
                    }
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
                _ = ExecuteCurrent(usePowershell: Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift));
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

    /// <summary>Shift + 左键点击目录项 → 在 PowerShell 打开。</summary>
    private void ResultList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) return;
        if (ResultList.ContainerFromElement(e.OriginalSource as System.Windows.DependencyObject) is not ListBoxItem { Content: ResultItem it }) return;
        if (it.Kind != ResultKind.Directory) return;
        OpenDirectory(it.Path, usePowershell: true);
        e.Handled = true;
    }

    // ---- 执行 ----

    private async Task ExecuteCurrent(bool usePowershell = false)
    {
        if (ResultList.SelectedItem is not ResultItem it) return;
        try
        {
            switch (it.Kind)
            {
                case ResultKind.Directory:
                {
                    // Enter = 资源管理器；Shift+Enter = PowerShell（记录一次使用）
                    _ = DaemonClient.RequestAsync(new { op = "record", path = it.Path });
                    OpenDirectory(it.Path, usePowershell);
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
                case ResultKind.Link:
                {
                    // UseShellExecute=true → 交给系统默认浏览器，不硬编码 chrome/edge
                    Process.Start(new ProcessStartInfo(it.Url) { UseShellExecute = true });
                    Hide();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"错误：{ex.Message}";
        }
    }

    private static void OpenDirectory(string path, bool usePowershell)
    {
        if (usePowershell)
        {
            // PowerShell（优先 pwsh，无则 Windows PowerShell），留在交互会话
            var exe = File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"))
                ? "pwsh.exe" : "powershell.exe";
            var escaped = path.Replace("'", "''");
            Process.Start(new ProcessStartInfo(exe,
                $"-NoExit -Command \"Set-Location -LiteralPath '{escaped}'\"")
            { UseShellExecute = true });
        }
        else
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }
}
