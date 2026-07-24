"""Call NvCplApiSetSetting(0x330) from inside NVIDIA App via Frida RPC (has UXD context).

Usage:
  python _frida_native_setdds.py dgpu|igpu|auto
Requires CDP/NVIDIA App already running with NvCpl.dll loaded.
"""
from __future__ import annotations

import json
import sys
import time
import winreg

import frida

JS = r"""
'use strict';
let setPtr = null;
function resolve() {
  const names = ['NvCpl.dll', 'nvcpl.dll'];
  let m = null;
  for (const n of names) { m = Process.findModuleByName(n); if (m) break; }
  if (!m) throw new Error('NvCpl.dll missing');
  setPtr = m.findExportByName('NvCplApiSetSetting');
  if (!setPtr) throw new Error('NvCplApiSetSetting missing');
  return { mod: m.name, set: setPtr.toString() };
}
rpc.exports = {
  info: function () { return resolve(); },
  setDds: function (mux, automatic) {
    resolve();
    // Match live capture: SetSetting(1, pHdr, 0x330, pVal)
    // pHdr = { u64:0, u64:0 } then value immediately after OR separate buffers.
    const id = 0x330;
    const hdr = Memory.alloc(0x40);
    hdr.writeByteArray(new Array(0x40).fill(0));
    const val = hdr.add(0x10);
    val.writeU32(1);                 // field0 observed = 1
    val.add(4).writeU32(mux | 0);    // MuxState
    val.add(8).writeU32(automatic ? 1 : 0);
    const set = new NativeFunction(setPtr, 'int', ['int', 'pointer', 'int', 'pointer']);
    const ret = set(1, hdr, id, val);
    return { ret: ret, mux: mux, automatic: !!automatic, val: hexdump(val, {length: 16, header:false, ansi:false}) };
  },
  // Also try a1=NULL / a1=val
  setDdsVariants: function (mux, automatic) {
    resolve();
    const id = 0x330;
    const results = [];
    const set = new NativeFunction(setPtr, 'int', ['int', 'pointer', 'int', 'pointer']);
    function one(label, a0, make) {
      const mem = Memory.alloc(0x40);
      mem.writeByteArray(new Array(0x40).fill(0));
      const { a1, a3 } = make(mem);
      a3.writeU32(1);
      a3.add(4).writeU32(mux | 0);
      a3.add(8).writeU32(automatic ? 1 : 0);
      let ret = -999;
      try { ret = set(a0, a1, id, a3); } catch (e) { ret = -1000; }
      results.push({ label: label, ret: ret });
    }
    one('a0=1 hdr+val', 1, (m) => ({ a1: m, a3: m.add(0x10) }));
    one('a0=0 hdr+val', 0, (m) => ({ a1: m, a3: m.add(0x10) }));
    one('a0=1 a1=null', 1, (m) => ({ a1: ptr(0), a3: m }));
    one('a0=1 a1=val', 1, (m) => ({ a1: m, a3: m }));
    return results;
  }
};
"""


def ace():
    key = winreg.OpenKey(
        winreg.HKEY_LOCAL_MACHINE,
        r"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE",
    )
    return {
        "state": winreg.QueryValueEx(key, "InternalMuxState")[0],
        "auto": winreg.QueryValueEx(key, "InternalMuxIsAutomaticMode")[0],
        "i2d": winreg.QueryValueEx(key, "ACESwitchedI2D")[0],
    }


def find_pid() -> int:
    device = frida.get_local_device()
    js = "send({mods: Process.enumerateModules().map(m => m.name)});"
    best = None
    for p in device.enumerate_processes():
        if "nvidia app" not in p.name.lower():
            continue
        session = device.attach(p.pid)
        box: dict = {}

        def on_msg(msg, _data):
            if msg["type"] == "send":
                box["mods"] = msg["payload"].get("mods", [])

        script = session.create_script(js)
        script.on("message", on_msg)
        script.load()
        time.sleep(0.2)
        mods = [m.lower() for m in box.get("mods", [])]
        session.detach()
        if "nvcpl.dll" in mods:
            return p.pid
    raise SystemExit("No NVIDIA App with NvCpl.dll — start CDP host first")


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "dgpu"
    if mode == "auto":
        mux, automatic = 1, True
    elif mode == "igpu":
        mux, automatic = 1, False
    else:
        mux, automatic = 2, False

    pid = find_pid()
    print("pid", pid, "target", mode, "mux", mux, "auto", automatic)
    before = ace()
    print("ACE before", before)

    session = frida.attach(pid)
    script = session.create_script(JS)
    script.load()
    print("info", script.exports_sync.info())
    print("variants", script.exports_sync.set_dds_variants(mux, automatic))
    time.sleep(1.5)
    mid = ace()
    print("ACE after variants", mid)

    # If no hit yet, try primary shape again
    if mid == before:
        print("primary", script.exports_sync.set_dds(mux, automatic))
        time.sleep(2.0)
    after = ace()
    print("ACE after", after)
    hit = before != after
    print("NATIVE_INPROC_HIT" if hit else "NATIVE_INPROC_NO_HIT")
    session.detach()
    return 0 if hit else 2


if __name__ == "__main__":
    raise SystemExit(main())
