"""SetSetting(1,hdr,0x330,val16) + Execute(0x10000) inside NVIDIA App."""
from __future__ import annotations

import sys
import time
import winreg

import frida

JS = r"""
'use strict';
function nvcpl() {
  return Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
}
rpc.exports = {
  apply: function (mux, automatic) {
    const m = nvcpl();
    const setPtr = m.findExportByName('NvCplApiSetSetting');
    const execPtr = m.findExportByName('NvCplApiExecute');
    const id = 0x330;
    const ALL = 0x10000;

    const block = Memory.alloc(0x40);
    block.writeByteArray(new Array(0x40).fill(0));
    // mirror live layout: a1 at block, value at block+0x10
    const a1 = block;
    const val = block.add(0x10);
    // Live captures:
    //   Optimus: 01 00 00 00 | MuxState | 0
    //   dGPU:    01 00 00 00 | MuxState | 0
    //   Auto:    01 01 00 00 | MuxState | 0   (automatic is byte1 of first dword)
    val.writeU32(0x01 | ((automatic ? 1 : 0) << 8));
    val.add(4).writeU32(mux | 0);
    val.add(8).writeU32(0);
    val.add(12).writeU32(0);

    const set = new NativeFunction(setPtr, 'int', ['int', 'pointer', 'int', 'pointer']);
    const exec1 = new NativeFunction(execPtr, 'int', ['int']);
    const exec2 = new NativeFunction(execPtr, 'int', ['int', 'int']);
    const exec3 = new NativeFunction(execPtr, 'int', ['int', 'int', 'int']);

    const setRet = set(1, a1, id, val);
    // Live capture: Execute(0x10000, 0, -1, ...)
    let execRet = null;
    const execTries = [];
    try { execTries.push({v:'exec1(ALL)', ret: exec1(ALL)}); } catch (e) { execTries.push({v:'exec1', err:String(e)}); }
    try { execTries.push({v:'exec2(ALL,0)', ret: exec2(ALL, 0)}); } catch (e) { execTries.push({v:'exec2', err:String(e)}); }
    try { execTries.push({v:'exec3(ALL,0,-1)', ret: exec3(ALL, 0, -1)}); } catch (e) { execTries.push({v:'exec3', err:String(e)}); }

    return {
      setRet: setRet,
      execTries: execTries,
      val: hexdump(val, {length: 16, header: false, ansi: false})
    };
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
    raise SystemExit("no pid")


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "dgpu"
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
    print(script.exports_sync.apply(mux, automatic))
    time.sleep(3)
    after = ace()
    print("ACE after", after)
    print("HIT" if before != after else "NO_HIT")
    session.detach()
    return 0 if before != after else 2


if __name__ == "__main__":
    raise SystemExit(main())
