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
  static IntPtr Vt(IntPtr o,int i){return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o),i*IntPtr.Size);}
  static IntPtr Z(int n){var p=Marshal.AllocCoTaskMem(n);for(int i=0;i<n;i++)Marshal.WriteByte(p,i,0);return p;}
  static void Ace(string t){using(var k=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
    Console.WriteLine(t+" state="+k.GetValue("InternalMuxState")+" auto="+k.GetValue("InternalMuxIsAutomaticMode"));}
  static void Main(){
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    Guid c=CLSID_AppSync,iu=IID_IUnknown; IntPtr sync; CoCreateInstance(ref c,IntPtr.Zero,4,ref iu,out sync);
    Guid fc=CLSID_AppFilter; IntPtr filter; CoCreateInstance(ref fc,sync,0x402,ref iu,out filter);
    Guid old=IID_OLD; IntPtr iface; Marshal.QueryInterface(filter,ref old,out iface);
    var set=(Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface,5),typeof(Fn1));
    // Set dgpu with EMPTY handle
    IntPtr coll=Z(0x20),items=Z(0x50),dMux=Z(0x20),dAuto=Z(0x20);
    Marshal.WriteInt32(dMux,0,3);Marshal.WriteInt32(dMux,4,4);Marshal.WriteInt32(dMux,8,2);
    Marshal.WriteInt32(dAuto,0,5);Marshal.WriteInt32(dAuto,4,1);Marshal.WriteInt32(dAuto,8,0);
    Marshal.WriteInt16(items,16,1);Marshal.WriteInt16(items,18,(short)SID);Marshal.WriteInt32(items,20,4);Marshal.WriteIntPtr(items,24,dMux);
    IntPtr d1=IntPtr.Add(items,0x20);
    Marshal.WriteInt16(d1,16,3);Marshal.WriteInt16(d1,18,(short)SID);Marshal.WriteInt32(d1,20,4);Marshal.WriteIntPtr(d1,24,dAuto);
    Marshal.WriteIntPtr(coll,0,items);Marshal.WriteInt64(coll,8,2);
    Ace("before");
    int hr=set(iface,coll);
    Console.WriteLine("empty handle Set HR=0x"+((uint)hr).ToString("X8"));
    System.Threading.Thread.Sleep(2500); Ace("after");
  }
}
