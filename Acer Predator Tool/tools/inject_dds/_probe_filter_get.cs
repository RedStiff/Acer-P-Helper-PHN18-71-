using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class Probe {
  static readonly Guid CLSID_AppFilter = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid CLSID_SysFilter = new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");
  static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_IStateData = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
  static readonly Guid IID_IClassFactory = new Guid("00000001-0000-0000-C000-000000000046");
  static readonly Guid HANDLE_DDS = new Guid("AFE3D677-141F-424B-808D-340D9EC4ACD6");
  const ushort SID=0x7d;

  [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr p,uint f);
  [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr ppv);
  [DllImport("ole32.dll")] static extern int CoGetClassObject(ref Guid clsid, uint ctx, IntPtr server, ref Guid iid, out IntPtr ppv);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
  [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr h, string n);
  [DllImport("oleaut32.dll", CharSet=CharSet.Unicode)] static extern int LoadTypeLib(string p, out IntPtr pptlib);

  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int DllGetClassObject(ref Guid clsid, ref Guid iid, out IntPtr ppv);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int FnCreate(IntPtr self, IntPtr outer, ref Guid iid, out IntPtr ppv);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int Fn2(IntPtr self, IntPtr a, IntPtr b);

  static IntPtr Vt(IntPtr o,int i){return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o), i*IntPtr.Size);}
  static void Ace(string t){
    using(var k=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
      Console.WriteLine(t+" state="+k.GetValue("InternalMuxState")+" auto="+k.GetValue("InternalMuxIsAutomaticMode")+" i2d="+k.GetValue("ACESwitchedI2D"));
  }

  static IntPtr TryCoCreate(Guid clsid, uint ctx, string tag){
    Guid iid=IID_IUnknown; IntPtr p;
    int hr=CoCreateInstance(ref clsid, IntPtr.Zero, ctx, ref iid, out p);
    Console.WriteLine("CoCreate "+tag+" ctx=0x"+ctx.ToString("x")+" HR=0x"+hr.ToString("X8")+(hr==0?" p="+p.ToString("X"):""));
    return hr==0?p:IntPtr.Zero;
  }

  static IntPtr TryDllFactory(string dll, Guid clsid, string tag){
    IntPtr h=LoadLibrary(dll);
    Console.WriteLine("LoadLibrary "+tag+" "+(h==IntPtr.Zero?"FAIL":h.ToString("X")));
    if(h==IntPtr.Zero) return IntPtr.Zero;
    IntPtr proc=GetProcAddress(h,"DllGetClassObject");
    if(proc==IntPtr.Zero){Console.WriteLine(" no DllGetClassObject"); return IntPtr.Zero;}
    var dgco=(DllGetClassObject)Marshal.GetDelegateForFunctionPointer(proc,typeof(DllGetClassObject));
    Guid iidF=IID_IClassFactory; IntPtr factory;
    Guid c=clsid;
    int hr=dgco(ref c, ref iidF, out factory);
    Console.WriteLine(" DllGetClassObject HR=0x"+hr.ToString("X8"));
    if(hr!=0) return IntPtr.Zero;
    var create=(FnCreate)Marshal.GetDelegateForFunctionPointer(Vt(factory,3),typeof(FnCreate));
    Guid iid=IID_IUnknown; IntPtr obj;
    // no aggregation
    hr=create(factory, IntPtr.Zero, ref iid, out obj);
    Console.WriteLine(" CreateInstance(noagg) HR=0x"+hr.ToString("X8")+(hr==0?" p="+obj.ToString("X"):""));
    if(hr==0) return obj;
    // try aggregation with a dummy outer? skip
    return IntPtr.Zero;
  }

  static void DumpIface(IntPtr p, string tag){
    if(p==IntPtr.Zero) return;
    try{
      IntPtr vt=Marshal.ReadIntPtr(p);
      Console.WriteLine(tag+" vt="+vt.ToString("X"));
      // dump +0..+0x40
      byte[] b=new byte[0x40]; Marshal.Copy(p,b,0,0x40);
      Console.Write(tag+" dump ");
      for(int i=0;i<0x40;i++) Console.Write(b[i].ToString("x2")+(i%16==15?"\n":" "));
      Console.WriteLine();
      // QI a few
      foreach(var g in new[]{IID_IUnknown, IID_IStateData, CLSID_AppFilter, CLSID_SysFilter,
        new Guid("DC09760E-9FDA-454A-B9D2-7E663E58C39D"),
        new Guid("A3116D99-0A9B-400D-851E-84B3E387DBCC")}){
        IntPtr q; Guid gg=g; int hr=Marshal.QueryInterface(p, ref gg, out q);
        Console.WriteLine("  QI "+g+" HR=0x"+hr.ToString("X8"));
        if(hr==0 && q!=p) Marshal.Release(q);
      }
    }catch(Exception ex){Console.WriteLine(tag+" dump ex "+ex.Message);}
  }

  static void TryGetSet(IntPtr filter, string tag){
    Console.WriteLine("==== Get/Set with "+tag+" ====");
    object sync=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
    IntPtr punk=Marshal.GetIUnknownForObject(sync);
    Guid iid=IID_IStateData; IntPtr sd; int hr=Marshal.QueryInterface(punk, ref iid, out sd);
    Console.WriteLine("QI IStateData HR=0x"+hr.ToString("X8"));
    if(hr!=0) return;
    var get=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,3),typeof(Fn2));
    var set=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,5),typeof(Fn2));

    IntPtr arena=Marshal.AllocHGlobal(0x2000);
    for(int i=0;i<0x2000;i++) Marshal.WriteByte(arena,i,0);
    IntPtr coll=arena, items=IntPtr.Add(arena,0x40), data=IntPtr.Add(arena,0x200);
    Marshal.WriteIntPtr(coll,0,items);
    Marshal.WriteInt64(coll,8,1);
    Marshal.WriteInt16(items,16,1);
    Marshal.WriteInt16(items,18,(short)SID);
    Marshal.WriteInt32(items,20,4);
    Marshal.WriteIntPtr(items,24,data);

    Ace("before-get");
    try{ hr=get(sd,coll,filter); Console.WriteLine("Get HR=0x"+hr.ToString("X8")); }
    catch(Exception ex){ Console.WriteLine("Get EX "+ex.GetType().Name+" "+ex.Message); return; }

    long cnt=Marshal.ReadInt64(coll,8);
    IntPtr items2=Marshal.ReadIntPtr(coll,0);
    Console.WriteLine("count="+cnt+" items="+items2.ToString("X"));
    if(items2!=IntPtr.Zero && cnt>=1){
      byte[] gb=new byte[16]; Marshal.Copy(items2,gb,0,16);
      Console.WriteLine("handle "+new Guid(gb));
      IntPtr dp=Marshal.ReadIntPtr(items2,24);
      if(dp!=IntPtr.Zero){ byte[] db=new byte[16]; try{Marshal.Copy(dp,db,0,16); Console.Write("data "); foreach(var x in db) Console.Write(x.ToString("x2")+" "); Console.WriteLine();}catch{}}
    }

    // Only Set if Get succeeded cleanly
    if(hr!=0){ Console.WriteLine("skip set"); return; }

    IntPtr setColl=IntPtr.Add(arena,0x800);
    IntPtr setItems=IntPtr.Add(arena,0x840);
    IntPtr dataMux=IntPtr.Add(arena,0x900);
    IntPtr dataAuto=IntPtr.Add(arena,0x940);
    Marshal.WriteInt32(dataMux,0,3); Marshal.WriteInt32(dataMux,4,4); Marshal.WriteInt32(dataMux,8,2); // dgpu
    Marshal.WriteInt32(dataAuto,0,5); Marshal.WriteInt32(dataAuto,4,1); Marshal.WriteInt32(dataAuto,8,0);
    byte[] hb=HANDLE_DDS.ToByteArray();
    // if get returned handle use it
    Guid handle=HANDLE_DDS;
    if(items2!=IntPtr.Zero && cnt>=1){ byte[] gb=new byte[16]; Marshal.Copy(items2,gb,0,16); Guid gh=new Guid(gb); if(gh!=Guid.Empty) handle=gh; hb=handle.ToByteArray(); }
    Marshal.Copy(hb,0,setItems,16);
    Marshal.WriteInt16(setItems,16,1); Marshal.WriteInt16(setItems,18,(short)SID); Marshal.WriteInt32(setItems,20,4); Marshal.WriteIntPtr(setItems,24,dataMux);
    IntPtr d1=IntPtr.Add(setItems,0x20);
    Marshal.Copy(hb,0,d1,16);
    Marshal.WriteInt16(d1,16,3); Marshal.WriteInt16(d1,18,(short)SID); Marshal.WriteInt32(d1,20,4); Marshal.WriteIntPtr(d1,24,dataAuto);
    Marshal.WriteIntPtr(setColl,0,setItems); Marshal.WriteInt64(setColl,8,2);

    Ace("before-set");
    try{ hr=set(sd,setColl,filter); Console.WriteLine("Set HR=0x"+hr.ToString("X8")); }
    catch(Exception ex){ Console.WriteLine("Set EX "+ex.GetType().Name+" "+ex.Message); return; }
    System.Threading.Thread.Sleep(1500);
    Ace("after-set");
  }

  static void Main(){
    CoInitializeEx(IntPtr.Zero,2);
    foreach(var p in System.Diagnostics.Process.GetProcessesByName("NVIDIA App")) try{p.Kill();}catch{}
    System.Threading.Thread.Sleep(600);

    string appDll=@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll";
    string sysDll=@"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll";

    // Preload both
    LoadLibrary(appDll); LoadLibrary(sysDll);

    uint[] ctxs={0x1,0x2,0x3,0x4,0x5,0x15,0x17,0x401,0x402,0x403,0x407,0x415,0x417,0x10001,0x10003};
    IntPtr appFilt=IntPtr.Zero, sysFilt=IntPtr.Zero;
    foreach(var c in ctxs){
      if(appFilt==IntPtr.Zero) appFilt=TryCoCreate(CLSID_AppFilter,c,"app");
      if(sysFilt==IntPtr.Zero) sysFilt=TryCoCreate(CLSID_SysFilter,c,"sys");
    }

    if(appFilt==IntPtr.Zero) appFilt=TryDllFactory(appDll, CLSID_AppFilter, "appdll");
    if(sysFilt==IntPtr.Zero) sysFilt=TryDllFactory(sysDll, CLSID_SysFilter, "sysdll");

    DumpIface(appFilt,"appFilt");
    DumpIface(sysFilt,"sysFilt");

    // null filter Get (safer first)
    TryGetSet(IntPtr.Zero, "NULL");

    if(sysFilt!=IntPtr.Zero) TryGetSet(sysFilt, "SYS");
    else if(appFilt!=IntPtr.Zero) TryGetSet(appFilt, "APP");
  }
}
