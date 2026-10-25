param(
    [Parameter(Mandatory = $true)][string]$PortraitRoot,
    [Parameter(Mandatory = $true)][string]$OriginalPath,
    [Parameter(Mandatory = $true)][ValidateRange(0, 1000000)][int]$PortraitRecord,
    [Parameter()][string]$Bank = 'TFAC00I0.RCI',
    [Parameter(Mandatory = $true)][string]$LayoutPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-Rect([object]$Rect, [string]$Name, [int]$MaxY, [bool]$LowerHalf) {
    foreach ($field in @('x', 'y', 'width', 'height')) {
        if ($null -eq $Rect.$field -or
            ($Rect.$field -isnot [int] -and $Rect.$field -isnot [long])) {
            throw "$Name.$field must be an integer"
        }
    }
    if ($Rect.x -lt 0 -or $Rect.y -lt 0 -or $Rect.width -le 0 -or $Rect.height -le 0 -or
        ($Rect.x + $Rect.width) -gt 64 -or ($Rect.y + $Rect.height) -gt $MaxY) {
        throw "$Name rectangle is outside its permitted canvas bounds"
    }
    if ($LowerHalf -and $Rect.y -lt 35) {
        throw "$Name must stay in the lower half (y >= 35)"
    }
    if (-not $LowerHalf -and ($Rect.y + $Rect.height) -gt 35) {
        throw "$Name must stay in the upper half (y + height <= 35)"
    }
}

$rootFull = [System.IO.Path]::GetFullPath($PortraitRoot)
$sourceFull = (Resolve-Path -LiteralPath $OriginalPath).Path
$layoutFull = (Resolve-Path -LiteralPath $LayoutPath).Path
if (Test-Path -LiteralPath $rootFull) {
    throw "PortraitRoot already exists; refusing to overwrite: $rootFull"
}
if (-not [System.IO.File]::Exists($sourceFull)) { throw "OriginalPath is not a file: $sourceFull" }
if ([System.IO.Path]::GetExtension($sourceFull).ToLowerInvariant() -ne '.png') {
    throw 'OriginalPath must name a PNG file'
}
if (-not [System.IO.File]::Exists($layoutFull)) { throw "LayoutPath is not a file: $layoutFull" }

$layout = Get-Content -LiteralPath $layoutFull -Raw | ConvertFrom-Json
if ($layout.width -ne 64 -or $layout.height -ne 64 -or $layout.splitY -ne 35) {
    throw 'Layout must specify width=64, height=64, splitY=35'
}
if ($null -ne $layout.portraitRecord -and $layout.portraitRecord -ne $PortraitRecord) {
    throw 'Layout portraitRecord does not match -PortraitRecord'
}
if (-not ($layout.eyeRegions -is [array]) -or $layout.eyeRegions.Count -ne 2) {
    throw 'Layout must provide exactly two eyeRegions'
}
for ($i = 0; $i -lt 2; $i++) {
    Assert-Rect $layout.eyeRegions[$i] "eyeRegions[$i]" 35 $false
}
Assert-Rect $layout.mouthRegion 'mouthRegion' 64 $true

$sourceHash = Get-Sha256 $sourceFull
$layoutHash = Get-Sha256 $layoutFull
$sourceBitmap = [System.Drawing.Bitmap]::FromFile($sourceFull)
try {
    if ($sourceBitmap.RawFormat.Guid -ne [System.Drawing.Imaging.ImageFormat]::Png.Guid) {
        throw 'Original file content is not encoded as PNG'
    }
    if ($sourceBitmap.Width -ne 64 -or $sourceBitmap.Height -ne 64) {
        throw "Original PNG must be 64x64; got $($sourceBitmap.Width)x$($sourceBitmap.Height)"
    }
}
finally {
    $sourceBitmap.Dispose()
}

[void][System.IO.Directory]::CreateDirectory($rootFull)
try {
    $originalCopy = Join-Path $rootFull 'original.png'
    Copy-Item -LiteralPath $sourceFull -Destination $originalCopy
    Copy-Item -LiteralPath $layoutFull -Destination (Join-Path $rootFull 'layout.json')
    if ((Get-Sha256 $originalCopy) -ne $sourceHash) {
        throw 'Copied original hash differs from source hash'
    }

    $bitmap = [System.Drawing.Bitmap]::FromFile($originalCopy)
    try {
        $upper = $bitmap.Clone([System.Drawing.Rectangle]::new(0, 0, 64, 35), $bitmap.PixelFormat)
        $lower = $bitmap.Clone([System.Drawing.Rectangle]::new(0, 35, 64, 29), $bitmap.PixelFormat)
        try {
            $upper.Save((Join-Path $rootFull 'source-upper.png'), [System.Drawing.Imaging.ImageFormat]::Png)
            $lower.Save((Join-Path $rootFull 'source-lower.png'), [System.Drawing.Imaging.ImageFormat]::Png)

            $scale = 8
            $eyeGrid = [System.Drawing.Bitmap]::new(64 * $scale * 2, 35 * $scale * 2,
                [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $mouthGrid = [System.Drawing.Bitmap]::new(64 * $scale * 3, 29 * $scale * 3,
                [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $eyeGraphics = [System.Drawing.Graphics]::FromImage($eyeGrid)
                $mouthGraphics = [System.Drawing.Graphics]::FromImage($mouthGrid)
                try {
                    foreach ($graphics in @($eyeGraphics, $mouthGraphics)) {
                        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
                        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
                        $graphics.Clear([System.Drawing.Color]::Transparent)
                    }
                    for ($cell = 0; $cell -lt 4; $cell++) {
                        $x = ($cell % 2) * (64 * $scale)
                        $y = [math]::Floor($cell / 2) * (35 * $scale)
                        $eyeGraphics.DrawImage($upper, $x, $y, 64 * $scale, 35 * $scale)
                    }
                    for ($cell = 0; $cell -lt 9; $cell++) {
                        $x = ($cell % 3) * (64 * $scale)
                        $y = [math]::Floor($cell / 3) * (29 * $scale)
                        $mouthGraphics.DrawImage($lower, $x, $y, 64 * $scale, 29 * $scale)
                    }
                    $eyeGrid.Save((Join-Path $rootFull 'eyes-reference-grid.png'), [System.Drawing.Imaging.ImageFormat]::Png)
                    $mouthGrid.Save((Join-Path $rootFull 'mouth-reference-grid.png'), [System.Drawing.Imaging.ImageFormat]::Png)
                }
                finally {
                    $eyeGraphics.Dispose()
                    $mouthGraphics.Dispose()
                }
            }
            finally {
                $eyeGrid.Dispose()
                $mouthGrid.Dispose()
            }
        }
        finally {
            $upper.Dispose()
            $lower.Dispose()
        }
    }
    finally {
        $bitmap.Dispose()
    }

    if ((Get-Sha256 $sourceFull) -ne $sourceHash) {
        throw 'Original source changed during workspace preparation'
    }
    $files = @{}
    foreach ($name in @('original.png', 'source-upper.png', 'source-lower.png',
                        'eyes-reference-grid.png', 'mouth-reference-grid.png', 'layout.json')) {
        $files[$name] = Get-Sha256 (Join-Path $rootFull $name)
    }
    $provenance = [ordered]@{
        schemaVersion = 1
        createdUtc = [DateTime]::UtcNow.ToString('o')
        preparationTool = 'tools/PreparePortraitSplitWorkspace.ps1'
        portraitRecord = $PortraitRecord
        bank = $Bank
        sourcePath = $sourceFull
        sourceSha256 = $sourceHash
        sourceWidth = 64
        sourceHeight = 64
        splitY = 35
        layoutPath = $layoutFull
        layoutSha256 = $layoutHash
        workspaceFiles = $files
        generatedExpressions = $false
        note = 'Reference-only workspace. No animated mouth or eye frames are declared; original neutral A/X may be used later only by an explicit builder.'
    }
    $provenance | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $rootFull 'setup-provenance.json') -Encoding utf8
    Write-Output "PORTRAIT_SPLIT_WORKSPACE_READY record=$PortraitRecord root=$rootFull sourceSha256=$sourceHash"
}
catch {
    throw "Workspace creation failed; inspect the partial new directory before removing it: $rootFull. $($_.Exception.Message)"
}
