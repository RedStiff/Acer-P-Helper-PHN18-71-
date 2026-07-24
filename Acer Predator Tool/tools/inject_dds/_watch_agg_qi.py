import frida, pathlib, subprocess, time, sys

DIR = pathlib.Path(r"E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds")
EXE = DIR / "_filter_qi.exe"
# Use a longer-lived host instead
HOST_CS = DIR / "_hold_agg.cs"
HOST_EXE = DIR / "_hold_agg.exe"

HOST_CS.write_text(r'''
using System;
using System.Runtime.InteropServices;
using System.Threading;
class H {
  static readonly Guid CLSID=new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid IID_IUnknown=new Guid("00000000-0000-0000-C000-000000000046");
  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p,uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c,IntPtr o,uint ctx,ref Guid i,out IntPtr p);
  [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
  static void Main(){
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    object sync=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
    IntPtr syncUnk=Marshal.GetIUnknownForObject(sync);
    Console.WriteLine("SYNC="+syncUnk.ToString("X"));
    Console.WriteLine("PID="+System.Diagnostics.Process.GetCurrentProcess().Id);
    Console.Out.Flush();
    // wait for frida
    Thread.Sleep(3000);
    Guid c=CLSID,i=IID_IUnknown; IntPtr filter;
    int hr=CoCreateInstance(ref c, syncUnk, 0x402, ref i, out filter);
    Console.WriteLine("CCI HR=0x"+hr.ToString("X8")+" filter="+(hr==0?filter.ToString("X"):""));
    Console.Out.Flush();
    Thread.Sleep(60000);
  }
}
''', encoding='utf-8')

csc = r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
subprocess.check_call([csc, "/nologo", "/platform:x64", f"/out:{HOST_EXE}", str(HOST_CS)])

proc = subprocess.Popen([str(HOST_EXE)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
time.sleep(1)
# read pid
pid = None
sync = None
while True:
    line = proc.stdout.readline()
    if not line:
        break
    print("HOST", line.strip(), flush=True)
    if line.startswith("PID="):
        pid = int(line.strip().split("=")[1])
    if line.startswith("SYNC="):
        sync = line.strip().split("=")[1]
    if pid and sync:
        break

print("attach", pid, "sync", sync, flush=True)
lines = []

def on_msg(msg, data):
    if msg["type"] == "send":
        m = msg["payload"].get("m", str(msg["payload"]))
        print(m, flush=True)
        lines.append(m)
    else:
        print(msg, flush=True)

JS = r'''
'use strict';
function log(s){send({t:'log',m:String(s)});}
function modOff(a){const m=Process.findModuleByAddress(a);return m?m.name+'+0x'+a.sub(m.base).toString(16):String(a);}
function guid(p){
  try{
    const b=new Uint8Array(p.readByteArray(16));
    const d1=(b[0]|b[1]<<8|b[2]<<16|b[3]<<24)>>>0;
    const d2=b[4]|b[5]<<8,d3=b[6]|b[7]<<8;
    const h=[...b].map(x=>('0'+x.toString(16)).slice(-2)).join('');
    return ('{'+('00000000'+d1.toString(16)).slice(-8)+'-'+('0000'+d2.toString(16)).slice(-4)+'-'+('0000'+d3.toString(16)).slice(-4)+'-'+h.slice(16,20)+'-'+h.slice(20)+'}').toUpperCase();
  }catch(e){return '?';}
}
const bat=Process.findModuleByName('nvxdbat.dll');
const real=bat.base.add(0x7f780);
const syncPtr=ptr('SYNC_PLACEHOLDER');

// Hook QI on SyncProxy during create by intercepting all QI from SyncProxy vtable slot0
const syncVt=syncPtr.readPointer();
const syncQI=syncVt.readPointer();
log('sync vt='+syncVt+' QI='+modOff(syncQI));
let arm=false;
Interceptor.attach(syncQI,{
  onEnter(a){
    if(!arm) return;
    if(a[0].equals(syncPtr) || true){
      this.watch=true;
      this.iid=guid(a[1]);
    }
  },
  onLeave(r){
    if(!this.watch) return;
    log('SYNC QI '+this.iid+' hr=0x'+(r.toInt32()>>>0).toString(16));
  }
});

Interceptor.attach(real,{
  onEnter(a){
    arm=true;
    log('REAL enter outer='+a[0]+' iid='+guid(a[1]));
    log('  outer dump:\n'+hexdump(a[0],{length:0x40}));
    try{log('  outer vt='+modOff(a[0].readPointer()));}catch(e){}
  },
  onLeave(r){
    log('REAL leave hr=0x'+(r.toInt32()>>>0).toString(16));
    arm=false;
  }
});

// Also disassemble real with Frida stalker briefly - log branches that mention 80070057
log('hooks ready');
'''.replace('SYNC_PLACEHOLDER', '0x' + sync)

session = frida.attach(pid)
script = session.create_script(JS)
script.on("message", on_msg)
script.load()

# wait for CCI line
deadline = time.time() + 15
while time.time() < deadline:
    line = proc.stdout.readline()
    if line:
        print("HOST", line.strip(), flush=True)
        lines.append("HOST " + line.strip())
        if "CCI HR=" in line:
            time.sleep(1)
            break

(DIR / "_agg_qi_watch.txt").write_text("\n".join(lines), encoding="utf-8")
print("saved", len(lines), flush=True)
proc.kill()
