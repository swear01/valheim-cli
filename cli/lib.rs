pub mod client;
pub mod install;
mod launch;

use base64::{Engine, engine::general_purpose::STANDARD};
use serde_json::{Map, Value, json};
use std::{collections::HashSet, fs, io::Write, path::PathBuf, thread, time::Duration};

pub type Result<T> = std::result::Result<T, String>;

fn write_new(path: &std::path::Path, bytes: &[u8]) -> std::io::Result<()> {
    let mut file = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(path)?;
    if let Err(error) = file.write_all(bytes) {
        drop(file);
        let _ = fs::remove_file(path);
        return Err(error);
    }
    Ok(())
}

const CONTROLS: &[&str] = &["input", "look", "action", "ui"];
const OPTIONS: &[(&str, &str)] = &[
    ("--actions", "actions"),
    ("--move-x", "moveX"),
    ("--move-z", "moveZ"),
    ("--ms", "durationMs"),
    ("--yaw", "yaw"),
    ("--pitch", "pitch"),
    ("--action", "action"),
    ("--slot", "slot"),
    ("--ui-action", "uiAction"),
    ("--button", "button"),
    ("--scroll", "scroll"),
    ("--target", "target"),
    ("--offset", "offset"),
    ("--command", "command"),
];

pub fn is_uuid(s: &str) -> bool {
    s.len() == 36
        && s.bytes().enumerate().all(|(i, b)| {
            if [8, 13, 18, 23].contains(&i) {
                b == b'-'
            } else {
                b.is_ascii_hexdigit()
            }
        })
}

fn number(s: &str) -> Result<Value> {
    let unsigned = s.strip_prefix('-').unwrap_or(s);
    let mut parts = unsigned.split('.');
    let integer = parts.next().unwrap_or("");
    let fraction = parts.next();
    if integer.is_empty()
        || !integer.bytes().all(|b| b.is_ascii_digit())
        || fraction.is_some_and(|p| p.is_empty() || !p.bytes().all(|b| b.is_ascii_digit()))
        || parts.next().is_some()
    {
        return Err("Invalid number".into());
    }
    let n: f64 = s.parse().map_err(|_| "Invalid number")?;
    if n.is_finite() && n.fract() == 0.0 && n.abs() <= 9007199254740991.0 {
        return Ok(json!(n as i64));
    }
    serde_json::Number::from_f64(n)
        .map(Value::Number)
        .ok_or("Invalid number".into())
}

pub fn validate(input: &Map<String, Value>, operation: &str) -> Result<()> {
    let allowed: &[&str] = match operation {
        "input" => &["actions", "moveX", "moveZ", "durationMs"],
        "look" => &["yaw", "pitch"],
        "action" => &["action", "slot", "scroll"],
        "ui" => &["uiAction", "target", "button", "scroll"],
        "ui-list" => &["offset"],
        _ => return Err("Unexpected action fields".into()),
    };
    if input.keys().any(|k| !allowed.contains(&k.as_str())) {
        return Err("Unexpected action fields".into());
    }
    let n = |k: &str| input.get(k).and_then(Value::as_f64).unwrap_or(0.0);
    let integer = |k: &str, min: f64, max: f64| {
        input
            .get(k)
            .and_then(Value::as_f64)
            .is_some_and(|v| v.fract() == 0.0 && v >= min && v <= max)
    };
    let s = |k: &str| input.get(k).and_then(Value::as_str).unwrap_or("");
    match operation {
        "input" => {
            let actions: Vec<&str> = input
                .get("actions")
                .map(|_| s("actions").split(',').collect())
                .unwrap_or_default();
            if actions.iter().any(|a| {
                ![
                    "attack",
                    "secondary",
                    "block",
                    "jump",
                    "crouch",
                    "run",
                    "dodge",
                ]
                .contains(a)
            }) || actions.iter().collect::<HashSet<_>>().len() != actions.len()
            {
                return Err("Unsupported or repeated game action".into());
            }
            if !integer("durationMs", 50.0, 5000.0) {
                return Err("Input requires duration 50..5000 ms".into());
            }
            if ["moveX", "moveZ"].iter().any(|k| {
                input.contains_key(*k)
                    && !input[*k]
                        .as_f64()
                        .is_some_and(|v| v.is_finite() && v.abs() <= 1.0)
            }) {
                return Err("Movement must be in -1..1".into());
            }
            if n("moveX") == 0.0 && n("moveZ") == 0.0 && actions.is_empty() {
                return Err("Empty input".into());
            }
        }
        "look" => {
            if !input.contains_key("yaw") && !input.contains_key("pitch")
                || ["yaw", "pitch"].iter().any(|k| {
                    input.contains_key(*k)
                        && !input[*k]
                            .as_f64()
                            .is_some_and(|v| v.is_finite() && v.abs() <= 180.0)
                })
            {
                return Err("Look requires degree deltas in -180..180".into());
            }
        }
        "action" => {
            if ![
                "menu",
                "interact",
                "slot",
                "inventory",
                "build-menu",
                "hide",
                "guardian",
                "place",
                "rotate",
            ]
            .contains(&s("action"))
            {
                return Err("Unknown game action".into());
            }
            if if s("action") == "slot" {
                !integer("slot", 1.0, 8.0)
            } else {
                input.contains_key("slot")
            } {
                return Err("Slot requires 1..8 and only applies to slot".into());
            }
            if if s("action") == "rotate" {
                !integer("scroll", -10.0, 10.0) || n("scroll") == 0.0
            } else {
                input.contains_key("scroll")
            } {
                return Err("Rotate requires nonzero scroll in -10..10".into());
            }
        }
        "ui-list" => {
            if input.contains_key("offset") && !integer("offset", 0.0, 100000.0) {
                return Err("Offset must be 0..100000".into());
            }
        }
        "ui" => {
            if !["click", "scroll"].contains(&s("uiAction")) {
                return Err("Unknown UI action".into());
            }
            if !is_uuid(s("target")) {
                return Err("UI requires a target ID from ui-list".into());
            }
            if input.contains_key("button") && !["left", "right", "middle"].contains(&s("button")) {
                return Err("Unknown UI button".into());
            }
            if if s("uiAction") == "scroll" {
                !integer("scroll", -10.0, 10.0) || n("scroll") == 0.0
            } else {
                input.contains_key("scroll")
            } {
                return Err("Scroll requires nonzero -10..10".into());
            }
        }
        _ => unreachable!(),
    }
    Ok(())
}

