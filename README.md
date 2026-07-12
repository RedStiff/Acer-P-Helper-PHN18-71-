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
| **Keyboard RGB** | Works | Effects, brightness, speed; solid + 4-zone static; live colour apply |
| **Lid logo backlight** | Works* | Static colour, brightness, on/off (Nekro `SetGamingLEDColor` path). Breathing/Neon **not** available on this chassis via WMI |
| **Graphics indicator** | Works* | **Read-only** Integrated / Discrete panel-owner detection (DDS result). Does **not** switch GPUs. Auto is not shown (undetectable) |
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

### Graphics (panel) — read-only on PHN18

On PHN18 the internal panel mux is **NVIDIA Advanced Optimus / DDS**. There is **no public NVAPI** to set Display Mode, and Acer `SetGamingMiscSetting` does **not** change the panel owner (confirmed by probe scripts).

The app therefore:

- **Detects** whether the primary desktop is on **Intel** (Hybrid / Optimus / Automatic-idle) or **NVIDIA** (Discrete / NVIDIA GPU only)
- **Does not** switch modes — change Display Mode in **NVIDIA Control Panel** if needed
- Does **not** show Auto — Automatic vs Optimus cannot be distinguished via public APIs

---

## What does not work / limitations

| Item | Status |
|------|--------|
| **GPU mux switching** (Integrated / Auto / Discrete) | **Not supported** — indicators only; use NVIDIA Control Panel → Display Mode |
| **Logo Breathing / Neon effects** | Not available via WMI on PHN18 |
| **1% fan granularity** | Not available via official WMI; raw EC write not enabled |
| **GPU / CPU overclock** | Not implemented (PredatorSense OC tabs) |
| **LCD override, USB charging, boot animation** | Not implemented |
| **Battery calibration** | Not implemented |
| **Per-key RGB** | Not supported — four-zone / effect modes only |
| **All Predator / Nitro models** | Only validated on **PHN18-71** (Windows 11); other models may partially work or fail |
| **Non-admin use** | Requires **Administrator** (WMI write methods) |

---

## Requirements

- Acer Predator laptop exposing `root\WMI` → `AcerGamingFunction`
- Windows 10 or 11
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- Run as **Administrator**

---

## Download / build

**Download:** [Releases](../../releases) — run `AcerPredatorTool.exe` as administrator.

**From source:**

```text
dotnet build "Acer Predator Tool/AcerPredatorTool.csproj" -c Release
```

Or open `AcerPredatorTool.slnx` in Visual Studio.

Output: `Acer Predator Tool/bin/Release/net10.0-windows/AcerPredatorTool.exe`

Published package layout (example): `release/AcerPredatorTool-1.0.0/`

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

Startup: the app registers `AcerPredatorTool` under `HKCU\...\Run` on first launch (`-hidden` for tray start).

---

## Research tools (`Acer Predator Tool/tools/`)

Helpers for reverse-engineering and verification. **Not part of the app UI.** Many require Administrator. Some `-Apply` / NVCP flows can blank the screen briefly.

**Use at your own risk.** Prefer read-only scripts first. Close `AcerPredatorTool` / games before SET / mux tests.

### Fan / EC (read-only)

| Script | Purpose |
|--------|---------|
| `ec_wmi_fan_snapshot.ps1` | WMI fan % / RPM / temps snapshot |
| `ec_dump_readonly.ps1` | Read-only EC memory dump helper |
| `ec_diff_dumps.ps1` | Diff two EC dumps |
| `README_EC_RESEARCH.md` | Fan-step / EC notes |

### GPU / DDS mux research

| Script / entry | Purpose |
|----------------|---------|
| `probe_gpu_auto.cmd` | Full suite: fingerprint, NV registry, NvCpl Hybrid GET, Acer baseline, SysInfo, WMI DryRun |
| `probe_gpu_auto_apply.cmd` | Same + focused Acer misc SET (already: **no mux HIT** on PHN18) |
| `probe_gpu_nvcp_capture.cmd` | Interactive BEFORE/AFTER capture while **you** change NVCP Display Mode |
| `INSTRUCTIONS_GPU_NVCP_CAPTURE.md` | Step-by-step for NVCP capture |
| `README_GPU_RESEARCH.md` | GPU research summary |
| `probe_gpu_fingerprint.ps1` | Panel owner + nvidia-smi (no admin) |
| `probe_gpu_nv_registry.ps1` | NVIDIA registry dump |
| `probe_gpu_nvcpl_hybrid.ps1` | `GetHybridMode` / Muxd probe (no public Set) |
| `probe_gpu_baseline.ps1` | Acer WMI inventory |
| `probe_gpu_sysinfo_scan.ps1` | SysInfo / ProfileSetting GET |
| `probe_gpu_wmi_bruteforce.ps1` | Misc candidate DryRun / `-Apply` |
| `_gpu_common.ps1` | Shared helpers for GPU probes |

### Lighting / logo probes

| Script | Purpose |
|--------|---------|
| `probe_logo_ask.ps1` | Interactive lid-logo colour probes |
| `probe_logo_nekro_confirm.ps1` | Nekro `SetGamingLEDColor` confirm path |
| `probe_logo_effects_ask.ps1` | Effect probes (Breathing/Neon — not effective on PHN18) |
| `verify_selfsufficiency.ps1` | Lighting self-check helper |

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

Official Acer PredatorSense / IntelliSense remain the proprietary reference for behaviour; this project is independent and unsupported by Acer.

NVIDIA Advanced Optimus / DDS Display Mode is controlled by NVIDIA Control Panel; NVIDIA has stated there is no public NVAPI for that setting.

---

## Strictly not for sale

Free, open-source community software. **Do not sell** this software or bundle it commercially. If you paid for it, you were scammed — report the seller.

---

## License

[MIT](LICENSE) — same spirit as the upstream p-helper project; respect licenses of linked third-party materials when reusing their code.
