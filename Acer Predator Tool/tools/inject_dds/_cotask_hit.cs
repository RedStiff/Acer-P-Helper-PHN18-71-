using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class CotaskHit {
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
  static void Ace(string t){
    using(var k=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
      Console.WriteLine(t+" state="+k.GetValue("InternalMuxState")+" auto="+k.GetValue("InternalMuxIsAutomaticMode")+" i2d="+k.GetValue("ACESwitchedI2D"));
  }
  static IntPtr Z(int n){ IntPtr p=Marshal.AllocCoTaskMem(n); for(int i=0;i<n;i++) Marshal.WriteByte(p,i,0); return p; }
  static void Main(string[] args){
    string mode=args.Length>0?args[0]:"getonly";
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll");
    object syncObj=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
    IntPtr sync=Marshal.GetIUnknownForObject(syncObj);
    Guid c=CLSID_Sys, iu=IID_IUnknown; IntPtr filter;
    int hr=CoCreateInstance(ref c,sync,0x402,ref iu,out filter);
    Console.WriteLine("filter HR=0x"+hr.ToString("X8")+" "+filter.ToString("X"));
    if(hr!=0) return;
    Guid isd=IID_IStateData; IntPtr sd;
    hr=Marshal.QueryInterface(sync,ref isd,out sd);
    Console.WriteLine("sd HR=0x"+hr.ToString("X8"));
    var get=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,3),typeof(Fn2));
    var set=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,5),typeof(Fn2));
    var dop=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,6),typeof(Fn2));

    IntPtr coll=Z(0x20);
    IntPtr items=Z(0x40);
    IntPtr data=Z(0x40);
    Marshal.WriteIntPtr(coll,0,items);
    Marshal.WriteInt64(coll,8,1);
    Marshal.WriteInt16(items,16,1);
    Marshal.WriteInt16(items,18,(short)SID);
    Marshal.WriteInt32(items,20,4);
    Marshal.WriteIntPtr(items,24,data);
    Marshal.WriteInt32(data,0,3); Marshal.WriteInt32(data,4,4); Marshal.WriteInt32(data,8,0);

    Ace("before");
    Console.WriteLine("Get CoTaskMem..."); Console.Out.Flush();
    try{ hr=get(sd,coll,filter); Console.WriteLine("Get HR=0x"+hr.ToString("X8")); }
    catch(Exception ex){ Console.WriteLine("Get EX "+ex.Message); return; }

    long cnt=Marshal.ReadInt64(coll,8);
    IntPtr items2=Marshal.ReadIntPtr(coll,0);
    Console.WriteLine("count="+cnt+" items="+items2.ToString("X"));
    Guid handle=HANDLE;
    if(items2!=IntPtr.Zero && cnt>=1){
      byte[] gb=new byte[16]; Marshal.Copy(items2,gb,0,16); handle=new Guid(gb);
      Console.WriteLine("handle "+handle);
      IntPtr dp=Marshal.ReadIntPtr(items2,24);
      if(dp!=IntPtr.Zero){ byte[] db=new byte[16]; try{Marshal.Copy(dp,db,0,16); Console.Write("data "); foreach(var x in db)Console.Write(x.ToString("x2")+" "); Console.WriteLine();}catch{}}
    }
    if(mode=="getonly"){ Ace("after-get"); return; }

    int mux=2,aut=0;
    if(mode=="auto"){mux=1;aut=1;}
    if(mode=="optimus"){mux=1;aut=0;}
    IntPtr setColl=Z(0x20), setItems=Z(0x50), dMux=Z(0x20), dAuto=Z(0x20);
    Marshal.WriteInt32(dMux,0,3); Marshal.WriteInt32(dMux,4,4); Marshal.WriteInt32(dMux,8,mux);
    Marshal.WriteInt32(dAuto,0,5); Marshal.WriteInt32(dAuto,4,1); Marshal.WriteInt32(dAuto,8,aut);
    if(handle==Guid.Empty) handle=HANDLE;
    byte[] hb=handle.ToByteArray();
    Marshal.Copy(hb,0,setItems,16);
    Marshal.WriteInt16(setItems,16,1); Marshal.WriteInt16(setItems,18,(short)SID);
    Marshal.WriteInt32(setItems,20,4); Marshal.WriteIntPtr(setItems,24,dMux);
    IntPtr d1=IntPtr.Add(setItems,0x20);
    Marshal.Copy(hb,0,d1,16);
    Marshal.WriteInt16(d1,16,3); Marshal.WriteInt16(d1,18,(short)SID);
    Marshal.WriteInt32(d1,20,4); Marshal.WriteIntPtr(d1,24,dAuto);
    Marshal.WriteIntPtr(setColl,0,setItems); Marshal.WriteInt64(setColl,8,2);
    Console.WriteLine("Set mux="+mux+" auto="+aut); Console.Out.Flush();
    try{ hr=set(sd,setColl,filter); Console.WriteLine("Set HR=0x"+hr.ToString("X8")); }
    catch(Exception ex){ Console.WriteLine("Set EX "+ex.Message); return; }
    IntPtr op=Z(0x20);
    Marshal.Copy(OP.ToByteArray(),0,op,16);
    Marshal.WriteInt16(op,16,9); Marshal.WriteInt16(op,18,2);
    try{ hr=dop(sd,op,filter); Console.WriteLine("DoOp HR=0x"+hr.ToString("X8")); }
    catch(Exception ex){ Console.WriteLine("DoOp EX "+ex.Message); }
    System.Threading.Thread.Sleep(2000);
    Ace("after");
  }
}
