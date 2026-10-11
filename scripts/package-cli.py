"""Archive one tested native executable; Python is a build-time tool only."""
import hashlib
import json
from pathlib import Path
import sys
import subprocess
import struct
import tomllib
import zipfile

ROOT = Path(__file__).resolve().parent.parent
if len(sys.argv) != 3:
    raise SystemExit("Usage: python3 scripts/package-cli.py <target> <platform-name>")
target, platform = sys.argv[1:]
version = tomllib.loads((ROOT / "Cargo.toml").read_text())["package"]["version"]
executable = "valheim.exe" if "windows" in target else "valheim"
binary = ROOT / "target" / target / "release" / executable
data = binary.read_bytes()
if "windows" in target and not data.startswith(b"MZ"):
    raise SystemExit("Invalid Windows executable")
if "windows" in target:
    pe = struct.unpack_from("<I", data, 0x3c)[0]
    if struct.unpack_from("<H", data, pe + 24 + 68)[0] != 2:
        raise SystemExit("Windows CLI must use the windowless GUI subsystem")
metadata = json.loads(subprocess.check_output([
    "cargo", "metadata", "--locked", "--format-version", "1", "--filter-platform", target,
], cwd=ROOT, text=True))
nodes = {node["id"]: node for node in metadata["resolve"]["nodes"]}
used = set()
def visit(package_id):
    if package_id in used:
        return
    used.add(package_id)
    for dependency in nodes[package_id]["dependencies"]:
        visit(dependency)
visit(metadata["resolve"]["root"])
notices = [(ROOT / "licenses/RUST-LICENSE-MIT.txt").read_text()]
if "musl" in target:
    notices.append((ROOT / "licenses/MUSL-COPYRIGHT.txt").read_text())
for package in metadata["packages"]:
    if package["id"] not in used or not package["source"]:
        continue
    directory = Path(package["manifest_path"]).parent
    license_file = next((directory / name for name in ["LICENSE-MIT", "LICENSE"] if (directory / name).is_file()), None)
    if license_file is None or "MIT" not in (package["license"] or ""):
        raise SystemExit(f"Missing supported license notice: {package['name']}")
    notices.append(f"{package['name']} {package['version']}\n{license_file.read_text()}")
destination = ROOT / "dist"
destination.mkdir(exist_ok=True)
archive = destination / f"valheim-{version}-{platform}.zip"
with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
    output.write(binary, executable)
    output.write(ROOT / "LICENSE", "LICENSE.txt")
    output.write(ROOT / "README.md", "README.md")
    output.write(ROOT / "skills/valheim-control/SKILL.md", "skills/valheim-control/SKILL.md")
    output.writestr("THIRD-PARTY-NOTICES.txt", "\n\n".join(notices))
digest = hashlib.sha256(archive.read_bytes()).hexdigest()
archive.with_suffix(".zip.sha256").write_text(f"{digest}  {archive.name}\n")
print(json.dumps({"platform": platform, "binaryBytes": len(data), "zipBytes": archive.stat().st_size, "sha256": digest}))
