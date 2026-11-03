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

## 参考

- [microsoft/PowerToys](https://github.com/microsoft/PowerToys) —— 搜索框 UI/UX 原型
- [openai/codex](https://github.com/openai/codex) —— Rust 侧进程/后台任务管理
- 兄弟仓库：`nx-drawer`（被替代的前代）
