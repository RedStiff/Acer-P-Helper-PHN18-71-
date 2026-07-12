# PHN18 GPU mux / Advanced Optimus research helpers
#
# Status (2026-07)
# ----------------
# App UI: GRAPHICS INDICATOR — READ-ONLY Integrated / Discrete (no Auto).
# Switching Display Mode is NOT implemented — use NVIDIA Control Panel.
# Acer SetGamingMiscSetting focused Apply: WMI_HITS=0 (no panel mux change).
# NVCP capture: real DDS switch changes MuxSignature; registry/Acer misc do not.
#
# USER ACTION (optional capture)
# -----------------------------
#   Read:  INSTRUCTIONS_GPU_NVCP_CAPTURE.md
#   Run:   probe_gpu_nvcp_capture.cmd
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
# - Close AcerPredatorTool / PredatorSense / games before -Apply
# - Expect possible short black screen on real DDS switch (NVCP)
# - Never force -Ids that include 0x0B (power profile) or OC IDs 0x05/0x07
# - Research only — not for production GPU switching
