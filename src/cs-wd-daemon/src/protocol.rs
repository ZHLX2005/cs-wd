//! 协议定义：named pipe 上的 JSON Lines 请求/响应。
//!
//! 请求用 serde 内部 tag 区分 op；响应统一 { ok, data, error }。

use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize)]
#[serde(tag = "op", rename_all = "snake_case")]
pub enum Request {
    Ping,
    /// 搜索：目录历史 + VSCode 最近 + br-tb 数据湖链接；返回结果带 kind
    Search { query: String },
    /// 后台执行命令（可选 cwd；省略时 daemon 用最近目录）
    Run { cmd: String, cwd: Option<String> },
    /// 直接记录一次目录使用（如打开交互终端时）
    Record { path: String },
    /// 列出后台任务
    Jobs,
    /// 结束任务
    Kill { id: u64 },
    /// 读取任务日志尾部
    Log { id: u64, tail: Option<usize> },
    /// 从任务表清除一条已退出的任务（不动日志文件）
    Forget { id: u64 },
}

#[derive(Debug, Serialize)]
pub struct Response {
    pub ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub data: Option<serde_json::Value>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
}

impl Response {
    pub fn ok(data: serde_json::Value) -> Self {
        Response { ok: true, data: Some(data), error: None }
    }
    pub fn err<E: Into<String>>(msg: E) -> Self {
        Response { ok: false, data: None, error: Some(msg.into()) }
    }
}

/// 目录历史条目（存 history.json，也作为 search 结果返回）
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HistoryEntry {
    pub path: String,
    pub count: u32,
    pub last_used_ms: u64,
}
