"""Reverse NvCplSetDDSState call chain in NvCplDisplayPlugin.dll and SetDisplayMux ID in nvxdapix."""
import struct
import pathlib
import re

plugin = pathlib.Path(
    r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\plugins\Base\NvCplDisplayPlugin.dll"
)
data = plugin.read_bytes()


def pe_info(blob):
    e = struct.unpack_from("<I", blob, 0x3C)[0]
    magic = struct.unpack_from("<H", blob, e + 24)[0]
    nsec = struct.unpack_from("<H", blob, e + 6)[0]
    opt = struct.unpack_from("<H", blob, e + 20)[0]
    image_base = (
        struct.unpack_from("<Q", blob, e + 24 + 24)[0]
        if magic == 0x20B
        else struct.unpack_from("<I", blob, e + 24 + 28)[0]
    )
    secs = []
    for i in range(nsec):
        o = e + 24 + opt + i * 40
        name = blob[o : o + 8].split(b"\0")[0].decode("ascii", "ignore")
        vsz, va, rsz, ptr = struct.unpack_from("<IIII", blob, o + 8)
        secs.append((name, va, max(vsz, rsz), ptr, rsz))
    return image_base, secs


def rva2off(secs, rva):
    for name, va, sz, ptr, rsz in secs:
        if va <= rva < va + sz:
            return ptr + (rva - va)
    return None


def off2rva(secs, off):
    for name, va, sz, ptr, rsz in secs:
        if ptr <= off < ptr + rsz:
            return va + (off - ptr)
    return None


ib, secs = pe_info(data)

# RTTI / mangled SetDDS
for nd in [
    b"NvCplSetDDSState",
    b"SetDDSState",
    b"NVCPLAPI_SETTINGS_DDS",
    b"NvCplApiSetSetting",
    b"Not switching mux",
    b"Not found mux object",
]:
    pos = 0
    while True:
        i = data.find(nd, pos)
        if i < 0:
            break
        print(f"{nd!r} @{i} rva={off2rva(secs,i)}")
        pos = i + 1
        if pos - i > 10:
            break

# Find imports of NvCplApiSetSetting / GetInfo from nvcpl
# Parse import table
e = struct.unpack_from("<I", data, 0x3C)[0]
magic = struct.unpack_from("<H", data, e + 24)[0]
if magic == 0x20B:
    imp_rva = struct.unpack_from("<I", data, e + 24 + 120)[0]
else:
    imp_rva = struct.unpack_from("<I", data, e + 24 + 104)[0]
print("import rva", hex(imp_rva))

imp_off = rva2off(secs, imp_rva)
interesting = []
off = imp_off
while True:
    ilt, ts, fwd, name_rva, iat = struct.unpack_from("<IIIII", data, off)
    if ilt == 0 and name_rva == 0:
        break
    name_off = rva2off(secs, name_rva)
    dll = data[name_off : data.find(b"\0", name_off)].decode()
    # walk ILT
    thunk_rva = ilt or iat
    t = rva2off(secs, thunk_rva)
    idx = 0
    while True:
        th = struct.unpack_from("<Q", data, t + idx * 8)[0]
        if th == 0:
            break
        if th & (1 << 63) == 0:
            no = rva2off(secs, th & 0x7FFFFFFF)
            if no is not None:
                hint = struct.unpack_from("<H", data, no)[0]
                fn = data[no + 2 : data.find(b"\0", no + 2)].decode("ascii", "replace")
                if any(
                    k in fn
                    for k in (
                        "SetSetting",
                        "GetSetting",
                        "GetInfo",
                        "Mux",
                        "Hybrid",
                        "DDS",
                        "Init",
                        "Execute",
                    )
                ):
                    iat_rva = iat + idx * 8
                    interesting.append((dll, fn, iat_rva))
        idx += 1
        if idx > 500:
            break
    off += 20

print("\nInteresting imports:")
for row in interesting:
    print(" ", row)

# For each IAT entry, find code refs (FF 15 rip-rel or 48 8B 05 / call [rip])
text = next(s for s in secs if s[0].startswith(".text"))
_, tva, tsz, tptr, trsz = text
code = data[tptr : tptr + trsz]


def find_iat_calls(iat_rva, label, limit=15):
    hits = []
    for i in range(len(code) - 6):
        # FF 15 disp32  => call qword ptr [rip+disp]
        if code[i] == 0xFF and code[i + 1] == 0x15:
            disp = struct.unpack_from("<i", code, i + 2)[0]
            target = (tva + i + 6) + disp
            if target == iat_rva:
                hits.append(tva + i)
        # 48 8B 05 disp32 => mov rax,[rip+disp] then call rax later
        if code[i] == 0x48 and code[i + 1] == 0x8B and code[i + 2] == 0x05:
            disp = struct.unpack_from("<i", code, i + 3)[0]
            target = (tva + i + 7) + disp
            if target == iat_rva:
                hits.append(tva + i)
    print(f"\nIAT calls to {label} ({len(hits)}):")
    for h in hits[:limit]:
        off = rva2off(secs, h)
        window = data[off - 40 : off + 24]
        imms = []
        for j in range(len(window) - 5):
            if window[j] in (0xB8, 0xB9, 0xBA, 0xBB):
                imms.append(struct.unpack_from("<I", window, j + 1)[0])
            if window[j] == 0x41 and window[j + 1] in (0xB8, 0xB9, 0xBA, 0xBB):
                imms.append(struct.unpack_from("<I", window, j + 2)[0])
            if window[j] == 0xC7 and window[j + 1] == 0x45:
                imms.append(struct.unpack_from("<I", window, j + 3)[0])
        print(f"  @{h:x} imms={[hex(x) for x in sorted(set(imms)) if x < 0x100000]}")


for dll, fn, iat_rva in interesting:
    find_iat_calls(iat_rva, fn)

# --- nvxdapix SetDisplayMux ---
print("\n=== nvxdapix SetDisplayMux ===")
nx = pathlib.Path(r"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdapix.dll")
nd = nx.read_bytes()
nib, nsecs = pe_info(nd)
# wide string
ws = "NvAPI_DISP_SetDisplayMux".encode("utf-16le")
pos = nd.find(ws)
print("wstring @", pos, "rva", off2rva(nsecs, pos) if pos >= 0 else None)
if pos >= 0:
    rva = off2rva(nsecs, pos)
    abs_addr = nib + rva
    # find rip-lea to this string
    text = next(s for s in nsecs if s[0].startswith(".text"))
    _, tva, tsz, tptr, trsz = text
    code = nd[tptr : tptr + trsz]
    for i in range(len(code) - 7):
        if code[i] in (0x48, 0x4C) and code[i + 1] == 0x8D and (code[i + 2] & 0xC7) == 0x05:
            disp = struct.unpack_from("<i", code, i + 3)[0]
            target = (tva + i + 7) + disp
            if target == rva:
                print(f"xref @{tva+i:x}")
                # dump 200 bytes before for mov ecx/edx with QI id (often 0xXXXXXXXX)
                off = rva2off(nsecs, tva + i)
                win = nd[off - 160 : off + 32]
                for j in range(len(win) - 5):
                    if win[j] in (0xB8, 0xB9, 0xBA, 0xBB):
                        v = struct.unpack_from("<I", win, j + 1)[0]
                        if v > 0x10000:
                            print(f"  imm32 0x{v:08X}")
                    if win[j] == 0x41 and win[j + 1] in (0xB8, 0xB9, 0xBA, 0xBB):
                        v = struct.unpack_from("<I", win, j + 2)[0]
                        if v > 0x10000:
                            print(f"  rimm32 0x{v:08X}")
