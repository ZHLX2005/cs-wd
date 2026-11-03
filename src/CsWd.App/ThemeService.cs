using System.Windows;

namespace CsWd;

/// <summary>深/浅主题切换：交给 WPF-UI 的 ApplicationThemeManager（PowerToys 同款机制）。</summary>
public static class ThemeService
{
    public const string Dark = "dark";
    public const string Light = "light";

    public static void Apply(string? name)
    {
        var theme = name == Light
            ? Wpf.Ui.Appearance.ApplicationTheme.Light
            : Wpf.Ui.Appearance.ApplicationTheme.Dark;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(theme);
    }
}
