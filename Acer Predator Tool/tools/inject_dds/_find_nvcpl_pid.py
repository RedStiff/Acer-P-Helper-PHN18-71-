import frida
import sys
import time

JS = """
send({t:'mods', mods: Process.enumerateModules().map(m => m.name)});
"""

d = frida.get_local_device()
for p in d.enumerate_processes():
    if "nvidia app" not in p.name.lower():
        continue
    try:
        s = d.attach(p.pid)
        result = {}

        def on_msg(msg, data):
            if msg["type"] == "send":
                result["mods"] = msg["payload"].get("mods", [])

        scr = s.create_script(JS)
        scr.on("message", on_msg)
        scr.load()
        time.sleep(0.3)
        mods = result.get("mods", [])
        hit = [m for m in mods if "nvcpl" in m.lower() or "nvxd" in m.lower() or "DisplayPlugin" in m]
        print(f"{p.pid}\thit={hit}\ttotal={len(mods)}")
        s.detach()
    except Exception as e:
        print(f"{p.pid}\tFAIL {type(e).__name__}: {e}")