pub struct Parsed {
    pub profile: PathBuf,
    pub port: u16,
    pub request: Value,
    pub image: Option<PathBuf>,
    pub game: Option<PathBuf>,
}

pub fn parse(args: Vec<String>) -> Result<Parsed> {
    let mut args = args.into_iter();
    let command = args.next().ok_or("Missing command")?;
    if ![
        "launch-agent",
        "install",
        "status",
        "players",
        "teleport",
        "observe",
        "stop",
        "ui-list",
        "console",
        "input",
        "look",
        "action",
        "ui",
    ]
    .contains(&command.as_str())
    {
        return Err("Unknown command. Run valheim --help".into());
    }
    let mut seen = HashSet::new();
    let mut positional = Vec::new();
    let mut fields = Map::new();
    let (mut profile, mut image, mut game, mut id) = (None, None, None, None);
    let (mut confirm, mut port) = (false, 28761);
    let absolute = |s: String| -> Result<PathBuf> {
        Ok(std::env::current_dir().map_err(|e| e.to_string())?.join(s))
    };
    while let Some(option) = args.next() {
        if !option.starts_with("--") {
            positional.push(option);
            continue;
        }
        if ![
            "--profile",
            "--port",
            "--confirm",
            "--request-id",
            "--image",
            "--game",
        ]
        .contains(&option.as_str())
            && !OPTIONS.iter().any(|(o, _)| *o == option)
        {
            return Err(format!("Unknown option: {option}"));
        }
        if !seen.insert(option.clone()) {
            return Err(format!("Duplicate option: {option}"));
        }
        if option == "--confirm" {
            confirm = true;
            continue;
        }
        let value = args
            .next()
            .filter(|s| !s.is_empty() && !s.starts_with("--"))
            .ok_or_else(|| format!("Missing value for {option}"))?;
        match option.as_str() {
            "--profile" => profile = Some(absolute(value)?),
            "--image" => image = Some(absolute(value)?),
            "--game" => game = Some(absolute(value)?),
            "--port" => {
                if !value.bytes().all(|b| b.is_ascii_digit()) {
                    return Err("Invalid port".into());
                }
                port = value.parse().map_err(|_| "Invalid port")?;
            }
            "--request-id" => {
                if !is_uuid(&value) {
                    return Err("Invalid request UUID".into());
                }
                id = Some(value);
            }
            _ => {
                let field = OPTIONS.iter().find(|(o, _)| *o == option).unwrap().1;
                let parsed = if [
                    "actions", "action", "uiAction", "button", "target", "command",
                ]
                .contains(&field)
                {
                    value.into()
                } else {
                    number(&value)?
                };
                fields.insert(field.into(), parsed);
            }
        }
    }
    let profile = profile.ok_or("--profile is required")?;
    if command == "launch-agent" {
        if !confirm
            || game.is_none()
            || !positional.is_empty()
            || seen
                .iter()
                .any(|o| !["--profile", "--game", "--confirm"].contains(&o.as_str()))
        {
            return Err("launch-agent requires only --profile, --game and --confirm".into());
        }
    } else {
        if game.is_some() {
            return Err("--game only applies to launch-agent".into());
        }
        if !CONTROLS.contains(&command.as_str())
            && !["teleport", "console"].contains(&command.as_str())
            && (!positional.is_empty() || confirm || id.is_some())
        {
            return Err("Unexpected arguments".into());
        }
        if !CONTROLS.contains(&command.as_str())
            && !["ui-list", "console"].contains(&command.as_str())
            && !fields.is_empty()
            || command != "observe" && image.is_some()
        {
            return Err("Unexpected arguments".into());
        }
        if CONTROLS.contains(&command.as_str()) {
            if !positional.is_empty() || !confirm {
                return Err("Control commands require --confirm and named options".into());
            }
            if command == "input" {
                fields.entry("durationMs").or_insert(json!(200));
            }
            validate(&fields, &command)?;
        }
        if command == "ui-list" {
            validate(&fields, &command)?;
        }
        if command == "console" {
            let line = fields.get("command").and_then(Value::as_str).unwrap_or("");
            if !positional.is_empty()
                || !confirm
                || fields.len() != 1
                || line.is_empty()
                || line.trim() != line
                || line.encode_utf16().count() > 512
                || line.chars().any(char::is_control)
            {
                return Err("console requires --confirm and --command with one trimmed line of 1..512 characters without control characters".into());
            }
        }
        if command == "install" && seen.contains("--port") {
            return Err("Configure the port in the plugin config after first launch".into());
        }
        if command == "teleport" {
            if positional.len() != 3 || !confirm {
                return Err("teleport requires x y z and --confirm".into());
            }
            let coordinates: Vec<Value> = positional
                .iter()
                .map(|v| number(v))
                .collect::<Result<_>>()?;
            let (x, y, z) = (
                coordinates[0].as_f64().unwrap(),
                coordinates[1].as_f64().unwrap(),
                coordinates[2].as_f64().unwrap(),
            );
            if x.abs() > 10500.0 || z.abs() > 10500.0 || !(-1000.0..=5000.0).contains(&y) {
                return Err("Coordinates outside allowed world bounds".into());
            }
            for (key, value) in ["x", "y", "z"].into_iter().zip(coordinates) {
                fields.insert(key.into(), value);
            }
        }
    }
    fields.insert("operation".into(), command.into());
    if let Some(id) = id {
        fields.insert("id".into(), id.into());
    }
    Ok(Parsed {
        profile,
        port,
        request: fields.into(),
        image,
        game,
    })
}

