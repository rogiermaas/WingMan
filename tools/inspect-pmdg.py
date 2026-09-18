import ctypes
import re
from pathlib import Path

path = Path('G:/MSFS2024/Packages/Community/pmdg-aircraft-738/Documentation/SDK/PMDG_NG3_SDK.h')
source = path.read_text()
body = source.split('struct PMDG_NG3_Data')[1].split('};')[0].split('{', 1)[1]
body = re.sub(r'//[^\n]*|/\*.*?\*/', '', body, flags=re.S)
types = {'bool': ctypes.c_uint8, 'unsigned char': ctypes.c_uint8, 'char': ctypes.c_char,
         'unsigned short': ctypes.c_uint16, 'short': ctypes.c_int16, 'float': ctypes.c_float,
         'unsigned int': ctypes.c_uint32, 'int': ctypes.c_int32, 'double': ctypes.c_double}
fields = []
for declaration in body.split(';'):
    declaration = declaration.strip()
    if not declaration:
        continue
    match = re.fullmatch(r'(bool|unsigned char|char|unsigned short|short|float|unsigned int|int|double)\s+(\w+)\s*((?:\[\d+\])*)', declaration)
    if not match:
        raise ValueError(declaration)
    kind, name, dimensions = match.groups()
    field = types[kind]
    for count in reversed(re.findall(r'\[(\d+)\]', dimensions)):
        field *= int(count)
    fields.append((name, field))
class Data(ctypes.Structure):
    _fields_ = fields
print('SDK structure size', ctypes.sizeof(Data))
for name, kind in fields:
    if name.startswith(('MCP_', 'AFS_', 'MAIN_', 'FMC_')) or name == 'AircraftModel':
        print(name, getattr(Data, name).offset, ctypes.sizeof(kind))
for line in source.splitlines():
    if '#define' in line and ('EVT_MCP_' in line or 'MOUSE_FLAG_LEFTSINGLE' in line):
        print(line.strip())
print('Packing directives', [line for line in source.splitlines() if 'pragma' in line])
