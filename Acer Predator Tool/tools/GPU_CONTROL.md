# Керування GPU на PHN18-71 (без Acer services)

Документ описує **усі перевірені** шляхи перемикання графіки на Acer Predator **PHN18-71**, реєстри/події/сервіси, залежності стеку NVIDIA, підводні камені та ризики.

Оновлено: **2026-07-21** — **v1.0.1.1** product: PnP + `NvAppSyncProxy` DDS у UI; combo 8/8; mem-scan rediscover.

Пов’язані файли:

| Файл | Роль |
|------|------|
| [`GPU_CONTROL.md`](GPU_CONTROL.md) | цей документ (операційна специфікація) |
| [`REFERENCE_GPU_TARGET.md`](REFERENCE_GPU_TARGET.md) | коротке резюме HIT / відхилених шляхів |
| [`REFERENCE_GPU_KEYS.md`](REFERENCE_GPU_KEYS.md) | історичні Acer/BIOS/WMI ключі |
| [`switch_gpu.ps1`](switch_gpu.ps1) | Endurance / Standard (PnP) |
| [`switch_dds.ps1`](switch_dds.ps1) | Ultimate / Optimus / Automatic (DDS) |
| [`_gpu_common.ps1`](_gpu_common.ps1) | `Get-GpuFingerprint` / `MuxSignature` |
| [`inject_dds\`](inject_dds/) | launcher + CDP `SetDDSState` |

---

## 1. Два незалежні шари керування

На PHN18 є **два різні** механізми. Їх не можна плутати.

```text
A. Windows PnP (Endurance / Standard)
   - Прибирає / повертає dGPU як пристрій у системі
   - Скрипт: switch_gpu.ps1  |  C#: GpuPnpController

B. NVIDIA Advanced Optimus / DDS (Ultimate / Optimus / Automatic)
   - Перемикає MUX панелі: Intel <-> NVIDIA
   - Product: NvAppSyncProxy + SessionFilter (COM, без App.exe)
   - Lab legacy: switch_dds.ps1 / CDP SetDDSState (вимкнено в UI)
```

| PreySense / NVCP | Ефект | Шлях | Скрипт |
|------------------|-------|------|--------|
| **Endurance** | dGPU вимкнений у Windows (iGPU-only) | PnP disable `VEN_10DE` + stop NV container | `switch_gpu.ps1 -Mode Endurance` |
| **Standard** | dGPU знову присутній (гібрид) | PnP enable + restart NV container | `switch_gpu.ps1 -Mode Standard` |
| **Ultimate** / NVIDIA GPU only | Панель на dGPU | DDS `MuxState=2` | `switch_dds.ps1 -Mode NvidiaOnly` |
| Optimus | Панель на iGPU (форс) | DDS `MuxState=1`, `auto=false` | `switch_dds.ps1 -Mode Optimus` |
| Automatic | DDS allow-list auto | DDS `MuxState=1`, `auto=true` | `switch_dds.ps1 -Mode Automatic` |

**Важливо:** Endurance ≠ Optimus. Endurance **вимикає пристрій** NVIDIA; Optimus лишає dGPU в системі, але панель веде Intel.

---

## 2. Метрика HIT: `MuxSignature`

Єдиний стабільний критерій живого перемикання панелі:

```text
MuxSignature = owner={Intel|NVIDIA}|nv_display={Enabled|Disabled}
```

Реалізація: `Get-GpuFingerprint` у `_gpu_common.ps1`.

| Поле | Джерело | Значення |
|------|---------|----------|
| `owner` | `EnumDisplayDevices` — primary adapter Kind | `Intel` / `NVIDIA` / `NONE` |
| `nv_display` | `nvidia-smi … display_active` | `Enabled` / `Disabled` |

Типові idle-стани:

| Режим | MuxSignature |
|-------|--------------|
| Optimus / Automatic (idle) | `owner=Intel\|nv_display=Disabled` |
| NVIDIA GPU only (Ultimate) | `owner=NVIDIA\|nv_display=Enabled` |
| Endurance (dGPU PnP off) | Intel owner; NVIDIA PnP відсутній / Error |

**Не використовувати** для HIT рішення `nv_power` / `nv_clock` — вони плавають у idle і дають хибні спрацьовування. Повне `Signature` лишається лише для телеметрії.

**Optimus vs Automatic** при idle мають **однаковий** `MuxSignature`. Розрізняти лише через ACE `InternalMuxIsAutomaticMode` (див. §4).

---

## 3. App UI mapping (Acer Predator Tool)

Секція GRAPHICS у головному вікні (і tray → Graphics):

| Ряд UI | Кнопка | Скриптовий / апаратний режим |
|--------|--------|------------------------------|
| **GPU DEVICE** | **iGPU Only** | Endurance — PnP disable `VEN_10DE` + stop NVDisplay container |
| **GPU DEVICE** | **Hybrid** | Standard — PnP enable + restart NVDisplay container |
| **DISPLAY MODE** | **Optimus** | DDS `{ bIsAutomatic:false, MuxState:1 }` |
| **DISPLAY MODE** | **Auto** | DDS `{ bIsAutomatic:true, MuxState:1 }` |
| **DISPLAY MODE** | **NVIDIA** | DDS `{ bIsAutomatic:false, MuxState:2 }` (Ultimate) |

Реалізація в C#: `GpuControlService` + `GpuPnpController` + `DdsControl` → `DdsAppSync` (COM).  
App inject/CDP — legacy (`AllowLegacyNvidiaAppHost=false`).

Правила UX:

- Перед **iGPU Only** — confirm dialog.
- Поки активний **iGPU Only**, кнопки DISPLAY MODE disabled (немає dGPU для DDS).
- Клік DISPLAY MODE при вимкненому dGPU спочатку робить Hybrid (+ wait UXD), потім DDS.
- Обов’язкові ToolTip на кожній кнопці.
- Tool **requireAdministrator** (PnP + SeDebug mem-scan).

CLI / PowerShell (нижче) лишаються для діагностики поза UI.  
Combo self-test: `tools\verify_gpu_combo` (elevated) → **8/8 PASS** (2026-07-21).

### 3.1 PnP — Endurance / Standard

Потрібні права **Administrator**.

```powershell
cd "e:\Projects\Acer Predator Tool\Acer Predator Tool\tools"

.\switch_gpu.ps1 -Mode Status
.\switch_gpu.ps1 -Mode Endurance          # підтвердити YES
.\switch_gpu.ps1 -Mode Standard
.\switch_gpu.ps1 -Mode Cycle -Force       # self-test
```

Механіка (`_acer_service.ps1`):

1. `Get-PnpDevice -Class Display` де `InstanceId -match 'VEN_10DE'`
2. `Disable-PnpDevice` / `Enable-PnpDevice`
3. `Stop-Service` / `Restart-Service` **`NVDisplay.ContainerLocalSystem`**

### 3.2 DDS — NvidiaOnly / Optimus / Automatic

```powershell
.\switch_dds.ps1 -Mode NvidiaOnly
.\switch_dds.ps1 -Mode Optimus
.\switch_dds.ps1 -Mode Automatic
.\switch_dds.ps1 -Mode Get                # лише GetDDSState
```

Пайплайн `switch_dds.ps1`:

1. Перевірити named event UXD (див. §5). Якщо немає — elevated restart сервісів.
2. Підняти CDP-хост NVIDIA App на **інтерактивному** столі (не private desktop — ламає single-instance App для користувача):
   - App: `NvidiaAppCdpHost` (CreateProcess SUSPENDED + inject, без окремого `launcher.exe`)
   - Tools: `inject_dds\launcher.exe` (те саме для скриптів)
   - `SW_HIDE` + inject `hook_cef_port.dll` → `remote_debugging_port = 9333` (CEF settings offset **336**)
   - Якщо CDP уже на `:9333` — reuse без Kill/Launch
   - **REJECTED:** desktop `NvCdpSilent` — App стає недоступним з трею
3. CDP `http://127.0.0.1:9333` → WebSocket з **`suppress_origin=True`** (інакше 403).
4. `window.cefQuery` → JSON IPC:

```json
{
  "command": "QUERY_IPC_EXTENSION_MESSAGE",
  "system": "CrimsonNative",
  "module": "NvCplDisplayPlugin",
  "method": "SetDDSState",
  "payload": { "bIsAutomatic": false, "MuxState": 2 }
}
```

| UI mode | `bIsAutomatic` | `MuxState` |
|---------|----------------|------------|
| NVIDIA GPU only | `false` | `2` (`State_DGPU`) |
| Optimus | `false` | `1` (`State_IGPU`) |
| Automatic | `true` | `1` |

`GetDDSState` повертає зокрема: `bIsSupported`, `bIsAutomatic`, `MuxState`, `kmdResponse`, `srType`, `_return_status`.

Успішний apply у логах UXD:

```text
NvCplApiIsUxdServiceRunning : UXD service is running.
NvCplApiSetSetting : Call NVCPLAPI_SETTINGS_DDS_INTERNAL_MUX_STATE.
```

Після успішного `SetDDSState` NVIDIA App UI показує той самий активний Display Mode / адаптер — IPC і драйверні шляхи спільні.

---

## 4. Реєстри (NVIDIA / Windows)

### 4.1 ACE Persistence (дзеркало DDS, НЕ live-control)

```text
HKLM\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE\
```

| Value (REG_DWORD) | Роль | Примітка |
|-------------------|------|----------|
| **`InternalMuxState`** | `1` = IGPU/Optimus-подібний, `2` = DGPU | Головний стан mux |
| **`InternalMuxIsAutomaticMode`** | `0` = Optimus/форс, `1` = Automatic | Єдиний idle-дискримінатор Auto vs Optimus |
| **`ACESwitchedI2D`** | `0` / `1` | Зазвичай `1` після переходу Intel→NVIDIA |
| `MuxChangeNotif` | імпульс нотифікації | Запис + pulse **не** перемикає mux |
| `MuxTrayIcon` | іконка tray | UI |
| `ShowBlockingApps` | DDS blocking apps UI | UI |
| `PowerModeMask` | маска power | спостережуване |
| `DpiScalePercent` | DPI scale | спостережуване (напр. 150) |

Карта режимів (після **реального** DDS apply):

| Режим | State | Auto | I2D | MuxSignature (idle) |
|-------|------:|-----:|----:|---------------------|
| Automatic | 1 | 1 | 0 | Intel / Disabled |
| Optimus | 1 | 0 | 0 | Intel / Disabled |
| NVIDIA GPU only | 2 | 0 | 1 | NVIDIA / Enabled |

**Проба запису** (`probe_gpu_ace_write.ps1`): значення в реєстрі зберігаються (`ACE_WRITE_PERSISTED=True`), але **`MUX_CHANGED=False`**.  
Висновок: ACE — **persistence / mirror** після драйверного apply, не вхід керування.

Альтернативний шлях (не використовувати як primary):

```text
HKLM\SYSTEM\CurrentControlSet\Services\nvlddmkm\Parameters\Global\NvHybrid\Persistence\
```

(зустрічається у рядках `nvcpl.dll`; на PHN18 робоче дзеркало — `\Global\NvHybrid\Persistence\ACE`).

### 4.2 Display class — `PipeConfig`

```text
HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0001\PipeConfig
```

Спостереження з NVCP capture:

| Стан панелі | `PipeConfig` (байти) |
|-------------|----------------------|
| Intel / Optimus | `01 00 00 00` |
| dGPU / NVIDIA only | `00 00 00 00` |

Також дзеркало, не самостійний SET-шлях.

Індекс `\0001` може відрізнятись між машинами — шукати NVIDIA adapter instance у класі `{4d36e968-…}`.

### 4.3 Acer capability flags (лише індикатори)

```text
HKLM\SOFTWARE\OEM\AcerAgentService\AdvanceSettings
```

| Value | Observed PHN18 | Значення |
|-------|----------------|----------|
| `discrete_gpu_support` | `0` | Agent каже «discrete mux UI unsupported» |
| `dgpu_mode_capability` | `7` | capability bitmask (біти TBD) |

Запис цих ключів **не** перемикає DDS/PnP.

### 4.4 PreySense / AcerService (відхилено на PHN18)

| Шлях | Статус |
|------|--------|
| TCP `GPU_MODE` на **:46933** | Немає протоколу ACER; сервіс на **:15152** (HTTPS PWA) |
| BIOS `AcerBiosConfigurationTool` Data\[80\] | `InvalidParameter` |
| WMI `SetGamingMiscSetting` GPU candidates | `WMI_HITS=0` для mux |

Деталі: [`REFERENCE_GPU_KEYS.md`](REFERENCE_GPU_KEYS.md).

---

## 5. Named events і runtime-залежності UXD

### 5.1 Критична подія

```text
Local\UXDServiceStarted-D40E81C4-06EF-454A-9E81-1F4D55CEBD57
```

- Namespace **`Local\`** → видима лише в тій же Windows session, що й NVIDIA App.
- Створюється стеком **UXD / nvxdapix** (`UxdService`), коли UX Driver реально піднятий.
- `NvCplApiIsUxdServiceRunning` робить `OpenEvent` на цю назву.

Якщо події немає → plugin `NvCplDisplayPlugin` у стані **failed**, `SetDDSState`/`GetDDSState` не працюють нормально.

### 5.2 Супутні події (спостережувані)

| Event | Роль |
|-------|------|
| `Local\NvAppUXDService-CD845085-3F0C-41E7-A6D7-7537A8040FE9` | частковий UXD у процесі App |
| `Local\NvXDSyncEngineStop-…` / `Local\NvXDSyncPluginStop-…` | session sync plugin живий |
| `Local\NvAppNvShowACEMuxTrayIcon-…` / `Hide…` | tray UI |
| `Global\NvXDSync-{3FE50880-8CA3-4FE1-A0B7-F11661CDD21E}-*` | sync між session/system (не завжди відкривається з user) |

### 5.3 Сервіси / контейнери

| Service / process | Роль |
|-------------------|------|
| **`NVDisplay.ContainerLocalSystem`** | Display.NvContainer; плагін **`NvXDCore.dll`** (system UXD) + session **`nvxdsyncplugin.dll`** (`StartUxdService`) |
| **`NvContainerLocalSystem`** | NVIDIA App container; також має `NvCpl\NvXDCore.dll` |
| Session `nvcontainer` (User / SPUser) | user plugins NVIDIA App |
| `NVIDIA App.exe` (CEF) | UI + host для `NvCplDisplayPlugin` / cefQuery |

Шлях Display container (типово):

```text
C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_*\Display.NvContainer\
```

Плагіни:

```text
plugins\LocalSystem\NvXDCore.dll
plugins\Session\nvxdsyncplugin.dll
```

### 5.4 Логи для діагностики

| Лог | Що шукати |
|-----|-----------|
| `%LOCALAPPDATA%\NVIDIA Corporation\NVIDIA App\UXD\Log.NVIDIA App.exe.log` | `UXD service is running` / `Open UXD service started event failed` / `NVCPLAPI_SETTINGS_DDS_INTERNAL_MUX_STATE` / `0x80070005` |
| `%ProgramData%\NVIDIA\NVDisplay.ContainerLocalSystem.log` | load `NvXDCore`, session plugins |
| `%ProgramData%\NVIDIA Corporation\NVIDIA App\UXD\Log.nvcontainer.exe.log` | system-side UXD errors |
| `%LOCALAPPDATA%\NVIDIA Corporation\NVIDIA App\CxNative_NVIDIA App.log` | CrimsonNative / plugin host |

---

## 6. Ланцюг виклику DDS (від UI до драйвера)

```text
NVIDIA App (CEF JS)
  → window.cefQuery
  → CrimsonNative / NvCplDisplayPlugin
  → GetDDSState / SetDDSState
  → nvcpl.dll : NvCplApiSetSetting(NVCPLAPI_SETTINGS_DDS_INTERNAL_MUX_STATE)
  → nvxdapix.dll / UXD Shim (потрібен UXDServiceStarted)
  → NvXDCore (NVDisplay.Container) + nvxdsync (session)
  → KMD nvlddmkm (Advanced Optimus mux)
  → оновлення панелі + запис ACE Persistence
```

Приватний рядок у `nvxdapix.dll`: `NvAPI_DISP_SetDisplayMux` (публічного ID у таблицях NVAPI немає; прямий SET через public NVAPI на PHN18 **не працює**).

---

## 7. Підводні камені

### 7.1 UXD «мертвий» після агресивних рестартів / інжектів

Симптоми:

- `Open UXD service started event failed with error: 2`
- UXD log: `HResult=0x80070005` на `ProcessGetSettings`
- `GetDDSState` → `bIsSupported:false`, `MuxState:0`, але IPC `_return_status: Success`
- NVIDIA App Display Mode сірий / «налаштування недоступні»

Лікування:

```powershell
# Admin
Restart-Service NVDisplay.ContainerLocalSystem -Force
Restart-Service NvContainerLocalSystem -Force
# зачекати ~5–10 с, перевірити OpenEvent на UXDServiceStarted
```

`switch_dds.ps1` робить це через UAC (`-Verb RunAs`), якщо події немає.

### 7.2 НЕ підробляти `UXDServiceStarted`

`CreateEvent` з тією ж назвою обходить `IsUxdServiceRunning`, plugin відповідає Success, але:

- `bIsSupported:false`
- mux / ACE / панель **не змінюються**

Це найнебезпечніший false-positive у всьому пайплайні.

### 7.3 CDP / CEF

| Пастка | Деталь |
|--------|--------|
| CLI `--remote-debugging-port` | NVIDIA часто ігнорує (`command_line_args_disabled`) |
| Рішення | hook `cef_initialize`, offset **336** = port |
| WebSocket Origin | без `suppress_origin=True` → **403** |
| Frida spawn | CDP можливий, але частіше ламає init плагінів; production = `launcher.exe` |
| Python `WriteProcessMemory` inject | на цій системі давав `ERROR_NOACCESS (998)`; нативний `launcher.exe` — OK |
| Кілька `NVIDIA App.exe` | перед launch робити `taskkill /IM "NVIDIA App.exe"` |

### 7.4 Endurance ламає DDS (і handle)

Після PnP disable dGPU:

- DDS/UXD недоступні; `DisplayMode=Unknown`
- Старий DDS handle часто stale → `EAB00003`
- Перед DISPLAY MODE: Hybrid + `EnsureUxdHealthy` + mem-scan rediscover (admin)
- Підтверджено Step 3: Endurance → ApplyDisplayMode(NVIDIA) → новий handle `{C4B3126E-…}` + ACE HIT

### 7.4b Step 3 combo (2026-07-21) — HIT

`verify_gpu_combo` elevated, шлях `GpuControlService` (без App.exe):

| Крок | Результат |
|------|-----------|
| Hybrid baseline | PASS |
| DDS Optimus → NVIDIA → Auto → Optimus | **4/4 ACE HIT** |
| Endurance iGPU Only | device=IgpuOnly, display=Unknown |
| Endurance → NVIDIA (auto Hybrid+DDS) | Hybrid + state=2; **новий handle** після mem-scan |
| Leave Optimus | PASS |
| **Разом** | **8/8** |

Фікс стабільності AppSync: не `FreeCoTaskMem` одразу після Set; не викликати empty Get / DoOp на hot path (heap `0xC0000374`).

### 7.5 Optimus vs Automatic

Idle `MuxSignature` однаковий. Перевіряти ACE:

```powershell
Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE' |
  Select InternalMuxState, InternalMuxIsAutomaticMode, ACESwitchedI2D
```

### 7.6 Session / integrity

- Події `Local\…` — per-session. Elevated admin vs Medium user — різні світи для Local events, якщо процеси в різних session.
- Робочий шлях підтверджено з **Medium** integrity + UAC лише на restart сервісів.

### 7.7 MessageBus напряму

Pipe `NvMessageBusBroadcast…` з чужого процесу — **ACL deny**. Тому використовується host NVIDIA App + CDP, а не прямий pipe-клієнт.

---

## 8. Ризики

| Ризик | Серйозність | Коментар |
|-------|-------------|----------|
| Чорний екран / коротке гасіння при mux switch | Середній | Нормальна поведінка Advanced Optimus; не переривати mid-switch |
| Втрата дисплея при Endurance + зламаний iGPU шлях | Високий | Завжди мати план відновлення: Safe Mode / enable PnP NVIDIA |
| Ігри/CUDA не бачать dGPU у Endurance | Очікувано | Це і є мета режиму |
| Inject у NVIDIA App (CDP hook) | Середній | Може тригерити AV/EDR; не залишати suspended процеси |
| Віддалений CDP :9333 | Середній | Порт лише localhost; не експонувати назовні; після роботи можна закрити App |
| Запис ACE вручну | Низький для mux, середній для плутанини | Не перемикає екран, але плутає телеметрію/UI індикатори |
| Подвійний NvXDCore (App container + Display container) | Середній | Конфлікт/access denied після «кривих» рестартів — лікується restart обох LS-сервісів |
| Версії драйвера / NVIDIA App | Високий long-term | Offsets CEF (336), GUID події, schema IPC можуть змінитись після оновлення |
| Fake UXD event у проді | Високий (логічний) | Тихий NO_HIT під виглядом Success |
| OC misc WMI (`0x05`/`0x07`) | Високий | **Не** brute-force (див. KEYS) |

Рекомендації безпеки для продукту:

1. Не викликати AcerService для GPU на PHN18.
2. UI кнопки Endurance/Standard/Ultimate вести лише на `switch_gpu` / `switch_dds`.
3. Перед DDS — перевірка `UXDServiceStarted` + `GetDDSState.bIsSupported==true`.
4. Після apply — обов’язково `MuxSignature` (і ACE для Auto).
5. Не писати ACE як «перемикач».
6. Не підробляти named events.

---

## 9. Відхилені шляхи (не використовувати)

| Шлях | Результат |
|------|-----------|
| AcerService `GPU_MODE` :46933 | Немає ACER protocol на PHN18 |
| BIOS offset 80 | InvalidParameter |
| Acer `SetGamingMiscSetting` GPU IDs | Немає mux HIT |
| Запис ACE + `MuxChangeNotif` | Persistence only |
| Public `NvAPI_SetHybridMode` | `0xFFFFFF98` not supported |
| `nvcpl.dll!NvCplSetDisplayMode` | `rc=1`, NO_HIT |
| Classic UIA по CEF Display Mode | Немає стабільних radio nodes / error dialog на Optimus |
| Прямий MessageBus pipe | ACL deny |
| Fake `UXDServiceStarted` | Success + `bIsSupported:false` + NO_HIT |

---

## 10. Швидка діагностика

```powershell
. .\tools\_gpu_common.ps1
(Get-GpuFingerprint).MuxSignature

Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE' |
  Select InternalMuxState, InternalMuxIsAutomaticMode, ACESwitchedI2D

Get-Service NVDisplay.ContainerLocalSystem, NvContainerLocalSystem
Get-PnpDevice -Class Display | ? { $_.InstanceId -match 'VEN_10DE' } | ft Status, FriendlyName

# UXD event (має бути ненульовий handle)
Add-Type @'
using System; using System.Runtime.InteropServices;
public class Uxd { [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)]
 public static extern IntPtr OpenEvent(uint a,bool b,string n); }
