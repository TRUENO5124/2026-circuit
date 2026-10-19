<#
.SYNOPSIS
    WinUI 3 (CircuitAssistant) 窗口级 UI 捕获与交互辅助脚本。

.DESCRIPTION
    针对未打包的 WinUI 3 桌面应用（CircuitAssistant.exe）提供一套可复用的
    窗口级自动化原语：
      * 启动 / 关闭应用（先 Stop-Process 同名进程）
      * 等待主窗口、读取 GetWindowRect（物理像素，脚本内已声明 Per-Monitor DPI Aware）
      * SetForegroundWindow（AttachThreadInput 兜底，返回并校验真正的前台窗口）
      * PrintWindow(flags=2, PW_RENDERFULLCONTENT) 截图（含重试 + 全黑检测）
      * 按窗口相对坐标用 SetCursorPos + SendInput 鼠标左/右键点击、双击、滚轮
      * System.Windows.Forms.SendKeys 发送按键 / 文本，剪贴板粘贴兜底中文路径
      * UI Automation 元素定位（按名字/控件类型列出按钮、文本、Tab 的物理坐标），
        用于“先在截图上确认按钮位置，再点击”，并把坐标写进日志
      * 证据文件命名规则：$EvidenceRoot\<phase>\<Name>.png
      * 每次截图 / 点击 / 定位都追加 JSONL 日志到 $EvidenceRoot\<phase>\flow-log.jsonl

    本脚本既可 dot-source 后被其它脚本复用（默认 -Action None 不做任何事），
    也可直接命令行调用，例如：
        pwsh -File tools/ui-verify/Capture-Ui.ps1 -Action Start
        pwsh -File tools/ui-verify/Capture-Ui.ps1 -Action Capture -Phase before -Name 01_welcome
        pwsh -File tools/ui-verify/Capture-Ui.ps1 -Action Click -X 855 -Y 660 -Name 02_template -CaptureAfter

.PARAMETER Action
    None | Start | Stop | Restart | Info | Foreground | Capture | Crop | Click | Wheel |
    Keys | Type | Paste | Elements | ClickElement | WaitElement | Sleep

.PARAMETER Phase
    证据子目录：before / after / debug（默认 before）。

.PARAMETER Name
    截图文件名（不含目录，可不带 .png 后缀）。

.PARAMETER ExePath
    应用 exe 路径。默认取旧基线构建 bin\Debug\...\win-x64\CircuitAssistant.exe；
    “改动后”证据用 -ExePath apps\desktop-winui\bin\_verify\CircuitAssistant.exe。

.NOTES
    写作用域：本脚本只读写 tools/ui-verify/ 下的证据文件。
    坐标约定：除特别说明外，所有 X/Y/Width/Height 均为“相对于窗口矩形左上角”
    的物理像素坐标；截图像素与窗口坐标 1:1（PrintWindow 已验证）。
#>
[CmdletBinding()]
param(
    [string]$Action = 'None',
    [string]$Phase = 'before',
    [string]$Name,
    [string]$ExePath,
    [string]$EvidenceRoot,
    [int]$X = 0,
    [int]$Y = 0,
    [int]$Width = 0,
    [int]$Height = 0,
    [int[]]$Crop,
    [ValidateSet('left', 'right')][string]$Button = 'left',
    [switch]$DoubleClick,
    [switch]$CaptureAfter,
    [switch]$NoForeground,
    [switch]$IncludeText,
    [int]$WheelDelta = -120,
    [string]$KeysText,
    [string]$TextValue,
    [string]$NamePattern,
    [string]$ControlType,
    [string]$SourcePath,
    [int]$DelayMs = 600,
    [int]$TimeoutSec = 20,
    [long]$Handle = 0,
    [int]$Index = -1,
    [string]$Note = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 常量
# ---------------------------------------------------------------------------
$script:UiScriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $EvidenceRoot -or $EvidenceRoot.Trim().Length -eq 0) {
    $EvidenceRoot = Join-Path $script:UiScriptDir 'evidence'
}
$script:UiEvidenceRoot = if ([System.IO.Path]::IsPathRooted($EvidenceRoot)) {
    [System.IO.Path]::GetFullPath($EvidenceRoot)
} else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $EvidenceRoot))
}
if (-not $ExePath -or $ExePath.Trim().Length -eq 0) {
    $ExePath = 'apps\desktop-winui\bin\Debug\net9.0-windows10.0.26100.0\win-x64\CircuitAssistant.exe'
}
$script:UiExePath = if ([System.IO.Path]::IsPathRooted($ExePath)) {
    [System.IO.Path]::GetFullPath($ExePath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $ExePath))
}
$script:UiProcessName = 'CircuitAssistant'
$script:UiWindowTitlePattern = '电路教学辅助系统'
$script:UiNativeInitialized = $false
$script:UiDpiAware = $false
$script:UiLastWindow = $null

