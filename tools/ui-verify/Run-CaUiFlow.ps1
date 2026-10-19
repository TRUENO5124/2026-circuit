# ---------------------------------------------------------------------------
# Run-CaUiFlow.ps1 — CircuitAssistant 教学分析界面“改动前/改动后”证据采集流程
#
# 用法（Windows PowerShell 5.1）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\ui-verify\Run-CaUiFlow.ps1 -Phase before
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\ui-verify\Run-CaUiFlow.ps1 -Phase after
#
# 流程（before = bin\Debug 旧 exe；after = bin\_verify 新构建，由 Lead 负责构建）：
#   1. Stop-Process 关闭同名进程 -> 启动 exe -> 固定窗口(物理 1280x745 @ 100,100) ->
#      截图 01_welcome
#   2. 欢迎页点“载入经典工程”（分压式共射放大电路教学模板）-> 截图 02_template_loaded
#   3. 点右侧“求解与理论分析 (Ctrl+R)” -> 等“求解完成” -> 截图 03_ce_derivation_full
#      + 裁剪 04_ce_derivation_panel / 05_ce_formula_region（公式框：角标/裁切情况）
#   4. 点“实时指标”标签 -> 截图 06_ce_metrics_full + 裁剪 07_ce_qpoint_region（Q 点）
#   5. Ctrl+O 打开 document\3-6题.circuit（PickerHost 对话框内粘贴完整路径回车）
#      -> 截图 08_pnp_loaded
#   6. 求解 -> 截图 09_pnp_derivation_full + 裁剪 10/11；点“实时指标”-> 12/13
#   7. 打开 examples\circuit-models\M01-nmos-common-source.circuit -> 求解 -> 14/15/16/17/18/19
#   8. 关闭应用，输出截图清单 JSON + flow-log.jsonl 记录
#
# 约束：只写 tools/ui-verify/evidence 下的文件；不修改应用源码；不构建应用。
# ---------------------------------------------------------------------------
[CmdletBinding()]
param(
    [ValidateSet('before', 'after')][string]$Phase = 'before',
    [string]$EvidencePhase,
    [string]$ExePath,
    [string]$EvidenceRoot,
    [string]$PnpProjectPath,
    [string]$MosProjectPath,
    [switch]$SkipMos,
    [switch]$SkipPnp,
    [switch]$SkipCe,
    [switch]$IncludeDrawer,
    [switch]$KeepAppOpen,
    [int]$WindowLeft = 100,
    [int]$WindowTop = 100,
    [int]$LogicalWidth = 1280,
    [int]$LogicalHeight = 745,
    [int]$DefaultTimeoutSec = 25
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location 'D:\BJTU\2026年大创'

# 注意：dot-source Capture-Ui.ps1 时它自己的 param() 默认值会覆盖当前作用域同名变量
# （$Phase/$ExePath/$EvidenceRoot），所以先保存本脚本的入参，dot-source 后再恢复。
$flowArgs = @{
    Phase        = $Phase
    EvidencePhase = $EvidencePhase
    ExePath      = $ExePath
    EvidenceRoot = $EvidenceRoot
}
$verifyRoot = $PSScriptRoot
. (Join-Path $verifyRoot 'Capture-Ui.ps1')
$Phase = $flowArgs.Phase
$EvidencePhase = $flowArgs.EvidencePhase
$ExePath = $flowArgs.ExePath
$EvidenceRoot = $flowArgs.EvidenceRoot

$workspaceRoot = 'D:\BJTU\2026年大创'
if (-not $EvidenceRoot -or $EvidenceRoot.Trim().Length -eq 0) { $EvidenceRoot = Join-Path $verifyRoot 'evidence' }
if (-not $PnpProjectPath) { $PnpProjectPath = Join-Path $workspaceRoot 'document\3-6题.circuit' }
if (-not $MosProjectPath) { $MosProjectPath = Join-Path $workspaceRoot 'examples\circuit-models\M01-nmos-common-source.circuit' }
$phaseMode = $Phase    # before/after 决定用哪份 exe
if (-not $ExePath) {
    if ($phaseMode -eq 'before') {
        $ExePath = Join-Path $workspaceRoot 'apps\desktop-winui\bin\Debug\net9.0-windows10.0.26100.0\win-x64\CircuitAssistant.exe'
    } else {
        $ExePath = Join-Path $workspaceRoot 'apps\desktop-winui\bin\_verify\CircuitAssistant.exe'
    }
}
if (-not (Test-Path -LiteralPath $ExePath)) { throw "找不到应用 exe（$phaseMode）：$ExePath" }
# $Phase 现在起表示“证据子目录名”（默认等于 before/after；after 重拍可用 after-fixed 保留旧证据）
if (-not $EvidencePhase -or $EvidencePhase.Trim().Length -eq 0) { $EvidencePhase = $phaseMode }
$Phase = $EvidencePhase
$exeInfo = Get-Item -LiteralPath $ExePath
Write-Host "=== Phase=$phaseMode evidenceDir=$Phase exe=$($exeInfo.FullName) lastWrite=$($exeInfo.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')) ==="
if ($phaseMode -eq 'after') {
    $beforeExe = Join-Path $workspaceRoot 'apps\desktop-winui\bin\Debug\net9.0-windows10.0.26100.0\win-x64\CircuitAssistant.exe'
    if (Test-Path -LiteralPath $beforeExe) {
        $beforeInfo = Get-Item -LiteralPath $beforeExe
        Write-Host "  baseline exe lastWrite=$($beforeInfo.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"
        if ($exeInfo.LastWriteTime -le $beforeInfo.LastWriteTime) {
            Write-Warning '!! after 构建时间不晚于基线 exe，请确认 Lead 已完成新构建'
        }
    }
}

$evidenceDir = Join-Path $EvidenceRoot $Phase
if (-not (Test-Path -LiteralPath $evidenceDir)) { New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null }
$debugDir = Join-Path $EvidenceRoot 'debug'
if (-not (Test-Path -LiteralPath $debugDir)) { New-Item -ItemType Directory -Force -Path $debugDir | Out-Null }

$script:FlowResults = New-Object System.Collections.ArrayList
$script:CurrentStage = 'init'

function Write-Stage {
    param([string]$Text)
    $script:CurrentStage = $Text
    Write-Host ("[{0}] {1}" -f (Get-Date).ToString('HH:mm:ss'), $Text)
}

function Get-CaTargetGeometry {
    $win = Get-CaWindow
    $scale = $win.Dpi / 96.0
    return [pscustomobject]@{
        Left   = $WindowLeft
        Top    = $WindowTop
        Width  = [int][Math]::Round($LogicalWidth * $scale)
        Height = [int][Math]::Round($LogicalHeight * $scale)
        Scale  = $scale
        Dpi    = $win.Dpi
    }
}

function Assert-CaGeometry {
    param([switch]$Foreground)
    $target = Get-CaTargetGeometry
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        $win = Get-CaWindow
        $mismatch = $win.Zoomed -or $win.Iconic -or
        [Math]::Abs($win.Left - $target.Left) -gt 2 -or
        [Math]::Abs($win.Top - $target.Top) -gt 2 -or
        [Math]::Abs($win.Width - $target.Width) -gt 2 -or
        [Math]::Abs($win.Height - $target.Height) -gt 2
        if ($mismatch) {
            Write-Host "  (window geometry $($win.Left),$($win.Top) $($win.Width)x$($win.Height) zoomed=$($win.Zoomed) iconic=$($win.Iconic) -> reset)"
            $null = Set-CaWindowRect -Left $target.Left -Top $target.Top -WidthPx $target.Width -HeightPx $target.Height
            Start-Sleep -Milliseconds (900 + 200 * $attempt)
            continue
        }
        # 连续两次读取稳定（防止布局/窗口状态在切换动画中抖动）
        Start-Sleep -Milliseconds 300
        $win2 = Get-CaWindow
        $stable = -not $win2.Zoomed -and -not $win2.Iconic -and
            [Math]::Abs($win2.Width - $target.Width) -le 2 -and
            [Math]::Abs($win2.Height - $target.Height) -le 2 -and
            [Math]::Abs($win2.Left - $target.Left) -le 2 -and
            [Math]::Abs($win2.Top - $target.Top) -le 2
        if ($stable) {
            if ($Foreground) { [void](Set-CaForeground -Quiet) }
            return $win2
        }
        Start-Sleep -Milliseconds 500
    }
    throw "窗口几何在 $attempt 次重置后仍不稳定（期望 $($target.Width)x$($target.Height) @ $($target.Left),$($target.Top)）"
}

