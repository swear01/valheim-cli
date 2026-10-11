"""Archive one tested native executable; Python is a build-time tool only."""
import hashlib
import json
from pathlib import Path
import sys
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
destination = ROOT / "dist"
destination.mkdir(exist_ok=True)
archive = destination / f"valheim-{version}-{platform}.zip"
with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
    output.write(binary, executable)
    output.write(ROOT / "LICENSE", "LICENSE.txt")
    output.write(ROOT / "README.md", "README.md")
digest = hashlib.sha256(archive.read_bytes()).hexdigest()
archive.with_suffix(".zip.sha256").write_text(f"{digest}  {archive.name}\n")
print(json.dumps({"platform": platform, "binaryBytes": len(data), "zipBytes": archive.stat().st_size, "sha256": digest}))
