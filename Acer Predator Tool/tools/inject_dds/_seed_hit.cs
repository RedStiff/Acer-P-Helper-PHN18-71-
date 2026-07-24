using System;using System.Runtime.InteropServices;using Microsoft.Win32;
class P{
  static Guid AS=new Guid("6E435E38-4A67-45C1-9D49-B83A8EDECC8E"),AF=new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static Guid IU=new Guid("00000000-0000-0000-C000-000000000046"),OLD=new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");
  static Guid H=new Guid("747D8BF5-AB15-448B-91C5-52EFEC7C5850");
  [DllImport("ole32")]static extern int CoInitializeEx(IntPtr p,uint f);
  [DllImport("ole32")]static extern int CoCreateInstance(ref Guid c,IntPtr o,uint ctx,ref Guid i,out IntPtr p);
  [DllImport("kernel32",CharSet=CharSet.Unicode)]static extern IntPtr LoadLibrary(string p);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Fn1(IntPtr s,IntPtr a);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Fn2(IntPtr s,IntPtr a,IntPtr b);
  static IntPtr Vt(IntPtr o,int i){return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o),i*IntPtr.Size);}
  static IntPtr Z(int n){var p=Marshal.AllocCoTaskMem(n);for(int i=0;i<n;i++)Marshal.WriteByte(p,i,0);return p;}
  static void Ace(string t){using(var k=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE")) Console.WriteLine(t+" state="+k.GetValue("InternalMuxState")+" auto="+k.GetValue("InternalMuxIsAutomaticMode")+" i2d="+k.GetValue("ACESwitchedI2D"));}
  static void Main(string[] a){
    int mux=a.Length>0&&a[0]=="dgpu"?2:1; int aut=a.Length>0&&a[0]=="auto"?1:0;
    CoInitializeEx(IntPtr.Zero,2); LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    Guid c=AS,iu=IU; IntPtr sync; int hr=CoCreateInstance(ref c,IntPtr.Zero,4,ref iu,out sync); Console.WriteLine("sync "+hr.ToString("X"));
    Guid old=OLD; IntPtr sd; Marshal.QueryInterface(sync,ref old,out sd);
    var ghi=(Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd,4),typeof(Fn2));
    IntPtr hm=Z(16),info=Z(0x40); Marshal.Copy(H.ToByteArray(),0,hm,16); hr=ghi(sd,hm,info);
    Console.WriteLine("GHI hr=0x"+((uint)hr).ToString("X")+" sid=0x"+Marshal.ReadInt32(info,0).ToString("X"));
    Guid fc=AF; IntPtr filter; hr=CoCreateInstance(ref fc,sync,0x402,ref iu,out filter); Console.WriteLine("filter "+hr.ToString("X"));
    IntPtr iface; Marshal.QueryInterface(filter,ref old,out iface);
    var set1=(Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface,5),typeof(Fn1));
    IntPtr coll=Z(0x20),items=Z(0x50),dMux=Z(0x20),dAuto=Z(0x20);
    Marshal.WriteInt32(dMux,0,3);Marshal.WriteInt32(dMux,4,4);Marshal.WriteInt32(dMux,8,mux);
    Marshal.WriteInt32(dAuto,0,5);Marshal.WriteInt32(dAuto,4,1);Marshal.WriteInt32(dAuto,8,aut);
    byte[] hb=H.ToByteArray(); Marshal.Copy(hb,0,items,16); Marshal.WriteInt16(items,16,1);Marshal.WriteInt16(items,18,0x7d);Marshal.WriteInt32(items,20,4);Marshal.WriteIntPtr(items,24,dMux);
    IntPtr d1=IntPtr.Add(items,0x20); Marshal.Copy(hb,0,d1,16);Marshal.WriteInt16(d1,16,3);Marshal.WriteInt16(d1,18,0x7d);Marshal.WriteInt32(d1,20,4);Marshal.WriteIntPtr(d1,24,dAuto);
    Marshal.WriteIntPtr(coll,0,items);Marshal.WriteInt64(coll,8,2);
    Ace("before"); hr=set1(iface,coll); Console.WriteLine("Set mux="+mux+" auto="+aut+" hr=0x"+((uint)hr).ToString("X")); System.Threading.Thread.Sleep(2500); Ace("after");
  }
}
