using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

class TlbDump {
  [DllImport("oleaut32", CharSet = CharSet.Unicode)]
  static extern int LoadTypeLib(string path, out ITypeLib tlb);

  static void Main() {
    string path = @"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll";
    int hr = LoadTypeLib(path, out ITypeLib tlb);
    Console.WriteLine("LoadTypeLib HR=0x" + hr.ToString("X8"));
    if (hr != 0 || tlb == null) {
      // try registered
      path = @"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll";
      return;
    }
    int n = tlb.GetTypeInfoCount();
    Console.WriteLine("types=" + n);
    for (int i = 0; i < n; i++) {
      tlb.GetTypeInfo(i, out ITypeInfo ti);
      ti.GetDocumentation(-1, out string name, out _, out _, out _);
      if (name == null) continue;
      if (!(name.Contains("State") || name.Contains("Handle") || name.Contains("Sync") ||
            name.Contains("Filter") || name.Contains("Batch") || name.Contains("Setting")))
        continue;
      ti.GetTypeAttr(out IntPtr pta);
      var ta = Marshal.PtrToStructure<TYPEATTR>(pta);
      Console.WriteLine("[" + i + "] " + name + " kind=" + ta.typekind + " funcs=" + ta.cFuncs);
      for (int f = 0; f < ta.cFuncs; f++) {
        ti.GetFuncDesc(f, out IntPtr pfd);
        var fd = Marshal.PtrToStructure<FUNCDESC>(pfd);
        ti.GetDocumentation(fd.memid, out string fn, out _, out _, out _);
        string[] names = new string[fd.cParams + 1];
        ti.GetNames(fd.memid, names, names.Length, out int nn);
        Console.WriteLine("  oVft=" + fd.oVft + " " + fn + " (" + string.Join(", ", names) + ")");
        ti.ReleaseFuncDesc(pfd);
      }
      ti.ReleaseTypeAttr(pta);
    }
  }
}
