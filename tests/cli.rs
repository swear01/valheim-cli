mod common;
use common::*;
use serde_json::{Value, json};
use std::{
    fs,
    io::{Read, Write},
    net::TcpListener,
    sync::{Arc, Mutex},
    thread,
    time::{Duration, Instant},
};
use valheim_agent_cli::{client, observe, parse, run, validate};

#[test]
fn validation_rejects_unsafe_and_ambiguous_input_before_connecting() {
    for input in [
        vec!["spawn"],
        vec!["status", "--profile"],
        vec!["status", "--profile", ".", "--confirm"],
        vec!["status", "--profile", ".", "--profile", "."],
        vec!["install", "--profile", ".", "--port", "28761"],
        vec!["status", "--profile", ".", "--game", "valheim.exe"],
        vec!["launch-agent", "--profile", ".", "--game", "valheim.exe"],
        vec![
            "launch-agent",
            "--profile",
            ".",
            "--game",
            "valheim.exe",
            "--confirm",
            "--port",
            "28761",
        ],
        vec!["input", "--profile", ".", "--move-z", "1"],
        vec!["input", "--profile", ".", "--keys", "W", "--confirm"],
        vec!["status", "--profile", ".", "--move-z", "1"],
        vec!["status", "--profile", ".", "--image", "frame.png"],
        vec!["stop", "--profile", ".", "--confirm"],
    ] {
        assert!(parse(args(&input)).is_err(), "{input:?}");
    }
    for coordinate in ["0;spawn", "NaN", "1e3", "999999", "1.0.0"] {
        assert!(
            parse(args(&[
                "teleport",
                coordinate,
                "2",
                "3",
                "--profile",
                ".",
                "--confirm"
            ]))
            .is_err()
        );
    }
    for line in [
        "",
        " help",
        "help ",
        "help\ninfo",
        "help\tinfo",
        &"x".repeat(513),
        &"😀".repeat(257),
    ] {
        assert!(
            parse(args(&[
                "console",
                "--profile",
                ".",
                "--command",
                line,
                "--confirm"
            ]))
            .is_err()
        );
    }
    for (value, operation) in [
        (json!({"actions":"" ,"durationMs":200}), "input"),
        (json!({"actions":"jump,jump","durationMs":200}), "input"),
        (json!({"actions":"jump,","durationMs":200}), "input"),
        (json!({"moveZ":1.1,"durationMs":200}), "input"),
        (json!({"moveZ":1,"durationMs":50.5}), "input"),
        (json!({"moveZ":1,"durationMs":5001}), "input"),
        (json!({"yaw":181}), "look"),
        (json!({}), "look"),
        (json!({"action":"slot","slot":0}), "action"),
        (json!({"action":"inventory","slot":1}), "action"),
        (json!({"action":"rotate","scroll":0}), "action"),
        (json!({"offset":-1}), "ui-list"),
        (json!({"offset":1.5}), "ui-list"),
        (json!({"uiAction":"click","target":"bad"}), "ui"),
        (
            json!({"uiAction":"scroll","target":"00000000-0000-0000-0000-000000000001"}),
            "ui",
        ),
    ] {
        assert!(
            validate(value.as_object().unwrap(), operation).is_err(),
            "{operation} {value}"
        );
    }
    assert!(
        run(args(&["--help"])).unwrap()["help"]
            .as_str()
            .unwrap()
            .contains("--confirm")
    );
    assert_eq!(run(args(&["--version"])).unwrap()["version"], "0.5.0");
    assert!(
        run(args(&["status", "--profile", "missing-profile"]))
            .unwrap_err()
            .contains("Start Valheim")
    );
    let movement = parse(args(&[
        "input",
        "--profile",
        ".",
        "--move-z",
        "1",
        "--ms",
        "500.0",
        "--confirm",
    ]))
    .unwrap();
    assert_eq!(movement.request["durationMs"].as_i64(), Some(500));
}

#[test]
fn all_commands_preserve_semantics_without_os_input_fields() {
    let temp = Temp::new();
    let profile = temp.profile();
    let received = Arc::new(Mutex::new(Vec::new()));
    let sink = received.clone();
    let (port, work) = server(9, move |v| {
        sink.lock().unwrap().push(v);
        json!({"state":"applied"})
    });
    let commands = [
        vec![
            "input",
            "--move-z",
            "1",
            "--actions",
            "run",
            "--ms",
            "500",
            "--confirm",
        ],
        vec!["stop"],
        vec!["look", "--yaw", "30", "--pitch", "-10", "--confirm"],
        vec!["action", "--action", "slot", "--slot", "1", "--confirm"],
        vec![
            "ui",
            "--ui-action",
            "click",
            "--target",
            "00000000-0000-0000-0000-000000000001",
            "--confirm",
        ],
        vec!["ui-list", "--offset", "64"],
        vec!["players"],
        vec![
            "console",
            "--command",
            "mod_example \"two words\" -1",
            "--confirm",
        ],
        vec!["teleport", "1", "2", "-3", "--confirm"],
    ];
    for command in commands {
        let mut command = args(&command);
        command.extend([
            "--profile".into(),
            profile.to_str().unwrap().into(),
            "--port".into(),
            port.to_string(),
        ]);
        let result = run(command).unwrap();
        assert!(!result.to_string().contains(&"a".repeat(64)));
    }
    work.join().unwrap();
    let requests = received.lock().unwrap();
    assert_eq!(requests[0]["durationMs"], 500);
    assert_eq!(requests[0]["moveZ"], 1);
    assert_eq!(requests[2]["pitch"], -10);
    assert_eq!(requests[5]["offset"], 64);
    assert_eq!(requests[7]["command"], "mod_example \"two words\" -1");
    assert_eq!(requests[8]["z"], -3);
    assert!(
        requests
            .iter()
            .all(|r| r.get("keys").is_none() && r.get("buttons").is_none())
    );
    let ids: std::collections::HashSet<&str> =
        requests.iter().map(|r| r["id"].as_str().unwrap()).collect();
    assert_eq!(ids.len(), requests.len());
    assert!(ids.iter().all(|s| valheim_agent_cli::is_uuid(s)));
}

