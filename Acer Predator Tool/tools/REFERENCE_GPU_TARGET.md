# PHN18 GPU target: full PreySense-equivalent without Acer services

> Повна операційна специфікація (реєстри, UXD, ризики, діагностика): **[`GPU_CONTROL.md`](GPU_CONTROL.md)**

Goal: reproduce Endurance / Standard / Ultimate (and NVCP Display Mode) **directly**, with **no** AcerService TCP, no PredatorSense UI, no dependency on Acer user-mode services.

## Functional map

| PreySense UI | Desired effect | PHN18 direct path | Status |
|---|---|---|---|
| **Endurance** | iGPU only in Windows (dGPU off) | PnP disable `VEN_10DE` + stop `NVDisplay.ContainerLocalSystem` | **DONE** (`switch_gpu.ps1 -Mode Endurance`) |
| **Standard** | Hybrid MS Optimus, dGPU present | PnP enable + restart NV container | **DONE** (`switch_gpu.ps1 -Mode Standard`) |
| **Ultimate** | Internal panel on dGPU (DDS) | **Product:** `DdsAppSync` / AppSync COM · Lab: `switch_dds.ps1` | **DONE in app 1.0.1.1** |
| (NVCP) Optimus | Force iGPU panel | **Product:** AppSync · Lab: `switch_dds.ps1 -Mode Optimus` | **DONE in app 1.0.1.1** |
| (NVCP) Automatic | DDS allow-list auto | **Product:** AppSync · Lab: `switch_dds.ps1 -Mode Automatic` | **DONE in app 1.0.1.1** |

## Proven HIT metric

`MuxSignature = owner={Intel|NVIDIA}|nv_display={Enabled|Disabled}`

Cycle 2026-07-19 (`nvcp_cycle_20260719_143930`): NVIDIA only / Optimus / Auto = **3/3 PASS** (manual NVCP).

## Rejected on PHN18 (do not depend on)

| Path | Result |
|---|---|
| AcerService `GPU_MODE` TCP :46933 | Unavailable (PWA :15152 only) |
| BIOS `AcerBiosConfigurationTool` offset 80 | InvalidParameter |
| Acer `SetGamingMiscSetting` GPU candidates | No mux HIT |

## Active research (no Acer)

| Probe | Result (2026-07-19) |
|---|---|
| `nvcpl.dll!NvCplSetDisplayMode` modes 0..3, cdecl/stdcall | **NO_HIT** — calls return `rc=1`, mux unchanged (`nvcp_setdisplaymode_20260719_145642`) |
| `NvCplApiShowOptimusTrayUI` | callable (`tray_cdecl0=0`), no mux change observed |
| `NvCplApiGetHybridMode` / `MsHybridStatus` | Init/Muxd often fail (`rc=9` / AV) without proper NVCP daemon |
| Classic UWP NVCP UIA dump | While on Intel Optimus, window is error dialog: *"Налаштування дисплея NVIDIA недоступні"* — no Display Mode radios exposed |
| Public NVAPI Advanced Optimus SET | **Officially unavailable** (NVIDIA forum) |

### HIT: ACE Persistence map (3 modes, no Acer)

