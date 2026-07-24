@echo off
:: Interactive NVIDIA App Display Mode capture (BEFORE -> you switch from tray -> AFTER -> DIFF).
:: Default: -UiSource Tray (open NVIDIA App from system tray yourself).
:: No admin required for fingerprint + registry diff.
cd /d "%~dp0"
start "NVIDIA Display Mode Capture" /D "%~dp0" powershell.exe -NoExit -NoProfile -ExecutionPolicy Bypass -File "%~dp0probe_gpu_nvcp_capture.ps1" -TargetMode "NVIDIA GPU only" -UiSource Tray
