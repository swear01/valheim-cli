mod common;
use common::*;
use serde_json::json;
use std::{fs, path::Path};
use valheim_agent_cli::{
    install::{self, hash},
    run,
};

fn fixture() -> (Temp, std::path::PathBuf, Vec<u8>) {
    let temp = Temp::new();
    let profile = temp.profile();
    let manifest =
        serde_json::to_vec(&json!({"version":"0.4.0","sha256":hash(b"MZ-fixture")})).unwrap();
    (temp, profile, manifest)
}

#[test]
fn installer_preserves_other_files_and_refuses_different_or_duplicate_bridges() {
    let (_temp, profile, manifest) = fixture();
    fs::write(profile.join("BepInEx/config/other.cfg"), "preserve").unwrap();
    assert!(install::install(&profile.join("missing"), b"MZ-fixture", &manifest).is_err());
    assert!(
        install::install(&profile, b"wrong", &manifest)
            .unwrap_err()
            .contains("checksum")
    );
    let first = install::install(&profile, b"MZ-fixture", &manifest).unwrap();
    let original = std::path::PathBuf::from(first["path"].as_str().unwrap());
    assert_eq!(
        first,
        install::install(&profile, b"MZ-fixture", &manifest).unwrap()
    );
    assert_eq!(
        fs::read_to_string(profile.join("BepInEx/config/other.cfg")).unwrap(),
        "preserve"
    );
    for subdirectory in ["", "AnyManager/Author-Package/BepInEx/plugins"] {
        let directory = profile.join("BepInEx/plugins").join(subdirectory);
        fs::create_dir_all(&directory).unwrap();
        let next = directory.join("ValheimCliBridge.dll");
        let current = install::installed(&profile, &hash(b"MZ-fixture"))
            .unwrap()
            .unwrap();
        fs::rename(current, &next).unwrap();
        assert_eq!(
            install::install(&profile, b"MZ-fixture", &manifest).unwrap()["path"],
            next.to_str().unwrap()
        );
    }
    fs::write(&original, b"MZ-fixture").unwrap();
    assert!(
        install::installed(&profile, &hash(b"MZ-fixture"))
            .unwrap_err()
            .contains("Multiple")
    );
    assert!(
        install::install(&profile, b"MZ-fixture", &manifest)
            .unwrap_err()
            .contains("Multiple")
    );
    fs::remove_file(original).unwrap();
    let current = install::installed(&profile, &hash(b"MZ-fixture"))
        .unwrap()
        .unwrap();
    fs::write(current, b"different").unwrap();
    assert!(
        install::install(&profile, b"MZ-fixture", &manifest)
            .unwrap_err()
            .contains("Different bridge")
    );
}

fn link_directory(target: &Path, link: &Path) {
    #[cfg(unix)]
    std::os::unix::fs::symlink(target, link).unwrap();
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        let result = std::process::Command::new("cmd.exe")
            .args(["/C", "mklink", "/J"])
            .arg(link)
            .arg(target)
            .creation_flags(0x08000000)
            .output()
            .unwrap();
        assert!(
            result.status.success(),
            "{}",
            String::from_utf8_lossy(&result.stderr)
        );
    }
}

#[test]
fn linked_plugin_folders_are_read_only_and_cycles_and_aliases_count_once() {
    let (temp, profile, manifest) = fixture();
    let original = install::install(&profile, b"MZ-fixture", &manifest).unwrap();
    let plugins = profile.join("BepInEx/plugins");
    let external = temp.0.join("external");
    fs::create_dir(&external).unwrap();
    link_directory(&external, &plugins.join("linked-mods"));
    link_directory(&plugins, &external.join("cycle"));
    link_directory(&external, &plugins.join("alias"));
    let target = external.join("ValheimCliBridge.dll");
    fs::rename(original["path"].as_str().unwrap(), &target).unwrap();
    let reused = install::install(&profile, b"MZ-fixture", &manifest).unwrap();
    assert_eq!(
        fs::canonicalize(reused["path"].as_str().unwrap()).unwrap(),
        target
    );
    assert!(!Path::new(original["path"].as_str().unwrap()).exists());
    let moved = temp.0.join("linked-root");
    fs::rename(profile.join("BepInEx"), &moved).unwrap();
    link_directory(&moved, &profile.join("BepInEx"));
    assert!(
        install::install(&profile, b"MZ-fixture", &manifest)
            .unwrap_err()
            .contains("symlinks")
    );
}

