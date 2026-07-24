import ctypes, time, winreg, subprocess, sys
from pathlib import Path
from ctypes import wintypes

def ace():
    k=winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,r"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE")
    return tuple(winreg.QueryValueEx(k,n)[0] for n in ("InternalMuxState","InternalMuxIsAutomaticMode","ACESwitchedI2D"))

class Dds(ctypes.Structure):
    _fields_=[("size",ctypes.c_uint32),("supported",ctypes.c_int32),("automatic",ctypes.c_int32),("mux",ctypes.c_int32),("kmd",ctypes.c_int32),("sr",ctypes.c_int32)]

sid=int(sys.argv[1],0)
mux=int(sys.argv[2]); auto=int(sys.argv[3])
kernel32=ctypes.WinDLL("kernel32"); kernel32.SetDllDirectoryW(r"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl")
n=ctypes.WinDLL(r"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvcpl.dll")
# try init variants safely
for label, call in [
    ("init0", lambda: n.NvCplApiInit()),
]:
    try:
        print(label, hex(call() & 0xffffffff))
    except Exception as e:
        print(label, "EX", type(e).__name__)
obj=Dds(size=ctypes.sizeof(Dds), supported=1, automatic=auto, mux=mux)
before=ace()
n.NvCplApiSetSetting.argtypes=[ctypes.c_int, ctypes.c_void_p, ctypes.c_int]
n.NvCplApiSetSetting.restype=ctypes.c_int
try:
    st=n.NvCplApiSetSetting(sid, ctypes.byref(obj), ctypes.sizeof(obj))
    print("set", hex(st & 0xffffffff))
except Exception as e:
    print("set EX", e)
time.sleep(1.5)
after=ace()
print("ace", before, "->", after, "HIT", before!=after)
