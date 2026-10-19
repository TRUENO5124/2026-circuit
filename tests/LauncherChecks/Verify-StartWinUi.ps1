[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$launcherPath = Join-Path $repositoryRoot 'start-winui.bat'
$launcher = Get-Content -LiteralPath $launcherPath -Raw
$helperPath = Join-Path $repositoryRoot 'scripts\Start-CircuitAssistant.ps1'

if ($launcher -notmatch 'Start-CircuitAssistant\.ps1') {
    throw 'start-winui.bat must retain the helper for first build and explicit rebuild.'
}

if ($launcher -notmatch 'start "" /D' -or $launcher -notmatch '"--build" goto build') {
    throw 'start-winui.bat must launch the existing executable directly and offer --build.'
}

if ($launcher -notmatch 'if errorlevel 1') {
    throw 'start-winui.bat must surface build helper failures.'
}

$defaultInvocationResult = & $helperPath -CheckOnly
if ($defaultInvocationResult -notmatch '^(READY|BUILD):') {
    throw "The launcher helper cannot resolve the repository from its own script path: $defaultInvocationResult"
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("CircuitAssistant-launcher-test-" + [Guid]::NewGuid().ToString('N'))
$temporaryApp = Join-Path $temporaryRoot 'apps\desktop-winui'
$temporaryOutput = Join-Path $temporaryApp 'bin\Debug\net9.0-windows10.0.26100.0\win-x64'
$temporaryProject = Join-Path $temporaryApp 'CircuitAssistant.csproj'
$temporarySource = Join-Path $temporaryApp 'MainWindow.xaml'
$temporaryExe = Join-Path $temporaryOutput 'CircuitAssistant.exe'
$temporaryStamp = Join-Path $temporaryOutput '.last-successful-build'

try {
    New-Item -ItemType Directory -Force -Path $temporaryOutput | Out-Null
    [System.IO.File]::WriteAllText($temporaryProject, '<Project />')
    [System.IO.File]::WriteAllText($temporarySource, '<Window />')
    [System.IO.File]::WriteAllBytes($temporaryExe, [byte[]](0))
    [System.IO.File]::WriteAllText($temporaryStamp, 'test')

    $oldTime = [DateTime]::UtcNow.AddMinutes(-2)
    $stampTime = [DateTime]::UtcNow.AddMinutes(-1)
    (Get-Item -LiteralPath $temporaryProject).LastWriteTimeUtc = $oldTime
    (Get-Item -LiteralPath $temporarySource).LastWriteTimeUtc = $oldTime
    (Get-Item -LiteralPath $temporaryExe).LastWriteTimeUtc = $oldTime
    (Get-Item -LiteralPath $temporaryStamp).LastWriteTimeUtc = $stampTime

    $freshResult = & $helperPath -ProjectRoot $temporaryRoot -CheckOnly
    if ($freshResult -notmatch '^READY:') {
        throw "Fresh launcher fixture was not accepted: $freshResult"
    }

    (Get-Item -LiteralPath $temporarySource).LastWriteTimeUtc = [DateTime]::UtcNow
    $staleResult = & $helperPath -ProjectRoot $temporaryRoot -CheckOnly
    if ($staleResult -notmatch '^READY:') {
        throw "Newer source must not block normal startup: $staleResult"
    }

    Remove-Item -LiteralPath $temporaryStamp -Force
    $unstampedResult = & $helperPath -ProjectRoot $temporaryRoot -CheckOnly
    if ($unstampedResult -notmatch '^READY:') {
        throw "A build stamp must not be required to launch an existing executable: $unstampedResult"
    }

    # The deliberately invalid project would fail if normal startup ran dotnet.
    & $helperPath -ProjectRoot $temporaryRoot -SkipLaunch
    if ($LASTEXITCODE -ne 0) { throw 'Normal startup attempted to build the invalid fixture project.' }

    $forcedResult = & $helperPath -ProjectRoot $temporaryRoot -ForceBuild -CheckOnly
    if ($forcedResult -notmatch '^BUILD: requested by caller') {
        throw "Explicit rebuild was not accepted: $forcedResult"
    }

    Remove-Item -LiteralPath $temporaryExe -Force
    $missingResult = & $helperPath -ProjectRoot $temporaryRoot -CheckOnly
    if ($missingResult -notmatch '^BUILD: executable is missing') {
        throw "Missing executable was not detected: $missingResult"
    }
}
finally {
    $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
    $resolvedTemporaryBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedTemporaryRoot.StartsWith($resolvedTemporaryBase, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTemporaryRoot)) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}

Write-Host 'Launcher checks passed: existing EXE opens without scanning or building; missing EXE or explicit rebuild requests a build.'
