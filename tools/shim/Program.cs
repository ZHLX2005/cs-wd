using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

// cs-wd 启动垫片：
//   让 Windows 开始菜单 / Win+S 能索引到一个稳定的 `cs-wd.exe`。
//   真正的应用在仓库发布目录，且依赖同目录的 .dll / .runtimeconfig.json；
//   把那些文件摊进开始菜单既脏又容易坏，所以这里只做一次转发。
//
//   目标路径不硬编码：从 HKCU\...\App Paths\cs-wd.exe 的默认值读。
//   这样垫片放到任何目录都有效，主程序换位置也只改注册表一处。
//   WinExe 子系统 → 不弹控制台；framework-dependent 单文件 → 依赖系统 .NET 8。
internal static class Program
{
    private const string AppPathsKey =
        @"Software\Microsoft\Windows\CurrentVersion\App Paths\cs-wd.exe";

    private static void Main()
    {
        var target = Registry.CurrentUser.OpenSubKey(AppPathsKey)?.GetValue("") as string;

        if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
        {
            // 无控制台窗口，错误落到临时文件，便于诊断而不是静默无反应
            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "cs-wd-shim-error.txt"),
                "cs-wd shim 找不到主程序。\n注册表: HKCU\\" + AppPathsKey +
                "\n解析到: " + (target ?? "(null)") +
                "\n请重新注册 App Paths 或重建 cs-wd。");
            return;
        }

        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
