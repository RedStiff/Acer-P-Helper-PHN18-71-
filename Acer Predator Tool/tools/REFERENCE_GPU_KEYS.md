# PHN18 / Acer Predator — GPU & display-mode keys reference

Living notes for Acer Predator Tool research. Sources: PreySense docs, Acer WMI probes on **PHN18-71**, NVCP captures.

**HIT metric:** `MuxSignature = owner={Intel|NVIDIA}|nv_display={Enabled|Disabled}`

---

## 1. Status on PHN18-71 (2026-07)

| Path | Result |
|------|--------|
| AcerService TCP `GPU_MODE` (PreySense port **46933**) | **Unavailable** — service listens on **15152** (HTTPS PWA), no ACER packet protocol |
| `AcerBiosConfigurationTool` Get/SetBiosOptions | Class present, calls return **InvalidParameter** |
| Acer `SetGamingMiscSetting` focused GPU candidates | **No mux HIT** (`WMI_HITS=0`) |
| NVIDIA Control Panel Display Mode (DDS) | **Works** — cycle 2026-07-19: NVIDIA only / Optimus / Auto = **3/3 PASS** |
| PnP disable/enable NVIDIA display (`VEN_10DE`) | **Works** for iGPU-only / restore (PreySense Endurance/Standard style) |
| Public NVAPI / `nvcpl.dll` SetHybridMode | **No SET export** |

App UI: graphics buttons are **read-only indicators** only.

---

## 2. BIOS option buffer (`AcerBiosConfigurationTool`)

PreySense historical map (`docs/discovered_offsets.md`). Buffer byte index = setting.

| Offset (dec) | Hex | Setting | Values | PHN18 note |
| ---: | ---: | --- | --- | --- |
| 14 | `0x0E` | Intel VT-x | `0` off, `1` on | not GPU |
| 15 | `0x0F` | Intel VT-d | `0`/`1` | not GPU |
| 22 | `0x16` | Active Efficient Cores | `0`/`1` | not GPU |
| 23 | `0x17` | GNA Device | `0`/`1` | not GPU |
| **80** | **`0x50`** | **Display mode** | **`1` Auto, `2` Optimus, `3` dGPU** | **GetBiosOptions = InvalidParameter** |
| 161 | `0xA1` | Network Boot | `0`/`1` | |
| 162 | `0xA2` | Wake on LAN | `0`/`1` | |
| 170 | `0xAA` | Wake on USB (lid closed) | `0`/`1` | |
| 177 | `0xB1` | USB/TBT Wake from S4 | `0`/`1` | |
| 301 | `0x12D` | F12 Boot Menu | `0`/`1` | |
| 302 | `0x12E` | Fn key behavior | `0` media, `1` function | |
| 303 | `0x12F` | D2D Recovery | `0`/`1` | |
| 305 | `0x131` | KB backlight timeout | `0`/`1` | |
| 307 | `0x133` | Fast Boot | `0`/`1` | |
| 328 | `0x148` | Post animation/sound | `0`/`1` | |
| 329 | `0x149` | Post animation sound | `0` mute, `1` unmute | |
| 1934 | `0x78E` | Secure Boot | `0`/`1` | |

**WMI methods:** `GetBiosOptions` / `SetBiosOptions` — params `Password` (`uint8[]`), `PasswordLen` (`uint16`), `Data` (`uint8[]` on SET).

PreySense maps AcerService mux → BIOS byte: discrete(`1`) → `3`, hybrid(`2`) → `1` (Auto).

---

## 3. AcerService TCP (PreySense protocol)

Documented for machines where it works (not PHN18 command path):

| Item | Value |
|------|--------|
| Host | `127.0.0.1` |
| Command port | **46933** |
| Telemetry port | 46753 |
| Packet | ASCII `ACER` + `uint32` LE packet id + JSON (optional AES-ECB if `HKCU\Software\Acer\XSense\AESkey` = 32 ASCII chars) |
| SET packet id | `100` (`SET_DEVICE_DATA`) |
| GET packet id | `20` (`GET_UPDATED_DATA`) |
| INIT packet id | `0` |

### `GPU_MODE` (SET)

```json
{"Function":"GPU_MODE","Parameter":{"mode":1}}
```

| `mode` | Meaning (PreySense) | Notes |
|--------|---------------------|--------|
| `1` | Discrete / Ultimate | Requires reboot |
| `2` | Hybrid (Endurance/Standard mux) | Endurance still disables dGPU in Windows |

