@echo off
:: Elevate and run PHN18 GPU path matrix.
:: Default: read-only + NvCpl + registry.
:: Optional args after -- :
::   -- -ApplyPnp
::   -- -ApplyPnp -ApplyAgentReg
cd /d "%~dp0"

set "ARGS="
if /I "%~1"=="--" (
  shift
  set "ARGS=%*"
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0probe_gpu_phn18_matrix.ps1\" %ARGS%'"
echo.
echo Done. Open newest phn18_matrix_*/MATRIX_SUMMARY.txt
pause
