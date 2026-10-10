# Valheim Agent CLI

A local CLI and BepInEx bridge for an agent controlling **your own character**.
No LLM, autonomous bot or ValheimMCP dependency is bundled. A shell-capable agent
uses JSON state and game screenshots to choose ordinary keyboard/mouse actions.

**0.2.0 is experimental and has not been tested in a running game.** The earlier
0.1.0 build was smoke-tested in Windows Valheim 1.0.17, but that does not validate
this release's new input or screenshot implementation. Compilation targets
Valheim 1.0.16. Automated tests use game and input fixtures, not a real player.

## Install

Install Node.js 22+ on the same computer as Valheim. Download the CLI tarball from
[GitHub Releases](https://github.com/swear01/valheim-cli/releases), then install it:

```sh
npm install -g ./valheim-agent-cli-0.2.0.tgz
valheim install --profile "C:\path\to\Gale\profile"
```

An npm registry publication is not implied. Exit Valheim before installing or
upgrading. The installer checks the bundled DLL hash and preserves other mods and
settings. It refuses to overwrite a different existing DLL: back up/remove only
that bridge first. If using the Thunderstore package, let the mod manager install
the DLL instead; do not install a second copy through the CLI. The internal plugin
GUID, DLL and configuration filenames remain `ValheimCliBridge`.

On first modded launch the bridge generates these local files:

- `BepInEx/config/swear01.ValheimCliBridge.cfg`
- `BepInEx/config/swear01.ValheimCliBridge.token`

Keep the random token private and out of modpacks. It inherits the profile's OS
permissions. There are no npm install hooks or runtime downloads. Only the
computer running the controlled character needs this bridge; the teammate and
server do not need it for ordinary input. This is not a dedicated-server bot.

## Enable character control

In F10 / Mods settings → Valheim CLI Bridge, enable **AllowControl**. Alternatively,
exit the game, edit the config and relaunch:

```ini
[Permissions]
AllowControl = true
AllowTeleport = false
```

Normal input supports either the host's or a joining player's own character.
It does not grant OP or bypass stamina, materials, combat, physics or game rules.
Input requires Windows, a living character in a world, and the game's foreground
window. System menu, chat and console block new input. Inventory and build menus
remain available. Permission changes through the configuration manager apply
on the next game update; Enabled / Port changes need a restart.

**Press F12 to release input and revoke AllowControl.** Re-enable it manually to
resume. `valheim stop` also releases input, without revoking the permission. It is
authenticated but needs neither permission nor a host role, and runs independently
of Unity's main-thread dispatcher. Do not play the same character concurrently.

## Observe, act, verify

Use the same `--profile` folder on every command; optional `--port` defaults to 28761.

```sh
valheim status --profile "C:\path\to\profile"
valheim players --profile "C:\path\to\profile"
valheim observe --profile "C:\path\to\profile" --image frame-001.png
valheim input --profile "C:\path\to\profile" --keys W,Shift --ms 500 --confirm
valheim status --profile "C:\path\to\profile"
valheim stop --profile "C:\path\to\profile"
```

`status` includes position, camera Euler angles in degrees, health, stamina,
death/teleport state, foreground status, permission and input activity. `players`
returns currently loaded player objects, not the complete remote server roster.

`observe` adds inventory item name tokens, stacks, quality, equipped state and
zero-based grid positions, plus a game-only screenshot taken after rendering.
Images retain aspect ratio within 1280×720. `capturedAt` is the image's UTC capture
time; state is queried separately. Images can be reused for at most 500 ms and are
invalidated when input or teleport starts. The CLI polls pending screenshot reads,
never write requests. `--image` writes a new PNG file and refuses to overwrite an
existing file. JSON contains the path and dimensions, never base64. Without
`--image`, only observation metadata is printed. Paused/background rendering may
time out; the CLI reports this explicitly.

`mouse` accepts only mouse motion/scroll. It can adjust aim, turn while walking or
drag a held button without extending the existing hold. A mouse-only operation
returns `state: "applied"`; verify its effect with an observation.

`input` returns `state: "started"` with an `inputId`; it does not wait for movement
or prove success. Poll `status` until `inputActive=false` and inspect `inputState`.
Another input is refused while one is active. Verify the position or a fresh
screenshot before deciding the next action. An interrupted input may have already
moved, attacked or consumed something; do not replay it automatically.

## Input reference

Actions use the player's existing bindings. These examples assume default controls;
relative mouse units are OS mouse motion, not degrees. Sensitivity and acceleration
change the result, so use short moves and observe the camera afterward.

| Purpose | Options after `valheim input --profile <folder> --confirm` |
| --- | --- |
| Walk / sprint | `--keys W --ms 500` / `--keys W,Shift --ms 500` |
| Jump / roll | `--keys Space --ms 100` / `--keys Space --buttons right --ms 100` |
| Look / aim | `--mouse-x 120 --mouse-y -30` |
| Attack / block | `--buttons left --ms 100` / `--buttons right --ms 500` |
| Charged bow | `--buttons left --ms 2500` (release fires when equipped and ready) |
| Interact / equip slot | `--keys E --ms 100` / `--keys 1 --ms 100` |
| Inventory / build menu | `--keys Tab --ms 100` / `--buttons right --ms 100` with hammer |
| UI click / drag | `--pointer-x 0.3 --pointer-y 0.4 --buttons left --ms 100` |
| Look / move pointer while held | `valheim mouse --profile <folder> --mouse-x 60 --confirm` or `--pointer-x 0.5 --pointer-y 0.6` |
| Scroll / rotate building | `--scroll 1` |

Pointer coordinates are relative to the game client area: `(0,0)` top left,
`(1,1)` bottom right. Use absolute pointer coordinates in a visible UI, and relative
motion for the camera. They cannot be combined in one request. Holds last
50–5000 ms (default 200); motion is limited to ±2000 per axis, wheel to ±10 notches.
Keys: A–Z, 0–9, Space, Shift, Control, Tab, Escape, Enter, Backspace and arrow names
Left/Up/Right/Down. Keys are comma-separated, case-insensitive, at most eight;
buttons are lowercase left/right/middle. No Alt, Windows or function keys are
accepted. Control may be combined only with Space to avoid system shortcuts.
Keys and buttons are released automatically at the deadline, on focus loss,
permission revocation, F12, plugin unload or `stop`.

The agent can use the normal inventory, workbench and build UIs for equipment,
eating, crafting and construction. There are no semantic `craft`, `build`, combat
AI or navigation commands; providing input does not prove an agent can reliably
complete a boss fight or an entire playthrough. See [the agent play guide](docs/agent-play.md)
for an operating loop and the required live acceptance tests.

## Teleport

```sh
valheim teleport 120 45 -230 --profile "C:\path\to\profile" --confirm
```

This separate permission remains host-only and off by default (`AllowTeleport`).
Joining admins cannot teleport through the bridge. Coordinates are X, Y (height),
Z: X/Z within ±10500, Y within -1000…5000. Bounds do not ensure safe ground.
`state: "started"` is not arrival: check `teleporting` and actual position afterward.
Use only a known safe destination in a test/backup world.

## Transport and failure limits

The CLI connects only to `127.0.0.1:28761`. Every operation requires the local
256-bit token and a UUID. No shell/console execution API, world-file editing,
outbound AI connection or network service is added. Never forward the port.

Frames use a 4-byte big-endian length plus JSON. Requests and ordinary replies are
capped at 64 KiB; image replies at 8 MiB. Connections last at most four seconds,
with a bounded queue and two-second main-thread queue deadline. Input release is
also checked every 20 ms by an independent timer so a Unity stall or disconnected
CLI does not extend a hold past its deadline under normal process operation.

Use `--request-id <UUID>` for input/mouse/teleport when tracking an operation. IDs of
started or uncertain writes are retained until restart (65,536 maximum); confirmed
cancelled requests do not consume an ID. A duplicate write is rejected. `stop`
bypasses both the write-history cap and Unity queue. Accepted but queued controls
are invalidated by stop, revocation or loss of control. A restart clears replay
history; a new UUID is a new action. Never automatically retry a timed-out write:
use `stop`, inspect status and observe the result first.

Windows SendInput targets the foreground application. Each native event checks
that Valheim owns that window, but focus can change between a check and injection.
Keep Valheim foreground and do not use the desktop during agent control. Physical
held modifiers block new actions. A hard process kill/OS crash prevents the
in-process release watchdog from running; physically tap/release any affected keys.
Failed releases remain visible and are retried by the watchdog.

## Build and verification

Requires Node.js 22+ and .NET SDK 9. Compile-only, pinned NuGet references are never
included in the distributable. No new runtime dependency is required for control.

```sh
npm run test:bridge
npm test
npm run build:bridge
npm run pack:bridge
npm pack
```

CI runs on Windows and Linux. Tests exercise the production protocol, dispatcher,
replay guard and Node-to-C# TCP connection; an injected input backend verifies
leases, stop, focus, F12, revocation, failure rollback and release retry. Screenshot
fixtures exercise scheduling and cleanup, not actual GPU capture. The Windows
SendInput structure layout is checked but **no automated test injects real OS
input or launches Valheim**. New controls, UI actions, screenshots, multiplayer
behavior and long sessions still require the dedicated test-world checklist.

Sources: [Windows SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput),
[Unity screenshot timing](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/ScreenCapture.CaptureScreenshotAsTexture.html),
[BepInEx plugin guide](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/2_plugin_start.html),
[Valheim compile references](https://github.com/Digitalroot-Valheim/Digitalroot.Valheim.References).