#[test]
fn screenshots_poll_only_reads_and_never_overwrite_files() {
    let temp = Temp::new();
    let image = temp.0.join("frame.png");
    let png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jv1cAAAAASUVORK5CYII=";
    let reads = Arc::new(Mutex::new(0));
    let counter = reads.clone();
    let (port, work) = server(4, move |r| {
        assert_eq!(r["operation"], "observe");
        let mut n = counter.lock().unwrap();
        *n += 1;
        if *n == 1 {
            json!({"state":"capture_pending"})
        } else {
            json!({"state":"observed","image":png,"imageWidth":1})
        }
    });
    let result = observe(port, &"a".repeat(64), Some(&image)).unwrap();
    assert!(result.get("image").is_none());
    let bytes = fs::read(&image).unwrap();
    assert!(bytes.starts_with(b"\x89PNG\r\n\x1a\n"));
    assert!(observe(port, &"a".repeat(64), Some(&image)).is_err());
    assert_eq!(fs::read(&image).unwrap(), bytes);
    assert!(
        observe(port, &"a".repeat(64), None)
            .unwrap()
            .get("image")
            .is_none()
    );
    work.join().unwrap();
    let (port, work) = server(
        1,
        |_| json!({"state":"capture_pending","captureError":"Screenshot failed"}),
    );
    assert_eq!(
        observe(port, &"a".repeat(64), None).unwrap_err(),
        "Screenshot failed"
    );
    work.join().unwrap();
    let (port, work) = server(1, |_| json!({"state":"observed","image":"eA=="}));
    assert!(
        observe(port, &"a".repeat(64), Some(&temp.0.join("bad.png")))
            .unwrap_err()
            .contains("PNG")
    );
    work.join().unwrap();
}

#[test]
fn framing_checks_identity_limits_fragmentation_and_unknown_write_outcomes() {
    for scenario in [
        "good",
        "bad-id",
        "refused",
        "oversize",
        "truncated",
        "trailing",
    ] {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        let work = thread::spawn(move || {
            let (mut stream, _) = listener.accept().unwrap();
            let mut header = [0; 4];
            stream.read_exact(&mut header).unwrap();
            let mut body = vec![0; u32::from_be_bytes(header) as usize];
            stream.read_exact(&mut body).unwrap();
            let request: Value = serde_json::from_slice(&body).unwrap();
            let response = json!({"id":if scenario == "bad-id" {json!("wrong")} else {request["id"].clone()},"ok":scenario != "refused","error":"denied","state":"observed"});
            let mut payload = serde_json::to_vec(&response).unwrap();
            let size = if scenario == "oversize" {
                999999
            } else {
                payload.len() as u32
            };
            let header = size.to_be_bytes();
            if scenario == "truncated" {
                payload.clear();
            }
            if scenario == "trailing" {
                payload.push(b'x');
            }
            stream.write_all(&header[..2]).unwrap();
            thread::sleep(Duration::from_millis(5));
            let mut rest = header[2..].to_vec();
            rest.extend(payload);
            let _ = stream.write_all(&rest);
        });
        let result = client::request(port, &"a".repeat(64), json!({"operation":"status"}));
        if scenario == "good" {
            assert_eq!(result.unwrap()["state"], "observed");
        } else {
            assert!(result.is_err(), "{scenario}");
        }
        work.join().unwrap();
    }
    for operation in ["teleport", "input", "console", "look", "action", "ui"] {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        let work = thread::spawn(move || {
            let (mut stream, _) = listener.accept().unwrap();
            let _ = stream.read(&mut [0u8; 1024]);
        });
        assert!(
            client::request(port, &"a".repeat(64), json!({"operation":operation}))
                .unwrap_err()
                .contains("outcome unknown; do not automatically retry")
        );
        work.join().unwrap();
    }
    let big = "x".repeat(100000);
    let (port, work) = server(3, move |_| json!({"image":big}));
    assert!(client::request(port, &"a".repeat(64), json!({"operation":"observe"})).is_ok());
    assert!(client::request(port, &"a".repeat(64), json!({"operation":"status"})).is_err());
    assert!(client::request(port, &"a".repeat(64), json!({"operation":"ui-list"})).is_ok());
    work.join().unwrap();
}

#[test]
fn slow_responses_cannot_extend_the_absolute_deadline() {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    let work = thread::spawn(move || {
        let (mut stream, _) = listener.accept().unwrap();
        let _ = stream.read(&mut [0u8; 1024]);
        for byte in [0, 0, 0, 100, b'{', b'"', b'i', b'd'] {
            if stream.write_all(&[byte]).is_err() {
                break;
            }
            thread::sleep(Duration::from_secs(1));
        }
    });
    let start = Instant::now();
    assert!(client::request(port, &"a".repeat(64), json!({"operation":"status"})).is_err());
    assert!(start.elapsed() < Duration::from_secs(7));
    work.join().unwrap();
}

#[test]
fn standalone_binary_runs_without_a_node_or_bun_search_path() {
    let result = std::process::Command::new(env!("CARGO_BIN_EXE_valheim"))
        .arg("--help")
        .env("PATH", "")
        .output()
        .unwrap();
    assert!(result.status.success());
    assert_eq!(
        serde_json::from_slice::<Value>(&result.stdout).unwrap()["ok"],
        true
    );
}