# ---------------------------------------------------------------------------
# 初始化：P/Invoke、DPI awareness、System.Drawing、WinForms、UIAutomation
# ---------------------------------------------------------------------------
function Initialize-UiNative {
    [CmdletBinding()]
    param()

    if ($script:UiNativeInitialized) { return }

    if (-not ('CircuitAssistant.UiNative' -as [type])) {
        $csharp = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace CircuitAssistant
{
    public static class UiNative
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx; public int dy; public uint mouseData;
            public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk; public ushort wScan; public uint dwFlags;
            public uint time; public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg; public ushort wParamL; public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public INPUTUNION U; }

        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr SetActiveWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int maxCount);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);

        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;

        public static bool EnableDpiAwareness()
        {
            try { return SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch { return false; }
        }

        public static RECT GetRect(IntPtr hWnd)
        {
            RECT rect;
            if (!GetWindowRect(hWnd, out rect)) throw new InvalidOperationException("GetWindowRect failed for HWND " + hWnd);
            return rect;
        }

        public static bool Activate(IntPtr hWnd)
        {
            if (IsIconic(hWnd)) ShowWindow(hWnd, 9 /* SW_RESTORE */);
            IntPtr fg = GetForegroundWindow();
            uint fgPid = 0;
            uint fgTid = GetWindowThreadProcessId(fg, out fgPid);
            uint selfTid = GetCurrentThreadId();
            bool attached = false;
            if (fgTid != 0 && fgTid != selfTid) attached = AttachThreadInput(selfTid, fgTid, true);
            try
            {
                SetForegroundWindow(hWnd);
                BringWindowToTop(hWnd);
                SetActiveWindow(hWnd);
            }
            finally
            {
                if (attached) AttachThreadInput(selfTid, fgTid, false);
            }
            System.Threading.Thread.Sleep(150);
            return GetForegroundWindow() == hWnd;
        }

        public static uint ClickAt(int screenX, int screenY, bool rightButton, bool doubleClick)
        {
            if (!SetCursorPos(screenX, screenY)) return 0;
            System.Threading.Thread.Sleep(90);
            uint down = rightButton ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
            uint up = rightButton ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;
            uint sent = 0;
            int clicks = doubleClick ? 2 : 1;
            for (int i = 0; i < clicks; i++)
            {
                INPUT[] inputs = new INPUT[2];
                inputs[0].type = INPUT_MOUSE;
                inputs[0].U.mi.dwFlags = down;
                inputs[0].U.mi.time = 0;
                inputs[1].type = INPUT_MOUSE;
                inputs[1].U.mi.dwFlags = up;
                inputs[1].U.mi.time = 0;
                sent += SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                System.Threading.Thread.Sleep(doubleClick ? 110 : 45);
            }
            return sent;
        }

        public static uint WheelAt(int screenX, int screenY, int delta)
        {
            if (!SetCursorPos(screenX, screenY)) return 0;
            System.Threading.Thread.Sleep(60);
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].U.mi.dwFlags = MOUSEEVENTF_WHEEL;
            inputs[0].U.mi.mouseData = unchecked((uint)delta);
            return SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static string WindowTitle(IntPtr hWnd)
        {
            var sb = new StringBuilder(512);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string WindowClass(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool SetWindowBounds(IntPtr hWnd, int x, int y, int width, int height)
        {
            if (IsZoomed(hWnd) || IsIconic(hWnd)) ShowWindow(hWnd, 9 /* SW_RESTORE */);
            System.Threading.Thread.Sleep(150);
            const uint SWP_NOZORDER = 0x0004;
            const uint SWP_NOACTIVATE = 0x0010;
            const uint SWP_SHOWWINDOW = 0x0040;
            return SetWindowPos(hWnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public class CaWindowInfo
        {
            public long Hwnd;
            public string Title;
            public string ClassName;
            public int Left;
            public int Top;
            public int Width;
            public int Height;
            public bool Visible;
            public bool Iconic;
            public bool Zoomed;
        }

        public static List<CaWindowInfo> GetProcessWindowInfos(uint pid)
        {
            var list = new List<CaWindowInfo>();
            EnumWindows((h, l) =>
            {
                uint p;
                GetWindowThreadProcessId(h, out p);
                if (p == pid)
                {
                    RECT r = GetRect(h);
                    var info = new CaWindowInfo();
                    info.Hwnd = h.ToInt64();
                    info.Title = WindowTitle(h);
                    info.ClassName = WindowClass(h);
                    info.Left = r.Left;
                    info.Top = r.Top;
                    info.Width = r.Right - r.Left;
                    info.Height = r.Bottom - r.Top;
                    info.Visible = IsWindowVisible(h);
                    info.Iconic = IsIconic(h);
                    info.Zoomed = IsZoomed(h);
                    list.Add(info);
                }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static List<string> GetProcessWindowRows(uint pid)
        {
            var rows = new List<string>();
            foreach (CaWindowInfo info in GetProcessWindowInfos(pid))
            {
                rows.Add(string.Join("\u001F", new string[] {
                    info.Hwnd.ToString(), info.Visible ? "1" : "0", info.Iconic ? "1" : "0", info.Zoomed ? "1" : "0",
                    info.Left.ToString(), info.Top.ToString(), info.Width.ToString(), info.Height.ToString(),
                    info.ClassName, info.Title
                }));
            }
            return rows;
        }

        public static List<string> ListProcessWindows(uint pid)
        {
            var list = new List<string>();
            foreach (CaWindowInfo info in GetProcessWindowInfos(pid))
            {
                list.Add(string.Format("hwnd=0x{0:X} visible={1} title='{2}' class='{3}' rect={4},{5} size={6}x{7} zoomed={8}",
                    info.Hwnd, info.Visible, info.Title, info.ClassName,
                    info.Left, info.Top, info.Width, info.Height, info.Zoomed));
            }
            return list;
        }

        // 在全部进程的可见顶层窗口里按标题正则 + 类名查找窗口（用于 PickerHost 的文件对话框等外部窗口）
        public static long FindVisibleWindow(string titleRegex, string classNameEquals)
        {
            var re = new System.Text.RegularExpressions.Regex(titleRegex);
            long found = 0;
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                RECT r = GetRect(h);
                if (r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) return true;
                if (!string.IsNullOrEmpty(classNameEquals) && WindowClass(h) != classNameEquals) return true;
                if (!re.IsMatch(WindowTitle(h))) return true;
                found = h.ToInt64();
                return false;
            }, IntPtr.Zero);
            return found;
        }
        // 仅按窗口类名查找可见顶层窗口（用于 PickerHost 文件对话框，标题可能是“打开/Open/选择”等）
        public static long FindVisibleWindowByClass(string classNameEquals)
        {
            long found = 0;
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                RECT r = GetRect(h);
                if (r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) return true;
                if (WindowClass(h) != classNameEquals) return true;
                found = h.ToInt64();
                return false;
            }, IntPtr.Zero);
            return found;
        }
    }
}
'@
        Add-Type -TypeDefinition $csharp -Language CSharp -ErrorAction Stop
    }

    # 让本进程 Per-Monitor DPI Aware，保证 GetWindowRect / SetCursorPos / PrintWindow
    # 都使用物理像素，与截图像素 1:1 对齐（WinUI 3 应用本身是 Per-Monitor V2）。
    $script:UiDpiAware = [CircuitAssistant.UiNative]::EnableDpiAwareness()

    # System.Drawing（PrintWindow -> Bitmap -> PNG）
    try { Add-Type -AssemblyName System.Drawing.Common -ErrorAction Stop } catch {
        try { Add-Type -AssemblyName System.Drawing -ErrorAction Stop } catch { throw "无法加载 System.Drawing: $($_.Exception.Message)" }
    }
    # System.Windows.Forms.SendKeys
    try { Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop } catch {
        Write-Verbose "System.Windows.Forms 不可用，SendKeys 将被剪贴板粘贴替代: $($_.Exception.Message)"
    }
    # UI Automation（定位按钮真实坐标）
    try {
        Add-Type -AssemblyName UIAutomationClient -ErrorAction Stop
        Add-Type -AssemblyName UIAutomationTypes -ErrorAction Stop
        $script:UiAutomationAvailable = $true
    } catch {
        $script:UiAutomationAvailable = $false
        Write-Verbose "UIAutomation 不可用: $($_.Exception.Message)"
    }

    $script:UiNativeInitialized = $true
}

# ---------------------------------------------------------------------------
# 内部工具
# ---------------------------------------------------------------------------
function Add-CaLog {
    param(
        [Parameter(Mandatory)][string]$Kind,
        [hashtable]$Data = @{}
    )
    Initialize-UiNative
    $dir = Join-Path $script:UiEvidenceRoot $Phase
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $winRef = $script:UiLastWindow
    $entry = [ordered]@{
        timestamp = (Get-Date).ToString('o')
        phase     = $Phase
        kind      = $Kind
        pid       = $null
        hwnd      = $null
        window    = $null
    }
    if ($winRef) {
        $entry['pid'] = $winRef.ProcessId
        $entry['hwnd'] = ('0x{0:X}' -f $winRef.Hwnd.ToInt64())
        $entry['window'] = "$($winRef.Left),$($winRef.Top),$($winRef.Width)x$($winRef.Height)"
    }
    foreach ($k in $Data.Keys) { $entry[$k] = $Data[$k] }
    $line = ($entry | ConvertTo-Json -Compress -Depth 5)
    $logPath = Join-Path $dir 'flow-log.jsonl'
    [System.IO.File]::AppendAllText($logPath, $line + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-CaDelay {
    param([int]$Min = 300, [int]$Max = 800)
    if ($Max -le $Min) { return $Min }
    return Get-Random -Minimum $Min -Maximum ($Max + 1)
}

function Get-CaWindowInternal {
    [CmdletBinding()]
    param([int]$Retries = 3)
    Initialize-UiNative
    for ($attempt = 1; $attempt -le $Retries; $attempt++) {
        $proc = Get-Process -Name $script:UiProcessName -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
            Select-Object -First 1
        $hwnd = [IntPtr]::Zero
        if ($proc) {
            $hwnd = [IntPtr]$proc.MainWindowHandle
        } else {
            # MainWindowHandle 偶发为 0（窗口重建/隐藏瞬间）：退化为枚举该进程的顶层窗口
            $anyProc = Get-Process -Name $script:UiProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $anyProc) { return $null }
            $proc = $anyProc
            try {
                $rows = [CircuitAssistant.UiNative]::GetProcessWindowRows([uint32]$proc.Id)
                foreach ($row in $rows) {
                    $parts = ([string]$row) -split ([char]0x1F)
                    if ($parts.Count -lt 10) { continue }
                    if ($parts[1] -ne '1') { continue }   # visible
                    $candidate = [long]$parts[0]
                    $w = [int]$parts[6]; $h = [int]$parts[7]
                    if ($w -lt 400 -or $h -lt 300) { continue }
                    if ($parts[9] -notmatch $script:UiWindowTitlePattern) { continue }
                    $hwnd = [IntPtr]$candidate
                    break
                }
            } catch { }
            if ($hwnd -eq [IntPtr]::Zero) {
                Start-Sleep -Milliseconds 250
                continue
            }
        }
        $rect = [CircuitAssistant.UiNative]::GetRect($hwnd)
        $obj = [pscustomobject]@{
            ProcessId = $proc.Id
            Hwnd      = $hwnd
            Title     = [CircuitAssistant.UiNative]::WindowTitle($hwnd)
            Left      = $rect.Left
            Top       = $rect.Top
            Right     = $rect.Right
            Bottom    = $rect.Bottom
            Width     = $rect.Right - $rect.Left
            Height    = $rect.Bottom - $rect.Top
            Dpi       = [CircuitAssistant.UiNative]::GetDpiForWindow($hwnd)
            Zoomed    = [CircuitAssistant.UiNative]::IsZoomed($hwnd)
            Iconic    = [CircuitAssistant.UiNative]::IsIconic($hwnd)
            ExePath   = $proc.Path
            DpiAware  = $script:UiDpiAware
        }
        $script:UiLastWindow = $obj
        return $obj
    }
    return $null
}

# ---------------------------------------------------------------------------
# 公共函数：启动 / 关闭 / 窗口信息
# ---------------------------------------------------------------------------
function Stop-CaApp {
    [CmdletBinding()]
    param([int]$TimeoutSec = 15)
    Initialize-UiNative
    $procs = @(Get-Process -Name $script:UiProcessName -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { return $false }
    $pids = ($procs | ForEach-Object { $_.Id }) -join ','
    Stop-Process -Name $script:UiProcessName -Force -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Name $script:UiProcessName -ErrorAction SilentlyContinue)) { return $true }
        Start-Sleep -Milliseconds 200
    }
    throw "CircuitAssistant 进程在 ${TimeoutSec}s 内未退出 (pid=$pids)"
}

