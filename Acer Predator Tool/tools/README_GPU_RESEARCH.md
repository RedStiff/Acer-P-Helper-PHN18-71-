# PHN18 GPU mux / Advanced Optimus research helpers
#
# Status (2026-07-21) — PRODUCT v1.0.1.1
# --------------------------------------
# App UI GRAPHICS:
#   GPU DEVICE  = PnP Endurance/Standard (GpuPnpController)
#   DISPLAY MODE = NvAppSyncProxy + SessionFilter (DdsAppSync), no App.exe
# Spec: GPU_CONTROL.md  |  Combo: tools/verify_gpu_combo (8/8 PASS)
#
# Below: historical research notes (CDP/NVCP/AcerService). Prefer AppSync for product.
#
# PreySense path (new probe)
# --------------------------
# https://github.com/hammadzaigham/PreySense uses AcerService TCP GPU_MODE
# (not AcerGaming misc). Ultimate = mode 1 + reboot; Endurance/Standard =
# enable/disable NVIDIA PnP device. See probe_gpu_preysense.ps1.
# On machines where AcerServiceSvc is Disabled, start it first (-StartService).
#
# USER ACTION (optional capture)
# -----------------------------
#   Read:  INSTRUCTIONS_GPU_NVCP_CAPTURE.md
#   Run:   probe_gpu_nvcp_capture.cmd
#
# Key reference
# -------------
#   REFERENCE_GPU_KEYS.md   BIOS offsets, AcerService, misc IDs, registry, PnP
#
# Working switch scripts (PHN18 PnP)
# ---------------------------------
#   switch_gpu.cmd -- -Mode Status
#   switch_gpu.cmd -- -Mode Endurance -Force
#   switch_gpu.cmd -- -Mode Standard -Force
#   switch_gpu.cmd -- -Mode Cycle -Force
#   (DDS "NVIDIA GPU only" still = NVCP Display Mode)
#
# PHN18 matrix (find what works on THIS machine)
# ----------------------------------------------
#   probe_gpu_phn18_matrix.cmd
#   probe_gpu_phn18_matrix.cmd -- -ApplyPnp
#   probe_gpu_phn18_matrix.cmd -- -ApplyPnp -ApplyAgentReg
#   -> phn18_matrix_*/MATRIX_SUMMARY.txt
#
# PreySense-style tests (UAC elevate) — USE AT YOUR OWN RISK
# ---------------------------------------------------------
# Goal: remove AcerService dependency by capturing what GPU_MODE changes.
# -Apply writes acer_service_capture_*/DIFF.txt:
#   TCP request/response, BIOS Data[80], Acer misc GET diffs, registry, PnP, services.
#
#   probe_gpu_preysense.cmd
#     powershell -File probe_gpu_preysense.ps1
#     powershell -File probe_gpu_preysense.ps1 -Action Status -StartService
#     powershell -File probe_gpu_preysense.ps1 -Action Ultimate -StartService -Apply
#     powershell -File probe_gpu_preysense.ps1 -Action Endurance -Apply
#     powershell -File probe_gpu_preysense.ps1 -Action Standard -Apply
#   Then open acer_service_capture_*/DIFF.txt + EMULATE.md
#   NOTE (PHN18): PreySense TCP 46933 + BIOS offset80 currently UNAVAILABLE.

#
# Automatic entry points (UAC elevate) — USE AT YOUR OWN RISK
# -----------------------------------------------------------
#   probe_gpu_auto.cmd          read-oriented suite + WMI DryRun
#   probe_gpu_auto_apply.cmd    + focused misc SET (may blank screen; no mux HIT on PHN18)
#
# Objective HIT
# -------------
# MuxSignature = owner={Intel|NVIDIA}|nv_display={Enabled|Disabled}
#
# Safety
# ------
# - Close AcerPredatorTool / PredatorSense / PreySense / games before -Apply
# - Ultimate/Hybrid mux SET may require reboot (-Reboot) before MuxSignature changes
# - Endurance disables the NVIDIA device — restore with -Action Standard -Apply
# - Expect possible short black screen on real DDS switch (NVCP)
# - Never force -Ids that include 0x0B (power profile) or OC IDs 0x05/0x07
# - Research only — not for production GPU switching