'@
[Uxd]::OpenEvent(0x100000,$false,'Local\UXDServiceStarted-D40E81C4-06EF-454A-9E81-1F4D55CEBD57')
```

Очікувано при здоровому DDS-стеку: сервіси Running, UXD event ≠ 0, `GetDDSState.bIsSupported=true`.

---

## 11. Карта файлів інструментів

| Компонент | Шлях |
|-----------|------|
| PnP switch | `switch_gpu.ps1` |
| DDS switch | `switch_dds.ps1` |
| Fingerprint | `_gpu_common.ps1` → `Get-GpuFingerprint` |
| PnP helpers | `_acer_service.ps1` → `Get/Set-NvidiaDisplayDeviceState`, `Set-NvidiaContainerService` |
| Native CDP launcher | `inject_dds\launcher.exe` + `hook_cef_port.dll` |
### Breakthrough: native `NvCplApiSetSetting(0x330)` + `Execute(0x10000)` (2026-07-20)

Frida in-process inside NVIDIA App (UXD context):

```text
NvCplApiSetSetting(1, hdr, 0x330, val16) → 0
NvCplApiExecute(0x10000, 0, -1)          → 0   // NVCPLAPI_SETTING_ALL (3 args — R8 required)
```

`val` (16 bytes), little-endian:

| Mode | bytes (first 8) | ACE |
|------|-----------------|-----|
| Optimus | `01 00 00 00 01 00 00 00` | state=1 auto=0 |
| NVIDIA only | `01 00 00 00 02 00 00 00` | state=2 auto=0 i2d=1 |
| Automatic | `01 01 00 00 01 00 00 00` | state=1 auto=1 |

Packing: byte0=`1`, byte1=`bIsAutomatic`, dword1=`MuxState`.

**Stock App hidden + inject (no CDP, no visible window)** — proven 2026-07-20:

1. `inject_native_dds.exe dgpu` → if needed, starts stock `NVIDIA App.exe` with `SW_HIDE`
2. Inject `dds_native_helper.dll` (pipe + continuous window hide worker)
3. Pipe `\\.\pipe\AcerPredatorDds` ← `igpu|auto|dgpu`

Cycle Optimus/Auto/dGPU/Auto/Optimus = **5/5 HIT**, `VISIBLE_WINDOWS=0` throughout.

Pitfall: `Execute(0x10000)` as 1-arg leaves garbage in R8 → UXD logs unknown setting / no mux change.

**Foreign process** (load `nvcpl.dll` from our exe): **NO_HIT** — needs in-App UXD (process can stay hidden; still must be NVIDIA App with live UXD).

App: `DdsNativeHost` (preferred) → CDP fallback via `NvidiaAppCdpHost`.

### Без NVIDIA App: NvXDCore / SyncProxy research (2026-07-20+)

**Fact:** `Local\UXDServiceStarted-…` лишається після `taskkill` усіх `NVIDIA App.exe` — UXD у `NVDisplay.Container`.

**Продуктове рішення:** шлях через NVIDIA App (inject / CDP / hidden host) — **відхилено** (блімає UI, flaky Optimus→Auto). Ціль — лише COM SyncProxy.

#### Доведені факти (RE)

| Факт | Деталі |
|------|--------|
| CoCreate `NvXDCore.SyncProxy` без App | **OK** → LocalServer `NVDisplay.Container.exe` |
| QI `IStateData` `{E6AB4158-38B8-4FDF-85CF-ADC2E9870970}` | **OK**, NumMethods=7 |
| Vtable `IStateData` | 3=`ProcessGetSettings`, 4=`ProcessGetHandleInfo`, 5=`ProcessSetSettings`, 6=`ProcessDoOperation` |
| Під час App HIT (Frida `ObjectStublessClient5/6`) | SetSettings + DoOperation на тому ж `this` |
| UXD SettingId DDS | **`0x7d`** (NvCpl `0x330` → UXD `0x7d`) |
| Handle (runtime) | `{AFE3D677-141F-424B-808D-340D9EC4ACD6}` (стабільний у сесії) |
| DescriptorRaw (stride 0x20) | `GUID handle` + `uint16 infoId` + `uint16 settingId` + `uint32 flags=4` + `void* data` |
| GenericData mux (`infoId=1`) | `type=3, size=4, value=MuxState` (1/2) |
| GenericData auto (`infoId=3`) | `type=5, size=1, value=bIsAutomatic` (0/1) |
| DoOperation GUID | `{D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD}` + `09 00 02 00` |
| 2-й аргумент (R8) Get/Set | **`NvAppStateDataSessionFilter`** CLSID `{3F6374C2-3540-476A-A123-D1DA2B6DDF86}` — живе з UXD-сесії App; **не** CoCreate під час apply |

#### SessionFilter: resolved creation (2026-07-20 evening)

| Крок | Результат |
|------|-----------|
| `CoGetClassObject` SessionFilter (`CLSCTX_INPROC_HANDLER`) | **OK** |
| `CreateInstance(null, …)` | `E_INVALIDARG` |
| `CreateInstance` потребує **aggregating outer**, який QI `ICallFactory` `{0000001B-…}` | SyncProxy COM-proxy підходить |
| `CoCreateInstance(SessionFilter, syncProxyUnk, 0x402, IID_IUnknown)` | **OK**, `vt=NVXDBat+…` / App `nvxdbat+0xf0100` |
| `AllocHGlobal` для DescriptorCollection | **heap corruption** у proxy stub |
| `Marshal.AllocCoTaskMem` для collection/items/GenericData | **GetSettings HR=0** (no crash) |
| `ProcessSetSettings` `flags=4` (як App HIT) | **`0xEAB00003`**, ACE без змін |
| `ProcessSetSettings` `flags=0` | **HR=0**, але ACE **без** HIT (noop / cache?) |
| `DoOperation` `{D812F4FF-…}+09 00 02 00` | HR=0, без mux HIT |

**Працюючий скелет no-App (ще без HIT):**

```text
1. CoCreate NvXDCore.SyncProxy
2. QI IStateData {E6AB4158-…}
3. CoCreate StateDataSessionFilter {5387A36B-…} (або App {3F6374C2-…})
     outer = SyncProxy IUnknown, ctx = 0x402 (INPROC_HANDLER|NO_CODE_DOWNLOAD)
