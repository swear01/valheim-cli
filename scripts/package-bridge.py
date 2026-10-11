"""Package the prepared bridge and portable skill; no native CLI is required."""
import hashlib
import json
from pathlib import Path
import struct
import zipfile

ROOT = Path(__file__).resolve().parent.parent
metadata = json.loads((ROOT / "bridge-dist/manifest.json").read_text())
dll = (ROOT / "bridge-dist/ValheimCliBridge.dll").read_bytes()
if hashlib.sha256(dll).hexdigest() != metadata["sha256"] or metadata["guid"] != "swear01.ValheimCliBridge":
    raise SystemExit("Bridge checksum/identity mismatch; run prepare-bridge.py")
icon = (ROOT / "assets/icon.png").read_bytes()
if icon[:8] != b"\x89PNG\r\n\x1a\n" or struct.unpack(">II", icon[16:24]) != (256, 256):
    raise SystemExit("Thunderstore icon must be a 256x256 PNG")
manifest = {
    "name": "ValheimCli", "version_number": metadata["version"],
    "website_url": "https://github.com/swear01/valheim-cli",
    "description": "Let AI agents control Valheim through a local API. Optional CLI; bring your own agent.",
    "dependencies": ["denikson-BepInExPack_Valheim-5.4.2351"],
}
files = {
    "manifest.json": (json.dumps(manifest, indent=2) + "\n").encode(),
    "README.md": (ROOT / "README.md").read_bytes(),
    "icon.png": icon,
    "BepInEx/plugins/ValheimCliBridge/ValheimCliBridge.dll": dll,
    "skills/valheim-control/SKILL.md": (ROOT / "skills/valheim-control/SKILL.md").read_bytes(),
}
destination = ROOT / "dist"
destination.mkdir(exist_ok=True)
path = destination / f"ValheimCli-{metadata['version']}.zip"
with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
    for name, data in files.items():
        output.writestr(name, data)
with zipfile.ZipFile(path) as output:
    assert set(output.namelist()) == set(files)
    assert all(output.read(name) == data for name, data in files.items())
print(json.dumps({"path": str(path), "bytes": path.stat().st_size, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}))
