// shortcut: shells may return early; revisit when Windows 11 24H2 is the minimum.
#![cfg_attr(windows, windows_subsystem = "windows")]

#[cfg(windows)]
fn attach_existing_console() {
    use std::ffi::c_void;
    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn AttachConsole(process_id: u32) -> i32;
        fn GetStdHandle(kind: u32) -> *mut c_void;
        fn GetFileType(handle: *mut c_void) -> u32;
        fn GetConsoleMode(handle: *mut c_void, mode: *mut u32) -> i32;
        fn SetStdHandle(kind: u32, handle: *mut c_void) -> i32;
    }
    // Attach only to the caller's console; never allocate a new window.
    unsafe {
        let redirected = [(-10i32) as u32, (-11i32) as u32, (-12i32) as u32].map(|kind| {
            let handle = GetStdHandle(kind);
            let mut mode = 0;
            let redirected = GetFileType(handle) != 0 && GetConsoleMode(handle, &mut mode) == 0;
            (kind, handle, redirected)
        });
        AttachConsole(u32::MAX);
        // AttachConsole can replace pipes inherited through cmd.exe.
        for (kind, handle, preserve) in redirected {
            if preserve {
                SetStdHandle(kind, handle);
            }
        }
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
