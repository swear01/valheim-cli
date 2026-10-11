fn main() {
    match valheim_agent_cli::run(std::env::args().skip(1).collect()) {
        Ok(value) => println!("{value}"),
        Err(error) => {
            eprintln!("{}", serde_json::json!({"ok": false, "error": error}));
            std::process::exit(1);
        }
    }
}
