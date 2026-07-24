"""Find numeric setting id near NVCPLAPI_SETTINGS_DDS_INTERNAL_MUX_STATE in nvcpl.dll."""
from __future__ import annotations

import struct
from pathlib import Path

NVCPL = Path(r"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvcpl.dll")
data = NVCPL.read_bytes()

e = struct.unpack_from("<I", data, 0x3C)[0]
magic = struct.unpack_from("<H", data, e + 24)[0]
nsec = struct.unpack_from("<H", data, e + 6)[0]
opt = struct.unpack_from("<H", data, e + 20)[0]
ib = (
    struct.unpack_from("<Q", data, e + 24 + 24)[0]
    if magic == 0x20B
    else struct.unpack_from("<I", data, e + 24 + 28)[0]
)
secs = []
for i in range(nsec):
    o = e + 24 + opt + i * 40
    name = data[o : o + 8].split(b"\0")[0].decode()
    vsz, va, rsz, ptr = struct.unpack_from("<IIII", data, o + 8)
    secs.append((name, va, max(vsz, rsz), ptr, rsz))


def rva2off(rva):
    for name, va, sz, ptr, rsz in secs:
        if va <= rva < va + sz:
            return ptr + (rva - va)
    return None


def off2rva(off):
    for name, va, sz, ptr, rsz in secs:
        if ptr <= off < ptr + rsz:
            return va + (off - ptr)
    return None


needle = b"NVCPLAPI_SETTINGS_DDS_INTERNAL_MUX_STATE"
pos = data.find(needle)
print("string @", pos, "rva", hex(off2rva(pos) or 0))
rva = off2rva(pos)
abs_addr = ib + rva

# Find lea/rip refs to string
text = next(s for s in secs if s[0].startswith(".text"))
_, tva, tsz, tptr, trsz = text
code = data[tptr : tptr + trsz]
xrefs = []
for i in range(len(code) - 7):
    # lea reg, [rip+disp]
    if code[i] in (0x48, 0x4C) and code[i + 1] == 0x8D and (code[i + 2] & 0xC7) == 0x05:
        disp = struct.unpack_from("<i", code, i + 3)[0]
        target = (tva + i + 7) + disp
        if target == rva:
            xrefs.append(tva + i)
    # push offset / mov reg, imm64 rarer for strings in x64

print("xrefs", [hex(x) for x in xrefs[:20]], "count", len(xrefs))
for xr in xrefs[:8]:
    off = rva2off(xr)
    win = data[off - 96 : off + 96]
    imms = []
    for j in range(len(win) - 4):
        # mov reg, imm32
        if win[j] in (0xB8, 0xB9, 0xBA, 0xBB):
            imms.append(struct.unpack_from("<I", win, j + 1)[0])
        if win[j] == 0x41 and win[j + 1] in (0xB8, 0xB9, 0xBA, 0xBB):
            imms.append(struct.unpack_from("<I", win, j + 2)[0])
        # mov dword ptr [rbp+x], imm32
        if win[j] == 0xC7 and win[j + 1] in (0x45, 0x44, 0x85, 0x84):
            # rough
            if win[j + 1] == 0x45:
                imms.append(struct.unpack_from("<I", win, j + 3)[0])
    uniq = sorted({v for v in imms if 0 < v < 0x10000 or (v & 0xFFFF0000) == 0})
    print(f" @{xr:x} nearby_imm32={[hex(v) for v in uniq[:30]]}")

# Also scan for DDS-related setting name table: string RVA followed by dword id
print("\nNearby dword table scan (±0x200 in .rdata):")
for name, va, sz, ptr, rsz in secs:
    if not name.startswith(".rdata") and name not in (".data", ".rdata"):
        continue
for delta in range(-0x200, 0x200, 4):
    off = pos + delta
    if off < 0 or off + 8 > len(data):
        continue
    # look for pointer-sized ref to our string in nearby structs
    pass

# Search code for cmp/mov with small ids and call to SetSetting export
# Print RTTI / PDB hint leftovers
for s in [
    b"INTERNAL_MUX_STATE",
    b"DDS_INTERNAL",
    b"DDSState",
    b"bIsAutomatic",
    b"MuxState",
]:
    i = data.find(s)
    print(s, i)
