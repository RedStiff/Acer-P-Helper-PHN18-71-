import struct
from pathlib import Path

data = Path(r"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvcpl.dll").read_bytes()
e = struct.unpack_from("<I", data, 0x3C)[0]
magic = struct.unpack_from("<H", data, e + 24)[0]
nsec = struct.unpack_from("<H", data, e + 6)[0]
opt = struct.unpack_from("<H", data, e + 20)[0]
secs = []
for i in range(nsec):
    o = e + 24 + opt + i * 40
    name = data[o : o + 8].split(b"\0")[0].decode()
    vsz, va, rsz, ptr = struct.unpack_from("<IIII", data, o + 8)
    secs.append((va, max(vsz, rsz), ptr, rsz))


def rva2off(rva):
    for va, sz, ptr, rsz in secs:
        if va <= rva < va + sz:
            return ptr + (rva - va)
    return None


out = Path(__file__).with_name("_re_setting_id_map.txt")
lines = []
code_rva = 0x3B80
off = rva2off(code_rva)
blob = data[off : off + 0x200]
i = 0
while i < len(blob) - 10:
    if blob[i : i + 3] == bytes.fromhex("488d05"):
        disp = struct.unpack_from("<i", blob, i + 3)[0]
        instr_rva = code_rva + i
        target = instr_rva + 7 + disp
        toff = rva2off(target)
        if toff is None:
            s = "?"
        else:
            end = data.find(b"\0", toff)
            s = data[toff:end].decode("ascii", "replace")
        window = blob[max(0, i - 0x20) : i + 0x40]
        ids = []
        for j in range(len(window) - 5):
            if window[j] == 0xB9:
                ids.append(struct.unpack_from("<I", window, j + 1)[0])
        lines.append(f"{instr_rva:#x} -> {s!r} ids={[hex(x) for x in ids]}")
    i += 1

# Also list all NVCPLAPI_SETTINGS_DDS* strings
pos = 0
while True:
    i = data.find(b"NVCPLAPI_SETTINGS_DDS", pos)
    if i < 0:
        break
    end = data.find(b"\0", i)
    lines.append(f"str@{i}: {data[i:end]!r}")
    pos = i + 1

out.write_text("\n".join(lines), encoding="utf-8")
print("wrote", out, "lines", len(lines))
