"""Print a bounded summary of the latest app log without mistaking labels for positions."""
import collections
import json
import pathlib

root = pathlib.Path(__file__).resolve().parents[1]
path = max((root / 'dist/EscortPlane2024/logs').glob('*.jsonl'), key=lambda p: p.stat().st_mtime)
latest, counts = {}, collections.Counter()
for line in path.read_text(encoding='utf-8-sig').splitlines():
    try: r = json.loads(line)
    except json.JSONDecodeError: continue
    counts[r['kind']] += 1
    latest[r['kind']] = r
contacts = latest.get('nameplate_snapshot', {}).get('data', {}).get('Contacts', [])
print(json.dumps({'log': str(path), 'lastOwn': latest.get('own_telemetry'),
    'standardAutopilot': latest.get('standard_ap_telemetry'),
    'labels': [{k:v for k,v in c.items() if k in ('Name','Model','Distance','Altitude')} for c in contacts],
    'latestPositionedTraffic': latest.get('coherent_traffic'),
    'guidanceOutputs': counts['guidance_output'], 'lastNameplateError': latest.get('nameplate_error'),
    'controlEvents': {k:v for k,v in latest.items() if any(w in k for w in ('follower_', 'guidance_', 'autopilot_command', 'follow_target'))},
    'counts': {k:v for k,v in counts.items() if any(w in k for w in ('nameplate', 'exception', 'coherent_traffic','position_response'))}}, indent=2))
