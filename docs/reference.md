# ValheimCli reference

For setup and a short example, see the [README](../README.md).

A local CLI and BepInEx bridge for an agent controlling **your own character**.
The agent reads JSON state and game screenshots, then requests game actions.
Controls and console commands run inside Valheim; no OS keyboard/mouse events, shell execution,
LLM, autonomous bot or ValheimMCP dependency is bundled.

**0.4.0 is experimental.** It combines native GUI discovery with isolated Windows
Agent startup. Short Windows Valheim 1.0.17 tests passed character/world selection,
movement, inventory guards, Console and manual pause/resume. Production builds
target Valheim 1.0.16 references; an additional actual 1.0.17 API build passes.
Multiplayer, long sessions and a full playthrough remain unverified.
The published DLL passed automated checks; short live Windows tests used the same
bridge source compiled in a separate test location, not the exact release DLL.

## Install

The game mod requires Valheim and BepInEx 5 for Valheim. CLI **0.5.0** is a native
Rust executable, with no separately installed Node.js, Bun, Rust or .NET runtime.
It embeds the original published **0.4.0 bridge**, its checksum manifest and the
Windows Agent startup script. Existing 0.4.0 bridge installations are reused.
No mod manager or QoL modpack is required. Download the ZIP matching your OS and
CPU from [GitHub Releases](https://github.com/swear01/valheim-cli/releases), extract
it, and run the executable directly or add its folder to PATH:

```sh
valheim install --profile "E:\SteamLibrary\steamapps\common\Valheim"
```

`--profile` means the installation root **containing** `BepInEx`, not a
Gale-specific profile. For a manual installation, use the game folder. With
Gale, r2modman or Thunderstore Mod Manager, use the selected profile's root;
the CLI does not launch or call those managers. Keep using that same root on
every command. Passing the `BepInEx` folder itself adds an extra directory level
and is incorrect.

For manual plugin installation, use `valheim install` to extract the embedded DLL,
then start the game with BepInEx enabled. The installer and Agent launcher recognize the original DLL
filename anywhere under `BepInEx/plugins`, including flat and manager-created
subfolders. A matching existing copy is reused; duplicate or different-version
copies are refused. Linked plugin subdirectories are scanned read-only, with cycles
skipped. A linked `BepInEx`/`plugins` root or bridge DLL is refused; installation
never writes through a linked directory.
Directory aliases pointing to the same physical DLL count as one copy.
Unreadable plugin directories abort installation/launch preflight: skipping them
could hide an existing bridge and create a duplicate. Fix directory permissions
before retrying.
The CLI does not install BepInEx or convert an unmodded game automatically.

The bridge is distributed as [ValheimCli on Thunderstore](https://thunderstore.io/c/valheim/p/swear01/ValheimCli/).
The CLI is distributed through GitHub Releases; no npm registry release is provided.
Exit Valheim before upgrading.
The installer checks the bundled DLL hash and preserves other mods/settings. It
refuses to overwrite a different DLL: back up/remove only that bridge first.
For Thunderstore installation, use the mod manager instead of installing a second
copy through the CLI. Internal GUID/DLL/config filenames remain `ValheimCliBridge`.

On first modded launch the bridge generates:

- `BepInEx/config/swear01.ValheimCliBridge.cfg`
- `BepInEx/config/swear01.ValheimCliBridge.token`

Keep the random token private and out of modpacks. It inherits the profile's OS
permissions. There are no runtime downloads. Only the
computer running the controlled character needs this bridge. A teammate/server
does not need it for ordinary actions. This is not a dedicated-server bot.

## Enable character control

Exit the game, edit `BepInEx/config/swear01.ValheimCliBridge.cfg`, and relaunch:

```ini
[Permissions]
AllowControl = true
AllowTeleport = false
```

If a separate configuration-manager mod is installed, its in-game settings UI
can also change these values. F10 / Mods settings is not supplied by this bridge
and is not a dependency.

Control requires ownership of the local, living character and no
system menu, chat, console, text input, cutscene or teleport. It works for the host
or a joining player without OP. Movement/combat/look are blocked while inventory,
map, store, build selector or other blocking UI is open. UI activation also works
in startup and pause menus; `uiAllowed` reports its separate permission gate.
UI discovery is an authenticated read and remains available when control permission is disabled.
Pages are ordered by hierarchy path; rediscover after a scene or hierarchy change.
Discovery scans active canvases and checks at most 64 targets per page; use it
after UI changes rather than polling every frame.
Settings permission changes apply on the next game update; Enabled / Port changes
need a restart. The bridge enables Unity background execution while loaded.
Commands do not activate the game window or move the OS pointer. While unfocused,
native cursor locking/hiding is suppressed; foreground cursor behavior remains
native. Do not control the same character concurrently.

**F12 while the game is focused cancels actions and revokes AllowControl and AllowConsole.**
Re-enable permissions manually to resume.
`valheim stop` cancels the managed control lease without revoking permission. It
is authenticated, bypasses Unity's request queue, and requires neither permission
nor a host role or game focus. The next game control tick receives a neutral
frame, then normal
human input resumes. The independent 20 ms watchdog expires leases even when the
main thread stalls, but game state cannot update until that thread resumes.

## Observe, act, verify

Use the same `--profile` folder on each command; optional `--port` defaults to 28761.

```sh
valheim status --profile <folder>
valheim players --profile <folder>
valheim observe --profile <folder> --image frame-001.png
valheim input --profile <folder> --move-z 1 --actions run --ms 500 --confirm
valheim look --profile <folder> --yaw 30 --pitch -10 --confirm
valheim stop --profile <folder>
```

`status` includes position, camera Euler angles, health, stamina, death/teleport
state, game focus, permission and control lease activity. `players` returns
currently loaded player objects, not the complete remote server roster.

`observe` adds item name tokens, stacks, quality, equipped state, zero-based grid
positions and a game-only screenshot captured after rendering. Images fit within
1280×720 while retaining aspect ratio. A minimized window may stop rendering;
background execution does not guarantee screenshots while minimized.
`capturedAt` is the image's UTC capture
time; state is queried separately. Images may be reused for at most 500 ms and
are invalidated by actions/teleport. The CLI polls pending screenshot reads, never
writes. `--image` saves a new PNG without overwriting files. JSON contains its
path/dimensions, never base64. Without `--image`, only metadata is printed.
Background/paused rendering may time out explicitly.

`input` returns `started` and an `inputId` before any game tick, not proof of motion
or success. Poll `status` until `inputActive=false`, check `inputState`, then verify
the outcome with position or a fresh image. Another input is refused during a
lease. `look` can adjust aim during a lease without extending it. Discrete actions
and UI events require the lease to finish or be stopped first. `applied` means the
handler ran; original game requirements can still refuse the intended result.

## Action reference

All examples require `--profile <folder> --confirm`. Bindings and OS mouse
sensitivity do not affect these commands.

| Purpose | Command/options |
| --- | --- |
| Walk / sprint | `input --move-z 1 --ms 500` / add `--actions run` |
| Strafe / reverse | `input --move-x -1 --ms 200` / `--move-z -1` |
| Attack / block | `input --actions attack --ms 100` / `--actions block --ms 500` |
| Charge bow | `input --actions attack --ms 2500` (neutral frame releases) |
| Jump / crouch toggle / dodge | `input --actions jump --ms 100` / `crouch` / `dodge` |
| Turn / aim | `look --yaw 30 --pitch -10` |
| Interact with crosshair target | `action --action interact` |
| Use/equip hotbar slot | `action --action slot --slot 1` |
| Toggle inventory / build selector | `action --action inventory` / `--action build-menu` |
| Hide hand items / guardian power | `action --action hide` / `--action guardian` |
| Place selected piece / rotate | `action --action place` / `--action rotate --scroll 1` |
| Open pause menu | `action --action menu` |
| List UI targets (read) | `ui-list [--offset 64]` |
| Activate UI target | `ui --ui-action click --target <id> --button left` |
| Scroll UI target | `ui --ui-action scroll --target <id> --scroll -1` |

Movement X/Z is relative to the player's look, bounded to -1…1; diagonals are
normalized. `actions` accepts comma-separated attack, secondary, block, jump,
crouch, run, dodge. Duration is 50–5000 ms (default 200). Attack/block have press
and hold states; jump/crouch/dodge fire once per lease. Crouch is the game's toggle
and can persist after the lease. A lease is not an autonomous navigation plan.

Yaw/pitch are degree deltas within ±180: positive yaw turns right, positive pitch
looks down. Scroll/rotation accepts nonzero integers within ±10.

Run `ui-list` to get labels, hierarchy paths, component types, supported actions
and interactable/reason fields. This read works before character/world selection
and does not require AllowControl. Use a returned ID with `ui --target <id>`;
activation requires AllowControl and ownership in a world, or startup UI.
Both discovery and activation work while the game is unfocused.
It also works in the pause menu. Movement remains blocked by menus.

IDs belong to the latest page, expire after 30 seconds and are consumed by any
UI action. Re-list before each action, including a second inventory click. Lists
return at most 64 targets and `uiNextOffset` for the next page. Hidden/obstructed
and disabled targets are reported but cannot be activated. The handler rechecks
visibility, interactability, label and object identity immediately before use.

Standard buttons use Unity's `OnSubmit → onClick` path; toggles/custom click
handlers and scroll controls receive their own Unity events. This preserves bound
runtime listeners, including mod listeners, without invoking arbitrary named
methods. Raycasts only check visibility and provide event-local geometry; the
agent supplies no screen coordinates and no OS cursor/key event is emitted.

Discovery covers active Canvas uGUI buttons, toggles, custom click/submit handlers
and scroll handlers, including dynamically added mod objects. Input fields,
IMGUI and UI Toolkit require a separate adapter; no universal mod compatibility
is claimed. Text-only input/creation is not implemented. The coordinate-based
0.2.0 UI interface is replaced in 0.3.0; update callers to use IDs.

Movement/combat modifies arguments at the game's normal `Player.SetControls`
call without skipping the original method. Look uses `Player.SetMouseLook`;
interaction/hotbar/UI use their game methods/events. Placement enters the original
`UpdatePlacement` validation/cost path. Movement/look also consult the native
`PlayerController.TakeInput` gate; world actions consult `Player.TakeInput`.
Closing an open inventory/build selector remains possible through its UI action.
The native input delay suppresses combat/jump/crouch/dodge while allowing movement;
one-shot presses suppressed during that delay are discarded. A held sprint ends
at zero stamina and needs a new input request after recovery. Hide equipment is
refused during attacks/dodges, and the build selector respects its native delay.

Stamina/material costs, combat and physics remain in game methods. This is a
semantic action API: `run` requests held sprint regardless of the user's toggle
binding. It does not reproduce every physical-input path. Mods that patch these
methods or watch character state can participate; mods reading only keyboard
shortcuts need a separate integration. Other Harmony patches may change or skip
game methods, so modpack compatibility still needs live testing. Private input,
interaction/placement/rotation members may change with game updates.

Inventory/workbench/build UIs provide equipment, food, crafting and construction.
There is no autonomous navigation, combat AI, semantic craft planner or proof that
an agent can complete a playthrough. See [the agent guide](agent-play.md) for
an operating loop and pending live acceptance checks.

## Native console

```sh
valheim console --profile <folder> --command "help 2 4" --confirm
```

Enable `AllowConsole = true` separately in the bridge's Permissions settings.
Console commands also work while unfocused. The game console must already be
enabled (for example by launching with `-console`). The bridge does not open the console UI,
change its input field or enable cheats/admin permissions.

The request runs `Console.instance.TryRunCommand(command, false, false)` on the
Unity main thread, using the native registry/parser, context checks and mod patches.
Commands registered by mods use the same entry point; mods watching only input-field
submission need their own integration. The command must be a single trimmed line
of 1–512 characters with no control characters; native argument parsing is preserved.

The JSON result's `state: "submitted"` means the native parser returned, not that
the requested effect succeeded. `consoleOutput` contains only synchronous output
written to that console during the call, including native unknown-command or
permission messages. Output is capped at 32 entries of 256 characters each;
`consoleOutputTruncated` reports clipping. Delayed/server replies and logs written
to other destinations are not captured. Verify consequential results with state
or observation. Exceptions/timeouts have uncertain outcomes and must not be retried
automatically; write UUIDs use the existing replay protection.

`AllowConsole` grants access to all commands normally available to that local
console, including mod commands and native teleport commands. `AllowTeleport`
only restricts the separate typed teleport API. Native command permissions still
apply, but mod code can define its own permissions and side effects. F12 revokes
console permission; `stop` cancels movement, not an executing console command.

## Teleport

```sh
valheim teleport 120 45 -230 --profile <folder> --confirm
```

This separate permission remains host-only and off by default (`AllowTeleport`).
Joining admins cannot teleport through this bridge. Coordinates are X, Y (height),
Z: X/Z within ±10500, Y within -1000…5000. Bounds do not ensure safe ground.
`started` is not arrival: verify `teleporting` and actual position. Use only known
safe destinations in a test/backup world.

## Transport and failure limits

The C# DLL is already the in-game service. An agent with a custom TCP client can
connect directly without this CLI; all permissions, replay checks and game-method
dispatch still run in the DLL. The CLI is a shell adapter, screenshot file writer,
installer and optional Windows launcher. It is not a separate background server.
The transport below is a custom framed JSON protocol, not an HTTP or MCP endpoint.
Shell-only agents need the native CLI or their own protocol client. A DLL cannot
launch Valheim before the game has loaded it.

Only `127.0.0.1:28761` listens. Every operation needs the local 256-bit token and a
UUID. No outbound AI endpoint or world-file editing is added. Never forward the
port. Frames use a four-byte big-endian length plus JSON: requests/ordinary replies
≤64 KiB, images ≤8 MiB. Connections last at most four seconds; the bounded Unity
queue has a two-second deadline. Unity access stays on the main thread; the
network stop callback and expiry timer only update managed lease state.

Use `--request-id <UUID>` on writes to track them. Started/uncertain write IDs are
retained until restart (65,536 maximum); cancelled requests do not consume an ID.
Duplicates are refused. Stop bypasses this cap and invalidates queued controls.
Revocation/loss of control also invalidates queued controls. Restart clears replay
history; a new UUID is a new action. Never retry a timed-out write automatically:
send stop, inspect status, then observe. Cancellation cannot undo damage, resources
already spent, a crouch toggle or a crafting job already started.

## Build and verification

Thunderstore currently assigns one ZIP to each package/version; its manifest has
no OS/CPU asset selector. We keep the managed bridge in one Thunderstore listing
and distribute the native CLI as separate GitHub Release assets. Bundling every
CLI executable in the same Thunderstore ZIP would make everyone download all
platforms, and does not make a mod manager choose or install an external CLI.
See the [manifest implementation](https://github.com/thunderstore-io/Thunderstore/blob/master/django/thunderstore/repository/package_manifest.py)
and [package-version model](https://github.com/thunderstore-io/Thunderstore/blob/master/django/thunderstore/repository/models/package_version.py).

Requires Rust, Python 3.11+ for packaging, and .NET SDK 9 for bridge verification.
These are developer tools, not user dependencies. The preparation script downloads
the original bridge release and verifies both the archive and DLL SHA-256 before
embedding it; rebuilding the C# project is an API check and does not replace that
published DLL. Pinned NuGet references are compile-only and never bundled.
Control uses the game's existing Harmony/Unity UI runtime.

```sh
python3 scripts/prepare-bridge.py
dotnet run --project bridge-tests -c Release
dotnet build bridge -c Release
cargo test --locked
cargo test --locked --test csharp -- --ignored
cargo build --locked --release
```

CI builds and runs tests on Windows x64, Linux x64 and macOS Intel/Apple Silicon.
Linux releases use musl and Windows releases statically link the MSVC runtime.
Tests cover the production protocol, dispatcher, replay protection, Rust-to-C#
TCP, an absolute client deadline, lease expiry, edge/hold actions, hook arguments, local
ownership, background/UI guards, stop epochs, game-action calls, UI events and screenshot
cleanup. Game/UI/Harmony fixtures do not execute real runtime patches or render
real pixels. UI discovery/menu activation, multiplayer and long sessions require
the dedicated test-world checklist.

Sources: [Harmony argument prefixes](https://harmony.pardeike.net/articles/patching-prefix.html),
[Unity UI events](https://docs.unity3d.com/es/530/ScriptReference/EventSystems.ExecuteEvents.html),
[Unity screenshot timing](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/ScreenCapture.CaptureScreenshotAsTexture.html),
[BepInEx plugin guide](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/2_plugin_start.html),
[Valheim references](https://github.com/Digitalroot-Valheim/Digitalroot.Valheim.References).

## Windows Agent mode

Use a separate character and world for unattended testing. After installing this
CLI's bridge and enabling `AllowControl` in a normal launch, exit Valheim:

```powershell
valheim launch-agent --profile "E:\SteamLibrary\steamapps\common\Valheim" --game "E:\SteamLibrary\steamapps\common\Valheim\valheim.exe" --confirm
valheim status --profile "E:\SteamLibrary\steamapps\common\Valheim"
valheim ui-list --profile "E:\SteamLibrary\steamapps\common\Valheim"
```

Run this command in your signed-in Windows desktop session. It refuses an existing
Valheim process, verifies the installed bridge hash, and starts the client on a
new Win32 desktop without switching your input desktop. The rendered GUI is
available through `observe` and native UI commands, but is absent from your normal
Alt+Tab list. Exit through the native Quit button after saving/logging out.
`launched` means process creation succeeded; poll `status` for readiness.

The launcher passes `--valheim-cli-agent`. This makes cursor locking/hiding stay
suppressed even when Unity reports focus within its isolated desktop, pauses the
game audio listener while the bridge is loaded, and retains background execution. It does
not grant action or Console permissions. `foreground` reports **Unity focus**, not
whether the game is on your visible Windows desktop. `agentMode`, `paused` and
`timeScale` report the bridge mode and native simulation state. Manual menus and
cinematics retain their native pause behavior; no automatic unpause is injected.

The GUI renderer and GPU remain active. This is not a headless dedicated server.
Ordinary launching can still take foreground before the plugin loads; use this
launcher for desktop isolation. The launcher uses Windows desktop/process APIs;
gameplay, Console and UI actions continue to use game methods without simulated
OS keyboard or mouse input. Multiplayer, direct video audio and long sessions remain unverified.
