using System.Windows;
using System.Windows.Interop;

namespace CsWd;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWin;
    private TrayService? _tray;
    private SettingsWindow? _settingsWin;
    private AppSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = AppSettings.Load();
        // 拉起 daemon（幂等：daemon 自带单实例探测）
        DaemonClient.EnsureDaemon();

        var win = new MainWindow();
        _mainWin = win;
        win.ShowAndFocus();
        win.ApplyHotkey(_settings);

        // 托盘常驻
        _tray = new TrayService(_settings.Hotkey);
        _tray.ShowRequested += win.ShowAndFocus;
        _tray.SettingsRequested += OpenSettings;
        _tray.ExitRequested += ExitApp;
    }

    private void OpenSettings()
    {
        if (_settingsWin != null)
        {
            _settingsWin.Activate();
            return;
        }
        var sw = new SettingsWindow(_settings);
        sw.Saved += s =>
        {
            _settings = s;
            _mainWin?.ApplyHotkey(s);
            _tray?.UpdateHotkeyLabel(s.Hotkey);
        };
        sw.Closed += (_, _) => _settingsWin = null;
        sw.Show();
    }

    private void ExitApp()
    {
        _tray?.Dispose();
        Shutdown();
    }
}
