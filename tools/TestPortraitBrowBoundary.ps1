[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
$builderPath = Join-Path $PSScriptRoot 'BuildSplitPortraitPrototype.ps1'
$proofRoot = Join-Path $repoRoot 'work/portrait-brow-boundary'
$runRoot = Join-Path $proofRoot ('fixture-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -is [array] -or $Actual -is [array]) {
        if (($Expected -join ',') -ne ($Actual -join ',')) { throw "$Message expected=[$($Expected -join ',')] actual=[$($Actual -join ',')]" }
    } elseif ($Expected -ne $Actual) { throw "$Message expected=$Expected actual=$Actual" }
}

function New-Fixture([string]$Name, $BrowLockEndY, [bool]$IncludeBound, [bool]$IncludeBoundaryRepair=$false) {
    $root = Join-Path $runRoot $Name
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'eyes/guarded') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'eyes/neutral') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'mouth') -Force | Out-Null
    $eyeRegions = if ($IncludeBoundaryRepair) {
        @(
            [ordered]@{x=18;y=15;width=15;height=14},
            [ordered]@{x=38;y=15;width=13;height=14}
        )
    } else {
        @(
            [ordered]@{x=14;y=12;width=17;height=18},
            [ordered]@{x=33;y=12;width=18;height=18}
        )
    }
    $layout = [ordered]@{
        portraitRecord = 999
        bank = 'SYNTHETIC.RCI'
        width = 64
        height = 64
        splitY = 35
        eyeRegions = $eyeRegions
        mouthRegion = [ordered]@{x=22;y=38;width=20;height=13}
        eyeFeatureLocks = @()
        pixelRepairs = @()
        useOriginalFeaturePalette = $false
    }
    if ($IncludeBound) { $layout.browLockEndY = $BrowLockEndY }
    $layout | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'layout.json') -Encoding UTF8

    $original = [Drawing.Bitmap]::new(64,64)
    $upper = [Drawing.Color]::FromArgb(255,20,20,20)
    for ($y=0; $y -lt 64; $y++) { for ($x=0; $x -lt 64; $x++) { $original.SetPixel($x,$y,$upper) } }
    $original.Save((Join-Path $root 'original.png'),[Drawing.Imaging.ImageFormat]::Png)
    $original.Dispose()

    $raw = [Drawing.Bitmap]::new(128,70)
    $phaseColors = @(
        [Drawing.Color]::FromArgb(255,220,10,10),
        [Drawing.Color]::FromArgb(255,10,210,10),
        [Drawing.Color]::FromArgb(255,10,10,220),
        [Drawing.Color]::FromArgb(255,220,10,10)
    )
    for ($cell=0; $cell -lt 4; $cell++) {
        $color = $phaseColors[$cell]
        $cellX = ($cell % 2) * 64
        $cellY = [Math]::Floor($cell / 2) * 35
        foreach ($region in $layout.eyeRegions) {
            for ($y=$region.y; $y -lt ($region.y+$region.height); $y++) {
                for ($x=$region.x; $x -lt ($region.x+$region.width); $x++) {
                    $raw.SetPixel($cellX+$x,$cellY+$y,$color)
                }
            }
        }
    }
    $raw.Save((Join-Path $root 'eyes/guarded/raw.png'),[Drawing.Imaging.ImageFormat]::Png)
    $raw.Dispose()

    if ($IncludeBoundaryRepair) {
        $guardedRawPath = Join-Path $root 'eyes/guarded/raw.png'
        $updatedRawPath = Join-Path $root 'eyes/guarded/raw-boundary-test.png'
        $guardedRaw = [Drawing.Bitmap]::FromFile($guardedRawPath)
        try {
            $guardedRaw.SetPixel(37,20,[Drawing.Color]::FromArgb(255,100,80,60))
            $guardedRaw.SetPixel(38,20,[Drawing.Color]::FromArgb(255,120,100,80))
            $guardedRaw.Save($updatedRawPath,[Drawing.Imaging.ImageFormat]::Png)
        } finally { $guardedRaw.Dispose() }
        Move-Item -LiteralPath $updatedRawPath -Destination $guardedRawPath -Force
        $donorSha = (Get-FileHash -LiteralPath $guardedRawPath -Algorithm SHA256).Hash
        $layout.boundaryRepairs = @(
            [ordered]@{
                id='guarded-outer-brow-extension';emotion='guarded';phases=@('open','half','closed');x=37;y=20
                area='outside-eye-mask';expectedOriginalRgb='141414';expectedBeforeRgb='141414'
                donorSourceFile='eyes/guarded/raw.png';donorSourceSha256=$donorSha;donorPhase='open';donorX=37;donorY=20;expectedDonorRgb='64503C'
                operation='blend-original';weight=0.5;expectedOutputRgb='3C3228'
            },
            [ordered]@{
                id='guarded-mask-edge-alignment';emotion='guarded';phases=@('open','half','closed');x=38;y=20
                area='inside-eye-mask';expectedOriginalRgb='141414';expectedBeforeRgb='463C32'
                donorSourceFile='eyes/guarded/raw.png';donorSourceSha256=$donorSha;donorPhase='open';donorX=38;donorY=20;expectedDonorRgb='786450'
                operation='copy-donor';expectedOutputRgb='786450'
            }
        )
        $layout | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'layout.json') -Encoding UTF8
    }

    $neutralRaw = [Drawing.Bitmap]::new(128,70)
    for ($cell=0; $cell -lt 4; $cell++) {
        $color = $phaseColors[$cell]
        $cellX = ($cell % 2) * 64
        $cellY = [Math]::Floor($cell / 2) * 35
        foreach ($region in $layout.eyeRegions) {
            for ($y=$region.y; $y -lt ($region.y+$region.height); $y++) {
                for ($x=$region.x; $x -lt ($region.x+$region.width); $x++) {
                    $neutralRaw.SetPixel($cellX+$x,$cellY+$y,$color)
                }
            }
        }
    }
    $neutralRaw.Save((Join-Path $root 'eyes/neutral/raw.png'),[Drawing.Imaging.ImageFormat]::Png)
    $neutralRaw.Dispose()

    $mouth = [Drawing.Bitmap]::new(64,29)
    for ($y=0; $y -lt 29; $y++) { for ($x=0; $x -lt 64; $x++) { $mouth.SetPixel($x,$y,$upper) } }
    foreach ($shape in 'A','B','C','D','E','F','G','H','X') {
        $mouth.Save((Join-Path $root "mouth/$shape.png"),[Drawing.Imaging.ImageFormat]::Png)
    }
    $mouth.Dispose()
    $sets = @()
    foreach ($emotion in 'neutral','happy','angry','sad','afraid','guarded') {
        $frames = @()
        foreach ($shape in 'A','B','C','D','E','F','G','H','X') {
            $frames += [ordered]@{shape=$shape;file="mouth/$shape.png";difference="mouth/$shape-diff.png"}
        }
        $sets += [ordered]@{id=$emotion;label=$emotion;frames=$frames}
    }
    [ordered]@{sets=$sets} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'mouth/emotion-sets.json') -Encoding UTF8
    return $root
}

