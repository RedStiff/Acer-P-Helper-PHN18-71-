@echo off
:: Elevate and run GPU suite WITH focused Acer misc SET (0x0C,0x0F,0x01,0x06,0x08).
:: May briefly blank the screen if a real mux path is hit.
:: Close games / AcerPredatorTool / PredatorSense first.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0probe_gpu_auto.ps1\" -Apply'"
echo.
echo Done. Check probe_gpu_auto_summary_*.txt for WMI_HITS.
pause
