# cs-wd

轻量的 Windows 键盘驱动启动器 —— PowerToys Run 的交互体验，极度克制的内存占用。

## 定位

- **键盘驱动**：全局快捷键（默认 `Alt+Space`）唤起搜索框，全程不碰鼠标。替代前代 `nx-drawer`（鼠标驱动，不程序员）。
- **UI/UX 对齐 PowerToys Run**：顶部搜索框 + 结果列表，深色，无边框置顶，失焦即隐藏。
- **轻量**：WPF 只做一层薄壳；重活全部交给 Rust daemon。不装 .NET 插件宿主，不做插件系统。

## 功能（MVP）

| 功能 | 说明 |
| --- | --- |
| 搜索历史目录 | daemon 记录每次执行命令时的工作目录，子串过滤 + 按使用频次排序 |
| 执行命令 → 后台 | 输入 `> <命令>` 回车，daemon spawn 子进程（无窗口），stdout/stderr 落盘日志 |
| 日志留存 | `%LOCALAPPDATA%\cs-wd\logs\<jobId>.log`，按 jobId 可查询 |
| 任务管理 | `jobs` / `kill <id>`（参考 openai/codex 的 codex-rs exec 模块） |
| 浏览器链接搜索 | 读 br-tb 的本地数据湖（分组收藏 / 时间线快照 / 会话录制），模糊匹配后回车用默认浏览器打开。链路契约见 `docs/QUICKGET.md` |

## 架构

```
┌──────────────────┐  named pipe (JSON Lines)  ┌──────────────────────┐
│  CsWd.App (WPF)  │ ────────────────────────► │  cs-wd-daemon (Rust) │
│  热键 / 搜索框UI  │ \\.\pipe\cs-wd-daemon     │  exec / history /    │
│  内存占用 ~60MB  │                           │  job 日志 / kill     │
└──────────────────┘                           └──────────────────────┘
```

- 前端：`src/CsWd.App`（.NET 8 WPF）
- 后端：`src/cs-wd-daemon`（Rust + tokio）
- 协议：`docs/DESIGN.md`

## 构建

```bash
dotnet build src/CsWd.App -c Release
cargo build --release -p cs-wd-daemon --manifest-path src/cs-wd-daemon/Cargo.toml
```

## 启动 / Windows 搜索

主程序在 `src/CsWd.App/bin/Release/net8.0-windows/CsWd.App.exe`。App 会自己拉起 daemon：`DaemonClient.EnsureDaemon()` **优先**找 `%LOCALAPPDATA%\cs-wd\cs-wd-daemon.exe`，找不到才回退到仓库 `bin/`，所以重编 daemon 后两处都要替换。

为了能从 **Win+S / 开始菜单** 搜到，另注册了两处：

| 位置 | 作用 |
| --- | --- |
| `%APPDATA%\Microsoft\Windows\Start Menu\Programs\cs-wd.exe` | 开始菜单 / Win+S 搜 `cs-wd` 可命中 |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\cs-wd.exe` | 默认值 = 主程序路径；`Win+R` 输 `cs-wd` 直接启动 |

开始菜单里的 `cs-wd.exe` 是约 150 KB 的**转发垫片**（源码 `tools/shim/`），不是主程序本体：它读 App Paths 注册表拿到目标路径再 `Process.Start`，因此主程序换位置只需改注册表一处，也不用把一堆 `.dll` 摊进开始菜单。WinExe 子系统，不弹控制台。

重建垫片：

```bash
cd tools/shim
dotnet publish -c Release -o publish
cp publish/cs-wd.exe "$APPDATA/Microsoft/Windows/Start Menu/Programs/cs-wd.exe"
```

Windows 搜索索引新文件有延迟；刚部署完先用 `Win+R` → `cs-wd` 验证，或等几分钟 / 注销重登。

## 参考

- [microsoft/PowerToys](https://github.com/microsoft/PowerToys) —— 搜索框 UI/UX 原型
- [openai/codex](https://github.com/openai/codex) —— Rust 侧进程/后台任务管理
- 兄弟仓库：`nx-drawer`（被替代的前代）