Related SET functions (same packet id): `OPERATING_MODE`, `FAN_CONTROL`, `LIGHTING`, `LCD_OVERDRIVE`, `PANEL_DFR_MODE`, …

**PHN18:** `AcerService.exe` embeds `https://localhost:15152` (PWA/JWT). Listening TCP **15152** accepts connect but does **not** speak this ACER protocol.

---

## 4. AcerGamingFunction WMI (`root\WMI`)

Misc setting packing (app / probes):

```text
GET: gmInput = id
SET: gmInput = id | (value << 8)
status = raw & 0xFF
value  = (raw >> 8) & 0xFF
```

### Known / used misc IDs

| ID | Role | Notes |
|----|------|--------|
| `0x01` | misc candidate / telemetry | scanned in GPU probes |
| `0x05` | OC1 | **DO NOT brute SET** |
| `0x06` | misc candidate | focused Apply: no mux HIT |
| `0x07` | OC2 | **DO NOT brute SET** |
| `0x08` | misc candidate | focused Apply: no mux HIT |
| **`0x0B`** | **Power / platform profile** | Silent=`0x00`, Balanced=`0x01`, Perf=`0x04`, Turbo=`0x05`, Eco=`0x06` — **not GPU mux** |
| `0x0A` | supported profiles | skip in GPU brute |
| `0x0C`..`0x30` | historical “GPU mode” guesses | DryRun/Apply on PHN18: **no panel mux change** |
| `0x0C`, `0x0F`, `0x0D`, `0x0E` | often appear in NVCP capture misc diffs as high-bit noise | treat as unreliable for HIT |

Other methods used by the app (not mux): `SetGamingFanBehavior`, `SetGamingFanSpeed`, `SetGamingLED*`, `GetGamingSysInfo`, …

---

## 5. Registry keys

### AcerAgent capability flags (observed on PHN18)

`HKLM\SOFTWARE\OEM\AcerAgentService\AdvanceSettings`

| Value | Observed | Meaning (inferred) |
|-------|----------|--------------------|
| `discrete_gpu_support` | **`0`** | Agent reports discrete mux UI unsupported |
| `dgpu_mode_capability` | **`7`** | bitmask / capability nibble (exact bits TBD) |
| `LCD_Overdrive_support` | `1` | LCD overdrive available |

Also present:

| Path | Value | Note |
|------|-------|------|
| `...\FanControl\CurrentFanMode` | `0` | fan mode mirror |
| `...\Overclock\GPU_OC_Support` | `1` | OC support flag |
| `...\LightSetting\*` | … | RGB, not mux |

Writing these flags alone is **unlikely** to switch DDS; useful as capability detection only.

### PreySense / XSense

| Path | Note |
|------|------|
| `HKCU\Software\Acer\XSense\AESkey` | 32-char ASCII → AES-ECB for AcerService TCP |
| `HKCU\SOFTWARE\PreySense` | PreySense app state (not Acer firmware) |

### NVIDIA / Windows (research dump targets)

Probes walk (see `probe_gpu_nv_registry.ps1`, observe sessions):

- `HKLM\SOFTWARE\NVIDIA Corporation`
- `HKCU\Software\NVIDIA Corporation`
- `HKLM\SYSTEM\CurrentControlSet\Services\nvlddmkm`
- `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers`
- `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}` (display class)
- `HKCU\Software\Microsoft\DirectX`

Interesting name patterns: `Hybrid`, `Mux`, `DDS`, `DisplayMode`, `Optimus`, `GpuMode`, `AdvancedOptimus`.

NVCP Display Mode changes **MuxSignature** but typically leave **little durable Acer misc / registry** delta (see `nvcp_capture_*/DIFF.txt`).

---

## 6. Windows PnP / services (PreySense Endurance / Standard)

| Action | Mechanism |
|--------|-----------|
| Endurance (iGPU only) | `Disable-PnpDevice` display class matching `VEN_10DE` + stop `NVDisplay.ContainerLocalSystem` |
| Standard (hybrid) | `Enable-PnpDevice` same + restart NV container service |

Does **not** flip hardware DDS mux the way NVCP “NVIDIA GPU only” does; it removes/restores the dGPU device from Windows.

Services often involved:

