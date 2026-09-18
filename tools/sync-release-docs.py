"""Refresh documentation beside the already-built local executable."""
import pathlib
import shutil

root = pathlib.Path(__file__).resolve().parents[1]
destination = root / "dist/EscortPlane2024"
shutil.copy2(root / "README.md", destination / "README.md")
shutil.copy2(root / "docs/EXPERIMENT-RESULTS.md", destination / "EXPERIMENT-RESULTS.md")
(destination / "docs").mkdir(exist_ok=True)
for source in (root / "docs").glob("*.md"):
    shutil.copy2(source, destination / "docs" / source.name)
print("Release documentation updated.")
