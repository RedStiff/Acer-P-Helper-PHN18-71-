
using System;
using System.Runtime.InteropServices;
using System.Threading;
class H {
  static readonly Guid CLSID=new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid IID_IUnknown=new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_IStateData=new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
  const ushort SID=0x7d;
  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p,uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c,IntPtr o,uint ctx,ref Guid i,out IntPtr p);
  [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Fn2(IntPtr s,IntPtr a,IntPtr b);
  static IntPtr Vt(IntPtr o,int i){return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o),i*IntPtr.Size);}
  static void Main(){
    CoInitializeEx(IntPtr.Zero,2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    object sync=Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
    IntPtr syncUnk=Marshal.GetIUnknownForObject(sync);
    Guid c=CLSID,iu=IID_IUnknown; IntPtr filter;
    int hr=CoCreateInstance(ref c,syncUnk,0x402,ref iu,out filter);
    Console.WriteLine("FILTER="+filter.ToString("X")+" HR=0x"+hr.ToString("X8"));
    Guid isd=IID_IStateData; IntPtr sd;
    hr=Marshal.QueryInterface(syncUnk,ref isd,out sd);
    Console.WriteLine("SD="+sd.ToString("X")+" HR=0x"+hr.ToString("X8"));
    Console.WriteLine("SYNC="+syncUnk.ToString("X"));
    Console.WriteLine("PID="+System.Diagnostics.Process.GetCurrentProcess().Id);
    Console.Out.Flush();
    Thread.Sleep(4000); // frida attach
    IntPtr arena=Marshal.AllocHGlobal(0x1000);
    for(int i=0;i<0x1000;i++) Marshal.WriteByte(arena,i,0);
    IntPtr coll=arena, items=IntPtr.Add(arena,0x40), data=IntPtr.Add(arena,0x200);
    Marshal.WriteIntPtr(coll,0,items); Marshal.WriteInt64(coll,8,1);
    Marshal.WriteInt16(items,16,1); Marshal.WriteInt16(items,18,(short)SID);
    Marshal.WriteInt32(items,20,4); Marshal.WriteIntPtr(items,24,data);
    Marshal.WriteInt32(data,0,3); Marshal.WriteInt32(data,4,4); Marshal.WriteInt32(data,8,0);
    var get=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,3),typeof(Fn2));
    Console.WriteLine("GET...");
    Console.Out.Flush();
    try{ hr=get(sd,coll,filter); Console.WriteLine("Get HR=0x"+hr.ToString("X8")); }
    catch(Exception ex){ Console.WriteLine("Get EX "+ex.Message); }
    Console.Out.Flush();
    Thread.Sleep(5000);
  }
}
