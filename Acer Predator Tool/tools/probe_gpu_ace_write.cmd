@echo off
:: Elevated ACE Persistence write probe (no Acer).
:: Examples:
::   probe_gpu_ace_write.cmd
::   probe_gpu_ace_write.cmd -- -Mode NvidiaOnly -Force -PulseNotify
::   probe_gpu_ace_write.cmd -- -Mode Optimus -Force
cd /d "%~dp0"

set "ARGS=-Mode NvidiaOnly -Force -PulseNotify"
if /I "%~1"=="--" (
  shift
  set "ARGS=%*"
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0probe_gpu_ace_write.ps1\" %ARGS%'"
echo.
pause
