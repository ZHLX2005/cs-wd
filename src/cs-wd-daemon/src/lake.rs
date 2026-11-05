//! br-tb 数据湖搜索源（QUICKGET）。
//!
//! 设计契约见 `docs/QUICKGET.md`：
//! - 只读白名单里的 4 个 kind。**绝不遍历 lake 目录** —— 其它 jsonl 里
//!   有 noteUpload（邮箱/密码/token）等敏感设置，不能进搜索索引。
//! - lake 是 append-only 且每次 backup 全量重写，同一 url 会重复累积；
//!   读侧按 host+path 收敛，取 _ts 最新 / 来源最优的一条。
//! - 打分沿用 br-tb `content/focus-search.js` 的梯度（100/80/60/40/20），
//!   加成总量严格 < 20，保证匹配档位永远优先于来源/热度加成。

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use serde_json::Value;

/// 允许进入搜索索引的 kind（其余一律忽略，含敏感设置）
const ALLOWED: [&str; 4] = [
    "groups.jsonl",
    "tabs.jsonl",
    "timelineSnapshots.jsonl",
    "recordings.jsonl",
];

#[derive(Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Debug)]
enum Source {
    Snapshot = 0,
    Recording = 1,
    Group = 2,
}

impl Source {
    fn bit(self) -> u8 {
        match self {
            Source::Snapshot => 1,
            Source::Recording => 2,
            Source::Group => 4,
        }
    }
    fn bonus(self) -> i32 {
        match self {
            Source::Group => 6,
            Source::Recording => 3,
            Source::Snapshot => 0,
        }
    }
    fn label(self) -> &'static str {
        match self {
            Source::Group => "收藏",
            Source::Recording => "录制",
            Source::Snapshot => "快照",
        }
    }
}

#[derive(Clone)]
struct GroupInfo {
    name: String,
    focus: bool,
    ts: u64,
}

#[derive(Clone)]
struct Candidate {
    url: String,
    key: String,
    title: String,
    source: Source,
    sources: u8,
    focus: bool,
    visit: u32,
    day: Option<i64>,
    group: String,
    ts: u64,
}

pub struct LinkItem {
    pub title: String,
    pub url: String,
    pub subtitle: String,
    pub score: i32,
}

/// 对外入口：query 为空时不铺历史链接（保持 cs-wd 原有「最近目录」行为）。
pub fn search(query: &str, limit: usize) -> Vec<LinkItem> {
    let q = query.trim().to_lowercase();
    if q.is_empty() {
        return Vec::new();
    }

    let dir = lake_dir();
    let groups = load_groups(&dir);
    let mut map: HashMap<String, Candidate> = HashMap::new();

    collect_tabs(&dir, &groups, &mut map);
    collect_nested(&dir, "timelineSnapshots.jsonl", Source::Snapshot, &mut map);
    collect_nested(&dir, "recordings.jsonl", Source::Recording, &mut map);

    let today = today_days();
    let mut out: Vec<LinkItem> = map
        .into_values()
        .filter_map(|c| score_candidate(&c, &q, today))
        .collect();

    // 分数降序；同分时短标题优先（更可能是精确命中的那一条）
    out.sort_by(|a, b| {
        b.score
            .cmp(&a.score)
            .then_with(|| a.title.len().cmp(&b.title.len()))
    });
    out.truncate(limit);
    out
}

/* ── 读取与收敛 ───────────────────────────────────────── */

fn lake_dir() -> PathBuf {
    if let Ok(d) = std::env::var("TABBOARD_LAKE_DIR") {
        return PathBuf::from(d);
    }
    let base = match std::env::var("TABBOARD_DIR") {
        Ok(d) => PathBuf::from(d),
        Err(_) => std::env::var("USERPROFILE")
            .map(|h| PathBuf::from(h).join(".tabboard"))
            .unwrap_or_else(|_| PathBuf::from(".tabboard")),
    };
    base.join("lake")
}

/// 只读白名单文件；坏行直接跳过，不让一条脏数据拖垮整个搜索。
fn read_lines(dir: &Path, file: &str) -> Vec<Value> {
    if !ALLOWED.contains(&file) {
        return Vec::new();
    }
    let text = match std::fs::read_to_string(dir.join(file)) {
        Ok(t) => t,
        Err(_) => return Vec::new(),
    };
    text.lines()
        .filter_map(|l| serde_json::from_str::<Value>(l.trim()).ok())
        .collect()
}

fn s(v: &Value, key: &str) -> String {
    v.get(key).and_then(|x| x.as_str()).unwrap_or("").to_string()
}

fn ts_of(v: &Value) -> u64 {
    v.get("_ts").and_then(|x| x.as_u64()).unwrap_or(0)
}