function Start-CaApp {
    [CmdletBinding()]
    param(
        [string]$Exe = $script:UiExePath,
        [int]$TimeoutSec = 60,
        [switch]$KeepExisting
    )
    Initialize-UiNative
    if (-not (Test-Path -LiteralPath $Exe)) { throw "找不到应用 exe: $Exe" }
    if (-not $KeepExisting) {
        if (Get-Process -Name $script:UiProcessName -ErrorAction SilentlyContinue) {
            [void](Stop-CaApp)
        }
    }
    $started = Start-Process -FilePath $Exe -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $win = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
        try { $started.Refresh() } catch {}
        $win = Get-CaWindowInternal
        if ($win -and $win.Title -match $script:UiWindowTitlePattern -and $win.Width -gt 200) { break }
        $win = $null
    }
    if (-not $win) { throw "应用启动 ${TimeoutSec}s 后仍未出现主窗口 (pid=$($started.Id))" }
    # 等 XAML 首帧渲染完成（欢迎页/主界面元素出现）
    [void](Set-CaForeground -Quiet)
    Start-Sleep -Milliseconds 1500
    [void](Get-CaWindowInternal)
    Add-CaLog -Kind 'app-start' -Data @{
        exe = $Exe; title = $win.Title; action = 'Start-CaApp'
        dpiAware = $script:UiDpiAware
    }
    return $win
}

function Get-CaWindow {
    [CmdletBinding()]
    param([switch]$AsJson)
    $win = Get-CaWindowInternal
    if (-not $win) { throw "未找到 CircuitAssistant 主窗口（进程未运行？）" }
    if ($AsJson) { return ($win | ConvertTo-Json -Depth 4) }
    return $win
}

function Set-CaWindowRect {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$Left,
        [Parameter(Mandatory)][int]$Top,
        [Parameter(Mandatory)][int]$WidthPx,
        [Parameter(Mandatory)][int]$HeightPx,
        [switch]$Foreground
    )
    Initialize-UiNative
    $win = Get-CaWindowInternal
    if (-not $win) { throw '窗口不存在，无法定尺寸' }
    $ok = [CircuitAssistant.UiNative]::SetWindowBounds($win.Hwnd, $Left, $Top, $WidthPx, $HeightPx)
    Start-Sleep -Milliseconds 700
    $win2 = Get-CaWindowInternal
    if ($Foreground) { [void](Set-CaForeground -Quiet) }
    Add-CaLog -Kind 'window-resize' -Data @{
        action = 'Set-CaWindowRect'; requested = "$Left,$Top,${WidthPx}x${HeightPx}"
        actual = if ($win2) { "$($win2.Left),$($win2.Top),$($win2.Width)x$($win2.Height)" } else { $null }
        ok = $ok
    }
    if (-not $win2) { throw '定尺寸后未找到窗口' }
    return $win2
}

function Get-CaTopLevelWindows {
    [CmdletBinding()]
    param()
    Initialize-UiNative
    $proc = Get-Process -Name $script:UiProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $proc) { throw '进程未运行' }
    $rows = [CircuitAssistant.UiNative]::GetProcessWindowRows([uint32]$proc.Id)
    $result = @()
    foreach ($row in $rows) {
        $parts = ([string]$row) -split ([char]0x1F)
        if ($parts.Count -lt 10) { continue }
        $result += [pscustomobject]@{
            Hwnd      = [long]$parts[0]
            Visible   = ($parts[1] -eq '1')
            Iconic    = ($parts[2] -eq '1')
            Zoomed    = ($parts[3] -eq '1')
            Left      = [int]$parts[4]
            Top       = [int]$parts[5]
            Width     = [int]$parts[6]
            Height    = [int]$parts[7]
            ClassName = $parts[8]
            Title     = $parts[9]
        }
    }
    return $result
}

function Find-CaWindowByTitle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$TitlePattern,
        [int]$MinWidth = 200,
        [switch]$IncludeInvisible
    )
    $wins = Get-CaTopLevelWindows
    foreach ($w in $wins) {
        if (-not $IncludeInvisible -and -not $w.Visible) { continue }
        if ($w.Width -lt $MinWidth) { continue }
        if ($w.Title -match $TitlePattern) { return $w }
    }
    return $null
}

function Capture-CaHandle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][long]$WindowHandle,
        [Parameter(Mandatory)][string]$FileName,
        [string]$PhaseName,
        [switch]$NoForeground,
        [int]$Retries = 5
    )
    $effectivePhase = if ($PhaseName -and $PhaseName.Trim().Length -gt 0) { $PhaseName } else { $Phase }
    $Phase = $effectivePhase
    Initialize-UiNative
    $hwnd = [IntPtr]$WindowHandle
    $outName = $FileName
    if ($outName -notmatch '\.png$') { $outName = "$outName.png" }
    $outDir = Join-Path $script:UiEvidenceRoot $effectivePhase
    if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
    $outPath = Join-Path $outDir $outName
    $lastError = $null
    for ($attempt = 1; $attempt -le $Retries; $attempt++) {
        try {
            $rect = [CircuitAssistant.UiNative]::GetRect($hwnd)
            $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
            if ($w -le 0 -or $h -le 0) { throw "窗口尺寸无效: ${w}x${h}" }
            if (-not $NoForeground) { [void][CircuitAssistant.UiNative]::Activate($hwnd) }
            Start-Sleep -Milliseconds (Get-Random -Minimum 300 -Maximum 700)
            $bmp = New-Object System.Drawing.Bitmap $w, $h
            try {
                $g = [System.Drawing.Graphics]::FromImage($bmp)
                try {
                    $hdc = $g.GetHdc()
                    try { $okPrint = [CircuitAssistant.UiNative]::PrintWindow($hwnd, $hdc, 2) }
                    finally { $g.ReleaseHdc($hdc) }
                } finally { $g.Dispose() }
                if (-not (Test-CaBitmapContent -Bitmap $bmp)) { throw '截图全黑/无内容' }
                $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
                Add-CaLog -Kind 'capture-window' -Data @{
                    file = $outPath; handle = ('0x{0:X}' -f $WindowHandle); imageWidth = $bmp.Width; imageHeight = $bmp.Height
                    bytes = (Get-Item -LiteralPath $outPath).Length; retry = $attempt; printWindow = $okPrint
                }
                return $outPath
            } finally { $bmp.Dispose() }
        } catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Milliseconds 400
        }
    }
    throw "按句柄截图失败: 0x$('{0:X}' -f $WindowHandle) / $lastError"
}

