//! cs-wd-daemon：后台常驻进程。
//!
//! 职责：named pipe JSON Lines 服务；命令后台执行与日志；目录历史。
//! 启动：`cs-wd-daemon.exe`（无参数）。数据目录 `%LOCALAPPDATA%\cs-wd\`。

mod exec;
mod history;
mod pipe;
mod protocol;

use std::sync::Arc;

use anyhow::Context;

fn data_dir() -> std::path::PathBuf {
    std::env::var("LOCALAPPDATA")
        .map(|d| std::path::PathBuf::from(d).join("cs-wd"))
        .unwrap_or_else(|_| std::path::PathBuf::from(".cs-wd"))
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| "cs_wd_daemon=info".into()),
        )
        .init();

    // 单实例：已有 daemon 在跑就退出
    if pipe::probe_alive().await {
        tracing::info!("daemon already running, exit");
        return Ok(());
    }

    let dir = data_dir();
    let hist = history::History::load(&dir).context("load history")?;
    let jobs = exec::JobTable::new(dir.join("logs"))?;

    let daemon = Arc::new(pipe::Daemon {
        history: Arc::new(tokio::sync::Mutex::new(hist)),
        jobs: Arc::new(jobs),
    });

    tracing::info!("cs-wd-daemon {} started, data dir: {}", env!("CARGO_PKG_VERSION"), dir.display());
    pipe::serve(daemon).await
}