function Get-CaImageSize {
    param([Parameter(Mandatory)][string]$Path)
    Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue
    $img = [System.Drawing.Image]::FromFile($Path)
    try { return [pscustomobject]@{ Width = $img.Width; Height = $img.Height } }
    finally { $img.Dispose() }
}

function Scroll-CaPanelToTop {
    param([string]$AnchorPattern = '依据当前电路类型')
    $null = Assert-CaGeometry
    $win = Get-CaWindow
    $anchor = Get-CaTextElement -Pattern $AnchorPattern -TimeoutSec 3
    $cx = if ($anchor) { $anchor.CenterWindowX } else { [int]($win.Width * 0.85) }
    $cy = if ($anchor) { $anchor.CenterWindowY + 60 } else { [int]($win.Height * 0.45) }
    [void](Set-CaForeground -Quiet)
    for ($i = 0; $i -lt 6; $i++) {
        [void](Send-CaWheel -WindowX $cx -WindowY $cy -Delta 360)
        Start-Sleep -Milliseconds 120
    }
    Start-Sleep -Milliseconds 500
}

function Scroll-CaPanelToText {
    # 在右侧面板内滚动，直到指定文本元素完整进入可视区并尽量位于面板上半部
    # （用于让公式框整体进入截图可视区）。
    param(
        [Parameter(Mandatory)][string]$Pattern,
        [int]$MaxSteps = 8,
        [int]$TargetWindowY = 620,
        [int]$BottomSafeMargin = 80
    )
    $null = Assert-CaGeometry
    $win = Get-CaWindow
    $cx = [int]($win.Width * 0.85)
    $cy = [int]($win.Height * 0.55)
    [void](Set-CaForeground -Quiet)
    $el = $null
    for ($i = 0; $i -lt $MaxSteps; $i++) {
        $el = Get-CaTextElement -Pattern $Pattern -TimeoutSec 1
        if ($el) {
            $bottom = $el.WindowY + $el.Height
            if ($el.WindowY -ge 265 -and $bottom -le ($win.Height - $BottomSafeMargin)) {
                # 已在可视区：若位置偏下（公式框会超出底部），再向下滚一点把标题上移
                if ($el.WindowY -gt $TargetWindowY) {
                    [void](Send-CaWheel -WindowX $cx -WindowY $cy -Delta -240)
                    Start-Sleep -Milliseconds 350
                    continue
                }
                Add-CaLog -Kind 'flow-scroll' -Data @{ stage = $script:CurrentStage; pattern = $Pattern; steps = $i; windowY = $el.WindowY }
                return $el
            }
        }
        [void](Send-CaWheel -WindowX $cx -WindowY $cy -Delta -360)
        Start-Sleep -Milliseconds 220
    }
    Add-CaLog -Kind 'flow-scroll-timeout' -Data @{ stage = $script:CurrentStage; pattern = $Pattern }
    return (Get-CaTextElement -Pattern $Pattern -TimeoutSec 2)
}

