"""Execute the skill's actual shell examples against the C# fixture, never a game."""
import json
import os
from pathlib import Path
import re
import queue
import threading
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent.parent
skill = (ROOT / "skills/valheim-control/SKILL.md").read_text()
assert len(skill.splitlines()) < 200, "Skill must stay under 200 lines"
blocks = re.findall(r"```(powershell|sh)\n(.*?)```", skill, re.S)
kind = "powershell" if os.name == "nt" else "sh"
examples = [body for language, body in blocks if language == kind]
flags = {"creationflags": subprocess.CREATE_NO_WINDOW} if os.name == "nt" else {}
fixture = subprocess.Popen([
    os.environ.get("DOTNET", "dotnet"),
    str(ROOT / "bridge-tests/bin/Release/net9.0/BridgeTests.dll"), "--serve",
], cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, **flags)
try:
    ready = queue.Queue()
    threading.Thread(target=lambda: ready.put(fixture.stdout.readline()), daemon=True).start()
    port = int(ready.get(timeout=10).strip())
    with tempfile.TemporaryDirectory(prefix="valheim skill '") as temporary:
        profile = Path(temporary)
        config = profile / "BepInEx/config"
        config.mkdir(parents=True)
        (config / "swear01.ValheimCliBridge.token").write_text("a" * 64)
        setup = examples[0]
        if kind == "powershell":
            setup = setup.replace("'<folder containing BepInEx>'", "'" + str(profile).replace("'", "''") + "'").replace("$BridgePort = 28761", f"$BridgePort = {port}")
            # Screenshot behavior is separately covered with the game's screenshot fixture;
            # this transport fixture does not render images. Exercise the real action block.
            actions = "\n".join(line + " | ConvertTo-Json -Compress -Depth 8" for line in examples[2].splitlines())
            script = "$ErrorActionPreference = 'Stop'\n" + setup + "\n" + actions
            path = profile / "exercise.ps1"
            path.write_text(script, encoding="utf-8-sig")
            result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-File", str(path)], capture_output=True, text=True, timeout=20, **flags)
            assert result.returncode == 0, result.stderr
        else:
            setup = setup.replace("'<folder containing BepInEx>'", "'" + str(profile).replace("'", "'\"'\"'") + "'").replace("bridge_port=28761", f"bridge_port={port}")
            result = subprocess.run(["sh", "-c", "set -eu\n" + setup + "\n" + examples[1]], capture_output=True, text=True, timeout=20)
            assert result.returncode == 0, result.stderr
        replies = []
        remaining = result.stdout.strip()
        while remaining:
            value, offset = json.JSONDecoder().raw_decode(remaining)
            replies.append(value)
            remaining = remaining[offset:].lstrip()
        assert len(replies) == 4 and all(reply["ok"] for reply in replies), result.stdout
        assert len({reply["id"] for reply in replies}) == 4, "Skill reused a request ID"
        assert [reply["state"] for reply in replies] == ["observed", "started", "observed", "observed"]
        assert "a" * 64 not in result.stdout + result.stderr, "Skill printed token"
        print(f"PASS: skill's {kind} examples call C# HTTP status/input/stop without a CLI or token output")
finally:
    fixture.kill()
    fixture.communicate(timeout=5)