#[cfg(unix)]
#[test]
fn linked_dll_and_unreadable_subfolders_cannot_hide_an_existing_bridge() {
    use std::os::unix::fs::{PermissionsExt, symlink};
    let (temp, profile, manifest) = fixture();
    let plugins = profile.join("BepInEx/plugins");
    fs::create_dir(&plugins).unwrap();
    let target = temp.0.join("ValheimCliBridge.dll");
    fs::write(&target, b"MZ-fixture").unwrap();
    let linked = plugins.join("ValheimCliBridge.dll");
    symlink(&target, &linked).unwrap();
    assert!(
        install::install(&profile, b"MZ-fixture", &manifest)
            .unwrap_err()
            .contains("symlink")
    );
    fs::remove_file(linked).unwrap();
    let external = temp.0.join("external");
    fs::create_dir(&external).unwrap();
    link_directory(&external, &plugins.join("external"));
    fs::rename(&target, external.join("ValheimCliBridge.dll")).unwrap();
    fs::set_permissions(&external, fs::Permissions::from_mode(0o0)).unwrap();
    let result = install::install(&profile, b"MZ-fixture", &manifest);
    let inaccessible = fs::read_dir(&external).is_err();
    fs::set_permissions(&external, fs::Permissions::from_mode(0o700)).unwrap();
    if inaccessible {
        assert!(result.is_err());
    } else {
        assert!(result.is_ok());
    }
    assert!(
        !plugins
            .join("swear01-ValheimCliBridge/ValheimCliBridge.dll")
            .exists()
    );
}

#[test]
fn native_binary_installs_embedded_original_release_without_sidecar_files() {
    let temp = Temp::new();
    let profile = temp.profile();
    let executable = temp.0.join(if cfg!(windows) {
        "valheim.exe"
    } else {
        "valheim"
    });
    fs::copy(env!("CARGO_BIN_EXE_valheim"), &executable).unwrap();
    let output = std::process::Command::new(&executable)
        .args(["install", "--profile"])
        .arg(&profile)
        .current_dir(&temp.0)
        .env("PATH", "")
        .output()
        .unwrap();
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    let result: serde_json::Value = serde_json::from_slice(&output.stdout).unwrap();
    assert_eq!(
        hash(&fs::read(result["path"].as_str().unwrap()).unwrap()),
        "35ad8648a5b7824eeb4c849ae10716d0d057bd0514b81cd24efb043cc51c57e2"
    );
    assert_eq!(result["version"], "0.4.0");
}

#[test]
fn launcher_refuses_unsupported_platform_or_disabled_config_without_starting_game() {
    let temp = Temp::new();
    let original = temp.profile();
    let profile = temp.0.join("profile ' $value `name");
    fs::rename(original, &profile).unwrap();
    fs::write(profile.join("valheim.exe"), "never executable").unwrap();
    fs::write(
        profile.join("BepInEx/core/BepInEx.Preloader.dll"),
        "fixture",
    )
    .unwrap();
    install::install(&profile, install::DLL, install::MANIFEST).unwrap();
    for config in [
        "[Permissions]\nAllowControl = false\n",
        "[Bridge]\nEnabled = false\n[Permissions]\nAllowControl = true\n",
        "[Bridge]\n  Enabled = false\n[Permissions]\n  AllowControl = true\n",
    ] {
        fs::write(
            profile.join("BepInEx/config/swear01.ValheimCliBridge.cfg"),
            config,
        )
        .unwrap();
        let result = run(vec![
            "launch-agent".into(),
            "--profile".into(),
            profile.to_str().unwrap().into(),
            "--game".into(),
            profile.join("valheim.exe").to_str().unwrap().into(),
            "--confirm".into(),
        ])
        .unwrap_err();
        if cfg!(windows) {
            assert!(
                result.contains("Enable the bridge and AllowControl"),
                "{result}"
            );
        } else {
            assert!(result.contains("requires Windows"));
        }
    }
}
