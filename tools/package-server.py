"""Package the staged AlmaLinux relay, Apache examples, website and releases."""
from pathlib import Path
import hashlib
import tarfile

root = Path(__file__).resolve().parents[1]
staged = root / "dist/wingman-server"
destination = root / "dist/WingMan-server-almalinux8-x64.tar.gz"

def permissions(info):
    info.uid = info.gid = 0
    info.uname = info.gname = "root"
    info.mode = 0o755 if info.isdir() or info.name.endswith("/wingman-relay") else 0o644
    return info

with tarfile.open(destination, "w:gz") as archive:
    archive.add(staged, arcname="wingman-server", filter=permissions)
digest = hashlib.file_digest(destination.open("rb"), "sha256").hexdigest()
destination.with_suffix(destination.suffix + ".sha256").write_text(digest + "  " + destination.name + "\n", encoding="ascii")
print(destination)