Path: `HKLM\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE\`

| Mode | `InternalMuxState` | `InternalMuxIsAutomaticMode` | `ACESwitchedI2D` | MuxSignature (idle) |
|---|---:|---:|---:|---|
| **Automatic** | `1` | `1` | `0` | `owner=Intel\|nv_display=Disabled` |
| **Optimus** (forced) | `1` | `0` | `0` | `owner=Intel\|nv_display=Disabled` |
| **NVIDIA GPU only** | `2` | `0` | `1` | `owner=NVIDIA\|nv_display=Enabled` |

Discriminator **Optimus vs Auto** = `InternalMuxIsAutomaticMode` (`0` vs `1`).  
Both keep `InternalMuxState=1` and Intel panel at idle.

Sessions:
- Auto → dGPU: `nvcp_capture_20260719_204505`
- dGPU → Auto: `nvcp_capture_20260719_204910` (`AutomaticMode` 0→1)
- dGPU → Optimus: `nvcp_capture_20260719_205907` (`AutomaticMode` stays `0`; only State 2→1, I2D 1→0)

Also: `...\Class\{4d36e968-...}\0001\PipeConfig`  
`01 00 00 00` (Intel) ↔ `00 00 00 00` (dGPU).

### ACE write probe (`ace_write_NvidiaOnly_20260719_210141`)

Wrote dGPU ACE (`State=2 Auto=0 I2D=1`) + pulsed `MuxChangeNotif` while on Optimus panel:

| Result | Value |
|---|---|
| `ACE_WRITE_PERSISTED` | **True** |
| `MUX_CHANGED` | **False** (stayed `owner=Intel`) |

Conclusion: ACE keys are a **persistence / mirror** after a real DDS switch, not the live control path. Need driver apply (IOCTL / NVAPI private / NVIDIA App IPC).

### Breakthrough: NVIDIA App `SetDDSState` (live DDS API)

Source: `CEF\plugins\Base\NvCplDisplayPlugin.dll` JSON schema (`module=NvCplDisplayPlugin`, system `CrimsonNative`).

| Command | Role |
|---|---|
| **`GetDDSState`** | returns `bIsSupported`, `bIsAutomatic`, `MuxState`, `kmdResponse`, `srType` |
| **`SetDDSState`** | params: `bIsAutomatic` (bool), `MuxState` (int) |

`MuxState` enum:

| Value | Name | Maps to |
|---:|---|---|
| 0 | `State_Invalid` | — |
| 1 | `State_IGPU` | Optimus / Automatic panel on Intel |
| 2 | `State_DGPU` | NVIDIA GPU only |

Mode mapping (PreySense / NVCP):

| UI mode | `SetDDSState` |
|---|---|
| NVIDIA GPU only | `{ bIsAutomatic: false, MuxState: 2 }` |
| Optimus | `{ bIsAutomatic: false, MuxState: 1 }` |
| Automatic | `{ bIsAutomatic: true, MuxState: 1 }` |

Also in `nvcpl.dll`: `_tagNVCPLAPI_DDSState`, setting name `NVCPLAPI_SETTINGS_DDS_INTERNAL_MUX_STATE`, logs `Not switching mux` / `Not found mux object`. `nvxdapix.dll` logs `NvAPI_DISP_SetDisplayMux` (private NVAPI; ID not in public tables).

### HIT: programmatic `SetDDSState` via CDP (2026-07-19 22:40)

Working path (no Acer):

1. Ensure UXD healthy: named event `Local\UXDServiceStarted-D40E81C4-06EF-454A-9E81-1F4D55CEBD57` must exist.  
   If missing → restart `NVDisplay.ContainerLocalSystem` + `NvContainerLocalSystem` (admin). Symptom when broken: `NvCplApiIsUxdServiceRunning` OpenEvent error 2, or UXD `0x80070005` / `bIsSupported:false`.
2. Launch `NVIDIA App.exe` suspended on the **interactive** desktop (`SW_HIDE`) → inject `hook_cef_port.dll` (writes `remote_debugging_port=9333` at CEF settings +336) → resume. App path: `NvidiaAppCdpHost` (no `launcher.exe`). Scripts: `inject_dds\launcher.exe`. Reuse CDP if already up. **Do not** use a private desktop — steals single-instance NVIDIA App from the user.
3. CDP WebSocket with `suppress_origin=True` → `window.cefQuery` → `QUERY_IPC_EXTENSION_MESSAGE` / `NvCplDisplayPlugin` / `SetDDSState`.

Proven cycle:

| Step | ACE | MuxSignature |
|---|---|---|
| Get (before) | `state=1\|auto=0\|i2d=0` | `owner=Intel\|nv_display=Disabled` |
| Set DGPU | `state=2\|auto=0\|i2d=1` | `owner=NVIDIA\|nv_display=Enabled` |
| Set IGPU | `state=1\|auto=0\|i2d=0` | `owner=Intel\|nv_display=Disabled` |

Log confirmation: `UXD service is running` → `NvCplApiSetSetting : Call NVCPLAPI_SETTINGS_DDS_INTERNAL_MUX_STATE`.

Script: `tools\switch_dds.ps1 -Mode NvidiaOnly|Optimus|Automatic|Get`  
Low-level: `tools\inject_dds\launcher.exe` + `tools\inject_dds\_hit_setdds.py`.

Do **not** fake `UXDServiceStarted` — that yields IPC Success with `bIsSupported:false` and no mux change.

Related dead-ends this session:

| Probe | Result |
|---|---|
| Public `NvAPI_GetHybridMode` | returns `3` (Advanced Optimus-ish); `SetHybridMode` → `0xFFFFFF98` (not supported) |
| ACE registry write | persist yes, mux no |
| CEF UIA | no Display Mode radios when Optimus / error dialog |
| MessageBus pipe from foreign process | ACL denied |
| Fake `UXDServiceStarted` event | plugin Success, `bIsSupported:false`, NO_HIT |
| Private desktop `NvCdpSilent` | steals single-instance NVIDIA App — rejected |
| Foreign-process `NvCplApiSetSetting(0x330)` | returns 0 + Execute(ALL) but **NO_HIT** without in-App UXD |

### Breakthrough: in-process native SetSetting (2026-07-20)

Inside NVIDIA App: `NvCplApiSetSetting(1, hdr, 0x330, val16)` then **`NvCplApiExecute(0x10000, 0, -1)`** (`NVCPLAPI_SETTING_ALL`, 3 args).

Value packing (16 bytes): `byte0=1`, `byte1=bIsAutomatic`, `dword1=MuxState`.

| Mode | First 8 bytes | Result |
|---|---|---|
| Optimus | `01 00 00 00 01 00 00 00` | HIT |
| Automatic | `01 01 00 00 01 00 00 00` | HIT |
| NVIDIA only | `01 00 00 00 02 00 00 00` | HIT |

**No CDP / no visible window:** inject `dds_native_helper.dll` into stock NVIDIA App launched `SW_HIDE`; helper hides any windows. Cycle 5/5 HIT, `VISIBLE_WINDOWS=0`.

```text
tools\inject_dds\inject_native_dds.exe dgpu
tools\inject_dds\switch_dds_native.ps1 -Mode cycle
```

Foreign `nvcpl.dll` from AcerPredatorTool alone = **NO_HIT**.

## App policy

- Do not call AcerService for GPU.
- Preferred DDS path: `DdsNativeHost` (hidden App + native inject). CDP (`NvidiaAppCdpHost`) remains fallback.
- Do not use private desktop `NvCdpSilent`.