function Invoke-Builder([string]$Root) {
    $command = "& '$builderPath' -PortraitRoot '$Root'"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = Join-Path $PSHOME 'powershell.exe'
    $start.Arguments = "-NoProfile -NonInteractive -EncodedCommand $encoded"
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    return [pscustomobject]@{ExitCode=$process.ExitCode;Output=($stdout + $stderr)}
}

function Read-PixelColor([string]$Path, [int]$X, [int]$Y) {
    $bitmap = [Drawing.Bitmap]::FromFile($Path)
    try { return $bitmap.GetPixel($X,$Y) } finally { $bitmap.Dispose() }
}

try {
    if ($PSVersionTable.PSVersion.Major -ne 5) { throw "Run this test with Windows PowerShell 5; got $($PSVersionTable.PSVersion)" }
    $specified = New-Fixture 'specified-17' 17 $true
    $result = Invoke-Builder $specified
    if ($result.ExitCode -ne 0) { throw "Builder failed for browLockEndY=17: $($result.Output)" }
    $manifest = Get-Content -LiteralPath (Join-Path $specified 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $openPath = Join-Path $specified 'eyes/guarded/open.png'
    $halfPath = Join-Path $specified 'eyes/guarded/half.png'
    $closedPath = Join-Path $specified 'eyes/guarded/closed.png'
    Assert-Equal ([Drawing.Color]::FromArgb(255,220,10,10).ToArgb()) (Read-PixelColor $openPath 20 15).ToArgb() 'Open brow sample'
    Assert-Equal (Read-PixelColor $openPath 20 15).ToArgb() (Read-PixelColor $halfPath 20 15).ToArgb() 'Half blink must retain open brow'
    Assert-Equal (Read-PixelColor $openPath 20 15).ToArgb() (Read-PixelColor $closedPath 20 15).ToArgb() 'Closed blink must retain open brow'
    Assert-Equal ([Drawing.Color]::FromArgb(255,10,10,220).ToArgb()) (Read-PixelColor $closedPath 20 21).ToArgb() 'Closed iris row must use closed candidate below brow-lock bound'
    Assert-Equal @(12,17) @($manifest.browLockRows) 'Manifest must report actual brow-lock rows'
    $neutralOriginal = [Drawing.Color]::FromArgb(255,20,20,20).ToArgb()
    foreach ($phase in 'half','closed') {
        Assert-Equal $neutralOriginal (Read-PixelColor (Join-Path $specified "eyes/neutral/$phase.png") 20 15).ToArgb() "Neutral $phase brow must stay original-exact"
    }

    $default = New-Fixture 'default-22' $null $false
    $result = Invoke-Builder $default
    if ($result.ExitCode -ne 0) { throw "Builder failed with default brow-lock bound: $($result.Output)" }
    $manifest = Get-Content -LiteralPath (Join-Path $default 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal @(12,22) @($manifest.browLockRows) 'Default manifest must preserve legacy bound'
    Assert-Equal ([Drawing.Color]::FromArgb(255,220,10,10).ToArgb()) (Read-PixelColor (Join-Path $default 'eyes/guarded/closed.png') 20 21).ToArgb() 'Default must preserve legacy closed iris lock through row 22'
    $defaultVerification = Get-Content -LiteralPath (Join-Path $default 'pixel-verification.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -ne $defaultVerification.PSObject.Properties['outsideMaskChangedPixels']) { throw 'No boundary metadata must leave the default pixel-verification shape unchanged' }
    $defaultEyeRecord = $defaultVerification.records | Where-Object { $_.kind -eq 'eyes' } | Select-Object -First 1
    if ($null -ne $defaultEyeRecord.PSObject.Properties['outsideMaskChangedPixels']) { throw 'No boundary metadata must leave the default eye-record shape unchanged' }
    if ($null -ne $manifest.PSObject.Properties['boundaryRepairs']) { throw 'No boundary metadata must leave the default manifest shape unchanged' }

    $boundary = New-Fixture 'boundary-repair' 24 $true $true
    $result = Invoke-Builder $boundary
    if ($result.ExitCode -ne 0) { throw "Builder failed for declared boundary repairs: $($result.Output)" }
    foreach ($phase in 'open','half','closed') {
        $upperPath = Join-Path $boundary "eyes/guarded/$phase.png"
        Assert-Equal ([Drawing.Color]::FromArgb(255,60,50,40).ToArgb()) (Read-PixelColor $upperPath 37 20).ToArgb() "Boundary pixel must blend original and selected raw donor in $phase"
        Assert-Equal ([Drawing.Color]::FromArgb(255,120,100,80).ToArgb()) (Read-PixelColor $upperPath 38 20).ToArgb() "Mask-edge pixel must use selected raw donor in $phase"
        Assert-Equal ([Drawing.Color]::FromArgb(255,20,20,20).ToArgb()) (Read-PixelColor $upperPath 36 20).ToArgb() "Unlisted outside pixel must remain original in $phase"
    }
    $manifest = Get-Content -LiteralPath (Join-Path $boundary 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $guardedSet = $manifest.eyeSets | Where-Object id -eq 'guarded'
    foreach ($phase in 'open','half','closed') {
        $phaseRecord = $guardedSet.phases.$phase
        Assert-Equal 1 $phaseRecord.outsideMaskChangedPixels "Manifest must report one outside-mask pixel in $phase"
        Assert-Equal 2 @($phaseRecord.boundaryRepairs).Count "Manifest must expose both repair pixels in $phase"
        $outsideRepair = $phaseRecord.boundaryRepairs | Where-Object area -eq 'outside-eye-mask'
        Assert-Equal 37 $outsideRepair.x "Manifest must report the true extended x range in $phase"
        Assert-Equal '141414' $outsideRepair.originalRgb "Manifest must pin the original color in $phase"
        Assert-Equal '64503C' $outsideRepair.donor.rgb "Manifest must identify selected raw donor color in $phase"
        Assert-Equal 'eyes/guarded/raw.png' $outsideRepair.donor.sourceFile "Manifest must identify the selected raw donor file in $phase"
        Assert-Equal 37 $outsideRepair.donor.x "Manifest must report the raw donor coordinate in $phase"
        Assert-Equal '3C3228' $outsideRepair.outputRgb "Manifest must report the resulting color in $phase"
    }
    $boundaryVerification = Get-Content -LiteralPath (Join-Path $boundary 'pixel-verification.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 3 $boundaryVerification.outsideMaskChangedPixels 'Verifier must count three declared outside-mask frame changes'
    Assert-Equal 3 $boundaryVerification.declaredOutsideMaskChangedPixels 'Verifier must separately report declared outside-mask writes'
    Assert-Equal 0 $boundaryVerification.undeclaredOutsideMaskChangedPixels 'Verifier must report no hidden outside-mask writes'
    Assert-Equal 6 $boundaryVerification.boundaryRepairApplications 'Verifier must count both repair pixels across all phases'

    $zero = New-Fixture 'lower-bound-0' 0 $true
    $result = Invoke-Builder $zero
    if ($result.ExitCode -ne 0) { throw "Builder rejected lower valid bound 0: $($result.Output)" }
    $manifest = Get-Content -LiteralPath (Join-Path $zero 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal @() @($manifest.browLockRows) 'Manifest must omit empty intersection when bound precedes eye regions'
    Assert-Equal ([Drawing.Color]::FromArgb(255,10,10,220).ToArgb()) (Read-PixelColor (Join-Path $zero 'eyes/guarded/closed.png') 20 15).ToArgb() 'Bound 0 must not lock rows below bound'

    $last = New-Fixture 'upper-bound-34' 34 $true
    $result = Invoke-Builder $last
    if ($result.ExitCode -ne 0) { throw "Builder rejected upper valid bound splitY-1: $($result.Output)" }
    $manifest = Get-Content -LiteralPath (Join-Path $last 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal @(12,34) @($manifest.browLockRows) 'Manifest must report upper valid bound'

    foreach ($bad in @(@{name='decimal';value=17.5},@{name='negative';value=-1},@{name='split';value=35})) {
        $invalid = New-Fixture ('invalid-'+$bad.name) $bad.value $true
        $result = Invoke-Builder $invalid
        if ($result.ExitCode -eq 0 -or -not $result.Output.Contains('browLockEndY')) { throw "Invalid browLockEndY '$($bad.value)' was not rejected: $($result.Output)" }
    }
    "PORTRAIT_BROW_BOUNDARY_PASS specified=17 default=22 invalid=3 fixtures=$runRoot"
} catch {
    Write-Error $_
    throw
}
