import frida, time, pathlib, subprocess, re

DIR = pathlib.Path(r"E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds")
APP = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe"
HELPER = DIR / "inject_native_dds.exe"
JS = (DIR / "frida_handle_capture.js").read_text(encoding="utf-8")
OUT = DIR / "_handle_capture.txt"
lines = []
ready = {"ok": False}
handles = []

def on_msg(msg, data):
    if msg["type"] == "send":
        m = msg["payload"].get("m", str(msg["payload"]))
        print(m, flush=True)
        lines.append(m)
        if m == "READY":
            ready["ok"] = True
        for h in re.findall(r"\{[0-9A-F-]{36}\}", m):
            if h not in ("{00000000-0000-0000-0000-000000000000}",):
                handles.append(h)
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

deadline = time.time() + 35
while time.time() < deadline:
    if any("nvcpl ready" in x for x in lines) and any("bat hooks ready" in x for x in lines):
        time.sleep(5)
        break
    time.sleep(0.3)

if HELPER.exists():
    print("trigger igpu", flush=True)
    try:
        subprocess.run([str(HELPER), "igpu"], cwd=str(DIR), timeout=50)
    except Exception as e:
        print("helper", e, flush=True)

time.sleep(2)
OUT.write_text("\n".join(lines), encoding="utf-8")
uniq = []
for h in handles:
    if h not in uniq:
        uniq.append(h)
print("HANDLES", uniq, flush=True)
(DIR / "_last_handles.txt").write_text("\n".join(uniq), encoding="utf-8")
try:
    session.detach()
except Exception:
    pass
subprocess.run(["taskkill", "/F", "/IM", "NVIDIA App.exe"], capture_output=True)
