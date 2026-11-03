using System.Runtime.InteropServices;

namespace CsWd;

/// <summary>
/// 全局热键（默认 Alt+Space）。注册在隐藏消息窗口上，WM_HOTKEY 时回调。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    public const int WmHotkey = 0x0312;
    private const int ModAlt = 0x0001;
    private const int VkSpace = 0x20;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _hwnd;

    public event Action? Pressed;

    public HotkeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        if (!RegisterHotKey(_hwnd, 1, ModAlt, VkSpace))
            throw new InvalidOperationException("RegisterHotKey(Alt+Space) 失败，可能被其他程序占用");
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
