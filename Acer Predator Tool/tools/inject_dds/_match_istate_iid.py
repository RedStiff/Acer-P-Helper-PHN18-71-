import pathlib
import struct
import winreg


def load_interfaces():
    root = winreg.OpenKey(winreg.HKEY_CLASSES_ROOT, r"Interface")
    guids = {}
    i = 0
    while True:
        try:
            name = winreg.EnumKey(root, i)
        except OSError:
            break
        i += 1
        try:
            k = winreg.OpenKey(root, name)
            desc, _ = winreg.QueryValueEx(k, None)
            guids[name.upper()] = desc
            winreg.CloseKey(k)
        except OSError:
            pass
    winreg.CloseKey(root)
    return guids


def scan_file(path, ifaces):
    data = pathlib.Path(path).read_bytes()
    print("\n####", path)
    needles = [
        b"IStateData",
        b"IStateEvents",
        "IStateData".encode("utf-16le"),
        "IStateEvents".encode("utf-16le"),
        b"ProcessSetSettings",
    ]
    for needle in needles:
        start = 0
        while True:
            i = data.find(needle, start)
            if i < 0:
                break
            lo = max(0, i - 1024)
            hits = []
            for off in range(lo, i - 15):
                b = data[off : off + 16]
                d1, d2, d3 = struct.unpack_from("<IHH", b, 0)
                g = "{%08X-%04X-%04X-%02X%02X-%02X%02X%02X%02X%02X%02X}" % (
                    d1,
                    d2,
                    d3,
                    b[8],
                    b[9],
                    b[10],
                    b[11],
                    b[12],
                    b[13],
                    b[14],
                    b[15],
                )
                key = g.upper()
                if key in ifaces:
                    hits.append((g, ifaces[key], off))
            label = needle[:40]
            print("  needle", label, "at", hex(i), "hits", len(hits))
            for h in hits[:15]:
                print("   ", h[0], "=", h[1])
            start = i + 1


def main():
    ifaces = load_interfaces()
    print("interfaces", len(ifaces))
    # also print any interface with State/UXD/NvXD in name
    for g, n in sorted(ifaces.items(), key=lambda x: x[1] or ""):
        if n and any(k in n for k in ("StateData", "StateEvents", "NvXD", "UXD", "SyncProxy", "IState")):
            print("IFACE", g, n)

    for f in [
        r"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdapix.dll",
        r"C:\Windows\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\Display.NvContainer\plugins\LocalSystem\NvXDCore.dll",
        r"C:\Windows\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\nvcpl.dll",
    ]:
        scan_file(f, ifaces)


if __name__ == "__main__":
    main()
