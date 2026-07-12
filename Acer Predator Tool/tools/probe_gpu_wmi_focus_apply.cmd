@echo off
:: Focused Apply: try most likely GPU-mode misc IDs only (0x0C, 0x0F, 0x01, 0x06, 0x08).
:: Expect possible short black screen.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0probe_gpu_wmi_bruteforce.ps1\" -Apply -Ids 0x0C,0x0F,0x01,0x06,0x08 -SettleMs 8000'"
PAUSE
