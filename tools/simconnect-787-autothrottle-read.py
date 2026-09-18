"""Read generic and 787 autothrottle flags through native SimConnect; no writes."""
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
op=bind('Open',[c.POINTER(c.c_void_p),c.c_char_p,c.c_void_p,u,c.c_void_p,u])
close=bind('Close',[c.c_void_p]);add=bind('AddToDataDefinition',[c.c_void_p,u,c.c_char_p,c.c_char_p,u,c.c_float,u])
request=bind('RequestDataOnSimObject',[c.c_void_p,u,u,u,u,u,u,u,u])
dispatch=bind('GetNextDispatch',[c.c_void_p,c.POINTER(c.c_void_p),c.POINTER(u)])
fields=['AUTOPILOT THROTTLE ARM','AUTOTHROTTLE ACTIVE','L:AS01B_AUTO_THROTTLE_ARM_STATE']
out={'at':dt.datetime.now(dt.timezone.utc).isoformat(),'samples':[],'exceptions':[]}
if op(c.byref(handle),b'Escort 787 autothrottle readback',None,0,None,0)<0:raise RuntimeError('Open failed')
try:
    for field in fields:
        if add(handle,1,field.encode(),b'number',4,0,0xffffffff)<0:raise RuntimeError('Define failed: '+field)
    if request(handle,1,1,0,4,0,0,0,0)<0:raise RuntimeError('Request failed')
    end=time.monotonic()+5
    while time.monotonic()<end:
        ptr=c.c_void_p();size=u()
        if dispatch(handle,c.byref(ptr),c.byref(size))>=0:
            raw=c.string_at(ptr,size.value);kind=struct.unpack_from('<I',raw,8)[0]
            if kind==8 and len(raw)>=64:out['samples'].append(dict(zip(fields,struct.unpack_from('<ddd',raw,40))))
            if kind==1:out['exceptions'].append(list(struct.unpack_from('<III',raw,12)))
        time.sleep(.02)
finally:close(handle)
(root/'artifacts/acquisition-research/787-autothrottle-native.json').write_text(json.dumps(out,indent=2),encoding='utf-8')
print(json.dumps({'at':out['at'],'sampleCount':len(out['samples']),'last':out['samples'][-1] if out['samples'] else None,'exceptions':out['exceptions']},indent=2))