function Get-CaTextElement {
    param(
        [Parameter(Mandatory)][string]$Pattern,
        [int]$TimeoutSec = 10,
        [int]$MaxWindowY = 0,
        [int]$MinWindowY = 0,
        [switch]$AllMatches
    )
    $els = @(Get-CaUiElements -NamePattern $Pattern -TypeFilter 'Text' -TimeoutSec $TimeoutSec)
    if ($MaxWindowY -gt 0) { $els = @($els | Where-Object { $_.WindowY -le $MaxWindowY }) }
    if ($MinWindowY -gt 0) { $els = @($els | Where-Object { $_.WindowY -ge $MinWindowY }) }
    if (-not $AllMatches) { if (@($els).Count -gt 0) { return @($els)[0] } else { return $null } }
    return @($els)
}

function Wait-CaText {
    param(
        [Parameter(Mandatory)][string]$Pattern,
        [int]$TimeoutSec = $DefaultTimeoutSec,
        [int]$MaxWindowY = 0
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ($true) {
        $el = Get-CaTextElement -Pattern $Pattern -TimeoutSec 0 -MaxWindowY $MaxWindowY
        if ($el) { return $el }
        if ((Get-Date) -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 350
    }
}

function Click-CaText {
    param(
        [Parameter(Mandatory)][string]$Pattern,
        [int]$TimeoutSec = 15,
        [int]$MaxWindowY = 0,
        [switch]$DoubleClick
    )
    $el = $null
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while (-not $el) {
        $els = @(Get-CaUiElements -NamePattern $Pattern -TypeFilter 'Text' -TimeoutSec 0)
        if ($MaxWindowY -gt 0) { $els = @($els | Where-Object { $_.WindowY -le $MaxWindowY }) }
        if (@($els).Count -gt 0) { $el = @($els)[0]; break }
        if ((Get-Date) -ge $deadline) { break }
        Start-Sleep -Milliseconds 350
    }
    if (-not $el) { throw "找不到可点击文本元素：$Pattern" }
    Write-Host ("  click [{0}] at window ({1},{2}) size {3}x{4}" -f $el.Name, $el.CenterWindowX, $el.CenterWindowY, $el.Width, $el.Height)
    $r = Send-CaClick -WindowX $el.CenterWindowX -WindowY $el.CenterWindowY -Double:$DoubleClick -PostClickDelayMs 650
    Add-CaLog -Kind 'flow-click' -Data @{
        stage = $script:CurrentStage; pattern = $Pattern; elementName = $el.Name
        windowX = $el.CenterWindowX; windowY = $el.CenterWindowY
        screenX = $r.ScreenX; screenY = $r.ScreenY
    }
    return $el
}

function Capture-Step {
    param([Parameter(Mandatory)][string]$Name, [string]$Note = '')
    $target = Get-CaTargetGeometry
    $path = $null
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $null = Assert-CaGeometry -Foreground
        Start-Sleep -Milliseconds 350
        $path = Capture-CaWindow -PhaseName $Phase -FileName $Name -Note $Note
        $size = Get-CaImageSize -Path $path
        if ([Math]::Abs($size.Width - $target.Width) -le 2 -and [Math]::Abs($size.Height - $target.Height) -le 2) { break }
        Write-Host ("  (capture {0} size {1}x{2} != target {3}x{4}; re-capturing)" -f $Name, $size.Width, $size.Height, $target.Width, $target.Height)
        Add-CaLog -Kind 'flow-capture-size-mismatch' -Data @{ stage = $script:CurrentStage; name = $Name; size = "$($size.Width)x$($size.Height)"; target = "$($target.Width)x$($target.Height)"; attempt = $attempt }
        $path = $null
    }
    if (-not $path) { throw "截图 $Name 的窗口尺寸始终与目标几何不一致" }
    [void]$script:FlowResults.Add([pscustomobject]@{ name = $Name; path = $path; note = $Note })
    Write-Host ("  capture -> {0}  ({1})" -f $path, $Note)
    return $path
}

function Get-CaRegion {
    param(
        [Parameter(Mandatory)][string[]]$Patterns,
        [int]$MarginLeft = 20,
        [int]$MarginTop = 15,
        [int]$MarginRight = 10,
        [int]$MarginBottom = 20,
        [int]$MinHeight = 40
    )
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        $null = Assert-CaGeometry
        Start-Sleep -Milliseconds 400
        $win = Get-CaWindow
        $minX = $win.Width; $minY = $win.Height; $maxX = 0; $maxY = 0; $hits = @()
        $staleHit = $false
        foreach ($p in $Patterns) {
            $els = @(Get-CaUiElements -NamePattern $p -TypeFilter 'Text' -TimeoutSec 8)
            foreach ($e in $els) {
                if ($e.Width -le 0 -or $e.Height -le 0) { continue }
                if ($e.WindowX -ge $win.Width -or $e.WindowY -ge $win.Height) { $staleHit = $true; continue }
                if ($e.WindowX -lt 0 -or $e.WindowY -lt 0) { $staleHit = $true; continue }
                if (($e.WindowY + $e.Height) -gt $win.Height) { $staleHit = $true; continue }
                $hits += "$p@($($e.WindowX),$($e.WindowY),$($e.Width)x$($e.Height))"
                if ($e.WindowX -lt $minX) { $minX = $e.WindowX }
                if ($e.WindowY -lt $minY) { $minY = $e.WindowY }
                $ex = $e.WindowX + $e.Width; $ey = $e.WindowY + $e.Height
                if ($ex -gt $maxX) { $maxX = $ex }
                if ($ey -gt $maxY) { $maxY = $ey }
            }
        }
        if ($hits.Count -eq 0) {
            if ($attempt -lt 4) { Start-Sleep -Milliseconds 700; continue }
            return $null
        }
        if ($staleHit -and $attempt -lt 4 -and $hits.Count -le 1) {
            Write-Host ("  (region measurement saw stale/offscreen bounds; retry {0})" -f ($attempt + 1))
            Start-Sleep -Milliseconds 800
            continue
        }
        $x = [Math]::Max(0, $minX - $MarginLeft)
        $y = [Math]::Max(0, $minY - $MarginTop)
        $right = [Math]::Min($win.Width, $maxX + $MarginRight)
        $bottom = [Math]::Min($win.Height, $maxY + $MarginBottom)
        $w = [Math]::Max(20, $right - $x)
        $h = [Math]::Max($MinHeight, $bottom - $y)
        return [pscustomobject]@{ X = $x; Y = $y; Width = $w; Height = $h; Hits = $hits }
    }
    return $null
}

