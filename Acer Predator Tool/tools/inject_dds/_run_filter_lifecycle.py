import frida, time, pathlib, subprocess, sys

DIR = pathlib.Path(r"E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds")
APP = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe"
HELPER = DIR / "inject_native_dds.exe"
JS = (DIR / "frida_filter_lifecycle.js").read_text(encoding="utf-8")
OUT = DIR / "_filter_lifecycle.txt"
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
print("resume ready=", ready["ok"], flush=True)
dev.resume(pid)

# Wait for filter create + nvcpl
deadline = time.time() + 40
while time.time() < deadline:
    if any("HOOK FILTER" in x for x in lines) and any("nvcpl hooks ready" in x for x in lines):
        time.sleep(3)
        break
    time.sleep(0.3)

# Trigger DDS via native helper if present, else just wait for UI
mode = sys.argv[1] if len(sys.argv) > 1 else "dgpu"
if HELPER.exists():
    print("trigger", HELPER, mode, flush=True)
    try:
        subprocess.run([str(HELPER), mode], cwd=str(DIR), timeout=45)
    except Exception as e:
        print("helper", e, flush=True)
else:
    print("no helper — wait for manual / natural DDS", flush=True)
    time.sleep(20)

time.sleep(3)
OUT.write_text("\n".join(lines), encoding="utf-8")
print("saved", OUT, "lines", len(lines), flush=True)

# Summarize
creates = [x for x in lines if "CREATE" in x or "CCI FILTER" in x]
hooks = [x for x in lines if "HOOK FILTER" in x]
dds = [x for x in lines if "[DDS]" in x or "ARM DDS" in x or "Stubless" in x]
print("=== SUMMARY creates", len(creates), "hooks", len(hooks), "dds", len(dds), flush=True)
for x in creates[:10]:
    print(x, flush=True)
for x in dds[:40]:
    print(x, flush=True)

try:
    session.detach()
except Exception:
    pass
subprocess.run(["taskkill", "/F", "/IM", "NVIDIA App.exe"], capture_output=True)
