# ---------------------------------------------------------------------------
# Verify-Round8.ps1 — 第八轮增量验收：B07（分压式共射 + 信号源内阻 Rs + 电解耦合电容 C3）
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\ui-verify\Verify-Round8.ps1
#
# 验证点：
#   1) 新 exe（bin\_verify，18:19:27）打开 C:\dsh_b07\b07.circuit 并求解成功，无
#      “存在教学模型未覆盖的电阻支路”报错；
#   2) 解题推导页：DC 分组（步骤1 Q点、步骤2 电解电容 C3 核验）+ AC 分组（步骤3-6，Av 为第4步）；
#   3) 第 4 步公式含 A_us=A_v·Ri/(R_s+Ri)，结果 Av=-34.25、Aus=-32.91（Rs=50 Ω）；
#   4) 实时指标：Q点 20.69 μA / 2.07 mA / 7.84 V，AC 卡出现“信号源内阻 Rs: 50 Ω”
#      与“源电压增益 Aus: -32.91”两行；
#   5) 同一新构建上载入普通分压式共射模板求解，确认 AC 卡不出现 Rs/Aus 两行（对照）。
# 证据：tools/ui-verify/evidence/round8/*.png + round8-summary.json
# ---------------------------------------------------------------------------
[CmdletBinding()]
param(
    [string]$ExePath = 'D:\BJTU\2026年大创\apps\desktop-winui\bin\_verify\CircuitAssistant.exe',
    [string]$B07Path = 'C:\dsh_b07\b07.circuit',
    [string]$EvidenceRoot = 'D:\BJTU\2026年大创\tools\ui-verify\evidence',
    [switch]$SkipNormalTemplateCheck
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location 'D:\BJTU\2026年大创'

$verifyRoot = $PSScriptRoot
$savedExePath = $ExePath
$savedEvidenceRoot = $EvidenceRoot
. (Join-Path $verifyRoot 'Capture-Ui.ps1')
$Phase = 'round8'
$ExePath = $savedExePath
$EvidenceRoot = $savedEvidenceRoot
$script:UiEvidenceRoot = $EvidenceRoot

if (-not (Test-Path -LiteralPath $ExePath)) { throw "找不到新 exe：$ExePath" }
if (-not (Test-Path -LiteralPath $B07Path)) { throw "找不到 B07 电路：$B07Path" }
$exeInfo = Get-Item -LiteralPath $ExePath
$evidenceDir = Join-Path $EvidenceRoot 'round8'
if (-not (Test-Path -LiteralPath $evidenceDir)) { New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null }
Get-ChildItem $evidenceDir -Filter *.png | Remove-Item -Force -ErrorAction SilentlyContinue

$script:Checks = New-Object System.Collections.ArrayList
$script:Files = New-Object System.Collections.ArrayList
$script:CurrentStage = 'init'

function Write-Stage { param([string]$Text) $script:CurrentStage = $Text; Write-Host ("[{0}] {1}" -f (Get-Date).ToString('HH:mm:ss'), $Text) }
function Add-Check {
    param([string]$Id, [bool]$Pass, [string]$Detail)
    [void]$script:Checks.Add([pscustomobject]@{ id = $Id; pass = $Pass; detail = $Detail })
    $mark = 'FAIL'; if ($Pass) { $mark = 'PASS' }
    Write-Host ("  [{0}] {1} - {2}" -f $mark, $Id, $Detail)
}
function Describe-El {
    param([object]$El, [string]$Missing = '缺失')
    if (-not $El) { return $Missing }
    return ("name=[{0}] y={1}" -f $El.Name, $El.WindowY)
}
function Get-TargetGeometry {
    $win = Get-CaWindow
    $scale = $win.Dpi / 96.0
    return [pscustomobject]@{
        Left = 100; Top = 100
        Width = [int][Math]::Round(1280 * $scale)
        Height = [int][Math]::Round(745 * $scale)
        Scale = $scale
    }
}
function Assert-Geometry {
    $target = Get-TargetGeometry
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $win = Get-CaWindow
        $bad = $win.Zoomed -or $win.Iconic -or
            [Math]::Abs($win.Left - 100) -gt 2 -or [Math]::Abs($win.Top - 100) -gt 2 -or
            [Math]::Abs($win.Width - $target.Width) -gt 2 -or [Math]::Abs($win.Height - $target.Height) -gt 2
        if ($bad) {
            Write-Host ("  (reset window geometry: {0},{1} {2}x{3})" -f $win.Left, $win.Top, $win.Width, $win.Height)
            $null = Set-CaWindowRect -Left 100 -Top 100 -WidthPx $target.Width -HeightPx $target.Height
            Start-Sleep -Milliseconds 800
            continue
        }
        [void](Set-CaForeground -Quiet)
        return (Get-CaWindow)
    }
    throw '窗口几何无法稳定'
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
    Write-Host ("  click [{0}] at window ({1},{2})" -f $el.Name, $el.CenterWindowX, $el.CenterWindowY)
    [void](Send-CaClick -WindowX $el.CenterWindowX -WindowY $el.CenterWindowY -PostClickDelayMs 600)
    return $el
}
function Capture-Step {
    param([string]$Name, [string]$Note = '')
    $target = Get-TargetGeometry
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $null = Assert-Geometry
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
    throw "截图尺寸与目标窗口几何不一致: $Name"
}
function Crop-Step {
    param([string]$SourceFile, [string]$Name, [int]$X, [int]$Y, [int]$W, [int]$H, [string]$Note = '')
    $path = Save-CaCrop -SourceFile $SourceFile -CropRect @([int]$X, [int]$Y, [int]$W, [int]$H) -FileName $Name -PhaseName 'round8'
    [void]$script:Files.Add([pscustomobject]@{ name = $Name; path = $path; note = "$Note region=($X,$Y,${W}x${H})" })
    Write-Host ("  crop   -> {0}  region=({1},{2},{3}x{4})" -f $path, $X, $Y, $W, $H)
    return $path
}
function Get-Region {
    param([string[]]$Patterns, [int]$MarginLeft = 25, [int]$MarginTop = 15, [int]$MarginRight = 10, [int]$MarginBottom = 20, [int]$MinHeight = 40)
    $null = Assert-Geometry
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
    $x = [Math]::Max(0, $minX - $MarginLeft)
    $y = [Math]::Max(0, $minY - $MarginTop)
    $right = [Math]::Min($win.Width, $maxX + $MarginRight)
    $bottom = [Math]::Min($win.Height, $maxY + $MarginBottom)
    return [pscustomobject]@{ X = $x; Y = $y; Width = [Math]::Max(20, $right - $x); Height = [Math]::Max($MinHeight, $bottom - $y); Hits = $hits }
}
function Scroll-Panel {
    param([ValidateSet('up', 'down')][string]$Direction = 'down', [int]$Times = 3, [int]$Delta = 240)
    for ($i = 0; $i -lt $Times; $i++) {
        $win = Get-CaWindow
        $wx = [int]($win.Width - 200)
        $wy = [int]($win.Height * 0.55)
        $d = -$Delta
        if ($Direction -eq 'up') { $d = $Delta }
        [void](Send-CaWheel -WindowX $wx -WindowY $wy -Delta $d)
        Start-Sleep -Milliseconds 160
    }
    Start-Sleep -Milliseconds 250
}
function Scroll-Until {
    param([ValidateSet('up', 'down')][string]$Direction = 'down', [string]$Pattern, [int]$MaxSteps = 12, [int]$TargetWindowY = 0)
    $null = Assert-Geometry
    for ($i = 0; $i -lt $MaxSteps; $i++) {
        $el = Wait-Text -Pattern $Pattern -TimeoutSec 1
        if ($el) {
            $win = Get-CaWindow
            $visible = ($el.WindowY -ge 250) -and (($el.WindowY + $el.Height) -le ($win.Height - 60))
            if ($visible) {
                if ($TargetWindowY -gt 0 -and $el.WindowY -gt $TargetWindowY) { Scroll-Panel -Direction down -Times 1; continue }
                return $el
            }
        }
        Scroll-Panel -Direction $Direction -Times 1
    }
    return (Wait-Text -Pattern $Pattern -TimeoutSec 2)
}
function Open-ProjectViaDialog {
    param([string]$ProjectPath, [string]$ExpectFileName)
    [void](Assert-Geometry)
    $win = Get-CaWindow
    $focusX = [int]($win.Width * 0.45); $focusY = [int]($win.Height * 0.82)
    $dlg = $null
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        [void](Set-CaForeground -Quiet)
        Start-Sleep -Milliseconds 250
        try { [void](Send-CaClick -WindowX $focusX -WindowY $focusY -PostClickDelayMs 250) } catch { }
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
    $fileBoxX = $dlg.Left + [int]($dlg.Width * 0.44)
    $fileBoxY = $dlg.Top + [int]($dlg.Height * 0.93)
    [void](Send-CaClickScreen -ScreenX $fileBoxX -ScreenY $fileBoxY -PostClickDelayMs 250)
    [void](Send-CaClickScreen -ScreenX $fileBoxX -ScreenY $fileBoxY -PostClickDelayMs 350)
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
    $escaped = [regex]::Escape($ExpectFileName)
    $status = Wait-Text -Pattern "工程已打开: $escaped" -TimeoutSec 30
    if (-not $status) { throw "未看到 [工程已打开: $ExpectFileName] 状态" }
    Write-Host ("  open status: [{0}]" -f $status.Name)
    Start-Sleep -Milliseconds 700
}
function Solve-AndWait {
    $before = Wait-Text -Pattern '^(求解完成|无法完成求解|求解失败|工程已打开|工程已载入)' -TimeoutSec 2
    $beforeName = ''
    if ($before) { $beforeName = $before.Name }
    $null = Click-Text -Pattern '求解与理论分析 \(Ctrl\+R\)' -TimeoutSec 15
    Start-Sleep -Milliseconds 700
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
    Start-Sleep -Milliseconds 700
    return $status
}
function Select-MetricsTab {
    foreach ($idx in @(1, 0, 2)) {
        try { $null = Select-CaTab -Index $idx -PostDelayMs 650 } catch { continue }
        if (@(Get-TextEls -Pattern '^直流静态工作点' -TimeoutSec 2).Count -gt 0) {
            Write-Host "  metrics tab active (uia index $idx)"
            return
        }
    }
    throw '无法切换到“实时指标”标签页'
}
function Start-AppFixed {
    if (Get-Process -Name CircuitAssistant -ErrorAction SilentlyContinue) { [void](Stop-CaApp) }
    $null = Stop-CaPickerHosts
    Start-Sleep -Milliseconds 400
    $win = Start-CaApp -Exe $ExePath -TimeoutSec 60
    $target = Get-TargetGeometry
    $null = Set-CaWindowRect -Left 100 -Top 100 -WidthPx $target.Width -HeightPx $target.Height -Foreground
    Start-Sleep -Milliseconds 900
    $null = Assert-Geometry
    Start-Sleep -Milliseconds 400
    return (Get-CaWindow)
}

