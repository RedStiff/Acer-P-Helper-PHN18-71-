import frida, time, pathlib, subprocess

DIR = pathlib.Path(r"E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds")
APP = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe"
JS = (DIR / "frida_outer_id.js").read_text(encoding="utf-8")
OUT = DIR / "_outer_id.txt"
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
time.sleep(1)
dev = frida.get_local_device()
pid = dev.spawn([APP])
session = dev.attach(pid)
script = session.create_script(JS)
script.on("message", on_msg)
script.load()
t0 = time.time()
while not ready["ok"] and time.time() - t0 < 10:
    time.sleep(0.05)
dev.resume(pid)

deadline = time.time() + 40
while time.time() < deadline:
    if any("=== FILTER CREATE ===" in x for x in lines) and any("CACHE " in x for x in lines):
        time.sleep(2)
        break
    time.sleep(0.2)

OUT.write_text("\n".join(lines), encoding="utf-8")
print("saved", OUT, "lines", len(lines), flush=True)
try:
    session.detach()
except Exception:
    pass
subprocess.run(["taskkill", "/F", "/IM", "NVIDIA App.exe"], capture_output=True)
