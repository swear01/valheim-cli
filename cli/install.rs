use crate::Result;
use serde_json::{Value, json};
use sha2::{Digest, Sha256};
use std::{
    collections::{HashMap, HashSet},
    fs,
    io::Write,
    path::{Path, PathBuf},
};

pub const DLL: &[u8] = include_bytes!("../bridge-dist/ValheimCliBridge.dll");
pub const MANIFEST: &[u8] = include_bytes!("../bridge-dist/manifest.json");
pub fn hash(bytes: &[u8]) -> String {
    format!("{:x}", Sha256::digest(bytes))
}

fn linked(metadata: &fs::Metadata) -> bool {
    #[cfg(windows)]
    {
        use std::os::windows::fs::MetadataExt;
        metadata.file_attributes() & 0x400 != 0
    }
    #[cfg(not(windows))]
    {
        metadata.file_type().is_symlink()
    }
}

fn safe_directory(path: &Path) -> Result<()> {
    fs::create_dir_all(path).map_err(|e| e.to_string())?;
    if linked(&fs::symlink_metadata(path).map_err(|e| e.to_string())?) {
        return Err("Profile/plugin path must not contain symlinks".into());
    }
    Ok(())
}

pub fn installed(profile: &Path, expected: &str) -> Result<Option<PathBuf>> {
    let bepinex = profile.join("BepInEx");
    let plugins = bepinex.join("plugins");
    for path in [&bepinex, &plugins] {
        match fs::symlink_metadata(path) {
            Ok(metadata) if linked(&metadata) => {
                return Err("Plugin paths must not contain symlinks".into());
            }
            Ok(_) => (),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(None),
            Err(e) => return Err(e.to_string()),
        }
    }
    fn scan(
        folder: &Path,
        parents: &mut HashSet<PathBuf>,
        found: &mut HashMap<PathBuf, PathBuf>,
    ) -> Result<()> {
        let resolved = fs::canonicalize(folder).map_err(|e| e.to_string())?;
        if !parents.insert(resolved.clone()) {
            return Ok(());
        }
        for entry in fs::read_dir(folder).map_err(|e| e.to_string())? {
            let entry = entry.map_err(|e| e.to_string())?;
            let path = entry.path();
            let metadata = fs::symlink_metadata(&path).map_err(|e| e.to_string())?;
            let bridge = entry
                .file_name()
                .to_string_lossy()
                .eq_ignore_ascii_case("ValheimCliBridge.dll");
            if linked(&metadata) {
                if bridge {
                    return Err("Bridge DLL must not be a symlink".into());
                }
                match fs::metadata(&path) {
                    Ok(m) if m.is_dir() => scan(&path, parents, found)?,
                    Ok(_) => (),
                    Err(e) if e.kind() == std::io::ErrorKind::NotFound => (),
                    Err(e) => return Err(e.to_string()),
                }
            } else if metadata.is_dir() {
                scan(&path, parents, found)?;
            } else if bridge {
                if !metadata.is_file() {
                    return Err("Bridge DLL must be a regular file".into());
                }
                found.insert(fs::canonicalize(&path).map_err(|e| e.to_string())?, path);
            }
        }
        parents.remove(&resolved);
        Ok(())
    }
    let mut found = HashMap::new();
    scan(&plugins, &mut HashSet::new(), &mut found)?;
    if found.len() > 1 {
        return Err("Multiple bridge DLLs found. Exit Valheim and remove duplicate bridge copies before continuing.".into());
    }
    let current = found.into_values().next();
    if let Some(path) = &current {
        if hash(&fs::read(path).map_err(|e| e.to_string())?) != expected {
            return Err("Different bridge already installed. Exit Valheim and back up/remove only this plugin before upgrading.".into());
        }
    }
    Ok(current)
}

pub fn install(profile: &Path, dll: &[u8], manifest: &[u8]) -> Result<Value> {
    let profile = fs::canonicalize(profile).map_err(|_| "Choose an existing BepInEx profile")?;
    if !profile.join("BepInEx/core/BepInEx.dll").is_file() {
        return Err("Choose an existing BepInEx profile".into());
    }
    let manifest: Value =
        serde_json::from_slice(manifest).map_err(|_| "Invalid bridge manifest")?;
    let expected = manifest["sha256"]
        .as_str()
        .ok_or("Invalid bridge manifest")?;
    if expected.len() != 64
        || !expected
            .bytes()
            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
    {
        return Err("Invalid bridge manifest".into());
    }
    if hash(dll) != expected {
        return Err("Bridge checksum mismatch".into());
    }
    let plugins = profile.join("BepInEx/plugins");
    safe_directory(&profile.join("BepInEx"))?;
    safe_directory(&plugins)?;
    let target = match installed(&profile, expected)? {
        Some(path) => path,
        None => {
            let directory = plugins.join("swear01-ValheimCliBridge");
            safe_directory(&directory)?;
            let target = directory.join("ValheimCliBridge.dll");
            match fs::OpenOptions::new()
                .write(true)
                .create_new(true)
                .open(&target)
            {
                Ok(mut file) => file.write_all(dll).map_err(|e| e.to_string())?,
                Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => {
                    if linked(&fs::symlink_metadata(&target).map_err(|e| e.to_string())?) {
                        return Err("Existing bridge must not be a symlink".into());
                    }
                    if hash(&fs::read(&target).map_err(|e| e.to_string())?) != expected {
                        return Err(
                            "Different bridge already installed; refusing replacement".into()
                        );
                    }
                }
                Err(e) => return Err(e.to_string()),
            }
            target
        }
    };
    Ok(
        json!({"ok":true,"state":"installed","path":target,"version":manifest["version"],"next":"Restart Valheim. The plugin creates its local token; teleport is disabled by default."}),
    )
}
