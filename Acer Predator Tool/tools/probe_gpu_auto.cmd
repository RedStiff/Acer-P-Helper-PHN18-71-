@echo off
:: Elevate and run full automatic GPU research suite (read-only + DryRun WMI).
:: For focused WMI SET: probe_gpu_auto_apply.cmd
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0probe_gpu_auto.ps1\"'"
echo.
echo Done. Check probe_gpu_auto_*.log and probe_gpu_auto_summary_*.txt in this folder.
pause
