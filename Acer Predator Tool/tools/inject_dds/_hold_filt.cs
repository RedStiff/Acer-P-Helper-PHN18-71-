using System;using System.Runtime.InteropServices;using System.Threading;
class H{
 static readonly Guid CLSID=new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
 static readonly Guid IID=new Guid("00000000-0000-0000-C000-000000000046");
 [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p,uint f);
 [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c,IntPtr o,uint ctx,ref Guid i,out IntPtr p);
 [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
 static void Main(){
  CoInitializeEx(IntPtr.Zero,2);
  LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
  object sync=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
  IntPtr su=Marshal.GetIUnknownForObject(sync);
  Guid c=CLSID,i=IID; IntPtr f;
  CoCreateInstance(ref c,su,0x402,ref i,out f);
  Console.WriteLine("FILTER="+f.ToString("X"));
  Console.WriteLine("IFACE="+IntPtr.Add(f,0x18).ToString("X"));
  Console.WriteLine("SYNC="+su.ToString("X"));
  Console.WriteLine("PID="+System.Diagnostics.Process.GetCurrentProcess().Id);
  Console.Out.Flush();
  Thread.Sleep(120000);
 }
}
