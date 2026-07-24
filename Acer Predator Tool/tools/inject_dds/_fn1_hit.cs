using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class Fn1Hit {
  static readonly Guid CLSID_App=new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid IID_IUnknown=new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid HANDLE_LIVE=new Guid("8FA752F3-70CA-49DC-BF80-58381E02E7F8");
  static readonly Guid OP=new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");
  const ushort SID=0x7d;
  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p,uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c,IntPtr o,uint ctx,ref Guid i,out IntPtr p);
  [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn1(IntPtr s,IntPtr a);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn2(IntPtr s,IntPtr a,IntPtr b);
  static IntPtr Vt(IntPtr o,int i){return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o),i*IntPtr.Size);}
  static IntPtr Z(int n){ var p=Marshal.AllocCoTaskMem(n); for(int i=0;i<n;i++) Marshal.WriteByte(p,i,0); return p; }
  static void Ace(string t){
    using(var k=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
      Console.WriteLine(t+" state="+k.GetValue("InternalMuxState")+" auto="+k.GetValue("InternalMuxIsAutomaticMode")+" i2d="+k.GetValue("ACESwitchedI2D"));
  }
  static void DumpData(IntPtr data,string tag){
    if(data==IntPtr.Zero){Console.WriteLine(tag+" null");return;}
    Console.WriteLine(tag+" type="+Marshal.ReadInt32(data,0)+" size="+Marshal.ReadInt32(data,4)+" val="+Marshal.ReadInt32(data,8));
  }
  static void Main(string[] args){
    string mode=args.Length>0?args[0]:"optimus"; // switch away from current dgpu
    int mux=1,aut=0;
    if(mode=="dgpu"){mux=2;aut=0;}
    if(mode=="auto"){mux=1;aut=1;}
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    object syncObj=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
    IntPtr sync=Marshal.GetIUnknownForObject(syncObj);
    Guid c=CLSID_App, iu=IID_IUnknown; IntPtr filter;
    int hr=CoCreateInstance(ref c,sync,0x402,ref iu,out filter);
    Console.WriteLine("filter HR=0x"+hr.ToString("X8")+" "+filter.ToString("X"));
    if(hr!=0) return;
    IntPtr iface=IntPtr.Add(filter,0x18);
    Console.WriteLine("iface="+iface.ToString("X")+" vt="+Marshal.ReadIntPtr(iface).ToString("X"));
    for(int i=0;i<12;i++) Console.WriteLine("  vt["+i+"]="+Vt(iface,i).ToString("X"));

    // slots 3=Get, 5=Set on secondary (from earlier dump); also try 8/10 absolute mistaken
    var get1=(Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface,3),typeof(Fn1));
    var set1=(Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface,5),typeof(Fn1));
    // DoOp might be slot 6
    var dop1=(Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface,6),typeof(Fn1));

    Ace("before");
    Guid handle=HANDLE_LIVE;

    // Get info 1,2,3 like App
    foreach(int info in new[]{2,1,3}){
      IntPtr coll=Z(0x20), items=Z(0x40), data=Z(0x40);
      Marshal.Copy(handle.ToByteArray(),0,items,16);
      Marshal.WriteInt16(items,16,(short)info); Marshal.WriteInt16(items,18,(short)SID);
      Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,data);
      Marshal.WriteInt32(data,0,info==3?5:3); Marshal.WriteInt32(data,4,info==3?1:4);
      Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
      hr=get1(iface,coll);
      Console.WriteLine("Fn1 Get info="+info+" HR=0x"+hr.ToString("X8"));
      DumpData(data,"  data");
    }

    // Set count=1 info=1 only (as App Stubless5 showed count=1!)
    {
      IntPtr coll=Z(0x20), items=Z(0x30), data=Z(0x20);
      Marshal.WriteInt32(data,0,3); Marshal.WriteInt32(data,4,4); Marshal.WriteInt32(data,8,mux);
      Marshal.Copy(handle.ToByteArray(),0,items,16);
      Marshal.WriteInt16(items,16,1); Marshal.WriteInt16(items,18,(short)SID);
      Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,data);
      Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
      hr=set1(iface,coll);
      Console.WriteLine("Fn1 Set mux-only HR=0x"+hr.ToString("X8"));
    }

    // Set count=2 mux+auto
    {
      IntPtr coll=Z(0x20), items=Z(0x50), dMux=Z(0x20), dAuto=Z(0x20);
      Marshal.WriteInt32(dMux,0,3); Marshal.WriteInt32(dMux,4,4); Marshal.WriteInt32(dMux,8,mux);
      Marshal.WriteInt32(dAuto,0,5); Marshal.WriteInt32(dAuto,4,1); Marshal.WriteInt32(dAuto,8,aut);
      byte[] hb=handle.ToByteArray();
      Marshal.Copy(hb,0,items,16);
      Marshal.WriteInt16(items,16,1); Marshal.WriteInt16(items,18,(short)SID);
      Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,dMux);
      IntPtr d1=IntPtr.Add(items,0x20);
      Marshal.Copy(hb,0,d1,16);
      Marshal.WriteInt16(d1,16,3); Marshal.WriteInt16(d1,18,(short)SID);
      Marshal.WriteInt32(d1,20,4); Marshal.WriteIntPtr(d1,24,dAuto);
      Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,2);
      hr=set1(iface,coll);
      Console.WriteLine("Fn1 Set mux+auto HR=0x"+hr.ToString("X8"));
    }

    // DoOp via Fn1
    {
      IntPtr op=Z(0x20);
      Marshal.Copy(OP.ToByteArray(),0,op,16);
      Marshal.WriteInt16(op,16,9); Marshal.WriteInt16(op,18,2);
      hr=dop1(iface,op);
      Console.WriteLine("Fn1 DoOp HR=0x"+hr.ToString("X8"));
    }

    System.Threading.Thread.Sleep(2000);
    Ace("after Fn1");

    // Re-get
    {
      IntPtr coll=Z(0x20), items=Z(0x40), data=Z(0x40);
      Marshal.Copy(handle.ToByteArray(),0,items,16);
      Marshal.WriteInt16(items,16,1); Marshal.WriteInt16(items,18,(short)SID);
      Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,data);
      Marshal.WriteInt32(data,0,3); Marshal.WriteInt32(data,4,4);
      Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
      hr=get1(iface,coll);
      Console.WriteLine("ReGet HR=0x"+hr.ToString("X8"));
      DumpData(data,"  re");
    }
  }
}
