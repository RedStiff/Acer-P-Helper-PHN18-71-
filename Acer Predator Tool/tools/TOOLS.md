# Tools — research & verification scripts

Operational GPU spec: [`GPU_CONTROL.md`](GPU_CONTROL.md)  
Lighting research: [`README_LIGHTING_RESEARCH.md`](README_LIGHTING_RESEARCH.md)  
Fan/EC: [`README_EC_RESEARCH.md`](README_EC_RESEARCH.md)

Most scripts need **Administrator**. Prefer read-only first. Close games / `AcerPredatorTool` before destructive GPU tests. Captures and `*.log` / `*_out.txt` are **not** kept in git — re-run scripts to regenerate.

---

## Product verification (keep)

| Entry | Purpose |
|-------|---------|
| `verify_gpu_combo/` | Elevated PnP + DDS AppSync combo (**8/8** target). `dotnet build -c Release` then run exe as admin |
| `verify_gpu_service/` | DDS-only cycle via `DdsControl` |
| `switch_gpu.ps1` / `switch_gpu.cmd` | Endurance / Standard / Status / Cycle (PnP) |
| `switch_dds.ps1` | Lab DDS via NVIDIA App CDP (legacy; product uses AppSync) |
| `_gpu_common.ps1` | Shared fingerprint / MuxSignature helpers |

```powershell
cd "…\Acer Predator Tool\tools"
.\switch_gpu.ps1 -Mode Status
.\switch_gpu.ps1 -Mode Cycle -Force   # Endurance → Standard

# Combo (UAC):
dotnet build .\verify_gpu_combo\verify_gpu_combo.csproj -c Release
Start-Process .\verify_gpu_combo\bin\Release\net10.0-windows\verify_gpu_combo.exe -Verb RunAs
```

---

## GPU research (historical / optional)

Canonical conclusions live in `GPU_CONTROL.md` and `REFERENCE_GPU_TARGET.md`.  
Scripts below are for re-probing after driver/App updates — **not** required for daily app use.

| Entry | Purpose |
|-------|---------|
| `probe_gpu_fingerprint.ps1` | Panel owner + nvidia-smi |
| `probe_gpu_nv_registry.ps1` | NVIDIA registry dump |
| `probe_gpu_nvcpl_hybrid.ps1` | NvCpl GetHybridMode (no public Set) |
| `probe_gpu_baseline.ps1` | Acer WMI inventory |
| `probe_gpu_phn18_matrix.cmd` | Matrix of paths → writes a **new** `phn18_matrix_*/` folder |
| `probe_gpu_preysense.cmd` | PreySense TCP/PnP observation |
| `probe_gpu_nvcp_capture.cmd` | Manual NVCP BEFORE/AFTER (`INSTRUCTIONS_GPU_NVCP_CAPTURE.md`) |
| `probe_gpu_auto.cmd` / `probe_gpu_auto_apply.cmd` | Suite + optional misc SET |
| `inject_dds/` | Lab: AppSync HIT sources (`_mem_handle.cs`, Frida JS). Product binaries: `hook_cef_port.dll`, `dds_native_helper.dll` |
| `REFERENCE_GPU_KEYS.md` | BIOS / AcerService / misc ID catalogue |

Timestamped `*_capture_*` / `*_20260719_*` dump folders are **removed** from the tree; re-run the matching `.cmd` if you need a fresh DIFF.

---

## Lighting

| Entry | Purpose |
|-------|---------|
| `README_LIGHTING_RESEARCH.md` | Product = pure WMI; zero Acer lighting SW |
| `verify_selfsufficiency.ps1` | WMI-only static after visual DARK (not true EC de-activate) |
| `replay_service_static_init.ps1` | Two-round WMI static sequence |
| `probe_logo_*.ps1` | Lid logo WMI probes |
| `probe_static_wake_openrgb.ps1` | Lab/RE only (OEM OpenRGB) — not product |
| `probe_ec_diff.ps1` | Lab/RE: WMI snapshot around OpenRGB detect |
| `probe_enable_lighting_svc.ps1` | Legacy: start AcerLightingService (not used) |
| `restart_lighting.cmd` | Legacy service restart |

```powershell
# Cold static without any Acer lighting SW (admin) — confirm cyan / 4-zone:
.\verify_selfsufficiency.ps1
```

---

## Fan / EC

| Entry | Purpose |
|-------|---------|
| `ec_wmi_fan_snapshot.ps1` | Fan % / RPM / temps |
| `ec_dump_readonly.ps1` / `ec_diff_dumps.ps1` | Read-only EC helpers |
| `README_EC_RESEARCH.md` | ~10% duty bands |

---

## Hygiene

Do **not** commit:

- `*.log`, `*_out.txt`, `*_console.txt`, `debug.log`
- `phn18_matrix_*`, `acer_service_capture_*`, `nvcp_capture_*`, other dated dump dirs
- `bin/`, `obj/` under `verify_*` / probe projects
- One-off `inject_dds\_*.exe` from `csc` (sources `*.cs` may remain)

Product publish still embeds `inject_dds\hook_cef_port.dll` and `dds_native_helper.dll` (legacy CDP path disabled in UI).
