@echo off
:: Reverse capture: current mode -> Optimus (tray NVIDIA App).
cd /d "%~dp0"
start "NVIDIA Display Mode Capture (Optimus)" /D "%~dp0" powershell.exe -NoExit -NoProfile -ExecutionPolicy Bypass -File "%~dp0probe_gpu_nvcp_capture.ps1" -TargetMode "Optimus" -UiSource Tray