pub fn run(args: Vec<String>) -> Result<Value> {
    if args.is_empty() || args.len() == 1 && ["help", "--help"].contains(&args[0].as_str()) {
        return Ok(json!({"ok":true,"help":include_str!("help.txt")}));
    }
    if args == ["--version"] {
        return Ok(json!({"ok":true,"version":env!("CARGO_PKG_VERSION"),"bridgeVersion":"0.4.0"}));
    }
    let args = parse(args)?;
    match args.request["operation"].as_str().unwrap() {
        "install" => return install::install(&args.profile, install::DLL, install::MANIFEST),
        "launch-agent" => return launch::launch(&args.profile, args.game.as_deref().unwrap()),
        _ => (),
    }
    let token = fs::read_to_string(args.profile.join("BepInEx/config/swear01.ValheimCliBridge.token"))
        .map_err(|_| "Bridge token not found or unreadable. Start Valheim with the plugin installed, then try again.")?;
    if args.request["operation"] == "observe" {
        return observe(args.port, token.trim(), args.image.as_deref());
    }
    client::request(args.port, token.trim(), args.request)
}

pub fn observe(port: u16, token: &str, path: Option<&std::path::Path>) -> Result<Value> {
    for _ in 0..20 {
        let mut result = client::request(port, token, json!({"operation":"observe"}))?;
        let image = result.as_object_mut().unwrap().remove("image");
        if let Some(image) = image
            .as_ref()
            .and_then(Value::as_str)
            .filter(|s| !s.is_empty())
        {
            if let Some(path) = path {
                let png = STANDARD
                    .decode(image)
                    .map_err(|_| "Invalid screenshot encoding")?;
                if !png.starts_with(b"\x89PNG\r\n\x1a\n") {
                    return Err("Invalid screenshot PNG".into());
                }
                write_new(path, &png).map_err(|e| format!("Cannot save new screenshot: {e}"))?;
                result["imagePath"] = path.to_string_lossy().as_ref().into();
            }
            return Ok(result);
        }
        if let Some(error) = result["captureError"].as_str().filter(|s| !s.is_empty()) {
            return Err(error.into());
        }
        if result["state"] != "capture_pending" {
            return Err("Bridge returned no screenshot".into());
        }
        thread::sleep(Duration::from_millis(50));
    }
    Err("Game did not render a screenshot; check game rendering and try observe again".into())
}
