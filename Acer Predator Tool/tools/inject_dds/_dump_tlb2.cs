using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

class DumpTlb
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)]
    static extern int LoadTypeLibEx(string path, int regkind, out ITypeLib tlb);

    static void Main()
    {
        string[] files = {
            @"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll",
            @"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll",
        };
        foreach (var f in files)
        {
            Console.WriteLine("==== " + f);
            ITypeLib tlb;
            int hr = LoadTypeLibEx(f, 2, out tlb);
            Console.WriteLine("HR=0x" + hr.ToString("X8"));
            if (hr != 0 || tlb == null) continue;
            int n = tlb.GetTypeInfoCount();
            Console.WriteLine("count=" + n);
            for (int i = 0; i < n; i++)
            {
                string name, doc; int ctx;
                tlb.GetDocumentation(i, out name, out doc, out ctx, out _);
                ITypeInfo ti; tlb.GetTypeInfo(i, out ti);
                IntPtr pAttr; ti.GetTypeAttr(out pAttr);
                TYPEATTR attr = (TYPEATTR)Marshal.PtrToStructure(pAttr, typeof(TYPEATTR));
                Console.WriteLine("[" + i + "] " + attr.typekind + " " + name + " guid=" + attr.guid + " funcs=" + attr.cFuncs + " impltypes=" + attr.cImplTypes);
                if (name != null && name.IndexOf("Filter", StringComparison.OrdinalIgnoreCase) >= 0
                    || name != null && name.IndexOf("State", StringComparison.OrdinalIgnoreCase) >= 0
                    || attr.cFuncs > 0 && attr.cFuncs < 30)
                {
                    for (int fi = 0; fi < attr.cFuncs; fi++)
                    {
                        IntPtr pfd; ti.GetFuncDesc(fi, out pfd);
                        FUNCDESC fd = (FUNCDESC)Marshal.PtrToStructure(pfd, typeof(FUNCDESC));
                        string[] names = new string[1]; int c;
                        ti.GetNames(fd.memid, names, 1, out c);
                        Console.WriteLine("  func[" + fi + "] " + names[0] + " params=" + fd.cParams + " oVft=" + fd.oVft);
                        ti.ReleaseFuncDesc(pfd);
                    }
                    for (int ii = 0; ii < attr.cImplTypes; ii++)
                    {
                        int href; ti.GetRefTypeOfImplType(ii, out href);
                        ITypeInfo ti2; ti.GetRefTypeInfo(href, out ti2);
                        string n2, d2; int c2;
                        ti2.GetDocumentation(-1, out n2, out d2, out c2, out _);
                        IntPtr pa2; ti2.GetTypeAttr(out pa2);
                        TYPEATTR a2 = (TYPEATTR)Marshal.PtrToStructure(pa2, typeof(TYPEATTR));
                        Console.WriteLine("  impl " + n2 + " " + a2.guid + " funcs=" + a2.cFuncs);
                        for (int fi = 0; fi < a2.cFuncs && fi < 20; fi++)
                        {
                            IntPtr pfd; ti2.GetFuncDesc(fi, out pfd);
                            FUNCDESC fd = (FUNCDESC)Marshal.PtrToStructure(pfd, typeof(FUNCDESC));
                            string[] names = new string[1]; int c;
                            ti2.GetNames(fd.memid, names, 1, out c);
                            Console.WriteLine("    " + names[0] + " params=" + fd.cParams + " oVft=" + fd.oVft);
                            ti2.ReleaseFuncDesc(pfd);
                        }
                        ti2.ReleaseTypeAttr(pa2);
                    }
                }
                ti.ReleaseTypeAttr(pAttr);
            }
        }
    }
}
