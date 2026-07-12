@echo off
:: Interactive NVCP Display Mode capture (BEFORE -> you switch -> AFTER -> DIFF).
:: No admin required for the core fingerprint + registry diff.
:: Optional: right-click -> Run as administrator to also capture Acer misc GET.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0probe_gpu_nvcp_capture.ps1" -TargetMode "NVIDIA GPU only"
