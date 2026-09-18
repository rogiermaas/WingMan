"""Launch the published diagnostic app, forwarding optional CLI arguments."""
import pathlib
import subprocess
import sys

root = pathlib.Path(__file__).resolve().parents[1]
process = subprocess.Popen(
    [str(root / "dist/EscortPlane2024/EscortPlane2024.exe"), *sys.argv[1:]],
    cwd=root,
)
print(f"Started EscortPlane2024 PID {process.pid}")
