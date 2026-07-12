# Інструкція: захоплення перемикання відеокарти (NVCP Display Mode)

Мета: під час **реального** перемикання панелі (Intel ↔ NVIDIA) зняти diff
реєстру / fingerprint, щоб знайти, що саме змінюється. Acer WMI уже перевірені —
вони mux не крутять.

## Перед стартом

1. Закрий **AcerPredatorTool**, PredatorSense, ігри.
2. Бажано почати з режиму, де панель на **Intel**
   (`owner=Intel`, `nv_display=Disabled`). Якщо невпевнений — просто продовжуй.
3. Адмін **не обов’язковий**. Якщо запустиш від адміна — додатково зніме Acer misc GET.

## Як запустити

Подвійний клік:

`Acer Predator Tool/tools/probe_gpu_nvcp_capture.cmd`

або в PowerShell:

```powershell
cd "Acer Predator Tool\tools"
.\probe_gpu_nvcp_capture.ps1
```

Інший цільовий режим:

```powershell
.\probe_gpu_nvcp_capture.ps1 -TargetMode "Optimus"
```

## Твої кроки в NVIDIA Control Panel

Скрипт сам відкриє NVCP і зупиниться з повідомленням **Press Enter**.

1. У NVCP відкрий:
   - **Display → Manage Display Mode**  
   або **Manage Power and Display mode** (назва залежить від драйвера).
2. Обери **NVIDIA GPU only** (перший прогін).
3. Підтверди / Apply. Очікуй короткий чорний екран (5–15 с).
4. Коли робочий стіл повернувся — повернись у вікно скрипта і натисни **Enter**.

## Що перевірити після прогону

У папці `Acer Predator Tool\tools\nvcp_capture_YYYYMMDD_HHMMSS\`:

| Файл | Зміст |
|------|--------|
| `DIFF.txt` | головний звіт |
| `before.json` / `after.json` | повні знімки |
| `capture.log` | повний лог |

У `DIFF.txt` шукай рядок:

- `MUX_CHANGED=True` — успіх, панель реально перемкнулась.
- `MUX_CHANGED=False` — перемикання не відбулось (не та сторінка NVCP / не Apply).

Успішний mux виглядає приблизно так:

```
BEFORE mux=owner=Intel|nv_display=Disabled
AFTER  mux=owner=NVIDIA|nv_display=Enabled
```

Якщо скрипт показав червоні помилки на diff, але mux змінився — можна перерахувати DIFF без нового перемикання:

```powershell
cd "Acer Predator Tool\tools"
.\probe_gpu_nvcp_capture.ps1 -RediffSessionDir .\nvcp_capture_YYYYMMDD_HHMMSS
```

## Другий прогін (назад)

Зараз панель уже може бути на **NVIDIA GPU only**. Для зворотного захоплення:

```powershell
cd "Acer Predator Tool\tools"
.\probe_gpu_nvcp_capture.ps1 -TargetMode "Optimus"
```

або **Automatic**. Бажано **Run as administrator**, щоб також зняти Acer misc GET.

## Після тестів

У NVCP поверни **Automatic** / **Optimus**, якщо не хочеш лишати Discrete.

## Що надіслати мені

Достатньо папки сесії `nvcp_capture_*` або хоча б `DIFF.txt` + підтвердження,
що екран реально блимав і режим змінився.
