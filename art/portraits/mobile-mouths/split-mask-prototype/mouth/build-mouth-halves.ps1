$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$base = Split-Path -Parent $root
$project = 'C:\Users\Daniel\Desktop\Code_Experiments\DFU\LivingCities'
$originalPath = Join-Path $base 'original.png'
$atlasPath = Join-Path $project 'art\portraits\mobile-mouths\tfac-077-full-frame-v5.png'
$manifestPath = Join-Path $project 'art\portraits\mobile-mouths\full-frame-v5-manifest.json'
$original = [System.Drawing.Bitmap]::FromFile($originalPath)
$atlas = [System.Drawing.Bitmap]::FromFile($atlasPath)
try {
    if ($original.Width -ne 64 -or $original.Height -ne 64) { throw 'Original must be 64x64.' }
    $cell = 418
    if ($atlas.Width -ne 1254 -or $atlas.Height -ne 1254) { throw 'V5 atlas dimensions must be 1254x1254 (3x3 cells at 418px).' }
    $mask = [System.Drawing.Bitmap]::new(64,29,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y=0; $y -lt 29; $y++) {
        for ($x=0; $x -lt 64; $x++) {
            $inside = ($x -ge 22 -and $x -le 41 -and $y -ge 4 -and $y -le 16)
            if ($inside) { $mask.SetPixel($x,$y,[System.Drawing.Color]::White) }
            else { $mask.SetPixel($x,$y,[System.Drawing.Color]::Black) }
        }
    }
    $maskPath = Join-Path $root 'mouth-mask.png'
    $mask.Save($maskPath,[System.Drawing.Imaging.ImageFormat]::Png)
    $mask.Dispose()

    $order = @('A','B','C','D','E','F','G','H','X')
    $changedCounts = [ordered]@{}
    $outsideCounts = [ordered]@{}
    $sampledCellHashes = [ordered]@{}
    foreach ($i in 0..8) {
        $label = $order[$i]
        $row = [int][Math]::Floor($i / 3)
        $col = $i % 3
        $sample = [System.Drawing.Bitmap]::new(64,64,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $sampleBytes = New-Object System.Collections.Generic.List[byte]
        for ($y=0; $y -lt 64; $y++) {
            for ($x=0; $x -lt 64; $x++) {
                # Sample each complete source cell at the center of each native pixel footprint.
                $sx = $col*$cell + [int][Math]::Floor(($x + 0.5) * $cell / 64.0)
                $sy = $row*$cell + [int][Math]::Floor(($y + 0.5) * $cell / 64.0)
                $c = $atlas.GetPixel($sx,$sy)
                $sample.SetPixel($x,$y,$c)
                $sampleBytes.Add($c.R); $sampleBytes.Add($c.G); $sampleBytes.Add($c.B); $sampleBytes.Add($c.A)
            }
        }
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { $sampledCellHashes[$label] = ([Convert]::ToHexString($sha.ComputeHash($sampleBytes.ToArray()))) }
        finally { $sha.Dispose() }
        $dest = [System.Drawing.Bitmap]::new(64,29,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $changed = 0
        $outsideChanged = 0
        for ($y=0; $y -lt 29; $y++) {
            for ($x=0; $x -lt 64; $x++) {
                $originalColor = $original.GetPixel($x,$y+35)
                $destColor = $originalColor
                if ($label -ne 'A' -and $label -ne 'X' -and $x -ge 22 -and $x -le 41 -and $y -ge 4 -and $y -le 16) {
                    $destColor = $sample.GetPixel($x,$y+35)
                }
                $dest.SetPixel($x,$y,$destColor)
                if ($destColor.ToArgb() -ne $originalColor.ToArgb()) {
                    if ($x -ge 22 -and $x -le 41 -and $y -ge 4 -and $y -le 16) { $changed++ } else { $outsideChanged++ }
                }
            }
        }
        $path = Join-Path $root ($label + '.png')
        $dest.Save($path,[System.Drawing.Imaging.ImageFormat]::Png)
        $changedCounts[$label] = $changed
        $outsideCounts[$label] = $outsideChanged
        $dest.Dispose(); $sample.Dispose()
    }
    if (($outsideCounts.Values | Measure-Object -Sum).Sum -ne 0) { throw "Outside-mask pixel changes detected: $outsideCounts" }
    $originalHash = (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash
    $atlasHash = (Get-FileHash -LiteralPath $atlasPath -Algorithm SHA256).Hash
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $expected = ($manifest | Where-Object {$_.record -eq '077'}).sha256
    if ($atlasHash -ne $expected) { throw "Atlas SHA256 differs from approved manifest: $atlasHash" }
    $outputHashes = [ordered]@{}
    foreach ($label in $order) {
        $outputHashes[$label] = (Get-FileHash -LiteralPath (Join-Path $root ($label + '.png')) -Algorithm SHA256).Hash
    }
    $provenance = [ordered]@{
        record = '077'
        sourceAtlas = [ordered]@{ path = 'art/portraits/mobile-mouths/tfac-077-full-frame-v5.png'; sha256 = $atlasHash; dimensions = @(1254,1254); grid = @(3,3); cellSize = @(418,418); expectedManifestSha256 = $expected; }
        immutableOriginal = [ordered]@{ path = 'art/portraits/mobile-mouths/split-mask-prototype/original.png'; sha256 = $originalHash; dimensions = @(64,64); }
        method = 'For each complete 418x418 source cell, sample a complete 64x64 frame using the center of each native pixel footprint (nearest-neighbor); composite only the declared mask from the sampled complete frame onto the original lower half. No independent mouth resize.'
        cellOrder = @('A','B','C','D','E','F','G','H','X')
        sampledCompleteCellSha256 = $sampledCellHashes
        outputs = $outputHashes
        outputDimensions = @(64,29)
        lowerHalfSourceRows = @(35,63)
        mask = [ordered]@{ file='mouth-mask.png'; outputSize=@(64,29); fullFrameInclusiveBounds=[ordered]@{x=@(22,41);y=@(39,51)}; lowerHalfRelativeInclusiveBounds=[ordered]@{x=@(22,41);y=@(4,16)}; }
        exactOriginalFrames = @('A','X')
        changedPixelsInsideMask = $changedCounts
        changedPixelsOutsideMask = $outsideCounts
        strictOutsideMaskCheck = 'PASS: every output pixel outside x=22..41,y=39..51 (full-frame coordinates) equals the immutable original.'
        review = 'pending'
    }
    $provenance | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'provenance.json') -Encoding utf8
    [pscustomobject]@{ originalSha256=$originalHash; atlasSha256=$atlasHash; expectedSha256=$expected; changedInsideMask=$changedCounts; changedOutsideMask=$outsideCounts; outputHashes=$outputHashes } | ConvertTo-Json -Depth 8
}
finally { $original.Dispose(); $atlas.Dispose() }


