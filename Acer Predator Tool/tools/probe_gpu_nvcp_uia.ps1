#Requires -Version 5.1
<#
.SYNOPSIS
  Enumerate / drive classic NVIDIA Control Panel Display Mode via UI Automation.
  No Acer services. Fallback when nvcpl.dll SET does not flip DDS mux.

.PARAMETER ApplyMode
  If set, click the matching radio/button text and Apply if found.
  Values: 'NVIDIA GPU only', 'Optimus', 'Automatic'

.PARAMETER DumpOnly
  Only dump UIA tree under NVIDIA Control Panel (default if ApplyMode empty).

.EXAMPLE
  .\probe_gpu_nvcp_uia.ps1
  .\probe_gpu_nvcp_uia.ps1 -ApplyMode 'NVIDIA GPU only'
#>
[CmdletBinding()]
param(
    [ValidateSet('', 'NVIDIA GPU only', 'Optimus', 'Automatic')]
    [string]$ApplyMode = '',
    [switch]$Force
)

. "$PSScriptRoot\_gpu_common.ps1"

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$sessionDir = Join-Path $PSScriptRoot ("nvcp_uia_{0}" -f $stamp)
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null
$log = Join-Path $sessionDir 'uia.log'
Write-GpuLog $log '=== NVCP UI Automation probe (NO Acer) ===' White

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-UiaRoot {
    return [System.Windows.Automation.AutomationElement]::RootElement
}

function Get-NvcpWindow {
    $root = Get-UiaRoot
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            'NVIDIA Control Panel',
            [System.Windows.Automation.PropertyConditionFlags]::IgnoreCase))
    )
    $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if ($win) { return $win }

    # Fallback: any window whose name contains NVIDIA
    $all = $root.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window))
    )
    foreach ($w in $all) {
        $n = $w.Current.Name
        if ($n -match 'NVIDIA') { return $w }
    }
    return $null
}

