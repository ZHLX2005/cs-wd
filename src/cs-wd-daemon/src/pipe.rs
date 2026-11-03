//! named pipe 服务端：accept 循环，每连接一个 task，读 JSON Lines → 分发 → 回写。

use std::sync::Arc;

use anyhow::Context;
use serde_json::json;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::net::windows::named_pipe::{ClientOptions, ServerOptions};

use crate::exec::JobTable;
use crate::history::History;
use crate::protocol::{Request, Response};

pub const PIPE_NAME: &str = r"\\.\pipe\cs-wd-daemon";

pub struct Daemon {
    pub history: Arc<tokio::sync::Mutex<History>>,
    pub jobs: Arc<JobTable>,
}

impl Daemon {
    pub async fn handle_line(&self, line: &str) -> Response {
        let req: Request = match serde_json::from_str(line) {
            Ok(r) => r,
            Err(e) => return Response::err(format!("bad request: {e}")),
        };
        match req {
            Request::Ping => Response::ok(json!({ "version": env!("CARGO_PKG_VERSION") })),
            Request::Search { query } => {
                let h = self.history.lock().await;
                let items: Vec<serde_json::Value> = h
                    .search(&query, 50)
                    .into_iter()
                    .map(|e| {
                        json!({ "path": e.path, "count": e.count, "lastUsedMs": e.last_used_ms })
                    })
                    .collect();
                Response::ok(json!({ "items": items }))
            }
            Request::Run { cmd, cwd } => {
                let cwd = match cwd {
                    Some(c) => Some(c),
                    None => {
                        let h = self.history.lock().await;
                        h.most_recent()
                    }
                };
                match self.jobs.spawn(&cmd, cwd.clone()).await {
                    Ok(v) => {
                        // 目录历史：执行成功才算一次使用
                        if let Some(d) = cwd {
                            self.history.lock().await.record(&d);
                        }
                        Response::ok(v)
                    }
                    Err(e) => Response::err(e.to_string()),
                }
            }
            Request::Record { path } => {
                self.history.lock().await.record(&path);
                Response::ok(json!({ "recorded": path }))
            }
            Request::Jobs => Response::ok(self.jobs.jobs().await),
            Request::Kill { id } => match self.jobs.kill(id).await {
                Ok(v) => Response::ok(v),
                Err(e) => Response::err(e.to_string()),
            },
            Request::Log { id, tail } => match self.jobs.log_tail(id, tail.unwrap_or(200)).await {
                Ok(v) => Response::ok(v),
                Err(e) => Response::err(e.to_string()),
            },
            Request::Forget { id } => match self.jobs.forget(id).await {
                Ok(v) => Response::ok(v),
                Err(e) => Response::err(e.to_string()),
            },
        }
    }
}

pub async fn serve(daemon: Arc<Daemon>) -> anyhow::Result<()> {
    let mut server = ServerOptions::new()
        .first_pipe_instance(true)
        .create(PIPE_NAME)
        .with_context(|| format!("create pipe {PIPE_NAME}"))?;
    tracing::info!("listening on {PIPE_NAME}");

    loop {
        // 等一个客户端连上来，然后立刻创建下一个实例排队
        server.connect().await?;
        let client = server;
        server = ServerOptions::new().create(PIPE_NAME)?;

        let daemon = daemon.clone();
        tokio::spawn(async move {
            let (reader, mut writer) = tokio::io::split(client);
            let mut lines = BufReader::new(reader).lines();
            while let Ok(Some(line)) = lines.next_line().await {
                if line.trim().is_empty() {
                    continue;
                }
                let resp = daemon.handle_line(&line).await;
                let mut out = serde_json::to_string(&resp).unwrap_or_default();
                out.push('\n');
                if writer.write_all(out.as_bytes()).await.is_err() {
                    break;
                }
                let _ = writer.flush().await;
            }
        });
    }
}

/// 探测 daemon 是否已在运行
pub async fn probe_alive() -> bool {
    match ClientOptions::new().open(PIPE_NAME) {
        Ok(_c) => true,
        Err(e) if e.raw_os_error() == Some(2) => false, // ERROR_FILE_NOT_FOUND
        Err(_) => true,                                  // 忙/拒绝 = 有实例
    }
}
