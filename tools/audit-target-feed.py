"""Compare raw traffic receipts with downstream motion rejection around a lost target."""
import collections
import json
import pathlib
import sys

root = pathlib.Path(__file__).resolve().parents[1]
path = max((root / 'dist/EscortPlane2024/logs').glob('*.jsonl'), key=lambda p: p.stat().st_mtime)
name = sys.argv[1] if len(sys.argv) > 1 else 'Kofi#7382'
raw, motion, messages, labels, errors = [], [], [], [], []
for line in path.read_text(encoding='utf-8-sig').splitlines():
    try:
        row = json.loads(line)
    except json.JSONDecodeError:
        continue
    kind, data = row['kind'], row.get('data', {})
    if kind == 'coherent_traffic' and data.get('Name', '').casefold() == name.casefold():
        raw.append(row)
    elif kind == 'focus_motion' and name.casefold() in data.get('targetName', '').casefold():
        motion.append(row)
    elif kind == 'coherent_bridge_message':
        try:
            message = json.loads(data.get('json', '{}'))
            if message.get('kind') == 'heartbeat':
                messages.append({'at': row['timestamp'], 'count': message.get('count')})
        except json.JSONDecodeError:
            pass
    elif kind == 'nameplate_snapshot':
        for c in data.get('Contacts', []):
            if c['Name'].casefold() == name.casefold():
                labels.append({'at': row['timestamp'], 'distance': c['Distance'], 'altitude': c['Altitude']})
    elif kind in ('coherent_packet_rejected', 'coherent_packet_error', 'bridge_error', 'bridge_maintenance_error'):
        errors.append(row)
end = raw[-1]['timestamp'] if raw else ''
result = {'log': str(path), 'name': name, 'rawSamples': len(raw), 'lastRaw': raw[-1:] or None,
          'motionSamples': len(motion), 'rejections': dict(collections.Counter(r['data'].get('rejection') or 'accepted' for r in motion)),
          'lastMotion': motion[-1:] or None, 'heartbeatsAfterLastRaw': [m for m in messages if m['at'] > end][:12],
          'lastLabels': labels[-8:], 'feedErrors': errors[-4:]}
out = root / 'artifacts/acquisition-research/takeoff-feed-audit.json'
out.write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
