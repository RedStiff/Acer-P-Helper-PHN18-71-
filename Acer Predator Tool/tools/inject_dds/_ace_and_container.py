import winreg
import subprocess
import pathlib

DIR = pathlib.Path(r"E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds")

k = winreg.OpenKey(
    winreg.HKEY_LOCAL_MACHINE,
    r"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE",
)
for n in ("InternalMuxState", "InternalMuxIsAutomaticMode", "ACESwitchedI2D"):
    print(n, winreg.QueryValueEx(k, n)[0])

r = subprocess.run(
    ["wmic", "process", "where", "name='NVDisplay.Container.exe'", "get", "ProcessId,CommandLine", "/FORMAT:LIST"],
    capture_output=True,
    text=True,
)
print(r.stdout)
