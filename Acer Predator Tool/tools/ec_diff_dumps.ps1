<#
.SYNOPSIS
  Diff two EC dump folders / text dumps (read-only analysis).

.DESCRIPTION
  Highlights changed bytes between two captures. Accepts:
  - Folders containing *.log / *.txt / *.hex / *.bin
  - Or two explicit files

.EXAMPLE
  .\ec_diff_dumps.ps1 -Left .\ec_dumps\a -Right .\ec_dumps\b
  .\ec_diff_dumps.ps1 -LeftFile .\ec_40.txt -RightFile .\ec_50.txt
#>
[CmdletBinding()]
param(
    [string]$Left,
    [string]$Right,
    [string]$LeftFile,
    [string]$RightFile,
    [int]$MaxDiffs = 64
)

$ErrorActionPreference = 'Stop'

function Get-HexBytesFromText {
    param([string]$Text)
    $matches = [regex]::Matches($Text, '(?i)\b([0-9A-F]{2})\b')
    if ($matches.Count -lt 16) { return $null }
    $bytes = New-Object byte[] $matches.Count
    for ($i = 0; $i -lt $matches.Count; $i++) {
        $bytes[$i] = [Convert]::ToByte($matches[$i].Groups[1].Value, 16)
    }
    return $bytes
}

function Get-BestDumpBytes {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $raw = [System.IO.File]::ReadAllBytes($Path)
        # Prefer parsed hex from text-ish files
        $ext = [IO.Path]::GetExtension($Path).ToLowerInvariant()
        if ($ext -in '.txt', '.log', '.hex', '.dump', '') {
            $text = [IO.File]::ReadAllText($Path)
            $parsed = Get-HexBytesFromText $text
            if ($parsed) { return $parsed }
        }
        return $raw
    }

    $files = Get-ChildItem -LiteralPath $Path -File -Include *.log,*.txt,*.hex,*.dump,*.bin -ErrorAction SilentlyContinue |
        Sort-Object Length -Descending
    foreach ($f in $files) {
        $bytes = Get-BestDumpBytes -Path $f.FullName
        if ($bytes -and $bytes.Length -ge 16) { return $bytes }
    }
    return $null
}

if (-not $LeftFile -and $Left) { $LeftFile = $Left }
if (-not $RightFile -and $Right) { $RightFile = $Right }
if (-not $LeftFile -or -not $RightFile) {
    throw 'Provide -Left/-Right directories or -LeftFile/-RightFile.'
}

$a = Get-BestDumpBytes -Path $LeftFile
$b = Get-BestDumpBytes -Path $RightFile
if (-not $a) { throw "Could not parse dump bytes from: $LeftFile" }
if (-not $b) { throw "Could not parse dump bytes from: $RightFile" }

$len = [Math]::Min($a.Length, $b.Length)
$diffs = New-Object System.Collections.Generic.List[object]
for ($i = 0; $i -lt $len; $i++) {
    if ($a[$i] -ne $b[$i]) {
        $diffs.Add([pscustomobject]@{
            Offset = ('0x{0:X4}' -f $i)
            Left   = ('0x{0:X2}' -f $a[$i])
            Right  = ('0x{0:X2}' -f $b[$i])
        })
        if ($diffs.Count -ge $MaxDiffs) { break }
    }
}

Write-Host ("Left : {0} ({1} bytes)" -f $LeftFile, $a.Length)
Write-Host ("Right: {0} ({1} bytes)" -f $RightFile, $b.Length)
Write-Host ("Compared first {0} bytes; showing up to {1} diffs." -f $len, $MaxDiffs)
Write-Host ''

if ($diffs.Count -eq 0) {
    Write-Host 'No differences in compared range.' -ForegroundColor Green
    if ($a.Length -ne $b.Length) {
        Write-Host ("Length differs: {0} vs {1}" -f $a.Length, $b.Length) -ForegroundColor Yellow
    }
}
else {
    $diffs | Format-Table -AutoSize
    Write-Host ("Total listed diffs: {0}" -f $diffs.Count)
}
