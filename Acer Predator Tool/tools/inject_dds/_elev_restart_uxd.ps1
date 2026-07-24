Restart-Service NVDisplay.ContainerLocalSystem -Force -ErrorAction SilentlyContinue
Restart-Service NvContainerLocalSystem -Force -ErrorAction SilentlyContinue
Start-Sleep 6
Get-Service NVDisplay.ContainerLocalSystem,NvContainerLocalSystem | Format-Table Name,Status
# Check event after settle
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class E {
 [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] public static extern IntPtr OpenEvent(uint a,bool b,string n);
}
"@
$h=[E]::OpenEvent(0x100000,$false,'Local\UXDServiceStarted-D40E81C4-06EF-454A-9E81-1F4D55CEBD57')
Write-Host "UXDServiceStarted handle=$h last=$( [Runtime.InteropServices.Marshal]::GetLastWin32Error() )"