function Crop-Step {
    param(
        [Parameter(Mandatory)][string]$SourceFile,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][object]$Region,
        [string]$Note = ''
    )
    if (-not $Region) { throw "Crop-Step '$Name' 没有可用区域" }
    $path = Save-CaCrop -SourceFile $SourceFile -CropRect @($Region.X, $Region.Y, $Region.Width, $Region.Height) -FileName $Name -PhaseName $Phase
    [void]$script:FlowResults.Add([pscustomobject]@{
            name = $Name; path = $path
            note = "$Note region=($($Region.X),$($Region.Y),$($Region.Width)x$($Region.Height)) hits=$($Region.Hits -join '; ')"
        })
    Write-Host ("  crop   -> {0}  region=({1},{2},{3}x{4})" -f $path, $Region.X, $Region.Y, $Region.Width, $Region.Height)
    Add-CaLog -Kind 'flow-crop' -Data @{
        stage = $script:CurrentStage; file = $path; source = $SourceFile
        region = "$($Region.X),$($Region.Y),$($Region.Width)x$($Region.Height)"; hits = ($Region.Hits -join '; ')
    }
    return $path
}

function Get-CaPanelRegion {
    param([string]$AnchorPattern = '依据当前电路类型')
    $null = Assert-CaGeometry
    $win = Get-CaWindow
    $anchor = $null
    $candidates = @($AnchorPattern, '依据当前电路类型', '^符号推导公理 / 定理方程:', '^解题推导$')
    foreach ($ap in $candidates) {
        $deadline = (Get-Date).AddSeconds(8)
        while (-not $anchor) {
            $els = @(Get-CaUiElements -NamePattern $ap -TypeFilter 'Text' -TimeoutSec 0)
            if (@($els).Count -gt 0) { $anchor = @($els)[0] }
            elseif ((Get-Date) -ge $deadline) { break }
            else { Start-Sleep -Milliseconds 300 }
        }
        if ($anchor) { break }
    }
    $hits = @()
    if ($anchor) {
        $x = [Math]::Max(0, $anchor.WindowX - 50)
        $y = [Math]::Max(0, $anchor.WindowY - 90)
        $hits += "anchor=$($anchor.Name)@($($anchor.WindowX),$($anchor.WindowY),$($anchor.Width)x$($anchor.Height))"
    } else {
        $x = [int]($win.Width * 0.655)
        $y = 230
        $hits += 'anchor=fallback(no anchor text found)'
    }
    return [pscustomobject]@{
        X = $x; Y = $y
        Width = [Math]::Max(50, $win.Width - $x - 8)
        Height = [Math]::Max(50, $win.Height - $y - 12)
        Hits = $hits
    }
}

function Invoke-CaSolve {
    param([string]$ExpectNote = '')
    $beforeEl = Get-CaTextElement -Pattern '^(求解完成|无法完成求解|求解失败|正在分析|工程已载入|工程已打开)' -TimeoutSec 2
    $beforeName = if ($beforeEl) { $beforeEl.Name } else { '' }
    Write-Host ("  status before solve: [{0}]" -f $beforeName)
    $null = Click-CaText -Pattern '求解与理论分析 \(Ctrl\+R\)' -TimeoutSec 15
    Start-Sleep -Milliseconds 700
    $deadline = (Get-Date).AddSeconds($DefaultTimeoutSec)
    $status = $null
    $sawAnalyzing = $false
    while ((Get-Date) -lt $deadline) {
        $an = Get-CaTextElement -Pattern '^(正在分析|正在提取|正在计算|分析中)' -TimeoutSec 0
        if ($an) { $sawAnalyzing = $true }
        $el = Get-CaTextElement -Pattern '^(求解完成|无法完成求解|求解失败)' -TimeoutSec 0
        if ($el -and ($sawAnalyzing -or $el.Name -ne $beforeName)) { $status = $el; break }
        Start-Sleep -Milliseconds 300
    }
    if (-not $status) {
        $null = Capture-Step -Name 'error_solve_timeout' -Note 'solve wait timeout'
        throw "求解等待超时（$ExpectNote）"
    }
    Write-Host ("  solve status: [{0}]" -f $status.Name)
    if ($status.Name -notmatch '^求解完成') {
        $null = Capture-Step -Name 'error_solve_failed' -Note "solve failed: $($status.Name)"
        throw "求解失败（$ExpectNote）：$($status.Name)"
    }
    Add-CaLog -Kind 'flow-solve' -Data @{ stage = $script:CurrentStage; status = $status.Name; previous = $beforeName; note = $ExpectNote }
    Start-Sleep -Milliseconds 700
    return $status
}

