# Valheim Agent CLI

A local CLI and BepInEx bridge for an agent controlling **your own character**.
The agent reads JSON state and game screenshots, then requests game actions.
Controls run inside Valheim; no OS keyboard/mouse events, shell/console execution,
LLM, autonomous bot or ValheimMCP dependency is bundled.

**0.2.0 is experimental and has not been tested in a running game.** Production
builds target Valheim 1.0.16 references; an additional local build against the
actual 1.0.17 game assembly passed. Neither compilation nor fixtures prove runtime
Harmony, UI, GPU capture, multiplayer or full-playthrough compatibility. Only the
earlier 0.1.0 release was smoke-tested in Windows Valheim 1.0.17.

## Install

Install Node.js 22+ on the same computer as Valheim. Download the experimental CLI
tarball when provided, then install it:

```sh
npm install -g ./valheim-agent-cli-0.2.0.tgz
valheim install --profile "C:\path\to\Gale\profile"
```

0.2.0 is not published to npm or Thunderstore. Exit Valheim before upgrading.
The installer checks the bundled DLL hash and preserves other mods/settings. It
refuses to overwrite a different DLL: back up/remove only that bridge first.
For Thunderstore installation, use the mod manager instead of installing a second
copy through the CLI. Internal GUID/DLL/config filenames remain `ValheimCliBridge`.

On first modded launch the bridge generates:

- `BepInEx/config/swear01.ValheimCliBridge.cfg`
- `BepInEx/config/swear01.ValheimCliBridge.token`

Keep the random token private and out of modpacks. It inherits the profile's OS
permissions. There are no npm install hooks or runtime downloads. Only the
computer running the controlled character needs this bridge. A teammate/server
does not need it for ordinary actions. This is not a dedicated-server bot.

## Enable character control

In F10 / Mods settings → Valheim CLI Bridge, enable **AllowControl**. Alternatively,
exit the game, edit the config and relaunch:

```ini
[Permissions]
AllowControl = true
AllowTeleport = false
```

Control requires ownership of the local, living character, a focused game and no
system menu, chat, console, text input, cutscene or teleport. It works for the host
or a joining player without OP. Movement/combat/look are blocked while inventory,
map, store, build selector or other blocking UI is open. UI commands require an
open inventory, store or build selector. Settings permission changes apply on the
next game update; Enabled / Port changes need a restart. Keep the game focused
and do not control the same character concurrently.

**F12 cancels actions and revokes AllowControl.** Re-enable it manually to resume.
`valheim stop` cancels the managed control lease without revoking permission. It
is authenticated, bypasses Unity's request queue, and requires neither permission
nor a host role. The next game control tick receives a neutral frame, then normal
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
1280×720 while retaining aspect ratio. `capturedAt` is the image's UTC capture
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
| Game UI click | `ui --ui-action click --pointer-x 0.3 --pointer-y 0.4 --button left` |
| Game UI scroll | `ui --ui-action scroll --pointer-x 0.3 --pointer-y 0.4 --scroll -1` |

Movement X/Z is relative to the player's look, bounded to -1…1; diagonals are
normalized. `actions` accepts comma-separated attack, secondary, block, jump,
crouch, run, dodge. Duration is 50–5000 ms (default 200). Attack/block have press
and hold states; jump/crouch/dodge fire once per lease. Crouch is the game's toggle
and can persist after the lease. A lease is not an autonomous navigation plan.

Yaw/pitch are degree deltas within ±180: positive yaw turns right, positive pitch
looks down. UI coordinates use the rendered game area, `(0,0)` top left and `(1,1)`
bottom right. UI dispatch uses Unity EventSystem; it never moves the OS cursor.
Click an inventory item, then its destination to move it. Scroll/rotation accepts
nonzero integers within ±10. Raw `--keys`, `--buttons`, `--mouse-x/y` and `mouse`
are removed from the unreleased 0.2.0 interface.

Movement/combat modifies arguments at the game's normal `Player.SetControls`
call without skipping the original method. Look uses `Player.SetMouseLook`;
interaction/hotbar/UI use their game methods/events. Placement enters the original
`UpdatePlacement` validation/cost path. These actions retain ordinary stamina,
materials, combat and physics rules; no OP or resource bypass is added. The private
interaction/placement/rotation members may change with game updates.

Inventory/workbench/build UIs provide equipment, food, crafting and construction.
There is no autonomous navigation, combat AI, semantic craft planner or proof that
an agent can complete a playthrough. See [the agent guide](docs/agent-play.md) for
an operating loop and pending live acceptance checks.

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

Requires Node.js 22+ and .NET SDK 9. Pinned NuGet references are compile-only and
never bundled. Control uses the game's existing Harmony/Unity UI runtime.

```sh
npm run test:bridge
npm test
npm run build:bridge
npm run pack:bridge
npm pack
```

CI runs on Windows/Linux. Tests cover the production protocol, dispatcher, replay
protection, Node-to-C# TCP, lease expiry, edge/hold actions, hook arguments, local
ownership, focus/UI guards, stop epochs, game-action calls, UI events and screenshot
cleanup. Game/UI/Harmony fixtures do not execute real runtime patches or render
real pixels. New controls, screenshots, multiplayer and long sessions require
the dedicated test-world checklist.

Sources: [Harmony argument prefixes](https://harmony.pardeike.net/articles/patching-prefix.html),
[Unity UI events](https://docs.unity3d.com/es/530/ScriptReference/EventSystems.ExecuteEvents.html),
[Unity screenshot timing](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/ScreenCapture.CaptureScreenshotAsTexture.html),
[BepInEx plugin guide](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/2_plugin_start.html),
[Valheim references](https://github.com/Digitalroot-Valheim/Digitalroot.Valheim.References).
