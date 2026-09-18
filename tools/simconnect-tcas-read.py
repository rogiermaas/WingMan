"""Read TCAS relative-position SimVars and own GPS through a separate SDK connection. No writes."""
import ctypes as c
import datetime as dt
import json
import pathlib
import struct
import time
root=pathlib.Path(__file__).resolve().parents[1]
dll=c.WinDLL(str(root/'dist/EscortPlane2024/SimConnect.dll'));handle=c.c_void_p();u=c.c_uint32
def bind(name,args):
    f=getattr(dll,'SimConnect_'+name);f.argtypes=args;f.restype=c.c_int32;return f
op=bind('Open',[c.POINTER(c.c_void_p),c.c_char_p,c.c_void_p,u,c.c_void_p,u]);close=bind('Close',[c.c_void_p])
add=bind('AddToDataDefinition',[c.c_void_p,u,c.c_char_p,c.c_char_p,u,c.c_float,u])
request=bind('RequestDataOnSimObject',[c.c_void_p,u,u,u,u,u,u,u,u])
dispatch=bind('GetNextDispatch',[c.c_void_p,c.POINTER(c.c_void_p),c.POINTER(u)])
fields=[('TCAS MODE','number'),('TCAS INTRUDER NETWORK ID','number'),('TCAS INTRUDER BEARING','degrees'),
    ('TCAS INTRUDER DISTANCE','nautical miles'),('TCAS INTRUDER RELATIVE ALTITUDE','feet'),('TCAS INTRUDER VERTICAL SPEED','feet per second'),
    ('PLANE LATITUDE','degrees'),('PLANE LONGITUDE','degrees'),('PLANE ALTITUDE','feet'),('GPS GROUND TRUE TRACK','degrees'),
    ('FLARM AVAILABLE','number'),('FLARM THREAT DISTANCE','meters'),('FLARM THREAT BEARING','degrees')]
out={'at':dt.datetime.now(dt.timezone.utc).isoformat(),'samples':[],'exceptions':[]}
if op(c.byref(handle),b'Escort TCAS read-only research',None,0,None,0)<0:raise RuntimeError('Open failed')
try:
    for i,(name,unit) in enumerate(fields,1):
        if add(handle,i,name.encode(),unit.encode(),4,0,0xffffffff)<0:raise RuntimeError('Define failed: '+name)
        request(handle,i,i,0,4,0,0,0,0)
    end=time.monotonic()+15
    while time.monotonic()<end:
        ptr=c.c_void_p();size=u()
        if dispatch(handle,c.byref(ptr),c.byref(size))>=0:
            raw=c.string_at(ptr,size.value);kind=struct.unpack_from('<I',raw,8)[0]
            if kind==8 and len(raw)>=48:
                ident=struct.unpack_from('<I',raw,12)[0]
                if 1<=ident<=len(fields):out['samples'].append({'at':dt.datetime.now(dt.timezone.utc).isoformat(),'field':fields[ident-1][0],'value':struct.unpack_from('<d',raw,40)[0]})
            if kind==1:out['exceptions'].append(list(struct.unpack_from('<III',raw,12)))
        time.sleep(.005)
finally:close(handle)
destination=root/'artifacts/acquisition-research'/('tcas-native-'+dt.datetime.now(dt.timezone.utc).strftime('%Y%m%d-%H%M%S')+'.json')
destination.write_text(json.dumps(out,indent=2),encoding='utf-8')
print(json.dumps({'file':str(destination),'samples':len(out['samples']),'last':{s['field']:s['value'] for s in out['samples']},'exceptions':out['exceptions']},indent=2))
