using System;using System.Runtime.InteropServices;using System.Threading;
class P{
 static readonly Guid CLSID=new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
 static readonly Guid IID_CF=new Guid("00000001-0000-0000-C000-000000000046");
 [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p,uint f);
 [DllImport("ole32")] static extern int CoGetClassObject(ref Guid c,uint ctx,IntPtr s,ref Guid i,out IntPtr p);
 [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
 static void Main(){
  CoInitializeEx(IntPtr.Zero,2);
  LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
  try{GC.KeepAlive(Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy")));}catch{}
  Guid c=CLSID,i=IID_CF; IntPtr cf;
  int hr=CoGetClassObject(ref c,0x402,IntPtr.Zero,ref i,out cf);
  Console.WriteLine("GCO HR=0x"+hr.ToString("X8")+" cf="+cf.ToString("X"));
  IntPtr vt=Marshal.ReadIntPtr(cf);
  Console.WriteLine("vt="+vt.ToString("X"));
  for(int n=0;n<8;n++) Console.WriteLine("vt["+n+"]="+Marshal.ReadIntPtr(vt,n*IntPtr.Size).ToString("X"));
  Console.WriteLine("PID="+System.Diagnostics.Process.GetCurrentProcess().Id);
  Console.Out.Flush();
  Thread.Sleep(120000);
 }
}
