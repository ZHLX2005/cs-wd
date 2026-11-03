using System.Runtime.InteropServices;

namespace CsWd;

/// <summary>
/// 全局热键。注册在隐藏消息窗口上，WM_HOTKEY 时回调。
/// 支持设置页里更换组合（Dispose 后重新 new）。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    public const int WmHotkey = 0x0312;

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _hwnd;

    public event Action? Pressed;

    public HotkeyService(IntPtr hwnd, uint modifiers, uint virtualKey)
    {
        _hwnd = hwnd;
        if (!RegisterHotKey(_hwnd, 1, modifiers, virtualKey))
            throw new InvalidOperationException($"RegisterHotKey 失败（错误码 {Marshal.GetLastWin32Error()}），组合可能被其他程序占用");
    }

    public IntPtr HookWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == 1)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose() => UnregisterHotKey(_hwnd, 1);
}

/// <summary>热键预设：字符串 ↔ (modifiers, vk)。</summary>
public static class HotkeyPresets
{
    // 顺序即设置页下拉顺序
    public static readonly string[] All =
    [
        "Alt+Space", "Ctrl+Space", "Alt+D", "Alt+`", "Ctrl+Alt+Space", "Ctrl+Alt+W",
    ];

    public static bool TryParse(string s, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        var parts = s.Split('+');
        if (parts.Length == 0) return false;
        var key = parts[^1].Trim().ToUpperInvariant();
        vk = key switch
        {
            "SPACE" => 0x20,
            "D" => 0x44,
            "W" => 0x57,
            "`" or "~" => 0xC0,
            _ => 0,
        };
        if (vk == 0) return false;
        foreach (var p in parts[..^1])
        {
            mods = p.Trim().ToUpperInvariant() switch
            {
                "ALT" => mods | HotkeyService.ModAlt,
                "CTRL" or "CONTROL" => mods | HotkeyService.ModControl,
                "SHIFT" => mods | HotkeyService.ModShift,
                _ => mods,
            };
        }
        return mods != 0;
    }
}
