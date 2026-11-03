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
    public const uint ModWin = 0x0008;

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

/// <summary>
/// 热键字符串 ↔ (modifiers, vk)。格式 "Ctrl+Alt+D"：末段=主键（WPF Key 枚举名，如 Space/D/Oem3），
/// 其余为修饰键。自定义录制产生的任意组合都可解析。
/// </summary>
public static class HotkeyPresets
{
    public static bool TryParse(string s, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split('+');
        var keyName = parts[^1].Trim();
        if (!Enum.TryParse(keyName, ignoreCase: true, out System.Windows.Input.Key key)) return false;
        if (IsModifier(key)) return false;
        vk = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        foreach (var p in parts[..^1])
        {
            mods = p.Trim().ToUpperInvariant() switch
            {
                "ALT" => mods | HotkeyService.ModAlt,
                "CTRL" or "CONTROL" => mods | HotkeyService.ModControl,
                "SHIFT" => mods | HotkeyService.ModShift,
                "WIN" => mods | HotkeyService.ModWin,
                _ => mods,
            };
        }
        return mods != 0;
    }

    public static bool IsModifier(System.Windows.Input.Key k) => k is
        System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or
        System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt or
        System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or
        System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin;

    /// <summary>录制结果 → 标准字符串（按 Ctrl/Alt/Shift/Win 顺序）。</summary>
    public static string Format(System.Windows.Input.ModifierKeys m, System.Windows.Input.Key k)
    {
        var seg = new List<string>(4);
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Control)) seg.Add("Ctrl");
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Alt)) seg.Add("Alt");
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Shift)) seg.Add("Shift");
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Windows)) seg.Add("Win");
        seg.Add(k.ToString());
        return string.Join('+', seg);
    }
}
