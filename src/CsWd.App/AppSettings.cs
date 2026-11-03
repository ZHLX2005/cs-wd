using System.IO;
using Microsoft.Win32;

namespace CsWd;

/// <summary>
/// 应用设置：%LOCALAPPDATA%\cs-wd\settings.json。
/// 热键组合 + 开机自启（HKCU Run 键）。
/// </summary>
public class AppSettings
{
    public string Hotkey { get; set; } = "Alt+Space";
    public bool AutoStart { get; set; }

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cs-wd");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new AppSettings();
        }
        catch { /* 损坏就用默认值 */ }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(this, JsonOpts));
        ApplyAutoStart();
    }

    /// <summary>按当前 AutoStart 同步 HKCU Run 键。</summary>
    public void ApplyAutoStart()
    {
        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
            if (k is null) return;
            if (AutoStart)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) k.SetValue("CsWd", $"\"{exe}\"");
            }
            else
            {
                k.DeleteValue("CsWd", throwOnMissingValue: false);
            }
        }
        catch { /* 注册表写不进就忽略 */ }
    }
}
