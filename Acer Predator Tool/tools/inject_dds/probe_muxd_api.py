"""Probe NvCpl Muxd/Hybrid APIs after ensuring UXDServiceStarted exists."""
import ctypes
import ctypes.wintypes as w
import time

EVENT_NAME = "Local\\UXDServiceStarted-D40E81C4-06EF-454A-9E81-1F4D55CEBD57"
NVCPL = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvcpl.dll"

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)


def ensure_started_event():
    ctypes.set_last_error(0)
    h = kernel32.OpenEventW(0x00100000, False, EVENT_NAME)
    if h:
        print("UXDServiceStarted already present", h)
        return h
    h = kernel32.CreateEventW(None, True, True, EVENT_NAME)
    print("created UXDServiceStarted", h, ctypes.get_last_error())
    return h


def main():
    eh = ensure_started_event()
    nvcpl = ctypes.WinDLL(NVCPL)
    for name in (
        "NvCplApiIsUxdServiceRunning",
        "NvCplApiMuxdInitialize",
        "NvCplApiMuxdClose",
        "NvCplApiGetHybridMode",
        "NvCplApiInit",
        "NvCplApiClose",
        "NvCplApiMsHybridStatus",
    ):
        try:
            fn = getattr(nvcpl, name)
            print("export", name, fn)
        except AttributeError:
            print("missing", name)

    is_uxd = nvcpl.NvCplApiIsUxdServiceRunning
    is_uxd.restype = w.BOOL
    print("IsUxdServiceRunning ->", is_uxd())

    # MuxdInitialize often takes no args or a void* context; try no-arg first.
    mux_init = nvcpl.NvCplApiMuxdInitialize
    mux_init.restype = ctypes.c_int
    try:
        mux_init.argtypes = []
        rc = mux_init()
        print("MuxdInitialize() ->", rc)
    except Exception as e:
        print("MuxdInitialize() failed", e)
        try:
            mux_init.argtypes = [ctypes.c_void_p]
            rc = mux_init(None)
            print("MuxdInitialize(NULL) ->", rc)
        except Exception as e2:
            print("MuxdInitialize(NULL) failed", e2)

    print("IsUxdServiceRunning after init ->", is_uxd())

    get_hybrid = nvcpl.NvCplApiGetHybridMode
    get_hybrid.restype = ctypes.c_int
    mode = ctypes.c_int(-1)
    try:
        get_hybrid.argtypes = [ctypes.POINTER(ctypes.c_int)]
        rc = get_hybrid(ctypes.byref(mode))
        print("GetHybridMode ->", rc, "mode=", mode.value)
    except Exception as e:
        print("GetHybridMode failed", e)
        try:
            get_hybrid.argtypes = []
            rc = get_hybrid()
            print("GetHybridMode() ->", rc)
        except Exception as e2:
            print("GetHybridMode() failed", e2)

    ms = nvcpl.NvCplApiMsHybridStatus
    ms.restype = ctypes.c_int
    try:
        ms.argtypes = []
        print("MsHybridStatus ->", ms())
    except Exception as e:
        print("MsHybridStatus failed", e)

    # Keep event handle alive briefly for inspection
    time.sleep(2)
    if eh:
        kernel32.CloseHandle(eh)


if __name__ == "__main__":
    main()
