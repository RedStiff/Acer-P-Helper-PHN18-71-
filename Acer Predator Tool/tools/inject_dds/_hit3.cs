using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class Hit3 {
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
  static void Dump(IntPtr p,int n,string tag){
    if(p==IntPtr.Zero){Console.WriteLine(tag+" null");return;}
    byte[] b=new byte[n]; try{Marshal.Copy(p,b,0,n);}catch(Exception ex){Console.WriteLine(tag+" "+ex.Message);return;}
    Console.Write(tag+" "); for(int i=0;i<n;i++) Console.Write(b[i].ToString("x2")+(i%16==15?"\n     ":" ")); Console.WriteLine();
  }
  static IntPtr MakeFilter(IntPtr sync, Guid clsid, string tag){
    Guid iu=IID_IUnknown; IntPtr f;
    int hr=CoCreateInstance(ref clsid,sync,0x402,ref iu,out f);
    Console.WriteLine(tag+" filter HR=0x"+hr.ToString("X8")+(hr==0?" "+f.ToString("X"):""));
    return hr==0?f:IntPtr.Zero;
  }
  static int DoSet(Fn2 set, Fn2 dop, IntPtr sd, IntPtr filter, Guid handle, int mux, int aut, uint flags){
    IntPtr setColl=Z(0x20), setItems=Z(0x50), dMux=Z(0x20), dAuto=Z(0x20);
    Marshal.WriteInt32(dMux,0,3); Marshal.WriteInt32(dMux,4,4); Marshal.WriteInt32(dMux,8,mux);
    Marshal.WriteInt32(dAuto,0,5); Marshal.WriteInt32(dAuto,4,1); Marshal.WriteInt32(dAuto,8,aut);
    byte[] hb=handle.ToByteArray();
    Marshal.Copy(hb,0,setItems,16);
    Marshal.WriteInt16(setItems,16,1); Marshal.WriteInt16(setItems,18,(short)SID);
    Marshal.WriteInt32(setItems,20,(int)flags); Marshal.WriteIntPtr(setItems,24,dMux);
    IntPtr d1=IntPtr.Add(setItems,0x20);
    Marshal.Copy(hb,0,d1,16);
    Marshal.WriteInt16(d1,16,3); Marshal.WriteInt16(d1,18,(short)SID);
    Marshal.WriteInt32(d1,20,(int)flags); Marshal.WriteIntPtr(d1,24,dAuto);
    Marshal.WriteIntPtr(setColl,0,setItems); Marshal.WriteInt64(setColl,8,2);
    int hr=set(sd,setColl,filter);
    Console.WriteLine("Set flags="+flags+" mux="+mux+" auto="+aut+" HR=0x"+hr.ToString("X8"));
    if(hr==0){
      IntPtr op=Z(0x20);
      Marshal.Copy(OP.ToByteArray(),0,op,16);
      Marshal.WriteInt16(op,16,9); Marshal.WriteInt16(op,18,2);
      int hr2=dop(sd,op,filter);
      Console.WriteLine("DoOp HR=0x"+hr2.ToString("X8"));
    }
    return hr;
  }
  static void Main(){
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll");
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    object syncObj=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
    IntPtr sync=Marshal.GetIUnknownForObject(syncObj);
    Guid isd=IID_IStateData; IntPtr sd; Marshal.QueryInterface(sync,ref isd,out sd);
    var get=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,3),typeof(Fn2));
    var set=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,5),typeof(Fn2));
    var dop=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,6),typeof(Fn2));

    string[] tags = new string[]{"sys","app"};
    Guid[] clsids = new Guid[]{CLSID_Sys, CLSID_App};
    for(int pi=0; pi<2; pi++){
      IntPtr filter=MakeFilter(sync, clsids[pi], tags[pi]);
      if(filter==IntPtr.Zero) continue;

      // Get with null data ptr
      IntPtr coll=Z(0x20), items=Z(0x40);
      byte[] hb=HANDLE.ToByteArray();
      Marshal.Copy(hb,0,items,16);
      Marshal.WriteInt16(items,16,1); Marshal.WriteInt16(items,18,(short)SID);
      Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,IntPtr.Zero);
      Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
      int hr=get(sd,coll,filter);
      Console.WriteLine(tags[pi]+" Get nullData HR=0x"+hr.ToString("X8"));
      IntPtr items2=Marshal.ReadIntPtr(coll,0);
      Dump(items2,0x30,tags[pi]+" items");
      if(items2!=IntPtr.Zero){
        IntPtr dp=Marshal.ReadIntPtr(items2,24);
        Dump(dp,0x10,tags[pi]+" data");
      }

      Ace(tags[pi]+" before");
      foreach(uint flags in new uint[]{4,0,1,2,3,5,8,0x14,0x104}){
        hr=DoSet(set,dop,sd,filter,HANDLE,2,0,flags);
        if(hr==0){ System.Threading.Thread.Sleep(2000); Ace(tags[pi]+" HIT flags="+flags); return; }
      }
      // try optimus values too with flags=4
      DoSet(set,dop,sd,filter,HANDLE,1,0,4);
      DoSet(set,dop,sd,filter,HANDLE,1,1,4);
    }
    Ace("done");
  }
}