fn is_openable(url: &str) -> bool {
    url.starts_with("http://") || url.starts_with("https://")
}

/// 对齐 focusSearch 的 getSearchableUrl：只留 hostname + pathname，
/// 丢掉协议 / userinfo / 端口 / query / hash，避免命中 utm_source 之类噪声。
fn searchable_url(url: &str) -> String {
    let rest = match url.find("://") {
        Some(i) => &url[i + 3..],
        None => url,
    };
    let (authority, path) = match rest.find('/') {
        Some(i) => (&rest[..i], &rest[i..]),
        None => (rest, ""),
    };
    let authority = authority.rsplit('@').next().unwrap_or(authority);
    let authority = authority.split(':').next().unwrap_or(authority);
    let path = path
        .split('?')
        .next()
        .unwrap_or(path)
        .split('#')
        .next()
        .unwrap_or(path);
    format!("{}{}", authority.to_lowercase(), path.to_lowercase())
}

fn host_of(url: &str) -> String {
    let rest = match url.find("://") {
        Some(i) => &url[i + 3..],
        None => url,
    };
    rest.split('/').next().unwrap_or(rest).to_string()
}

/// 同一 url（归一化后）收敛成一条：来源更优或 _ts 更新者胜出，
/// 同时把 visitCount / 时间 / 出现过的来源位图合并进来。
fn merge(map: &mut HashMap<String, Candidate>, mut c: Candidate) {
    let bit = c.source.bit();
    match map.get_mut(&c.key) {
        None => {
            c.sources = bit;
            map.insert(c.key.clone(), c);
        }
        Some(cur) => {
            cur.sources |= bit;
            cur.visit = cur.visit.max(c.visit);
            cur.day = match (cur.day, c.day) {
                (Some(a), Some(b)) => Some(a.max(b)),
                (Some(a), None) => Some(a),
                (None, Some(b)) => Some(b),
                (None, None) => None,
            };
            cur.focus |= c.focus;
            if cur.group.is_empty() {
                cur.group = c.group.clone();
            }

            if c.source > cur.source || (c.source == cur.source && c.ts > cur.ts) {
                let (sources, visit, day, focus, group) =
                    (cur.sources, cur.visit, cur.day, cur.focus, cur.group.clone());
                c.sources = sources;
                c.visit = visit;
                c.day = day;
                c.focus = focus;
                c.group = group;
                *cur = c;
            }
        }
    }
}

fn load_groups(dir: &Path) -> HashMap<String, GroupInfo> {
    let mut m: HashMap<String, GroupInfo> = HashMap::new();
    for v in read_lines(dir, "groups.jsonl") {
        let id = s(&v, "id");
        if id.is_empty() {
            continue;
        }
        let ts = ts_of(&v);
        let replace = match m.get(&id) {
            Some(cur) => ts > cur.ts,
            None => true,
        };
        if replace {
            m.insert(
                id,
                GroupInfo {
                    name: s(&v, "name"),
                    focus: v
                        .get("inFocusSearch")
                        .and_then(|x| x.as_bool())
                        .unwrap_or(false),
                    ts,
                },
            );
        }
    }
    m
}

fn collect_tabs(
    dir: &Path,
    groups: &HashMap<String, GroupInfo>,
    map: &mut HashMap<String, Candidate>,
) {
    for v in read_lines(dir, "tabs.jsonl") {
        let url = s(&v, "url");
        if !is_openable(&url) {
            continue;
        }
        let gid = s(&v, "groupId");
        let g = groups.get(&gid);
        let title = {
            let t = s(&v, "title");
            if t.is_empty() { url.clone() } else { t }
        };
        let day = v
            .get("lastVisit")
            .and_then(|x| x.as_str())
            .and_then(parse_day)
            .or_else(|| v.get("timestamp").and_then(|x| x.as_str()).and_then(parse_day));

        merge(
            map,
            Candidate {
                key: searchable_url(&url),
                url,
                title,
                source: Source::Group,
                sources: 0,
                focus: g.map(|x| x.focus).unwrap_or(false),
                visit: v
                    .get("visitCount")
                    .and_then(|x| x.as_u64())
                    .unwrap_or(0) as u32,
                day,
                group: g.map(|x| x.name.clone()).unwrap_or_default(),
                ts: ts_of(&v),
            },
        );
    }
}

