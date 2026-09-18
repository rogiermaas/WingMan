"""Capture the existing follower window without activating it or changing controls."""
import ctypes as c
from ctypes import wintypes as w
import pathlib
import struct
import sys
import zlib
u=c.WinDLL('user32');g=c.WinDLL('gdi32')
callback=c.WINFUNCTYPE(w.BOOL,w.HWND,w.LPARAM)
windows=[]
@callback
def visit(hwnd,unused):
    buf=c.create_unicode_buffer(256);u.GetWindowTextW(hwnd,buf,256)
    if buf.value=='Escort Plane 2024':windows.append(hwnd)
    return True
u.EnumWindows.argtypes=[callback,w.LPARAM];u.EnumWindows(visit,0)
if not windows:raise RuntimeError('No Escort window')
hwnd=windows[0];rect=w.RECT()
u.GetWindowRect.argtypes=[w.HWND,c.POINTER(w.RECT)];u.GetWindowRect(hwnd,c.byref(rect))
width,height=rect.right-rect.left,rect.bottom-rect.top
class Header(c.Structure):
    _fields_=[('size',w.DWORD),('width',w.LONG),('height',w.LONG),('planes',w.WORD),('bits',w.WORD),('compression',w.DWORD),('imageSize',w.DWORD),('xppm',w.LONG),('yppm',w.LONG),('used',w.DWORD),('important',w.DWORD)]
g.CreateCompatibleDC.argtypes=[w.HDC];g.CreateCompatibleDC.restype=w.HDC
g.CreateDIBSection.argtypes=[w.HDC,c.POINTER(Header),w.UINT,c.POINTER(c.c_void_p),w.HANDLE,w.DWORD];g.CreateDIBSection.restype=w.HBITMAP
g.SelectObject.argtypes=[w.HDC,w.HANDLE];g.SelectObject.restype=w.HANDLE
g.DeleteObject.argtypes=[w.HANDLE];g.DeleteDC.argtypes=[w.HDC]
u.PrintWindow.argtypes=[w.HWND,w.HDC,w.UINT];u.PrintWindow.restype=w.BOOL
header=Header(c.sizeof(Header),width,-height,1,32,0,0,0,0,0,0)
dc=g.CreateCompatibleDC(None);bits=c.c_void_p();bitmap=g.CreateDIBSection(dc,c.byref(header),0,c.byref(bits),None,0)
if not bitmap:raise RuntimeError('Cannot allocate capture bitmap')
old=g.SelectObject(dc,bitmap)
try:
    if not u.PrintWindow(hwnd,dc,2):raise RuntimeError('PrintWindow failed')
    raw=c.string_at(bits,width*height*4)
finally:
    g.SelectObject(dc,old);g.DeleteObject(bitmap);g.DeleteDC(dc)
rgb=bytearray(width*height*3)
rgb[0::3]=raw[2::4];rgb[1::3]=raw[1::4];rgb[2::3]=raw[0::4]
scan=b''.join(b'\0'+rgb[y*width*3:(y+1)*width*3] for y in range(height))
def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
png=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(scan))+chunk(b'IEND',b'')
filename=pathlib.Path(sys.argv[1]).name if len(sys.argv)>1 else 'stock-787-live-map.png'
path=pathlib.Path(__file__).resolve().parents[1]/'artifacts/acquisition-research'/filename
path.write_bytes(png);print(path)
