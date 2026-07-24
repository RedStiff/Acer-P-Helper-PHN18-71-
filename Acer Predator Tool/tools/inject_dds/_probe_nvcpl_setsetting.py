"""Probe NvCplApiGet/SetSetting with candidate IDs from string xref (0x323/0x330/0x331)."""
from __future__ import annotations

import ctypes
import sys
import time
import winreg
from ctypes import wintypes
from pathlib import Path

NVCPL_DIR = Path(r"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl")
NVCPL = NVCPL_DIR / "nvcpl.dll"
CANDIDATE_IDS = [0x323, 0x330, 0x331, 0x332, 0x32F, 0x300, 0x301]


def ace():
    key = winreg.OpenKey(
        winreg.HKEY_LOCAL_MACHINE,
        r"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE",
    )
    state, _ = winreg.QueryValueEx(key, "InternalMuxState")
    auto, _ = winreg.QueryValueEx(key, "InternalMuxIsAutomaticMode")
    i2d, _ = winreg.QueryValueEx(key, "ACESwitchedI2D")
    return f"state={state}|auto={auto}|i2d={i2d}"


class DdsA(ctypes.Structure):
    _fields_ = [
        ("size", ctypes.c_uint32),
        ("supported", ctypes.c_int32),
        ("automatic", ctypes.c_int32),
        ("mux", ctypes.c_int32),
        ("kmd", ctypes.c_int32),
        ("sr", ctypes.c_int32),
    ]


class DdsB(ctypes.Structure):
    _fields_ = [
        ("supported", ctypes.c_int32),
        ("automatic", ctypes.c_int32),
        ("mux", ctypes.c_int32),
        ("kmd", ctypes.c_int32),
        ("sr", ctypes.c_int32),
    ]


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "get"  # get | set-dgpu | set-igpu | set-auto
    print("ACE before", ace())

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.SetDllDirectoryW(str(NVCPL_DIR))
    nvcpl = ctypes.WinDLL(str(NVCPL))

    # Prototypes — cdecl guesses
    Init = nvcpl.NvCplApiInit
    Init.restype = ctypes.c_int
    Close = nvcpl.NvCplApiClose
    Close.restype = ctypes.c_int
    IsUxd = nvcpl.NvCplApiIsUxdServiceRunning
    IsUxd.restype = ctypes.c_int

    GetSetting = nvcpl.NvCplApiGetSetting
    SetSetting = nvcpl.NvCplApiSetSetting
    GetType = nvcpl.NvCplApiGetSettingType
    EnumIds = nvcpl.NvCplApiEnumSettingIDs

    print("IsUxd ->", IsUxd())
    try:
        print("Init() ->", hex(Init()))
    except Exception as e:
        print("Init EX", e)
    try:
        Init.argtypes = [ctypes.c_void_p]
        print("Init(NULL) ->", hex(Init(None)))
    except Exception as e:
        print("Init(NULL) EX", e)

    # Enum
    buf = (ctypes.c_int * 512)()
    cnt = ctypes.c_int(512)
    EnumIds.argtypes = [ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int)]
    EnumIds.restype = ctypes.c_int
    st = EnumIds(buf, ctypes.byref(cnt))
    print(f"EnumIds st={st:#x} n={cnt.value}")
    ids = [buf[i] for i in range(max(0, min(cnt.value, 512)))]
    if ids:
        print(" first ids", [hex(x) for x in ids[:40]])
        for c in CANDIDATE_IDS:
            print(f"  candidate {hex(c)} in enum? {c in ids}")

    # Try GetSetting signatures
    # A: (id, void* out, int* size)
    # B: (void* ctx, id, void* out)
    # C: (id, void* out)
    for sid in CANDIDATE_IDS:
        for shape_name, shape_cls in (("A", DdsA), ("B", DdsB)):
            for sig in ("id_buf_sz", "id_buf", "buf_id"):
                st_val = -1
                try:
                    obj = shape_cls()
                    if shape_name == "A":
                        obj.size = ctypes.sizeof(shape_cls)
                    sz = ctypes.c_int(ctypes.sizeof(shape_cls))
                    if sig == "id_buf_sz":
                        GetSetting.argtypes = [ctypes.c_int, ctypes.c_void_p, ctypes.POINTER(ctypes.c_int)]
                        GetSetting.restype = ctypes.c_int
                        st_val = GetSetting(sid, ctypes.byref(obj), ctypes.byref(sz))
                    elif sig == "id_buf":
                        GetSetting.argtypes = [ctypes.c_int, ctypes.c_void_p]
                        GetSetting.restype = ctypes.c_int
                        st_val = GetSetting(sid, ctypes.byref(obj))
                    else:
                        GetSetting.argtypes = [ctypes.c_void_p, ctypes.c_int]
                        GetSetting.restype = ctypes.c_int
                        st_val = GetSetting(ctypes.byref(obj), sid)
                    fields = {n: getattr(obj, n) for n, _ in shape_cls._fields_}
                    # Heuristic: mux in {0,1,2}, auto in {0,1}
                    mux = fields.get("mux")
                    auto = fields.get("automatic")
                    interesting = mux in (0, 1, 2) and auto in (0, 1) and st_val == 0
                    if st_val == 0 or interesting:
                        print(
                            f"GET ok? sid={sid:#x} shape={shape_name} sig={sig} st={st_val:#x} fields={fields}"
                        )
                except OSError as e:
                    print(f"GET crash sid={sid:#x} {shape_name}/{sig}: {e}")

    if mode.startswith("set"):
        mux = 2 if "dgpu" in mode else 1
        automatic = 1 if "auto" in mode else 0
        print(f"\nSET mux={mux} auto={automatic}")
        for sid in CANDIDATE_IDS:
            for shape_name, shape_cls in (("A", DdsA), ("B", DdsB)):
                obj = shape_cls()
                if shape_name == "A":
                    obj.size = ctypes.sizeof(shape_cls)
                    obj.supported = 1
                    obj.automatic = automatic
                    obj.mux = mux
                else:
                    obj.supported = 1
                    obj.automatic = automatic
                    obj.mux = mux
                before = ace()
                try:
                    SetSetting.argtypes = [ctypes.c_int, ctypes.c_void_p, ctypes.c_int]
                    SetSetting.restype = ctypes.c_int
                    st_val = SetSetting(sid, ctypes.byref(obj), ctypes.sizeof(shape_cls))
                except Exception:
                    try:
                        SetSetting.argtypes = [ctypes.c_int, ctypes.c_void_p]
                        st_val = SetSetting(sid, ctypes.byref(obj))
                    except Exception as e:
                        print(f"SET EX sid={sid:#x} {shape_name}: {e}")
                        continue
                time.sleep(1.2)
                after = ace()
                hit = before != after
                print(
                    f"SET sid={sid:#x} shape={shape_name} st={st_val:#x} {before} -> {after} HIT={hit}"
                )
                if hit:
                    print("NATIVE_HIT")
                    return 0

    try:
        Close()
    except Exception:
        pass
    print("ACE final", ace())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
