using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CsWd;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    /// <summary>保存成功后触发（App 据此重注册热键、切主题、更新托盘文案）。</summary>
    public event Action<AppSettings>? Saved;

    private readonly AppSettings _settings;
    private string _pendingHotkey;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _pendingHotkey = settings.Hotkey;

        HotkeyBox.Text = _pendingHotkey;
        AutoStartTgl.IsChecked = settings.AutoStart;
        (settings.Theme == ThemeService.Light ? LightRb : DarkRb).IsChecked = true;
        DataDirText.Text = AppSettings.DataDir;
    }

    // ---- 热键录制 ----

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        HotkeyHint.Text = "正在录制：按下组合键（需含 Ctrl/Alt/Win），Esc 取消";
        HotkeyBox.Clear();
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = _pendingHotkey;
        HotkeyHint.Text = "点击输入框后按下想要的组合键（需包含 Ctrl/Alt/Win 修饰），Esc 取消";
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (HotkeyPresets.IsModifier(key)) return; // 只按了修饰键，等主键

        if (key == Key.Escape) // 取消录制
        {
            Keyboard.ClearFocus();
            HotkeyBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
            return;
        }
        if (key is Key.Tab or Key.Return or Key.Enter) return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None)
        {
            HotkeyHint.Text = "需要至少一个修饰键（Ctrl / Alt / Win，Shift 可叠加）";
            return;
        }

        _pendingHotkey = HotkeyPresets.Format(mods, key);
        HotkeyBox.Text = _pendingHotkey;
        HotkeyHint.Text = "已录入，点击「保存」生效";
        Keyboard.ClearFocus();
        HotkeyBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
    }

    // ---- 保存 ----

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var hotkey = string.IsNullOrWhiteSpace(HotkeyBox.Text) ? _pendingHotkey : HotkeyBox.Text;
        if (!HotkeyPresets.TryParse(hotkey, out _, out _))
        {
            HotkeyHint.Text = $"无法解析热键「{hotkey}」";
            return;
        }

        _settings.Hotkey = hotkey;
        _settings.Theme = (LightRb.IsChecked == true) ? ThemeService.Light : ThemeService.Dark;
        _settings.AutoStart = AutoStartTgl.IsChecked == true;
        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            HotkeyHint.Text = $"保存失败：{ex.Message}";
            return;
        }

        Saved?.Invoke(_settings);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenLogs_Click(object sender, RoutedEventArgs e) =>
        OpenDir(Path.Combine(AppSettings.DataDir, "logs"));

    private void OpenData_Click(object sender, RoutedEventArgs e) => OpenDir(AppSettings.DataDir);

    private static void OpenDir(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
