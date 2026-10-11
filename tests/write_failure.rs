#![cfg(unix)]
mod common;
use base64::{Engine, engine::general_purpose::STANDARD};
use common::*;
use serde_json::json;
use std::{fs, process::Command};

#[test]
fn failed_dll_and_screenshot_writes_remove_only_the_new_file() {
    let temp = Temp::new();
    let profile = temp.profile();
    let image = temp.0.join("frame.png");
    let (port, worker) = server(
        1,
        |_| json!({"image":STANDARD.encode(b"\x89PNG\r\n\x1a\nfixture")}),
    );
    for operation in ["install", "observe"] {
        let mut command = Command::new("bash");
        command
            .args([
                "-c",
                "trap '' XFSZ; ulimit -f 0 || exit 99; exec \"$@\"",
                "limited-write",
            ])
            .arg(env!("CARGO_BIN_EXE_valheim"))
            .args([operation, "--profile"])
            .arg(&profile);
        let target = if operation == "observe" {
            command
                .args(["--port", &port.to_string(), "--image"])
                .arg(&image);
            image.clone()
        } else {
            profile.join("BepInEx/plugins/swear01-ValheimCliBridge/ValheimCliBridge.dll")
        };
        let output = command.output().unwrap();
        assert!(!output.status.success());
        assert!(
            String::from_utf8_lossy(&output.stderr).contains("File too large"),
            "{output:?}"
        );
        assert!(
            !target.exists(),
            "Partial file left at {}",
            target.display()
        );
    }
    worker.join().unwrap();
    assert_eq!(
        fs::read(profile.join("BepInEx/core/BepInEx.dll")).unwrap(),
        b"fixture"
    );
}
