"""Summarize positioned native aircraft without inferring multiplayer identity."""
import collections
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else max(
    pathlib.Path('dist/EscortPlane2024/logs').glob('*.jsonl'), key=lambda p: p.stat().st_mtime)
titles, first, last = {}, {}, {}
counts = collections.Counter()
rejected = collections.Counter()
for line in path.read_text(encoding='utf-8-sig').splitlines():
    try:
        row = json.loads(line)
    except json.JSONDecodeError:
        continue
    kind, data = row.get('kind'), row.get('data', {})
    counts[kind] += 1
    if kind == 'traffic_discovered':
        titles[data['objectId']] = data.get('title', '')
    if kind == 'direct_position_result':
        if data['accepted']:
            first.setdefault(data['objectId'], data['position'])
            last[data['objectId']] = data['position']
        else:
            rejected[data.get('issue', 'Unavailable')] += 1
print(json.dumps({'log': str(path), 'positionedIds': len(last), 'rejections': rejected,
    'exceptions': counts['simconnect_exception'], 'commandsSent': counts['guidance_output'],
    'aircraft': [{'id': i, 'title': titles.get(i, ''), 'altitudeFeet': round(p['AltitudeFeet']),
                  'coordinatesChanged': p != first[i]} for i, p in last.items()]}, indent=2))