| Service | Role |
|---------|------|
| `AcerServiceSvc` | Wrapper → `AcerService.exe` (PHN18: port 15152 PWA) |
| `AcerLightingService` | Lighting stack; start with AcerService for some builds |
| `NVDisplay.ContainerLocalSystem` | NVIDIA user-mode container |

---

## 7. NVIDIA user-mode APIs

| API | GET | SET |
|-----|-----|-----|
| `nvcpl.dll` `NvCplApiGetHybridMode` / `NvCplApiMsHybridStatus` | yes (probe) | **no public SET** |
| NVAPI Display Mode / DDS | — | **NVCP UI only** (NVIDIA statement) |
| `nvidia-smi` `display_active` | yes | n/a |

Correlate HybridMode integer with `MuxSignature` via `probe_gpu_nvcpl_hybrid.ps1`.

---

## 8. PreySense UI ↔ hardware mapping (reference)

| UI | Mux (AcerService) | Windows side |
|----|-------------------|--------------|
| Endurance | hybrid `2` | Disable NVIDIA PnP + stop NV service |
| Standard | hybrid `2` | Enable NVIDIA PnP + restart NV service |
| Ultimate | discrete `1` | SET + **reboot** |

---

## 9. Switch scripts (proven on PHN18)

| Script | Purpose |
|--------|---------|
| `switch_gpu.ps1 -Mode Status` | Show mux + NVIDIA PnP state |
| `switch_gpu.ps1 -Mode Endurance` | Disable NVIDIA PnP + stop NV service |
| `switch_gpu.ps1 -Mode Standard` | Enable NVIDIA PnP + restart NV service |
| `switch_gpu.ps1 -Mode Cycle -Force` | Self-test Endurance → Standard |
| `switch_dds.ps1 -Mode NvidiaOnly\|Optimus\|Automatic\|Get` | Live DDS / Advanced Optimus (no Acer) |
| `switch_gpu.cmd` / `switch_gpu_endurance.cmd` / `switch_gpu_standard.cmd` | Elevated wrappers |

## 10. Probe entry points

| Script | Purpose |
|--------|---------|
| `probe_gpu_phn18_matrix.ps1` | Matrix of read-only + optional PnP tests for this model |
| `probe_gpu_preysense.ps1` | PreySense TCP/BIOS/PnP with DIFF sessions |
| `probe_gpu_nvcp_capture.ps1` | Interactive NVCP DDS capture (one mode) |
| `switch_gpu_nvcp_cycle.ps1` / `.cmd` | Full cycle: NVIDIA GPU only → Optimus → Automatic |

### NVCP cycle result (PHN18-71, `nvcp_cycle_20260719_143930`)

| Step | Mode | MuxSignature | Verdict |
|------|------|--------------|---------|
| baseline | — | `owner=Intel\|nv_display=Disabled` | — |
| 1 | NVIDIA GPU only | `owner=NVIDIA\|nv_display=Enabled` | PASS |
| 2 | Optimus | `owner=Intel\|nv_display=Disabled` | PASS |
| 3 | Automatic | `owner=Intel\|nv_display=Disabled` (same as Optimus idle) | PASS_LIKELY |

**Launch note:** `Control Panel Client\nvcplui.exe` on this machine is an **NVIDIA App stub**. Starting it without CEF cwd shows **libcef.dll missing**. Use classic UWP *NVIDIA Control Panel*, or `NVIDIA App\CEF\NVIDIA App.exe` with WorkingDirectory = that CEF folder (`Open-NvidiaDisplayModeUi` in `_gpu_common.ps1`).
| `probe_gpu_nvcpl_hybrid.ps1` | NvCpl GetHybridMode |
| `probe_gpu_baseline.ps1` | Misc GET inventory |
| `REFERENCE_GPU_KEYS.md` | this file |

---

## 11. Emulation priority for Acer Predator Tool (PHN18)

1. **Read-only indicator** — `EnumDisplayDevices` + `nvidia-smi` (`Get-GpuFingerprint` / `MuxSignature`).
2. **Endurance / Standard** — PnP disable/enable NVIDIA + NVDisplay container (`switch_gpu.ps1`).
3. **Ultimate / Optimus / Automatic** — NVIDIA App `SetDDSState` via CDP (`switch_dds.ps1`) — **proven 2026-07-19**.
4. Do **not** depend on AcerService `46933` or BIOS offset 80 on this SKU.
5. Do **not** treat ACE registry write as live control (mirror only).

Повна специфікація: **[`GPU_CONTROL.md`](GPU_CONTROL.md)**.
