using System.Windows;

namespace CsWd;

/// <summary>深/浅主题切换：整体替换 MergedDictionaries 顶层字典。</summary>
public static class ThemeService
{
    public const string Dark = "dark";
    public const string Light = "light";

    public static void Apply(string? name)
    {
        var file = name == Light ? "Light" : "Dark";
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/CsWd.App;component/Themes/{file}.xaml"),
        };
        var md = System.Windows.Application.Current.Resources.MergedDictionaries;
        md.Clear();
        md.Add(dict);
    }
}
