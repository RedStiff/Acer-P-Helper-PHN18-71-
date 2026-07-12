# EC / fan research tools (read-only)

Target model: **Acer Predator PHN18-71** (Predator Sense v4 WMI).

## Why

Custom fan control via `AcerGamingFunction.SetGamingFanSpeed` accepts 0–100%, but firmware applies **~10% duty bands**. These scripts help find whether a finer EC register exists (for later comparison with official Acer IntelliSense / PredatorSense v5), **without writing** to the EC from our app.

## Scripts

| Script | Purpose |
|--------|---------|
| `ec_wmi_fan_snapshot.ps1` | Safe WMI snapshot: requested fan %, temps, RPM |
| `ec_dump_readonly.ps1` | Session folder + optional RWEverything **read/dump** attempts |
| `ec_diff_dumps.ps1` | Diff two dump folders/files for changed bytes |

## Suggested workflow

1. Run Acer Predator Tool as Admin → **Custom** fans.
2. `.\ec_wmi_fan_snapshot.ps1 -Label cpu40_gpu40`
3. `.\ec_dump_readonly.ps1` (needs [RWEverything](https://rweverything.com/) `Rw.exe`, or save EC table from GUI into the session folder).
4. Change to 41% / 50% and repeat.
5. `.\ec_diff_dumps.ps1 -Left <dirA> -Right <dirB>`

If 40% vs 41% dumps match and 40% vs 50% differ, that confirms decade quantization in EC RAM as well as in RPM.

## Safety

- These helpers are intended for **read-only** capture.
- Do **not** paste write commands (`WEC`, random port writes) unless you have a verified PHN18-71 map.
- RWEverything uses a kernel driver that Windows may block; that is a security trade-off, separate from EC write risk.