function Set-CaForeground {
    [CmdletBinding()]
    param([switch]$Quiet)
    Initialize-UiNative
    $win = Get-CaWindowInternal
    if (-not $win) { throw '未找到应用窗口，无法置前' }
    $ok = [CircuitAssistant.UiNative]::Activate($win.Hwnd)
    if (-not $ok) {
        # 兜底：HWND_TOPMOST 提升 -> Activate -> HWND_NOTOPMOST（不要用鼠标点标题栏兜底：
        # 连续两次点击标题栏会被系统识别为双击而最大化窗口，破坏截图几何一致性）。
        $flags = 0x0001 -bor 0x0002 -bor 0x0040   # SWP_NOSIZE|SWP_NOMOVE|SWP_SHOWWINDOW
        [void][CircuitAssistant.UiNative]::SetWindowPos($win.Hwnd, [IntPtr](-1), 0, 0, 0, 0, $flags)
        Start-Sleep -Milliseconds 200
        $ok = [CircuitAssistant.UiNative]::Activate($win.Hwnd)
        $flags2 = 0x0001 -bor 0x0002             # SWP_NOSIZE|SWP_NOMOVE
        [void][CircuitAssistant.UiNative]::SetWindowPos($win.Hwnd, [IntPtr](-2), 0, 0, 0, 0, $flags2)
        Start-Sleep -Milliseconds 150
        if (-not $ok) { $ok = ([CircuitAssistant.UiNative]::GetForegroundWindow() -eq $win.Hwnd) }
    }
    if (-not $ok -and -not $Quiet) {
        Add-CaLog -Kind 'foreground-failed' -Data @{ reason = 'SetForegroundWindow/Activate/topmost 均失败' }
    }
    if ($ok) { $script:UiLastWindow = $win }
    return $ok
}

# ---------------------------------------------------------------------------
# 外部窗口（例如 PickerHost.exe 的文件选择对话框）定位 / 激活 / 键盘输入
# ---------------------------------------------------------------------------
function Get-CaExternalWindow {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$TitlePattern,
        [string]$ClassName = '',
        [int]$TimeoutSec = 0
    )
    Initialize-UiNative
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ($true) {
        $raw = [CircuitAssistant.UiNative]::FindVisibleWindow($TitlePattern, $ClassName)
        if ($raw -ne 0) {
            $hwnd = [IntPtr]$raw
            $rect = [CircuitAssistant.UiNative]::GetRect($hwnd)
            $info = [pscustomobject]@{
                Hwnd      = $raw
                Title     = [CircuitAssistant.UiNative]::WindowTitle($hwnd)
                ClassName = [CircuitAssistant.UiNative]::WindowClass($hwnd)
                Left      = $rect.Left
                Top       = $rect.Top
                Width     = $rect.Right - $rect.Left
                Height    = $rect.Bottom - $rect.Top
            }
            Add-CaLog -Kind 'external-window' -Data @{
                pattern = $TitlePattern; className = $ClassName; handle = ('0x{0:X}' -f $raw)
                title = $info.Title; rect = "$($info.Left),$($info.Top),$($info.Width)x$($info.Height)"
            }
            return $info
        }
        if ((Get-Date) -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 300
    }
}

function Get-CaPickerWindows {
    [CmdletBinding()]
    param()
    Initialize-UiNative
    $result = @()
    $procs = @(Get-Process -Name 'PickerHost' -ErrorAction SilentlyContinue)
    foreach ($proc in $procs) {
        $rows = [CircuitAssistant.UiNative]::GetProcessWindowRows([uint32]$proc.Id)
        foreach ($row in $rows) {
            $parts = ([string]$row) -split ([char]0x1F)
            if ($parts.Count -lt 10) { continue }
            $result += [pscustomobject]@{
                Hwnd      = [long]$parts[0]
                Visible   = ($parts[1] -eq '1')
                Iconic    = ($parts[2] -eq '1')
                Zoomed    = ($parts[3] -eq '1')
                Left      = [int]$parts[4]
                Top       = [int]$parts[5]
                Width     = [int]$parts[6]
                Height    = [int]$parts[7]
                ClassName = $parts[8]
                Title     = $parts[9]
                ProcessId = $proc.Id
            }
        }
    }
    return $result
}

function Stop-CaPickerHosts {
    [CmdletBinding()]
    param([int]$TimeoutSec = 8)
    Initialize-UiNative
    $procs = @(Get-Process -Name 'PickerHost' -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { return 0 }
    $count = $procs.Count
    $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Name 'PickerHost' -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 200
    }
    Add-CaLog -Kind 'pickerhost-cleanup' -Data @{ killed = $count }
    return $count
}

function Get-CaPickerDialog {
    [CmdletBinding()]
    param(
        [int]$TimeoutSec = 10,
        [string[]]$TitlePatterns = @('^打开$', '^Open$', '^打开\(O\)$', '^Open \(O\)$')
    )
    Initialize-UiNative
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ($true) {
        # 首选：PickerHost.exe 进程的可见顶层窗口（文件选择对话框的实际宿主进程）。
        # 注意区分“打开”与“另存为/保存”对话框：只接受打开类标题，排除保存类。
        $all = @(Get-CaPickerWindows | Where-Object { $_.Visible -and $_.Width -gt 200 -and $_.Height -gt 100 })
        $picker = @($all | Where-Object { $_.Title -match '打开|Open' })
        if ($picker.Count -eq 0) {
            $picker = @($all | Where-Object { $_.Title -notmatch '另存为|保存|Save|打印|Print|图表' })
        }
        if ($picker.Count -gt 0) {
            $d = $picker[0]
            $info = [pscustomobject]@{
                Hwnd = $d.Hwnd; Title = $d.Title; ClassName = $d.ClassName
                Left = $d.Left; Top = $d.Top; Width = $d.Width; Height = $d.Height
            }
            Add-CaLog -Kind 'external-window' -Data @{ source = 'PickerHost'; handle = ('0x{0:X}' -f $d.Hwnd); title = $d.Title; className = $d.ClassName }
            return $info
        }
        # 次选：严格标题匹配（避免误命中标题里带“文件”的资源管理器窗口）
        foreach ($tp in $TitlePatterns) {
            $dlg = Get-CaExternalWindow -TitlePattern $tp -ClassName '#32770' -TimeoutSec 0
            if ($dlg) { return $dlg }
        }
        if ((Get-Date) -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 300
    }
}

function Set-CaClipboardText {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Text)
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try {
            Set-Clipboard -Value $Text -ErrorAction Stop
            return $true
        } catch {
            Start-Sleep -Milliseconds 300
        }
    }
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
        [System.Windows.Forms.Clipboard]::SetText($Text)
        return $true
    } catch { }
    throw '剪贴板写入失败（可能被其它进程/输入法占用）'
}

function Set-CaExternalForeground {
    [CmdletBinding()]
    param([Parameter(Mandatory)][long]$WindowHandle)
    Initialize-UiNative
    $ok = [CircuitAssistant.UiNative]::Activate([IntPtr]$WindowHandle)
    Start-Sleep -Milliseconds 200
    return $ok
}

function Send-CaKeysTo {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][long]$WindowHandle,
        [Parameter(Mandatory)][string]$Keys
    )
    Initialize-UiNative
    [void](Set-CaExternalForeground -WindowHandle $WindowHandle)
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
    Add-CaLog -Kind 'keys-external' -Data @{ handle = ('0x{0:X}' -f $WindowHandle); keys = $Keys; note = $Note }
    return $true
}

