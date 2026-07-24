# Changelog

## 1.0.1.1 — 2026-07-21

### Graphics (PHN18-71)
- **GPU DEVICE** and **DISPLAY MODE** switching in the app UI (no PredatorSense / AcerService).
- **iGPU Only / Hybrid** via Windows PnP (`GpuPnpController`).
- **Optimus / Auto / NVIDIA** via `NvAppSyncProxy` + App SessionFilter COM (`DdsAppSync`) — **no `NVIDIA App.exe`**, no CDP inject.
- DDS handle: HKCU cache → GHI validate → elevated **nvcontainer mem-scan** rediscover after Endurance / container restart.
- Combo self-test: `tools/verify_gpu_combo` → **8/8 PASS**.
- Stability: avoid early `FreeCoTaskMem` / empty Get / DoOp on the AppSync hot path (heap corruption).

### Docs
- `README.md` updated for live GPU switching and release layout.
- `tools/GPU_CONTROL.md` — product path, mem-scan, Step 3 combo results.

### Requirements (GPU)
- Administrator
- NVIDIA App **installed** (COM / `nvxdbat.dll`); App process not required for apply
- Advanced Optimus / hybrid laptop (validated on PHN18-71)

---

## 1.0.0

Initial public line: power / fans / RGB / logo / display Hz / Game Sync; graphics indicator read-only.
