[CmdletBinding()]
param([string]$PortraitRoot)
. (Join-Path $PSScriptRoot 'TextProtocol.ps1')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PortraitRoot)) {
    $root = Join-Path $repoRoot 'art/portraits/mobile-mouths/split-mask-prototype'
} elseif ([IO.Path]::IsPathRooted($PortraitRoot)) {
    $root = [IO.Path]::GetFullPath($PortraitRoot)
} else {
    $root = [IO.Path]::GetFullPath((Join-Path $repoRoot $PortraitRoot))
}
$layout = Get-Content -LiteralPath (Join-Path $root 'layout.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$width = [int]$layout.width
$height = [int]$layout.height
$splitY = [int]$layout.splitY
if ($width -ne 64 -or $height -ne 64 -or $splitY -ne 35) { throw "Only native 64x64 portraits with splitY=35 are supported for now; layout declares ${width}x${height} splitY=$splitY" }
$browLockEndY = 22
if ($null -ne $layout.browLockEndY) {
    $configuredBrowLockEndY = $layout.browLockEndY
    if ($configuredBrowLockEndY -isnot [int] -and $configuredBrowLockEndY -isnot [long]) {
        throw 'layout.browLockEndY must be an integer from 0 through splitY-1'
    }
    if ($configuredBrowLockEndY -lt 0 -or $configuredBrowLockEndY -ge $splitY) {
        throw 'layout.browLockEndY must be an integer from 0 through splitY-1'
    }
    $browLockEndY = [int]$configuredBrowLockEndY
}
$browLockStartY = $null
foreach ($region in $layout.eyeRegions) {
    if ($null -eq $browLockStartY -or [int]$region.y -lt $browLockStartY) { $browLockStartY = [int]$region.y }
}
$browLockRows = @()
if ($null -ne $browLockStartY -and $browLockEndY -ge $browLockStartY) {
    $browLockRows = @($browLockStartY,$browLockEndY)
}
$lowerHeight = $height - $splitY
$original = [Drawing.Bitmap]::FromFile((Join-Path $root 'original.png'))
if ($original.Width -ne $width -or $original.Height -ne $height) { $original.Dispose(); throw "Original dimensions do not match layout: expected ${width}x${height}" }
$records = [Collections.Generic.List[object]]::new()
$labels = @{neutral='Neutral';happy=('Fr'+[char]0x00f6+'hlich');angry=('W'+[char]0x00fc+'tend');sad='Traurig';afraid=([string][char]0x00c4+'ngstlich');guarded='Skeptisch'}
$sets = [Collections.Generic.List[object]]::new()
$referencePalette = $null
if ($layout.useOriginalFeaturePalette) {
    . (Join-Path $PSScriptRoot 'PortraitReferencePalette.ps1')
    $referencePalette = New-OriginalFeaturePalette -Original $original -Regions $layout.eyeRegions
}

function In-EyeRegion([int]$X, [int]$Y) {
    foreach ($region in $layout.eyeRegions) {
        if ($X -ge $region.x -and $X -lt ($region.x+$region.width) -and $Y -ge $region.y -and $Y -lt ($region.y+$region.height)) { return $true }
    }
    return $false
}

function Get-RgbHex([Drawing.Color]$Color) {
    return ('{0:X2}{1:X2}{2:X2}' -f $Color.R,$Color.G,$Color.B)
}

$boundaryRepairs = @()
$boundaryRepairProperty = $layout.PSObject.Properties['boundaryRepairs']
if ($null -ne $boundaryRepairProperty) {
    if ($boundaryRepairProperty.Value -is [string] -or $boundaryRepairProperty.Value -isnot [array]) {
        throw 'layout.boundaryRepairs must be an array of explicit pixel repairs'
    }
    $boundaryRepairs = @($boundaryRepairProperty.Value)
}
$boundaryRepairKeys = @{}
foreach ($repair in $boundaryRepairs) {
    $requiredProperties = @('id','emotion','phases','x','y','area','expectedOriginalRgb','expectedBeforeRgb','donorSourceFile','donorSourceSha256','donorPhase','donorX','donorY','expectedDonorRgb','operation','expectedOutputRgb')
    foreach ($propertyName in $requiredProperties) {
        if ($null -eq $repair.PSObject.Properties[$propertyName]) { throw "layout.boundaryRepairs entries require $propertyName" }
    }
    if ([string]::IsNullOrWhiteSpace([string]$repair.id)) { throw 'layout.boundaryRepairs.id must be non-empty' }
    if (@('neutral','happy','angry','sad','afraid','guarded') -notcontains [string]$repair.emotion) { throw "Unsupported boundary repair emotion: $($repair.emotion)" }
    if ($repair.phases -is [string] -or $repair.phases -isnot [array] -or @($repair.phases).Count -lt 1) { throw "Boundary repair $($repair.id) phases must be a non-empty array" }
    foreach ($phase in $repair.phases) { if (@('open','half','closed') -notcontains [string]$phase) { throw "Unsupported boundary repair phase: $phase" } }
    foreach ($coordinateName in @('x','y','donorX','donorY')) {
        $coordinate = $repair.$coordinateName
        if ($coordinate -isnot [int] -and $coordinate -isnot [long]) { throw "Boundary repair $($repair.id) $coordinateName must be an integer" }
    }
    if ($repair.x -lt 0 -or $repair.x -ge $width -or $repair.y -lt 0 -or $repair.y -ge $splitY -or $repair.donorX -lt 0 -or $repair.donorX -ge $width -or $repair.donorY -lt 0 -or $repair.donorY -ge $splitY) {
        throw "Boundary repair $($repair.id) coordinates must fit the native upper image"
    }
    foreach ($rgbName in @('expectedOriginalRgb','expectedBeforeRgb','expectedDonorRgb','expectedOutputRgb')) {
        if ((-not (Test-HexValue ([string]$repair.$rgbName) 6))) { throw "Boundary repair $($repair.id) $rgbName must be six-digit RGB hex" }
    }
    if (([IO.Path]::IsPathRooted([string]$repair.donorSourceFile) -or ([string]$repair.donorSourceFile).Contains(':')) -or (([string]$repair.donorSourceFile).Replace('\','/').Split([char]'/') | Where-Object { $_ -eq '..' }).Count -gt 0) { throw "Boundary repair $($repair.id) donorSourceFile must stay inside the portrait root" }
    if ((-not (Test-HexValue ([string]$repair.donorSourceSha256) 64))) { throw "Boundary repair $($repair.id) donorSourceSha256 must be a SHA-256 hash" }
    if ([string]$repair.donorPhase -ne 'open') { throw "Boundary repair $($repair.id) supports only the open raw donor phase" }
    if (@('outside-eye-mask','inside-eye-mask') -notcontains [string]$repair.area) { throw "Boundary repair $($repair.id) area must be inside-eye-mask or outside-eye-mask" }
    $isInsideMask = In-EyeRegion ([int]$repair.x) ([int]$repair.y)
    $actualArea = if ($isInsideMask) { 'inside-eye-mask' } else { 'outside-eye-mask' }
    if ([string]$repair.area -ne $actualArea) { throw "Boundary repair $($repair.id) claims $($repair.area) but coordinate ($($repair.x),$($repair.y)) is $actualArea" }
    if (@('blend-original','copy-donor') -notcontains [string]$repair.operation) { throw "Boundary repair $($repair.id) operation must be blend-original or copy-donor" }
    if ([string]$repair.operation -eq 'blend-original') {
        if ($null -eq $repair.PSObject.Properties['weight'] -or $repair.weight -isnot [double] -and $repair.weight -isnot [decimal] -and $repair.weight -isnot [int] -and $repair.weight -isnot [long] -or [double]$repair.weight -lt 0 -or [double]$repair.weight -gt 1) { throw "Boundary repair $($repair.id) blend-original weight must be numeric from 0 through 1" }
    } elseif ($null -ne $repair.PSObject.Properties['weight']) {
        throw "Boundary repair $($repair.id) copy-donor must not declare weight"
    }
    if ($repair.area -eq 'outside-eye-mask' -and [string]$repair.expectedOriginalRgb -eq [string]$repair.expectedOutputRgb) { throw "Boundary repair $($repair.id) outside-mask output must change its original pixel" }
    foreach ($phase in $repair.phases) {
        $key = '{0}/{1}/{2}/{3}' -f $repair.emotion,$phase,$repair.x,$repair.y
        if ($boundaryRepairKeys.ContainsKey($key)) { throw "Duplicate boundary repair coordinate: $key" }
        $boundaryRepairKeys[$key] = $repair.id
    }
}
$boundaryRepairApplications = [Collections.Generic.List[object]]::new()
$boundaryOutsideMaskChanges = [Collections.Generic.List[object]]::new()

function Save-Upper([Drawing.Bitmap]$Candidate, [string]$Emotion, [string]$Phase, [Drawing.Bitmap]$OpenCandidate) {
    $output = [Drawing.Bitmap]::new($width,$splitY)
    $difference = [Drawing.Bitmap]::new($width,$splitY)
    $changed = 0
    $outside = 0
    $lockSources = @{}
    $frameRepairs = @($boundaryRepairs | Where-Object { $_.emotion -eq $Emotion -and @($_.phases) -contains $Phase })
    try {
        foreach ($lock in $layout.eyeFeatureLocks) {
            if ($Emotion -eq $lock.emotion -and $Phase -eq $lock.phase -and $lock.sourceFile -and -not $lockSources.ContainsKey([string]$lock.sourceFile)) {
                $source = [Drawing.Bitmap]::FromFile((Join-Path $root ([string]$lock.sourceFile)))
                $lockSources[[string]$lock.sourceFile] = $source
                if ($source.Width -ne $width -or $source.Height -ne $splitY) { throw "Eye feature source dimensions: $($lock.sourceFile)" }
            }
        }
        for ($y=0; $y -lt $splitY; $y++) {
            for ($x=0; $x -lt $width; $x++) {
                $base = $original.GetPixel($x,$y)
                $pixel = $base
                if (-not ($Emotion -eq 'neutral' -and $Phase -eq 'open')) {
                    foreach ($region in $layout.eyeRegions) {
                        if ($x -ge $region.x -and $x -lt ($region.x+$region.width) -and $y -ge $region.y -and $y -lt ($region.y+$region.height)) {
                            # The one-pixel transition remains entirely inside the declared region.
                            # All source eyes and brows lie in the fully opaque inner region.
                            $edge = [Math]::Min([Math]::Min($x-$region.x,$region.x+$region.width-1-$x),[Math]::Min($y-$region.y,$region.y+$region.height-1-$y))
                            $weight = [Math]::Min(1.0,($edge+1)/2.0)
                            $sample = $Candidate.GetPixel($x,$y)
                            # Brows use one master for this emotion, never a regenerated blink brow.
                            if ($Phase -ne 'open' -and $y -le $browLockEndY) {
                                $sample = if ($Emotion -eq 'neutral') { $base } else { $OpenCandidate.GetPixel($x,$y) }
                            }
                            # A blink covers the eyeball vertically; it must not replace an iris
                            # column with sclera. These native feature locks belong to this atlas.
                            foreach ($lock in $layout.eyeFeatureLocks) {
                                if ($Emotion -eq $lock.emotion -and $Phase -eq $lock.phase -and
                                    $x -ge $lock.x -and $x -lt ($lock.x+$lock.width) -and
                                    $y -ge $lock.y -and $y -lt ($lock.y+$lock.height)) {
                                    $sample = if ($lock.sourceFile) { $lockSources[[string]$lock.sourceFile].GetPixel($x,$y) } else { $OpenCandidate.GetPixel($x,$y) }
                                }
                            }
                            foreach ($repair in $layout.pixelRepairs) {
                                if ($Emotion -eq $repair.emotion -and $Phase -eq $repair.phase -and $x -eq $repair.x -and $y -eq $repair.y) {
                                    $sourceRgb = '{0:X2}{1:X2}{2:X2}' -f $sample.R,$sample.G,$sample.B
                                    if ($sourceRgb -ne $repair.expectedSourceRgb) { throw "Pixel repair source changed: $Emotion/$Phase ($x,$y) expected $($repair.expectedSourceRgb), got $sourceRgb" }
                                    $sample = $Candidate.GetPixel($repair.copyFromX,$repair.copyFromY)
                                }
                            }
                            $pixel = [Drawing.Color]::FromArgb(255,
                                [int][Math]::Round($base.R+($sample.R-$base.R)*$weight),
                                [int][Math]::Round($base.G+($sample.G-$base.G)*$weight),
                                [int][Math]::Round($base.B+($sample.B-$base.B)*$weight))
                        }
                    }
                }
                if ($referencePalette -and (In-EyeRegion $x $y)) { $pixel = $referencePalette.Quantize($pixel) }
                $output.SetPixel($x,$y,$pixel)
            }
        }

        $appliedFrameRepairs = [Collections.Generic.List[object]]::new()
        foreach ($repair in $frameRepairs) {
            $selectedSourcePath = [IO.Path]::GetFullPath((Get-EyeSource $Emotion ([string]$repair.donorPhase)))
            $declaredSourcePath = [IO.Path]::GetFullPath((Join-Path $root ([string]$repair.donorSourceFile)))
            if (-not [string]::Equals($selectedSourcePath,$declaredSourcePath,[StringComparison]::OrdinalIgnoreCase)) {
                throw "Boundary repair $($repair.id) donor file is not the selected raw source for $Emotion"
            }
            if (-not (Test-Path -LiteralPath $selectedSourcePath)) { throw "Boundary repair donor source missing: $selectedSourcePath" }
            $actualSourceSha = (Get-FileHash -LiteralPath $selectedSourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actualSourceSha -ne ([string]$repair.donorSourceSha256).ToLowerInvariant()) { throw "Boundary repair donor file hash changed: $($repair.id)" }
            if ($null -eq $OpenCandidate) { throw "Boundary repair $($repair.id) has no selected open donor cell" }

            $x = [int]$repair.x; $y = [int]$repair.y
            $base = $original.GetPixel($x,$y)
            $before = $output.GetPixel($x,$y)
            $donor = $OpenCandidate.GetPixel([int]$repair.donorX,[int]$repair.donorY)
            $originalRgb = Get-RgbHex $base
            $beforeRgb = Get-RgbHex $before
            $donorRgb = Get-RgbHex $donor
            if ($originalRgb -ne ([string]$repair.expectedOriginalRgb).ToUpperInvariant()) { throw "Boundary repair original changed: $($repair.id) expected $($repair.expectedOriginalRgb), got $originalRgb" }
            if ($beforeRgb -ne ([string]$repair.expectedBeforeRgb).ToUpperInvariant()) { throw "Boundary repair prior composite changed: $($repair.id) expected $($repair.expectedBeforeRgb), got $beforeRgb" }
            if ($donorRgb -ne ([string]$repair.expectedDonorRgb).ToUpperInvariant()) { throw "Boundary repair donor pixel changed: $($repair.id) expected $($repair.expectedDonorRgb), got $donorRgb" }

            if ([string]$repair.operation -eq 'blend-original') {
                $weight = [double]$repair.weight
                $repaired = [Drawing.Color]::FromArgb(255,
                    [int][Math]::Round($base.R+($donor.R-$base.R)*$weight),
                    [int][Math]::Round($base.G+($donor.G-$base.G)*$weight),
                    [int][Math]::Round($base.B+($donor.B-$base.B)*$weight))
            } else { $repaired = [Drawing.Color]::FromArgb(255,$donor.R,$donor.G,$donor.B) }
            $outputRgb = Get-RgbHex $repaired
            if ($outputRgb -ne ([string]$repair.expectedOutputRgb).ToUpperInvariant()) { throw "Boundary repair calculated RGB changed: $($repair.id) expected $($repair.expectedOutputRgb), got $outputRgb" }
            $output.SetPixel($x,$y,$repaired)

            $application = [ordered]@{
                id=[string]$repair.id;emotion=$Emotion;phase=$Phase;x=$x;y=$y;area=[string]$repair.area
                originalRgb=$originalRgb;beforeRgb=$beforeRgb
                donor=[ordered]@{sourceFile=[string]$repair.donorSourceFile;sha256=$actualSourceSha;phase=[string]$repair.donorPhase;x=[int]$repair.donorX;y=[int]$repair.donorY;rgb=$donorRgb}
                operation=[string]$repair.operation;outputRgb=$outputRgb
            }
            if ([string]$repair.operation -eq 'blend-original') { $application.weight=[double]$repair.weight }
            $appliedFrameRepairs.Add($application)
            $boundaryRepairApplications.Add($application)
        }

        # Recount against the final output and allow only explicitly declared
        # boundary writes outside the original eye masks.
        for ($y=0; $y -lt $splitY; $y++) {
            for ($x=0; $x -lt $width; $x++) {
                $base = $original.GetPixel($x,$y)
                $pixel = $output.GetPixel($x,$y)
                if ($pixel.ToArgb() -ne $base.ToArgb()) {
                    $changed++
                    if (-not (In-EyeRegion $x $y)) {
                        $outside++
                        $matchingRepair = @($appliedFrameRepairs | Where-Object { $_.x -eq $x -and $_.y -eq $y -and $_.area -eq 'outside-eye-mask' })
                        if ($matchingRepair.Count -ne 1 -or $matchingRepair[0].outputRgb -ne (Get-RgbHex $pixel)) { throw "Pixels changed outside eye mask without one matching boundary repair: $Emotion/$Phase ($x,$y)" }
                        $boundaryOutsideMaskChanges.Add($matchingRepair[0])
                    }
                    $difference.SetPixel($x,$y,[Drawing.Color]::Magenta)
                } else { $difference.SetPixel($x,$y,[Drawing.Color]::Black) }
            }
        }
        $expectedOutsideCount = @($appliedFrameRepairs | Where-Object { $_.area -eq 'outside-eye-mask' }).Count
        if ($outside -ne $expectedOutsideCount) { throw "Declared outside-mask repair count mismatch: $Emotion/$Phase expected $expectedOutsideCount, got $outside" }
        $relative = 'eyes/'+$Emotion+'/'+$Phase+'.png'
        $path = Join-Path $root $relative
        $output.Save($path,[Drawing.Imaging.ImageFormat]::Png)
        $diffRelative = 'eyes/'+$Emotion+'/'+$Phase+'-diff.png'
        $difference.Save((Join-Path $root $diffRelative),[Drawing.Imaging.ImageFormat]::Png)
        $eyeRecord = [ordered]@{kind='eyes';emotion=$Emotion;phase=$Phase;file=$relative;difference=$diffRelative;width=$width;height=$splitY;changedPixels=$changed;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
        $phaseResult = [ordered]@{file=$relative;difference=$diffRelative;changedPixels=$changed}
        if ($appliedFrameRepairs.Count -gt 0) {
            $eyeRecord.outsideMaskChangedPixels = $outside
            $eyeRecord.boundaryRepairs = $appliedFrameRepairs.ToArray()
            $phaseResult.outsideMaskChangedPixels = $outside
            $phaseResult.outsideMaskChanges = @($appliedFrameRepairs | Where-Object { $_.area -eq 'outside-eye-mask' })
            $phaseResult.boundaryRepairs = $appliedFrameRepairs.ToArray()
        }
        $records.Add($eyeRecord)
        return $phaseResult
    } finally { foreach ($source in $lockSources.Values) { $source.Dispose() };$output.Dispose();$difference.Dispose() }
}

try {
    function Get-SelectionPath($Selection) {
        if ($null -eq $Selection) { return $null }
        if ($Selection -is [string]) { return [string]$Selection }
        if ($Selection.file) { return [string]$Selection.file }
        return $null
    }
    function Get-EyeSource([string]$Emotion, [string]$Phase) {
        $selection = $null
        if ($layout.sourceSelections -and $layout.sourceSelections.eyes) {
            $emotionSelection = $layout.sourceSelections.eyes.$Emotion
            if ($emotionSelection) {
                if ($emotionSelection -is [string]) { $selection = $emotionSelection }
                elseif ($Phase -and $emotionSelection.$Phase) { $selection = $emotionSelection.$Phase }
                elseif ($emotionSelection.raw) { $selection = $emotionSelection.raw }
                elseif ($emotionSelection.default) { $selection = $emotionSelection.default }
                elseif ($emotionSelection.file) { $selection = $emotionSelection }
            }
        }
        $selectedPath = Get-SelectionPath $selection
        if ($selectedPath) { return Join-Path $root $selectedPath }

        $basePath = Join-Path $root ('eyes/'+$Emotion+'/raw.png')
        if ([int]$layout.portraitRecord -eq 77) {
            $correctedPath = Join-Path $root ('eyes/'+$Emotion+'/raw-corrected.png')
            if (Test-Path -LiteralPath $correctedPath) { $basePath = $correctedPath }
            if ($Emotion -eq 'guarded') {
                foreach ($version in 3,4,5) {
                    $versionPath = Join-Path $root "eyes/guarded/raw-v$version.png"
                    if (Test-Path -LiteralPath $versionPath) { $basePath = $versionPath }
                }
            }
            if ($Phase -eq 'closed' -and $Emotion -eq 'happy') {
                $overridePath = Join-Path $root 'eyes/happy/correction-raw.png'
                if (Test-Path -LiteralPath $overridePath) { return $overridePath }
            }
            if ($Phase -eq 'half' -and $Emotion -eq 'sad') {
                $overridePath = Join-Path $root 'eyes/sad/half-correction-v3.png'
                if (Test-Path -LiteralPath $overridePath) { return $overridePath }
            }
        }
        return $basePath
    }
    foreach ($emotion in @('neutral','happy','angry','sad','afraid','guarded')) {
        $rawPath = Get-EyeSource $emotion $null
        if (-not (Test-Path -LiteralPath $rawPath)) { continue }
        $raw = [Drawing.Bitmap]::FromFile($rawPath)
        $openCandidate = $null
        try {
            if ([Math]::Abs(($raw.Width/[double]$raw.Height)/($width/[double]$splitY)-1) -gt 0.02) { throw "Eye sheet aspect mismatch: $emotion" }
            $phaseFiles = [ordered]@{}
            $index = 0
            foreach ($phase in @('open','half','closed')) {
                $candidate = [Drawing.Bitmap]::new($width,$splitY)
                $graphics = [Drawing.Graphics]::FromImage($candidate)
                $phaseRaw = $raw
                $overrideImage = $null
                try {
                    $phasePath = Get-EyeSource $emotion $phase
                    if ($phasePath -ne $rawPath) {
                        if (-not (Test-Path -LiteralPath $phasePath)) { throw "Selected eye source missing: $emotion/$phase ($phasePath)" }
                        $overrideImage = [Drawing.Bitmap]::FromFile($phasePath)
                        $phaseRaw = $overrideImage
                    }
                    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
                    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
                    $cellWidth = $phaseRaw.Width/2.0
                    $cellHeight = $phaseRaw.Height/2.0
                    $graphics.DrawImage($phaseRaw,[Drawing.Rectangle]::new(0,0,$width,$splitY),[single](($index%2)*$cellWidth),[single]([Math]::Floor($index/2)*$cellHeight),[single]$cellWidth,[single]$cellHeight,[Drawing.GraphicsUnit]::Pixel)
                    if ($phase -eq 'open') { $openCandidate = [Drawing.Bitmap]$candidate.Clone() }
                    $phaseFiles[$phase] = Save-Upper $candidate $emotion $phase $openCandidate
                } finally { if ($overrideImage) { $overrideImage.Dispose() };$graphics.Dispose();$candidate.Dispose() }
                $index++
            }
            $sets.Add([ordered]@{id=$emotion;label=$labels[$emotion];phases=$phaseFiles})
        } finally { if ($openCandidate) { $openCandidate.Dispose() };$raw.Dispose() }
    }
    $mouths = [Collections.Generic.List[object]]::new()
    foreach ($shape in 'A','B','C','D','E','F','G','H','X') {
        $relative = 'mouth/'+$shape+'.png'
        $path = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $path)) { throw "Mouth half missing: $shape" }
        $image = [Drawing.Bitmap]::FromFile($path)
        $diff = [Drawing.Bitmap]::new($width,$lowerHeight)
        $changed = 0
        $outside = 0
        try {
            if ($image.Width -ne $width -or $image.Height -ne $lowerHeight) { throw "Mouth dimensions: $shape" }
            for ($y=0; $y -lt $lowerHeight; $y++) {
                for ($x=0; $x -lt $width; $x++) {
                    $fullY = $y+$splitY
                    if ($image.GetPixel($x,$y).ToArgb() -ne $original.GetPixel($x,$fullY).ToArgb()) {
                        $changed++
                        $region = $layout.mouthRegion
                        if ($x -lt $region.x -or $x -ge ($region.x+$region.width) -or $fullY -lt $region.y -or $fullY -ge ($region.y+$region.height)) { $outside++ }
                        $diff.SetPixel($x,$y,[Drawing.Color]::Magenta)
                    } else { $diff.SetPixel($x,$y,[Drawing.Color]::Black) }
                }
            }
            if ($outside -ne 0) { throw "Pixels changed outside mouth mask: $shape" }
            if ($shape -in 'A','X' -and $changed -ne 0) { throw "Closed native mouth changed: $shape" }
            $diffRelative = 'mouth/'+$shape+'-diff.png'
            $diff.Save((Join-Path $root $diffRelative),[Drawing.Imaging.ImageFormat]::Png)
            $mouths.Add([ordered]@{shape=$shape;file=$relative;difference=$diffRelative;changedPixels=$changed})
            $records.Add([ordered]@{kind='mouth';shape=$shape;file=$relative;width=$width;height=$lowerHeight;changedPixels=$changed;outsideMaskChangedPixels=$outside;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash})
        } finally { $image.Dispose();$diff.Dispose() }
    }
    if ($sets.Count -lt 1) { throw 'No complete eye sets' }
    $emotionMouths = Get-Content -LiteralPath (Join-Path $root 'mouth/emotion-sets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($emotionMouths.sets.Count -ne 6) { throw 'Six emotion mouth sets required' }
    $repoUri = [Uri]($repoRoot.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar)
    $rootUri = [Uri]($root.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar)
    $prefix = [Uri]::UnescapeDataString($repoUri.MakeRelativeUri($rootUri).ToString())
    foreach ($mouthSet in $emotionMouths.sets) {
        $mouthSet.label = $labels[$mouthSet.id]
        foreach ($mouthFrame in $mouthSet.frames) {
            if ($mouthFrame.file.StartsWith($prefix)) { $mouthFrame.file = $mouthFrame.file.Substring($prefix.Length) }
            if ($mouthFrame.difference.StartsWith($prefix)) { $mouthFrame.difference = $mouthFrame.difference.Substring($prefix.Length) }
        }
    }
    $portraitRecord = if ($null -ne $layout.portraitRecord) { $layout.portraitRecord } else { 77 }
    $bank = if ($layout.bank) { $layout.bank } else { 'TFAC00I0.RCI' }
    $manifest = [ordered]@{schemaVersion=2;portraitRecord=$portraitRecord;bank=$bank;status='masked-half-prototype-visual-review-pending-not-installed';width=$width;height=$height;splitY=$splitY;original='original.png';originalUpper='source-upper.png';originalLower='source-lower.png';eyeSets=$sets.ToArray();mouths=$mouths.ToArray();mouthSets=$emotionMouths.sets;audio='../sample.wav';timing='../sample.visemes.json';visualAcceptance='pending';browLockRows=$browLockRows;eyeRegions=$layout.eyeRegions;eyeFeatureLocks=$layout.eyeFeatureLocks}
    if ($boundaryRepairs.Count -gt 0) {
        $expectedApplicationCount = 0
        foreach ($repair in $boundaryRepairs) { $expectedApplicationCount += @($repair.phases).Count }
        if ($boundaryRepairApplications.Count -ne $expectedApplicationCount) { throw "Boundary repair application count mismatch: expected $expectedApplicationCount, got $($boundaryRepairApplications.Count)" }
        $outsideRecords = $boundaryOutsideMaskChanges.ToArray()
        $declaredOutsideCount = @($boundaryRepairApplications.ToArray() | Where-Object { $_.area -eq 'outside-eye-mask' }).Count
        if ($outsideRecords.Count -ne $declaredOutsideCount) { throw "Declared outside-mask repair total mismatch: expected $declaredOutsideCount, got $($outsideRecords.Count)" }
        $manifest.boundaryRepairSummary = [ordered]@{
            declaredPixelRules=$boundaryRepairs
            appliedPixelRepairs=$boundaryRepairApplications.ToArray()
            outsideMaskChangedPixels=$outsideRecords.Count
            outsideMaskChanges=$outsideRecords
        }
    }
    $audioManifestPath = Join-Path $root 'audio/emotion-samples.json'
    $manifest.useOriginalFeaturePalette = [bool]$layout.useOriginalFeaturePalette
    if (Test-Path -LiteralPath $audioManifestPath) {
        $audioManifest = Get-Content -LiteralPath $audioManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($audioManifest.audioSets.Count -ne 6) { throw 'Six emotional audio sets required' }
        $manifest.audioSets = $audioManifest.audioSets
    }
    $manifest | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $root 'manifest.json') -Encoding UTF8
    $pixelVerification = [ordered]@{status='pixel-invariants-pass-not-visual-acceptance';eyes=$sets.Count;parts=$records.Count;records=$records.ToArray()}
    if ($boundaryRepairs.Count -gt 0) {
        $outsideMaskTotal = $boundaryOutsideMaskChanges.Count
        $declaredOutsideTotal = @($boundaryRepairApplications.ToArray() | Where-Object { $_.area -eq 'outside-eye-mask' }).Count
        $pixelVerification.outsideMaskChangedPixels = $outsideMaskTotal
        $pixelVerification.declaredOutsideMaskChangedPixels = $declaredOutsideTotal
        $pixelVerification.undeclaredOutsideMaskChangedPixels = 0
        $pixelVerification.boundaryRepairApplications = $boundaryRepairApplications.Count
        $pixelVerification.outsideMaskChanges = $boundaryOutsideMaskChanges.ToArray()
    }
    $pixelVerification | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $root 'pixel-verification.json') -Encoding UTF8
    if ($boundaryRepairs.Count -gt 0) { "SPLIT_PORTRAIT_PIXEL_PASS eyeSets=$($sets.Count) parts=$($records.Count) outsideMaskChanges=$outsideMaskTotal" }
    else { "SPLIT_PORTRAIT_PIXEL_PASS eyeSets=$($sets.Count) parts=$($records.Count)" }
} finally { $original.Dispose() }
