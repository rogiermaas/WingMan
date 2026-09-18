from pathlib import Path
import os

roots = [Path(os.environ['APPDATA']) / 'Microsoft Flight Simulator 2024',
         Path(os.environ['LOCALAPPDATA']) / 'Packages/Microsoft.Limitless_8wekyb3d8bbwe/LocalState',
         Path('G:/MSFS2024/Packages/Community/pmdg-aircraft-738')]
for root in roots:
    if root.exists():
        for path in root.rglob('*Options.ini'):
            print(path)
            print(path.read_text(errors='replace')[:3000])
