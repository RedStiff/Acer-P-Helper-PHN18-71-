using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
class P {
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Fn0();
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnHwnd(IntPtr h);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnRef(ref int v);
  [STAThread]
  static void Main() {
    ApplicationConfiguration.Initialize();
    var nvcplui = @"C:\Program Files\NVIDIA Corporation\Control Panel Client\nvcplui.exe";
    if (!Process.GetProcessesByName("nvcplui").Any())
      Process.Start(new ProcessStartInfo(nvcplui,"/s"){UseShellExecute=false,CreateNoWindow=true});
    Thread.Sleep(2000);
    using var f = new Form{Opacity=0,Width=1,Height=1,ShowInTaskbar=false,FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Location=new System.Drawing.Point(-32000,-32000)};
    f.Shown += (_,__) => {
      var h = NativeLibrary.Load(@"C:\WINDOWS\system32\nvcpl.dll");
      Marshal.GetDelegateForFunctionPointer<Fn0>(NativeLibrary.GetExport(h,"NvCplDaemon"))();
      int inst=0; Marshal.GetDelegateForFunctionPointer<FnRef>(NativeLibrary.GetExport(h,"NvCplApiIsClientInstalled"))(ref inst);
      Console.WriteLine($"client_installed={inst}");
      var init=Marshal.GetDelegateForFunctionPointer<FnHwnd>(NativeLibrary.GetExport(h,"NvCplApiInit"));
      Console.WriteLine($"Init(hwnd)={init(f.Handle)}");
      for(int i=0;i<20;i++){Application.DoEvents(); Thread.Sleep(50);}
      Console.WriteLine($"MuxdInit={Marshal.GetDelegateForFunctionPointer<Fn0>(NativeLibrary.GetExport(h,"NvCplApiMuxdInitialize"))()}");
      int mode=-1; Console.WriteLine($"Hybrid={Marshal.GetDelegateForFunctionPointer<FnRef>(NativeLibrary.GetExport(h,"NvCplApiGetHybridMode"))(ref mode)} mode={mode}");
      f.Close();
    };
    Application.Run(f);
  }
}
