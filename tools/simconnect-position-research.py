"""Read-only alternative SimVar acquisition matrix using the installed native SDK.

Requests each variable separately, including STRUCT LATLONALT. Does not expose any
write, camera-acquisition, AI-creation or autopilot function. Saves raw observations.
"""
import collections
import ctypes as c
import datetime as dt
import json
import math
import pathlib
import struct
import time

root = pathlib.Path(__file__).resolve().parents[1]
dll = c.WinDLL(str(root / 'dist/EscortPlane2024/SimConnect.dll'))
handle = c.c_void_p()
u = c.c_uint32
def function(name, args):
    f = getattr(dll, 'SimConnect_' + name)
    f.argtypes, f.restype = args, c.c_int32
    return f
open_sim = function('Open', [c.POINTER(c.c_void_p), c.c_char_p, c.c_void_p, u, c.c_void_p, u])
close = function('Close', [c.c_void_p])
add = function('AddToDataDefinition', [c.c_void_p, u, c.c_char_p, c.c_char_p, u, c.c_float, u])
bytype = function('RequestDataOnSimObjectType', [c.c_void_p, u, u, u, u])
byid = function('RequestDataOnSimObject', [c.c_void_p, u, u, u, u, u, u, u, u])
dispatch = function('GetNextDispatch', [c.c_void_p, c.POINTER(c.c_void_p), c.POINTER(u)])

definitions = {
    1: ('TITLE', None, 9),
    2: ('CATEGORY', None, 9),
    3: ('SIM ON GROUND', 'bool', 4),
    4: ('PLANE LATITUDE', 'degrees', 4),
    5: ('PLANE LONGITUDE', 'degrees', 4),
    6: ('PLANE ALTITUDE', 'meters', 4),
    7: ('STRUCT LATLONALT', None, 15),
    8: ('GROUND VELOCITY', 'knots', 4),
}
out = root / 'artifacts/acquisition-research' / ('native-matrix-' + dt.datetime.now(dt.timezone.utc).strftime('%Y%m%d-%H%M%S') + '.json')
result = {'startedAt': dt.datetime.now(dt.timezone.utc).isoformat(), 'radiusMeters': 37040,
          'definitions': definitions, 'responses': [], 'exceptions': [], 'calls': []}
def check(hr, label):
    result['calls'].append({'call': label, 'hr': hr})
    if hr < 0:
        raise OSError(f'{label}: HRESULT {hr:#x}')

check(open_sim(c.byref(handle), b'Escort read-only position research', None, 0, None, 0), 'Open')
try:
    for ident, (name, units, datatype) in definitions.items():
        check(add(handle, ident, name.encode(), units.encode() if units else None, datatype, 0, 0xffffffff), 'Define ' + name)
    started, next_scan, scan = time.monotonic(), 0, 0
    while time.monotonic() - started < 25:
        now = time.monotonic()
        if now >= next_scan and scan < 3:
            # Each field has an independent request, eliminating unsupported-field interactions.
            for ident in definitions:
                check(bytype(handle, 1000 + scan * 100 + ident, ident, 37040, 1), f'ALL {scan} {ident}')
                check(byid(handle, 2000 + scan * 100 + ident, ident, 0, 1, 0, 0, 0, 0), f'USER {scan} {ident}')
            for typ in (2, 3):
                check(bytype(handle, 3000 + scan * 100 + typ, 3, 185200, typ), f'AIR/HELI100NM {scan} {typ}')
            scan += 1
            next_scan = now + 8
        ptr, size = c.c_void_p(), u()
        for _ in range(1500):
            if dispatch(handle, c.byref(ptr), c.byref(size)) < 0:
                break
            raw = c.string_at(ptr, size.value)
            if len(raw) < 12: continue
            kind = struct.unpack_from('<I', raw, 8)[0]
            if kind == 1 and len(raw) >= 24:
                result['exceptions'].append(dict(zip(('code', 'sendId', 'index'), struct.unpack_from('<III', raw, 12))))
            if kind not in (8, 9) or len(raw) < 40: continue
            req, obj, define, flags, entry, total, count = struct.unpack_from('<7I', raw, 12)
            if define not in definitions: continue
            payload = raw[40:]
            datatype = definitions[define][2]
            if datatype == 9:
                value = payload.split(b'\0', 1)[0].decode('utf-8', errors='replace')
            elif datatype == 15 and len(payload) >= 24:
                value = list(struct.unpack_from('<ddd', payload))
            elif datatype == 4 and len(payload) >= 8:
                value = struct.unpack_from('<d', payload)[0]
            else:
                value = {'unexpectedLength': len(payload)}
            result['responses'].append({'at': round(time.monotonic() - started, 3), 'request': req, 'object': obj, 'definition': define, 'value': value})
        time.sleep(.02)
finally:
    close(handle)
    out.write_text(json.dumps(result, indent=2), encoding='utf-8')

objects, own = {}, {}
counts = collections.Counter()
for r in result['responses']:
    counts[str(r['definition'])] += 1
    target = own if 2000 <= r['request'] < 2300 else objects.setdefault(str(r['object']), {})
    target[definitions[r['definition']][0]] = r['value']
own_ids = {r['object'] for r in result['responses'] if 2000 <= r['request'] < 2300}
candidates = {}
for ident, obj in objects.items():
    if int(ident) in own_ids: continue
    label = str(obj.get('TITLE', '')) + ' ' + str(obj.get('CATEGORY', ''))
    if not obj.get('TITLE') or any(term in label.lower() for term in ('airplane', 'aircraft', 'helicopter', 'fakesim', 'glider')):
        candidates[ident] = obj
print(json.dumps({'file': str(out), 'ownIds': list(own_ids), 'own': own,
    'allObjects': len(objects), 'airOrHeli100NM': sorted({r['object'] for r in result['responses'] if r['request'] >= 3000} - own_ids),
    'countsByDefinition': counts, 'candidateObjects': candidates, 'exceptions': result['exceptions']}, indent=2))
