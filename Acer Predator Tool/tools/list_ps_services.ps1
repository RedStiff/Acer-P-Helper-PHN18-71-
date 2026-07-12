# Lists PredatorSense/Acer related services and drivers to see what performs boot-time lighting init
Write-Output '=== Services (Acer/Predator) ==='
Get-CimInstance Win32_Service |
    Where-Object { $_.DisplayName -match 'Acer|Predator' -or $_.Name -match 'Acer|Predator' } |
    Select-Object Name, DisplayName, State, StartMode, PathName |
    Format-List

Write-Output '=== Kernel drivers (Acer/Predator) ==='
Get-CimInstance Win32_SystemDriver |
    Where-Object { $_.DisplayName -match 'Acer|Predator' -or $_.Name -match 'Acer|Predator' } |
    Select-Object Name, DisplayName, State, StartMode, PathName |
    Format-List

Write-Output '=== Running processes (Acer/Predator) ==='
Get-Process |
    Where-Object { $_.ProcessName -match 'Acer|Predator|sense' } |
    Select-Object ProcessName, Id, Path |
    Format-List
