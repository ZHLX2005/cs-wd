# cs-wd 架构设计

## 1. 组成

| 组件 | 技术 | 职责 |
| --- | --- | --- |
| `src/CsWd.App` | C# / WPF / .NET 8 | 全局热键、搜索框 UI、结果列表、named pipe 客户端 |
| `src/cs-wd-daemon` | Rust / tokio | named pipe 服务端、命令执行（后台 job）、目录历史、日志、进程终止 |

数据目录：`%LOCALAPPDATA%\cs-wd\`
- `history.json` —— 目录历史 `[{ path, count, lastUsedMs }]`
- `logs/<jobId>.log` —— 每个 job 的合并 stdout/stderr 流

## 2. 协议（named pipe `cs-wd-daemon`，UTF-8 JSON Lines）

请求（一行一个对象）：

```json
{"op":"ping"}
{"op":"search","query":"drawer"}
{"op":"run","cmd":"git status","cwd":"D:/a_js/js_proj"}
{"op":"jobs"}
{"op":"kill","id":3}
{"op":"log","id":3,"tail":200}
```

响应（一行一个对象）：

```json
{"ok":true,"data":{"version":"0.1.0"}}
{"ok":true,"data":{"items":[{"path":"D:/a_js/js_proj/nx-drawer","count":12,"lastUsedMs":1762000000000}]}}
{"ok":true,"data":{"job":{"id":3,"cmd":"git status","pid":4321}}}
{"ok":false,"error":"job 3 not found"}
```

- 每个 App 请求新建一条 pipe 连接，请求-响应后关闭（简单可靠，MVP 不做长连接）。
- daemon 侧 `op` 用 `serde(tag="op")` 解出；未知 op 返回 `{"ok":false,"error":"unknown op"}`。

## 3. 交互（对齐 PowerToys Run）

- `Alt+Space` 全局热键唤起/隐藏（`RegisterHotKey`，后续做成可配置）。
- 窗口：宽 720px，无边框圆角，激活屏幕水平居中、垂直约 1/4 处，Topmost、不进任务栏。
- 失焦自动隐藏；`Esc` 隐藏；`↑/↓` 选择；`Enter` 执行当前项。
- 结果项：左侧 emoji/字符图标，主标题（白），副标题（灰、小号）。
- 输入语义：
  - 空输入 → 最近使用的目录列表；
  - 普通文本 → 目录历史子串过滤；
  - `> <cmd>` → 直接后台执行该命令（cwd = 最近目录）。

## 4. daemon 内部

- `exec.rs`：`tokio::process::Command` + `CREATE_NO_WINDOW(0x08000000)`；job 表 `HashMap<u64, JobEntry>`；stdout/stderr 合并写 `%LOCALAPPDATA%\cs-wd\logs\<id>.log`；进程退出后异步回收，防僵尸。
- `history.rs`：每次 `run` 记录 cwd（count+1，lastUsed 更新），写回 JSON；查询=子串匹配按 `count` 降序取前 N。
- `pipe.rs`：tokio `named_pipe` ServerOptions 循环 accept；每连接 spawn task 读行-处理-回写。

## 5. 参考仓库

- **PowerToys**（`D:\a_other\nxkvrepo\powertoys`）：`src/modules/launcher/PowerLauncher` —— 搜索框布局、热键、结果项视觉。
- **codex**（`D:\a_other\nxkvrepo\codex`）：`codex-rs/exec`、`codex-rs/core` 的 spawn/中断/日志策略。

## 6. 内存预算

PowerToys Run 常驻 ~300MB+。cs-wd 预算：App ≈ 60-80MB（WPF 基线）、daemon ≈ 5MB。不做插件加载、不用 WebView、结果列表不虚拟化（MVP ≤ 50 项）。
