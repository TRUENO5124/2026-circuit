[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$assetDirectory = Join-Path $ProjectRoot 'apps\desktop-winui\Assets'
$svgPath = Join-Path $assetDirectory 'AppIcon.svg'
$renderPath = Join-Path $assetDirectory '.AppIcon.render.png'

if (-not (Test-Path -LiteralPath $svgPath)) {
    throw "Vector icon source not found: $svgPath"
}

$chromeCandidates = @(
    'C:\Program Files\Google\Chrome\Application\chrome.exe',
    'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Microsoft\Edge\Application\msedge.exe'
)
$browserPath = $chromeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $browserPath) {
    throw 'Chrome or Microsoft Edge is required to render AppIcon.svg.'
}

$svgUri = ([System.Uri]$svgPath).AbsoluteUri
& $browserPath `
    '--headless=new' `
    '--disable-gpu' `
    '--hide-scrollbars' `
    '--default-background-color=00000000' `
    '--force-device-scale-factor=2' `
    '--window-size=512,512' `
    "--screenshot=$renderPath" `
    $svgUri | Out-Null

if (-not (Test-Path -LiteralPath $renderPath)) {
    throw 'The browser did not produce the intermediate icon render.'
}

Add-Type -AssemblyName System.Drawing.Common

function New-ResizedPngBytes {
    param(
        [Parameter(Mandatory)] [System.Drawing.Image]$Source,
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] [int]$Height
    )

    $bitmap = [System.Drawing.Bitmap]::new(
        $Width,
        $Height,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    )
    try {
        $bitmap.SetResolution(96, 96)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.DrawImage($Source, 0, 0, $Width, $Height)
        }
        finally {
            $graphics.Dispose()
        }

        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $bitmap.Dispose()
    }
}

function Save-Bytes {
    param(
        [Parameter(Mandatory)] [byte[]]$Bytes,
        [Parameter(Mandatory)] [string]$Path
    )

    [System.IO.File]::WriteAllBytes($Path, $Bytes)
}

$master = [System.Drawing.Image]::FromFile($renderPath)
try {
    $pngTargets = @(
        @{ Name = 'LockScreenLogo.scale-200.png'; Width = 48; Height = 48 },
        @{ Name = 'Square150x150Logo.scale-200.png'; Width = 300; Height = 300 },
        @{ Name = 'Square44x44Logo.scale-200.png'; Width = 88; Height = 88 },
        @{ Name = 'Square44x44Logo.targetsize-24_altform-unplated.png'; Width = 24; Height = 24 },
        @{ Name = 'Square44x44Logo.targetsize-48_altform-lightunplated.png'; Width = 48; Height = 48 },
        @{ Name = 'StoreLogo.png'; Width = 50; Height = 50 }
    )

    foreach ($target in $pngTargets) {
        $bytes = New-ResizedPngBytes -Source $master -Width $target.Width -Height $target.Height
        Save-Bytes -Bytes $bytes -Path (Join-Path $assetDirectory $target.Name)
    }

    $icoSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $icoImages = @(
        foreach ($size in $icoSizes) {
            [pscustomobject]@{
                Size = $size
                Bytes = New-ResizedPngBytes -Source $master -Width $size -Height $size
            }
        }
    )

    $iconPath = Join-Path $assetDirectory 'AppIcon.ico'
    $iconStream = [System.IO.File]::Create($iconPath)
    $writer = [System.IO.BinaryWriter]::new($iconStream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$icoImages.Count)

        $offset = 6 + (16 * $icoImages.Count)
        foreach ($image in $icoImages) {
            $dimensionByte = if ($image.Size -ge 256) { [byte]0 } else { [byte]$image.Size }
            $writer.Write($dimensionByte)
            $writer.Write($dimensionByte)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$image.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $image.Bytes.Length
        }

        foreach ($image in $icoImages) {
            # PowerShell stores function output in an object array; select the
            # BinaryWriter byte[] overload explicitly so the whole PNG payload
            # is written instead of a single converted byte.
            $writer.Write([byte[]]$image.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $iconStream.Dispose()
    }
}
finally {
    $master.Dispose()
    Remove-Item -LiteralPath $renderPath -Force
}

Write-Host "Generated AppIcon.ico and Windows icon PNG assets from $svgPath"
