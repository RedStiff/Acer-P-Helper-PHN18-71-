import frida, time, pathlib, subprocess

DIR = pathlib.Path(r"E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds")
APP = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe"
JS = (DIR / "frida_spawn_create_hook.js").read_text(encoding="utf-8")
OUT = DIR / "_spawn_create_hook.txt"
lines = []

def on_msg(msg, data):
    if msg["type"] == "send":
        m = msg["payload"].get("m", str(msg["payload"]))
        print(m, flush=True)
        lines.append(m)
    else:
        print(msg, flush=True)
        lines.append(str(msg))

# kill hold factory + app
subprocess.run(["taskkill", "/F", "/IM", "_hold_factory.exe"], capture_output=True)
subprocess.run(["taskkill", "/F", "/IM", "NVIDIA App.exe"], capture_output=True)
time.sleep(1)

dev = frida.get_local_device()
pid = dev.spawn([APP])
print("SPAWNED", pid, flush=True)
session = dev.attach(pid)
script = session.create_script(JS)
script.on("message", on_msg)
script.load()
dev.resume(pid)

deadline = time.time() + 60
saw = False
while time.time() < deadline:
    if any("WRAP leave" in x or "CCI FILTER" in x for x in lines):
        saw = True
        time.sleep(5)
        break
    time.sleep(0.5)

if not saw:
    time.sleep(15)

OUT.write_text("\n".join(lines), encoding="utf-8")
print("saved", OUT, "lines", len(lines), flush=True)
try:
    session.detach()
except Exception:
    pass
subprocess.run(["taskkill", "/F", "/IM", "NVIDIA App.exe"], capture_output=True)
