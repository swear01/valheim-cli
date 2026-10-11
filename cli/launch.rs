use crate::Result;
use serde_json::Value;
use std::path::Path;

#[cfg(not(windows))]
pub fn launch(_: &Path, _: &Path) -> Result<Value> {
    Err("launch-agent currently requires Windows".into())
}

#[cfg(windows)]
pub fn launch(profile: &Path, game: &Path) -> Result<Value> {
    use base64::{Engine, engine::general_purpose::STANDARD};
    use std::{
        os::windows::process::CommandExt,
        process::{Command, Stdio},
    };
    for path in [profile, game] {
        if path
            .to_string_lossy()
            .chars()
            .any(|c| c == '"' || c <= '\u{1f}')
        {
            return Err("Choose an existing game executable and BepInEx profile".into());
        }
    }
    if !game.is_file() || !profile.join("BepInEx/core/BepInEx.Preloader.dll").is_file() {
        return Err("Choose an existing game executable and BepInEx profile".into());
    }
    let manifest: Value =
        serde_json::from_slice(crate::install::MANIFEST).map_err(|_| "Invalid bridge manifest")?;
    if crate::install::installed(
        profile,
        manifest["sha256"]
            .as_str()
            .ok_or("Invalid bridge manifest")?,
    )?
    .is_none()
    {
        return Err("Install this CLI version of the bridge before launch-agent".into());
    }
    let script = format!(
        "& {{\n{}\n}} -Game '{}' -Profile '{}'",
        include_str!("../src/windows-agent-launch.ps1"),
        game.to_string_lossy().replace('\'', "''"),
        profile.to_string_lossy().replace('\'', "''")
    );
    let bytes: Vec<u8> = script.encode_utf16().flat_map(u16::to_le_bytes).collect();
    let output = Command::new("powershell.exe")
        .args([
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-EncodedCommand",
            &STANDARD.encode(bytes),
        ])
        .creation_flags(0x08000000)
        .stdin(Stdio::null())
        .output()
        .map_err(|e| format!("Cannot start Agent launcher: {e}"))?;
    if !output.status.success() {
        return Err(format!(
            "Agent launcher failed: {}",
            String::from_utf8_lossy(&output.stderr).trim()
        ));
    }
    serde_json::from_slice(&output.stdout)
        .map_err(|_| "Agent launcher returned invalid JSON".into())
}