function Send-CaPasteTo {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][long]$WindowHandle,
        [Parameter(Mandatory)][string]$Text,
        [switch]$SelectAllFirst
    )
    Initialize-UiNative
    [void](Set-CaExternalForeground -WindowHandle $WindowHandle)
    [void](Set-CaClipboardText -Text $Text)
    Start-Sleep -Milliseconds 250
    if ($SelectAllFirst) {
        [System.Windows.Forms.SendKeys]::SendWait('^a')
        Start-Sleep -Milliseconds 150
    }
    [System.Windows.Forms.SendKeys]::SendWait('^v')
    Start-Sleep -Milliseconds 300
    Add-CaLog -Kind 'paste-external' -Data @{ handle = ('0x{0:X}' -f $WindowHandle); text = $Text; note = $Note }
    return $true
}

function Click-CaExternalElement {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][long]$WindowHandle,
        [Parameter(Mandatory)][string]$NamePattern,
        [int]$TimeoutSec = 8,
        [switch]$DoubleClick,
        [switch]$ActivateFirst
    )
    Initialize-UiNative
    if ($ActivateFirst) {
        [void](Set-CaExternalForeground -WindowHandle $WindowHandle)
        Start-Sleep -Milliseconds 250
    }
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ($true) {
        try {
            $root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$WindowHandle)
            if ($root) {
                $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                    [System.Windows.Automation.Condition]::TrueCondition)
                for ($i = 0; $i -lt $all.Count; $i++) {
                    try {
                        $el = $all.Item($i)
                        $nm = $el.Current.Name
                        if (-not $nm -or $nm -notmatch $NamePattern) { continue }
                        $br = $el.Current.BoundingRectangle
                        if ($br.Width -le 0 -or $br.Height -le 0) { continue }
                        $sx = [int]($br.X + $br.Width / 2)
                        $sy = [int]($br.Y + $br.Height / 2)
                        Add-CaLog -Kind 'click-external-element' -Data @{
                            handle = ('0x{0:X}' -f $WindowHandle); pattern = $NamePattern; name = $nm
                            screenX = $sx; screenY = $sy; double = $DoubleClick.IsPresent; note = $Note
                        }
                        # 先单击激活对话框窗口（跨进程首次点击可能只用于激活），再执行目标点击
                        [void](Send-CaClickScreen -ScreenX $sx -ScreenY $sy -PostClickDelayMs 250)
                        if ($DoubleClick) {
                            [void](Send-CaClickScreen -ScreenX $sx -ScreenY $sy -Double -PostClickDelayMs 800)
                        } else {
                            [void](Send-CaClickScreen -ScreenX $sx -ScreenY $sy -PostClickDelayMs 600)
                        }
                        return $true
                    } catch { }
                }
            }
        } catch { }
        if ((Get-Date) -ge $deadline) { return $false }
        Start-Sleep -Milliseconds 300
    }
}

function Wait-CaWindowGone {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][long]$WindowHandle,
        [int]$TimeoutSec = 15
    )
    Initialize-UiNative
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (-not [CircuitAssistant.UiNative]::IsWindowVisible([IntPtr]$WindowHandle)) {
            Add-CaLog -Kind 'window-gone' -Data @{ handle = ('0x{0:X}' -f $WindowHandle) }
            return $true
        }
        Start-Sleep -Milliseconds 300
    }
    Add-CaLog -Kind 'window-gone-timeout' -Data @{ handle = ('0x{0:X}' -f $WindowHandle) }
    return $false
}

# ---------------------------------------------------------------------------
# 鼠标 / 键盘
# ---------------------------------------------------------------------------
function ConvertTo-CaWindowPoint {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$WindowX,
        [Parameter(Mandatory)][int]$WindowY
    )
    $win = Get-CaWindowInternal
    if (-not $win) { throw '未找到应用窗口' }
    return [pscustomobject]@{
        ScreenX = $win.Left + $WindowX
        ScreenY = $win.Top + $WindowY
        WindowX = $WindowX
        WindowY = $WindowY
        Window  = $win
    }
}

function Send-CaClick {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$WindowX,
        [Parameter(Mandatory)][int]$WindowY,
        [ValidateSet('left', 'right')][string]$MouseButton = 'left',
        [switch]$Double,
        [switch]$NoActivate,
        [int]$PostClickDelayMs = 450
    )
    Initialize-UiNative
    if (-not $NoActivate) {
        $fg = Set-CaForeground -Quiet
        if ($fg) { Start-Sleep -Milliseconds 200 }
    }
    $win = Get-CaWindowInternal
    if (-not $win) { throw '未找到应用窗口' }
    if ($WindowX -lt 0 -or $WindowY -lt 0 -or $WindowX -ge $win.Width -or $WindowY -ge $win.Height) {
        throw "点击坐标 ($WindowX,$WindowY) 超出窗口范围 $($win.Width)x$($win.Height)"
    }
    $sx = $win.Left + $WindowX
    $sy = $win.Top + $WindowY
    $sent = [CircuitAssistant.UiNative]::ClickAt($sx, $sy, ($MouseButton -eq 'right'), $Double.IsPresent)
    if ($PostClickDelayMs -gt 0) { Start-Sleep -Milliseconds $PostClickDelayMs }
    $fgAfter = [CircuitAssistant.UiNative]::GetForegroundWindow() -eq $win.Hwnd
    $result = [pscustomobject]@{
        ScreenX = $sx; ScreenY = $sy
        WindowX = $WindowX; WindowY = $WindowY
        SentInputs = $sent
        Double = $Double.IsPresent
        ForegroundAfter = $fgAfter
        WindowWidth = $win.Width; WindowHeight = $win.Height
    }
    Add-CaLog -Kind 'click' -Data @{
        action = 'Send-CaClick'; screenX = $sx; screenY = $sy
        windowX = $WindowX; windowY = $WindowY; button = $MouseButton
        double = $Double.IsPresent; sentInputs = $sent
        foregroundAfter = $fgAfter; note = $Note
    }
    return $result
}

function Send-CaClickScreen {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$ScreenX,
        [Parameter(Mandatory)][int]$ScreenY,
        [ValidateSet('left', 'right')][string]$MouseButton = 'left',
        [switch]$Double,
        [long]$ActivateHandle = 0,
        [int]$PostClickDelayMs = 500
    )
    Initialize-UiNative
    if ($ActivateHandle -ne 0) { [void][CircuitAssistant.UiNative]::Activate([IntPtr]$ActivateHandle) }
    Start-Sleep -Milliseconds 150
    $sent = [CircuitAssistant.UiNative]::ClickAt($ScreenX, $ScreenY, ($MouseButton -eq 'right'), $Double.IsPresent)
    if ($PostClickDelayMs -gt 0) { Start-Sleep -Milliseconds $PostClickDelayMs }
    $result = [pscustomobject]@{
        ScreenX = $ScreenX; ScreenY = $ScreenY; SentInputs = $sent
        Double = $Double.IsPresent
    }
    Add-CaLog -Kind 'click-screen' -Data @{
        action = 'Send-CaClickScreen'; screenX = $ScreenX; screenY = $ScreenY
        button = $MouseButton; double = $Double.IsPresent; sentInputs = $sent; note = $Note
    }
    return $result
}

function Send-CaWheel {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$WindowX,
        [Parameter(Mandatory)][int]$WindowY,
        [int]$Delta = -120
    )
    Initialize-UiNative
    [void](Set-CaForeground -Quiet)
    Start-Sleep -Milliseconds 150
    $win = Get-CaWindowInternal
    $sx = $win.Left + $WindowX
    $sy = $win.Top + $WindowY
    $sent = [CircuitAssistant.UiNative]::WheelAt($sx, $sy, $Delta)
    Add-CaLog -Kind 'wheel' -Data @{ screenX = $sx; screenY = $sy; delta = $Delta; sentInputs = $sent; note = $Note }
    return [pscustomobject]@{ ScreenX = $sx; ScreenY = $sy; SentInputs = $sent }
}