4. Descriptor* лише через CoTaskMemAlloc
5. ProcessGetSettings / ProcessSetSettings / ProcessDoOperation
```

**Set semantics (no-App filter):**

| Call | HR | ACE |
|------|----|-----|
| `infoId=1` (mux) `flags=4` | `EAB00003` | no change |
| `infoId=1` (mux) `flags=0` | 0 | no change |
| `infoId=3` (auto) `flags=4` | 0 | no change |
| `DoOperation` | 0 | no change |
| Get any infoId (empty/obs handle) | 0 | `val=0` (не читає live ACE) |

Висновок: COM-шлях до SyncProxy **живий** (create filter + Get/Set без crash), але наш SessionFilter **не прив’язаний до UXD-сесії**, яка реально крутить mux. App HIT іде з «прогрітим» filter/handle після NvCpl/UXD init.

#### Filter lifecycle (Frida App, 2026-07-21)

| Факт | Деталі |
|------|--------|
| DDS handle **не** глобальний | У сесії App HIT: `{8FA752F3-70CA-49DC-BF80-58381E02E7F8}` (старий `{AFE3D677-…}` — stale) |
| App викликає через **`filter+0x18`** | secondary iface; Get=`vt[3]` `@+0x7f540`, Set=`vt[5]` `@+0x7f5c0` |
| Сигнатура wrapper | `Fn1(this, collection*)` — без окремого context (filter уже `this`) |
| Wrapper Get/Set | `validate()` → `resolve()` → QI **`{627D7951-9643-4DE6-898F-6C6B766AAB39}`** (IStateData **OLD**) → `ProcessGet/SetSettings` |
| SyncProxy QI `627D7951` | **E_NOINTERFACE** (працює лише `E6AB4158` NEW) |
| Filter QI `627D7951` після agg | **OK** → повертає `filter+0x18` |
| Cache `iface+0x40` | має вказувати на об’єкт з QI OLD IStateData; у no-App там «порожній» proxy → resolve `E_NOINTERFACE` |
| Перед DDS Set App робить | Get info=2,1 (sid=`0x7d`), потім Set (часто count=1, info=1) |

**Наступний крок (закрито 2026-07-21):** outer має бути **`NvAppSyncProxy`**, не system `NvXDCore.SyncProxy`.

#### ACE HIT без NVIDIA App.exe (2026-07-21) — CONFIRMED

Ключ: App використовує **окремий** LocalServer / IID-стек (OLD), не system SyncProxy (NEW).

| Компонент | CLSID / IID | Сервер |
|-----------|-------------|--------|
| **`NvAppSyncProxy`** (outer) | `{6E435E38-4A67-45C1-9D49-B83A8EDECC8E}` | `nvcontainer.exe` |
| App `ISyncProxy` | `{463FE815-7BC0-4463-9CE4-D8C8BD6EA257}` | PS: App `nvxdbat.dll` |
| IStateData **OLD** | `{627D7951-9643-4DE6-898F-6C6B766AAB39}` | PS: App `nvxdbat.dll` |
| `NvAppStateDataSessionFilter` | `{3F6374C2-3540-476A-A123-D1DA2B6DDF86}` | InprocHandler → App `nvxdbat.dll` |
| (опційно) `NvAppNvXDBatchEngine` | `{9C793FCD-5185-47BB-BB30-21750359CA2C}` | App `nvxdbat.dll` — **не потрібен** для HIT |

System-стек **NO_HIT** (як і раніше):

| Компонент | Результат |
|-----------|-----------|
| `NvXDCore.SyncProxy` `{DCAB0989-…}` + NEW `{E6AB4158}` | Set `flags=4` → `EAB00003` / noop |
| `NvXDBatchEngine` `{1DC715B2-…}` + SysFilter | `0x8000FFFF` / no ACE |

**Мінімальний HIT-рецепт (без `NVIDIA App.exe`):**

```text
1. CoInitializeEx(STA)
2. LoadLibrary(…\NVIDIA App\NvCpl\nvxdbat.dll)
3. CoCreate NvAppSyncProxy {6E435E38-…}  (CLSCTX_LOCAL_SERVER)
4. CoCreate NvAppStateDataSessionFilter {3F6374C2-…}
     outer = NvAppSyncProxy IUnknown, ctx = 0x402
