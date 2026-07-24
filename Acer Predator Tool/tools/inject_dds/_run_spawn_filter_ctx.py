import frida, time, pathlib, subprocess, sys, os

DIR = pathlib.Path(r"E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds")
APP = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe"
JS = (DIR / "frida_spawn_filter_ctx.js").read_text(encoding="utf-8")
OUT = DIR / "_spawn_filter_ctx.txt"
lines = []

def on_msg(msg, data):
    if msg["type"] == "send":
        m = msg["payload"].get("m", str(msg["payload"]))
        print(m, flush=True)
        lines.append(m)
    else:
        print(msg, flush=True)
        lines.append(str(msg))

# Kill existing
subprocess.run(["taskkill", "/F", "/IM", "NVIDIA App.exe"], capture_output=True)
time.sleep(1)

dev = frida.get_local_device()
pid = dev.spawn([APP], stdio="pipe")
print("SPAWNED", pid, flush=True)
session = dev.attach(pid)
script = session.create_script(JS)
script.on("message", on_msg)
script.load()
dev.resume(pid)

# Wait for UXD / filter create
deadline = time.time() + 45
while time.time() < deadline:
    if any("CCI clsid={3F6374C2" in x and "hr=0x0" in x.replace(" ", "") for x in lines):
        # give a moment for dump
        time.sleep(2)
        break
    if any("CCI clsid={3F6374C2" in x for x in lines) and any("hr=0x0" in x for x in lines if "3F6374C2" in x):
        time.sleep(2)
        break
    time.sleep(0.5)

# Also accept any CCI line for filter
time.sleep(8)

OUT.write_text("\n".join(lines), encoding="utf-8")
print("saved", OUT, "lines", len(lines), flush=True)

try:
    session.detach()
except Exception:
    pass
# leave app running for follow-up; or kill
# subprocess.run(["taskkill", "/F", "/IM", "NVIDIA App.exe"], capture_output=True)
