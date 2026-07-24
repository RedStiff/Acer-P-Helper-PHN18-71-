using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class Hit5 {
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
  static int SetOne(Fn2 set,IntPtr sd,IntPtr filter,Guid handle,int info,int muxOrAuto,bool isAuto,uint flags){
    IntPtr coll=Z(0x20), items=Z(0x30), data=Z(0x20);
    if(isAuto){ Marshal.WriteInt32(data,0,5); Marshal.WriteInt32(data,4,1); Marshal.WriteInt32(data,8,muxOrAuto); }
    else { Marshal.WriteInt32(data,0,3); Marshal.WriteInt32(data,4,4); Marshal.WriteInt32(data,8,muxOrAuto); }
    byte[] hb=handle.ToByteArray();
    Marshal.Copy(hb,0,items,16);
    Marshal.WriteInt16(items,16,(short)info); Marshal.WriteInt16(items,18,(short)SID);
    Marshal.WriteInt32(items,20,(int)flags); Marshal.WriteIntPtr(items,24,data);
    Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
    int hr=set(sd,coll,filter);
    Console.WriteLine("Set1 info="+info+" v="+muxOrAuto+" f="+flags+" handle="+(handle==Guid.Empty?"empty":"obs")+" HR=0x"+hr.ToString("X8"));
    return hr;
  }
  static void Main(){
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll");
    object syncObj=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
    IntPtr sync=Marshal.GetIUnknownForObject(syncObj);
    Guid c=CLSID_Sys, iu=IID_IUnknown; IntPtr filter;
    CoCreateInstance(ref c,sync,0x402,ref iu,out filter);
    Guid isd=IID_IStateData; IntPtr sd; Marshal.QueryInterface(sync,ref isd,out sd);
    var get=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,3),typeof(Fn2));
    var set=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,5),typeof(Fn2));
    var dop=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,6),typeof(Fn2));
    Ace("before");

    // Enumerate Get with empty handle, various infoIds
    for(int info=0; info<=8; info++){
      IntPtr coll=Z(0x20), items=Z(0x40), data=Z(0x40);
      Marshal.WriteInt16(items,16,(short)info); Marshal.WriteInt16(items,18,(short)SID);
      Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,data);
      Marshal.WriteInt32(data,0,3); Marshal.WriteInt32(data,4,4);
      Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
      int hr=get(sd,coll,filter);
      byte[] gb=new byte[16]; Marshal.Copy(items,gb,0,16);
      Guid h=new Guid(gb);
      Console.WriteLine("Get info="+info+" HR=0x"+hr.ToString("X8")+" handle="+h+" val="+Marshal.ReadInt32(data,8)+" type="+Marshal.ReadInt32(data,0));
    }

    // Set single-desc variants
    foreach(var h in new[]{Guid.Empty, HANDLE}){
      SetOne(set,sd,filter,h,1,2,false,4);
      SetOne(set,sd,filter,h,1,2,false,0);
      SetOne(set,sd,filter,h,3,0,true,4);
      SetOne(set,sd,filter,h,3,0,true,0);
    }

    // DoOp variants
    foreach(short a in new short[]{9,0,1,2,8,10})
    foreach(short b in new short[]{2,0,1,9}){
      IntPtr op=Z(0x20);
      Marshal.Copy(OP.ToByteArray(),0,op,16);
      Marshal.WriteInt16(op,16,a); Marshal.WriteInt16(op,18,b);
      int hr=dop(sd,op,filter);
      if(hr==0 || hr==unchecked((int)0xEAB00003))
        Console.WriteLine("DoOp "+a+","+b+" HR=0x"+hr.ToString("X8"));
    }
    Ace("after");
  }
}
