# QUICKGET —— 从浏览器 tab 到键盘直达

> 跨三个仓库的链路设计：`br-tb`（Chrome 扩展，数据源） → `nx-tb`（native host，初始化 + 落盘） → `cs-wd`（键盘启动器，搜索 + 打开）。
> 本文只描述设计与契约，不含实现细节。当前状态见第 8 节。

## 1. 目标

在 cs-wd 的搜索框里输入几个字，就能命中浏览器里攒下的链接——分组收藏、时间线快照、会话录制里出现过的页面都算——回车用**默认浏览器**打开。

一句话概括职责：**浏览器负责攒，本地负责存，键盘负责找。**

## 2. 边界（明确不做什么）

- cs-wd **不写**任何浏览器数据，只读；不保存副本、不做索引缓存。
- cs-wd **不调用**浏览器历史 API，只用扩展自己攒下来的数据（用户可见、可管理）。
- nx-tb 只在**初始化**和**落盘**两个动作里出场，运行期不参与查询。
- 数据的唯一真相是 br-tb 的 `chrome.storage.local` 与本地数据湖；两者由同一套 backup 动作保持一致。
- 不做云同步，不跨设备。

## 3. 参与方

| 仓库 | 本地路径 | 角色 | 不该做的事 |
| --- | --- | --- | --- |
| **br-tb** | `D:\a_js\js_proj\br_controller\bro_chat_testfeature__`（远程 `ZHLX2005/br_tb`，分支 master） | 数据源。MV3 扩展，纯 JS 无构建 | 不做搜索 UI，不关心谁消费 |
| **nx-tb** | `D:\a_js\js_proj\nx-tb`（远程 `On-DevPlan/nx-tb`） | native messaging 宿主 + 数据湖写入；`tb init` 负责注册 | 不做搜索，不解释业务语义 |
| **cs-wd** | `D:\a_js\js_proj\cs-wd`（远程 `ZHLX2005/cs-wd`） | 消费端。WPF 薄壳 + Rust daemon | 不存数据，不改数据 |

## 4. 数据来源

浏览器侧一共有**四类**可搜的东西。前三类是数据源，第四类只是灾备。

### 4.1 分组收藏（当前 tabs）

用户主动整理过的链接，信号最强。

```json
{ "id": "g_xxx", "name": "📄 面包", "color": "#f9ca24", "isDefault": true,
  "goto": false, "inFocusSearch": false, "visible": true, "ns": "default" }
```

tabs 按 groupId 分桶：`{ "g_xxx": [ { "id": "...", "title": "...", "url": "...", "favicon": "" } ] }`

- 语义：**我明确收藏的**。搜索时应给最高权重。
- **`inFocusSearch` 是扩展内 focusSearch 的硬过滤开关**，但 cs-wd 只把它当**加权信号（+2）**、不做硬过滤。原因：实测本机 8 个分组里 `inFocusSearch` 全为 `false`（用户没用过浏览器内那个搜索浮层），硬过滤会让「收藏分组」这一路完全空转。分组收藏按现有分组照常参与，勾选过的排更前。
- 存在多命名空间 `ns`（默认 `default`），跨 ns 的同名分组要区分。

### 4.2 时间线快照（timelineSnapshots）

用户手动「收集当前窗口」时打的一个包，一次收集 = 一条快照。

```json
{ "id": "s_xxx", "timestamp": "2026-11-04T02:10:00.000Z",
  "tabs": [ { "id": "...", "title": "...", "url": "...", "favicon": "" } ] }
```

- 语义：**某时刻我桌上摊开的那一堆页面**。有上下文价值（同一快照里的链接彼此相关）。
- 容量：快照数组按**总标签数上限 1000** 裁剪，超出时从最旧的快照开始丢。
- 时间只有快照级别的，包内每个 tab 没有独立时间戳。

### 4.3 会话录制（recordings）

打开录制模式后，浏览过程中**自动**捕获的页面（`chrome.tabs.onUpdated`）。

