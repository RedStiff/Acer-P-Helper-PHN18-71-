import pathlib, re, struct

def guids_in(data, start, end):
    chunk = data[start:end]
    found = []
    for m in re.finditer(rb'\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}', chunk):
        found.append(m.group().decode())
    for m in re.finditer(
        rb'\{(?:[0-9A-Fa-f]\x00){8}\-\x00(?:[0-9A-Fa-f]\x00){4}\-\x00(?:[0-9A-Fa-f]\x00){4}\-\x00(?:[0-9A-Fa-f]\x00){4}\-\x00(?:[0-9A-Fa-f]\x00){12}\}',
        chunk,
    ):
        try:
            found.append(m.group().decode("utf-16le"))
        except Exception:
            pass
    # raw 16-byte GUID structs near MSVC __uuidof data (heuristic: version nibble)
    for i in range(0, max(0, len(chunk) - 16), 4):
        g = chunk[i : i + 16]
        # Data4[0] often 0xC0 for COM, or various
        d1, d2, d3 = struct.unpack_from("<IHH", g, 0)
        if d2 in (0x0000, 0x0001, 0x0010, 0x4xxx if False else d2) and 0 < d1 < 0xFFFFFFFF:
            if d2 in (0x0000, 0x0010, 0x4FE1, 0x874D, 0x1301, 0x4668, 0x4075, 0xC2AD):
                pass
    return sorted(set(found))


def scan(path, needles, radius=256):
    data = pathlib.Path(path).read_bytes()
    print("\n####", path)
    for n in needles:
        for enc, label in ((n.encode("ascii"), "ascii"), (n.encode("utf-16le"), "wide")):
            start = 0
            while True:
                i = data.find(enc, start)
                if i < 0:
                    break
                gs = guids_in(data, max(0, i - radius), min(len(data), i + len(enc) + radius))
                print(f"  {n} {label}@{i:#x} guids={gs}")
                start = i + 1


needles = [
    "IStateData",
    "IStateEvents",
    "ProcessSetSettings",
    "ProcessGetSettings",
    "SyncProxy",
    "IID_IStateData",
]
files = [
    r"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdapix.dll",
    r"C:\Windows\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\Display.NvContainer\plugins\LocalSystem\NvXDCore.dll",
    r"C:\Windows\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\nvcpl.dll",
]
for f in files:
    scan(f, needles)
