using System.Drawing;
using System.Windows.Forms;

namespace CsWd;

/// <summary>
/// 系统托盘：常驻图标 + 右键菜单（唤起 / 设置 / 退出）+ 双击唤起。
/// 图标在内存绘制，不依赖资产文件。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _showItem;

    public event Action? ShowRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayService(string hotkeyLabel)
    {
        _icon = new NotifyIcon
        {
            Text = $"cs-wd（{hotkeyLabel}）",
            Icon = CreateIcon(),
            Visible = true,
        };

        _showItem = new ToolStripMenuItem($"唤起（{hotkeyLabel}）");
        _showItem.Click += (_, _) => ShowRequested?.Invoke();

        var settingsItem = new ToolStripMenuItem("设置…");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _icon.ContextMenuStrip = new ContextMenuStrip();
        _icon.ContextMenuStrip.Items.Add(_showItem);
        _icon.ContextMenuStrip.Items.Add(settingsItem);
        _icon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _icon.ContextMenuStrip.Items.Add(exitItem);
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    /// <summary>设置页改热键后同步菜单文案。</summary>
    public void UpdateHotkeyLabel(string hotkey)
    {
        _showItem.Text = $"唤起（{hotkey}）";
        _icon.Text = $"cs-wd（{hotkey}）";
    }

    /// <summary>内存里画一枚深底蓝条的启动器图标。</summary>
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.Clear(Color.FromArgb(31, 31, 31));
            // 搜索框轮廓
            using var pen = new Pen(Color.FromArgb(76, 194, 255), 3);
            g.DrawRectangle(pen, 4, 5, 18, 10);
            g.DrawLine(pen, 22, 15, 28, 21);
            // 结果行
            using var gray = new Pen(Color.FromArgb(154, 154, 154), 3);
            g.DrawLine(gray, 5, 22, 24, 22);
            g.DrawLine(gray, 5, 28, 16, 28);
        }
        var hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        // 克隆一份，原位图句柄随 using 释放
        var clone = (Icon)icon.Clone();
        Native.DestroyIcon(hIcon);
        return clone;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static class Native
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}
