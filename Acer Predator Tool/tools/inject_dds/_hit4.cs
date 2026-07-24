using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class Hit4 {
  static readonly Guid CLSID_App=new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
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
  static int Set(Fn2 set,Fn2 dop,IntPtr sd,IntPtr filter,int mux,int aut,uint flags,bool doOp){
    IntPtr setColl=Z(0x20), setItems=Z(0x50), dMux=Z(0x20), dAuto=Z(0x20);
    Marshal.WriteInt32(dMux,0,3); Marshal.WriteInt32(dMux,4,4); Marshal.WriteInt32(dMux,8,mux);
    Marshal.WriteInt32(dAuto,0,5); Marshal.WriteInt32(dAuto,4,1); Marshal.WriteInt32(dAuto,8,aut);
    byte[] hb=HANDLE.ToByteArray();
    Marshal.Copy(hb,0,setItems,16);
    Marshal.WriteInt16(setItems,16,1); Marshal.WriteInt16(setItems,18,(short)SID);
    Marshal.WriteInt32(setItems,20,(int)flags); Marshal.WriteIntPtr(setItems,24,dMux);
    IntPtr d1=IntPtr.Add(setItems,0x20);
    Marshal.Copy(hb,0,d1,16);
    Marshal.WriteInt16(d1,16,3); Marshal.WriteInt16(d1,18,(short)SID);
    Marshal.WriteInt32(d1,20,(int)flags); Marshal.WriteIntPtr(d1,24,dAuto);
    Marshal.WriteIntPtr(setColl,0,setItems); Marshal.WriteInt64(setColl,8,2);
    int hr=set(sd,setColl,filter);
    Console.WriteLine("Set f="+flags+" mux="+mux+" auto="+aut+" HR=0x"+hr.ToString("X8"));
    if(doOp){
      IntPtr op=Z(0x20);
      Marshal.Copy(OP.ToByteArray(),0,op,16);
      Marshal.WriteInt16(op,16,9); Marshal.WriteInt16(op,18,2);
      int hr2=dop(sd,op,filter);
      Console.WriteLine("DoOp HR=0x"+hr2.ToString("X8"));
    }
    return hr;
  }
  static void Main(string[] args){
    int targetMux=2, targetAuto=0;
    if(args.Length>0 && args[0]=="optimus"){targetMux=1;targetAuto=0;}
    if(args.Length>0 && args[0]=="auto"){targetMux=1;targetAuto=1;}
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll");
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    object syncObj=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
    IntPtr sync=Marshal.GetIUnknownForObject(syncObj);
    Guid isd=IID_IStateData; IntPtr sd; Marshal.QueryInterface(sync,ref isd,out sd);
    var set=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,5),typeof(Fn2));
    var dop=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,6),typeof(Fn2));
    var get=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,3),typeof(Fn2));

    Guid[] cls={CLSID_Sys,CLSID_App}; string[] tags={"sys","app"};
    for(int i=0;i<2;i++){
      Guid iu=IID_IUnknown; IntPtr filter;
      int hr=CoCreateInstance(ref cls[i],sync,0x402,ref iu,out filter);
      Console.WriteLine("==== "+tags[i]+" filter HR=0x"+hr.ToString("X8"));
      if(hr!=0) continue;
      Ace(tags[i]+" before");

      // flags=0 set then DoOp (maybe commit)
      Set(set,dop,sd,filter,targetMux,targetAuto,0,true);
      System.Threading.Thread.Sleep(1500);
      Ace(tags[i]+" after f0+op");

      // flags=4 alone
      Set(set,dop,sd,filter,targetMux,targetAuto,4,true);
      System.Threading.Thread.Sleep(1500);
      Ace(tags[i]+" after f4");

      // sequence: f0 then f4
      Set(set,dop,sd,filter,targetMux,targetAuto,0,false);
      Set(set,dop,sd,filter,targetMux,targetAuto,4,true);
      System.Threading.Thread.Sleep(1500);
      Ace(tags[i]+" after f0thenf4");

      // Get after
      IntPtr coll=Z(0x20), items=Z(0x40), data=Z(0x20);
      byte[] hb=HANDLE.ToByteArray();
      Marshal.Copy(hb,0,items,16);
      Marshal.WriteInt16(items,16,1); Marshal.WriteInt16(items,18,(short)SID);
      Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,data);
      Marshal.WriteInt32(data,0,3); Marshal.WriteInt32(data,4,4);
      Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
      hr=get(sd,coll,filter);
      Console.WriteLine(tags[i]+" Get HR=0x"+hr.ToString("X8")+" val="+Marshal.ReadInt32(data,8));
    }
  }
}
