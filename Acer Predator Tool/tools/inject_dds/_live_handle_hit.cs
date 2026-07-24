using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class LiveHandleHit {
  static readonly Guid CLSID_App=new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid CLSID_Sys=new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");
  static readonly Guid IID_IUnknown=new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_IStateData=new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
  static readonly Guid HANDLE_LIVE=new Guid("8FA752F3-70CA-49DC-BF80-58381E02E7F8");
  static readonly Guid HANDLE_OLD=new Guid("AFE3D677-141F-424B-808D-340D9EC4ACD6");
  static readonly Guid OP=new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");
  const ushort SID=0x7d;
  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p,uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c,IntPtr o,uint ctx,ref Guid i,out IntPtr p);
  [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn2(IntPtr s,IntPtr a,IntPtr b);
  static IntPtr Vt(IntPtr o,int i){return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o),i*IntPtr.Size);}
  static IntPtr Z(int n){ var p=Marshal.AllocCoTaskMem(n); for(int i=0;i<n;i++) Marshal.WriteByte(p,i,0); return p; }
  static void Ace(string t){
    using(var k=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
      Console.WriteLine(t+" state="+k.GetValue("InternalMuxState")+" auto="+k.GetValue("InternalMuxIsAutomaticMode")+" i2d="+k.GetValue("ACESwitchedI2D"));
  }
  static int GetOne(Fn2 get,IntPtr sd,IntPtr filter,Guid handle,int info,uint flags){
    IntPtr coll=Z(0x20), items=Z(0x40), data=Z(0x40);
    Marshal.Copy(handle.ToByteArray(),0,items,16);
    Marshal.WriteInt16(items,16,(short)info); Marshal.WriteInt16(items,18,(short)SID);
    Marshal.WriteInt32(items,20,(int)flags); Marshal.WriteIntPtr(items,24,data);
    Marshal.WriteInt32(data,0, info==3?5:3); Marshal.WriteInt32(data,4, info==3?1:4);
    Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
    int hr=get(sd,coll,filter);
    int val=Marshal.ReadInt32(data,8);
    byte[] gb=new byte[16]; Marshal.Copy(items,gb,0,16);
    Console.WriteLine("Get info="+info+" f="+flags+" HR=0x"+hr.ToString("X8")+" handle="+new Guid(gb)+" val="+val);
    return hr;
  }
  static int SetMuxAuto(Fn2 set,Fn2 dop,IntPtr sd,IntPtr ctx,Guid handle,int mux,int aut,uint flags,bool doOp){
    IntPtr coll=Z(0x20), items=Z(0x50), dMux=Z(0x20), dAuto=Z(0x20);
    Marshal.WriteInt32(dMux,0,3); Marshal.WriteInt32(dMux,4,4); Marshal.WriteInt32(dMux,8,mux);
    Marshal.WriteInt32(dAuto,0,5); Marshal.WriteInt32(dAuto,4,1); Marshal.WriteInt32(dAuto,8,aut);
    byte[] hb=handle.ToByteArray();
    Marshal.Copy(hb,0,items,16);
    Marshal.WriteInt16(items,16,1); Marshal.WriteInt16(items,18,(short)SID);
    Marshal.WriteInt32(items,20,(int)flags); Marshal.WriteIntPtr(items,24,dMux);
    IntPtr d1=IntPtr.Add(items,0x20);
    Marshal.Copy(hb,0,d1,16);
    Marshal.WriteInt16(d1,16,3); Marshal.WriteInt16(d1,18,(short)SID);
    Marshal.WriteInt32(d1,20,(int)flags); Marshal.WriteIntPtr(d1,24,dAuto);
    Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,2);
    int hr=set(sd,coll,ctx);
    Console.WriteLine("Set mux="+mux+" auto="+aut+" f="+flags+" HR=0x"+hr.ToString("X8"));
    if(doOp){
      IntPtr op=Z(0x20);
      Marshal.Copy(OP.ToByteArray(),0,op,16);
      Marshal.WriteInt16(op,16,9); Marshal.WriteInt16(op,18,2);
      Console.WriteLine("DoOp HR=0x"+dop(sd,op,ctx).ToString("X8"));
    }
    return hr;
  }
  static void Main(string[] args){
    string mode=args.Length>0?args[0]:"dgpu";
    int mux=2,aut=0;
    if(mode=="optimus"){mux=1;aut=0;}
    if(mode=="auto"){mux=1;aut=1;}
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll");
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    object syncObj=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
    IntPtr sync=Marshal.GetIUnknownForObject(syncObj);
    Guid isd=IID_IStateData; IntPtr sd; Marshal.QueryInterface(sync,ref isd,out sd);
    var get=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,3),typeof(Fn2));
    var set=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,5),typeof(Fn2));
    var dop=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,6),typeof(Fn2));

    // App filter preferred
    Guid c=CLSID_App, iu=IID_IUnknown; IntPtr filter;
    int hr=CoCreateInstance(ref c,sync,0x402,ref iu,out filter);
    if(hr!=0){ c=CLSID_Sys; hr=CoCreateInstance(ref c,sync,0x402,ref iu,out filter); }
    Console.WriteLine("filter HR=0x"+hr.ToString("X8")+" "+filter.ToString("X"));
    IntPtr filterSec=IntPtr.Add(filter,0x18); // secondary iface seen in App
    Console.WriteLine("filter+0x18="+filterSec.ToString("X")+" ptr="+Marshal.ReadIntPtr(filterSec).ToString("X"));

    Ace("before");

    // App sequence: Get info2, Get info1, then Set
    foreach(var h in new[]{HANDLE_LIVE, HANDLE_OLD, Guid.Empty}){
      Console.WriteLine("==== handle "+h+" ====");
      GetOne(get,sd,filter,h,2,4);
      GetOne(get,sd,filter,h,1,4);
      GetOne(get,sd,filter,h,3,4);
      // Set via SyncProxy+filter
      SetMuxAuto(set,dop,sd,filter,h,mux,aut,4,true);
      System.Threading.Thread.Sleep(1500);
      Ace("after syncCtx "+(h==Guid.Empty?"empty":h.ToString().Substring(0,8)));

      // Set via filter+0x18 as BOTH this and? No - set needs IStateData this.
      // Try filter+0x18 as context only already done.
      // Try calling filterSec vt as if it were IStateData (slot 3/5/6 relative to its vt)
      try{
        IntPtr secObj=filter; // try with adjusted this = filter+0x18
        // In MI, interface pointer IS filter+0x18
        IntPtr iface=IntPtr.Add(filter,0x18);
        var getF=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(iface,3),typeof(Fn2));
        var setF=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(iface,5),typeof(Fn2));
        var dopF=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(iface,6),typeof(Fn2));
        Console.WriteLine("via filter+0x18 vt[3]="+Vt(iface,3).ToString("X")+" vt[5]="+Vt(iface,5).ToString("X"));
        GetOne(getF,iface,filter,h,1,4); // context maybe unused / null
        // App called filt.vt[8] and vt[10] - different slot numbering on secondary vt
        // secondary vt[8] from log = Get, vt[10] = Set. So relative to iface pointer's vt:
        // if iface = filter+0x18 and its vt[0] is first method of that iface...
        var get8=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(iface,8),typeof(Fn2));
        var set10=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(iface,10),typeof(Fn2));
        Console.WriteLine("vt8="+Vt(iface,8).ToString("X")+" vt10="+Vt(iface,10).ToString("X"));
        // App: filt.vt[8](coll) and vt[10](coll) with a2=0 - only one arg after this!
        // So signature may be Fn1 or Fn2 with null filter!
        IntPtr coll=Z(0x20), items=Z(0x50), dMux=Z(0x20), dAuto=Z(0x20);
        Marshal.WriteInt32(dMux,0,3); Marshal.WriteInt32(dMux,4,4); Marshal.WriteInt32(dMux,8,mux);
        Marshal.WriteInt32(dAuto,0,5); Marshal.WriteInt32(dAuto,4,1); Marshal.WriteInt32(dAuto,8,aut);
        byte[] hb=h.ToByteArray();
        Marshal.Copy(hb,0,items,16);
        Marshal.WriteInt16(items,16,1); Marshal.WriteInt16(items,18,(short)SID);
        Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,dMux);
        IntPtr d1=IntPtr.Add(items,0x20);
        Marshal.Copy(hb,0,d1,16);
        Marshal.WriteInt16(d1,16,3); Marshal.WriteInt16(d1,18,(short)SID);
        Marshal.WriteInt32(d1,20,4); Marshal.WriteIntPtr(d1,24,dAuto);
        Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,2);
        // App Set: a2=0
        hr=set10(iface,coll,IntPtr.Zero);
        Console.WriteLine("filter+18 set10 HR=0x"+hr.ToString("X8"));
        System.Threading.Thread.Sleep(1500);
        Ace("after filter+18 "+(h==Guid.Empty?"empty":h.ToString().Substring(0,8)));
      }catch(Exception ex){ Console.WriteLine("filter+18 EX "+ex.Message); }
    }
  }
}
