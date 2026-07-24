"""Find immediates near NVCPLAPI_SETTINGS_DDS_INTERNAL_MUX_STATE xrefs in plugin."""
import struct
import pathlib
import re

plugin = pathlib.Path(
    r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\plugins\Base\NvCplDisplayPlugin.dll"
)
data = plugin.read_bytes()


def pe_sections(blob: bytes):
    e_lfanew = struct.unpack_from("<I", blob, 0x3C)[0]
    nsec = struct.unpack_from("<H", blob, e_lfanew + 6)[0]
    opt = struct.unpack_from("<H", blob, e_lfanew + 20)[0]
    magic = struct.unpack_from("<H", blob, e_lfanew + 24)[0]
    image_base = (
        struct.unpack_from("<Q", blob, e_lfanew + 24 + 24)[0]
        if magic == 0x20B
        else struct.unpack_from("<I", blob, e_lfanew + 24 + 28)[0]
    )
    sec_off = e_lfanew + 24 + opt
    secs = []
    for i in range(nsec):
        o = sec_off + i * 40
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


image_base, secs = pe_sections(data)
print("image_base", hex(image_base))

needle = b"NVCPLAPI_SETTINGS_DDS_INTERNAL_MUX_STATE"
# may be absent in plugin; try shorter
needles = [
    b"NVCPLAPI_SETTINGS_DDS_INTERNAL_MUX_STATE",
    b"NvCplApiGetInfo NVFLAGS_DDS_STATUS",
    b"ExecuteAsync NvCplSetDDSState",
    b"Finish NvCplSetDDSState",
    b"Invalid MuxState value passed",
]
for nd in needles:
    pos = data.find(nd)
    print(f"\nstring {nd!r} @ {pos}")
    if pos < 0:
        continue
    rva = off2rva(secs, pos)
    if rva is None:
        print("  no rva")
        continue
    abs_addr = image_base + rva
    print(f"  rva={rva:x} abs={abs_addr:x}")

    # Find LEA/MOV rip-relative or absolute pointers to this VA in .text
    text = next(s for s in secs if s[0].startswith(".text"))
    tname, tva, tsz, tptr, trsz = text
    code = data[tptr : tptr + trsz]
    hits = []
    # RIP-relative: 48 8D xx disp32  or 48 8B xx disp32 or 4C ... pointing to abs
    for i in range(len(code) - 7):
        # lea/mov r64, [rip+disp32] : rex 8D/8B modrm(rm=5)
        b0, b1, b2 = code[i], code[i + 1], code[i + 2]
        if b0 in (0x48, 0x4C, 0x49) and b1 in (0x8D, 0x8B) and (b2 & 0xC7) == 0x05:
            disp = struct.unpack_from("<i", code, i + 3)[0]
            instr_rva = tva + i
            next_rva = instr_rva + 7
            target = next_rva + disp
            if target == rva or abs(target - rva) < 4:
                hits.append((instr_rva, "lea/mov rip", target))
        # absolute 64-bit address embedded
        if i + 8 <= len(code):
            q = struct.unpack_from("<Q", code, i)[0]
            if q == abs_addr:
                hits.append((tva + i, "abs64", rva))
    print(f"  code xrefs: {len(hits)}")
    for h in hits[:20]:
        print(f"    @{h[0]:x} {h[1]} -> {h[2]:x}")
        # dump 48 bytes before xref for immediates
        off = rva2off(secs, h[0])
        if off is None:
            continue
        window = data[max(0, off - 64) : off + 32]
        imms = re.findall(rb"(?:(?:\xB8|\xB9|\xBA|\xBB|\x41\xB8|\x41\xB9|\x41\xBA|\x41\xBB))(....)", window)
        # also C7 45/44 xx imm32
        vals = []
        for j in range(len(window) - 5):
            if window[j] in (0xB8, 0xB9, 0xBA, 0xBB):
                vals.append(struct.unpack_from("<I", window, j + 1)[0])
            if window[j] == 0x41 and window[j + 1] in (0xB8, 0xB9, 0xBA, 0xBB):
                vals.append(struct.unpack_from("<I", window, j + 2)[0])
            if window[j] == 0xC7 and window[j + 1] in (0x45, 0x44, 0x40, 0x41, 0x42, 0x43):
                # C7 45 xx imm32  or C7 44 24 xx imm32
                if window[j + 1] == 0x45:
                    vals.append(struct.unpack_from("<I", window, j + 3)[0])
                elif window[j + 1] == 0x44:
                    vals.append(struct.unpack_from("<I", window, j + 4)[0])
        uniq = sorted(set(vals))
        print("      immediates:", " ".join(f"0x{v:X}" for v in uniq if v < 0x100000))

# Also search nvcpl for setting id tables: pairs of (id, string rva)
nvcpl = pathlib.Path(r"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvcpl.dll")
nd = nvcpl.read_bytes()
ib, nsecs = pe_sections(nd)
pos = nd.find(b"NVCPLAPI_SETTINGS_DDS_INTERNAL_MUX_STATE")
print("\nnvcpl string @", pos)
rva = off2rva(nsecs, pos)
print("rva", hex(rva) if rva else None, "abs", hex(ib + rva) if rva else None)
if rva:
    abs_addr = ib + rva
    # scan for abs address pointers (qword) in data sections
    for name, va, sz, ptr, rsz in nsecs:
        chunk = nd[ptr : ptr + rsz]
        for i in range(0, len(chunk) - 8, 4):
            q = struct.unpack_from("<Q", chunk, i)[0]
            if q == abs_addr:
                # dump surrounding as possible {id, ptr} or {ptr, id}
                base = max(0, i - 32)
                print(f"ptr in {name} off+{i} rva={va+i:x}")
                for k in range(base, min(len(chunk), i + 40), 4):
                    v = struct.unpack_from("<I", chunk, k)[0]
                    mark = " <<" if k == i else ""
                    print(f"  +{k-i:+4d}: 0x{v:08X}{mark}")
