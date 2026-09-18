"""Package corresponding WingMan source; deliberately excludes keys, logs and SDK binaries."""
from pathlib import Path
import zipfile

root = Path(__file__).resolve().parents[1]
destination = root / "dist/wingman-server/web/WingMan-source.zip"
destination.parent.mkdir(parents=True, exist_ok=True)
directories = ["src", "relay", "installer", "deploy", "docs", "tools", "assets", "licenses", "tests", ".github"]
files = [root / name for name in ["README.md", "LICENSE.txt", "THIRD-PARTY-NOTICES.md", "build.ps1", "build-wingman.ps1", ".gitignore"]]
for directory in directories:
    for file in (root / directory).rglob("*"):
        if file.is_file() and not any(part in {"bin", "obj", "__pycache__", "node_modules", ".git"} for part in file.parts):
            files.append(file)
with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for file in sorted(set(files)):
        # Local application drafts and operator records are not release documentation.
        if file.is_relative_to(root / 'docs/signpath'): continue
        if file.name in {'FREE-CODE-SIGNING.md', 'WINGMAN-LIVE-DEPLOYMENT.md'}: continue
        if file.name in {"publish-final.py", "publish-update-test.py"}: continue
        if file.name.lower().endswith((".exe", ".dll", ".pdb", ".msi", ".jsonl")) or "private" in file.name.lower(): continue
        if file.suffix.lower() == ".pem" and file.name != "update-public.pem": continue
        archive.write(file, "WingMan/" + file.relative_to(root).as_posix())
print(destination)
