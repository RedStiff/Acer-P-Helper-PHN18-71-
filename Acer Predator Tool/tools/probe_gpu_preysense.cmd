@echo off
:: Elevate and open PreySense-style GPU probe (Status by default).
:: Pass args after -- , e.g.:
::   probe_gpu_preysense.cmd -- -Action Ultimate -StartService
::   probe_gpu_preysense.cmd -- -Action Ultimate -StartService -Apply
cd /d "%~dp0"

set "ARGS="
if /I "%~1"=="--" (
  shift
  set "ARGS=%*"
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0probe_gpu_preysense.ps1\" %ARGS%'"
echo.
echo Done. Check probe_gpu_preysense_*.log in this folder.
pause