function Send-CaKeys {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Keys)
    Initialize-UiNative
    [void](Set-CaForeground -Quiet)
    Start-Sleep -Milliseconds 250
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
    Add-CaLog -Kind 'keys' -Data @{ keys = $Keys; note = $Note }
    return $true
}

function Send-CaPaste {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Text)
    Initialize-UiNative
    [void](Set-CaForeground -Quiet)
    [void](Set-CaClipboardText -Text $Text)
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait('^v')
    Start-Sleep -Milliseconds 250
    Add-CaLog -Kind 'paste' -Data @{ text = $Text; note = $Note }
    return $true
}

function Send-CaText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Text,
        [switch]$ForcePaste
    )
    Initialize-UiNative
    $isAscii = ($Text -match '^[\x20-\x7E]*$')
    if ($ForcePaste -or -not $isAscii) {
        return Send-CaPaste -Text $Text
    }
    return Send-CaKeys -Keys $Text
}

# ---------------------------------------------------------------------------
# UI Automation 定位
# ---------------------------------------------------------------------------
function Get-CaUiElements {
    [CmdletBinding()]
    param(
        [string]$NamePattern,
        [string]$TypeFilter,
        [switch]$IncludeTextOnly,
        [switch]$IncludeOffscreen,
        [int]$TimeoutSec = -1
    )
    Initialize-UiNative
    if (-not $script:UiAutomationAvailable) { throw 'UI Automation 不可用' }
    $deadline = if ($TimeoutSec -gt 0) { (Get-Date).AddSeconds($TimeoutSec) } else { (Get-Date) }
    $minimalNames = @('Button', 'Text', 'TabItem', 'MenuItem', 'Hyperlink', 'Expander', 'ComboBox', 'Edit', 'CheckBox', 'Slider', 'ToggleSwitch', 'ListItem')
    while ($true) {
        try {
            $win = Get-CaWindowInternal
            if (-not $win) {
                if ($TimeoutSec -le 0) { return @() }
            } else {
                $root = [System.Windows.Automation.AutomationElement]::FromHandle($win.Hwnd)
                if ($root) {
                    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                        [System.Windows.Automation.Condition]::TrueCondition)
                    $results = @()
                    for ($i = 0; $i -lt $all.Count; $i++) {
                        try {
                            $el = $all.Item($i)
                            $cur = $el.Current
                            $type = ($cur.ControlType.ProgrammaticName -replace '^ControlType\.', '')
                            $nm = $cur.Name
                            $br = $cur.BoundingRectangle
                            if ($br.Width -le 0 -or $br.Height -le 0) { continue }
                            if (-not $IncludeOffscreen -and $cur.IsOffscreen) { continue }
                            if ($TypeFilter -and $type -ne $TypeFilter) { continue }
                            if ($IncludeTextOnly -and $type -ne 'Text') { continue }
                            if (-not $TypeFilter -and -not $IncludeTextOnly -and ($minimalNames -notcontains $type)) { continue }
                            if ($NamePattern -and ($nm -notmatch $NamePattern)) { continue }
                            $invokable = $false
                            try {
                                $invokePattern = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                                if ($invokePattern) { $invokable = $true }
                            } catch { }
                            $results += [pscustomobject]@{
                                    Type           = $type
                                    Name           = $nm
                                    AutomationId   = $cur.AutomationId
                                    WindowX        = [int]($br.X - $win.Left)
                                    WindowY        = [int]($br.Y - $win.Top)
                                    Width          = [int]$br.Width
                                    Height         = [int]$br.Height
                                    CenterWindowX  = [int]($br.X - $win.Left + $br.Width / 2)
                                    CenterWindowY  = [int]($br.Y - $win.Top + $br.Height / 2)
                                    ScreenCenterX  = [int]($br.X + $br.Width / 2)
                                    ScreenCenterY  = [int]($br.Y + $br.Height / 2)
                                    Invokable      = $invokable
                                    IsOffscreen    = [bool]$cur.IsOffscreen
                                }
                        } catch { }
                    }
                    if ($results.Count -gt 0 -or (Get-Date) -ge $deadline) {
                        $script:UiLastWindow = $win
                        return $results
                    }
                }
            }
        } catch {
            # 窗口切换/重建瞬间 UIA 树可能报 ElementNotAvailable：重试而不是让流程失败
            Add-CaLog -Kind 'uia-retry' -Data @{ error = $_.Exception.Message }
        }
        if ((Get-Date) -ge $deadline) { return @() }
        Start-Sleep -Milliseconds 300
    }
}

function Wait-CaUiElement {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Pattern,
        [string]$TypeFilter,
        [int]$TimeoutSec = 20
    )
    $el = Get-CaUiElements -NamePattern $Pattern -TypeFilter $TypeFilter -TimeoutSec $TimeoutSec
    if (-not $el -or @($el).Count -eq 0) {
        Add-CaLog -Kind 'wait-element-timeout' -Data @{ pattern = $Pattern; type = $TypeFilter }
        return $null
    }
    $first = @($el)[0]
    Add-CaLog -Kind 'element-found' -Data @{
        pattern = $Pattern; type = $first.Type; name = $first.Name
        windowX = $first.WindowX; windowY = $first.WindowY
        width = $first.Width; height = $first.Height
    }
    return $first
}

function Invoke-CaClickElement {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Pattern,
        [string]$TypeFilter,
        [int]$TimeoutSec = 15,
        [switch]$CaptureAfter,
        [string]$CaptureName
    )
    $el = Wait-CaUiElement -Pattern $Pattern -TypeFilter $TypeFilter -TimeoutSec $TimeoutSec
    if (-not $el) {
        Add-CaLog -Kind 'click-element-failed' -Data @{ pattern = $Pattern; reason = 'element not found' }
        throw "找不到 UI 元素：$Pattern"
    }
    $click = Send-CaClick -WindowX $el.CenterWindowX -WindowY $el.CenterWindowY -PostClickDelayMs 600
    Add-CaLog -Kind 'click-element' -Data @{
        pattern = $Pattern; elementName = $el.Name; elementType = $el.Type
        elementWindowX = $el.WindowX; elementWindowY = $el.WindowY
        elementWidth = $el.Width; elementHeight = $el.Height
        centerWindowX = $el.CenterWindowX; centerWindowY = $el.CenterWindowY
        screenX = $click.ScreenX; screenY = $click.ScreenY; note = $Note
    }
    if ($CaptureAfter) {
        $nm = if ($CaptureName) { $CaptureName } else { "click-$([regex]::Replace($Pattern, '[^\w\u4e00-\u9fa5-]', '_'))" }
        [void](Capture-CaWindow -PhaseName $Phase -FileName $nm -Note "after click element $Pattern" -NoForeground)
    }
    return $el
}

# ---------------------------------------------------------------------------
# Pivot 标签页（TabItem）定位与 UIA 选择
# ---------------------------------------------------------------------------
function Get-CaTabItems {
    [CmdletBinding()]
    param()
    Initialize-UiNative
    $win = Get-CaWindowInternal
    if (-not $win) { throw '窗口不存在' }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($win.Hwnd)
    $cond = New-Object System.Windows.Automation.PropertyCondition -ArgumentList ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::TabItem)
    $tabs = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $list = @()
    for ($i = 0; $i -lt $tabs.Count; $i++) {
        $tab = $tabs.Item($i)
        $br = $tab.Current.BoundingRectangle
        $list += [pscustomobject]@{
            Index       = $i
            Name        = $tab.Current.Name
            WindowX     = [int]($br.X - $win.Left)
            WindowY     = [int]($br.Y - $win.Top)
            Width       = [int]$br.Width
            Height      = [int]$br.Height
            IsOffscreen = [bool]$tab.Current.IsOffscreen
            Element     = $tab
        }
    }
    return $list
}