```json
{ "id": "r_xxx", "name": "录制 11-04 10:30", "startTime": "...", "endTime": null,
  "tabs": [ { "id": "...", "title": "...", "url": "...", "favicon": "",
              "timestamp": "2026-11-04T02:31:00.000Z" } ] }
```

- 语义：**我最近实际走过的地方**，带逐条时间戳，最能反映"当下在干什么"。
- 规则：同一录制内按 url 去重（只留首次），每个录制最多 100 条，新的 `unshift` 到前面。
- 这是「历史 tab」里质量最高的一路——有时间、有去重、有会话归属。

### 4.4 备份 JSON 文件（不是搜索源）

扩展里「导出全部数据」下载的 `tabboard-backup-*.json` 是**整库快照**，用于换机/回滚。cs-wd 不需要读它，只在数据湖损坏时作为重建输入。

### 4.5 关于「历史 tab」的判断

历史 tab（4.2 / 4.3）与分组收藏有本质区别，不能同等对待：

| | 分组收藏 | 时间线快照 | 会话录制 |
| --- | --- | --- | --- |
| 用户意图 | 显式收藏 | 一次性打包 | 被动记录 |
| 噪声 | 低 | 中（整窗收集，含无关页） | 高（什么都记） |
| 时间精度 | 无 | 快照级 | 逐条 |
| 重复度 | 低 | 高（同一页反复收集） | 中（录制内已去重） |

所以历史 tab **是一等来源，但要降权**——见第 6 节。

## 5. 落盘：本地数据湖

### 5.1 谁写、写在哪

扩展里分组/标签发生变化 → `background/groups.js` 的 `tbSyncToLocal` → 把整个 storage 转成 `records` → `sendNativeMessage('com.tabboard.tb', { command: 'backup', records })` → Go host 按 kind 分桶**追加**写：

```
~/.tabboard/lake/groups.jsonl
~/.tabboard/lake/tabs.jsonl
~/.tabboard/lake/timelineSnapshots.jsonl
~/.tabboard/lake/recordings.jsonl
~/.tabboard/lake/settings.jsonl
...
```

- `kind` 就是 storage 的键名，只允许字母/数字/下划线。
- 目录可用 `TABBOARD_DIR` / `TABBOARD_LAKE_DIR` 覆盖。
- 每行是 `data` 对象本身，额外注入一个 `_ts`（写入时刻的 Unix 毫秒）。

### 5.2 扁平化规则

| storage 形态 | 落湖形态 |
| --- | --- |
| `tabs`（对象 `{groupId: [tab...]}`） | 拆成**每条 tab 一行**，并注入 `groupId` |
| 数组（groups / timelineSnapshots / recordings / …） | **每个元素一行**（快照内嵌 `tabs` 数组不展开） |
| 普通对象（settings / recordingState） | 一行 |

所以 `tabs.jsonl` 是扁平的（适合逐条搜索），而 `timelineSnapshots.jsonl` / `recordings.jsonl` 是**嵌套**的，消费端要自己展开一层。

单行示例：

```json
{"id":"t_1","title":"GitHub","url":"https://github.com","favicon":"","groupId":"g_xxx","_ts":1762220000000}
```

### 5.3 关键陷阱：append-only 会累积重复

lake 是纯追加写，而 backup 每次都是**全量** records。也就是说同一个 tab 每同步一次就多一行，`_ts` 不同、内容相同。

消费端读到的是「变更流水」，不是「当前状态」。**必须**在读取时收敛：

- 分组 / 标签：按 `id` 取 `_ts` 最大的一条（或按 `url` 去重，因为 tab id 每次导入会变）。
- 快照 / 录制：按记录 `id` 取最新。
- 已经被删除的分组或 tab，湖里**不会**出现删除标记——湖只增不减。

这条是整条链路里最容易踩的坑：不去重的话，搜「github」可能出来三十条一模一样的。

