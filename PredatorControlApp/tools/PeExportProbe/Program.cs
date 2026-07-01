using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
class P {
  [DllImport("kernel32", SetLastError=true, CharSet=CharSet.Ansi)] static extern IntPtr LoadLibraryA(string n);
  [DllImport("kernel32", CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr m, string n);
  [DllImport("dbghelp.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool SymInitialize(IntPtr h, string? p, bool i);
  static void Main(string[] a) {
    foreach (var path in a) {
      if (!File.Exists(path)) { Console.WriteLine("MISSING "+path); continue; }
      if (!NativeLibrary.TryLoad(path, out var h)) { Console.WriteLine("LOAD FAIL "+path); continue; }
      Console.WriteLine("=== "+path+" ===");
      var names = new List<string>();
      foreach (var n in new[]{"NvCplApiInit","NvCplDaemon","NvCplStartupRunOnce","NvCplApiMuxdInitialize","NvCplApiExecute","NvCplApiIsClientInstalled","NvCplApiConnect","NvCplApiOpen","EasyAPIInit","EnsureBackend","GetGlobalPPEState","SetGlobalPPEState","messageBusNew","messageBusPostMessage","getMessageBusInterface"})
        if (NativeLibrary.TryGetExport(h, n, out _)) names.Add(n);
      Console.WriteLine(string.Join(", ", names));
    }
  }
}
