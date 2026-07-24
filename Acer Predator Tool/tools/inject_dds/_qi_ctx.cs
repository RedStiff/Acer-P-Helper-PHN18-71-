using System;
using System.Runtime.InteropServices;

class Q {
  [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr a,uint f);
  static void Main() {
    CoInitializeEx(IntPtr.Zero,2);
    foreach (var p in System.Diagnostics.Process.GetProcessesByName("NVIDIA App"))
      try { p.Kill(); } catch {}
    System.Threading.Thread.Sleep(500);
    object o = Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy", true));
    IntPtr punk = Marshal.GetIUnknownForObject(o);
    string[] iids = {
      "3F6374C2-3540-476A-A123-D1DA2B6DDF86",
      "E6AB4158-38B8-4FDF-85CF-ADC2E9870970",
      "DC09760E-9FDA-454A-B9D2-7E663E58C39D",
      "00000000-0000-0000-C000-000000000046",
      "B196B286-BAB4-101A-B69C-00AA00341D07", // IConnectionPointContainer
      "DD9EA505-2D7D-42FE-9859-E5FE20254EBF",
      "49C042AF-E41B-4E12-9877-BF7CF1A25733",
      "31B10098-8E5D-4322-AF4A-EFA91025279A",
    };
    foreach (var s in iids) {
      Guid g = new Guid(s);
      IntPtr p; int hr = Marshal.QueryInterface(punk, ref g, out p);
      Console.WriteLine("QI " + s + " HR=0x" + hr.ToString("X8") + (hr==0 ? " OK "+p.ToString("X") : ""));
      if (hr==0) Marshal.Release(p);
    }
    // Also dump SyncProxy object bytes
    Console.WriteLine("punk=" + punk.ToString("X"));
    IntPtr vt = Marshal.ReadIntPtr(punk);
    Console.WriteLine("vt=" + vt.ToString("X"));
    for (int i=0;i<16;i++)
      Console.WriteLine("  +" + (i*8).ToString("x") + " " + Marshal.ReadIntPtr(punk, i*IntPtr.Size).ToString("X"));
  }
}