# ===========================================================================
# 主流程
# ===========================================================================
Add-CaLog -Kind 'round8-start' -Data @{ exe = $exeInfo.FullName; exeLastWrite = $exeInfo.LastWriteTime.ToString('o'); b07 = $B07Path }
$failed = $false
try {
    Write-Stage ("Start new build: {0}" -f $exeInfo.LastWriteTime.ToString('HH:mm:ss'))
    $null = Start-AppFixed

    # --- B07：打开 + 求解 ---
    Write-Stage 'B07: open project'
    Open-ProjectViaDialog -ProjectPath $B07Path -ExpectFileName 'b07.circuit'
    Write-Stage 'B07: solve'
    $status = Solve-AndWait
    Add-Check -Id 'solve-success' -Pass ($status.Name -match '^求解完成') -Detail $status.Name
    $errEls = @(Get-TextEls -Pattern '存在教学模型未覆盖的电阻支路' -TimeoutSec 2)
    Add-Check -Id 'no-resistor-branch-error' -Pass (@($errEls).Count -eq 0) -Detail ("匹配数=" + @($errEls).Count)
    $dcHeader = Wait-Text -Pattern '^直流等效电路（DC 分析）' -TimeoutSec 10
    Add-Check -Id 'dc-group-header' -Pass ([bool]$dcHeader) -Detail (Describe-El $dcHeader)

    # --- 推导页：DC 组（步骤1/2）---
    for ($i = 0; $i -lt 16; $i++) { Scroll-Panel -Direction up -Times 1 }
    Start-Sleep -Milliseconds 400
    $topFull = Capture-Step -Name '01_b07_derivation_top_full' -Note 'B07 derivation top: DC group header + step1/2'
    $panel = Get-Region -Patterns @('依据当前电路类型与模型假设生成教学推导', '直流等效电路（DC 分析）') -MarginLeft 50 -MarginTop 20 -MarginRight 10 -MarginBottom 500 -MinHeight 400
    if ($panel) { $null = Crop-Step -SourceFile $topFull -Name '02_b07_derivation_top_panel' -X $panel.X -Y $panel.Y -W $panel.Width -H $panel.Height -Note 'right panel: DC group' }
    $c3Step = Scroll-Until -Direction down -Pattern '电解电容 C3 直流偏置与功能特性核验' -MaxSteps 10 -TargetWindowY 600
    Add-Check -Id 'dc-step2-electrolytic-check' -Pass ([bool]$c3Step) -Detail (Describe-El $c3Step)
    Start-Sleep -Milliseconds 400
    $c3Full = Capture-Step -Name '03_b07_step2_electrolytic_full' -Note 'B07 DC step2: electrolytic C3 polarity check'
    $c3Reg = Get-Region -Patterns @('电解电容 C3 直流偏置与功能特性核验', '极性正常') -MarginLeft 25 -MarginTop 110 -MarginRight 10 -MarginBottom 40 -MinHeight 300
    if ($c3Reg) { $null = Crop-Step -SourceFile $c3Full -Name '04_b07_step2_electrolytic_region' -X $c3Reg.X -Y $c3Reg.Y -W $c3Reg.Width -H $c3Reg.Height -Note 'electrolytic check card' }

    # --- 推导页：AC 组 + 步骤4 Av/Aus ---
    $acHeader = Scroll-Until -Direction down -Pattern '^交流等效电路（AC 分析）' -MaxSteps 12 -TargetWindowY 420
    Add-Check -Id 'ac-group-header' -Pass ([bool]$acHeader) -Detail (Describe-El $acHeader)
    Start-Sleep -Milliseconds 400
    $acFull = Capture-Step -Name '05_b07_ac_group_full' -Note 'B07 AC group header + AC steps'
    $acReg = Get-Region -Patterns @('^交流等效电路（AC 分析）') -MarginLeft 50 -MarginTop 90 -MarginRight 10 -MarginBottom 420 -MinHeight 400
    if ($acReg) { $null = Crop-Step -SourceFile $acFull -Name '06_b07_ac_group_panel' -X $acReg.X -Y $acReg.Y -W $acReg.Width -H $acReg.Height -Note 'AC group header + steps' }
    $avStep = Scroll-Until -Direction down -Pattern '中频段交流电压放大倍数 Av' -MaxSteps 10 -TargetWindowY 520
    Add-Check -Id 'ac-av-step' -Pass ([bool]$avStep) -Detail (Describe-El $avStep)
    Add-Check -Id 'groups-order' -Pass ([bool]($dcHeader -and $c3Step -and $acHeader -and $avStep)) -Detail 'DC组(步骤1/2) → AC组(步骤3-6) 顺序命中'
    Start-Sleep -Milliseconds 400
    $avFull = Capture-Step -Name '07_b07_av_step_full' -Note 'B07 AC step4: Av/Aus formulas + substitution'
    $formulaReg = Get-Region -Patterns @('中频段交流电压放大倍数 Av', '符号推导(公式|公理).*定理方程', '数值代入') -MarginLeft 25 -MarginTop 80 -MarginRight 10 -MarginBottom 130 -MinHeight 320
    if ($formulaReg) { $null = Crop-Step -SourceFile $avFull -Name '08_b07_av_formula_region' -X $formulaReg.X -Y $formulaReg.Y -W $formulaReg.Width -H $formulaReg.Height -Note 'Av/Aus formulas + substitution' }
    Scroll-Panel -Direction down -Times 1
    Start-Sleep -Milliseconds 350
    $avResultFull = Capture-Step -Name '09_b07_av_result_full' -Note 'B07 AC step4: substitution + result (Av=-34.25, Aus=-32.91)'
    $avVal = @(Get-TextEls -Pattern '-34\.25' -TimeoutSec 3)
    $ausVal = @(Get-TextEls -Pattern '-32\.91' -TimeoutSec 3)
    $avDetail = "匹配数=" + @($avVal).Count
    if (@($avVal).Count -gt 0) { $avDetail += " name=$(@($avVal)[0].Name)" }
    $ausDetail = "匹配数=" + @($ausVal).Count
    if (@($ausVal).Count -gt 0) { $ausDetail += " name=$(@($ausVal)[0].Name)" }
    Add-Check -Id 'av-value-34.25' -Pass (@($avVal).Count -gt 0) -Detail $avDetail
    Add-Check -Id 'aus-value-32.91' -Pass (@($ausVal).Count -gt 0) -Detail $ausDetail

    # --- 实时指标：Q 点 + AC 卡 Rs/Aus ---
    Write-Stage 'B07: metrics tab'
    Select-MetricsTab
    Start-Sleep -Milliseconds 600
    $metricsFull = Capture-Step -Name '11_b07_metrics_full' -Note 'B07 metrics tab: Q point + AC card (Rs/Aus rows)'
    $qReg = Get-Region -Patterns @('直流静态工作点', '基极静态电流', '集电极电流', '管压降') -MarginLeft 25 -MarginTop 15 -MarginRight 10 -MarginBottom 80 -MinHeight 160
    if ($qReg) { $null = Crop-Step -SourceFile $metricsFull -Name '12_b07_qpoint_region' -X $qReg.X -Y $qReg.Y -W $qReg.Width -H $qReg.Height -Note 'B07 Q point card' }
    $acMReg = Get-Region -Patterns @('交流小信号动态与频响指标', '信号源内阻 Rs', '源电压增益 Aus') -MarginLeft 25 -MarginTop 15 -MarginRight 10 -MarginBottom 30 -MinHeight 160
    if ($acMReg) { $null = Crop-Step -SourceFile $metricsFull -Name '13_b07_ac_metrics_region' -X $acMReg.X -Y $acMReg.Y -W $acMReg.Width -H $acMReg.Height -Note 'B07 AC card incl Rs/Aus rows' }
    $rsLabel = @(Get-TextEls -Pattern '^信号源内阻 Rs:' -TimeoutSec 3)
    $ausLabel = @(Get-TextEls -Pattern '^源电压增益 Aus:' -TimeoutSec 3)
    Add-Check -Id 'metrics-rs-row' -Pass (@($rsLabel).Count -gt 0) -Detail ("匹配数=" + @($rsLabel).Count)
    Add-Check -Id 'metrics-aus-row' -Pass (@($ausLabel).Count -gt 0) -Detail ("匹配数=" + @($ausLabel).Count)
    $qIbq = @(Get-TextEls -Pattern '20\.69 μA' -TimeoutSec 3)
    $qIcq = @(Get-TextEls -Pattern '2\.07 mA' -TimeoutSec 3)
    $qVce = @(Get-TextEls -Pattern '7\.84 V' -TimeoutSec 3)
    $rsVal = @(Get-TextEls -Pattern '^50 Ω$' -TimeoutSec 3)
    $ausVal2 = @(Get-TextEls -Pattern '^-32\.91$' -TimeoutSec 3)
    Add-Check -Id 'qpoint-ibq-20.69uA' -Pass (@($qIbq).Count -gt 0) -Detail ("匹配数=" + @($qIbq).Count)
    Add-Check -Id 'qpoint-icq-2.07mA' -Pass (@($qIcq).Count -gt 0) -Detail ("匹配数=" + @($qIcq).Count)
    Add-Check -Id 'qpoint-vceq-7.84V' -Pass (@($qVce).Count -gt 0) -Detail ("匹配数=" + @($qVce).Count)
    Add-Check -Id 'metrics-rs-value-50ohm' -Pass (@($rsVal).Count -gt 0) -Detail ("匹配数=" + @($rsVal).Count)
    Add-Check -Id 'metrics-aus-value-32.91' -Pass (@($ausVal2).Count -gt 0) -Detail ("匹配数=" + @($ausVal2).Count)

    # --- 对照：普通分压式共射模板不应出现 Rs/Aus 两行 ---
    if (-not $SkipNormalTemplateCheck) {
        Write-Stage 'Control: normal CE template must NOT show Rs/Aus rows'
        $null = Start-AppFixed
        $null = Wait-Text -Pattern '载入经典工程' -TimeoutSec 20
        $null = Click-Text -Pattern '^载入经典工程$' -TimeoutSec 15
        $null = Wait-Text -Pattern '求解与理论分析 \(Ctrl\+R\)' -TimeoutSec 20
        Start-Sleep -Milliseconds 700
        $null = Solve-AndWait
        Select-MetricsTab
        Start-Sleep -Milliseconds 500
        $null = Capture-Step -Name '14_normal_template_metrics_full' -Note 'control: normal CE template metrics (no Rs/Aus rows expected)'
        $rsNormal = @(Get-TextEls -Pattern '^信号源内阻 Rs:' -TimeoutSec 3)
        $ausNormal = @(Get-TextEls -Pattern '^源电压增益 Aus:' -TimeoutSec 3)
        Add-Check -Id 'normal-template-no-rs-row' -Pass (@($rsNormal).Count -eq 0) -Detail ("匹配数=" + @($rsNormal).Count)
        Add-Check -Id 'normal-template-no-aus-row' -Pass (@($ausNormal).Count -eq 0) -Detail ("匹配数=" + @($ausNormal).Count)
    }
    Add-CaLog -Kind 'round8-success' -Data @{ checks = $script:Checks.Count; files = $script:Files.Count }
}
catch {
    $err = $_.Exception.Message
    Write-Host "!! ROUND8 FAILED at [$script:CurrentStage]: $err"
    Add-CaLog -Kind 'round8-failed' -Data @{ stage = $script:CurrentStage; error = $err }
    try { $null = Assert-Geometry; $null = Capture-CaWindow -PhaseName 'round8' -FileName 'error_round8_failed' -Note "stage=$($script:CurrentStage) error=$err" } catch { }
    $failed = $true
}
finally {
    Get-Process CircuitAssistant, PickerHost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host '=== checks ==='
foreach ($c in $script:Checks) {
    $mark = 'FAIL'; if ($c.pass) { $mark = 'PASS' }
    Write-Host ("[{0}] {1} - {2}" -f $mark, $c.id, $c.detail)
}
Write-Host ''
Write-Host '=== screenshots ==='
foreach ($f in $script:Files) { Write-Host ("{0}`n    {1}" -f $f.name, $f.path) }
$summary = [pscustomobject]@{
    phase = 'round8'
    exe = $exeInfo.FullName
    exeLastWriteTime = $exeInfo.LastWriteTime.ToString('o')
    b07 = $B07Path
    capturedAt = (Get-Date).ToString('o')
    allChecksPassed = (((@($script:Checks | Where-Object { -not $_.pass }).Count) -eq 0) -and (-not $failed))
    checks = $script:Checks
    screenshots = $script:Files
}
$summaryPath = Join-Path $evidenceDir 'round8-summary.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Host "summary -> $summaryPath"
$failedChecks = @($script:Checks | Where-Object { -not $_.pass })
if ($failedChecks.Count -gt 0) { Write-Host ("!! {0} check(s) FAILED" -f $failedChecks.Count); exit 1 }
if ($failed) { exit 1 }
Write-Host '=== round8 verification complete: ALL CHECKS PASSED ==='
