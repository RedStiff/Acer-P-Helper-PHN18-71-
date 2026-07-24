using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class Hit2 {
  static readonly Guid CLSID_Sys=new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");
  static readonly Guid IID_IUnknown=new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_IStateData=new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
  static readonly Guid HANDLE_OBS=new Guid("AFE3D677-141F-424B-808D-340D9EC4ACD6");
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
  static void Dump(IntPtr p,int n,string tag){
    if(p==IntPtr.Zero){Console.WriteLine(tag+" null"); return;}
    byte[] b=new byte[n];
    try{Marshal.Copy(p,b,0,n);}catch(Exception ex){Console.WriteLine(tag+" "+ex.Message);return;}
    Console.Write(tag+" ");
    for(int i=0;i<n;i++) Console.Write(b[i].ToString("x2")+(i%16==15?"\n     ":" "));
    Console.WriteLine();
  }
  static void Main(string[] args){
    string mode=args.Length>0?args[0]:"dgpu";
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll");
    object syncObj=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
    IntPtr sync=Marshal.GetIUnknownForObject(syncObj);
    Guid c=CLSID_Sys, iu=IID_IUnknown; IntPtr filter;
    int hr=CoCreateInstance(ref c,sync,0x402,ref iu,out filter);
    Console.WriteLine("filter HR=0x"+hr.ToString("X8"));
    Guid isd=IID_IStateData; IntPtr sd;
    hr=Marshal.QueryInterface(sync,ref isd,out sd);
    var get=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,3),typeof(Fn2));
    var ghi=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,4),typeof(Fn2));
    var set=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,5),typeof(Fn2));
    var dop=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,6),typeof(Fn2));

    // --- GetHandleInfo: try empty collection / various shapes ---
    Console.WriteLine("=== GetHandleInfo trials ===");
    // Trial A: same DescriptorCollection shape, sid=0, info=0
    IntPtr coll=Z(0x20); IntPtr items=Z(0x100); IntPtr data=Z(0x40);
    Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,0); // count 0 - ask server to fill?
    try{ hr=ghi(sd,coll,filter); Console.WriteLine("GHI count0 HR=0x"+hr.ToString("X8")+" count="+Marshal.ReadInt64(coll,8)+" items="+Marshal.ReadIntPtr(coll,0).ToString("X")); }
    catch(Exception ex){ Console.WriteLine("GHI A EX "+ex.Message); }

    // Trial B: count=1 empty desc
    Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
    try{ hr=ghi(sd,coll,filter); Console.WriteLine("GHI count1 HR=0x"+hr.ToString("X8")+" count="+Marshal.ReadInt64(coll,8)); Dump(Marshal.ReadIntPtr(coll,0),0x40,"ghi-items"); }
    catch(Exception ex){ Console.WriteLine("GHI B EX "+ex.Message); }

    // Trial C: maybe GetHandleInfo takes different args - try (out ptr, filter)
    IntPtr outBuf=Z(0x200);
    try{ hr=ghi(sd,outBuf,filter); Console.WriteLine("GHI outBuf HR=0x"+hr.ToString("X8")); Dump(outBuf,0x40,"outBuf"); }
    catch(Exception ex){ Console.WriteLine("GHI C EX "+ex.Message); }

    // --- GetSettings with observed handle ---
    Console.WriteLine("=== Get with known handle ===");
    items=Z(0x40); data=Z(0x40); coll=Z(0x20);
    byte[] hb=HANDLE_OBS.ToByteArray();
    Marshal.Copy(hb,0,items,16);
    Marshal.WriteInt16(items,16,1); Marshal.WriteInt16(items,18,(short)SID);
    Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,data);
    Marshal.WriteInt32(data,0,3); Marshal.WriteInt32(data,4,4); Marshal.WriteInt32(data,8,0);
    Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
    Ace("before-get");
    hr=get(sd,coll,filter);
    Console.WriteLine("Get HR=0x"+hr.ToString("X8"));
    Dump(items,0x30,"items");
    Dump(data,0x10,"data");

    // also get infoId=3 auto
    IntPtr items3=Z(0x40); IntPtr data3=Z(0x40); IntPtr coll3=Z(0x20);
    Marshal.Copy(hb,0,items3,16);
    Marshal.WriteInt16(items3,16,3); Marshal.WriteInt16(items3,18,(short)SID);
    Marshal.WriteInt32(items3,20,4); Marshal.WriteIntPtr(items3,24,data3);
    Marshal.WriteInt32(data3,0,5); Marshal.WriteInt32(data3,4,1); Marshal.WriteInt32(data3,8,0);
    Marshal.WriteIntPtr(coll3,0,items3); Marshal.WriteInt64(coll3,8,1);
    hr=get(sd,coll3,filter);
    Console.WriteLine("Get auto HR=0x"+hr.ToString("X8"));
    Dump(data3,0x10,"data3");

    // --- Set with known handle ---
    int mux=2,aut=0;
    if(mode=="auto"){mux=1;aut=1;}
    if(mode=="optimus"){mux=1;aut=0;}
    IntPtr setColl=Z(0x20), setItems=Z(0x50), dMux=Z(0x20), dAuto=Z(0x20);
    Marshal.WriteInt32(dMux,0,3); Marshal.WriteInt32(dMux,4,4); Marshal.WriteInt32(dMux,8,mux);
    Marshal.WriteInt32(dAuto,0,5); Marshal.WriteInt32(dAuto,4,1); Marshal.WriteInt32(dAuto,8,aut);
    Marshal.Copy(hb,0,setItems,16);
    Marshal.WriteInt16(setItems,16,1); Marshal.WriteInt16(setItems,18,(short)SID);
    Marshal.WriteInt32(setItems,20,4); Marshal.WriteIntPtr(setItems,24,dMux);
    IntPtr d1=IntPtr.Add(setItems,0x20);
    Marshal.Copy(hb,0,d1,16);
    Marshal.WriteInt16(d1,16,3); Marshal.WriteInt16(d1,18,(short)SID);
    Marshal.WriteInt32(d1,20,4); Marshal.WriteIntPtr(d1,24,dAuto);
    Marshal.WriteIntPtr(setColl,0,setItems); Marshal.WriteInt64(setColl,8,2);
    Ace("before-set");
    Console.WriteLine("Set mux="+mux+" auto="+aut);
    hr=set(sd,setColl,filter);
    Console.WriteLine("Set HR=0x"+hr.ToString("X8"));
    IntPtr op=Z(0x20);
    Marshal.Copy(OP.ToByteArray(),0,op,16);
    Marshal.WriteInt16(op,16,9); Marshal.WriteInt16(op,18,2);
    hr=dop(sd,op,filter);
    Console.WriteLine("DoOp HR=0x"+hr.ToString("X8"));
    System.Threading.Thread.Sleep(2500);
    Ace("after");

    // Re-get
    hr=get(sd,coll,filter);
    Console.WriteLine("ReGet HR=0x"+hr.ToString("X8"));
    Dump(data,0x10,"re-data");
  }
}
