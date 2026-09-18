"""Bounded before/during/after map-binding traffic acquisition comparison."""
import collections
import datetime as dt
import json
import pathlib
import sys
import time

root = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / 'artifacts/python'))
import websocket

view = int(sys.argv[1]) if len(sys.argv) > 1 else 49
sock = websocket.create_connection(f'ws://127.0.0.1:19999/devtools/page/{view}', timeout=5, suppress_origin=True)
request = 0
records = []
def evaluate(script):
    global request
    request += 1
    sock.send(json.dumps({'id': request, 'method': 'Runtime.evaluate', 'params': {'expression': script, 'returnByValue': True}}))
    end = time.monotonic() + 6
    while time.monotonic() < end:
        r = json.loads(sock.recv())
        if r.get('id') != request: continue
        if r.get('error') or r.get('result', {}).get('wasThrown'): raise RuntimeError(str(r))
        return json.loads(r['result']['result']['value'])
    raise TimeoutError('Debugger request')
def script(name): return (root / 'tools' / name).read_text(encoding='utf-8')

try:
    evaluate(script('coherent-airtraffic-research-start.js'))
    for phase, seconds in [('before', 12), ('bound', 24), ('after', 12)]:
        if phase == 'bound': records.append({'phase': 'bind', 'data': evaluate(script('coherent-airtraffic-research-bind.js'))})
        if phase == 'after': records.append({'phase': 'unbind', 'data': evaluate(script('coherent-airtraffic-research-stop.js'))})
        for _ in range(seconds):
            records.append({'phase': phase, 'data': evaluate(script('coherent-airtraffic-research-read.js'))})
            time.sleep(1)
        samples = [r['data'] for r in records if r['phase'] == phase]
        names = sorted({a.get('name', '') for r in samples for obs in r.get('reads', {}).values() for a in obs.get('data', [])})
        print(json.dumps({'phase': phase, 'samples': len(samples), 'names': names, 'binding': samples[-1].get('binding')}, ensure_ascii=True), flush=True)
finally:
    try: records.append({'phase': 'cleanup', 'data': evaluate(script('coherent-airtraffic-research-stop.js'))})
    finally:
        sock.close()
        path = root / 'artifacts/acquisition-research' / ('map-ab-' + dt.datetime.now(dt.timezone.utc).strftime('%Y%m%d-%H%M%S') + '.json')
        path.write_text(json.dumps(records, indent=2), encoding='utf-8')
        print('Saved ' + str(path), flush=True)
