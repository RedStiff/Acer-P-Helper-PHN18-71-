import frida
import subprocess
import sys
import time
import pathlib

DIR = pathlib.Path(r"E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds")
JS = (DIR / "frida_capture_statedata.js").read_text(encoding="utf-8")
OUT = DIR / "_statedata_cap.txt"


def main():
    lines = []

    def on_msg(msg, data):
        if msg["type"] == "send":
            p = msg.get("payload") or {}
            m = p.get("m", str(p))
            print(m)
            lines.append(m)
        else:
            print("MSG", msg)
            lines.append(str(msg))

    # Ensure helper host is up via inject (starts hidden App if needed)
    print("=== starting inject ping ===")
    subprocess.run([str(DIR / "inject_native_dds.exe"), "ping"], cwd=str(DIR), timeout=60)
    time.sleep(2)

    # Find NVIDIA App
    device = frida.get_local_device()
    procs = [p for p in device.enumerate_processes() if "NVIDIA App" in p.name]
    print("procs", [(p.pid, p.name) for p in procs])
    if not procs:
        print("NO APP")
        return 1
    # Prefer process that has nvcpl - try all
    session = None
    script = None
    for p in procs:
        try:
            session = device.attach(p.pid)
            script = session.create_script(JS)
            script.on("message", on_msg)
            script.load()
            print("attached", p.pid)
            break
        except Exception as e:
            print("attach fail", p.pid, e)
            session = None

    if not session:
        return 1

    time.sleep(1)
    print("=== trigger dgpu ===")
    r = subprocess.run([str(DIR / "inject_native_dds.exe"), "dgpu"], cwd=str(DIR), timeout=60, capture_output=True, text=True)
    print("inject out:", r.stdout, r.stderr)
    time.sleep(3)

    print("=== trigger auto ===")
    r = subprocess.run([str(DIR / "inject_native_dds.exe"), "auto"], cwd=str(DIR), timeout=60, capture_output=True, text=True)
    print("inject out:", r.stdout, r.stderr)
    time.sleep(3)

    OUT.write_text("\n".join(lines), encoding="utf-8")
    print("wrote", OUT)
    try:
        session.detach()
    except Exception:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