function Invoke-CaOpenProject {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$ExpectFileName,
        [int]$TimeoutSec = 30
    )
    if (-not (Test-Path -LiteralPath $ProjectPath)) { throw "工程文件不存在：$ProjectPath" }
    Write-Host "  open project: $ProjectPath"
    [void](Assert-CaGeometry -Foreground)
    Start-Sleep -Milliseconds 300

    # 现象：Ctrl+O（Page 级 KeyboardAccelerator）偶尔没有弹出 PickerHost 文件对话框。
    # 因此最多重试 3 次；仍失败则退回“工程管理 -> 打开工程”菜单路径。
    $dlg = $null
    $winForFocus = Get-CaWindow
    $focusX = [int]($winForFocus.Width * 0.45)
    $focusY = [int]($winForFocus.Height * 0.82)
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        [void](Set-CaForeground -Quiet)
        Start-Sleep -Milliseconds 300
        # 先点一下画布空白区，确保页面/窗口焦点在应用内（之后 KeyboardAccelerator 才会生效）
        try { [void](Send-CaClick -WindowX $focusX -WindowY $focusY -PostClickDelayMs 250) } catch { }
        Start-Sleep -Milliseconds 200
        [System.Windows.Forms.SendKeys]::SendWait('^o')
        $dlg = Get-CaPickerDialog -TimeoutSec 6
        if ($dlg -and $dlg.Title -match '另存为|保存|Save|打印|Print') {
            Write-Host ("  detected a save/export dialog [{0}] instead of open dialog; closing it" -f $dlg.Title)
            [void](Send-CaKeysTo -WindowHandle $dlg.Hwnd -Keys '{ESC}')
            Start-Sleep -Milliseconds 600
            $dlg = $null
        }
        if ($dlg) { break }
        Write-Host ("  Ctrl+O attempt {0} produced no dialog; retrying" -f $attempt)
        Add-CaLog -Kind 'flow-open-retry' -Data @{ stage = $script:CurrentStage; attempt = $attempt; method = 'ctrl+o' }
        Start-Sleep -Milliseconds 800
    }
    if (-not $dlg) {
        Write-Host '  fallback: 工程管理 menu -> 打开工程'
        $null = Click-CaText -Pattern '^工程管理$' -TimeoutSec 10
        Start-Sleep -Milliseconds 600
        $menuItem = $null
        $deadline = (Get-Date).AddSeconds(8)
        while (-not $menuItem -and (Get-Date) -lt $deadline) {
            $items = @(Get-CaUiElements -NamePattern '^打开工程 / 导入 Web CircuitGraph' -TypeFilter 'MenuItem' -TimeoutSec 0)
            if (@($items).Count -gt 0) { $menuItem = @($items)[0] } else { Start-Sleep -Milliseconds 300 }
        }
        if ($menuItem) {
            $null = Send-CaClick -WindowX $menuItem.CenterWindowX -WindowY $menuItem.CenterWindowY -PostClickDelayMs 900
            $dlg = Get-CaPickerDialog -TimeoutSec 12
            if ($dlg -and $dlg.Title -match '另存为|保存|Save|打印|Print') {
                [void](Send-CaKeysTo -WindowHandle $dlg.Hwnd -Keys '{ESC}')
                Start-Sleep -Milliseconds 600
                $dlg = $null
            }
        }
        Add-CaLog -Kind 'flow-open-retry' -Data @{ stage = $script:CurrentStage; method = 'menu'; found = [bool]$dlg }
    }
    if (-not $dlg) {
        $null = Capture-Step -Name 'error_open_dialog_missing' -Note 'Ctrl+O / menu did not show PickerHost dialog'
        throw '未能打开文件选择对话框（Ctrl+O 与工程管理菜单均失败）'
    }
    Write-Host ("  dialog 0x{0:X} [{1}] {2}x{3} at {4},{5}" -f $dlg.Hwnd, $dlg.Title, $dlg.Width, $dlg.Height, $dlg.Left, $dlg.Top)
    # 对话框刚出现时 PickerHost 可能还在初始化，刷新一次句柄，避免句柄失效
    Start-Sleep -Milliseconds 600
    $dlg = Get-CaPickerDialog -TimeoutSec 4
    if (-not $dlg) {
        $null = Capture-Step -Name 'error_open_dialog_vanished' -Note 'PickerHost dialog disappeared before interaction'
        throw '文件选择对话框在交互前消失（可能被外部操作关闭）'
    }
    Write-Host ("  dialog(refreshed) 0x{0:X} [{1}] {2}x{3}" -f $dlg.Hwnd, $dlg.Title, $dlg.Width, $dlg.Height)

    $isAsciiPath = ($ProjectPath -match '^[\x20-\x7E]+$')
    try {
        $dbg = Capture-CaHandle -WindowHandle $dlg.Hwnd -FileName ("dlg-$Phase-$ExpectFileName-before.png") -PhaseName 'debug' -NoForeground
        [void]$script:FlowResults.Add([pscustomobject]@{ name = "debug-dialog"; path = $dbg; note = "open dialog for $ExpectFileName" })
    } catch {
        Write-Host ("  (dialog pre-capture skipped: {0})" -f $_.Exception.Message)
    }

    # 方案（已验证可用）：双击“文件名”输入框聚焦 -> 剪贴板粘贴绝对路径（绕开输入法）
    # -> 点击“打开(O)”按钮（UIA 精确定位 + 首次点击仅激活对话框）。
    $gone = $false
    try {
        [void](Set-CaExternalForeground -WindowHandle $dlg.Hwnd)
        Start-Sleep -Milliseconds 400
        $fileBoxX = $dlg.Left + [int]($dlg.Width * 0.44)
        $fileBoxY = $dlg.Top + [int]($dlg.Height * 0.93)
        Write-Host ("  click filename box at ({0},{1}) then paste absolute path" -f $fileBoxX, $fileBoxY)
        [void](Send-CaClickScreen -ScreenX $fileBoxX -ScreenY $fileBoxY -PostClickDelayMs 300)
        [void](Send-CaClickScreen -ScreenX $fileBoxX -ScreenY $fileBoxY -PostClickDelayMs 400)
        [void](Set-CaClipboardText -Text $ProjectPath)
        [void](Send-CaKeysTo -WindowHandle $dlg.Hwnd -Keys '^a')
        Start-Sleep -Milliseconds 150
        [void](Send-CaKeysTo -WindowHandle $dlg.Hwnd -Keys '^v')
        Start-Sleep -Milliseconds 600
        try {
            $dbg2 = Capture-CaHandle -WindowHandle $dlg.Hwnd -FileName ("dlg-$Phase-$ExpectFileName-pasted.png") -PhaseName 'debug' -NoForeground
            [void]$script:FlowResults.Add([pscustomobject]@{ name = "debug-dialog-pasted"; path = $dbg2; note = "path pasted for $ExpectFileName" })
        } catch {
            Write-Host ("  (dialog post-capture skipped: {0})" -f $_.Exception.Message)
        }
        $clickedOpen = Click-CaExternalElement -WindowHandle $dlg.Hwnd -NamePattern '^打开' -TimeoutSec 8 -ActivateFirst
        if (-not $clickedOpen) {
            Write-Host '  (open button not found via UIA; falling back to Enter)'
            [void](Send-CaKeysTo -WindowHandle $dlg.Hwnd -Keys '{ENTER}')
        }
        $gone = Wait-CaWindowGone -WindowHandle $dlg.Hwnd -TimeoutSec 8
        if (-not $gone) {
            Write-Host '  dialog still open; click 打开(O) again'
            [void](Click-CaExternalElement -WindowHandle $dlg.Hwnd -NamePattern '^打开' -TimeoutSec 6 -ActivateFirst)
            $gone = Wait-CaWindowGone -WindowHandle $dlg.Hwnd -TimeoutSec 8
        }
        Add-CaLog -Kind 'flow-open-route' -Data @{ stage = $script:CurrentStage; gone = $gone }
    } catch {
        Write-Host ("  dialog interaction failed: {0}" -f $_.Exception.Message)
        Add-CaLog -Kind 'flow-open-route-failed' -Data @{ error = $_.Exception.Message }
    }
    if (-not $gone) {
        try {
            $dbg3 = Capture-CaHandle -WindowHandle $dlg.Hwnd -FileName ("dlg-$Phase-$ExpectFileName-stuck.png") -PhaseName 'debug' -NoForeground
            [void]$script:FlowResults.Add([pscustomobject]@{ name = "debug-dialog-stuck"; path = $dbg3; note = "dialog for $ExpectFileName did not close" })
        } catch { }
        [void](Stop-CaPickerHosts)
        throw '文件对话框未关闭（路径输入/回车可能未生效，见 debug 截图）'
    }
    $escaped = [regex]::Escape($ExpectFileName)
    $status = Wait-CaText -Pattern "工程已打开: $escaped" -TimeoutSec $TimeoutSec
    if (-not $status) {
        $err = Get-CaTextElement -Pattern '打开工程失败|导入失败' -TimeoutSec 1
        if ($err) { throw "打开工程失败：$($err.Name)" }
        throw "打开工程后未看到状态 [工程已打开: $ExpectFileName]"
    }
    Write-Host ("  open status: [{0}]" -f $status.Name)
    Add-CaLog -Kind 'flow-open' -Data @{ stage = $script:CurrentStage; file = $ProjectPath; status = $status.Name }
    Start-Sleep -Milliseconds 700
    return $status
}

