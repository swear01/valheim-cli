#![cfg(windows)]
mod common;
use common::*;
use serde_json::Value;
use std::{fs, os::windows::process::CommandExt, process::Command};
use valheim_agent_cli::install;

#[test]
fn windowless_executable_preserves_piped_output_and_exit_codes() {
    let executable = env!("CARGO_BIN_EXE_valheim");
    let bytes = fs::read(executable).unwrap();
    let pe = u32::from_le_bytes(bytes[0x3c..0x40].try_into().unwrap()) as usize;
    assert_eq!(
        u16::from_le_bytes(bytes[pe + 92..pe + 94].try_into().unwrap()),
        2
    );
    for (argument, code) in [("--version", 0), ("--invalid", 1)] {
        let output = Command::new(executable).arg(argument).output().unwrap();
        assert_eq!(output.status.code(), Some(code));
        let json: Value = serde_json::from_slice(if code == 0 {
            &output.stdout
        } else {
            &output.stderr
        })
        .unwrap();
        assert_eq!(json["ok"], code == 0);
        assert!(
            if code == 0 {
                output.stderr
            } else {
                output.stdout
            }
            .is_empty()
        );
    }
    for (argument, ok) in [("--version", true), ("--invalid", false)] {
        let output = Command::new("cmd.exe")
            .args(["/D", "/S", "/C"])
            .raw_arg(format!("\"\"{executable}\" {argument}\""))
            .creation_flags(0x08000000)
            .output()
            .unwrap();
        assert_eq!(
            serde_json::from_slice::<Value>(if ok { &output.stdout } else { &output.stderr })
                .unwrap()["ok"],
            ok
        );
    }
}

#[test]
fn launcher_does_not_mistake_its_native_cli_for_the_game() {
    let temp = Temp::new();
    let profile = temp.profile();
    fs::write(
        profile.join("valheim.exe"),
        b"deliberately not an executable",
    )
    .unwrap();
    fs::write(
        profile.join("BepInEx/core/BepInEx.Preloader.dll"),
        b"fixture",
    )
    .unwrap();
    fs::write(
        profile.join("BepInEx/config/swear01.ValheimCliBridge.cfg"),
        "[Permissions]\nAllowControl = true\n",
    )
    .unwrap();
    install::install(&profile, install::DLL, install::MANIFEST).unwrap();
    let output = Command::new(env!("CARGO_BIN_EXE_valheim"))
        .args(["launch-agent", "--profile"])
        .arg(&profile)
        .arg("--game")
        .arg(profile.join("valheim.exe"))
        .arg("--confirm")
        .output()
        .unwrap();
    assert_eq!(output.status.code(), Some(1));
    let result: Value = serde_json::from_slice(&output.stderr).unwrap();
    let error = result["error"].as_str().unwrap();
    assert!(error.contains("Agent launcher failed"), "{error}");
    assert!(!error.contains("Valheim is already running"), "{error}");
    assert!(
        error.contains("Run launch-agent in the signed-in")
            || error.contains("not a valid Win32 application")
            || error.contains("not compatible with the version of Windows"),
        "{error}"
    );
}
