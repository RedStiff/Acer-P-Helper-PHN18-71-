# Захоплення Display Mode (NVIDIA App / DDS)

Мета: під час **реального** перемикання панелі (Intel ↔ NVIDIA) зняти DIFF
реєстру + MuxSignature. Acer-сервіси не використовуються.

## Перед стартом

1. Закрий AcerPredatorTool / ігри.
2. Бажано почати з панелі на **Intel** (`owner=Intel|nv_display=Disabled`).
3. Адмін не обов’язковий.

## Як відкриваєш UI

На PHN18 основний шлях: **NVIDIA App з системного трею** (іконка NVIDIA).
Контекстне меню робочого столу — рідше, теж ок.

Скрипт за замовчуванням **не форсує** запуск вікна (`-UiSource Tray`):
чекає, поки ти сам відкриєш App з трею і перемкнеш режим.

## Запуск

```bat
probe_gpu_nvcp_capture.cmd
```

або:

```powershell
cd "Acer Predator Tool\tools"
.\probe_gpu_nvcp_capture.ps1 -TargetMode "NVIDIA GPU only" -UiSource Tray
```

Інші варіанти:

```powershell
.\probe_gpu_nvcp_capture.ps1 -TargetMode "Optimus" -UiSource Tray
.\probe_gpu_nvcp_capture.ps1 -UiSource AppLaunch   # автозапуск NVIDIA App.exe (CEF cwd)
```

## Твої кроки

1. У вікні скрипта з’явиться BEFORE і інструкція.
2. Трей → **NVIDIA App** → Display Mode / Manage Display Mode.
3. Обери цільовий режим (перший прогін: **NVIDIA GPU only**).
4. Apply → чорний екран 5–15 с → робочий стіл.
5. Повернись у вікно скрипта → **Enter**.

## Результат

Папка `nvcp_capture_YYYYMMDD_HHMMSS\`:

| Файл | Зміст |
|------|--------|
| `DIFF.txt` | головний звіт |
| `before.json` / `after.json` | знімки |
| `capture.log` | лог |

Успіх: `MUX_CHANGED=True` і зміна `owner=Intel` ↔ `owner=NVIDIA`.

## Зворотний прогін

```powershell
.\probe_gpu_nvcp_capture.ps1 -TargetMode "Optimus" -UiSource Tray
```

Після цього надішли `DIFF.txt` (або всю папку сесії).
