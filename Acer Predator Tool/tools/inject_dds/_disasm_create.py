import frida, pathlib, sys, time

DIR = pathlib.Path(r"E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds")
pid = int(sys.argv[1])
lines = []

def on_msg(msg, data):
    if msg["type"] == "send":
        m = msg["payload"].get("m", str(msg["payload"]))
        print(m, flush=True)
        lines.append(m)
    else:
        print(msg, flush=True)
        lines.append(str(msg))

JS = r"""
'use strict';
function log(s){send({t:'log',m:String(s)});}
function modOff(a){const m=Process.findModuleByAddress(a);return m?m.name+'+0x'+a.sub(m.base).toString(16):String(a);}
const bat=Process.findModuleByName('nvxdbat.dll');
log('nvxdbat '+bat.base+' size='+bat.size);
const combase=Process.findModuleByName('combase.dll');
const gco=new NativeFunction(combase.findExportByName('CoGetClassObject'),'int',['pointer','uint','pointer','pointer','pointer']);
const clsid=Memory.alloc(16);
clsid.writeByteArray([0xC2,0x74,0x63,0x3F,0x40,0x35,0x6A,0x47,0xA1,0x23,0xD1,0xDA,0x2B,0x6D,0xDF,0x86]);
const iid=Memory.alloc(16);
iid.writeByteArray([0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xC0,0x00,0x00,0x00,0x00,0x00,0x00,0x46]);
const ppv=Memory.alloc(Process.pointerSize);
const hr=gco(clsid,0x402,ptr(0),iid,ppv);
log('GCO hr=0x'+hr.toString(16)+' cf='+ppv.readPointer());
const cf=ppv.readPointer();
const vt=cf.readPointer();
const create=vt.add(3*Process.pointerSize).readPointer();
log('CreateInstance='+create+' '+modOff(create));
let a=create;
for(let i=0;i<80;i++){
  const insn=Instruction.parse(a);
  log('0x'+a.sub(bat.base).toString(16)+'  '+insn.toString());
  a=insn.next;
  if(insn.mnemonic==='ret') break;
}
Interceptor.attach(create,{
  onEnter(args){
    this.outer=args[1]; this.iid=args[2]; this.ppv=args[3];
    log('Create enter self='+args[0]+' outer='+args[1]+' iid='+args[2]);
  },
  onLeave(r){ log('Create leave hr=0x'+(r.toInt32()>>>0).toString(16)); }
});
const FN=new NativeFunction(create,'int',['pointer','pointer','pointer','pointer']);
const iidU=Memory.alloc(16);
iidU.writeByteArray([0,0,0,0,0,0,0,0,0xc0,0,0,0,0,0,0,0x46]);
const out=Memory.alloc(Process.pointerSize); out.writePointer(ptr(0));
const hr2=FN(cf,ptr(0),iidU,out);
log('manual Create hr=0x'+(hr2>>>0).toString(16)+' obj='+out.readPointer());
"""

s = frida.attach(pid)
sc = s.create_script(JS)
sc.on("message", on_msg)
sc.load()
time.sleep(1)
(DIR / "_create_disasm.txt").write_text("\n".join(lines), encoding="utf-8")
print("saved", len(lines), flush=True)
