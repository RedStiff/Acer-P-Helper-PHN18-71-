"""Capture full SetSetting + Execute args (incl. a4/a5) during live CDP SetDDS."""
from __future__ import annotations

import json
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

import frida

PORT = 9333
HERE = Path(__file__).resolve().parent
HIT = HERE / "_hit_setdds.py"
LOG = Path.home() / "AppData" / "Local" / "Temp" / "dds_set_exec_capture.jsonl"

JS = r"""
'use strict';
function dump(p, n) {
  try { return hexdump(p, { length: n, header: false, ansi: false }); }
  catch (e) { return String(e); }
}
function hook(name) {
  const m = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
  const addr = m.findExportByName(name);
  send({t:'hook', name:name, addr: addr.toString()});
  Interceptor.attach(addr, {
    onEnter(args) {
      const rec = { t:'enter', name:name, args:[] };
      for (let i = 0; i < 6; i++) {
        const a = args[i];
        const item = { i:i, p:a.toString(), i32:a.toInt32() };
        try {
          if (!a.isNull() && a.compare(ptr(0x10000)) > 0)
            item.dump = dump(a, i === 1 ? 64 : 32);
        } catch (_) {}
        rec.args.push(item);
      }
      // stack spill for 5th/6th on x64
      try {
        const sp = this.context.rsp;
        rec.stack5 = dump(sp.add(0x28), 32);
      } catch (_) {}
      send(rec);
      this._name = name;
    },
    onLeave(retval) {
      send({t:'leave', name:this._name, ret: retval.toInt32()});
    }
  });
}
hook('NvCplApiSetSetting');
hook('NvCplApiExecute');
hook('NvCplApiGetSetting');
send({t:'ready'});
"""


def cdp_up():
    try:
        urllib.request.urlopen(f"http://127.0.0.1:{PORT}/json", timeout=2)
        return True
    except Exception:
        return False


def find_pid():
    device = frida.get_local_device()
    for p in device.enumerate_processes():
        if "nvidia app" not in p.name.lower():
            continue
        session = device.attach(p.pid)
        box: dict = {}

        def on_msg(msg, _data):
            if msg["type"] == "send":
                box["mods"] = msg["payload"].get("mods", [])

        script = session.create_script(
            "send({mods: Process.enumerateModules().map(m => m.name)});"
        )
        script.on("message", on_msg)
        script.load()
        time.sleep(0.2)
        mods = [m.lower() for m in box.get("mods", [])]
        session.detach()
        if "nvcpl.dll" in mods:
            return p.pid
    raise SystemExit("no pid")


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "igpu"
    if not cdp_up():
        raise SystemExit("CDP down")
    LOG.write_text("", encoding="utf-8")
    pid = find_pid()
    print("attach", pid)
    records = []

    def on_msg(msg, data):
        if msg["type"] == "send":
            payload = msg["payload"]
            records.append(payload)
            with LOG.open("a", encoding="utf-8") as f:
                f.write(json.dumps(payload) + "\n")
            if payload.get("t") in ("hook", "ready", "leave") or payload.get("name") in (
                "NvCplApiSetSetting",
                "NvCplApiExecute",
            ):
                print("FRIDA", json.dumps(payload, ensure_ascii=False)[:600])

    session = frida.attach(pid)
    script = session.create_script(JS)
    script.on("message", on_msg)
    script.load()
    time.sleep(0.3)
    print("trigger", mode)
    subprocess.run([sys.executable, "-u", str(HIT), mode], cwd=str(HERE))
    time.sleep(1)
    session.detach()
    sets = [r for r in records if r.get("t") == "enter" and r.get("name") == "NvCplApiSetSetting"]
    execs = [r for r in records if r.get("t") == "enter" and r.get("name") == "NvCplApiExecute"]
    print(f"SetSetting={len(sets)} Execute={len(execs)} log={LOG}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
