import frida, time, pathlib, subprocess, re

DIR = pathlib.Path(r"E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds")
APP = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe"
HELPER = DIR / "inject_native_dds.exe"
JS = (DIR / "frida_handle_mint.js").read_text(encoding="utf-8")
OUT = DIR / "_handle_mint.txt"
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

# Hard reset UXD session to force fresh handles if possible
subprocess.run(["taskkill", "/F", "/IM", "NVIDIA App.exe"], capture_output=True)
subprocess.run(["powershell", "-NoP", "-C",
                "Restart-Service NVDisplay.ContainerLocalSystem -Force -EA SilentlyContinue; "
                "Restart-Service NvContainerLocalSystem -Force -EA SilentlyContinue"],
               capture_output=True)
time.sleep(12)

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

deadline = time.time() + 50
while time.time() < deadline:
    if any("bat ready" in x for x in lines) and any("plcy ready" in x for x in lines):
        # wait for FIRST7d during init
        t1 = time.time()
        while time.time() - t1 < 20:
            if any(x.startswith("FIRST7d") for x in lines):
                break
            time.sleep(0.3)
        break
    time.sleep(0.3)

if HELPER.exists() and not any(x.startswith("FIRST7d") for x in lines):
    print("trigger dgpu to force DDS path", flush=True)
    try:
        subprocess.run([str(HELPER), "dgpu"], cwd=str(DIR), timeout=50)
    except Exception as e:
        print("helper", e, flush=True)

time.sleep(2)
OUT.write_text("\n".join(lines), encoding="utf-8")
print("=== SUMMARY ===", flush=True)
for key in ("FIRST7d", "UUID ", "CoCreateGuid", "UUID_MATCH", "UUID_NOT_LOCAL",
            "BAT_SetExisting_FN", "PLCY_SetExisting_FN", "PLCY_StoreData_FN",
            "BAT_StoreData_FN", "DOP_WRAP", "leaDone"):
    hits = [x for x in lines if x.startswith(key) or key in x]
    print(f"-- {key} ({len(hits)}) --", flush=True)
    for x in hits[:12]:
        print(x, flush=True)

# extract DDS handle
for x in lines:
    m = re.search(r"FIRST7d \w+ h=(\{[^}]+\})", x)
    if m:
        (DIR / "_minted_dds_handle.txt").write_text(m.group(1), encoding="utf-8")
        print("MINTED", m.group(1), flush=True)
        break

try:
    session.detach()
except Exception:
    pass
subprocess.run(["taskkill", "/F", "/IM", "NVIDIA App.exe"], capture_output=True)