function Export-UiaTree {
    param(
        $Element,
        [int]$Depth = 0,
        [int]$MaxDepth = 8,
        [System.Collections.Generic.List[string]]$Lines
    )
    if ($null -eq $Element -or $Depth -gt $MaxDepth) { return }
    try {
        $c = $Element.Current
        $line = ('{0}[{1}] name="{2}" autoId="{3}" class="{4}"' -f `
            ('  ' * $Depth), $c.ControlType.ProgrammaticName, $c.Name, $c.AutomationId, $c.ClassName)
        [void]$Lines.Add($line)
        if ($c.Name -match 'Optimus|NVIDIA GPU|Automatic|Display Mode|Apply|Select') {
            Write-GpuLog $log ("HIT_NODE depth={0} type={1} name={2}" -f $Depth, $c.ControlType.ProgrammaticName, $c.Name) Cyan
        }
    } catch {
        [void]$Lines.Add(('{0}<err {1}>' -f ('  ' * $Depth), $_.Exception.Message))
        return
    }
    $children = $Element.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($ch in $children) {
        Export-UiaTree -Element $ch -Depth ($Depth + 1) -MaxDepth $MaxDepth -Lines $Lines
    }
}

function Find-ByNameContains {
    param($Root, [string]$Text, [System.Windows.Automation.ControlType]$Type = $null)
    $nameCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Text,
        [System.Windows.Automation.PropertyConditionFlags]::IgnoreCase)
    if ($null -eq $Type) {
        return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
    }
    $and = New-Object System.Windows.Automation.AndCondition(
        $nameCond,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $Type))
    )
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $and)
}

function Invoke-UiaClick {
    param($Element)
    if ($null -eq $Element) { return $false }
    try {
        $inv = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($inv) { $inv.Invoke(); return $true }
    } catch {}
    try {
        $sel = $Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        if ($sel) { $sel.Select(); return $true }
    } catch {}
    try {
        $toggle = $Element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if ($toggle) { $toggle.Toggle(); return $true }
    } catch {}
    try {
        $rect = $Element.Current.BoundingRectangle
        if ($rect.Width -gt 0 -and $rect.Height -gt 0) {
            $x = [int]($rect.X + $rect.Width / 2)
            $y = [int]($rect.Y + $rect.Height / 2)
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MouseClick {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
  [DllImport("user32.dll")] public static extern void mouse_event(int f, int dx, int dy, int d, int e);
  public const int LEFTDOWN=0x02, LEFTUP=0x04;
  public static void Click(int x, int y) {
    SetCursorPos(x,y);
    mouse_event(LEFTDOWN,0,0,0,0);
    mouse_event(LEFTUP,0,0,0,0);
  }
}
'@ -ErrorAction SilentlyContinue
            [MouseClick]::Click($x, $y)
            return $true
        }
    } catch {}
    return $false
}

$before = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $before -Label 'BEFORE'

$opened = Open-NvidiaDisplayModeUi -Source AppLaunch
Write-GpuLog $log ("Open UI: {0} {1}" -f $opened.Ok, $opened.Method) Cyan
Start-Sleep -Seconds 2

$win = Get-NvcpWindow
if (-not $win) {
    Write-GpuLog $log 'NVIDIA Control Panel window not found. Open it manually and re-run.' Red
    exit 2
}
Write-GpuLog $log ("Window: name={0}" -f $win.Current.Name) Green

$lines = New-Object System.Collections.Generic.List[string]
Export-UiaTree -Element $win -Lines $lines -MaxDepth 10
$treePath = Join-Path $sessionDir 'uia_tree.txt'
$lines | Set-Content -LiteralPath $treePath -Encoding UTF8
Write-GpuLog $log ("Tree dumped: {0} ({1} lines)" -f $treePath, $lines.Count) Cyan

# Try navigate to Display Mode in tree/list
$navNames = @(
    'Manage Display Mode',
    'Manage Power and Display mode',
    'Display Mode',
    'Display'
)
foreach ($n in $navNames) {
    $el = Find-ByNameContains -Root $win -Text $n
    if ($el) {
        Write-GpuLog $log ("Nav candidate: {0}" -f $el.Current.Name) Yellow
        if ($ApplyMode) {
            $ok = Invoke-UiaClick -Element $el
            Write-GpuLog $log ("  click nav Ok={0}" -f $ok)
            Start-Sleep -Seconds 1
            $win = Get-NvcpWindow
        }
    }
}

if ($ApplyMode) {
    if (-not $Force) {
        Write-Host ("Will select Display Mode: {0}" -f $ApplyMode) -ForegroundColor Yellow
        if ((Read-Host 'Type YES') -ne 'YES') { exit 1 }
    }
    $targets = @($ApplyMode)
    if ($ApplyMode -eq 'NVIDIA GPU only') { $targets += @('NVIDIA GPU Only', 'NVIDIA only') }
    $clicked = $false
    foreach ($t in $targets) {
        $el = Find-ByNameContains -Root $win -Text $t
        if ($el) {
            Write-GpuLog $log ("Target node: type={0} name={1}" -f $el.Current.ControlType.ProgrammaticName, $el.Current.Name) Cyan
            $clicked = Invoke-UiaClick -Element $el
            Write-GpuLog $log ("  select Ok={0}" -f $clicked)
            if ($clicked) { break }
        }
    }
    if (-not $clicked) {
        Write-GpuLog $log 'Target radio/text not found in UIA tree. Navigate manually to Display Mode page and re-run.' Yellow
    } else {
        Start-Sleep -Milliseconds 500
        foreach ($applyName in @('Apply', 'OK', 'Yes')) {
            $btn = Find-ByNameContains -Root $win -Text $applyName -Type ([System.Windows.Automation.ControlType]::Button)
            if ($btn) {
                $aok = Invoke-UiaClick -Element $btn
                Write-GpuLog $log ("Apply button '{0}' Ok={1}" -f $applyName, $aok)
                if ($aok) { break }
            }
        }
        Write-GpuLog $log 'Waiting 8s for possible DDS switch...'
        Start-Sleep -Seconds 8
    }
}

$after = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $after -Label 'AFTER'
$changed = $before.MuxSignature -ne $after.MuxSignature
$summary = @(
    'NVCP UIA SUMMARY',
    ("Session: {0}" -f $sessionDir),
    ("BEFORE: {0}" -f $before.MuxSignature),
    ("AFTER:  {0}" -f $after.MuxSignature),
    ("MUX_CHANGED={0}" -f $changed),
    ("ApplyMode={0}" -f $ApplyMode),
    ("Tree: {0}" -f $treePath)
) -join "`r`n"
Set-Content (Join-Path $sessionDir 'SUMMARY.txt') $summary -Encoding UTF8
Write-Host $summary -ForegroundColor Cyan
Write-GpuLog $log ("DONE MUX_CHANGED={0}" -f $changed) $(if ($changed) { 'Green' } else { 'Yellow' })
