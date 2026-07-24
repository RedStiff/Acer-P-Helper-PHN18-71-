using System; using System.Runtime.InteropServices; using Microsoft.Win32;
class T {
  static readonly Guid CLSID_AppSync=new Guid("6E435E38-4A67-45C1-9D49-B83A8EDECC8E");
  static readonly Guid CLSID_AppFilter=new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid IID_IUnknown=new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_OLD=new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");
  const ushort SID=0x7d;
  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p,uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c,IntPtr o,uint ctx,ref Guid i,out IntPtr p);
  [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn1(IntPtr s,IntPtr a);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn2(IntPtr s,IntPtr a,IntPtr b);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn3(IntPtr s,IntPtr a,IntPtr b,IntPtr c);
  static IntPtr Vt(IntPtr o,int i){return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o),i*IntPtr.Size);}
  static IntPtr Z(int n){var p=Marshal.AllocCoTaskMem(n);for(int i=0;i<n;i++)Marshal.WriteByte(p,i,0);return p;}
  static void Dump(IntPtr p,int n,string t){Console.Write(t+": ");byte[] b=new byte[n];try{Marshal.Copy(p,b,0,n);}catch{Console.WriteLine("fail");return;}foreach(var x in b)Console.Write(x.ToString("x2")+" ");Console.WriteLine();}
  static void Main(){
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    Guid c=CLSID_AppSync,iu=IID_IUnknown; IntPtr sync; int hr=CoCreateInstance(ref c,IntPtr.Zero,4,ref iu,out sync);
    Guid old=IID_OLD; IntPtr sd; Marshal.QueryInterface(sync,ref old,out sd);
    Guid fc=CLSID_AppFilter; IntPtr filter; CoCreateInstance(ref fc,sync,0x402,ref iu,out filter);
    IntPtr iface; Marshal.QueryInterface(filter,ref old,out iface);
    Console.WriteLine("sd="+sd.ToString("X")+" iface="+iface.ToString("X"));
    // try vt[4] with various arity
    IntPtr buf=Z(0x100);
    try{ var f=(Fn1)Marshal.GetDelegateForFunctionPointer(Vt(sd,4),typeof(Fn1)); hr=f(sd,buf); Console.WriteLine("sd Fn1 "+((uint)hr).ToString("X8")); Dump(buf,64,"buf"); }catch(Exception ex){Console.WriteLine("Fn1 "+ex.Message);}
    try{ var f=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,4),typeof(Fn2)); hr=f(sd,buf,filter); Console.WriteLine("sd Fn2 filter "+((uint)hr).ToString("X8")); Dump(buf,64,"buf"); }catch(Exception ex){Console.WriteLine("Fn2 "+ex.Message);}
    try{ var f=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,4),typeof(Fn2)); IntPtr sid=Z(4); Marshal.WriteInt16(sid,0,(short)SID); hr=f(sd,sid,buf); Console.WriteLine("sd Fn2 sid "+((uint)hr).ToString("X8")); Dump(buf,64,"buf"); }catch(Exception ex){Console.WriteLine("Fn2sid "+ex.Message);}
    // wrapper has vt[4]?
    try{ var f=(Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface,4),typeof(Fn1)); hr=f(iface,buf); Console.WriteLine("iface Fn1 "+((uint)hr).ToString("X8")); Dump(buf,64,"buf"); }catch(Exception ex){Console.WriteLine("iface "+ex.Message);}
  }
}
