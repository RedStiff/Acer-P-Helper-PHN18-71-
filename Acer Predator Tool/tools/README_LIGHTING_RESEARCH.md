# Acer Lighting research (PHN18)

Оновлено: **2026-07-22** — product = pure WMI; cold-EC RE **parked** until reproducible case.

Мета: статична RGB клавіатури / logo через `AcerGamingFunction` WMI, **без** PredatorSense, **без** `AcerLightingService`, **без** OEM `OpenRGB.exe` / `AcerECKeyboardController`.

**Статус:** звичайний шлях закритий (`verify_selfsufficiency` OK). Дослідження справжнього cold EC відкладено — немає відтворюваного скидання активації (reboot/BIOS не чистять).

---

## Product policy

| Залежність | Статус |
|------------|--------|
| `AcerLightingService` | Не стартуємо, не потрібен продукту |
| OEM OpenRGB / AcerEC* DLL (DriverStore) | Не викликаємо |
| `root\WMI\AcerGamingFunction` | Єдиний шлях освітлення |

Linux (Linuwu-Sense) керує static тим самим ACPI/WMI буфером `SetGamingKBBacklight` — без OpenRGB. На Windows продукт робить те саме.

---

## Поведінка / «активація» static

| Стан | Effects | Static |
|------|---------|--------|
| Після успішного static (звичайний reboot / BIOS reset) | WMI | WMI — **профіль лишається** |
| Після критичного збою EC (BSOD тощо) | WMI | потрібен `RunStaticEcRecovery` |

Підтверджено користувачем (2026-07-22):

- Static треба «підняти» **один раз**; далі працює без Acer SW.
- **Reboot і скидання BIOS не скидають** цю активацію.
- Губиться лише після **критичного збою** (ймовірно корупція / реініт volatile або NVRAM EC).
- `verify_selfsufficiency.ps1` step «DARK» (`mode 0` **без** `[8]=3`) лише гасить підсвітку — це **не** скидання активації. Тому тест проходить на вже «теплому» EC.

Гіпотеза: профіль RGB (або прапорець static-ready) пишеться в **пам’ять EC**, яка живе від RTC/вбудованої батареї й не чиститься soft reboot / UEFI defaults. AcerLightingService/OpenRGB лише один раз проганяють той самий WMI-шлях; вони не обов’язкові після першого успішного static.

### Як навмисне відтворити «холодний» EC (lab)

Надійного WMI «de-activate» поки немає. Кандидати (від м’якого до жорсткого):

1. **EC power drain (G3):** Shutdown → витягнути зарядку → затиснути power ~40–60 с → почекати кілька хвилин → увімкнути. Часто скидає volatile EC RAM; NVRAM може лишитись.
2. **Дочекатись реального cold** після BSOD / hard hang (як у прод-сценарії).
3. Не плутати з DARK у verify — після DARK static знову запалюється саме тому, що активація вже є.

Product: завжди викликати `RunStaticEcRecovery` на static apply (дешево) — покриває рідкий post-crash випадок без Acer SW.

---

## Поточний стан у застосунку

| Шлях | Реалізація |
|------|------------|
| Effects | `BeginLightingUpdate` + `SetGamingKBBacklight` mode≠0 |
| Static / 4-zone | `RunStaticEcRecovery` — лише WMI, 2 rounds |
| `SetGamingLED` / `SetGamingKBBacklight` | **UInt8Array MAX=16** (`SendByteArrayCommand`) |
| Lid logo | Nekro `SetGamingLEDColor` |
| Acer lighting userland | **Не використовується** |

Static payload Linuwu: `{mode, speed, brightness, 0, dir, R, G, B, **3**, **1**, …}` — без `[8]=3` firmware трактує mode 0 як off.

Cold round (спрощено): probe Get* → SetGamingLED → SetGamingLEDColor → SetGamingLEDBehavior(0x07) → KBBacklight mode0 RGB=0 → SetGamingRgbKb×4 → SetGamingLEDBehavior(all zones).

---

## MOF / WMI

| Method | `gmInput` |
|--------|-----------|
| `SetGamingLED` / `SetGamingKBBacklight` | UInt8Array MAX=16 |
| `SetGamingLEDColor` / `SetGamingRgbKb` / `SetGamingLEDBehavior` | UInt64 |
| Get* | UInt32 / UInt64 |

---

## Lab

```powershell
# WMI-only after visual DARK (activation already present on normal boots):
.\verify_selfsufficiency.ps1

# Two-round WMI static sequence only:
.\replay_service_static_init.ps1
```

Historical RE (не product): `probe_static_wake_openrgb.ps1`, `probe_ec_diff.ps1` — порівняння з OEM OpenRGB detect. Не підключати до застосунку.

Індекс: [`TOOLS.md`](TOOLS.md).
