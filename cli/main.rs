#![cfg_attr(windows, windows_subsystem = "windows")]

#[cfg(windows)]
fn attach_existing_console() {
    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn AttachConsole(process_id: u32) -> i32;
    }
    // Attach only to the caller's console; never allocate a new window.
    unsafe {
        AttachConsole(u32::MAX);
    }
}

fn main() {
    #[cfg(windows)]
    attach_existing_console();
    match valheim_agent_cli::run(std::env::args().skip(1).collect()) {
        Ok(value) => println!("{value}"),
        Err(error) => {
            eprintln!("{}", serde_json::json!({"ok": false, "error": error}));
            std::process::exit(1);
        }
    }
}
