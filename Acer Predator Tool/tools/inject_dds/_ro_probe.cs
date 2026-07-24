using System; using System.Runtime.InteropServices;
class T {
  static readonly Guid CLSID_Plcy=new Guid("11829530-E41B-40E3-B3E1-24EFF3D39144");
  static readonly Guid CLSID_AppSync=new Guid("6E435E38-4A67-45C1-9D49-B83A8EDECC8E");
  static readonly Guid IID_IUnknown=new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_RO=new Guid("693C25B2-E6AF-4783-8F8F-3BB222071D58");
  static readonly Guid IID_OLD=new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");
  static readonly Guid ROOT=new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");
  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p,uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c,IntPtr o,uint ctx,ref Guid i,out IntPtr p);
  [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn0(IntPtr s);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn1(IntPtr s,IntPtr a);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn2(IntPtr s,IntPtr a,IntPtr b);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn3(IntPtr s,IntPtr a,IntPtr b,IntPtr c);
  static IntPtr Vt(IntPtr o,int i){return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o),i*IntPtr.Size);}
  static IntPtr Z(int n){var p=Marshal.AllocCoTaskMem(n);for(int i=0;i<n;i++)Marshal.WriteByte(p,i,0);return p;}
  static string H(int hr){return "0x"+((uint)hr).ToString("X8");}
  static void Dump(IntPtr p,int n,string t){if(p==IntPtr.Zero){Console.WriteLine(t+" null");return;}byte[] b=new byte[n];try{Marshal.Copy(p,b,0,n);}catch{Console.WriteLine(t+" fail");return;}Console.Write(t+" ");foreach(var x in b)Console.Write(x.ToString("x2")+" ");Console.WriteLine();}
  static void Main(){
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdplcy.dll");
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    Guid pc=CLSID_Plcy,iu=IID_IUnknown; IntPtr plcy; Console.WriteLine("plcy "+H(CoCreateInstance(ref pc,IntPtr.Zero,1,ref iu,out plcy)));
    Guid iro=IID_RO; IntPtr ro; Console.WriteLine("RO "+H(Marshal.QueryInterface(plcy,ref iro,out ro))+" "+ro.ToString("X"));
    Console.WriteLine("RO vt0="+Marshal.ReadIntPtr(ro).ToString("X"));
    for(int i=0;i<12;i++) Console.WriteLine(" vt["+i+"]="+Vt(ro,i).ToString("X"));

    // NumMethods unknown — brute slots 3..10 with various arg shapes
    IntPtr buf=Z(0x200), outp=Z(8), root=Z(16);
    Marshal.Copy(ROOT.ToByteArray(),0,root,16);
    for(int slot=3; slot<=10; slot++){
      // Fn0
      try{ var f=(Fn0)Marshal.GetDelegateForFunctionPointer(Vt(ro,slot),typeof(Fn0)); int hr=f(ro); if(hr!=unchecked((int)0x80004001)&&hr!=unchecked((int)0x8000FFFF)) Console.WriteLine("slot"+slot+" Fn0 "+H(hr)); }catch{}
      // Fn1 buf
      try{ for(int i=0;i<0x200;i++)Marshal.WriteByte(buf,i,0); var f=(Fn1)Marshal.GetDelegateForFunctionPointer(Vt(ro,slot),typeof(Fn1)); int hr=f(ro,buf); if(hr==0||(uint)hr==0xEAB00003) {Console.WriteLine("slot"+slot+" Fn1(buf) "+H(hr)); Dump(buf,0x40,"b");} }catch(Exception ex){ if(!ex.Message.Contains(" balanc")) Console.WriteLine("slot"+slot+" Fn1 EX "+ex.GetType().Name); }
      // Fn1 root
      try{ var f=(Fn1)Marshal.GetDelegateForFunctionPointer(Vt(ro,slot),typeof(Fn1)); int hr=f(ro,root); if(hr==0||(uint)hr==0xEAB00003) Console.WriteLine("slot"+slot+" Fn1(root) "+H(hr)); }catch{}
      // Fn2 root,buf
      try{ for(int i=0;i<0x200;i++)Marshal.WriteByte(buf,i,0); var f=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(ro,slot),typeof(Fn2)); int hr=f(ro,root,buf); if(hr==0||(uint)hr==0xEAB00003){Console.WriteLine("slot"+slot+" Fn2(root,buf) "+H(hr)); Dump(buf,0x40,"b");} }catch{}
      // Fn2 sid,buf
      try{ IntPtr sid=Z(4); Marshal.WriteInt16(sid,0,0x7d); for(int i=0;i<0x200;i++)Marshal.WriteByte(buf,i,0); var f=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(ro,slot),typeof(Fn2)); int hr=f(ro,sid,buf); if(hr==0||(uint)hr==0xEAB00003){Console.WriteLine("slot"+slot+" Fn2(sid7d,buf) "+H(hr)); Dump(buf,0x40,"b");} }catch{}
    }

    // Also RO on AppSync
    Guid sc=CLSID_AppSync; IntPtr sync; CoCreateInstance(ref sc,IntPtr.Zero,4,ref iu,out sync);
    iro=IID_RO; IntPtr sro; int hr3=Marshal.QueryInterface(sync,ref iro,out sro);
    Console.WriteLine("AppSync RO "+H(hr3));
  }
}
