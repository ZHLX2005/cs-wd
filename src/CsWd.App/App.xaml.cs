using System.Windows;

namespace CsWd;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 尝试拉起 daemon（幂等：daemon 自带单实例探测）
        DaemonClient.EnsureDaemon();
        var win = new MainWindow();
        win.ShowAndFocus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }
}
