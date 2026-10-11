"""Fetch the unchanged, hash-pinned 0.4.0 bridge for native CLI builds."""
import hashlib
import io
import json
from pathlib import Path
import tarfile
import urllib.request

ROOT = Path(__file__).resolve().parent.parent
URL = "https://github.com/swear01/valheim-cli/releases/download/v0.4.0/valheim-agent-cli-0.4.0.tgz"
HASH = "2aa799d136963875b3f469b0ae52959235fce7ba37609d91df5aa8b9f124340f"
DLL_HASH = "35ad8648a5b7824eeb4c849ae10716d0d057bd0514b81cd24efb043cc51c57e2"

with urllib.request.urlopen(URL, timeout=60) as response:
    archive = response.read()
if hashlib.sha256(archive).hexdigest() != HASH:
    raise SystemExit("Release archive checksum mismatch")
with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as archive_file:
    dll = archive_file.extractfile("package/bridge-dist/ValheimCliBridge.dll").read()
    manifest = archive_file.extractfile("package/bridge-dist/manifest.json").read()
metadata = json.loads(manifest)
if hashlib.sha256(dll).hexdigest() != DLL_HASH or metadata["sha256"] != DLL_HASH or metadata["version"] != "0.4.0" or metadata["guid"] != "swear01.ValheimCliBridge":
    raise SystemExit("Bridge release checksum/version mismatch")
destination = ROOT / "bridge-dist"
destination.mkdir(exist_ok=True)
(destination / "ValheimCliBridge.dll").write_bytes(dll)
(destination / "manifest.json").write_bytes(manifest)
print("Prepared original published bridge 0.4.0; game mod unchanged")
