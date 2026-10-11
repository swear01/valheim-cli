mod common;
use common::*;
use serde_json::{Value, json};
use std::{
    io::{BufRead, BufReader},
    path::PathBuf,
    process::{Child, Command, Stdio},
    sync::mpsc,
    thread,
    time::Duration,
};
use valheim_agent_cli::client::{request, uuid};

struct Fixture(Child);
impl Drop for Fixture {
    fn drop(&mut self) {
        let _ = self.0.kill();
        let _ = self.0.wait();
    }
}

#[test]
#[ignore = "build bridge-tests with .NET SDK 9, then run cargo test --test csharp -- --ignored"]
fn native_cli_talks_to_production_csharp_transport_and_dispatcher() {
    let runtime = std::env::var_os("DOTNET_ROOT")
        .map(PathBuf::from)
        .map(|p| {
            p.join(if cfg!(windows) {
                "dotnet.exe"
            } else {
                "dotnet"
            })
        })
        .filter(|p| p.exists())
        .unwrap_or_else(|| "dotnet".into());
    let mut fixture = Fixture(
        Command::new(runtime)
            .args(["bridge-tests/bin/Release/net9.0/BridgeTests.dll", "--serve"])
            .stdout(Stdio::piped())
            .stderr(Stdio::inherit())
            .stdin(Stdio::null())
            .spawn()
            .unwrap(),
    );
    let stdout = fixture.0.stdout.take().unwrap();
    let (send, receive) = mpsc::channel();
    thread::spawn(move || {
        let mut line = String::new();
        let result = BufReader::new(stdout).read_line(&mut line).map(|_| line);
        let _ = send.send(result);
    });
    let port: u16 = receive
        .recv_timeout(Duration::from_secs(10))
        .expect("Bridge startup timeout")
        .unwrap()
        .trim()
        .parse()
        .expect("Fixture did not print a port");
    let token = "a".repeat(64);
    assert_eq!(
        request(port, &token, json!({"operation":"status"})).unwrap()["version"],
        "0.3.0"
    );
    assert_eq!(
        request(port, &token, json!({"operation":"players"})).unwrap()["players"],
        json!(["fixture-player"])
    );
    assert!(
        request(port, &"b".repeat(64), json!({"operation":"status"}))
            .unwrap_err()
            .contains("Unauthorized")
    );
    assert!(request(port, &token, json!({"operation":"pos ; spawn Troll"})).is_err());
    for mut value in [
        json!({"operation":"teleport","x":1,"y":2,"z":3}),
        json!({"operation":"input","moveZ":1,"durationMs":200}),
        json!({"operation":"look","yaw":30,"pitch":-10}),
        json!({"operation":"action","action":"slot","slot":1}),
        json!({"operation":"ui","uiAction":"click","target":"00000000-0000-0000-0000-000000000001"}),
        json!({"operation":"console","command":"help 2 4"}),
    ] {
        value["id"] = uuid().unwrap().into();
        assert_eq!(
            request(port, &token, value.clone()).unwrap()["state"],
            "started"
        );
        assert!(
            request(port, &token, value)
                .unwrap_err()
                .contains("Write ID already used")
        );
    }
    for (coordinate, error, reusable) in [
        (0, "Fixture cancelled", true),
        (-1, "Game operation failed", false),
    ] {
        let mut value =
            json!({"operation":"teleport","id":uuid().unwrap(),"x":coordinate,"y":2,"z":3});
        assert!(
            request(port, &token, value.clone())
                .unwrap_err()
                .contains(error)
        );
        value["x"] = 1.into();
        assert_eq!(request(port, &token, value).is_ok(), reusable);
    }
    for (slot, reusable) in [(8, true), (7, false)] {
        let mut value =
            json!({"operation":"action","id":uuid().unwrap(),"action":"slot","slot":slot});
        assert!(request(port, &token, value.clone()).is_err());
        value["slot"] = 1.into();
        assert_eq!(request(port, &token, value).is_ok(), reusable);
    }
    assert_eq!(
        request(port, &token, json!({"operation":"stop"})).unwrap()["ok"],
        true
    );
    let temp = Temp::new();
    let profile = temp.profile();
    let output = Command::new(env!("CARGO_BIN_EXE_valheim"))
        .args(["status", "--profile"])
        .arg(profile)
        .args(["--port", &port.to_string()])
        .env("PATH", "")
        .output()
        .unwrap();
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    assert_eq!(
        serde_json::from_slice::<Value>(&output.stdout).unwrap()["state"],
        "observed"
    );
}
