# ValheimCli

Let AI agents read game state, take screenshots and control your character through a local CLI.
Movement, combat, UI buttons and console commands use game methods without moving your mouse or taking window focus.
Bring your own agent; no AI model is included.

## Install

1. Install **ValheimCli** and **BepInEx** through your mod manager, or extract the [Thunderstore download](https://thunderstore.io/c/valheim/p/swear01/ValheimCli/) into your BepInEx installation. Only the player being controlled needs the mod.
2. Download the CLI ZIP for **Windows x64**, **Linux x64**, or **macOS Intel / Apple Silicon** from [GitHub Releases](https://github.com/swear01/valheim-cli/releases). Extract it and run `valheim.exe` on Windows or `./valheim` on Linux/macOS. Add its folder to PATH to use the `valheim` examples below from anywhere.

**No Node.js, Bun or Rust installation is needed.** The native CLI includes its bridge installer and Windows startup script. The C# mod serves game commands locally; the CLI connects to it. Both run on the game computer, independently of your mod manager or modpack.
On Windows, the CLI does not open a console window; JSON output still works in an existing terminal or through captured pipes.
Exit Valheim before upgrading, and remove the old `ValheimCliBridge` mod if installed.

## Enable control

Launch the modded game once, exit, then edit `BepInEx/config/swear01.ValheimCliBridge.cfg`:

```ini
[Permissions]
AllowControl = true
```

Relaunch the modded game. Console access is optional: set `AllowConsole = true` and launch with `-console`. Normal command permissions still apply.

## Use

Replace `<folder>` with the folder **containing** `BepInEx`: your game folder for a manual install, or your mod manager's profile folder.

```sh
valheim status --profile "<folder>"
valheim observe --profile "<folder>" --image frame-001.png
valheim input --profile "<folder>" --move-z 1 --ms 300 --confirm
valheim stop --profile "<folder>"
```

These check the connection, capture a screenshot, walk forward briefly and stop movement. Check `status` and a fresh screenshot after an action to verify the result; use a new image filename each time.

Use `valheim ui-list` to discover buttons, `valheim ui` to activate them, and `valheim console` to run a command directly. All need the same `--profile`; UI actions and console commands also need `--confirm`. Run `valheim --help` for all options.

## Windows Agent mode

After setup, exit Valheim and run:

```sh
valheim launch-agent --profile "<folder>" --game "<path/to/valheim.exe>" --confirm
```

This starts the game on an isolated Windows desktop. No game window appears on your normal desktop; use `status` to check readiness and the CLI to interact. The game still renders and uses the GPU.

## Before you play

- Experimental: multiplayer and long sessions remain unverified. Start in a test world.
- Keep `swear01.ValheimCliBridge.token` private. Connections are local only.
- `stop` cancels movement; **F12 in the focused game** also disables control and console access. Neither undoes completed actions. After a timeout, inspect state before trying again.
- Standard Unity UI buttons are supported, including compatible mod buttons. Text entry and some custom UI are unsupported.

[Full reference](https://github.com/swear01/valheim-cli/blob/main/docs/reference.md) · [Agent guide](https://github.com/swear01/valheim-cli/blob/main/docs/agent-play.md) · [Report a problem](https://github.com/swear01/valheim-cli/issues)
