//! 目录历史：记录每次 run 的工作目录，支持子串过滤、按频次排序。

use std::collections::HashMap;
use std::path::PathBuf;

use anyhow::Context;
use serde_json::json;

use crate::protocol::HistoryEntry;

pub struct History {
    file: PathBuf,
    /// path → entry
    map: HashMap<String, HistoryEntry>,
}

fn now_ms() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

impl History {
    pub fn load(dir: &std::path::Path) -> anyhow::Result<Self> {
        std::fs::create_dir_all(dir).context("create data dir")?;
        let file = dir.join("history.json");
        let map = if file.exists() {
            let raw = std::fs::read_to_string(&file)?;
            let list: Vec<HistoryEntry> = serde_json::from_str(&raw).unwrap_or_default();
            list.into_iter().map(|e| (e.path.clone(), e)).collect()
        } else {
            HashMap::new()
        };
        Ok(History { file, map })
    }

    /// 记录一次工作目录使用
    pub fn record(&mut self, path: &str) {
        let e = self.map.entry(path.to_string()).or_insert_with(|| HistoryEntry {
            path: path.to_string(),
            count: 0,
            last_used_ms: 0,
        });
        e.count += 1;
        e.last_used_ms = now_ms();
        self.flush();
    }

    /// 子串过滤，按 count 降序取前 n
    pub fn search(&self, query: &str, n: usize) -> Vec<HistoryEntry> {
        let q = query.to_lowercase();
        let mut items: Vec<HistoryEntry> = self
            .map
            .values()
            .filter(|e| q.is_empty() || e.path.to_lowercase().contains(&q))
            .cloned()
            .collect();
        items.sort_by(|a, b| b.count.cmp(&a.count).then(b.last_used_ms.cmp(&a.last_used_ms)));
        items.truncate(n);
        items
    }

    /// 最近使用的目录（run 未指定 cwd 时用）
    pub fn most_recent(&self) -> Option<String> {
        self.map
            .values()
            .max_by_key(|e| e.last_used_ms)
            .map(|e| e.path.clone())
    }

    fn flush(&self) {
        let mut list: Vec<&HistoryEntry> = self.map.values().collect();
        list.sort_by(|a, b| b.last_used_ms.cmp(&a.last_used_ms));
        if let Ok(s) = serde_json::to_string_pretty(&list) {
            let _ = std::fs::write(&self.file, s);
        }
    }

    pub fn to_json(&self, n: usize) -> serde_json::Value {
        json!({ "items": self.search("", n) })
    }
}
