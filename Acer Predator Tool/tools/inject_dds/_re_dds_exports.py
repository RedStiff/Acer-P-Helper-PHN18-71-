"""Export/string scan around DDS mux path in nvcpl / nvxdapix / plugin."""
from __future__ import annotations

import struct
from pathlib import Path

ROOTS = [
    Path(r"C:\Program Files\NVIDIA Corporation\NVIDIA App"),
    Path(r"C:\Program Files\NVIDIA Corporation"),
]


def find_file(name: str) -> Path | None:
    for root in ROOTS:
        if not root.exists():
            continue
        for p in root.rglob(name):
            return p
    return None


def pe_sections(data: bytes):
    e = struct.unpack_from("<I", data, 0x3C)[0]
    magic = struct.unpack_from("<H", data, e + 24)[0]
    nsec = struct.unpack_from("<H", data, e + 6)[0]
    opt = struct.unpack_from("<H", data, e + 20)[0]
    image_base = (
        struct.unpack_from("<Q", data, e + 24 + 24)[0]
        if magic == 0x20B
        else struct.unpack_from("<I", data, e + 24 + 28)[0]
    )
    secs = []
    for i in range(nsec):
        o = e + 24 + opt + i * 40
        name = data[o : o + 8].split(b"\0")[0].decode("ascii", "ignore")
        vsz, va, rsz, ptr = struct.unpack_from("<IIII", data, o + 8)
        secs.append((name, va, max(vsz, rsz), ptr, rsz))
    return e, magic, image_base, secs


def rva2off(secs, rva):
    for name, va, sz, ptr, rsz in secs:
        if va <= rva < va + sz:
            return ptr + (rva - va)
    return None


def list_exports(data: bytes):
    e, magic, ib, secs = pe_sections(data)
    exp_rva = struct.unpack_from("<I", data, e + 24 + (112 if magic == 0x20B else 96))[0]
    off = rva2off(secs, exp_rva)
    if off is None:
        return []
    (
        _c,
        _t,
        _maj,
        _min,
        _name,
        _base,
        nfunc,
        nnames,
        addr_funcs,
        addr_names,
        addr_ords,
    ) = struct.unpack_from("<IIHHIIIIIII", data, off)
    out = []
    for i in range(nnames):
        nr = struct.unpack_from("<I", data, rva2off(secs, addr_names) + i * 4)[0]
        no = rva2off(secs, nr)
        name = data[no : data.find(b"\0", no)].decode("ascii", "replace")
        ord_i = struct.unpack_from("<H", data, rva2off(secs, addr_ords) + i * 2)[0]
        fr = struct.unpack_from("<I", data, rva2off(secs, addr_funcs) + ord_i * 4)[0]
        out.append((name, fr))
    return out


def dump_strings(path: Path, needles: list[bytes]):
    data = path.read_bytes()
    print(f"\n=== {path} size={len(data)} ===")
    for s in needles:
        pos = 0
        n = 0
        while n < 5:
            i = data.find(s, pos)
            if i < 0:
                break
            print(f"  {s!r} @{i}")
            pos = i + 1
            n += 1


def main():
    nvcpl = find_file("nvcpl.dll")
    plugin = find_file("NvCplDisplayPlugin.dll")
    nvxd = find_file("nvxdapix.dll")
    nvxdcore = find_file("NvXDCore.dll")
    print("nvcpl", nvcpl)
    print("plugin", plugin)
    print("nvxdapix", nvxd)
    print("NvXDCore", nvxdcore)

    needles = [
        b"NVCPLAPI_SETTINGS_DDS_INTERNAL_MUX_STATE",
        b"NVCPLAPI_SETTINGS_DDS",
        b"NvAPI_DISP_SetDisplayMux",
        b"_tagNVCPLAPI_DDSState",
        b"Not switching mux",
        b"NvCplApiSetSetting",
        b"SetDDSState",
    ]
    for p in (nvcpl, plugin, nvxd, nvxdcore):
        if p and p.exists():
            dump_strings(p, needles)

    if nvcpl and nvcpl.exists():
        ex = list_exports(nvcpl.read_bytes())
        keys = ("Setting", "Mux", "DDS", "Hybrid", "DisplayMode", "Execute", "Manage", "Init", "Uxd", "UXD")
        filt = sorted(n for n, _ in ex if any(k in n for k in keys))
        print(f"\nnvcpl filtered exports ({len(filt)}):")
        for n in filt:
            print(" ", n)

    if nvxd and nvxd.exists():
        data = nvxd.read_bytes()
        e, magic, ib, secs = pe_sections(data)
        ws = "NvAPI_DISP_SetDisplayMux".encode("utf-16le")
        pos = data.find(ws)
        print("\nnvxdapix wide SetDisplayMux @", pos)
        if pos >= 0:
            rva = None
            for name, va, sz, ptr, rsz in secs:
                if ptr <= pos < ptr + rsz:
                    rva = va + (pos - ptr)
                    break
            print(" rva", hex(rva) if rva is not None else None, "va", hex(ib + rva) if rva is not None else None)
            text = next(s for s in secs if s[0].startswith(".text"))
            _, tva, tsz, tptr, trsz = text
            code = data[tptr : tptr + trsz]
            for i in range(len(code) - 7):
                if code[i] in (0x48, 0x4C) and code[i + 1] == 0x8D and (code[i + 2] & 0xC7) == 0x05:
                    disp = struct.unpack_from("<i", code, i + 3)[0]
                    target = (tva + i + 7) + disp
                    if target == rva:
                        print(f" xref @{tva + i:x}")
                        off = tptr + i
                        win = data[off - 180 : off + 40]
                        for j in range(len(win) - 5):
                            if win[j] in (0xB8, 0xB9, 0xBA, 0xBB):
                                v = struct.unpack_from("<I", win, j + 1)[0]
                                if v > 0x10000:
                                    print(f"  imm32 0x{v:08X}")
                            if win[j] == 0x41 and win[j + 1] in (0xB8, 0xB9, 0xBA, 0xBB):
                                v = struct.unpack_from("<I", win, j + 2)[0]
                                if v > 0x10000:
                                    print(f"  rimm32 0x{v:08X}")

    # plugin: GetProcAddress("NvCplApiSetSetting") pattern
    if plugin and plugin.exists():
        data = plugin.read_bytes()
        for s in (b"NvCplApiSetSetting", b"NvCplApiGetSetting", b"nvcpl.dll", b"NVCPLAPI_SETTINGS_DDS_INTERNAL_MUX_STATE"):
            print("plugin has", s, data.find(s) >= 0)


if __name__ == "__main__":
    main()
