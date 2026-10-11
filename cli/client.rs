use serde_json::Value;
use std::io::{Read, Write};
use std::net::{SocketAddr, TcpStream};
use std::time::{Duration, Instant};

pub fn uuid() -> Result<String, String> {
    let mut bytes = [0u8; 16];
    getrandom::fill(&mut bytes).map_err(|_| "OS random generator unavailable")?;
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    let h: String = bytes.iter().map(|b| format!("{b:02x}")).collect();
    Ok(format!(
        "{}-{}-{}-{}-{}",
        &h[..8],
        &h[8..12],
        &h[12..16],
        &h[16..20],
        &h[20..]
    ))
}

pub fn request(port: u16, token: &str, mut value: Value) -> Result<Value, String> {
    if port < 1024 {
        return Err("Invalid bridge port".into());
    }
    if token.len() != 64
        || !token
            .bytes()
            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
    {
        return Err("Invalid local token".into());
    }
    if value.get("id").is_none() {
        value["id"] = uuid()?.into();
    }
    value["token"] = token.into();
    let body = serde_json::to_vec(&value).map_err(|_| "Invalid request JSON")?;
    if body.len() > 65536 {
        return Err("Request too large".into());
    }
    let write = matches!(
        value["operation"].as_str(),
        Some("teleport" | "console" | "input" | "look" | "action" | "ui")
    );
    let max_response = if matches!(value["operation"].as_str(), Some("observe" | "ui-list")) {
        8 * 1024 * 1024
    } else {
        65536
    };
    let deadline = Instant::now() + Duration::from_secs(5);
    let mut socket = TcpStream::connect_timeout(
        &SocketAddr::from(([127, 0, 0, 1], port)),
        Duration::from_secs(5),
    )
    .map_err(|_| "Cannot connect to bridge. Start Valheim with the plugin enabled.")?;
    let mut acknowledged = false;
    let result = (|| {
        let mut frame = (body.len() as u32).to_be_bytes().to_vec();
        frame.extend_from_slice(&body);
        let mut remaining = frame.as_slice();
        while !remaining.is_empty() {
            socket
                .set_write_timeout(Some(time_left(deadline)?))
                .map_err(|_| "Bridge connection failed")?;
            let n = socket
                .write(remaining)
                .map_err(|_| "Bridge connection failed")?;
            if n == 0 {
                return Err("Bridge connection failed".into());
            }
            remaining = &remaining[n..];
        }
        let mut header = [0u8; 4];
        read(&mut socket, &mut header, deadline)?;
        let size = u32::from_be_bytes(header) as usize;
        if size == 0 || size > max_response {
            return Err("Invalid bridge frame".into());
        }
        let mut payload = vec![0u8; size];
        read(&mut socket, &mut payload, deadline)?;
        let response: Value =
            serde_json::from_slice(&payload).map_err(|_| "Invalid bridge JSON")?;
        socket
            .set_nonblocking(true)
            .map_err(|_| "Bridge connection failed")?;
        if socket.peek(&mut [0u8; 1]).is_ok_and(|n| n > 0) {
            return Err("Trailing bridge data".into());
        }
        if response["id"] != value["id"] || !response["ok"].is_boolean() {
            return Err("Invalid bridge response".into());
        }
        acknowledged = true;
        if response["ok"] != true {
            return Err(response["error"]
                .as_str()
                .unwrap_or("Bridge rejected operation")
                .into());
        }
        Ok(response)
    })();
    result.map_err(|error: String| {
        if write && !acknowledged {
            format!("{error} Write outcome unknown; do not automatically retry. Use stop/status.")
        } else {
            error
        }
    })
}

fn time_left(deadline: Instant) -> Result<Duration, String> {
    deadline
        .checked_duration_since(Instant::now())
        .filter(|d| !d.is_zero())
        .ok_or("Bridge timeout".into())
}

fn read(socket: &mut TcpStream, mut buffer: &mut [u8], deadline: Instant) -> Result<(), String> {
    while !buffer.is_empty() {
        socket
            .set_read_timeout(Some(time_left(deadline)?))
            .map_err(|_| "Bridge connection failed")?;
        let n = socket
            .read(buffer)
            .map_err(|_| "Bridge timeout or connection failure")?;
        if n == 0 {
            return Err("Bridge closed before a complete response".into());
        }
        buffer = &mut buffer[n..];
    }
    Ok(())
}
