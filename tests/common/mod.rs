#![allow(dead_code)]
use serde_json::Value;
use std::{
    fs,
    io::{Read, Write},
    net::TcpListener,
    path::PathBuf,
    thread,
    time::{Duration, Instant},
};

pub struct Temp(pub PathBuf);
impl Temp {
    pub fn new() -> Self {
        let path = fs::canonicalize(std::env::temp_dir())
            .unwrap()
            .join(format!(
                "valheim ü {}",
                valheim_agent_cli::client::uuid().unwrap()
            ));
        fs::create_dir(&path).unwrap();
        Self(path)
    }
    pub fn profile(&self) -> PathBuf {
        let path = self.0.join("profile");
        fs::create_dir_all(path.join("BepInEx/config")).unwrap();
        fs::create_dir_all(path.join("BepInEx/core")).unwrap();
        fs::write(path.join("BepInEx/core/BepInEx.dll"), b"fixture").unwrap();
        fs::write(
            path.join("BepInEx/config/swear01.ValheimCliBridge.token"),
            "a".repeat(64),
        )
        .unwrap();
        path
    }
}
impl Drop for Temp {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}
pub fn args(values: &[&str]) -> Vec<String> {
    values.iter().map(|v| v.to_string()).collect()
}

pub fn server(
    count: usize,
    handler: impl Fn(Value) -> Value + Send + 'static,
) -> (u16, thread::JoinHandle<()>) {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    listener.set_nonblocking(true).unwrap();
    let work = thread::spawn(move || {
        let deadline = Instant::now() + Duration::from_secs(10);
        for _ in 0..count {
            let mut socket = loop {
                match listener.accept() {
                    Ok((socket, _)) => break socket,
                    Err(e)
                        if e.kind() == std::io::ErrorKind::WouldBlock
                            && Instant::now() < deadline =>
                    {
                        thread::sleep(Duration::from_millis(1))
                    }
                    Err(e) => panic!("Fixture accept: {e}"),
                }
            };
            socket.set_nonblocking(false).unwrap();
            socket
                .set_read_timeout(Some(Duration::from_secs(5)))
                .unwrap();
            let mut header = [0u8; 4];
            socket.read_exact(&mut header).unwrap();
            let size = u32::from_be_bytes(header) as usize;
            assert!(size <= 65536);
            let mut body = vec![0u8; size];
            socket.read_exact(&mut body).unwrap();
            let value: Value = serde_json::from_slice(&body).unwrap();
            let id = value["id"].clone();
            assert_eq!(value["token"], "a".repeat(64));
            let mut response = handler(value);
            response["id"] = id;
            response["ok"] = true.into();
            let bytes = serde_json::to_vec(&response).unwrap();
            let mut frame = (bytes.len() as u32).to_be_bytes().to_vec();
            frame.extend(bytes);
            socket.write_all(&frame).unwrap();
        }
    });
    (port, work)
}
