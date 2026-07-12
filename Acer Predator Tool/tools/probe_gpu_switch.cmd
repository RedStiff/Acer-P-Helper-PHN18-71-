@echo off
:: Elevate and run automatic GPU research suite (baseline + dry-run bruteforce).
:: For real WMI SET attempts: probe_gpu_switch_apply.cmd
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0probe_gpu_switch.ps1\"'"