5. QI filter → IStateData OLD {627D7951-…}  (= filter+0x18 wrapper)
6. wrapper Set(collection)  infoId=1+3, sid=0x7d, flags=4, **live handle**
7. (опційно) ProcessDoOperation на AppSync OLD
8. Descriptor* лише CoTaskMemAlloc
```

**Підтверджені ACE HIT** (без `NVIDIA App.exe`):

| Перехід | before → after |
|---------|----------------|
| → NVIDIA | `state=1 auto=0 i2d=0` → `2/0/1` |
| → Optimus | `2/0/1` → `1/0/0` |
| → Auto | `1/0/0` → `1/1/0` |
| cycle Optimus/Auto/NVIDIA/Optimus | **4/4 HIT** (поки handle живий) |

**Handle lifetime (критично):**

| Факт | Деталі |
|------|--------|
| Handle **сесійний** | Старий GUID → `EAB00003` на Set / GHI |
| Приклад | `{8FA752F3-…}` stale; `{747D8BF5-AB15-448B-91C5-52EFEC7C5850}` → HIT |
| Empty handle Set/GHI | `EAB00003` |
| Wrapper Get без відомого handle | `HR=0`, не повертає GUID |
| Get з відомим handle + preinit type=1 | `type/size` OK, **payload=0** (немає nested handle walk) |

#### ProcessGetHandleInfo (2026-07-21) — validate only

C++: `ProcessGetHandleInfo(Handle, HandleInfo*)`  
COM (AppSync OLD): `Fn2(sd, handleGuid*, info*)` → **HR=0**

| Вхід | Результат |
|------|-----------|
| DDS handle `{747D8BF5-…}` | `settingId=0x7d` @ info+0, handle echo @ +12 |
| Root/DoOp `{D812F4FF-…}` | `settingId=0x2` |
| Empty GUID | `EAB00003` |

**Висновок:** GHI **не створює і не перелічує** handles — лише валідує відомий GUID. У `DdsAppSync` використовується як pre-Set check.

Спроби discovery (NO_HIT на enum):

| Підхід | Результат |
|--------|-----------|
| Get count=0 / empty handle fill | no GUID |
| Walk Get nested Handle (type=1) з root | payload завжди 0 |
| DoOp opcode brute | no new DDS handle |
| `IStateDataReadOnly` (NumMethods=5) | лише Get+GHI subset |
| `NvAppNvXDPlcyEngine` як outer | filter OK, Get/DoOp `0x8000FFFF` |

**Наступний крок (інший no-App):** ~~звідки `nvxdplcy` бере handle~~ → **mem-scan discovery (HIT, 2026-07-21)**

#### Mem-scan DDS handle discovery (2026-07-21) — HIT

DescriptorRaw живе в **`nvcontainer.exe`** з `NvXDCore.dll` / `nvxdbat.dll` (не на диску в scanned trees).

Паттерн: `[guid:16][infoId:2][sid=0x7d][flags=1|4]` → GHI `settingId==0x7d` → wrapper Set.

| Умова | Результат |
|-------|-----------|
| Без admin (`OpenProcess` err=5) | NO_HANDLE на захищених PID |
| Admin + `SeDebugPrivilege` | DISCOVERED `{747D8BF5-…}` + ACE HIT (NVIDIA→Optimus) |
| Frida attach (без admin) | ті самі candidates у RW heap |

**Product:** `DdsAppSync` — cache/seed GHI → якщо fail → elevated mem-scan+GHI. Lab: `_mem_handle.exe` (elevate).

Нотатки:

- Залежність: **встановлений NVIDIA App** (DLL + COM `NvAppSyncProxy`), процес App **не** потрібен для apply (поки є валідний handle **або** admin для mem-scan refresh).
- Lab: `_ghi_probe.exe`, `_handle_walk.exe`, `_appsync_hit.exe`, `_fresh_h.exe`, `_mem_handle.exe`, `frida_handle_origin2.js`.

| Відхилено | Причина |
|-----------|---------|
| App inject / CDP / hidden host | UI blink, flaky, залежність від App |
| Foreign `NvCplApiSetSetting` | NO_HIT / AV |
| Fake `UXDServiceStarted` | NO_HIT |
| `NvCdpSilent` private desktop | краде single-instance App |
| System `NvXDCore.SyncProxy` + NEW IStateData | `EAB00003` / noop — не той IID-стек |

---

## 12. Політика для Acer Predator Tool

1. **Не** залежати від Acer services для GPU на PHN18.
2. Endurance/Standard → PnP (`GpuPnpController` / `switch_gpu.ps1`).
3. Optimus/Auto/NVIDIA → **`NvAppSyncProxy` + App SessionFilter** (COM, без `NVIDIA App.exe`). Handle: cache → GHI → **elevated mem-scan**. App-inject/CDP — legacy, вимкнено.
4. UI: **iGPU Only / Hybrid** + **Optimus / Auto / NVIDIA** (див. §3).
5. Індикатори читати з PnP + ACE + panel owner (`GpuControlService.GetStatus`).
6. При оновленні драйвера/App — ревалідувати UXD SettingId `0x7d`, handle GUID, `NvAppSyncProxy` / SessionFilter CLSID.
7. Tool має працювати **elevated** (PnP + SeDebug mem-scan).

### PHN18 matrix (historical, 2026-07-19) — archived summary

Dated dump folders removed from `tools/`. Outcome snapshot:

| Path | Outcome |
|------|---------|
| AcerService TCP `GPU_MODE` :46933 | **UNAVAILABLE** (only :15152) |
| BIOS offset 80 | **UNAVAILABLE** |
| AcerAgent `discrete_gpu_support` toggle | **NO_HIT** |
| PnP Endurance cycle | **WORKS** |
| NVCP / AppSync DDS Display Mode | **WORKS** (product = AppSync) |

Re-run: `probe_gpu_phn18_matrix.cmd` (creates a new `phn18_matrix_*/`). Script index: [`TOOLS.md`](TOOLS.md).
