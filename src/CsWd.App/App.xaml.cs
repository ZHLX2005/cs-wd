using System.Windows;
using System.Windows.Interop;

namespace CsWd;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWin;
    private TrayService? _tray;
    private SettingsWindow? _settingsWin;
    private AppSettings _settings = new();
    private System.Threading.Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常兜底：写日志到 %LOCALAPPDATA%\cs-wd\logs\app-*.log，UI 线程异常不让进程死
        DispatcherUnhandledException += (_, e) =>
        {
            LogCrash(e.Exception);
            System.Windows.Forms.MessageBox.Show(
                $"出现错误（已写入日志，程序继续运行）：\n{e.Exception.Message}",
                "cs-wd", System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash(e.Exception);
            e.SetObserved();
        };

        // 单实例保护：重复启动时激活已有实例并退出
        _singleInstanceMutex = new System.Threading.Mutex(true, @"Local\cs-wd-single-instance", out var isNew);
        if (!isNew)
        {
            System.Windows.Forms.MessageBox.Show(
                "cs-wd 已在运行（右下角托盘图标）。\n双击托盘图标或按热键唤起。",
                "cs-wd", System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Information);
            Shutdown();
            return;
        }

        _settings = AppSettings.Load();
        ThemeService.Apply(_settings.Theme);
        // 拉起 daemon（幂等：daemon 自带单实例探测）
        DaemonClient.EnsureDaemon();

        var win = new MainWindow();
        _mainWin = win;
        win.ShowAndFocus();
        win.ApplyHotkey(_settings);

        // 托盘常驻
        _tray = new TrayService(_settings.Hotkey);
        _tray.ShowRequested += win.ShowAndFocus;
        _tray.JobsRequested += OpenJobs;
        _tray.SettingsRequested += OpenSettings;
        _tray.ExitRequested += ExitApp;
    }

    private JobsWindow? _jobsWin;

    private void OpenJobs()
    {
        if (_jobsWin != null)
        {
            _jobsWin.Activate();
            return;
        }
        _jobsWin = new JobsWindow();
        _jobsWin.Closed += (_, _) => _jobsWin = null;
        _jobsWin.Show();
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
            ThemeService.Apply(s.Theme);
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

    internal static void LogCrash(object exception)
    {
        try
        {
            var dir = System.IO.Path.Combine(AppSettings.DataDir, "logs");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(dir, $"app-{DateTime.Now:yyyyMMdd}.log"),
                $"\n===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\n{exception}\n");
        }
        catch { /* 日志写不进去就算了，别再抛 */ }
    }
}
