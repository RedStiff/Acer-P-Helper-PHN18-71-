import frida, time, pathlib, subprocess, sys

DIR = pathlib.Path(r"E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds")
APP = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe"
HELPER = DIR / "inject_native_dds.exe"
JS = (DIR / "frida_filter_cache.js").read_text(encoding="utf-8")
OUT = DIR / "_filter_cache.txt"
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
print("SPAWNED", pid, flush=True)
session = dev.attach(pid)
script = session.create_script(JS)
script.on("message", on_msg)
script.load()
t0 = time.time()
while not ready["ok"] and time.time() - t0 < 10:
    time.sleep(0.05)
dev.resume(pid)
print("resumed", flush=True)

# wait for filter + some cache activity
deadline = time.time() + 35
while time.time() < deadline:
    if any("WATCH filter=" in x for x in lines) and any("nvcpl ready" in x for x in lines):
        time.sleep(4)
        break
    time.sleep(0.3)

mode = sys.argv[1] if len(sys.argv) > 1 else "optimus"
if HELPER.exists():
    print("trigger", mode, flush=True)
    try:
        subprocess.run([str(HELPER), mode], cwd=str(DIR), timeout=50)
    except Exception as e:
        print("helper", e, flush=True)
else:
    time.sleep(15)

time.sleep(2)
OUT.write_text("\n".join(lines), encoding="utf-8")
print("saved", len(lines), flush=True)
for key in ("OUTER", "CACHE WRITE", "CACHE POLL", "ARM_", "CREATE", "CCI FILTER", "RESOLVE"):
    hits = [x for x in lines if key in x]
    print(f"--- {key} ({len(hits)}) ---", flush=True)
    for x in hits[:25]:
        print(x, flush=True)

try:
    session.detach()
except Exception:
    pass
subprocess.run(["taskkill", "/F", "/IM", "NVIDIA App.exe"], capture_output=True)
