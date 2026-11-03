using System.Windows;

namespace CsWd;

/// <summary>深/浅主题切换：交给 WPF-UI 的 ApplicationThemeManager（PowerToys 同款机制）。</summary>
public static class ThemeService
{
    public const string Dark = "dark";
    public const string Light = "light";

    public static void Apply(string? name)
    {
        try
        {
            var theme = name == Light
                ? Wpf.Ui.Appearance.ApplicationTheme.Light
                : Wpf.Ui.Appearance.ApplicationTheme.Dark;
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(theme);

            // 补齐 wpfui 没有的个别 brush key。
            // 注意：不能用 <SolidColorBrush Color="{DynamicResource ...}"/>——
            // 资源字典里的 DR 延迟求值会在渲染线程（DUCE 同步）抛
            // "#XXXXXX 不是 Color 的有效值" 并直接杀进程（已验证的崩溃）。
            // 改为切主题后用代码创建 frozen brush。
            var res = System.Windows.Application.Current.Resources;
            res["TextControlPlaceholderForegroundBrush"] = MakeFrozenBrush("TextControlPlaceholderForeground");
            res["AccentBrush"] = MakeFrozenBrush("SystemAccentColorPrimary");
        }
        catch (Exception ex)
        {
            App.LogCrash(ex); // 主题失败不该拖死启动
        }
    }

    private static System.Windows.Media.SolidColorBrush MakeFrozenBrush(string key)
    {
        var brush = System.Windows.Application.Current.Resources[key] switch
        {
            System.Windows.Media.SolidColorBrush sb => new System.Windows.Media.SolidColorBrush(sb.Color),
            System.Windows.Media.Color c => new System.Windows.Media.SolidColorBrush(c),
            _ => System.Windows.Media.Brushes.Gray.Clone(),
        };
        brush.Freeze();
        return brush;
    }
}
