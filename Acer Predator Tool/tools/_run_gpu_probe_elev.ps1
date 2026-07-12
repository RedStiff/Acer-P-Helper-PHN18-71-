$ErrorActionPreference = "Continue"
Set-Location "$PSScriptRoot"
& ".\probe_gpu_auto.ps1" 2>&1 | Tee-Object -FilePath ".\probe_gpu_auto_console_latest.txt"
