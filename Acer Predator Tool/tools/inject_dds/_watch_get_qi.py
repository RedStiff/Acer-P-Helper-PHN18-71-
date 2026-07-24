import frida, pathlib, subprocess, time, sys

DIR = pathlib.Path(r"E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds")
CS = DIR / "_hold_get.cs"
EXE = DIR / "_hold_get.exe"

CS.write_text(r'''
using System;
using System.Runtime.InteropServices;
using System.Threading;
class H {
  static readonly Guid CLSID=new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid IID_IUnknown=new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_IStateData=new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
  const ushort SID=0x7d;
  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p,uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c,IntPtr o,uint ctx,ref Guid i,out IntPtr p);
  [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn2(IntPtr s,IntPtr a,IntPtr b);
  static IntPtr Vt(IntPtr o,int i){return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o),i*IntPtr.Size);}
  static void Main(){
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    object sync=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
    IntPtr syncUnk=Marshal.GetIUnknownForObject(sync);
    Guid c=CLSID,iu=IID_IUnknown; IntPtr filter;
    int hr=CoCreateInstance(ref c,syncUnk,0x402,ref iu,out filter);
    Console.WriteLine("FILTER="+filter.ToString("X")+" HR=0x"+hr.ToString("X8"));
    Guid isd=IID_IStateData; IntPtr sd;
    hr=Marshal.QueryInterface(syncUnk,ref isd,out sd);
    Console.WriteLine("SD="+sd.ToString("X")+" HR=0x"+hr.ToString("X8"));
    Console.WriteLine("SYNC="+syncUnk.ToString("X"));
    Console.WriteLine("PID="+System.Diagnostics.Process.GetCurrentProcess().Id);
    Console.Out.Flush();
    Thread.Sleep(4000); // frida attach
    IntPtr arena=Marshal.AllocHGlobal(0x1000);
    for(int i=0;i<0x1000;i++) Marshal.WriteByte(arena,i,0);
    IntPtr coll=arena, items=IntPtr.Add(arena,0x40), data=IntPtr.Add(arena,0x200);
    Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
    Marshal.WriteInt16(items,16,1); Marshal.WriteInt16(items,18,(short)SID);
    Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,data);
    Marshal.WriteInt32(data,0,3); Marshal.WriteInt32(data,4,4); Marshal.WriteInt32(data,8,0);
    var get=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,3),typeof(Fn2));
    Console.WriteLine("GET...");
    Console.Out.Flush();
    try{ hr=get(sd,coll,filter); Console.WriteLine("Get HR=0x"+hr.ToString("X8")); }
    catch(Exception ex){ Console.WriteLine("Get EX "+ex.Message); }
    Console.Out.Flush();
    Thread.Sleep(5000);
  }
}
''', encoding='utf-8')

subprocess.check_call([
    r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "/nologo", "/platform:x64", f"/out:{EXE}", str(CS)
])

proc = subprocess.Popen([str(EXE)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
meta = {}
while True:
    line = proc.stdout.readline()
    if not line:
        break
    print("HOST", line.strip(), flush=True)
    if "=" in line:
        k, _, v = line.strip().partition("=")
        meta[k] = v.split()[0] if v else ""
    if "PID=" in line:
        break

pid = int(meta["PID"])
filter_p = meta.get("FILTER", "0")
print("attach", pid, "filter", filter_p, flush=True)

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
function guid(p){
  try{
    const b=new Uint8Array(p.readByteArray(16));
    const d1=(b[0]|b[1]<<8|b[2]<<16|b[3]<<24)>>>0;
    const d2=b[4]|b[5]<<8,d3=b[6]|b[7]<<8;
    const h=[...b].map(x=>('0'+x.toString(16)).slice(-2)).join('');
    return ('{'+('00000000'+d1.toString(16)).slice(-8)+'-'+('0000'+d2.toString(16)).slice(-4)+'-'+('0000'+d3.toString(16)).slice(-4)+'-'+h.slice(16,20)+'-'+h.slice(20)+'}').toUpperCase();
  }catch(e){return '?';}
}
function modOff(a){const m=Process.findModuleByAddress(a);return m?m.name+'+0x'+a.sub(m.base).toString(16):String(a);}

const filter=ptr('0xFILTER');
const fvt=filter.readPointer();
const fqi=fvt.readPointer();
log('filter vt='+modOff(fvt)+' QI='+modOff(fqi));

Interceptor.attach(fqi,{
  onEnter(a){
    if(!a[0].equals(filter)) return;
    this.hit=true; this.iid=guid(a[1]); this.ppv=a[2];
  },
  onLeave(r){
    if(!this.hit) return;
    let p=ptr(0); try{if(r.toInt32()===0)p=this.ppv.readPointer();}catch(e){}
    log('FILTER QI '+this.iid+' hr=0x'+(r.toInt32()>>>0).toString(16)+' -> '+p);
  }
});

// Also hook combase ObjectStublessClient3
const combase=Process.findModuleByName('combase.dll');
Interceptor.attach(combase.findExportByName('ObjectStublessClient3'),{
  onEnter(a){
    log('Stubless3 this='+a[0]+' a1='+a[1]+' a2='+a[2]);
    try{log(' a1:\n'+hexdump(a[1],{length:0x30}));}catch(e){}
    try{log(' a2 vt='+modOff(a[2].readPointer()));}catch(e){}
  },
  onLeave(r){ log('Stubless3 hr=0x'+(r.toInt32()>>>0).toString(16)); }
});

log('ready');
'''.replace('FILTER', filter_p)

session = frida.attach(pid)
script = session.create_script(JS)
script.on("message", on_msg)
script.load()

# wait for GET / crash
deadline = time.time() + 20
while time.time() < deadline:
    line = proc.stdout.readline()
    if not line:
        if proc.poll() is not None:
            print("proc exited", proc.returncode, flush=True)
            break
        time.sleep(0.1)
        continue
    print("HOST", line.strip(), flush=True)
    lines.append("HOST " + line.strip())
    if "Get HR=" in line or "Get EX" in line:
        time.sleep(1)
        break

(DIR / "_hold_get_watch.txt").write_text("\n".join(lines), encoding="utf-8")
print("saved", len(lines), flush=True)
try:
    proc.kill()
except Exception:
    pass
