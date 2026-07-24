using System; using System.Runtime.InteropServices; using System.Threading;
class Hold {
  static readonly Guid CLSID=new Guid("6E435E38-4A67-45C1-9D49-B83A8EDECC8E");
  static readonly Guid IID_IU=new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_OLD=new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");
  static readonly Guid H=new Guid("747D8BF5-AB15-448B-91C5-52EFEC7C5850");
  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p,uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c,IntPtr o,uint ctx,ref Guid i,out IntPtr p);
  [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn2(IntPtr s,IntPtr a,IntPtr b);
  static IntPtr Vt(IntPtr o,int i){return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o),i*IntPtr.Size);}
  static void Main(){
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    Guid c=CLSID,iu=IID_IU; IntPtr sync; Console.WriteLine("cci "+CoCreateInstance(ref c,IntPtr.Zero,4,ref iu,out sync).ToString("X8"));
    Guid old=IID_OLD; IntPtr sd; Marshal.QueryInterface(sync,ref old,out sd);
    var ghi=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,4),typeof(Fn2));
    IntPtr hm=Marshal.AllocCoTaskMem(16), info=Marshal.AllocCoTaskMem(0x40);
    for(int i=0;i<0x40;i++)Marshal.WriteByte(info,i,0);
    Marshal.Copy(H.ToByteArray(),0,hm,16);
    int hr=ghi(sd,hm,info);
    Console.WriteLine("GHI HR=0x"+((uint)hr).ToString("X8")+" sid=0x"+Marshal.ReadInt32(info,0).ToString("X"));
    Console.WriteLine("HOLD pid="+System.Diagnostics.Process.GetCurrentProcess().Id);
    Console.Out.Flush();
    Thread.Sleep(120000);
  }
}
