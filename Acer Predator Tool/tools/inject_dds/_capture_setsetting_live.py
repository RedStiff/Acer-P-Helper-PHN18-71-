"""Launch CDP host (if needed), Frida-hook NvCplApiSetSetting, trigger SetDDS, dump args.

Usage:
  python _capture_setsetting_live.py [dgpu|igpu|auto]
"""
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
LAUNCHER = HERE / "launcher.exe"
HIT = HERE / "_hit_setdds.py"
LOG = Path.home() / "AppData" / "Local" / "Temp" / "dds_setsetting_capture.jsonl"

JS = r"""
'use strict';
function dump(p, n) {
  try { return hexdump(p, { length: n, header: false, ansi: false }); }
  catch (e) { return String(e); }
}
function hookOne(modNames, name) {
  let m = null;
  let used = null;
  for (const mod of modNames) {
    m = Process.findModuleByName(mod);
    if (m) { used = mod; break; }
  }
  if (!m) { send({t:'miss_mod', mods: modNames}); return; }
  const addr = m.findExportByName(name);
  if (!addr) { send({t:'miss_exp', mod: used, name: name}); return; }
  send({t:'hook', mod: used, name: name, addr: addr.toString()});
  Interceptor.attach(addr, {
    onEnter(args) {
      const rec = {
        t: 'enter',
        name: name,
        a0: args[0].toString(),
        a1: args[1].toString(),
        a2: args[2].toString(),
        a3: args[3].toString(),
        a0i: args[0].toInt32(),
        a1i: args[1].toInt32(),
        a2i: args[2].toInt32(),
      };
      try {
        if (!args[0].isNull() && Math.abs(args[0].toInt32()) > 0x10000)
          rec.d0 = dump(args[0], 64);
      } catch (_) {}
      try {
        if (!args[1].isNull())
          rec.d1 = dump(args[1], 128);
      } catch (_) {}
      try {
        if (!args[2].isNull() && Math.abs(args[2].toInt32()) > 0x10000)
          rec.d2 = dump(args[2], 64);
      } catch (_) {}
      send(rec);
      this._name = name;
    },
    onLeave(retval) {
      send({ t: 'leave', name: this._name, ret: retval.toInt32(), retx: retval.toString() });
    }
  });
}
function main() {
  const nvcpl = ['NvCpl.dll', 'nvcpl.dll', 'NVCPL.dll'];
  hookOne(nvcpl, 'NvCplApiSetSetting');
  hookOne(nvcpl, 'NvCplApiGetSetting');
  hookOne(nvcpl, 'NvCplApiInit');
  const nx = Process.findModuleByName('nvxdapix.dll') || Process.findModuleByName('NvXDApiX.dll');
  if (nx) {
    nx.enumerateExports().forEach(e => {
      if (/QueryInterface/i.test(e.name)) send({t:'nx_exp', name: e.name, addr: e.address.toString()});
    });
  }
  send({t:'ready', pid: Process.id});
}
setImmediate(main);
"""


def cdp_up() -> bool:
    try:
        urllib.request.urlopen(f"http://127.0.0.1:{PORT}/json", timeout=2)
        return True
    except Exception:
        return False


def ensure_cdp():
    if cdp_up():
        print("CDP already up")
        return
    print("Launching CDP host via launcher.exe")
    subprocess.run(
        ["powershell", "-NoProfile", "-Command",
         "Get-Process 'NVIDIA App' -EA SilentlyContinue | Stop-Process -Force"],
        check=False,
    )
    time.sleep(0.8)
    r = subprocess.run([str(LAUNCHER)], cwd=str(HERE))
    if r.returncode != 0:
        raise SystemExit(f"launcher failed {r.returncode}")
    for i in range(80):
        if cdp_up():
            print(f"CDP up after {i}")
            time.sleep(3)
            return
        time.sleep(0.35)
    raise SystemExit("CDP did not come up")


def find_nvidia_pid() -> int:
    device = frida.get_local_device()
    procs = [
        p for p in device.enumerate_processes()
        if "nvidia app" in p.name.lower()
    ]
    if not procs:
        raise SystemExit("No NVIDIA App process")

    js = "send({mods: Process.enumerateModules().map(m => m.name)});"
    best = None
    for p in procs:
        try:
            session = device.attach(p.pid)
            box: dict = {}

            def on_msg(msg, _data):
                if msg["type"] == "send":
                    box["mods"] = msg["payload"].get("mods", [])

            script = session.create_script(js)
            script.on("message", on_msg)
            script.load()
            time.sleep(0.25)
            mods = [m.lower() for m in box.get("mods", [])]
            session.detach()
            score = sum(
                1
                for key in ("nvcpl.dll", "nvcpldisplayplugin.dll", "nvxdapix.dll")
                if key in mods
            )
            print(f"  pid={p.pid} score={score}")
            if score > 0 and (best is None or score > best[0]):
                best = (score, p.pid)
        except Exception as ex:
            print(f"  pid={p.pid} fail {ex}")
    if not best:
        raise SystemExit("No NVIDIA App process with NvCpl.dll")
    return best[1]


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "dgpu"
    ensure_cdp()
    pid = find_nvidia_pid()
    print("attach", pid)
    records = []

    def on_msg(msg, data):
        if msg["type"] == "send":
            payload = msg["payload"]
            print("FRIDA", json.dumps(payload, ensure_ascii=False)[:500])
            records.append(payload)
            with LOG.open("a", encoding="utf-8") as f:
                f.write(json.dumps(payload) + "\n")
        elif msg["type"] == "error":
            print("FRIDA ERR", msg)

    session = frida.attach(pid)
    script = session.create_script(JS)
    script.on("message", on_msg)
    script.load()
    time.sleep(0.5)

    print("Trigger SetDDS", mode)
    hit = subprocess.run([sys.executable, "-u", str(HIT), mode], cwd=str(HERE))
    print("hit exit", hit.returncode)
    time.sleep(1.0)

    session.detach()
    sets = [r for r in records if r.get("t") == "enter" and r.get("name") == "NvCplApiSetSetting"]
    print(f"Captured SetSetting enters: {len(sets)}")
    print("LOG", LOG)
    return 0 if sets else 2


if __name__ == "__main__":
    raise SystemExit(main())
