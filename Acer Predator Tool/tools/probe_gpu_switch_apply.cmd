@echo off
:: Elevate and run GPU suite WITH SetGamingMiscSetting attempts.
:: May briefly blank the screen if a real mux path is hit.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0probe_gpu_switch.ps1\" -Apply'"
