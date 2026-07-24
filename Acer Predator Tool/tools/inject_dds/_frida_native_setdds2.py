"""In-process SetDDS via Frida: SetSetting(0x330) + size=16 + NvCplApiExecute."""
from __future__ import annotations

import sys
import time
import winreg

import frida

JS = r"""
'use strict';
function mod() {
  return Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
}
function exp(name) {
  const m = mod();
  if (!m) throw new Error('NvCpl missing');
  const p = m.findExportByName(name);
  if (!p) throw new Error(name + ' missing');
  return p;
}
rpc.exports = {
  setDds: function (mux, automatic) {
    const setPtr = exp('NvCplApiSetSetting');
    const execPtr = exp('NvCplApiExecute');
    const id = 0x330;
    const size = 16;

    const val = Memory.alloc(32);
    val.writeByteArray(new Array(32).fill(0));
    val.writeU32(1);
    val.add(4).writeU32(mux | 0);
    val.add(8).writeU32(automatic ? 1 : 0);
    val.add(12).writeU32(0);

    const hdr = Memory.alloc(0x20);
    hdr.writeByteArray(new Array(0x20).fill(0));

    const results = [];

    // Variant A: 5-arg (a0, a1, id, val, size) cdecl
    try {
      const set5 = new NativeFunction(setPtr, 'int', ['int', 'pointer', 'int', 'pointer', 'int']);
      const r = set5(1, hdr, id, val, size);
      results.push({v:'set5(1,hdr,id,val,16)', ret:r});
    } catch (e) { results.push({v:'set5', err:String(e)}); }

    // Variant B: 4-arg (a0, id, val, size)
    try {
      const set4 = new NativeFunction(setPtr, 'int', ['int', 'int', 'pointer', 'int']);
      const r = set4(1, id, val, size);
      results.push({v:'set4(1,id,val,16)', ret:r});
    } catch (e) { results.push({v:'set4', err:String(e)}); }

    // Variant C: 4-arg live-like (1, hdr, id, val) but val is 16 bytes
    try {
      const set4b = new NativeFunction(setPtr, 'int', ['int', 'pointer', 'int', 'pointer']);
      const r = set4b(1, hdr, id, val);
      results.push({v:'set4b(1,hdr,id,val16)', ret:r});
    } catch (e) { results.push({v:'set4b', err:String(e)}); }

    // Variant D: put size in hdr first dword
    try {
      hdr.writeU32(size);
      const set4b = new NativeFunction(setPtr, 'int', ['int', 'pointer', 'int', 'pointer']);
      const r = set4b(1, hdr, id, val);
      results.push({v:'set4b hdr.size=16', ret:r});
    } catch (e) { results.push({v:'set4b-sizehdr', err:String(e)}); }

    // Execute ALL — observed after successful CDP SetSetting
    // Guess Execute(settingId) or Execute(0) for ALL
    const execCandidates = [];
    try {
      const exec1 = new NativeFunction(execPtr, 'int', ['int']);
      execCandidates.push({v:'exec(0)', ret: exec1(0)});
      execCandidates.push({v:'exec(id)', ret: exec1(id)});
      execCandidates.push({v:'exec(-1)', ret: exec1(-1)});
    } catch (e) { execCandidates.push({v:'exec1', err:String(e)}); }
    try {
      const exec2 = new NativeFunction(execPtr, 'int', ['int', 'int']);
      execCandidates.push({v:'exec(1,id)', ret: exec2(1, id)});
      execCandidates.push({v:'exec(1,0)', ret: exec2(1, 0)});
    } catch (e) { execCandidates.push({v:'exec2', err:String(e)}); }

    return { results: results, exec: execCandidates, valHex: hexdump(val, {length:16, header:false, ansi:false}) };
  }
};
"""


def ace():
    key = winreg.OpenKey(
        winreg.HKEY_LOCAL_MACHINE,
        r"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE",
    )
    return (
        winreg.QueryValueEx(key, "InternalMuxState")[0],
        winreg.QueryValueEx(key, "InternalMuxIsAutomaticMode")[0],
        winreg.QueryValueEx(key, "ACESwitchedI2D")[0],
    )


def find_pid() -> int:
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
    raise SystemExit("No NvCpl host")


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "igpu"
    if mode == "auto":
        mux, automatic = 1, True
    elif mode == "igpu":
        mux, automatic = 1, False
    else:
        mux, automatic = 2, False

    pid = find_pid()
    before = ace()
    print("pid", pid, "mode", mode, "ACE", before)

    session = frida.attach(pid)
    script = session.create_script(JS)
    script.load()
    out = script.exports_sync.set_dds(mux, automatic)
    print(out)
    time.sleep(2.5)
    after = ace()
    print("ACE after", after)
    print("HIT" if before != after else "NO_HIT")
    session.detach()
    return 0 if before != after else 2


if __name__ == "__main__":
    raise SystemExit(main())
