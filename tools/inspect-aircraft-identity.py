from pathlib import Path
import json
import urllib.request

views = json.load(urllib.request.urlopen('http://127.0.0.1:19999/pagelist.json'))
for view in views:
    if 'VCockpit' in view.get('url', ''):
        print({key: view.get(key) for key in ('id', 'title', 'url')})
for package in ('pmdg-aircraft-738', 'pmdg-aircraft-738-liveries'):
    root = Path('G:/MSFS2024/Packages/Community') / package
    for path in root.rglob('aircraft.cfg'):
        text = path.read_text(errors='replace')
        if '737-800 PAX BW HD' in text:
            print(path)
            for line in text.splitlines():
                if any(k in line.lower() for k in ('title', 'manufacturer', 'base_container')):
                    print(line[:180])