## 6. 消费端契约（cs-wd）

### 6.1 现状

- 协议：named pipe `cs-wd-daemon`，UTF-8 JSON Lines，一请求一连接。
- `op:search` 目前**只查** `%LOCALAPPDATA%\cs-wd\history.json`（目录历史），子串匹配 + 按 count 降序取前 30。
- 结果在 C# 侧映射成 `ResultKind.Directory`（📁）或 `RunCommand`（⚡）。

### 6.2 建议的统一结果模型

daemon 侧把多路结果归一成一个带 `kind` 的项，C# 只负责渲染：

```json
{ "kind": "link" | "directory" | "command",
  "title": "页面标题", "subtitle": "来源 · 分组名 · 时间",
  "url": "https://...",           // kind=link 时必有
  "path": "D:/...",               // kind=directory
  "cmd": "..." }                  // kind=command
```

- `link` 项丢弃 `favicon`：数据湖里的 favicon 是 `chrome://favicon/...` 形式，cs-wd 侧取不到，用统一图标即可。
- 点击行为：`Process.Start(url)` + `UseShellExecute = true`，走系统默认浏览器。

### 6.3 排序与去重（历史 tab 在这层被驯服）

用**两层**打分，别揉成一个数：

**第一层：匹配质量**（照抄 focusSearch 的梯度，见 6.4）

| 命中方式 | 分 |
| --- | --- |
| 标题完全相等 | 100 |
| 标题前缀 | 80 |
| 标题包含 | 60 |
| URL（host+path）包含 | 40 |
| 有序模糊匹配 | 20 |

**第二层：加成**（在上面的分上叠加，量级要小于一个匹配档位的差）

1. **硬去重**：先按 `url` 收敛，同 url 只留一条。
2. **来源权重**：分组收藏 > 会话录制 > 时间线快照（见 4.5 表）。分组收藏最高，历史 tab 依次降权。
3. **时间衰减**：录制/快照按时间给加成，越新越高；分组收藏没有时间，给固定基准。
4. **频次加成**：同一 url 在多个来源/多次快照里出现，说明常用，加分。
5. **空输入**：不做「历史全部列出来」，保持现有行为（最近目录）即可，避免一开框就被旧链接淹没。

### 6.4 参考实现：br-tb 的 focusSearch

扩展里已经有一个打磨过的搜索框（`Alt+Shift+S` 浮层），cs-wd 的链接搜索应当**直接沿用它的打分与过滤规则**，别另发明一套。代码在两处：

- `content/focus-search.js` —— 浮层 UI + 匹配打分（`matchTab` / `filterAndSortTabs` / `fuzzyMatchOrdered`）
- `background/focus.js` —— 搜索池怎么组（`handleGetAllOpenTabs`）

#### 搜索池（哪些能搜到）

```
当前打开的标签页  ∪  inFocusSearch === true 的分组里的链接
→ 排除 chrome: / chrome-extension: / edge://newtab
→ 按 url 去重（浏览器实况优先，因为它排在前面先占位）
```

条目带 `source: 'browser' | 'group'`，可用于加权或标注来源；`audible` 的标签额外 +200 分（正在放声音的排最前）。

> **注意池子不同**：focusSearch 搜的是「**当前开着** + 勾选分组」，cs-wd 搜的是「**历史沉淀**」。所以 cs-wd 天然没有 `audible`、没有实时标签，不能指望两边结果一致——但**打分梯度可以完全复用**。

#### 匹配梯度

```
标题 === q          → 100  exact
标题 startsWith q   →  80  prefix
标题 includes q     →  60  contains
host+path includes q→  40  url
有序子序列匹配       →  20  fuzzy
```

#### 两个值得照抄的细节

**1. 模糊匹配是有序子序列，且限制跨度**

```js
// 逐字符按序匹配；匹配到的首尾跨度必须 <= query 长度 × 2.5
var span = lastMatch - firstMatch + 1;
return span <= query.length * 2.5;
```

