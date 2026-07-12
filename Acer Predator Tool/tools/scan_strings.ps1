# Extracts ASCII and UTF-16 strings from PredatorSense service binaries
# and filters for WMI/EC/lighting-related keywords to reveal the init sequence.
param(
    [string[]]$Files = @(
        'C:\WINDOWS\System32\DriverStore\FileRepository\predatorservice.inf_amd64_438f22dfe1d5b109\AcerLightingService.exe',
        'C:\WINDOWS\System32\DriverStore\FileRepository\predatorservice.inf_amd64_438f22dfe1d5b109\AcerECKeyboardController.dll',
        'C:\WINDOWS\System32\DriverStore\FileRepository\predatorservice.inf_amd64_438f22dfe1d5b109\AcerHardwareService.exe'
    )
)

$keywords = 'Gaming|Backlight|RgbKb|LEDBehavior|LEDColor|WMI|wmi|ACPI|GamingFunction|KBBacklight|EC_|lighting|Lighting|static|Static|zone|Zone|brightness|Brightness|init|Init'

foreach ($file in $Files) {
    Write-Output "===== $([System.IO.Path]::GetFileName($file)) ====="
    $bytes = [System.IO.File]::ReadAllBytes($file)

    $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
    $asciiStrings = [regex]::Matches($ascii, '[\x20-\x7E]{6,}') | ForEach-Object { $_.Value }

    $unicode = [System.Text.Encoding]::Unicode.GetString($bytes)
    $unicodeStrings = [regex]::Matches($unicode, '[\x20-\x7E]{6,}') | ForEach-Object { $_.Value }

    ($asciiStrings + $unicodeStrings) |
        Where-Object { $_ -match $keywords } |
        Sort-Object -Unique
}
