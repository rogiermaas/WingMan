"""Summarize local traffic evidence and compare diagnostic listener routes."""
import collections
import datetime as dt
import json
import math
import pathlib

root = pathlib.Path(__file__).resolve().parents[1]
log = max((root / 'dist/EscortPlane2024/logs').glob('*.jsonl'), key=lambda p: p.stat().st_mtime)
first, last, names, counts = {}, {}, {}, collections.Counter()
first_distance = {}
own = None
for line in log.read_text(encoding='utf-8-sig').splitlines():
    try: r = json.loads(line)
    except json.JSONDecodeError: continue
    kind, d = r['kind'], r['data']
    counts[kind] += 1
    if kind == 'nameplate_snapshot':
        names = {x['Name']: x for x in d['Contacts']}
    if kind == 'coherent_traffic' and d.get('Name'):
        name = d['Name']
        first.setdefault(name, {'time': d.get('ReceivedAt'), 'data': d, 'nameplate': names.get(name)})
        last[name] = {'time': d.get('ReceivedAt'), 'data': d, 'nameplate': names.get(name)}
    if kind == 'own_telemetry': own = d

groups = {}
for path in (root / 'artifacts/coherent').glob('*-coherent-airtraffic-research-read.json'):
    data = json.loads(path.read_text())
    value = data.get('result', {}).get('result', {}).get('value')
    if not value: continue
    v = json.loads(value)
    if 'startedAt' not in v: continue
    group = groups.setdefault(str(v['startedAt']), {'samples': 0, 'routes': {}, 'binding': None, 'sourceFiles': []})
    group['samples'] += 1
    group['sourceFiles'].append(path.name)
    group['binding'] = v.get('binding')
    for route, observation in v.get('reads', {}).items():
        dest = group['routes'].setdefault(route, {'responses': 0, 'empty': 0, 'errors': [], 'names': set(), 'firstNonEmptyAt': None})
        if observation.get('status') == 'received':
            dest['responses'] += 1
            data = observation.get('data', [])
            if not data: dest['empty'] += 1
            elif dest['firstNonEmptyAt'] is None: dest['firstNonEmptyAt'] = observation['at']
            dest['names'].update(x.get('name', '') for x in data)
        elif observation.get('status') == 'error': dest['errors'].append(observation.get('error'))
for group in groups.values():
    for route in group['routes'].values(): route['names'] = sorted(route['names'])
    group['firstSourceFile'], group['lastSourceFile'] = min(group['sourceFiles']), max(group['sourceFiles'])
    del group['sourceFiles']
result = {'log': str(log), 'currentNameplateCount': len(names), 'currentLabels': [{k: v[k] for k in ('Name', 'Model', 'Distance', 'Altitude')} for v in names.values()], 'firstNamedContacts': first, 'lastNamedContacts': last,
          'simconnectExceptions': counts['simconnect_exception'], 'guidanceOutputs': counts['guidance_output'], 'diagnosticGroups': groups}
(root / 'artifacts/acquisition-research/comparison-summary.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