function Invoke-CaMetricsTab {
    # Pivot 标签页由 UIA TabItem 承载，标题文本本身不在 UIA 树里；用 SelectionItemPattern
    # 选择（声明顺序 0=参数设置, 1=实时指标, 2=解题推导），再用可见的“直流静态工作点”验证。
    $ok = $false
    $selected = -1
    foreach ($idx in @(1, 0, 2)) {
        try { $null = Select-CaTab -Index $idx -PostDelayMs 650 }
        catch { Write-Host ("  select tab index {0} failed: {1}" -f $idx, $_.Exception.Message); continue }
        $el = Get-CaTextElement -Pattern '^直流静态工作点' -TimeoutSec 2
        if ($el) { $ok = $true; $selected = $idx; break }
    }
    if (-not $ok) {
        # 兜底：按可见 TabItem 的屏幕位置点击第二个标签（实时指标）
        $tabs = @(Get-CaTabItems | Where-Object { -not $_.IsOffscreen -and $_.Width -gt 0 } | Sort-Object WindowX)
        $target = @($tabs | Select-Object -Skip 1 -First 1)
        if (@($target).Count -gt 0) {
            $t = $target[0]
            $null = Send-CaClick -WindowX ($t.WindowX + [int]($t.Width / 2)) -WindowY ($t.WindowY + [int]($t.Height / 2)) -PostClickDelayMs 700
            Start-Sleep -Milliseconds 500
            $el = Get-CaTextElement -Pattern '^直流静态工作点' -TimeoutSec 3
            if ($el) { $ok = $true; $selected = -2 }
        }
    }
    if (-not $ok) {
        $null = Capture-Step -Name 'error_metrics_tab' -Note 'cannot switch to metrics tab'
        throw '无法切换到“实时指标”标签页'
    }
    $method = if ($selected -eq -2) { 'click-tabitem' } else { 'uia-selection' }
    Add-CaLog -Kind 'flow-metrics-tab' -Data @{ stage = $script:CurrentStage; method = $method; index = $selected }
    Write-Host ("  metrics tab active (method={0}, index={1})" -f $method, $selected)
    Start-Sleep -Milliseconds 500
}

function Get-CaMetricsRegion {
    $r = Get-CaRegion -Patterns @('直流静态工作点', '基极静态电流', '集电极电流', '管压降') -MarginLeft 25 -MarginTop 15 -MarginRight 10 -MarginBottom 85 -MinHeight 150
    if (-not $r) { $r = Get-CaRegion -Patterns @('直流静态工作点') -MarginLeft 25 -MarginTop 15 -MarginRight 10 -MarginBottom 160 -MinHeight 150 }
    return $r
}

