//! 后台任务：spawn 无窗口子进程，stdout/stderr 合并落盘日志，支持列表/终止/读尾部。
//!
//! 设计参考 openai/codex（codex-rs/exec、core）的子进程生命周期策略：
//! 常驻 job 表 + 异步回收 + 全量日志按 jobId 留存。
//!
//! 注意：不在持锁状态下 await `child.wait()`（否则 jobs/kill 会被阻塞）——
//! child 句柄移交给回收任务，kill 走 `taskkill /T /F`（顺带杀进程树）。

use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::Arc;

use anyhow::Context;
use serde_json::json;
use tokio::sync::Mutex;

#[derive(Debug, Clone)]
pub struct Job {
    pub id: u64,
    pub cmd: String,
    pub cwd: Option<String>,
    pub pid: Option<u32>,
    pub log_path: PathBuf,
    pub started_ms: u64,
    pub exited: bool,
    pub code: Option<i32>,
}

pub struct JobTable {
    next_id: std::sync::atomic::AtomicU64,
    jobs: Arc<Mutex<HashMap<u64, Job>>>,
    log_dir: PathBuf,
}

fn now_ms() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

impl JobTable {
    pub fn new(log_dir: PathBuf) -> anyhow::Result<Self> {
        std::fs::create_dir_all(&log_dir).context("create log dir")?;
        Ok(JobTable {
            next_id: std::sync::atomic::AtomicU64::new(1),
            jobs: Arc::new(Mutex::new(HashMap::new())),
            log_dir,
        })
    }

    pub async fn spawn(&self, cmd: &str, cwd: Option<String>) -> anyhow::Result<serde_json::Value> {
        let id = self.next_id.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        let log_path = self.log_dir.join(format!("{id}.log"));
        let log_file = std::fs::File::create(&log_path).context("create log file")?;

        // cmd 交给 shell 解析（支持管道/重定向），无窗口后台运行
        let mut c = tokio::process::Command::new("cmd");
        c.arg("/C")
            .arg(cmd)
            .stdout(std::process::Stdio::from(log_file.try_clone()?))
            .stderr(std::process::Stdio::from(log_file))
            .stdin(std::process::Stdio::null())
            .creation_flags(0x0800_0000); // CREATE_NO_WINDOW
        if let Some(dir) = &cwd {
            c.current_dir(dir);
        }

        let mut child = c.spawn().with_context(|| format!("spawn: {cmd}"))?;
        let pid = child.id();

        let job = Job {
            id,
            cmd: cmd.to_string(),
            cwd: cwd.clone(),
            pid,
            log_path: log_path.clone(),
            started_ms: now_ms(),
            exited: false,
            code: None,
        };

        // 先入表，再启动回收任务（否则回收任务可能查不到条目）
        self.jobs.lock().await.insert(id, job.clone());

        // 回收任务：等进程退出，更新终态（child 句柄移交本任务，不占表锁）
        let table = self.jobs.clone();
        tokio::spawn(async move {
            let code = child.wait().await.ok().and_then(|s| s.code());
            let mut jobs = table.lock().await;
            if let Some(j) = jobs.get_mut(&id) {
                j.exited = true;
                j.code = code;
            }
        });

        tracing::info!(id, cmd, "job spawned");
        Ok(json!({ "job": { "id": id, "cmd": cmd, "pid": pid, "log": log_path.to_string_lossy() } }))
    }

    pub async fn jobs(&self) -> serde_json::Value {
        let mut jobs = self.jobs.lock().await;
        let mut list: Vec<serde_json::Value> = jobs
            .values_mut()
            .map(|j| {
                json!({
                    "id": j.id, "cmd": j.cmd, "cwd": j.cwd, "pid": j.pid,
                    "exited": j.exited, "code": j.code,
                    "startedMs": j.started_ms, "log": j.log_path.to_string_lossy(),
                })
            })
            .collect();
        list.sort_by_key(|v| v["id"].as_u64().unwrap_or(0));
        json!({ "jobs": list })
    }

    /// 清除一条已退出的任务记录（日志文件保留）
    pub async fn forget(&self, id: u64) -> anyhow::Result<serde_json::Value> {
        let mut jobs = self.jobs.lock().await;
        match jobs.remove(&id) {
            Some(j) if !j.exited => {
                // 运行中的任务不允许 forget，放回去
                jobs.insert(id, j);
                anyhow::bail!("job {id} is still running; use kill first")
            }
            _ => Ok(json!({ "forgot": id })),
        }
    }

    pub async fn kill(&self, id: u64) -> anyhow::Result<serde_json::Value> {
        let pid = {
            let jobs = self.jobs.lock().await;
            let j = jobs.get(&id).context("job not found")?;
            match j.pid {
                Some(p) => p,
                None => anyhow::bail!("job {id} has no pid (already reaped)"),
            }
        };
        let status = tokio::process::Command::new("taskkill")
            .args(["/F", "/T", "/PID", &pid.to_string()])
            .creation_flags(0x0800_0000)
            .output()
            .await
            .context("taskkill")?;
        if status.status.success() {
            Ok(json!({ "killed": id, "pid": pid }))
        } else {
            Ok(json!({ "killed": id, "pid": pid, "note": String::from_utf8_lossy(&status.stderr).trim() }))
        }
    }

    pub async fn log_tail(&self, id: u64, tail: usize) -> anyhow::Result<serde_json::Value> {
        let path = {
            let jobs = self.jobs.lock().await;
            match jobs.get(&id) {
                Some(j) => j.log_path.clone(),
                None => self.log_dir.join(format!("{id}.log")),
            }
        };
        let data = std::fs::read_to_string(&path).with_context(|| format!("read {}", path.display()))?;
        let lines: Vec<&str> = data.lines().collect();
        let start = lines.len().saturating_sub(tail);
        Ok(json!({ "id": id, "lines": &lines[start..] }))
    }
}
