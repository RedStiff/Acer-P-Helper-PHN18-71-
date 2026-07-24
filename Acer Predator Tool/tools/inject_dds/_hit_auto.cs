using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class HitAuto {
  static readonly Guid CLSID_Sys=new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");
  static readonly Guid IID_IUnknown=new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_IStateData=new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
  static readonly Guid HANDLE=new Guid("AFE3D677-141F-424B-808D-340D9EC4ACD6");
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
  static void Main(string[] args){
    int aut = (args.Length>0 && args[0]=="0") ? 0 : 1;
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll");
    object syncObj=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
    IntPtr sync=Marshal.GetIUnknownForObject(syncObj);
    Guid c=CLSID_Sys, iu=IID_IUnknown; IntPtr filter;
    CoCreateInstance(ref c,sync,0x402,ref iu,out filter);
    Guid isd=IID_IStateData; IntPtr sd; Marshal.QueryInterface(sync,ref isd,out sd);
    var set=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,5),typeof(Fn2));
    var dop=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,6),typeof(Fn2));

    Ace("before");
    // Only auto infoId=3 flags=4
    IntPtr coll=Z(0x20), items=Z(0x30), data=Z(0x20);
    Marshal.WriteInt32(data,0,5); Marshal.WriteInt32(data,4,1); Marshal.WriteInt32(data,8,aut);
    // try both empty and observed handle
    foreach(var h in new[]{Guid.Empty, HANDLE}){
      byte[] hb=h.ToByteArray();
      Marshal.Copy(hb,0,items,16);
      Marshal.WriteInt16(items,16,3); Marshal.WriteInt16(items,18,(short)SID);
      Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,data);
      Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
      int hr=set(sd,coll,filter);
      Console.WriteLine("Set auto="+aut+" handle="+(h==Guid.Empty?"empty":"obs")+" HR=0x"+hr.ToString("X8"));
      IntPtr op=Z(0x20);
      Marshal.Copy(OP.ToByteArray(),0,op,16);
      Marshal.WriteInt16(op,16,9); Marshal.WriteInt16(op,18,2);
      int hr2=dop(sd,op,filter);
      Console.WriteLine("DoOp HR=0x"+hr2.ToString("X8"));
      System.Threading.Thread.Sleep(2000);
      Ace("after auto="+aut+" "+(h==Guid.Empty?"empty":"obs"));
    }

    // Combined: auto flags=4 + mux flags=0 then DoOp
    IntPtr setColl=Z(0x20), setItems=Z(0x50), dMux=Z(0x20), dAuto=Z(0x20);
    Marshal.WriteInt32(dMux,0,3); Marshal.WriteInt32(dMux,4,4); Marshal.WriteInt32(dMux,8,1); // keep optimus mux
    Marshal.WriteInt32(dAuto,0,5); Marshal.WriteInt32(dAuto,4,1); Marshal.WriteInt32(dAuto,8,aut);
    byte[] hb2=HANDLE.ToByteArray();
    Marshal.Copy(hb2,0,setItems,16);
    Marshal.WriteInt16(setItems,16,1); Marshal.WriteInt16(setItems,18,(short)SID);
    Marshal.WriteInt32(setItems,20,0); Marshal.WriteIntPtr(setItems,24,dMux); // mux flags=0
    IntPtr d1=IntPtr.Add(setItems,0x20);
    Marshal.Copy(hb2,0,d1,16);
    Marshal.WriteInt16(d1,16,3); Marshal.WriteInt16(d1,18,(short)SID);
    Marshal.WriteInt32(d1,20,4); Marshal.WriteIntPtr(d1,24,dAuto); // auto flags=4
    Marshal.WriteIntPtr(setColl,0,setItems); Marshal.WriteInt64(setColl,8,2);
    int hr3=set(sd,setColl,filter);
    Console.WriteLine("Set combo mux.f0+auto.f4 HR=0x"+hr3.ToString("X8"));
    IntPtr op2=Z(0x20);
    Marshal.Copy(OP.ToByteArray(),0,op2,16);
    Marshal.WriteInt16(op2,16,9); Marshal.WriteInt16(op2,18,2);
    Console.WriteLine("DoOp combo HR=0x"+dop(sd,op2,filter).ToString("X8"));
    System.Threading.Thread.Sleep(2500);
    Ace("after combo");
  }
}
