@echo off
setlocal
:: Full interactive NVCP Display Mode cycle:
::   NVIDIA GPU only -> Optimus -> Automatic
:: Does NOT need admin (DDS is controlled in NVIDIA Control Panel).
::
:: Usage:
::   switch_gpu_nvcp_cycle.cmd
::   switch_gpu_nvcp_cycle.cmd -- -SkipOpenNvcp
cd /d "%~dp0"

set "ARGS="
if /I "%~1"=="--" (
  shift
  set "ARGS=%*"
)

start "NVCP Display Mode Cycle" /D "%~dp0" powershell.exe -NoExit -NoProfile -ExecutionPolicy Bypass -File "%~dp0switch_gpu_nvcp_cycle.ps1" %ARGS%
endlocal
