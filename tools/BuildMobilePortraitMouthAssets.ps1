param(
    [Parameter(Mandatory=$true)][int]$Record,
    [Parameter(Mandatory=$true)][string]$GeneratedMouthSheet,
    [ValidateSet(2,3)][int]$Version=3
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$name = '{0:D3}' -f $Record
$folder = Join-Path $root 'art/portraits/mobile-mouths'
$sourcePath = Join-Path $root "work/breeze-tts-20261021/gwen-portrait-candidates/tfac00i0-rci-$name.png"
$source = [System.Drawing.Bitmap]::FromFile($sourcePath)
$generated = [System.Drawing.Bitmap]::FromFile((Resolve-Path -LiteralPath $GeneratedMouthSheet))
$output = New-Object System.Drawing.Bitmap(192,192)
$order = 'ABCDEFGHX'
$changed = @()
$writeRect = if ($Version -eq 3) { @(23,39,18,11) } else { @(22,39,20,14) }
try {
    if ($source.Width -ne 64 -or $source.Height -ne 64) { throw 'Expected original 64x64 portrait.' }
    # The model sees only the native rectangle (18,35,28,20), enlarged 16x.
    # Use an even smaller write rectangle to preserve the native nose and chin.
    for ($tile=0; $tile -lt 9; $tile++) {
        $col = $tile % 3; $row = [int][Math]::Floor($tile / 3)
        $count = 0
        for ($y=0; $y -lt 64; $y++) {
            for ($x=0; $x -lt 64; $x++) {
                $original = $source.GetPixel($x,$y)
                $pixel = $original
                $inside = $x -ge $writeRect[0] -and $x -lt ($writeRect[0]+$writeRect[2]) -and $y -ge $writeRect[1] -and $y -lt ($writeRect[1]+$writeRect[3])
                $closedOriginal = $tile -eq 8 -or ($Version -eq 3 -and $tile -eq 0)
                if (-not $closedOriginal -and $inside) {
                    # The generated lip center drifted down about two native pixels.
                    # Re-anchor it at the original lips, shrink width 20% and height 30%.
                    $sampleX = $x+0.5; $sampleY = $y+0.5
                    if ($Version -eq 3) {
                        $sampleX = 32.0 + ($sampleX-32.0)/0.8
                        $sampleY = 45.0 + ($sampleY-43.5)/0.7
                    }
                    $sx = [int][Math]::Floor(($col + ($sampleX-18)/28.0) * $generated.Width/3.0)
                    $sy = [int][Math]::Floor(($row + ($sampleY-35)/20.0) * $generated.Height/3.0)
                    $pixel = $generated.GetPixel($sx,$sy)
                }
                $output.SetPixel(($col*64+$x),($row*64+$y),$pixel)
                if ($pixel.ToArgb() -ne $original.ToArgb()) {
                    if (-not $inside -or $closedOriginal) { throw 'Pixel escaped the mouth rectangle or changed a closed original.' }
                    $count++
                }
            }
        }
        $changed += @{ shape = [string]$order[$tile]; changedPixels = $count }
    }
    $outPath = Join-Path $folder "tfac-$name-visemes-v$Version.png"
    $output.Save($outPath,[System.Drawing.Imaging.ImageFormat]::Png)
    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $folder "tfac-$name-original.png")
    @{
        schemaVersion=1; bank='TFAC00I0.RCI'; record=$Record; nativeSize=@(64,64)
        atlasSize=@(192,192); tileOrder='A B C D E F G H X'; generatedCrop=@(18,35,28,20)
        writeRectangle=$writeRect; outsideChangedPixels=0; neutralXOriginal=$true
        version=$Version; mouthScale= $(if ($Version -eq 3) { @(0.8,0.7) } else { @(1.0,1.0) })
        originalSha256=(Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
        generatedSha256=(Get-FileHash -LiteralPath $GeneratedMouthSheet -Algorithm SHA256).Hash.ToLowerInvariant()
        atlasSha256=(Get-FileHash -LiteralPath $outPath -Algorithm SHA256).Hash.ToLowerInvariant()
        changedPixels=$changed; generator='built-in image_gen'; status='visual-review-pending; not installed'
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $folder "tfac-$name-manifest-v$Version.json") -Encoding UTF8
    Write-Output "MOUTH_ASSET_PASS: face=$Record; outsideChangedPixels=0; neutralXOriginal=true; atlas=$outPath"
} finally { $source.Dispose(); $generated.Dispose(); $output.Dispose() }
