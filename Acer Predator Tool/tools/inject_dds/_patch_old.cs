using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class PatchOld {
  static readonly Guid CLSID_App=new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid IID_IUnknown=new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_IStateData_NEW=new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
  static readonly Guid IID_IStateData_OLD=new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");
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
  static void Main(string[] args){
    string mode=args.Length>0?args[0]:"optimus";
    int mux=1,aut=0;
    if(mode=="dgpu"){mux=2;aut=0;}
    if(mode=="auto"){mux=1;aut=1;}
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    object syncObj=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
    IntPtr sync=Marshal.GetIUnknownForObject(syncObj);
    Guid isdNew=IID_IStateData_NEW; IntPtr sdNew;
    Marshal.QueryInterface(sync, ref isdNew, out sdNew);

    Guid c=CLSID_App, iu=IID_IUnknown; IntPtr filter;
    int hr=CoCreateInstance(ref c, sync, 0x402, ref iu, out filter);
    Console.WriteLine("filter="+filter.ToString("X"));
    IntPtr iface=IntPtr.Add(filter,0x18);

    Guid isdOld=IID_IStateData_OLD; IntPtr sdOld;
    hr=Marshal.QueryInterface(filter, ref isdOld, out sdOld);
    Console.WriteLine("filt QI OLD IStateData HR=0x"+hr.ToString("X8")+" "+sdOld.ToString("X")+" iface="+iface.ToString("X"));

    // What is vt of sdOld? If same as iface, it's the secondary iface
    Console.WriteLine("sdOld vt="+Marshal.ReadIntPtr(sdOld).ToString("X")+" iface vt="+Marshal.ReadIntPtr(iface).ToString("X"));
    Console.WriteLine("sdOld vt[3]="+Vt(sdOld,3).ToString("X")+" (Get wrapper?)");
    Console.WriteLine("sdOld vt[5]="+Vt(sdOld,5).ToString("X")+" (Set wrapper?)");
    // If sdOld IS IStateData with real ProcessGetSettings, vt[3] should be stubless/proxy
    // If sdOld is filter secondary, vt[3] is 0x7f540 wrapper - recursion risk if we patch with it

    IntPtr before=Marshal.ReadIntPtr(iface,0x40);
    Console.WriteLine("cache before="+before.ToString("X"));

    // Patch with sdOld (supports 627D7951)
    Marshal.WriteIntPtr(iface, 0x40, sdOld);
    Console.WriteLine("cache after="+Marshal.ReadIntPtr(iface,0x40).ToString("X"));

    // Use SyncProxy path BUT pass sdOld as context? 
    var setNew=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sdNew,5),typeof(Fn2));
    var getNew=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sdNew,3),typeof(Fn2));
    var dopNew=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sdNew,6),typeof(Fn2));

    // Try calling sdOld as IStateData directly with Fn2(coll, filter) - if vt is proxy
    bool sdOldIsWrapper = Vt(sdOld,3)==Vt(iface,3);
    Console.WriteLine("sdOldIsWrapper="+sdOldIsWrapper);

    Ace("before");
    Guid handle=HANDLE_LIVE;

    if(!sdOldIsWrapper){
      var getOld=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sdOld,3),typeof(Fn2));
      var setOld=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sdOld,5),typeof(Fn2));
      IntPtr coll=Z(0x20), items=Z(0x40), data=Z(0x40);
      Marshal.Copy(handle.ToByteArray(),0,items,16);
      Marshal.WriteInt16(items,16,1); Marshal.WriteInt16(items,18,(short)SID);
      Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,data);
      Marshal.WriteInt32(data,0,3); Marshal.WriteInt32(data,4,4);
      Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
      hr=getOld(sdOld,coll,filter);
      Console.WriteLine("sdOld Get HR=0x"+hr.ToString("X8")+" val="+Marshal.ReadInt32(data,8));
    } else {
      // Call wrapper Get after patch - may recurse; use carefully with depth
      var get1=(Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface,3),typeof(Fn1));
      var set1=(Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface,5),typeof(Fn1));
      IntPtr coll=Z(0x20), items=Z(0x40), data=Z(0x40);
      Marshal.Copy(handle.ToByteArray(),0,items,16);
      Marshal.WriteInt16(items,16,1); Marshal.WriteInt16(items,18,(short)SID);
      Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,data);
      Marshal.WriteInt32(data,0,3); Marshal.WriteInt32(data,4,4);
      Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
      try {
        hr=get1(iface,coll);
        Console.WriteLine("wrapper Get after patch HR=0x"+hr.ToString("X8")+" val="+Marshal.ReadInt32(data,8));
      } catch(Exception ex){ Console.WriteLine("wrapper Get EX "+ex.Message); }

      // Set
      IntPtr setColl=Z(0x20), setItems=Z(0x50), dMux=Z(0x20), dAuto=Z(0x20);
      Marshal.WriteInt32(dMux,0,3); Marshal.WriteInt32(dMux,4,4); Marshal.WriteInt32(dMux,8,mux);
      Marshal.WriteInt32(dAuto,0,5); Marshal.WriteInt32(dAuto,4,1); Marshal.WriteInt32(dAuto,8,aut);
      byte[] hb=handle.ToByteArray();
      Marshal.Copy(hb,0,setItems,16);
      Marshal.WriteInt16(setItems,16,1); Marshal.WriteInt16(setItems,18,(short)SID);
      Marshal.WriteInt32(setItems,20,4); Marshal.WriteIntPtr(setItems,24,dMux);
      IntPtr d1=IntPtr.Add(setItems,0x20);
      Marshal.Copy(hb,0,d1,16);
      Marshal.WriteInt16(d1,16,3); Marshal.WriteInt16(d1,18,(short)SID);
      Marshal.WriteInt32(d1,20,4); Marshal.WriteIntPtr(d1,24,dAuto);
      Marshal.WriteIntPtr(setColl,0,setItems); Marshal.WriteInt64(setColl,8,2);
      try {
        hr=set1(iface,setColl);
        Console.WriteLine("wrapper Set HR=0x"+hr.ToString("X8"));
      } catch(Exception ex){ Console.WriteLine("wrapper Set EX "+ex.Message); }
    }

    // Classic with filter / sdOld as ctx
    IntPtr setColl2=Z(0x20), setItems2=Z(0x50), dMux2=Z(0x20), dAuto2=Z(0x20);
    Marshal.WriteInt32(dMux2,0,3); Marshal.WriteInt32(dMux2,4,4); Marshal.WriteInt32(dMux2,8,mux);
    Marshal.WriteInt32(dAuto2,0,5); Marshal.WriteInt32(dAuto2,4,1); Marshal.WriteInt32(dAuto2,8,aut);
    byte[] hb2=handle.ToByteArray();
    Marshal.Copy(hb2,0,setItems2,16);
    Marshal.WriteInt16(setItems2,16,1); Marshal.WriteInt16(setItems2,18,(short)SID);
    Marshal.WriteInt32(setItems2,20,4); Marshal.WriteIntPtr(setItems2,24,dMux2);
    IntPtr d1c=IntPtr.Add(setItems2,0x20);
    Marshal.Copy(hb2,0,d1c,16);
    Marshal.WriteInt16(d1c,16,3); Marshal.WriteInt16(d1c,18,(short)SID);
    Marshal.WriteInt32(d1c,20,4); Marshal.WriteIntPtr(d1c,24,dAuto2);
    Marshal.WriteIntPtr(setColl2,0,setItems2); Marshal.WriteInt64(setColl2,8,2);

    foreach(var ctx in new[]{ filter, sdOld, iface }){
      hr=setNew(sdNew,setColl2,ctx);
      Console.WriteLine("classic Set ctx="+ctx.ToString("X")+" HR=0x"+hr.ToString("X8"));
      if(hr==0){
        IntPtr op=Z(0x20);
        Marshal.Copy(OP.ToByteArray(),0,op,16);
        Marshal.WriteInt16(op,16,9); Marshal.WriteInt16(op,18,2);
        Console.WriteLine("DoOp HR=0x"+dopNew(sdNew,op,ctx).ToString("X8"));
        System.Threading.Thread.Sleep(2000);
        Ace("after ctx "+ctx.ToString("X"));
        break;
      }
    }
    Ace("final");
  }
}
