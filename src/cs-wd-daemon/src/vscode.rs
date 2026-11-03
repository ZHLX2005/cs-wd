//! VSCode 最近打开的项目（目录）。
//!
//! 数据源（按优先级探测）：
//! 1. `%APPDATA%/Code/User/globalStorage/state.vscdb`（SQLite，key=`history.recentlyOpenedPathsList`）
//! 2. `%APPDATA%/Code - Insiders/...` 同上
//! 3. `%APPDATA%/Code/User/globalStorage/storage.json`（旧版 JSON，递归收集 file:/// 值）
//!
//! 60 秒内存缓存：搜索框每敲一个字符都会 search，不能每次都开 db。

use std::path::PathBuf;
use std::sync::Mutex;
use std::time::{Duration, Instant};

use serde_json::Value;

pub struct VscodeRecent {
    cache: Mutex<Option<(Instant, Vec<String>)>>,
}

const TTL: Duration = Duration::from_secs(60);
const DB_KEY: &str = "history.recentlyOpenedPathsList";

impl VscodeRecent {
    pub fn new() -> Self {
        VscodeRecent { cache: Mutex::new(None) }
    }

    /// 最近打开的目录列表（已过滤存在的目录，按 VSCode 的最近顺序）
    pub fn recent(&self) -> Vec<String> {
        let mut guard = match self.cache.lock() {
            Ok(g) => g,
            Err(_) => return Vec::new(),
        };
        if let Some((at, list)) = guard.as_ref() {
            if at.elapsed() < TTL {
                return list.clone();
            }
        }
        let list = Self::read().unwrap_or_default();
        *guard = Some((Instant::now(), list.clone()));
        list
    }

    fn read() -> anyhow::Result<Vec<String>> {
        let appdata = std::env::var("APPDATA").unwrap_or_default();
        if appdata.is_empty() {
            return Ok(Vec::new());
        }
        let base = PathBuf::from(&appdata);
        for dir in ["Code", "Code - Insiders"] {
            let db = base.join(dir).join("User/globalStorage/state.vscdb");
            if db.exists() {
                if let Ok(v) = Self::read_db(&db) {
                    return Ok(Self::parse_entries(&v));
                }
            }
        }
        let legacy = base.join("Code/User/globalStorage/storage.json");
        if legacy.exists() {
            if let Ok(v) = Self::read_json(&legacy) {
                return Ok(Self::parse_entries(&v));
            }
        }
        Ok(Vec::new())
    }

    fn read_db(path: &PathBuf) -> anyhow::Result<Value> {
        let conn = rusqlite::Connection::open_with_flags(
            path,
            rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY,
        )?;
        let raw: String = conn.query_row(
            "SELECT value FROM ItemTable WHERE key = ?1",
            [DB_KEY],
            |r| r.get(0),
        )?;
        Ok(serde_json::from_str(&raw)?)
    }

    fn read_json(path: &PathBuf) -> anyhow::Result<Value> {
        let raw = std::fs::read_to_string(path)?;
        Ok(serde_json::from_str(&raw)?)
    }

    /// 从 recentlyOpenedPathsList JSON 提取目录：folderUri → 本地路径，过滤不存在的
    fn parse_entries(v: &Value) -> Vec<String> {
        let mut out = Vec::new();
        if let Some(entries) = v.get("entries").and_then(|e| e.as_array()) {
            for e in entries {
                if let Some(uri) = e.get("folderUri").and_then(|u| u.as_str()) {
                    if let Some(p) = file_uri_to_path(uri) {
                        if std::path::Path::new(&p).is_dir() && !out.contains(&p) {
                            out.push(p);
                        }
                    }
                }
            }
        }
        out
    }
}

/// `file:///D%3A/a_js/proj` → `D:\a_js\proj`（手写 percent-decode，不引 url crate）
fn file_uri_to_path(uri: &str) -> Option<String> {
    let rest = uri.strip_prefix("file:///")?;
    let decoded = percent_decode(rest);
    if decoded.is_empty() {
        return None;
    }
    // 去掉尾部斜杠、截断 query/fragment
    let decoded = decoded.split(['?', '#']).next().unwrap_or(&decoded);
    let decoded = decoded.trim_end_matches('/');
    if decoded.is_empty() {
        return None;
    }
    Some(decoded.replace('/', "\\"))
}

fn percent_decode(s: &str) -> String {
    let bytes = s.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'%' && i + 2 < bytes.len() {
            if let Ok(b) = u8::from_str_radix(&s[i + 1..i + 3], 16) {
                out.push(b);
                i += 3;
                continue;
            }
        }
        out.push(bytes[i]);
        i += 1;
    }
    String::from_utf8_lossy(&out).into_owned()
}
