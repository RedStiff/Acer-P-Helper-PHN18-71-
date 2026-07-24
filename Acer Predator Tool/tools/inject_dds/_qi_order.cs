using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class QiOrder {
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
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn2(IntPtr s,IntPtr a,IntPtr b);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn1(IntPtr s,IntPtr a);
  static IntPtr Vt(IntPtr o,int i){return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o),i*IntPtr.Size);}
  static IntPtr Z(int n){ var p=Marshal.AllocCoTaskMem(n); for(int i=0;i<n;i++) Marshal.WriteByte(p,i,0); return p; }
  static void Ace(string t){
    using(var k=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
      Console.WriteLine(t+" state="+k.GetValue("InternalMuxState")+" auto="+k.GetValue("InternalMuxIsAutomaticMode")+" i2d="+k.GetValue("ACESwitchedI2D"));
  }
  static void Qi(IntPtr p, Guid iid, string tag){
    IntPtr q; Guid g=iid; int hr=Marshal.QueryInterface(p, ref g, out q);
    Console.WriteLine(tag+" QI "+iid.ToString("D").Substring(0,8)+" HR=0x"+hr.ToString("X8")+(hr==0?" "+q.ToString("X"):""));
    if(hr==0 && q!=p) Marshal.Release(q);
  }
  static void Main(string[] args){
    string mode=args.Length>0?args[0]:"optimus";
    int mux=1,aut=0;
    if(mode=="dgpu"){mux=2;aut=0;}
    if(mode=="auto"){mux=1;aut=1;}
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    LoadLibrary(@"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll");

    object syncObj=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
    IntPtr sync=Marshal.GetIUnknownForObject(syncObj);
    Console.WriteLine("sync="+sync.ToString("X"));
    Qi(sync, IID_IStateData_NEW, "before-agg");
    Qi(sync, IID_IStateData_OLD, "before-agg");

    Guid isd=IID_IStateData_NEW; IntPtr sd;
    int hr=Marshal.QueryInterface(sync, ref isd, out sd);
    Console.WriteLine("keep sd HR=0x"+hr.ToString("X8")+" "+sd.ToString("X"));

    Guid c=CLSID_App, iu=IID_IUnknown; IntPtr filter;
    hr=CoCreateInstance(ref c, sync, 0x402, ref iu, out filter);
    Console.WriteLine("filter HR=0x"+hr.ToString("X8")+" "+filter.ToString("X"));
    Qi(sync, IID_IStateData_NEW, "after-agg-sync");
    Qi(sync, IID_IStateData_OLD, "after-agg-sync");
    Qi(filter, IID_IStateData_NEW, "after-agg-filt");
    Qi(filter, IID_IStateData_OLD, "after-agg-filt");

    // Patch? read iface+0x40 object and try QI
    IntPtr iface=IntPtr.Add(filter,0x18);
    IntPtr cached=Marshal.ReadIntPtr(iface,0x40);
    Console.WriteLine("cached@iface+40="+cached.ToString("X"));
    if(cached!=IntPtr.Zero){
      Qi(cached, IID_IStateData_OLD, "cached");
      Qi(cached, IID_IStateData_NEW, "cached");
    }

    // Try overwrite cached pointer with our working sd
    Console.WriteLine("patch iface+0x40 with sd");
    Marshal.WriteIntPtr(iface, 0x40, sd);

    var get1=(Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface,3),typeof(Fn1));
    var set1=(Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface,5),typeof(Fn1));
    Ace("before");

    IntPtr coll=Z(0x20), items=Z(0x40), data=Z(0x40);
    Marshal.Copy(HANDLE_LIVE.ToByteArray(),0,items,16);
    Marshal.WriteInt16(items,16,1); Marshal.WriteInt16(items,18,(short)SID);
    Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,data);
    Marshal.WriteInt32(data,0,3); Marshal.WriteInt32(data,4,4);
    Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
    hr=get1(iface,coll);
    Console.WriteLine("patched Fn1 Get HR=0x"+hr.ToString("X8")+" val="+Marshal.ReadInt32(data,8));

    // Set mux via patched Fn1
    IntPtr setColl=Z(0x20), setItems=Z(0x50), dMux=Z(0x20), dAuto=Z(0x20);
    Marshal.WriteInt32(dMux,0,3); Marshal.WriteInt32(dMux,4,4); Marshal.WriteInt32(dMux,8,mux);
    Marshal.WriteInt32(dAuto,0,5); Marshal.WriteInt32(dAuto,4,1); Marshal.WriteInt32(dAuto,8,aut);
    byte[] hb=HANDLE_LIVE.ToByteArray();
    Marshal.Copy(hb,0,setItems,16);
    Marshal.WriteInt16(setItems,16,1); Marshal.WriteInt16(setItems,18,(short)SID);
    Marshal.WriteInt32(setItems,20,4); Marshal.WriteIntPtr(setItems,24,dMux);
    IntPtr d1=IntPtr.Add(setItems,0x20);
    Marshal.Copy(hb,0,d1,16);
    Marshal.WriteInt16(d1,16,3); Marshal.WriteInt16(d1,18,(short)SID);
    Marshal.WriteInt32(d1,20,4); Marshal.WriteIntPtr(d1,24,dAuto);
    Marshal.WriteIntPtr(setColl,0,setItems); Marshal.WriteInt64(setColl,8,2);
    hr=set1(iface,setColl);
    Console.WriteLine("patched Fn1 Set HR=0x"+hr.ToString("X8"));

    // Also classic SyncProxy path with live handle
    var set=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,5),typeof(Fn2));
    var dop=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,6),typeof(Fn2));
    IntPtr setColl2=Z(0x20), setItems2=Z(0x50), dMux2=Z(0x20), dAuto2=Z(0x20);
    Marshal.WriteInt32(dMux2,0,3); Marshal.WriteInt32(dMux2,4,4); Marshal.WriteInt32(dMux2,8,mux);
    Marshal.WriteInt32(dAuto2,0,5); Marshal.WriteInt32(dAuto2,4,1); Marshal.WriteInt32(dAuto2,8,aut);
    Marshal.Copy(hb,0,setItems2,16);
    Marshal.WriteInt16(setItems2,16,1); Marshal.WriteInt16(setItems2,18,(short)SID);
    Marshal.WriteInt32(setItems2,20,4); Marshal.WriteIntPtr(setItems2,24,dMux2);
    IntPtr d1b=IntPtr.Add(setItems2,0x20);
    Marshal.Copy(hb,0,d1b,16);
    Marshal.WriteInt16(d1b,16,3); Marshal.WriteInt16(d1b,18,(short)SID);
    Marshal.WriteInt32(d1b,20,4); Marshal.WriteIntPtr(d1b,24,dAuto2);
    Marshal.WriteIntPtr(setColl2,0,setItems2); Marshal.WriteInt64(setColl2,8,2);
    hr=set(sd,setColl2,filter);
    Console.WriteLine("classic Set HR=0x"+hr.ToString("X8"));
    IntPtr op=Z(0x20);
    Marshal.Copy(OP.ToByteArray(),0,op,16);
    Marshal.WriteInt16(op,16,9); Marshal.WriteInt16(op,18,2);
    Console.WriteLine("DoOp HR=0x"+dop(sd,op,filter).ToString("X8"));
    System.Threading.Thread.Sleep(2500);
    Ace("after");
  }
}
