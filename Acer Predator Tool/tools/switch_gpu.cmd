@echo off
:: Elevate and run GPU switch (PHN18 PnP Endurance/Standard).
:: Examples:
::   switch_gpu.cmd
::   switch_gpu.cmd -- -Mode Status
::   switch_gpu.cmd -- -Mode Endurance -Force
::   switch_gpu.cmd -- -Mode Standard -Force
::   switch_gpu.cmd -- -Mode Cycle -Force
cd /d "%~dp0"

set "ARGS=-Mode Status"
if /I "%~1"=="--" (
  shift
  set "ARGS=%*"
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0switch_gpu.ps1\" %ARGS%'"
echo.
pause
