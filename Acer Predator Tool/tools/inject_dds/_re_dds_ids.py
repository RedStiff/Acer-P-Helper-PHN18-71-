import struct
from pathlib import Path

data = Path(r"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvcpl.dll").read_bytes()
e = struct.unpack_from("<I", data, 0x3C)[0]
nsec = struct.unpack_from("<H", data, e + 6)[0]
opt = struct.unpack_from("<H", data, e + 20)[0]
secs = []
for i in range(nsec):
    o = e + 24 + opt + i * 40
    vsz, va, rsz, ptr = struct.unpack_from("<IIII", data, o + 8)
    secs.append((va, max(vsz, rsz), ptr))


def rva2off(rva):
    for va, sz, ptr in secs:
        if va <= rva < va + sz:
            return ptr + (rva - va)
    return None


# Walk .text for pattern: lea rax,[rip+str] ... mov ecx, imm32 ... call
text = next(s for s in secs)  # wrong
# find .text
e2 = struct.unpack_from("<I", data, 0x3C)[0]
# rebuild with names
secs2 = []
for i in range(nsec):
    o = e + 24 + opt + i * 40
    name = data[o : o + 8].split(b"\0")[0].decode()
    vsz, va, rsz, ptr = struct.unpack_from("<IIII", data, o + 8)
    secs2.append((name, va, max(vsz, rsz), ptr))

text = next(s for s in secs2 if s[0].startswith(".text"))
_, tva, tsz, tptr = text
code = data[tptr : tptr + tsz]
out = []
for i in range(len(code) - 16):
    if code[i : i + 3] != b"\x48\x8D\x05":
        continue
    disp = struct.unpack_from("<i", code, i + 3)[0]
    instr_rva = tva + i
    str_rva = instr_rva + 7 + disp
    soff = rva2off(str_rva)
    if soff is None:
        continue
    s = data[soff : data.find(b"\0", soff)]
    if b"DDS" not in s and b"MUX" not in s:
        continue
    # find mov ecx, imm32 in next 40 bytes
    chunk = code[i : i + 48]
    sid = None
    for j in range(len(chunk) - 5):
        if chunk[j] == 0xB9:
            sid = struct.unpack_from("<I", chunk, j + 1)[0]
            break
    id_s = f"{sid:#x}" if sid is not None else "None"
    out.append(f"{instr_rva:#x} id={id_s} {s.decode('ascii', 'replace')}")

Path(__file__).with_name("_re_dds_ids.txt").write_text("\n".join(out), encoding="utf-8")
print("\n".join(out))
