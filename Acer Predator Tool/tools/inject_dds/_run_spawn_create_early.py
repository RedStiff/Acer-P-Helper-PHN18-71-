import frida, time, pathlib, subprocess

DIR = pathlib.Path(r"E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds")
APP = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe"
JS = (DIR / "frida_spawn_create_early.js").read_text(encoding="utf-8")
OUT = DIR / "_spawn_create_early.txt"
lines = []
ready = {"ok": False}

def on_msg(msg, data):
    if msg["type"] == "send":
        m = msg["payload"].get("m", str(msg["payload"]))
        print(m, flush=True)
        lines.append(m)
        if m == "READY":
            ready["ok"] = True
    else:
        print(msg, flush=True)
        lines.append(str(msg))

subprocess.run(["taskkill", "/F", "/IM", "NVIDIA App.exe"], capture_output=True)
subprocess.run(["taskkill", "/F", "/IM", "_hold_factory.exe"], capture_output=True)
time.sleep(1)

dev = frida.get_local_device()
pid = dev.spawn([APP])
print("SPAWNED", pid, flush=True)
session = dev.attach(pid)
script = session.create_script(JS)
script.on("message", on_msg)
script.load()

# Wait until hooks ready BEFORE resume
t0 = time.time()
while not ready["ok"] and time.time() - t0 < 10:
    time.sleep(0.05)
print("resume hooks_ready=", ready["ok"], flush=True)
dev.resume(pid)

deadline = time.time() + 50
while time.time() < deadline:
    if any(("CCI {3F6374C" in x) or ("WRAP leave" in x and "hr=0x0" in x) for x in lines):
        time.sleep(6)
        break
    time.sleep(0.3)

OUT.write_text("\n".join(lines), encoding="utf-8")
print("saved", len(lines), flush=True)
try:
    session.detach()
except Exception:
    pass
subprocess.run(["taskkill", "/F", "/IM", "NVIDIA App.exe"], capture_output=True)
