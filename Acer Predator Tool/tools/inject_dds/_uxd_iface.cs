using System;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

class P
{
    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr a, uint f);

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)]
    static extern int LoadTypeLibEx(string path, uint regkind, out ComTypes.ITypeLib tlb);

    static void DumpTlb(string path)
    {
        Console.WriteLine("\n==== " + path);
        ComTypes.ITypeLib tlb;
        int hr = LoadTypeLibEx(path, 2, out tlb);
        Console.WriteLine("HR=0x" + hr.ToString("X8"));
        if (hr != 0 || tlb == null) return;
        int n = tlb.GetTypeInfoCount();
        for (int i = 0; i < n; i++)
        {
            ComTypes.ITypeInfo ti;
            tlb.GetTypeInfo(i, out ti);
            string name, doc, help;
            int ctx;
            ti.GetDocumentation(-1, out name, out doc, out ctx, out help);
            IntPtr pta;
            ti.GetTypeAttr(out pta);
            var ta = (ComTypes.TYPEATTR)Marshal.PtrToStructure(pta, typeof(ComTypes.TYPEATTR));
            Console.WriteLine("[" + i + "] " + ta.typekind + " " + name + " guid=" + ta.guid + " funcs=" + ta.cFuncs + " impl=" + ta.cImplTypes);
            if (ta.typekind == ComTypes.TYPEKIND.TKIND_COCLASS)
            {
                for (int t = 0; t < ta.cImplTypes; t++)
                {
                    int href;
                    ti.GetRefTypeOfImplType(t, out href);
                    ComTypes.ITypeInfo ti2;
                    ti.GetRefTypeInfo(href, out ti2);
                    string n2, d2, h2;
                    int c2;
                    ti2.GetDocumentation(-1, out n2, out d2, out c2, out h2);
                    IntPtr p2;
                    ti2.GetTypeAttr(out p2);
                    var a2 = (ComTypes.TYPEATTR)Marshal.PtrToStructure(p2, typeof(ComTypes.TYPEATTR));
                    Console.WriteLine("   impl " + n2 + " " + a2.guid + " funcs=" + a2.cFuncs);
                    DumpFuncs(ti2, a2);
                    ti2.ReleaseTypeAttr(p2);
                }
            }
            if (ta.typekind == ComTypes.TYPEKIND.TKIND_INTERFACE || ta.typekind == ComTypes.TYPEKIND.TKIND_DISPATCH)
                DumpFuncs(ti, ta);
            ti.ReleaseTypeAttr(pta);
        }
    }

    static void DumpFuncs(ComTypes.ITypeInfo ti, ComTypes.TYPEATTR ta)
    {
        int lim = Math.Min((int)ta.cFuncs, 60);
        for (int f = 0; f < lim; f++)
        {
            IntPtr pfd;
            ti.GetFuncDesc(f, out pfd);
            var fd = (ComTypes.FUNCDESC)Marshal.PtrToStructure(pfd, typeof(ComTypes.FUNCDESC));
            string fn, fdc, fh;
            int fc;
            ti.GetDocumentation(fd.memid, out fn, out fdc, out fc, out fh);
            Console.WriteLine("      " + fn + " memid=" + fd.memid + " params=" + fd.cParams + " vft=" + fd.oVft);
            ti.ReleaseFuncDesc(pfd);
        }
    }

    static void TryCreate(string prog)
    {
        Console.WriteLine("\n==== Create " + prog);
        try
        {
            Type t = Type.GetTypeFromProgID(prog, true);
            object o = Activator.CreateInstance(t);
            Console.WriteLine("OK " + o);
            IntPtr punk = Marshal.GetIUnknownForObject(o);
            // probe common IIDs from typelib dumps / known NVIDIA GUIDs
            string[] iids =
            {
                "00000000-0000-0000-C000-000000000046", // IUnknown
                "00020400-0000-0000-C000-000000000046", // IDispatch
                "4473E3A7-C2AD-4075-A1F8-935A584740A9", // connection point guess
            };
            foreach (var s in iids)
            {
                Guid g = new Guid(s);
                IntPtr p;
                int hr = Marshal.QueryInterface(punk, ref g, out p);
                Console.WriteLine("QI " + s + " HR=0x" + hr.ToString("X8") + " p=" + p);
                if (hr == 0) Marshal.Release(p);
            }
            Marshal.Release(punk);
            Marshal.FinalReleaseComObject(o);
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL " + ex.Message);
        }
    }

    static void Main()
    {
        CoInitializeEx(IntPtr.Zero, 2);
        DumpTlb(@"C:\Windows\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll");
        DumpTlb(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
        DumpTlb(@"C:\Windows\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\Display.NvContainer\plugins\Session\nvxdsyncplugin.dll");
        DumpTlb(@"C:\Windows\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\Display.NvContainer\plugins\LocalSystem\NvXDCore.dll");
        TryCreate("NvXDCore.SyncProxy");
        TryCreate("NVXDBat.NvXDBatchEngine");
        TryCreate("NvXDCore.PipelineRegistrar");
    }
}