/// timelineSnapshots / recordings：一行一条记录，内嵌 tabs 数组需要展开。
fn collect_nested(dir: &Path, file: &str, source: Source, map: &mut HashMap<String, Candidate>) {
    for v in read_lines(dir, file) {
        let row_day = v
            .get("timestamp")
            .and_then(|x| x.as_str())
            .and_then(parse_day)
            .or_else(|| v.get("startTime").and_then(|x| x.as_str()).and_then(parse_day));
        let row_ts = ts_of(&v);
        let group = s(&v, "name");
        let tabs = match v.get("tabs").and_then(|x| x.as_array()) {
            Some(a) => a.clone(),
            None => continue,
        };
        for t in &tabs {
            let url = s(t, "url");
            if !is_openable(&url) {
                continue;
            }
            let title = {
                let x = s(t, "title");
                if x.is_empty() { url.clone() } else { x }
            };
            let day = t
                .get("timestamp")
                .and_then(|x| x.as_str())
                .and_then(parse_day)
                .or(row_day);
            merge(
                map,
                Candidate {
                    key: searchable_url(&url),
                    url,
                    title,
                    source,
                    sources: 0,
                    focus: false,
                    visit: 0,
                    day,
                    group: group.clone(),
                    ts: row_ts,
                },
            );
        }
    }
}

/* ── 打分 ─────────────────────────────────────────────── */

/// 有序子序列 + 跨度上限 span <= 2.5 * query.len()（照抄 focusSearch）。
fn fuzzy_ordered(text: &str, q: &str) -> bool {
    if q.is_empty() {
        return true;
    }
    let tc: Vec<char> = text.chars().collect();
    let qc: Vec<char> = q.chars().collect();
    let mut qi = 0usize;
    let mut first: Option<usize> = None;
    let mut last = 0usize;
    for (i, &ch) in tc.iter().enumerate() {
        if qi < qc.len() && ch == qc[qi] {
            if first.is_none() {
                first = Some(i);
            }
            last = i;
            qi += 1;
        }
    }
    if qi != qc.len() {
        return false;
    }
    let span = last - first.unwrap_or(0) + 1;
    span * 2 <= qc.len() * 5
}

fn visit_bonus(v: u32) -> i32 {
    let mut n = 0i32;
    let mut x = v.saturating_add(1);
    while x > 1 && n < 4 {
        x /= 2;
        n += 1;
    }
    n.min(4)
}

fn recency_bonus(day: Option<i64>, today: i64) -> i32 {
    match day {
        Some(d) => {
            let age = today - d;
            if age <= 7 {
                3
            } else if age <= 30 {
                2
            } else if age <= 90 {
                1
            } else {
                0
            }
        }
        None => 0,
    }
}

fn score_candidate(c: &Candidate, q: &str, today: i64) -> Option<LinkItem> {
    let title = c.title.to_lowercase();
    let base = if title == q {
        100
    } else if title.starts_with(q) {
        80
    } else if title.contains(q) {
        60
    } else if c.key.contains(q) {
        40
    } else if fuzzy_ordered(&title, q) {
        20
    } else {
        0
    };
    if base == 0 {
        return None;
    }

    // 加成总量 <= 6+2+4+3+2 = 17 < 20，不会跨档位
    let bonus = c.source.bonus()
        + if c.focus { 2 } else { 0 }
        + visit_bonus(c.visit)
        + recency_bonus(c.day, today)
        + (c.sources.count_ones().saturating_sub(1) as i32).min(2);

    let host = host_of(&c.url);
    let subtitle = if c.group.is_empty() {
        format!("{} · {}", c.source.label(), host)
    } else {
        format!("{} · {} · {}", c.source.label(), c.group, host)
    };

    Some(LinkItem {
        title: c.title.clone(),
        url: c.url.clone(),
        subtitle,
        score: base + bonus,
    })
}

/* ── 日期（RFC3339 的前 10 位 → 距 1970-01-01 天数，避免引入 chrono） ── */

fn parse_day(iso: &str) -> Option<i64> {
    if iso.len() < 10 {
        return None;
    }
    let y = iso.get(0..4)?.parse::<i64>().ok()?;
    let m = iso.get(5..7)?.parse::<i64>().ok()?;
    let d = iso.get(8..10)?.parse::<i64>().ok()?;
    if !(1..=12).contains(&m) || !(1..=31).contains(&d) {
        return None;
    }
    Some(civil_to_days(y, m, d))
}

fn civil_to_days(y: i64, m: i64, d: i64) -> i64 {
    let y = y - if m <= 2 { 1 } else { 0 };
    let era = if y >= 0 { y } else { y - 399 } / 400;
    let yoe = y - era * 400;
    let mp = m + if m > 2 { -3 } else { 9 };
    let doy = (153 * mp + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    era * 146097 + doe - 719468
}

fn today_days() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs() as i64 / 86400)
        .unwrap_or(0)
}