不是编辑距离，实现极便宜；跨度上限则挡住了「q 的三个字符散落在标题首中尾」这种假阳性。这条规则比单纯 `includes` 宽容、比编辑距离省事，很值得继承。

**2. URL 先归一化再匹配**

```js
var parsed = new URL(url);
return parsed.hostname + parsed.pathname;   // 丢掉协议、query、hash
```

否则 query 会命中 `?utm_source=...`、`#section` 之类的噪声。数据湖里存的是完整 URL，读侧也要先做这一步。

#### 不要照搬的部分：前缀模式冲突

focusSearch 用 `>` 开系统页（`chrome://` 一类）、`~` 开 localhost（纯数字直接拼 `http://localhost:<端口>/`，另有 10 条历史队列）。

而 **cs-wd 已经用 `>` 表示「后台执行命令」**。同一个符号两套语义会打架，所以 cs-wd 保留自己的 `>`，不引入 focusSearch 的 `>`；`~` 若要接，得单独定义，别默默生效。

## 7. 初始化（一次性）

```
cs-wd 里跑：npx nx-tb tb init
```

它做三件事：定位 native host 可执行文件 → 写 `~/.tabboard/host.manifest.json`（host 名 `com.tabboard.tb`，allowed_origins 指向扩展）→ 把 manifest 路径写进 **HKCU** 的 Chrome 与 Edge `NativeMessagingHosts\com.tabboard.tb`（无需管理员）。

前置条件只有一条：host 二进制存在。两条路——用 npm 上带二进制的 `nx-tb` 包，或本地 `cd native-host && go build -o tabboard-host.exe .`。

## 8. 实现状态（链路已打通）

| 项 | 状态 |
| --- | --- |
| br-tb 的 `nativeMessaging` + 自动 backup | ✅ |
| native host 二进制 + `tb init`（HKCU Chrome/Edge 注册） | ✅ `~/.tabboard/bin/tabboard-host.exe` |
| 数据湖 | ✅ 26 个 jsonl；`tabs` 75 行、快照 40 条（965 个 tab）、录制 12 条（157 个 tab） |
| Rust daemon 读湖 + 分级打分 | ✅ `src/cs-wd-daemon/src/lake.rs` |
| C# `ResultKind.Link` + 默认浏览器打开 | ✅ |

**实测**（named pipe `{"op":"search","query":"github"}`）：返回 30 条 link，`GitHub` 以 107 分排首；`bilibili` / `notion` 分别 30 / 17 条，来源标签（收藏 / 录制 / 快照）与分组名都正确。

实现落点：

- `cs-wd-daemon/src/lake.rs` —— 白名单读取、按 host+path 收敛、展开嵌套 tabs、两层打分
- `cs-wd-daemon/src/pipe.rs` —— `Search` 返回带 `kind` 的统一 items（目录 20 + 链接 30）
- `CsWd.App/ResultItem.cs` / `MainWindow.xaml.cs` —— 渲染 Link、回车 `UseShellExecute`

两个构建/部署注意：

1. daemon 需要 MSVC 链接器（VS Build Tools 2022 的「使用 C++ 的桌面开发」），本机已装。
2. `DaemonClient.EnsureDaemon()` **优先**启动 `%LOCALAPPDATA%\cs-wd\cs-wd-daemon.exe`，仓库 `bin/` 只是兜底。重新构建后**两处都要替换**，否则 App 仍在跑旧 daemon。两处旧 exe 都留了 `.bak-20261104`。

## 9. 名词

- **数据湖 / lake**：`~/.tabboard/lake/` 下按 kind 分的 append-only JSONL 文件。
- **kind**：lake 的分文件维度，等于 chrome.storage 的键名。
- **backup**：扩展把全量 storage 推给 host 的一次动作，不是"备份文件"。
- **ns**：分组命名空间，默认 `default`，用于隔离不同场景的分组集合。
