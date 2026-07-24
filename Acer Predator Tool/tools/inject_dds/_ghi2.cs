using System; using System.Runtime.InteropServices;
class T {
  static readonly Guid CLSID_AppSync=new Guid("6E435E38-4A67-45C1-9D49-B83A8EDECC8E");
  static readonly Guid CLSID_AppFilter=new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid CLSID_Plcy=new Guid("11829530-E41B-40E3-B3E1-24EFF3D39144");
  static readonly Guid IID_IUnknown=new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_OLD=new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");
  static readonly Guid ROOT=new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");
  static readonly Guid DDS=new Guid("747D8BF5-AB15-448B-91C5-52EFEC7C5850");
  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p,uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c,IntPtr o,uint ctx,ref Guid i,out IntPtr p);
  [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn1(IntPtr s,IntPtr a);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn2(IntPtr s,IntPtr a,IntPtr b);
  static IntPtr Vt(IntPtr o,int i){return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o),i*IntPtr.Size);}
  static IntPtr Z(int n){var p=Marshal.AllocCoTaskMem(n);for(int i=0;i<n;i++)Marshal.WriteByte(p,i,0);return p;}
  static string H(int hr){return "0x"+((uint)hr).ToString("X8");}
  static void Dump(IntPtr p,int n,string t){byte[] b=new byte[n];Marshal.Copy(p,b,0,n);Console.Write(t+" ");foreach(var x in b)Console.Write(x.ToString("x2")+" ");Console.WriteLine();}
  static void Main(){
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdplcy.dll");
    Guid c=CLSID_AppSync,iu=IID_IUnknown; IntPtr sync; CoCreateInstance(ref c,IntPtr.Zero,4,ref iu,out sync);
    Guid old=IID_OLD; IntPtr sd; Marshal.QueryInterface(sync,ref old,out sd);
    Guid fc=CLSID_AppFilter; IntPtr filter; CoCreateInstance(ref fc,sync,0x402,ref iu,out filter);
    IntPtr iface; old=IID_OLD; Marshal.QueryInterface(filter,ref old,out iface);
    var ghi=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,4),typeof(Fn2));
    var get1=(Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface,3),typeof(Fn1));
    var get=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,3),typeof(Fn2));

    // GHI DDS
    IntPtr hm=Z(16), info=Z(0x40); Marshal.Copy(DDS.ToByteArray(),0,hm,16);
    Console.WriteLine("GHI DDS "+H(ghi(sd,hm,info))); Dump(info,0x28,"dds");

    // Get root info=7 preinit type1 size16
    foreach(var useWrap in new[]{true,false}){
      IntPtr coll=Z(0x20),items=Z(0x40),data=Z(0x40);
      Marshal.Copy(ROOT.ToByteArray(),0,items,16);
      Marshal.WriteInt16(items,16,7); Marshal.WriteInt16(items,18,2);
      Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,data);
      Marshal.WriteInt32(data,0,1); Marshal.WriteInt32(data,4,16);
      Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
      int hr=useWrap?get1(iface,coll):get(sd,coll,filter);
      Console.WriteLine((useWrap?"wrap":"classic")+" Get root info7 "+H(hr)+" type="+Marshal.ReadInt32(data,0)+" size="+Marshal.ReadInt32(data,4));
      Dump(data,0x28,"data");
    }

    // Policy engine
    Guid pc=CLSID_Plcy; IntPtr plcy;
    int hr2=CoCreateInstance(ref pc,IntPtr.Zero,1,ref iu,out plcy);
    Console.WriteLine("Plcy IUnknown "+H(hr2)+" "+plcy.ToString("X"));
    if(hr2==0){
      old=IID_OLD; IntPtr psd; hr2=Marshal.QueryInterface(plcy,ref old,out psd);
      Console.WriteLine("Plcy OLD "+H(hr2)+" "+psd.ToString("X"));
      // try common IIDs
      foreach(var g in new[]{
        "463FE815-7BC0-4463-9CE4-D8C8BD6EA257",
        "E6AB4158-38B8-4FDF-85CF-ADC2E9870970",
        "693C25B2-E6AF-4783-8F8F-3BB222071D58",
        "A3116D99-0A9B-400D-851E-84B3E387DBCC"
      }){
        Guid ig=new Guid(g); IntPtr p; int hr=Marshal.QueryInterface(plcy,ref ig,out p);
        Console.WriteLine("Plcy QI "+g+" "+H(hr)+(p==IntPtr.Zero?"":" @"+p.ToString("X")));
        if(p!=IntPtr.Zero) Marshal.Release(p);
      }
    }
  }
}
