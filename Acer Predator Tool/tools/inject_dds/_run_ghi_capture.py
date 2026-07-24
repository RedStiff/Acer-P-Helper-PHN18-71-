import frida, time, pathlib, subprocess, re

DIR = pathlib.Path(r"E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds")
APP = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe"
HELPER = DIR / "inject_native_dds.exe"
JS = (DIR / "frida_ghi_capture.js").read_text(encoding="utf-8")
OUT = DIR / "_ghi_capture.txt"
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

# wait for bat + some GHI
deadline = time.time() + 45
while time.time() < deadline:
    if any("bat ready" in x for x in lines):
        time.sleep(8)
        break
    time.sleep(0.3)

if HELPER.exists():
    print("trigger dgpu", flush=True)
    try:
        subprocess.run([str(HELPER), "dgpu"], cwd=str(DIR), timeout=50)
    except Exception as e:
        print("helper", e, flush=True)

time.sleep(2)
OUT.write_text("\n".join(lines), encoding="utf-8")
ghi = [x for x in lines if x.startswith("==== GHI") or x.startswith("WRAP4") or "sid=0x7d" in x]
print("--- summary ---", flush=True)
print("lines", len(lines), "ghi-ish", len(ghi), flush=True)
for x in ghi[:40]:
    print(x, flush=True)
# extract handles near 0x7d
handles = []
for i, x in enumerate(lines):
    if "sid=0x7d" in x:
        m = re.search(r"h=\{([0-9A-F-]+)\}", x)
        if m:
            handles.append(m.group(1))
print("DDS_HANDLES", list(dict.fromkeys(handles)), flush=True)
try:
    session.detach()
except Exception:
    pass
subprocess.run(["taskkill", "/F", "/IM", "NVIDIA App.exe"], capture_output=True)
