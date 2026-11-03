using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace CsWd;

public partial class SettingsWindow : Window
{
    /// <summary>保存成功后触发（App 据此重注册热键、更新托盘文案）。</summary>
    public event Action<AppSettings>? Saved;

    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        foreach (var h in HotkeyPresets.All) HotkeyBox.Items.Add(h);
        HotkeyBox.SelectedItem = HotkeyPresets.All.Contains(settings.Hotkey) ? settings.Hotkey : HotkeyPresets.All[0];
        HotkeyBox.SelectionChanged += (_, _) => HotkeyHint.Text = "";

        AutoStartBox.IsChecked = settings.AutoStart;
        DataDirText.Text = AppSettings.DataDir;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var hotkey = (string?)HotkeyBox.SelectedItem ?? "Alt+Space";
        if (!HotkeyPresets.TryParse(hotkey, out _, out _))
        {
            HotkeyHint.Text = "无法解析该热键组合";
            return;
        }

        _settings.Hotkey = hotkey;
        _settings.AutoStart = AutoStartBox.IsChecked == true;
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
