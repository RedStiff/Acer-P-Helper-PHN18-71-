# Acer Predator Tool

A lightweight, open-source alternative to PredatorSense for Acer Predator laptops. Built with C# / WinForms, it runs from the system tray and talks directly to Acer’s `AcerGamingFunction` WMI interface — without the official suite’s background services.

This repository is a fork/evolution of **[Paulrod20/p-helper](https://github.com/Paulrod20/p-helper)** (Predator Control), published as **[supesonly/Acer-P-Helper](https://github.com/supesonly/Acer-P-Helper)** and extended for Helios Neo **PHN18-71** and related Predator Sense v4 hardware.

---

## Disclaimer — use at your own risk

> **This software is experimental community tooling.** It was built with the assistance of AI tools and community reverse-engineering of Acer / NVIDIA interfaces.
>
> It has been tested primarily on **Acer Predator Helios Neo 18 (PHN18-71)** under Windows 11. Compatibility with other models is **not guaranteed**.
>
> **Use at your own risk.** The authors provide **no warranty**. Incorrect fan, power, lighting, or research-script usage can cause thermal throttling, display blanking, instability, or other hardware/software issues. Keep a recovery path (PredatorSense / BIOS / Windows Safe Mode) available.
>
> Scripts under `Acer Predator Tool/tools/` may call elevated WMI, scan registry, or briefly blank the screen during GPU Display Mode research. Run them only if you understand what they do.

---

## What works

| Area | Status | Notes |
|------|--------|--------|
| **Power modes** | Works | Silent, Balanced, Performance, Turbo, Eco; AC/battery auto behaviour |
| **Fan Auto / Max / Custom** | Works | Custom duty via WMI; **EC applies ~10% steps** (see below) |
| **Advanced fan curves** | Works | Per power-mode profiles, live apply, slope/stair, Save to profile |
| **Temps / fan RPM / clocks** | Works | Live readouts on the main panel and tray tooltip |
| **Display refresh** | Works | Toggle 60 Hz ↔ panel max |
| **Keyboard RGB** | Works* | Effects via WMI; static: one OEM OpenRGB detect per boot then WMI — **no AcerLightingService** |
| **Lid logo backlight** | Works* | Static colour, brightness, on/off (Nekro `SetGamingLEDColor` path). Breathing/Neon **not** available on this chassis via WMI |
| **Graphics** | Works* | **iGPU Only / Hybrid** (PnP) + **Optimus / Auto / NVIDIA** (DDS via `NvAppSyncProxy`, no NVIDIA App.exe). PHN18-71 validated |
| **Battery charge limit** | Works* | 80% health / 100% full when `BatteryControl` WMI is present |
| **Game Sync** | Works* | Per-app profiles (power/fan/RGB) while monitoring is enabled |
| **System tray + hotkeys** | Works | Tray menu; startup; **Predator** key and **Ctrl+Alt+P** show/hide the window |
| **UI shell** | Works | Fixed-size borderless main window; Lighting side panel; Curves panel |

\* Model-dependent — probe at runtime; UI shows limited / read-only behaviour when switching is unsupported.

### Fan Custom / Advanced — EC ~10% step (PHN18-71)

WMI `SetGamingFanSpeed` accepts 0–100%, but firmware maps duty to **bands of ~10%** (0, 10, …, 100). Measured RPM is flat inside a band and jumps on decade change.

The app mirrors that reality:

- Custom sliders step by **10%**
- Curve points and hardware targets snap to **10%**
- RPM hints use measured band tables (not linear `% × maxRpm`)

There is **no documented 1% PWM** path for PHN18-71 through `AcerGamingFunction`.

### Graphics — PnP + NVIDIA AppSync (PHN18)

Two independent layers (do not confuse):

| UI | Effect | Path |
|----|--------|------|
| **iGPU Only** | dGPU disabled in Windows (PreySense Endurance) | PnP disable `VEN_10DE` + stop NVDisplay container |
| **Hybrid** | dGPU present again (Standard) | PnP enable + restart container |
| **Optimus / Auto / NVIDIA** | Advanced Optimus Display Mode (DDS mux) | `NvAppSyncProxy` + SessionFilter COM (`DdsAppSync`) |

Requirements for Display Mode: **Administrator**, **NVIDIA App installed** (COM / `nvxdbat.dll` — App process not required), Advanced Optimus hardware.

After **iGPU Only**, Display Mode auto-restores Hybrid, waits for UXD, and may **mem-scan rediscover** the DDS handle (SeDebug). Details: [`Acer Predator Tool/tools/GPU_CONTROL.md`](Acer%20Predator%20Tool/tools/GPU_CONTROL.md).

---

## What does not work / limitations

| Item | Status |
|------|--------|
| **Logo Breathing / Neon effects** | Not available via WMI on PHN18 |
| **1% fan granularity** | Not available via official WMI; raw EC write not enabled |
| **GPU / CPU overclock** | Not implemented (PredatorSense OC tabs) |
| **LCD override, USB charging, boot animation** | Not implemented |
| **Battery calibration** | Not implemented |
| **Per-key RGB** | Not supported — four-zone / effect modes only |
| **AcerLightingService** | Not required — replaced by OEM OpenRGB detect once/boot + WMI |
| **All Predator / Nitro models** | Only validated on **PHN18-71** (Windows 11); other models may partially work or fail |
| **Non-admin use** | Requires **Administrator** (WMI write, PnP, DDS handle rediscover) |

---

## Requirements

- Acer Predator laptop exposing `root\WMI` → `AcerGamingFunction`
- Windows 10 or 11
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- Run as **Administrator**

---

## Download / build

**Download:** [Releases](../../releases) — run `AcerPredatorTool.exe` as administrator. See [`CHANGELOG.md`](CHANGELOG.md).

**From source:**

```text
dotnet build "Acer Predator Tool/AcerPredatorTool.csproj" -c Release
```

Or open `AcerPredatorTool.slnx` in Visual Studio.

Output: `Acer Predator Tool/bin/Release/net10.0-windows/AcerPredatorTool.exe`

**Publish package (framework-dependent, win-x64):**

```text
dotnet publish "Acer Predator Tool/AcerPredatorTool.csproj" -c Release -r win-x64 --self-contained false -o "release/AcerPredatorTool-1.0.1.1"
```

Published layout: `release/AcerPredatorTool-1.0.1.1/`

---

## Open / hide the window (hotkeys)

While the app is running in the tray:

| Input | Action |
|-------|--------|
| **Predator** key (dedicated laptop key) | Show or hide the main window |
| **Ctrl+Alt+P** | Same (global hotkey) |
| Tray icon double-click | Show the window |

If another app already owns **Ctrl+Alt+P**, the global hotkey may fail to register; the **Predator** key still works when Acer’s conflicting hotkey services are disabled.

---

## Replacing PredatorSense

The app uses the firmware WMI provider (part of the platform), not PredatorSense services. You can disable Acer gaming services after confirming this tool works on your machine. Keep PredatorSense installed until you are satisfied.

**Do not disable the WMI/ACPI stack** — only PredatorSense userland services if you choose to.

Startup: because the app runs elevated (`requireAdministrator`), it cannot use `HKCU\...\Run` (Windows suppresses UAC at logon). On launch it registers a logon Scheduled Task named `AcerPredatorTool` with highest privileges and `-hidden` (tray). Legacy Run-key entries are removed.

---

## Research tools (`Acer Predator Tool/tools/`)

Helpers for reverse-engineering and verification. **Not part of the app UI.** Many require Administrator.

**Index:** [`Acer Predator Tool/tools/TOOLS.md`](Acer%20Predator%20Tool/tools/TOOLS.md) — how to run verification and research scripts.  
**GPU product spec:** [`GPU_CONTROL.md`](Acer%20Predator%20Tool/tools/GPU_CONTROL.md) · **Lighting:** [`README_LIGHTING_RESEARCH.md`](Acer%20Predator%20Tool/tools/README_LIGHTING_RESEARCH.md)

**Use at your own risk.** Prefer read-only scripts first. Close `AcerPredatorTool` / games before SET / mux tests. Dated capture folders and `*.log` dumps are not kept in the tree — re-run scripts to regenerate.

### Fan / EC (read-only)

| Script | Purpose |
|--------|---------|
| `ec_wmi_fan_snapshot.ps1` | WMI fan % / RPM / temps snapshot |
| `ec_dump_readonly.ps1` | Read-only EC memory dump helper |
| `ec_diff_dumps.ps1` | Diff two EC dumps |
| `README_EC_RESEARCH.md` | Fan-step / EC notes |

### GPU / DDS

| Script / entry | Purpose |
|----------------|---------|
| `TOOLS.md` | How to run tests |
| `GPU_CONTROL.md` | Product + lab spec (PnP + AppSync) |
| `switch_gpu.cmd` | Endurance / Standard via PnP |
| `verify_gpu_combo/` | Elevated PnP+DDS combo self-test |
| `REFERENCE_GPU_TARGET.md` / `REFERENCE_GPU_KEYS.md` | HIT summary / key catalogue |
| `README_GPU_RESEARCH.md` | Historical research notes |
| `inject_dds/` | Lab sources; product embeds `hook_cef_port.dll`, `dds_native_helper.dll` |

### Lighting / logo

| Script | Purpose |
|--------|---------|
| `README_LIGHTING_RESEARCH.md` | AcerLightingService / OpenRGB / cold-wake |
| `probe_enable_lighting_svc.ps1` | Start `AcerLightingService` |
| `restart_lighting.cmd` | Restart service |
| `probe_logo_*.ps1` | Lid logo WMI probes |
| `verify_selfsufficiency.ps1` | Lighting self-check |

---

## Credits & materials

### Upstream / fork

- **[supesonly/Acer-P-Helper](https://github.com/supesonly/Acer-P-Helper)** — Acer Predator Tool / Predator Control (this project’s GitHub home)
- **[Paulrod20/p-helper](https://github.com/Paulrod20/p-helper)** — original Windows Predator Control app this lineage started from

### WMI / fan / Predator Sense reverse-engineering references

- **[linux `acer-wmi`](https://github.com/torvalds/linux/blob/master/drivers/platform/x86/acer-wmi.c)** — upstream kernel documentation of Predator v4 sensors, fan behaviour, and `SetGamingFanSpeed` / `GetGamingFanSpeed`
- **[0x7375646F/Linuwu-Sense](https://github.com/0x7375646F/Linuwu-Sense)** — community Predator/Nitro WMI module (fan encoding, custom mode sequences)
- **[JafarAkhondali/acer-predator-turbo-and-rgb-keyboard-linux-module](https://github.com/JafarAkhondali/acer-predator-turbo-and-rgb-keyboard-linux-module)** — earlier reverse-engineering of Predator Sense WMI/RGB/turbo paths
- **[rafradek/Acer-Predator-Scripts](https://github.com/rafradek/Acer-Predator-Scripts)** — lightweight PredatorSense-oriented scripting / debloat approach
- **[hammadzaigham/PreySense](https://github.com/hammadzaigham/PreySense)** — G-Helper fork for Predator; AcerService `GPU_MODE` / PnP GPU paths used by `probe_gpu_preysense.ps1`

Official Acer PredatorSense / IntelliSense remain the proprietary reference for behaviour; this project is independent and unsupported by Acer.

NVIDIA Advanced Optimus / DDS Display Mode is set by the app through **NvAppSyncProxy** (no public NVAPI). AcerService `GPU_MODE` is not used on PHN18.

---

## Strictly not for sale

Free, open-source community software. **Do not sell** this software or bundle it commercially. If you paid for it, you were scammed — report the seller.

---

## License

[MIT](LICENSE) — same spirit as the upstream p-helper project; respect licenses of linked third-party materials when reusing their code.
