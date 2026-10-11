"""Build the current managed bridge and checksum manifest for native CLI builds."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
version = ET.parse(ROOT / "bridge/ValheimCliBridge.csproj").findtext(".//Version")
if f'"Valheim CLI Bridge", "{version}"' not in (ROOT / "bridge/Plugin.cs").read_text():
    raise SystemExit("Bridge assembly and plugin versions differ")
subprocess.run([os.environ.get("DOTNET", "dotnet"), "build", str(ROOT / "bridge"), "-c", "Release", "--nologo"], check=True)
dll = ROOT / "bridge/bin/Release/net48/ValheimCliBridge.dll"
destination = ROOT / "bridge-dist"
destination.mkdir(exist_ok=True)
shutil.copyfile(dll, destination / dll.name)
(destination / "manifest.json").write_text(json.dumps({
    "guid": "swear01.ValheimCliBridge", "version": version,
    "sha256": hashlib.sha256(dll.read_bytes()).hexdigest(),
}) + "\n")
print(f"Prepared current bridge {version}")
