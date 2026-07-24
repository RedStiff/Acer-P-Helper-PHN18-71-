using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class Q
{
    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr a, uint f);

    static void DumpIface(string iid)
    {
        try
        {
            RegistryKey k = Registry.ClassesRoot.OpenSubKey(@"Interface\" + iid);
            if (k == null) { Console.WriteLine("  (no reg)"); return; }
            Console.WriteLine("  reg name=" + k.GetValue(null));
            RegistryKey t = k.OpenSubKey("TypeLib");
            if (t != null)
            {
                Console.WriteLine("  typelib=" + t.GetValue(null) + " ver=" + t.GetValue("Version"));
                t.Close();
            }
            Console.WriteLine("  NumMethods=" + k.GetValue("NumMethods"));
            foreach (string sk in k.GetSubKeyNames())
            {
                RegistryKey s = k.OpenSubKey(sk);
                Console.WriteLine("  sub " + sk + " = " + s.GetValue(null));
                s.Close();
            }
            k.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine("  reg err " + ex.Message);
        }
    }

    static void Main()
    {
        CoInitializeEx(IntPtr.Zero, 2);
        Type t = Type.GetTypeFromProgID("NvXDCore.SyncProxy", true);
        object o = Activator.CreateInstance(t);
        IntPtr punk = Marshal.GetIUnknownForObject(o);
        string[] iids =
        {
            "627D7951-9643-4DE6-898F-6C6B766AAB39",
            "E6AB4158-38B8-4FDF-85CF-ADC2E9870970",
            "693C25B2-E6AF-4783-8F8F-3BB222071D58",
            "A3116D99-0A9B-400D-851E-84B3E387DBCC",
            "4473E3A7-C2AD-4075-A1F8-935A584740A9",
            "8DEEF6BE-6810-4817-956C-C54AE0B0FFAC",
            "463FE815-7BC0-4463-9CE4-D8C8BD6EA257",
            "DC09760E-9FDA-454A-B9D2-7E663E58C39D",
            "0D0497A9-19AD-49B9-A68E-4B2684AAF26C",
            "E1A87F97-EC2C-4F55-867A-A2781571DE8E",
            "DD9DEA72-E17E-4056-AC79-F889A1647CE4",
            "E13B9A7F-EC39-4E7F-9970-12F348C89FD0",
        };
        for (int n = 0; n < iids.Length; n++)
        {
            string s = iids[n];
            Guid g = new Guid(s);
            IntPtr p;
            int hr = Marshal.QueryInterface(punk, ref g, out p);
            Console.WriteLine("QI " + s + " HR=0x" + hr.ToString("X8") + (hr == 0 ? " OK" : ""));
            DumpIface("{" + s + "}");
            if (hr == 0)
            {
                IntPtr vt = Marshal.ReadIntPtr(p);
                for (int i = 0; i < 32; i++)
                    Console.WriteLine("  vt[" + i + "]=0x" + Marshal.ReadIntPtr(vt, i * IntPtr.Size).ToString("X"));
                Marshal.Release(p);
            }
        }
        Marshal.Release(punk);
        Marshal.FinalReleaseComObject(o);
    }
}