function Select-CaTab {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$Index,
        [int]$PostDelayMs = 500
    )
    Initialize-UiNative
    $tabs = @(Get-CaTabItems)
    if ($Index -lt 0 -or $Index -ge $tabs.Count) { throw "标签页索引越界：$Index（共 $($tabs.Count) 个）" }
    $tab = $tabs[$Index]
    try {
        $pattern = $tab.Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $pattern.Select()
    } catch {
        throw "标签页 $Index 不支持 SelectionItemPattern：$($_.Exception.Message)"
    }
    Start-Sleep -Milliseconds $PostDelayMs
    Add-CaLog -Kind 'select-tab' -Data @{ index = $Index; count = $tabs.Count; note = $Note }
    return [pscustomobject]@{ Index = $Index; Count = $tabs.Count; WindowX = $tab.WindowX; WindowY = $tab.WindowY }
}

# ---------------------------------------------------------------------------
# 截图
# ---------------------------------------------------------------------------
function Test-CaBitmapContent {
    param(
        [Parameter(Mandatory)][System.Drawing.Bitmap]$Bitmap,
        [int]$SampleStep = 23,
        [double]$MinNonBlackRatio = 0.02
    )
    $total = 0; $nonBlack = 0
    for ($yy = 0; $yy -lt $Bitmap.Height; $yy += $SampleStep) {
        for ($xx = 0; $xx -lt $Bitmap.Width; $xx += $SampleStep) {
            $c = $Bitmap.GetPixel($xx, $yy)
            $total++
            if (($c.R + $c.G + $c.B) -gt 24) { $nonBlack++ }
        }
    }
    if ($total -eq 0) { return $false }
    return (($nonBlack / $total) -ge $MinNonBlackRatio)
}

function Capture-CaWindow {
    [CmdletBinding()]
    param(
        [string]$PhaseName,
        [string]$FileName,
        [int[]]$CropRect,
        [switch]$NoForeground,
        [int]$Retries = 6,
        [string]$Note = ''
    )
    $effectivePhase = if ($PhaseName -and $PhaseName.Trim().Length -gt 0) { $PhaseName } else { $Phase }
    $effectiveName = if ($FileName -and $FileName.Trim().Length -gt 0) { $FileName } else { $Name }
    $Phase = $effectivePhase
    Initialize-UiNative
    if (-not $effectiveName) { throw 'Capture-CaWindow 需要 -FileName 参数' }
    if (-not $NoForeground) {
        [void](Set-CaForeground -Quiet)
        Start-Sleep -Milliseconds (Get-Random -Minimum 200 -Maximum 400)
    }
    $outName = $effectiveName
    if ($outName -notmatch '\.png$') { $outName = "$outName.png" }
    $outDir = Join-Path $script:UiEvidenceRoot $effectivePhase
    if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
    $outPath = Join-Path $outDir $outName

    $lastError = $null
    for ($attempt = 1; $attempt -le $Retries; $attempt++) {
        try {
            $win = Get-CaWindowInternal
            if (-not $win) { throw '窗口不存在' }
            Start-Sleep -Milliseconds (Get-Random -Minimum 300 -Maximum 800)
            $win = Get-CaWindowInternal
            $bmp = New-Object System.Drawing.Bitmap ($win.Width), ($win.Height)
            try {
                $g = [System.Drawing.Graphics]::FromImage($bmp)
                try {
                    $hdc = $g.GetHdc()
                    try {
                        $okPrint = [CircuitAssistant.UiNative]::PrintWindow($win.Hwnd, $hdc, 2)
                    } finally { $g.ReleaseHdc($hdc) }
                } finally { $g.Dispose() }
                $hasContent = Test-CaBitmapContent -Bitmap $bmp
                if (-not $hasContent) { throw '截图全黑/无内容' }
                $final = $bmp
                if ($CropRect -and $CropRect.Count -eq 4) {
                    $cx = $CropRect[0]; $cy = $CropRect[1]; $cw = $CropRect[2]; $ch = $CropRect[3]
                    $cx = [Math]::Max(0, [Math]::Min($cx, $bmp.Width - 1))
                    $cy = [Math]::Max(0, [Math]::Min($cy, $bmp.Height - 1))
                    $cw = [Math]::Max(1, [Math]::Min($cw, $bmp.Width - $cx))
                    $ch = [Math]::Max(1, [Math]::Min($ch, $bmp.Height - $cy))
                    $crop = New-Object System.Drawing.Bitmap $cw, $ch
                    $cg = [System.Drawing.Graphics]::FromImage($crop)
                    try { $cg.DrawImage($bmp, (New-Object System.Drawing.Rectangle 0, 0, $cw, $ch), (New-Object System.Drawing.Rectangle $cx, $cy, $cw, $ch), [System.Drawing.GraphicsUnit]::Pixel) }
                    finally { $cg.Dispose() }
                    $final = $crop
                }
                $final.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
                $finalImgW = $final.Width; $finalImgH = $final.Height; $finalSize = (Get-Item -LiteralPath $outPath).Length
                if (-not [object]::ReferenceEquals($final, $bmp)) { $final.Dispose() }
                Add-CaLog -Kind 'capture' -Data @{
                    file = $outPath; imageWidth = $finalImgW; imageHeight = $finalImgH; bytes = $finalSize
                    printWindow = $okPrint; retry = $attempt; crop = ($CropRect -join ',')
                    note = $Note
                }
                return $outPath
            } finally { $bmp.Dispose() }
        } catch {
            $lastError = $_.Exception.Message
            Add-CaLog -Kind 'capture-retry' -Data @{ attempt = $attempt; error = $lastError; target = $outPath }
            Start-Sleep -Milliseconds 400
        }
    }
    throw "截图失败（$Retries 次重试）: $outPath / $lastError"
}

function Save-CaCrop {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceFile,
        [Parameter(Mandatory)][int[]]$CropRect,
        [Parameter(Mandatory)][string]$FileName,
        [string]$PhaseName
    )
    if ($PhaseName -and $PhaseName.Trim().Length -gt 0) { $Phase = $PhaseName }
    Initialize-UiNative
    $src = if ([System.IO.Path]::IsPathRooted($SourceFile)) {
        [System.IO.Path]::GetFullPath($SourceFile)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $SourceFile))
    }
    if (-not (Test-Path -LiteralPath $src)) { throw "找不到源截图: $src" }
    $img = [System.Drawing.Image]::FromFile($src)
    try {
        $cx = [Math]::Max(0, [Math]::Min($CropRect[0], $img.Width - 1))
        $cy = [Math]::Max(0, [Math]::Min($CropRect[1], $img.Height - 1))
        $cw = [Math]::Max(1, [Math]::Min($CropRect[2], $img.Width - $cx))
        $ch = [Math]::Max(1, [Math]::Min($CropRect[3], $img.Height - $cy))
        $crop = New-Object System.Drawing.Bitmap $cw, $ch
        $g = [System.Drawing.Graphics]::FromImage($crop)
        try { $g.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, $cw, $ch), (New-Object System.Drawing.Rectangle $cx, $cy, $cw, $ch), [System.Drawing.GraphicsUnit]::Pixel) }
        finally { $g.Dispose() }
        $outName = $FileName
        if ($outName -notmatch '\.png$') { $outName = "$outName.png" }
        $outDir = Join-Path $script:UiEvidenceRoot $Phase
        if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
        $outPath = Join-Path $outDir $outName
        $crop.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $crop.Dispose()
        Add-CaLog -Kind 'crop' -Data @{ source = $src; file = $outPath; crop = ($CropRect -join ',') }
        return $outPath
    } finally { $img.Dispose() }
}