function Get-CaFormulaRegion {
    $r = Get-CaRegion -Patterns @('符号推导(公式|公理).*定理方程', '数值代入') -MarginLeft 30 -MarginTop 20 -MarginRight 10 -MarginBottom 90 -MinHeight 200
    if (-not $r) { $r = Get-CaPanelRegion }
    return $r
}

# ===========================================================================
# 主流程
# ===========================================================================
Add-CaLog -Kind 'flow-start' -Data @{ phase = $Phase; exe = $exeInfo.FullName; exeLastWrite = $exeInfo.LastWriteTime.ToString('o') }
try {
    Write-Stage "Cleanup leftover processes"
    if (Get-Process -Name CircuitAssistant -ErrorAction SilentlyContinue) { [void](Stop-CaApp) }
    $pickerKilled = Stop-CaPickerHosts
    if ($pickerKilled -gt 0) { Write-Host "  closed $pickerKilled leftover PickerHost dialog process(es)" }
    Start-Sleep -Milliseconds 400

    Write-Stage "Start app: $Phase"
    $win = Start-CaApp -Exe $ExePath -TimeoutSec 60
    Write-Host ("  app pid={0} window={1}x{2} dpi={3}" -f $win.ProcessId, $win.Width, $win.Height, $win.Dpi)
    $target = Get-CaTargetGeometry
    Write-Host ("  restrict window to {0},{1} {2}x{3} (scale={4})" -f $target.Left, $target.Top, $target.Width, $target.Height, $target.Scale)
    $win = Set-CaWindowRect -Left $target.Left -Top $target.Top -WidthPx $target.Width -HeightPx $target.Height -Foreground
    Start-Sleep -Milliseconds 800
    $null = Assert-CaGeometry -Foreground
    Start-Sleep -Milliseconds 600

    if (-not $SkipCe) {
        # --- 1. 欢迎页 ---
        Write-Stage 'STEP 1: welcome page'
        $null = Wait-CaText -Pattern '载入经典工程' -TimeoutSec 20
        $null = Capture-Step -Name '01_welcome' -Note 'blank workspace welcome page'

        # --- 2. 载入分压式共射教学模板 ---
        Write-Stage 'STEP 2: load 分压式共射放大电路 template'
        $null = Click-CaText -Pattern '^载入经典工程$' -TimeoutSec 15
        $null = Wait-CaText -Pattern '求解与理论分析 \(Ctrl\+R\)' -TimeoutSec 20
        Start-Sleep -Milliseconds 800
        $null = Capture-Step -Name '02_template_loaded' -Note 'voltage-divider common-emitter template loaded'

        # --- 3. 求解 + 解题推导公式框 ---
        Write-Stage 'STEP 3: solve common-emitter template -> derivation tab'
        $null = Invoke-CaSolve -ExpectNote 'CE template'
        $null = Wait-CaText -Pattern '符号推导(公式|公理).*定理方程' -TimeoutSec 15
        # 推导页内部是 ScrollViewer：滚到顶部，保证“直流等效电路（DC 分析）”分组标题出现在截图里
        Scroll-CaPanelToTop
        Start-Sleep -Milliseconds 500
        $null = Capture-Step -Name '03_ce_derivation_full' -Note 'CE derivation tab after solve (full window, scrolled to top)'
        $panel = Get-CaPanelRegion
        $null = Crop-Step -SourceFile (Join-Path $evidenceDir '03_ce_derivation_full.png') -Name '04_ce_derivation_panel' -Region $panel -Note 'right panel derivation region'
        # 公式框可能在滚动区下方：向下滚到公式框可见后再截一张整页，并据此裁公式区
        $null = Scroll-CaPanelToText -Pattern '符号推导(公式|公理).*定理方程'
        Start-Sleep -Milliseconds 400
        $null = Capture-Step -Name '04b_ce_formula_view_full' -Note 'CE derivation scrolled to formula boxes (full window)'
        $formula = Get-CaFormulaRegion
        $null = Crop-Step -SourceFile (Join-Path $evidenceDir '04b_ce_formula_view_full.png') -Name '05_ce_formula_region' -Region $formula -Note 'formula boxes (true sub/superscript, fractions, no clipping) region'

        # --- 4. 实时指标 Q 点 ---
        Write-Stage 'STEP 4: CE metrics -> Q point'
        Invoke-CaMetricsTab
        $null = Capture-Step -Name '06_ce_metrics_full' -Note 'CE metrics tab (Q point + AC)'
        $qregion = Get-CaMetricsRegion
        $null = Crop-Step -SourceFile (Join-Path $evidenceDir '06_ce_metrics_full.png') -Name '07_ce_qpoint_region' -Region $qregion -Note 'CE Q point card region'

        # --- 4b. 解题推导抽屉（底部速览，可选） ---
        if ($IncludeDrawer) {
            Write-Stage 'STEP 4b: CE derivation drawer'
            try {
                $null = Click-CaText -Pattern '^解题推导抽屉$' -TimeoutSec 10
                Start-Sleep -Milliseconds 900
                $null = Capture-Step -Name '07b_ce_derivation_drawer' -Note 'CE bottom derivation drawer opened'
                $null = Click-CaText -Pattern '^解题推导抽屉$' -TimeoutSec 10
                Start-Sleep -Milliseconds 500
            } catch {
                Write-Host "  (drawer capture skipped: $($_.Exception.Message))"
                Add-CaLog -Kind 'flow-drawer-skip' -Data @{ error = $_.Exception.Message }
            }
        }


    }

    if (-not $SkipPnp) {
        # --- 5. 打开 document/3-6题.circuit (PNP 分压偏置) ---
        Write-Stage 'STEP 5: open document/3-6题.circuit (PNP)'
        $pnpFileName = [System.IO.Path]::GetFileName($PnpProjectPath)
        $null = Invoke-CaOpenProject -ProjectPath $PnpProjectPath -ExpectFileName $pnpFileName
        $null = Capture-Step -Name '08_pnp_loaded' -Note 'PNP 3-6题.circuit opened'

        # --- 6. PNP 求解 + 推导 + Q 点 ---
        Write-Stage 'STEP 6: solve PNP project -> derivation'
        $null = Invoke-CaSolve -ExpectNote 'PNP 3-6题'
        $null = Wait-CaText -Pattern '符号推导(公式|公理).*定理方程' -TimeoutSec 15
        Scroll-CaPanelToTop
        Start-Sleep -Milliseconds 500
        $null = Capture-Step -Name '09_pnp_derivation_full' -Note 'PNP derivation tab after solve (full window, scrolled to top)'
        $panel2 = Get-CaPanelRegion
        $null = Crop-Step -SourceFile (Join-Path $evidenceDir '09_pnp_derivation_full.png') -Name '10_pnp_derivation_panel' -Region $panel2 -Note 'PNP derivation panel region'
        $null = Scroll-CaPanelToText -Pattern '符号推导(公式|公理).*定理方程'
        Start-Sleep -Milliseconds 400
        $null = Capture-Step -Name '10b_pnp_formula_view_full' -Note 'PNP derivation scrolled to formula boxes (full window)'
        $formula2 = Get-CaFormulaRegion
        $null = Crop-Step -SourceFile (Join-Path $evidenceDir '10b_pnp_formula_view_full.png') -Name '11_pnp_formula_region' -Region $formula2 -Note 'PNP formula boxes region'

        Write-Stage 'STEP 7: PNP metrics -> Q point'
        Invoke-CaMetricsTab
        $null = Capture-Step -Name '12_pnp_metrics_full' -Note 'PNP metrics tab (Q point + AC)'
        $qregion2 = Get-CaMetricsRegion
        $null = Crop-Step -SourceFile (Join-Path $evidenceDir '12_pnp_metrics_full.png') -Name '13_pnp_qpoint_region' -Region $qregion2 -Note 'PNP Q point card region'
    }

    # --- 8. MOS 共源模板 ---
    if (-not $SkipMos) {
        Write-Stage 'STEP 8: open examples/circuit-models/M01-nmos-common-source.circuit'
        $null = Invoke-CaOpenProject -ProjectPath $MosProjectPath -ExpectFileName 'M01-nmos-common-source.circuit'
        $null = Capture-Step -Name '14_mos_loaded' -Note 'M01 NMOS common-source template opened'
        Write-Stage 'STEP 9: solve MOS -> derivation'
        $null = Invoke-CaSolve -ExpectNote 'MOS M01'
        $null = Wait-CaText -Pattern '符号推导(公式|公理).*定理方程' -TimeoutSec 15
        Scroll-CaPanelToTop
        Start-Sleep -Milliseconds 500
        $null = Capture-Step -Name '15_mos_derivation_full' -Note 'MOS derivation tab after solve (full window, scrolled to top)'
        $panel3 = Get-CaPanelRegion
        $null = Crop-Step -SourceFile (Join-Path $evidenceDir '15_mos_derivation_full.png') -Name '16_mos_derivation_panel' -Region $panel3 -Note 'MOS derivation panel region'
        $null = Scroll-CaPanelToText -Pattern '符号推导(公式|公理).*定理方程'
        Start-Sleep -Milliseconds 400
        $null = Capture-Step -Name '16b_mos_formula_view_full' -Note 'MOS derivation scrolled to formula boxes (full window)'
        $formula3 = Get-CaFormulaRegion
        $null = Crop-Step -SourceFile (Join-Path $evidenceDir '16b_mos_formula_view_full.png') -Name '17_mos_formula_region' -Region $formula3 -Note 'MOS formula boxes region'
        Write-Stage 'STEP 10: MOS metrics -> Q point'
        Invoke-CaMetricsTab
        $null = Capture-Step -Name '18_mos_metrics_full' -Note 'MOS metrics tab (Q point)'
        $qregion3 = Get-CaMetricsRegion
        $null = Crop-Step -SourceFile (Join-Path $evidenceDir '18_mos_metrics_full.png') -Name '19_mos_qpoint_region' -Region $qregion3 -Note 'MOS Q point card region'
    }

    Add-CaLog -Kind 'flow-success' -Data @{ phase = $Phase; screenshots = $script:FlowResults.Count }
}
catch {
    $err = $_.Exception.Message
    Write-Host "!! FLOW FAILED at [$script:CurrentStage]: $err"
    Add-CaLog -Kind 'flow-failed' -Data @{ stage = $script:CurrentStage; error = $err }
    try {
        $null = Assert-CaGeometry
        $null = Capture-CaWindow -PhaseName $Phase -FileName 'error_flow_failed' -Note "stage=$($script:CurrentStage) error=$err"
    } catch { Write-Host "  (failed to capture diagnostic screenshot: $($_.Exception.Message))" }
    throw
}
finally {
    if (-not $KeepAppOpen) {
        try { if (Get-Process -Name CircuitAssistant -ErrorAction SilentlyContinue) { [void](Stop-CaApp) } } catch { }
    }
}

Write-Host ''
Write-Host '=== screenshots ==='
foreach ($r in $script:FlowResults) {
    Write-Host ("{0}`n    {1}`n    {2}" -f $r.name, $r.path, $r.note)
}
$summaryPath = Join-Path $evidenceDir 'capture-summary.json'
$summary = [pscustomobject]@{
    phase            = $Phase
    exe              = $exeInfo.FullName
    exeLastWriteTime = $exeInfo.LastWriteTime.ToString('o')
    capturedAt       = (Get-Date).ToString('o')
    screenshots      = $script:FlowResults
}
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Host "summary -> $summaryPath"
Write-Host '=== flow complete ==='
