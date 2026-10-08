# Valheim Agent CLI

A small CLI and a separate BepInEx plugin for local, player-owned Valheim automation.
The CLI prints JSON and exits nonzero on errors. An agent that can run shell commands
can call it directly. It does not include an LLM or depend on ValheimMCP.

**Version 0.1.0 is experimental.** The plugin compiles against Valheim 1.0.16 references.
Automated checks exercise the production protocol, authentication and dispatcher,
using a fixture in place of the game. Actual Valheim launch, Unity/Mono behavior,
teleport arrival and multiplayer behavior still require a test world.

## Install

Install Node.js 22 or later on the **same computer that runs Valheim**. Then, once
the package is published to npm:

```sh
npm install -g valheim-agent-cli
valheim install --profile "C:\path\to\Gale\Valheim\profiles\your-profile"
```

Exit the game before installing. `install` copies only the bundled, checksum-verified
`ValheimCliBridge.dll` to `BepInEx/plugins/swear01-ValheimCliBridge/` in an existing
BepInEx profile. It preserves other plugins and settings. Installing the identical
DLL again is harmless; replacing a different DLL requires backing up/removing that
specific plugin first. There are no npm install hooks or downloads during installation.

Alternatively, copy the DLL from `bridge-dist/` manually into that plugin folder.
Gale/r2z exports containing a local DLL require **Import all files**. npm installs the
computer CLI; Gale loads the game DLL. An npm package cannot itself be loaded as a
Valheim mod.

Start the modded game. The plugin creates:

* `BepInEx/config/swear01.ValheimCliBridge.cfg`
* `BepInEx/config/swear01.ValheimCliBridge.token` — a random local credential

Do not commit, share, or put the token in a modpack. Each player generates their own.
The token inherits the profile folder permissions; other programs running as the
same OS user may read it. Restrict access to the profile folder if sharing a computer.

## Use

```sh
valheim status --profile "C:\path\to\profile"
valheim players --profile "C:\path\to\profile"
valheim teleport 120 45 -230 --profile "C:\path\to\profile" --confirm
```

`status` reports whether a local player is in a world, their position, host status,
and whether teleporting is active/allowed. `players` reports **currently loaded
player objects**; it is not a complete server roster for players outside the loaded area.
Read operations work on the local host or a joining client with this plugin installed.

Teleport is disabled by default. To enable it, the host must set:

```ini
[Permissions]
AllowTeleport = true
```

Restart the game after editing configuration. Only the **local host's own living
character** can teleport. Joining clients, including remote admins, cannot issue
writes through this release. This is not an OP-granting tool and does not change
`adminlist.txt`. The teammate does not need this plugin for the host's local CLI.

Coordinates use **X, Y (height), Z**, not console `goto` argument order. X/Z are
limited to ±10500; Y to -1000…5000. Bounds do not prove safe terrain: a bad height can
place you underground or in the air. Test with known positions in a backup/test world.
Valheim and other teleport mods still control loading and arrival safety.

A successful teleport response has `state: "started"`, the observed starting
`position` and requested `destination`. It does **not** mean the character has arrived.
Call `status` afterward to inspect `teleporting` and actual `position`.

## Agent usage and timeout behavior

An agent can call these same commands and parse stdout as JSON. Credentials are read
from the chosen profile, never passed in command-line arguments or printed in errors.
Use `--request-id <UUID>` on writes when preserving an operation ID across a retry.

Never retry a write automatically after a connection failure or timeout. Work still
queued at its deadline is cancelled and cannot run later. Work that already started
can report an unknown outcome; inspect game state before issuing another operation.
The bridge refuses reuse of a write ID during its lifetime and caps writes at 256
per restart. This is not a persistent exactly-once guarantee: restarting clears the
ID history, and using a different UUID represents a new operation.

## Architecture and limits

`bin/valheim.js` → validated CLI arguments → `src/client.js` → loopback TCP →
`bridge/Server.cs` → bounded dispatcher → `Plugin.Execute` on Unity's main thread.

* TCP binds only to `127.0.0.1:28761`; there is no public HTTP/MCP endpoint.
* Each connection carries one big-endian 4-byte length plus one JSON request/response.
* Every request requires the 256-bit random token and a UUID. Unknown/duplicate JSON
  fields, nested input, invalid numbers and unsupported operations are rejected.
* Frames are capped at 64 KiB; connection lifetime at 4 seconds; queue at 8;
  main-thread execution at one job per frame, with a 2-second queue deadline.
* There is no arbitrary console execution, command alias expansion, shell execution,
  world-file editing, outbound networking or background AI process.

Configure `[Bridge] Enabled` or `Port` in the plugin config and restart; use the same
port via CLI `--port`. To disable, exit Valheim and remove only this plugin folder.
For a lost/exposed token, stop the game, delete the token file, and restart to rotate it.
Do not expose or forward the bridge port to a LAN or the internet. Running this CLI
on a different computer is outside this release's scope.

## Build and verify

Requires Node.js 22+ and .NET SDK 9. Compilation uses pinned, compile-only NuGet
references from nuget.org and the official BepInEx feed; reference assemblies are
never shipped with the npm package.

```sh
npm run test:bridge
npm test
npm run build:bridge
npm run pack:bridge
npm pack
```

`npm test` includes a real Node → C# TCP integration check; run `test:bridge` first
to build its C# fixture. The npm tarball contains the CLI, bridge DLL and checksum
manifest, README and license. `prepack` refuses missing/mismatched bridge artifacts.

CI repeats compilation, security/timeout/transport checks and packaging on Windows
and Linux. No automated check claims to launch Valheim. Before using a real world,
verify: load the plugin, query status/loaded players, verify a joining client cannot
teleport, enable host teleport in a test world, check arrival, disable it again, and
confirm your other mods and world saving still work.

Sources: [BepInEx plugin guide](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/2_plugin_start.html),
[Node TCP API](https://nodejs.org/api/net.html),
[Valheim compile references](https://github.com/Digitalroot-Valheim/Digitalroot.Valheim.References).
