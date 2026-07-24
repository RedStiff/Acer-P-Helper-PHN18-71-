"""Foreign-process attempt: NvCplApiSetSetting(0x330)+Execute(0x10000) without Frida."""
from __future__ import annotations

import ctypes
import sys
import time
import winreg
from pathlib import Path

NVCPL_DIR = Path(r"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl")


def ace():
    key = winreg.OpenKey(
        winreg.HKEY_LOCAL_MACHINE,
        r"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE",
    )
    return (
        winreg.QueryValueEx(key, "InternalMuxState")[0],
        winreg.QueryValueEx(key, "InternalMuxIsAutomaticMode")[0],
        winreg.QueryValueEx(key, "ACESwitchedI2D")[0],
    )


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "igpu"
    if mode == "auto":
        mux, automatic = 1, 1
    elif mode == "igpu":
        mux, automatic = 1, 0
    else:
        mux, automatic = 2, 0

    before = ace()
    print("ACE before", before, "target", mode)

    k32 = ctypes.WinDLL("kernel32")
    k32.SetDllDirectoryW(str(NVCPL_DIR))
    n = ctypes.WinDLL(str(NVCPL_DIR / "nvcpl.dll"))

    try:
        print("Init", hex(n.NvCplApiInit() & 0xFFFFFFFF))
    except Exception as e:
        print("Init EX", e)

    class Val(ctypes.Structure):
        _fields_ = [
            ("f0", ctypes.c_uint32),
            ("mux", ctypes.c_uint32),
            ("automatic", ctypes.c_uint32),
            ("pad", ctypes.c_uint32),
        ]

    class Block(ctypes.Structure):
        _fields_ = [("hdr", ctypes.c_uint64 * 2), ("val", Val)]

    block = Block()
    block.val.f0 = 1
    block.val.mux = mux
    block.val.automatic = automatic
    block.val.pad = 0

    Set = n.NvCplApiSetSetting
    Set.argtypes = [ctypes.c_int, ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p]
    Set.restype = ctypes.c_int
    Exec = n.NvCplApiExecute
    Exec.argtypes = [ctypes.c_int]
    Exec.restype = ctypes.c_int

    val_ptr = ctypes.byref(block, ctypes.sizeof(ctypes.c_uint64) * 2)
    st = Set(1, ctypes.byref(block), 0x330, val_ptr)
    print("SetSetting", hex(st & 0xFFFFFFFF))
    er = Exec(0x10000)
    print("Execute(ALL)", hex(er & 0xFFFFFFFF))
    time.sleep(3)
    after = ace()
    print("ACE after", after)
    print("FOREIGN_HIT" if before != after else "FOREIGN_NO_HIT")
    try:
        n.NvCplApiClose()
    except Exception:
        pass
    return 0 if before != after else 2


if __name__ == "__main__":
    raise SystemExit(main())
