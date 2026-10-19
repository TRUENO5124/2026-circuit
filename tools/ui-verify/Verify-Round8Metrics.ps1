# ---------------------------------------------------------------------------
# Verify-Round8Metrics.ps1 — 第八轮增量验收补拍：AC 指标卡布局修复（Rs/Aus 两行不重叠）
#
# 新 exe：apps\desktop-winui\bin\_verify\CircuitAssistant.exe（LastWriteTime 18:30:46）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\ui-verify\Verify-Round8Metrics.ps1
#
# 内容：
#   1) B07（C:\dsh_b07\b07.circuit）求解 -> 〖实时指标〗：
#      11b_b07_metrics_fixed.png / 12b_b07_qpoint_fixed.png / 13b_b07_ac_metrics_fixed.png
#      断言：求解完成 6 步；无〖存在教学模型未覆盖的电阻支路〗；
#            AC 卡有〖信号源内阻 Rs: 50 Ω〗〖源电压增益 Aus: -32.91〗且两行不重叠（行距>20px）；
#            Q 点 20.69 μA / 2.07 mA / 7.84 V
#   2) 普通分压式共射模板（无 Rs）-> 14b_normal_metrics_fixed.png
#      断言：AC 卡不出现 Rs/Aus 两行
# 证据：tools/ui-verify/evidence/round8/*.png（其余历史截图已归档到 round8-buggy-ac-card/）
# ---------------------------------------------------------------------------
[CmdletBinding()]
param(
    [string]$ExePath = 'D:\BJTU\2026年大创\apps\desktop-winui\bin\_verify\CircuitAssistant.exe',
    [string]$B07Path = 'C:\dsh_b07\b07.circuit',
    [string]$EvidenceRoot = 'D:\BJTU\2026年大创\tools\ui-verify\evidence'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location 'D:\BJTU\2026年大创'

$savedExePath = $ExePath
$savedEvidenceRoot = $EvidenceRoot
. (Join-Path $PSScriptRoot 'Capture-Ui.ps1')
$Phase = 'round8'
$ExePath = $savedExePath
$EvidenceRoot = $savedEvidenceRoot
$script:UiEvidenceRoot = $EvidenceRoot

if (-not (Test-Path -LiteralPath $ExePath)) { throw "找不到新 exe：$ExePath" }
$exeInfo = Get-Item -LiteralPath $ExePath
$evidenceDir = Join-Path $EvidenceRoot 'round8'
if (-not (Test-Path -LiteralPath $evidenceDir)) { New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null }
$script:Checks = New-Object System.Collections.ArrayList
$script:Files = New-Object System.Collections.ArrayList
$script:Stage = 'init'

function Log { param([string]$Text) $script:Stage = $Text; Write-Host ("[{0}] {1}" -f (Get-Date).ToString('HH:mm:ss'), $Text) }
function Add-Check {
    param([string]$Id, [bool]$Pass, [string]$Detail)
    [void]$script:Checks.Add([pscustomobject]@{ id = $Id; pass = $Pass; detail = $Detail })
    $mark = 'FAIL'; if ($Pass) { $mark = 'PASS' }
    Write-Host ("  [{0}] {1} - {2}" -f $mark, $Id, $Detail)
}
function Get-TextEls { param([string]$Pattern, [int]$TimeoutSec = 6) return @(Get-CaUiElements -NamePattern $Pattern -TypeFilter 'Text' -TimeoutSec $TimeoutSec) }
function Wait-Text {
    param([string]$Pattern, [int]$TimeoutSec = 20)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ($true) {
        $els = @(Get-TextEls -Pattern $Pattern -TimeoutSec 0)
        if (@($els).Count -gt 0) { return @($els)[0] }
        if ((Get-Date) -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 350
    }
}
function Click-Text {
    param([string]$Pattern, [int]$TimeoutSec = 15)
    $el = Wait-Text -Pattern $Pattern -TimeoutSec $TimeoutSec
    if (-not $el) { throw "找不到可点击文本：$Pattern" }
    [void](Send-CaClick -WindowX $el.CenterWindowX -WindowY $el.CenterWindowY -PostClickDelayMs 600)
    return $el
}
function Target-Geom {
    $win = Get-CaWindow
    $scale = $win.Dpi / 96.0
    return [pscustomobject]@{ Width = [int][Math]::Round(1280 * $scale); Height = [int][Math]::Round(745 * $scale) }
}
function Assert-Geom {
    $target = Target-Geom
    for ($i = 1; $i -le 5; $i++) {
        $win = Get-CaWindow
        $bad = $win.Zoomed -or $win.Iconic -or
            [Math]::Abs($win.Left - 100) -gt 2 -or [Math]::Abs($win.Top - 100) -gt 2 -or
            [Math]::Abs($win.Width - $target.Width) -gt 2 -or [Math]::Abs($win.Height - $target.Height) -gt 2
        if ($bad) {
            $null = Set-CaWindowRect -Left 100 -Top 100 -WidthPx $target.Width -HeightPx $target.Height
            Start-Sleep -Milliseconds 800
            continue
        }
        [void](Set-CaForeground -Quiet)
        return (Get-CaWindow)
    }
    throw '窗口几何无法稳定'
}
function Capture-Step {
    param([string]$Name, [string]$Note = '')
    $target = Target-Geom
    for ($i = 1; $i -le 3; $i++) {
        $null = Assert-Geom
        Start-Sleep -Milliseconds 300
        $path = Capture-CaWindow -PhaseName 'round8' -FileName $Name -Note $Note
        $img = [System.Drawing.Image]::FromFile($path)
        try { $w = $img.Width; $h = $img.Height } finally { $img.Dispose() }
        if ([Math]::Abs($w - $target.Width) -le 2 -and [Math]::Abs($h - $target.Height) -le 2) {
            [void]$script:Files.Add([pscustomobject]@{ name = $Name; path = $path; note = $Note })
            Write-Host "  capture -> $path"
            return $path
        }
    }
    throw "截图尺寸不一致: $Name"
}
function Crop-Step {
    param([string]$SourceFile, [string]$Name, [int]$X, [int]$Y, [int]$W, [int]$H, [string]$Note = '')
    $path = Save-CaCrop -SourceFile $SourceFile -CropRect @([int]$X, [int]$Y, [int]$W, [int]$H) -FileName $Name -PhaseName 'round8'
    [void]$script:Files.Add([pscustomobject]@{ name = $Name; path = $path; note = "$Note region=($X,$Y,${W}x${H})" })
    Write-Host "  crop   -> $path"
    return $path
}
function Region-Of {
    param([string[]]$Patterns, [int]$ML = 25, [int]$MT = 15, [int]$MR = 10, [int]$MB = 25, [int]$MinH = 40)
    $null = Assert-Geom
    $win = Get-CaWindow
    $minX = $win.Width; $minY = $win.Height; $maxX = 0; $maxY = 0; $hits = @()
    foreach ($p in $Patterns) {
        foreach ($e in @(Get-TextEls -Pattern $p -TimeoutSec 6)) {
            if ($e.Width -le 0 -or $e.Height -le 0) { continue }
            if ($e.WindowX -lt 0 -or $e.WindowY -lt 0 -or $e.WindowX -ge $win.Width -or $e.WindowY -ge $win.Height) { continue }
            $hits += "$p@($($e.WindowX),$($e.WindowY))"
            if ($e.WindowX -lt $minX) { $minX = $e.WindowX }
            if ($e.WindowY -lt $minY) { $minY = $e.WindowY }
            if (($e.WindowX + $e.Width) -gt $maxX) { $maxX = $e.WindowX + $e.Width }
            if (($e.WindowY + $e.Height) -gt $maxY) { $maxY = $e.WindowY + $e.Height }
        }
    }
    if ($hits.Count -eq 0) { return $null }
    return [pscustomobject]@{ X = [Math]::Max(0, $minX - $ML); Y = [Math]::Max(0, $minY - $MT); Width = [Math]::Max(20, [Math]::Min($win.Width, $maxX + $MR) - [Math]::Max(0, $minX - $ML)); Height = [Math]::Max($MinH, [Math]::Min($win.Height, $maxY + $MB) - [Math]::Max(0, $minY - $MT)) }
}
function Select-MetricsTab {
    foreach ($idx in @(1, 0, 2)) {
        try { $null = Select-CaTab -Index $idx -PostDelayMs 650 } catch { continue }
        if (@(Get-TextEls -Pattern '^直流静态工作点' -TimeoutSec 2).Count -gt 0) { Write-Host "  metrics tab active (uia index $idx)"; return }
    }
    throw '无法切换到〖实时指标〗标签页'
}
function Start-AppFixed {
    if (Get-Process -Name CircuitAssistant -ErrorAction SilentlyContinue) { [void](Stop-CaApp) }
    $null = Stop-CaPickerHosts
    Start-Sleep -Milliseconds 400
    $null = Start-CaApp -Exe $ExePath -TimeoutSec 60
    $target = Target-Geom
    $null = Set-CaWindowRect -Left 100 -Top 100 -WidthPx $target.Width -HeightPx $target.Height -Foreground
    Start-Sleep -Milliseconds 900
    $null = Assert-Geom
    Start-Sleep -Milliseconds 400
}
function Open-ProjectViaDialog {
    param([string]$ProjectPath, [string]$ExpectFileName)
    [void](Assert-Geom)
    $win = Get-CaWindow
    $fx = [int]($win.Width * 0.45); $fy = [int]($win.Height * 0.82)
    $dlg = $null
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        [void](Set-CaForeground -Quiet)
        Start-Sleep -Milliseconds 250
        try { [void](Send-CaClick -WindowX $fx -WindowY $fy -PostClickDelayMs 250) } catch { }
        Start-Sleep -Milliseconds 200
        [System.Windows.Forms.SendKeys]::SendWait('^o')
        $dlg = Get-CaPickerDialog -TimeoutSec 6
        if ($dlg) { break }
        Write-Host "  Ctrl+O attempt $attempt produced no dialog; retrying"
    }
    if (-not $dlg) { throw '未能打开文件选择对话框' }
    Start-Sleep -Milliseconds 600
    $dlg = Get-CaPickerDialog -TimeoutSec 5
    if (-not $dlg) { throw '文件选择对话框在交互前消失' }
    [void](Set-CaExternalForeground -WindowHandle $dlg.Hwnd)
    Start-Sleep -Milliseconds 300
    $bx = $dlg.Left + [int]($dlg.Width * 0.44); $by = $dlg.Top + [int]($dlg.Height * 0.93)
    [void](Send-CaClickScreen -ScreenX $bx -ScreenY $by -PostClickDelayMs 250)
    [void](Send-CaClickScreen -ScreenX $bx -ScreenY $by -PostClickDelayMs 350)
    [void](Set-CaClipboardText -Text $ProjectPath)
    [void](Send-CaKeysTo -WindowHandle $dlg.Hwnd -Keys '^a')
    Start-Sleep -Milliseconds 150
    [void](Send-CaKeysTo -WindowHandle $dlg.Hwnd -Keys '^v')
    Start-Sleep -Milliseconds 500
    $clickedOpen = Click-CaExternalElement -WindowHandle $dlg.Hwnd -NamePattern '^打开' -TimeoutSec 8 -ActivateFirst
    if (-not $clickedOpen) { [void](Send-CaKeysTo -WindowHandle $dlg.Hwnd -Keys '{ENTER}') }
    $gone = Wait-CaWindowGone -WindowHandle $dlg.Hwnd -TimeoutSec 8
    if (-not $gone) {
        [void](Click-CaExternalElement -WindowHandle $dlg.Hwnd -NamePattern '^打开' -TimeoutSec 6 -ActivateFirst)
        $gone = Wait-CaWindowGone -WindowHandle $dlg.Hwnd -TimeoutSec 8
    }
    if (-not $gone) { throw '文件对话框未关闭' }
    $status = Wait-Text -Pattern ("工程已打开: " + [regex]::Escape($ExpectFileName)) -TimeoutSec 30
    if (-not $status) { throw "未看到 [工程已打开: $ExpectFileName]" }
    Write-Host ("  open status: [{0}]" -f $status.Name)
    Start-Sleep -Milliseconds 800
}
function Solve-AndWait {
    $before = Wait-Text -Pattern '^(求解完成|无法完成求解|求解失败|工程已打开|工程已载入)' -TimeoutSec 2
    $beforeName = ''
    if ($before) { $beforeName = $before.Name }
    $null = Click-Text -Pattern '求解与理论分析 \(Ctrl\+R\)' -TimeoutSec 15
    Start-Sleep -Milliseconds 800
    $deadline = (Get-Date).AddSeconds(30)
    $status = $null; $sawAnalyzing = $false
    while ((Get-Date) -lt $deadline) {
        if (@(Get-TextEls -Pattern '^(正在分析|正在提取|正在计算|分析中)' -TimeoutSec 0).Count -gt 0) { $sawAnalyzing = $true }
        $el = Wait-Text -Pattern '^(求解完成|无法完成求解|求解失败)' -TimeoutSec 1
        if ($el -and ($sawAnalyzing -or $el.Name -ne $beforeName)) { $status = $el; break }
        Start-Sleep -Milliseconds 300
    }
    if (-not $status) { throw '求解等待超时' }
    Write-Host ("  solve status: [{0}]" -f $status.Name)
    Start-Sleep -Milliseconds 800
    return $status
}

# ===========================================================================
Add-CaLog -Kind 'round8b-start' -Data @{ exe = $exeInfo.FullName; exeLastWrite = $exeInfo.LastWriteTime.ToString('o') }
$failed = $false
try {
    Log ("Start new build: {0}" -f $exeInfo.LastWriteTime.ToString('HH:mm:ss'))
    Start-AppFixed

    # ---- B07：求解 + 实时指标 ----
    Log 'B07: open + solve'
    Open-ProjectViaDialog -ProjectPath $B07Path -ExpectFileName 'b07.circuit'
    $status = Solve-AndWait
    Add-Check -Id 'b07-solve-success' -Pass ($status.Name -match '^求解完成.*6 个步骤') -Detail $status.Name
    $errEls = @(Get-TextEls -Pattern '存在教学模型未覆盖的电阻支路' -TimeoutSec 2)
    Add-Check -Id 'b07-no-resistor-branch-error' -Pass (@($errEls).Count -eq 0) -Detail ("匹配数=" + @($errEls).Count)

    Log 'B07: metrics tab'
    Select-MetricsTab
    Start-Sleep -Milliseconds 600
    $metricsFull = Capture-Step -Name '11b_b07_metrics_fixed' -Note 'B07 metrics tab after AC-card grid fix (18:30:46 build)'
    $qReg = Region-Of -Patterns @('直流静态工作点', '基极静态电流', '集电极电流', '管压降') -ML 25 -MT 15 -MR 10 -MB 80 -MinH 160
    if ($qReg) { $null = Crop-Step -SourceFile $metricsFull -Name '12b_b07_qpoint_fixed' -X $qReg.X -Y $qReg.Y -W $qReg.Width -H $qReg.Height -Note 'B07 Q point card (fixed build)' }
    $acReg = Region-Of -Patterns @('交流小信号动态与频响指标', '信号源内阻 Rs', '源电压增益 Aus') -ML 25 -MT 15 -MR 10 -MB 40 -MinH 160
    if ($acReg) { $null = Crop-Step -SourceFile $metricsFull -Name '13b_b07_ac_metrics_fixed' -X $acReg.X -Y $acReg.Y -W $acReg.Width -H $acReg.Height -Note 'B07 AC card incl Rs/Aus rows (fixed build)' }

    $rsLabel = @(Get-TextEls -Pattern '^信号源内阻 Rs:' -TimeoutSec 3)
    $ausLabel = @(Get-TextEls -Pattern '^源电压增益 Aus:' -TimeoutSec 3)
    Add-Check -Id 'metrics-rs-row' -Pass (@($rsLabel).Count -gt 0) -Detail ("匹配数=" + @($rsLabel).Count)
    Add-Check -Id 'metrics-aus-row' -Pass (@($ausLabel).Count -gt 0) -Detail ("匹配数=" + @($ausLabel).Count)
    $rsVal = @(Get-TextEls -Pattern '^50 Ω$' -TimeoutSec 3)
    $ausVal = @(Get-TextEls -Pattern '^-32\.91$' -TimeoutSec 3)
    Add-Check -Id 'metrics-rs-value-50ohm' -Pass (@($rsVal).Count -gt 0) -Detail ("匹配数=" + @($rsVal).Count)
    Add-Check -Id 'metrics-aus-value-32.91' -Pass (@($ausVal).Count -gt 0) -Detail ("匹配数=" + @($ausVal).Count)
    $qIbq = @(Get-TextEls -Pattern '20\.69 μA' -TimeoutSec 3)
    $qIcq = @(Get-TextEls -Pattern '2\.07 mA' -TimeoutSec 3)
    $qVce = @(Get-TextEls -Pattern '7\.84 V' -TimeoutSec 3)
    Add-Check -Id 'qpoint-ibq-20.69uA' -Pass (@($qIbq).Count -gt 0) -Detail ("匹配数=" + @($qIbq).Count)
    Add-Check -Id 'qpoint-icq-2.07mA' -Pass (@($qIcq).Count -gt 0) -Detail ("匹配数=" + @($qIcq).Count)
    Add-Check -Id 'qpoint-vceq-7.84V' -Pass (@($qVce).Count -gt 0) -Detail ("匹配数=" + @($qVce).Count)
    # 行距/重叠核验：两个标签的 BoundingRectangle 垂直间距（应 >= 20px，修复前重叠时约为 0）
    if ((@($rsLabel).Count -gt 0) -and (@($ausLabel).Count -gt 0)) {
        $rsRect = @($rsLabel)[0]
        $ausRect = @($ausLabel)[0]
        $gap = [Math]::Abs($ausRect.WindowY - $rsRect.WindowY)
        Add-Check -Id 'ac-rows-not-overlapped' -Pass ($gap -ge 20 -and $gap -le 120) -Detail ("Rs.y=$($rsRect.WindowY) Aus.y=$($ausRect.WindowY) 行距=$gap px")
    } else {
        Add-Check -Id 'ac-rows-not-overlapped' -Pass $false -Detail '缺少 Rs/Aus 标签，无法判定行距'
    }

    # ---- 对照：普通分压式共射模板不应出现 Rs/Aus 两行 ----
    Log 'Control: normal CE template metrics'
    Start-AppFixed
    $null = Wait-Text -Pattern '载入经典工程' -TimeoutSec 20
    $null = Click-Text -Pattern '^载入经典工程$' -TimeoutSec 15
    $null = Wait-Text -Pattern '求解与理论分析 \(Ctrl\+R\)' -TimeoutSec 20
    Start-Sleep -Milliseconds 800
    $null = Solve-AndWait
    Select-MetricsTab
    Start-Sleep -Milliseconds 600
    $null = Capture-Step -Name '14b_normal_metrics_fixed' -Note 'control: normal CE template metrics after fix (no Rs/Aus rows expected)'
    $rsNormal = @(Get-TextEls -Pattern '^信号源内阻 Rs:' -TimeoutSec 3)
    $ausNormal = @(Get-TextEls -Pattern '^源电压增益 Aus:' -TimeoutSec 3)
    Add-Check -Id 'normal-template-no-rs-row' -Pass (@($rsNormal).Count -eq 0) -Detail ("匹配数=" + @($rsNormal).Count)
    Add-Check -Id 'normal-template-no-aus-row' -Pass (@($ausNormal).Count -eq 0) -Detail ("匹配数=" + @($ausNormal).Count)
    Add-CaLog -Kind 'round8b-success' -Data @{ checks = $script:Checks.Count }
}
catch {
    $err = $_.Exception.Message
    Write-Host "!! ROUND8B FAILED at [$script:Stage]: $err"
    Add-CaLog -Kind 'round8b-failed' -Data @{ stage = $script:Stage; error = $err }
    try { $null = Assert-Geom; $null = Capture-CaWindow -PhaseName 'round8' -FileName 'error_round8b_failed' -Note "stage=$($script:Stage) error=$err" } catch { }
    $failed = $true
}
finally {
    Get-Process CircuitAssistant, PickerHost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host '=== checks ==='
foreach ($c in $script:Checks) { $mark = 'FAIL'; if ($c.pass) { $mark = 'PASS' }; Write-Host ("[{0}] {1} - {2}" -f $mark, $c.id, $c.detail) }
Write-Host ''
Write-Host '=== screenshots ==='
foreach ($f in $script:Files) { Write-Host ("{0}`n    {1}" -f $f.name, $f.path) }
$summaryPath = Join-Path $evidenceDir 'round8b-summary.json'
[pscustomobject]@{
    phase = 'round8b'
    exe = $exeInfo.FullName
    exeLastWriteTime = $exeInfo.LastWriteTime.ToString('o')
    capturedAt = (Get-Date).ToString('o')
    checks = $script:Checks
    screenshots = $script:Files
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Host "summary -> $summaryPath"
$failedChecks = @($script:Checks | Where-Object { -not $_.pass })
if ($failedChecks.Count -gt 0) { Write-Host ("!! {0} check(s) FAILED" -f $failedChecks.Count); exit 1 }
if ($failed) { exit 1 }
Write-Host '=== round8b verification complete: ALL CHECKS PASSED ==='
