using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

class T
{
    [DllImport("oleaut32.dll", CharSet=CharSet.Unicode)]
    static extern int LoadTypeLibEx(string szFile, int regkind, out ITypeLib pptlib);

    static string VarKind(VarEnum v){ return v.ToString(); }

    static void DumpType(ITypeInfo ti, int idx)
    {
        IntPtr p;
        ti.GetTypeAttr(out p);
        TYPEATTR attr = (TYPEATTR)Marshal.PtrToStructure(p, typeof(TYPEATTR));
        string[] names; string doc; int ctx;
        ti.GetDocumentation(idx < 0 ? -1 : idx, out names, out doc, out ctx, out _);
        // GetDocumentation for type uses -1
        string name, help;
        ti.GetDocumentation(-1, out name, out help, out ctx, out _);
        Console.WriteLine("TYPE " + name + " kind=" + attr.typekind + " guid=" + attr.guid + " cFuncs=" + attr.cFuncs + " cVars=" + attr.cVars);
        for (int i = 0; i < attr.cFuncs; i++)
        {
            IntPtr pf;
            ti.GetFuncDesc(i, out pf);
            FUNCDESC fd = (FUNCDESC)Marshal.PtrToStructure(pf, typeof(FUNCDESC));
            string[] ns = new string[1];
            int c;
            ti.GetNames(fd.memid, ns, 1, out c);
            Console.WriteLine("  FUNC[" + i + "] " + ns[0] + " memid=" + fd.memid + " inv=" + fd.invkind + " cParams=" + fd.cParams + " oVft=" + fd.oVft + " ccall=" + fd.callconv + " retvalvt=" + fd.elemdescFunc.tdesc.vt);
            if (fd.cParams > 0 && fd.lprgelemdescParam != IntPtr.Zero)
            {
                for (int pidx = 0; pidx < fd.cParams; pidx++)
                {
                    ELEMDESC ed = (ELEMDESC)Marshal.PtrToStructure(
                        IntPtr.Add(fd.lprgelemdescParam, pidx * Marshal.SizeOf(typeof(ELEMDESC))), typeof(ELEMDESC));
                    Console.WriteLine("    param[" + pidx + "] vt=" + ed.tdesc.vt + " wParamFlags=" + ed.desc.paramdesc.wParamFlags);
                }
            }
            ti.ReleaseFuncDesc(pf);
        }
        for (int i = 0; i < attr.cVars; i++)
        {
            IntPtr pv;
            ti.GetVarDesc(i, out pv);
            VARDESC vd = (VARDESC)Marshal.PtrToStructure(pv, typeof(VARDESC));
            string[] ns = new string[1];
            int c;
            ti.GetNames(vd.memid, ns, 1, out c);
            object val = null;
            if (vd.varkind == VARKIND.VAR_CONST)
                val = Marshal.GetObjectForNativeVariant(vd.desc.lpvarValue);
            Console.WriteLine("  VAR[" + i + "] " + ns[0] + " = " + val);
            ti.ReleaseVarDesc(pv);
        }
        ti.ReleaseTypeAttr(p);
    }

    static void DumpFile(string path)
    {
        Console.WriteLine("======== " + path);
        ITypeLib tl;
        int hr = LoadTypeLibEx(path, 2 /*REGKIND_NONE*/, out tl);
        Console.WriteLine("LoadTypeLib HR=0x" + hr.ToString("X8"));
        if (hr != 0 || tl == null) return;
        int n = tl.GetTypeInfoCount();
        Console.WriteLine("TypeInfoCount=" + n);
        for (int i = 0; i < n; i++)
        {
            string name, doc; int ctx;
            tl.GetDocumentation(i, out name, out doc, out ctx, out _);
            ITypeInfo ti;
            tl.GetTypeInfo(i, out ti);
            IntPtr p;
            ti.GetTypeAttr(out p);
            TYPEATTR attr = (TYPEATTR)Marshal.PtrToStructure(p, typeof(TYPEATTR));
            bool interesting = (name != null && (
                name.IndexOf("State", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Sync", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Descriptor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Setting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Mux", StringComparison.OrdinalIgnoreCase) >= 0));
            if (interesting || attr.typekind == TYPEKIND.TKIND_INTERFACE || attr.typekind == TYPEKIND.TKIND_DISPATCH)
            {
                Console.WriteLine("[" + i + "] " + name + " kind=" + attr.typekind + " cFuncs=" + attr.cFuncs + " guid=" + attr.guid);
                if (interesting || (attr.cFuncs > 0 && attr.cFuncs < 40 && name != null && name.StartsWith("I")))
                    DumpType(ti, i);
            }
            ti.ReleaseTypeAttr(p);
        }
    }

    static void Main()
    {
        DumpFile(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
        DumpFile(@"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll");
    }
}
