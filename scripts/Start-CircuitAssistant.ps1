[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [switch]$CheckOnly,
    [switch]$SkipLaunch,
    [switch]$ForceBuild
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}

function Get-BuildState {
    param(
        [Parameter(Mandatory)] [string]$ExecutablePath,
        [switch]$Rebuild
    )

    if (-not (Test-Path -LiteralPath $ExecutablePath)) {
        return [pscustomobject]@{ NeedsBuild = $true; Reason = 'executable is missing' }
    }

    if ($Rebuild) {
        return [pscustomobject]@{ NeedsBuild = $true; Reason = 'requested by caller' }
    }

    return [pscustomobject]@{ NeedsBuild = $false; Reason = 'existing executable is available (source freshness is not checked)' }
}

try {
    $projectRootPath = [System.IO.Path]::GetFullPath($ProjectRoot)
    $applicationDirectory = Join-Path $projectRootPath 'apps\desktop-winui'
    $projectPath = Join-Path $applicationDirectory 'CircuitAssistant.csproj'
    $outputDirectory = Join-Path $applicationDirectory 'bin\Debug\net9.0-windows10.0.26100.0\win-x64'
    $executablePath = Join-Path $outputDirectory 'CircuitAssistant.exe'
    $buildStampPath = Join-Path $outputDirectory '.last-successful-build'

    $state = Get-BuildState `
        -ExecutablePath $executablePath `
        -Rebuild:$ForceBuild

    if ($CheckOnly) {
        if ($state.NeedsBuild) {
            Write-Output "BUILD: $($state.Reason)"
        }
        else {
            Write-Output "READY: $($state.Reason)"
        }
        exit 0
    }

    if ($state.NeedsBuild) {
        if (-not (Test-Path -LiteralPath $projectPath)) {
            throw "CircuitAssistant project was not found: $projectPath"
        }
        Write-Host "Updating CircuitAssistant ($($state.Reason))..."
        & dotnet build $projectPath --nologo
        if ($LASTEXITCODE -ne 0) {
            throw 'Build failed. Save and close any running CircuitAssistant window, then try again.'
        }

        if (-not (Test-Path -LiteralPath $executablePath)) {
            throw "Build completed, but the executable was not found: $executablePath"
        }

        $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
        [System.IO.File]::WriteAllText(
            $buildStampPath,
            [DateTime]::UtcNow.ToString('O'),
            $utf8NoBom
        )
    }
    else {
        Write-Host 'Opening existing CircuitAssistant build (use -ForceBuild to update)...'
    }

    if (-not $SkipLaunch) {
        Start-Process -FilePath $executablePath -WorkingDirectory $outputDirectory
    }
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
