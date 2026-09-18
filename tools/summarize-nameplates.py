"""Report the display-only nameplate fallback and whether label text changes."""
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else max(pathlib.Path('dist/EscortPlane2024/logs').glob('*.jsonl'), key=lambda p: p.stat().st_mtime)
snapshots, errors, commands = [], [], 0
for line in path.read_text(encoding='utf-8-sig').splitlines():
    try:
        row = json.loads(line)
    except json.JSONDecodeError:
        continue
    if row['kind'] == 'nameplate_snapshot':
        snapshots.append(row['data'])
    if row['kind'] == 'nameplate_error':
        errors.append(row['data'])
    if row['kind'] == 'guidance_output':
        commands += 1
first = {c['LabelId']: c for c in snapshots[0]['Contacts']} if snapshots else {}
last = snapshots[-1]['Contacts'] if snapshots else []
print(json.dumps({'log': str(path), 'snapshots': len(snapshots), 'nameplates': len(last),
    'labelChanges': sum(c['LabelId'] in first and c != first[c['LabelId']] for c in last),
    'names': [c['Name'] for c in last], 'errors': errors[-3:], 'guidanceCommands': commands}, indent=2))