# ---------------------------------------------------------------------------
# CLI 分发
# ---------------------------------------------------------------------------
function Invoke-CaAction {
    switch ($Action) {
        'None' { return $null }
        'Start' {
            $w = Start-CaApp -Exe $script:UiExePath -TimeoutSec $TimeoutSec
            return ($w | ConvertTo-Json -Depth 4)
        }
        'Stop' {
            $stopped = Stop-CaApp
            Add-CaLog -Kind 'app-stop' -Data @{ stopped = $stopped }
            return "stopped=$stopped"
        }
        'Restart' {
            [void](Stop-CaApp)
            $w = Start-CaApp -Exe $script:UiExePath -TimeoutSec $TimeoutSec
            return ($w | ConvertTo-Json -Depth 4)
        }
        'Info' {
            $w = Get-CaWindow
            return ($w | ConvertTo-Json -Depth 4)
        }
        'Foreground' {
            $ok = Set-CaForeground
            return "foreground=$ok"
        }
        'Capture' {
            return (Capture-CaWindow -PhaseName $Phase -FileName $Name -CropRect $Crop -NoForeground:$NoForeground -Note $Note)
        }
        'Crop' {
            if (-not $SourcePath) { throw 'Crop 需要 -SourcePath' }
            if (-not $Crop -or $Crop.Count -ne 4) { throw 'Crop 需要 -Crop x,y,w,h' }
            return (Save-CaCrop -SourceFile $SourcePath -CropRect $Crop -FileName $Name -PhaseName $Phase)
        }
        'Click' {
            $r = Send-CaClick -WindowX $X -WindowY $Y -MouseButton $Button -Double:$DoubleClick
            if ($CaptureAfter) {
                if (-not $Name) { throw 'Click -CaptureAfter 需要 -Name' }
                [void](Capture-CaWindow -PhaseName $Phase -FileName $Name -CropRect $Crop -NoForeground -Note "after click ($X,$Y)")
            }
            return ($r | ConvertTo-Json -Depth 4)
        }
        'Wheel' {
            $r = Send-CaWheel -WindowX $X -WindowY $Y -Delta $WheelDelta
            return ($r | ConvertTo-Json -Depth 4)
        }
        'Keys' {
            if (-not $KeysText) { throw 'Keys 需要 -KeysText' }
            return (Send-CaKeys -Keys $KeysText)
        }
        'Type' {
            if (-not $TextValue) { throw 'Type 需要 -TextValue' }
            return (Send-CaText -Text $TextValue)
        }
        'Paste' {
            if (-not $TextValue) { throw 'Paste 需要 -TextValue' }
            return (Send-CaPaste -Text $TextValue)
        }
        'Elements' {
            $els = Get-CaUiElements -NamePattern $NamePattern -TypeFilter $ControlType -IncludeTextOnly:$IncludeText -TimeoutSec $TimeoutSec
            return ($els | ConvertTo-Json -Depth 4)
        }
        'ClickElement' {
            if (-not $NamePattern) { throw 'ClickElement 需要 -NamePattern' }
            $el = Invoke-CaClickElement -Pattern $NamePattern -TypeFilter $ControlType -TimeoutSec $TimeoutSec -CaptureAfter:$CaptureAfter -CaptureName $Name
            if ($CaptureAfter) { return "clicked element '$($el.Name)' at window ($($el.CenterWindowX),$($el.CenterWindowY))" }
            return ($el | ConvertTo-Json -Depth 4)
        }
        'WaitElement' {
            if (-not $NamePattern) { throw 'WaitElement 需要 -NamePattern' }
            $el = Wait-CaUiElement -Pattern $NamePattern -TypeFilter $ControlType -TimeoutSec $TimeoutSec
            if (-not $el) { return $null }
            return ($el | ConvertTo-Json -Depth 4)
        }
        'Sleep' {
            Start-Sleep -Milliseconds $DelayMs
            return "slept=$DelayMs"
        }
        'Resize' {
            if ($Width -le 0 -or $Height -le 0) { throw 'Resize 需要 -Width -Height（物理像素），可带 -X -Y' }
            $w = Set-CaWindowRect -Left $X -Top $Y -WidthPx $Width -HeightPx $Height -Foreground
            return ($w | ConvertTo-Json -Depth 4)
        }
        'Windows' {
            $wins = Get-CaTopLevelWindows
            return (($wins | ForEach-Object {
                        [pscustomobject]@{
                            Hwnd = ('0x{0:X}' -f $_.Hwnd); Title = $_.Title; ClassName = $_.ClassName
                            Left = $_.Left; Top = $_.Top; Width = $_.Width; Height = $_.Height
                            Visible = $_.Visible; Iconic = $_.Iconic; Zoomed = $_.Zoomed
                        }
                    }) | ConvertTo-Json -Depth 4)
        }
        'FindWindow' {
            if (-not $NamePattern) { throw 'FindWindow 需要 -NamePattern（窗口标题正则）' }
            $w = Find-CaWindowByTitle -TitlePattern $NamePattern
            if (-not $w) { return $null }
            return ([pscustomobject]@{
                    Hwnd = ('0x{0:X}' -f $w.Hwnd); Title = $w.Title; ClassName = $w.ClassName
                    Left = $w.Left; Top = $w.Top; Width = $w.Width; Height = $w.Height
                    Visible = $w.Visible; Zoomed = $w.Zoomed
                } | ConvertTo-Json -Depth 4)
        }
        'CaptureHwnd' {
            if ($Handle -le 0) { throw 'CaptureHwnd 需要 -Handle（十进制或 0x 十六进制）' }
            return (Capture-CaHandle -WindowHandle $Handle -FileName $Name -PhaseName $Phase -NoForeground:$NoForeground)
        }
        'ExternalWindow' {
            if (-not $NamePattern) { throw 'ExternalWindow 需要 -NamePattern（窗口标题正则）' }
            $w = Get-CaExternalWindow -TitlePattern $NamePattern -ClassName $ControlType -TimeoutSec $TimeoutSec
            if (-not $w) { return $null }
            return ($w | ConvertTo-Json -Depth 4)
        }
        'KeysTo' {
            if ($Handle -le 0) { throw 'KeysTo 需要 -Handle' }
            if (-not $KeysText) { throw 'KeysTo 需要 -KeysText' }
            return (Send-CaKeysTo -WindowHandle $Handle -Keys $KeysText)
        }
        'PasteTo' {
            if ($Handle -le 0) { throw 'PasteTo 需要 -Handle' }
            if (-not $TextValue) { throw 'PasteTo 需要 -TextValue' }
            return (Send-CaPasteTo -WindowHandle $Handle -Text $TextValue)
        }
        'ClickScreen' {
            return (Send-CaClickScreen -ScreenX $X -ScreenY $Y -MouseButton $Button -Double:$DoubleClick -ActivateHandle $Handle | ConvertTo-Json -Depth 4)
        }
        'WaitGone' {
            if ($Handle -le 0) { throw 'WaitGone 需要 -Handle' }
            $gone = Wait-CaWindowGone -WindowHandle $Handle -TimeoutSec $TimeoutSec
            return "gone=$gone"
        }
        'Tabs' {
            $tabs = @(Get-CaTabItems) | ForEach-Object {
                [pscustomobject]@{
                    Index = $_.Index; Name = $_.Name; WindowX = $_.WindowX; WindowY = $_.WindowY
                    Width = $_.Width; Height = $_.Height; IsOffscreen = $_.IsOffscreen
                }
            }
            return ($tabs | ConvertTo-Json -Depth 4)
        }
        'SelectTab' {
            if ($Index -lt 0) { throw 'SelectTab 需要 -Index' }
            return (Select-CaTab -Index $Index -PostDelayMs $DelayMs | ConvertTo-Json -Depth 4)
        }
        default { throw "未知 Action: $Action" }
    }
}

# dot-source 时不执行任何动作（-Action 默认 None）
$script:UiCliResult = Invoke-CaAction
if ($Action -ne 'None') {
    if ($null -ne $script:UiCliResult) { Write-Output $script:UiCliResult }
}

