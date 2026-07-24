import struct, pathlib, re

nvcpl = pathlib.Path(r"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvcpl.dll")
data = nvcpl.read_bytes()


def pe_exports(blob: bytes):
    e_lfanew = struct.unpack_from("<I", blob, 0x3C)[0]
    magic = struct.unpack_from("<H", blob, e_lfanew + 24)[0]
    if magic == 0x20B:
        exp_rva = struct.unpack_from("<I", blob, e_lfanew + 24 + 112)[0]
    else:
        exp_rva = struct.unpack_from("<I", blob, e_lfanew + 24 + 96)[0]
    nsec = struct.unpack_from("<H", blob, e_lfanew + 6)[0]
    opt = struct.unpack_from("<H", blob, e_lfanew + 20)[0]
    sec_off = e_lfanew + 24 + opt
    secs = []
    for i in range(nsec):
        o = sec_off + i * 40
        vsz, va, rsz, ptr = struct.unpack_from("<IIII", blob, o + 8)
        secs.append((va, max(vsz, rsz), ptr))

    def rva2off(rva):
        for va, sz, ptr in secs:
            if va <= rva < va + sz:
                return ptr + (rva - va)
        return None

    off = rva2off(exp_rva)
    _ch, _ts, _ma, _mi, _name, base, _nfunc, nnames, afuncs, anames, aords = struct.unpack_from(
        "<IIHHIIIIIII", blob, off
    )
    out = []
    for i in range(nnames):
        nrva = struct.unpack_from("<I", blob, rva2off(anames) + 4 * i)[0]
        o = rva2off(nrva)
        nm = blob[o : blob.index(b"\0", o)].decode()
        ord_ = struct.unpack_from("<H", blob, rva2off(aords) + 2 * i)[0]
        frva = struct.unpack_from("<I", blob, rva2off(afuncs) + 4 * ord_)[0]
        out.append((nm, base + ord_, frva))
    return out


exps = pe_exports(data)
keys = ("Dds", "DDS", "Mux", "Hybrid", "Setting", "Enum", "Init", "Execute", "Manage", "GetInfo", "Close")
for nm, ord_, frva in sorted(exps):
    if any(k in nm for k in keys):
        print(f"{nm} ord={ord_} rva={frva:x}")

print("\n=== SETTINGS string order (DDS neighborhood) ===")
pat = re.compile(rb"NVCPLAPI_SETTINGS_[A-Z0-9_]+")
seen = []
pos = 0
while True:
    m = pat.search(data, pos)
    if not m:
        break
    s = m.group().decode()
    if s not in seen:
        seen.append(s)
    pos = m.end()

for i, s in enumerate(seen):
    if "DDS" in s or "MUX" in s or "HYBRID" in s:
        for j in range(max(0, i - 8), min(len(seen), i + 9)):
            mark = " <<<<" if j == i else ""
            print(f"  {j:4d} {seen[j]}{mark}")
        print("---")

print("total unique settings strings", len(seen))

# Also NVFLAGS_
print("\n=== NVFLAGS with DDS/HYBRID ===")
for m in re.finditer(rb"NVFLAGS_[A-Z0-9_]+", data):
    s = m.group().decode()
    if any(x in s for x in ("DDS", "HYBRID", "MUX", "SYSTEM")):
        print(s)
