import frida, time, subprocess, winreg, pathlib

pid = 15512
outdir = pathlib.Path(r"e:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds\_ioctl_caps")
outdir.mkdir(exist_ok=True)
device = frida.get_local_device()
session = device.attach(pid)
js = open(
    r"e:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds\frida_capture_ioctl_dds.js",
    encoding="utf-8",
).read()
script = session.create_script(js)
n = [0]


def on_msg(msg, data):
    if msg["type"] == "send":
        p = msg["payload"]
        if isinstance(p, dict) and p.get("t") == "buf" and data:
            n[0] += 1
            path = outdir / ("ioctl_%x_%d_%d.bin" % (p["code"], p["len"], n[0]))
            path.write_bytes(bytes(data))
            b = bytes(data)
            print("SAVED", path, "len", len(b))
            print("  head", b[:64].hex())
            print("  dgpu pattern @", b.find(bytes.fromhex("0100000002000000")))
            print("  igpu pattern @", b.find(bytes.fromhex("0100000001000000")))
            print("  auto pattern @", b.find(bytes.fromhex("0101000001000000")))
        else:
            print(p)
    else:
        print(msg)


script.on("message", on_msg)
script.load()
time.sleep(0.5)


def ace():
    k = winreg.OpenKey(
        winreg.HKEY_LOCAL_MACHINE,
        r"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE",
    )
    return {
        x: winreg.QueryValueEx(k, x)[0]
        for x in ("InternalMuxState", "InternalMuxIsAutomaticMode", "ACESwitchedI2D")
    }


print("ACE", ace())
for mode in ("dgpu", "igpu"):
    print("====", mode)
    subprocess.run(
        [
            r"e:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds\inject_native_dds.exe",
            mode,
        ]
    )
    time.sleep(2)
    print("ACE", ace())
session.detach()
print("files", sorted(outdir.glob("*.bin")))
