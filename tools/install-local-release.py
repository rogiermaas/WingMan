"""Install a built local release after the application has been closed."""
from pathlib import Path
import hashlib
import shutil
import sys

root = Path(__file__).resolve().parents[1]
source = (root / sys.argv[1]).resolve()
if not source.is_relative_to(root / 'artifacts'):
    raise ValueError('Release source must be an artifacts directory')
destination = root / 'dist/EscortPlane2024'
backup = root / 'artifacts/previous-local-release'
backup.mkdir(exist_ok=True)
for name in ('EscortPlane2024.exe', 'EscortPlane2024.pdb'):
    target = destination / name
    if target.exists():
        shutil.copy2(target, backup / name)
    shutil.copy2(source / name, target)
    assert hashlib.sha256(target.read_bytes()).digest() == hashlib.sha256((source / name).read_bytes()).digest()
print('Installed and hash-verified local executable and symbols.')
