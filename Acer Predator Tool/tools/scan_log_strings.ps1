# Finds log-configuration related strings (registry value names, levels)
# inside AcerLightingService and OpenRGB binaries.
$files = @(
    'C:\WINDOWS\System32\DriverStore\FileRepository\predatorservice.inf_amd64_438f22dfe1d5b109\AcerLightingService.exe',
    'C:\WINDOWS\System32\DriverStore\FileRepository\predatorservice.inf_amd64_438f22dfe1d5b109\OpenRGB.exe',
    'C:\WINDOWS\System32\DriverStore\FileRepository\predatorservice.inf_amd64_438f22dfe1d5b109\AcerECKeyboardController.dll'
)

$keywords = 'Log|log|Level|level|Debug|debug|Trace|trace|Verbose|verbose'

foreach ($file in $files) {
    Write-Output "===== $([System.IO.Path]::GetFileName($file)) ====="
    $bytes = [System.IO.File]::ReadAllBytes($file)

    $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
    $asciiStrings = [regex]::Matches($ascii, '[\x20-\x7E]{4,}') | ForEach-Object { $_.Value }

    $unicode = [System.Text.Encoding]::Unicode.GetString($bytes)
    $unicodeStrings = [regex]::Matches($unicode, '[\x20-\x7E]{4,}') | ForEach-Object { $_.Value }

    ($asciiStrings + $unicodeStrings) |
        Where-Object { $_ -match $keywords -and $_.Length -lt 100 } |
        Sort-Object -Unique |
        Select-String -Pattern 'Log|Level|Debug|Trace|Verbose' -SimpleMatch:$false
}
