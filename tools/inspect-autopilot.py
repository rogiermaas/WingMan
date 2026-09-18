"""Read installed simulator source snippets needed for autopilot integration."""
from pathlib import Path

root = Path('G:/Steam/steamapps/common/MSFS2024/Packages/fs-base-ui/html_ui/Templates/InGameHud/Avionics/Minimap/dist')
for name in ('msfssdk-iife.js', 'Minimap.js'):
    source = (root / name).read_text(encoding='utf-8')
    for needle in ('GET_AIR_TRAFFIC', 'traffic.alt', '.alt,', 'updateIntruder'):
        start = 0
        for _ in range(3):
            index = source.find(needle, start)
            if index < 0:
                break
            print(name, needle, source[max(0, index-300):index+800])
            start = index + len(needle)
