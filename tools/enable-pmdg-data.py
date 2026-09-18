"""Enable the installed PMDG SDK telemetry with a dated backup; no simulator restart."""
from pathlib import Path
from datetime import datetime, timezone
import os
import re
import shutil

path = Path(os.environ['APPDATA']) / 'Microsoft Flight Simulator 2024/WASM/MSFS2024/pmdg-aircraft-738/work/737_Options.ini'
text = path.read_text()
section = re.search(r'(?ims)^\[SDK\]\s*\n(.*?)(?=^\[|\Z)', text)
if section and re.search(r'(?im)^EnableDataBroadcast\s*=\s*1\s*$', section.group(1)):
    print('PMDG data broadcast already enabled.')
else:
    backup = path.with_suffix('.ini.escort-' + datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S') + '.bak')
    shutil.copy2(path, backup)
    if section:
        content = re.sub(r'(?im)^EnableDataBroadcast\s*=.*\n?', '', section.group(1))
        text = text[:section.start()] + '[SDK]\nEnableDataBroadcast=1\n' + content + text[section.end():]
    else:
        text = text.rstrip() + '\n\n[SDK]\nEnableDataBroadcast=1\n'
    path.write_text(text)
    print('Enabled PMDG SDK data broadcast:', path)
    print('Backup:', backup)
    print('The aircraft may need reloading before it starts broadcasting.')
