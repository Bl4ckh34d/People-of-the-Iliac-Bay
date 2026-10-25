[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PortraitRoot
)

. (Join-Path $PSScriptRoot 'TextProtocol.ps1')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([IO.Path]::IsPathRooted($PortraitRoot)) {
    $root = [IO.Path]::GetFullPath($PortraitRoot)
} else {
    $root = [IO.Path]::GetFullPath((Join-Path $repoRoot $PortraitRoot))
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-RootRelativePath([string]$Path) {
    $prefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $Path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw "Input path is outside PortraitRoot: $Path" }
    $Path.Substring($prefix.Length).Replace([IO.Path]::DirectorySeparatorChar,'/')
}

function Assert-Integer([object]$Value, [string]$Name) {
    if ($Value -isnot [int] -and $Value -isnot [long]) { throw "$Name must be an integer" }
}

function Assert-Rect([object]$Rect, [string]$Name, [int]$CanvasWidth, [int]$CanvasHeight) {
    foreach ($field in @('x','y','width','height')) {
        if ($null -eq $Rect.$field) { throw "$Name.$field is required" }
        Assert-Integer $Rect.$field "$Name.$field"
    }
    if ($Rect.x -lt 0 -or $Rect.y -lt 0 -or $Rect.width -le 0 -or $Rect.height -le 0 -or
        ($Rect.x + $Rect.width) -gt $CanvasWidth -or ($Rect.y + $Rect.height) -gt $CanvasHeight) {
        throw "$Name rectangle is outside ${CanvasWidth}x${CanvasHeight} bounds"
    }
}

function Test-RectContains([object]$Outer, [object]$Inner) {
    return ($Inner.x -ge $Outer.x -and $Inner.y -ge $Outer.y -and
        ($Inner.x + $Inner.width) -le ($Outer.x + $Outer.width) -and
        ($Inner.y + $Inner.height) -le ($Outer.y + $Outer.height))
}

function Test-InEyeMask([int]$X, [int]$Y, [object[]]$Regions) {
    foreach ($region in $Regions) {
        if ($X -ge $region.x -and $X -lt ($region.x + $region.width) -and
            $Y -ge $region.y -and $Y -lt ($region.y + $region.height)) { return $true }
    }
    return $false
}

function Test-InRectList([int]$X, [int]$Y, [object[]]$Regions) {
    foreach ($region in $Regions) {
        if ($X -ge $region.x -and $X -lt ($region.x + $region.width) -and
            $Y -ge $region.y -and $Y -lt ($region.y + $region.height)) { return $true }
    }
    return $false
}

function Get-HalfOcclusionRegion([int]$X, [int]$Y, [object[]]$Regions) {
    foreach ($region in $Regions) {
        $target = $region.target
        if ($X -ge $target.x -and $X -lt ($target.x + $target.width) -and
            $Y -ge $target.y -and $Y -lt ($target.y + $target.height)) { return $region }
    }
    return $null
}

function Get-BrowBoundaryPixel([int]$X, [int]$Y, [object[]]$Pixels) {
    foreach ($pixel in $Pixels) { if ([int]$pixel.x -eq $X -and [int]$pixel.y -eq $Y) { return $pixel } }
    return $null
}

function Get-CellBounds([System.Drawing.Bitmap]$Image, [int]$Index, [int]$Columns, [int]$Rows) {
    $column = $Index % $Columns
    $row = [int][Math]::Floor($Index / $Columns)
    $left = [int][Math]::Floor($column * $Image.Width / [double]$Columns)
    $right = [int][Math]::Floor(($column + 1) * $Image.Width / [double]$Columns)
    $top = [int][Math]::Floor($row * $Image.Height / [double]$Rows)
    $bottom = [int][Math]::Floor(($row + 1) * $Image.Height / [double]$Rows)
    if ($right -le $left -or $bottom -le $top) { throw "Invalid 3x3 gaze cell $Index" }
    return [ordered]@{left=$left;top=$top;width=($right-$left);height=($bottom-$top);row=$row;column=$column}
}

function New-NativeCell([System.Drawing.Bitmap]$Image, [object]$Bounds, [int]$Width, [int]$Height) {
    $cell = [Drawing.Bitmap]::new($Width,$Height,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        for ($y=0; $y -lt $Height; $y++) {
            $sourceY = $Bounds.top + [int][Math]::Floor(($y + 0.5) * $Bounds.height / [double]$Height)
            if ($sourceY -ge ($Bounds.top + $Bounds.height)) { $sourceY = $Bounds.top + $Bounds.height - 1 }
            for ($x=0; $x -lt $Width; $x++) {
                $sourceX = $Bounds.left + [int][Math]::Floor(($x + 0.5) * $Bounds.width / [double]$Width)
                if ($sourceX -ge ($Bounds.left + $Bounds.width)) { $sourceX = $Bounds.left + $Bounds.width - 1 }
                $sample = $Image.GetPixel($sourceX,$sourceY)
                $cell.SetPixel($x,$y,[Drawing.Color]::FromArgb(255,$sample.R,$sample.G,$sample.B))
            }
        }
        return $cell
    } catch {
        $cell.Dispose()
        throw
    }
}

function Copy-Color([Drawing.Bitmap]$Destination, [int]$X, [int]$Y, [Drawing.Bitmap]$Source, [int]$SourceX, [int]$SourceY) {
    $sample = $Source.GetPixel($SourceX,$SourceY)
    $Destination.SetPixel($X,$Y,[Drawing.Color]::FromArgb(255,$sample.R,$sample.G,$sample.B))
}

function Get-OpaqueArgb([Drawing.Color]$Color) {
    [Drawing.Color]::FromArgb(255,$Color.R,$Color.G,$Color.B).ToArgb()
}

function Resolve-RootInputPath([string]$RelativePath, [string]$Name) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)) { throw "$Name must be a portrait-root-relative path" }
    $candidate = [IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    $prefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw "$Name escapes PortraitRoot: $RelativePath" }
    return $candidate
}

function Get-HalfSourceY([int]$TargetY, [object]$Region, [int]$Offset) {
    $sourceTop = [int]$Region.y
    $sourceBottom = [int]($Region.y + $Region.height - 1)
    return [Math]::Max($sourceTop,[Math]::Min($sourceBottom,($TargetY - $Offset)))
}

function Assert-ReferenceGrid([Drawing.Bitmap]$Reference, [Drawing.Bitmap]$ReferenceUpper, [Drawing.Bitmap]$HalfSource,
    [int]$Width, [int]$SplitY, [string]$GazeLayout) {
    $columns = if ($GazeLayout -eq 'horizontal-pair') { 2 } else { 3 }
    $rows = if ($GazeLayout -eq 'horizontal-pair') { 2 } else { 3 }
    if (($Reference.Width % $columns) -ne 0 -or ($Reference.Height % $rows) -ne 0) { throw "thinking-reference-grid.png must divide evenly into ${columns}x${rows} cells" }
    $cellWidth = $Reference.Width / $columns
    $cellHeight = $Reference.Height / $rows
    if (($cellWidth % $Width) -ne 0 -or ($cellHeight % $SplitY) -ne 0) { throw 'thinking-reference-grid.png cells must be integer enlargements of the native upper half' }
    $scaleX = $cellWidth / $Width
    $scaleY = $cellHeight / $SplitY
    if ($scaleX -le 0 -or $scaleX -ne $scaleY) { throw 'thinking-reference-grid.png must use one equal integer enlargement scale' }
    for ($cell = 0; $cell -lt ($columns * $rows); $cell++) {
        $originX = ($cell % $columns) * $cellWidth
        $originY = [int][Math]::Floor($cell / $columns) * $cellHeight
        $expectedSource = $ReferenceUpper
        if ($GazeLayout -eq 'horizontal-pair' -and $cell -ge 2) { $expectedSource = $HalfSource }
        for ($y = 0; $y -lt $SplitY; $y++) {
            for ($x = 0; $x -lt $Width; $x++) {
                $expected = $expectedSource.GetPixel($x,$y).ToArgb()
                for ($dy = 0; $dy -lt $scaleY; $dy++) {
                    for ($dx = 0; $dx -lt $scaleX; $dx++) {
                        if ($Reference.GetPixel($originX + $x*$scaleX + $dx,$originY + $y*$scaleY + $dy).ToArgb() -ne $expected) {
                            throw "thinking-reference-grid.png is not an exact repeated source grid (cell=$cell x=$x y=$y)"
                        }
                    }
                }
            }
        }
    }
    $result = [ordered]@{width=$Reference.Width;height=$Reference.Height;scale=$scaleX;cells=($columns*$rows);exactSourceCells=$true}
    if ($GazeLayout -eq 'horizontal-pair') { $result['columns'] = $columns; $result['rows'] = $rows }
    return $result
}

function Assert-SavedFrame([string]$Path, [Drawing.Bitmap]$Original, [Drawing.Bitmap]$Gaze, [Drawing.Bitmap]$BrowMaster,
    [Drawing.Bitmap]$HalfSource, [Drawing.Bitmap]$ClosedSource, [Drawing.Bitmap]$OriginalUpper, [object[]]$OriginalLockRegions, [object[]]$EyeRegions,
    [object[]]$HalfLidRegions, [int]$HalfOffset, [int]$BrowEnd, [string]$Phase, [int]$Width, [int]$SplitY,
    [string]$GazeLayout, [Drawing.Bitmap]$ReferenceUpper, [object[]]$ReferenceLockRegions, [object[]]$HalfOcclusionRegions, [object[]]$BrowBoundaryPixels) {
    $saved = [Drawing.Bitmap]::FromFile($Path)
    try {
        if ($saved.Width -ne $Width -or $saved.Height -ne $SplitY) { throw "Saved frame dimensions differ: $Path" }
        $outside = 0; $browLock = 0; $halfOverlay = 0; $halfOcclusionPixels = 0; $closedPixels = 0; $originalLockPixels = 0; $referenceLockPixels = 0; $browBoundaryPixelComparisons = 0; $declaredOutsideMaskChangedPixels = 0
        for ($y=0; $y -lt $SplitY; $y++) {
            for ($x=0; $x -lt $Width; $x++) {
                $mask = Test-InEyeMask $x $y $EyeRegions
                $expected = $Original.GetPixel($x,$y).ToArgb()
                if ($mask -and $GazeLayout -eq 'horizontal-pair') {
                    if ($y -le $BrowEnd) { $expected = $BrowMaster.GetPixel($x,$y).ToArgb(); $browLock++ }
                    elseif (Test-InRectList $x $y $OriginalLockRegions) { $expected = $OriginalUpper.GetPixel($x,$y).ToArgb(); $originalLockPixels++ }
                    elseif (Test-InRectList $x $y $ReferenceLockRegions) { $expected = $ReferenceUpper.GetPixel($x,$y).ToArgb(); $referenceLockPixels++ }
                    elseif ($Phase -eq 'closed') { $expected = Get-OpaqueArgb $ClosedSource.GetPixel($x,$y); $closedPixels++ }
                    elseif ($Phase -eq 'half' -and $HalfOcclusionRegions.Count -gt 0) {
                        $region = Get-HalfOcclusionRegion $x $y $HalfOcclusionRegions
                        if ($null -ne $region) {
                            $sourceX = [int]$region.source.x + ($x - [int]$region.target.x)
                            $sourceY = [int]$region.source.y + ($y - [int]$region.target.y)
                            $expected = Get-OpaqueArgb $ClosedSource.GetPixel($sourceX,$sourceY)
                            $halfOcclusionPixels++
                        } else { $expected = Get-OpaqueArgb $Gaze.GetPixel($x,$y) }
                    }
                    else { $expected = Get-OpaqueArgb $Gaze.GetPixel($x,$y) }
                    if ($saved.GetPixel($x,$y).ToArgb() -ne $expected) { throw "Saved ${Phase} frame differs from its horizontal-pair source rule at ($x,$y)" }
                } elseif ($mask) {
                    if ($y -le $BrowEnd) { $expected = $BrowMaster.GetPixel($x,$y).ToArgb(); $browLock++ }
                    elseif ($Phase -eq 'closed') { $expected = Get-OpaqueArgb $ClosedSource.GetPixel($x,$y); $closedPixels++ }
                    elseif ($Phase -eq 'half') {
                        $region = $null
                        foreach ($candidate in $HalfLidRegions) {
                            $bottom = [int]($candidate.y + $candidate.height + $HalfOffset)
                            if ($x -ge $candidate.x -and $x -lt ($candidate.x+$candidate.width) -and $y -ge $candidate.y -and $y -lt $bottom) { $region = $candidate; break }
                        }
                        if ($null -ne $region) { $sourceY = Get-HalfSourceY $y $region $HalfOffset; $expected = Get-OpaqueArgb $HalfSource.GetPixel($x,$sourceY); $halfOverlay++ }
                        else { $expected = Get-OpaqueArgb $Gaze.GetPixel($x,$y) }
                    } else { $expected = Get-OpaqueArgb $Gaze.GetPixel($x,$y) }
                    if ($saved.GetPixel($x,$y).A -ne 255) { throw "Feature mask is not opaque in ${Phase}: ($x,$y)" }
                } else {
                    $boundaryPixel = Get-BrowBoundaryPixel $x $y $BrowBoundaryPixels
                    if ($null -ne $boundaryPixel) {
                        $expected = Get-OpaqueArgb $BrowMaster.GetPixel($x,$y)
                        $browBoundaryPixelComparisons++
                        if ($expected -ne $Original.GetPixel($x,$y).ToArgb()) { $declaredOutsideMaskChangedPixels++ }
                    }
                    $actual = $saved.GetPixel($x,$y).ToArgb()
                    if ($actual -ne $expected) {
                        if ($null -ne $boundaryPixel) { throw "Saved ${Phase} frame differs from declared browBoundaryPixels at ($x,$y)" }
                        $outside++
                    } elseif ($actual -ne $Original.GetPixel($x,$y).ToArgb()) { $outside++ }
                    continue
                }
                if ($saved.GetPixel($x,$y).ToArgb() -ne $expected) { throw "Saved ${Phase} frame differs from its source rule at ($x,$y)" }
            }
        }
        if ($outside -ne $declaredOutsideMaskChangedPixels) { throw "Saved ${Phase} frame changed $outside pixels outside eye masks, but only $declaredOutsideMaskChangedPixels are declared brow-boundary writes" }
        $checks = [ordered]@{outsideMaskChangedPixels=$outside;browLockPixels=$browLock;originalLockPixels=$originalLockPixels;halfLidOverlayPixels=$halfOverlay;closedEyePixels=$closedPixels;halfLidOffset=$HalfOffset}
        if ($ReferenceLockRegions.Count -gt 0) { $checks['referenceLockPixels'] = $referenceLockPixels }
        if ($HalfOcclusionRegions.Count -gt 0) { $checks['halfOcclusionPixels'] = $halfOcclusionPixels }
        if ($BrowBoundaryPixels.Count -gt 0) { $checks['browBoundaryPixelComparisons'] = $browBoundaryPixelComparisons; $checks['declaredOutsideMaskChangedPixels'] = $declaredOutsideMaskChangedPixels }
        return $checks
    } finally { $saved.Dispose() }
}

$layoutPath = Join-Path $root 'layout.json'
$thinkingLayoutPath = Join-Path $root 'thinking-layout.json'
$provenancePath = Join-Path $root 'setup-provenance.json'
$originalPath = Join-Path $root 'original.png'
$referencePath = Join-Path $root 'thinking-reference-grid.png'
$rawPath = Join-Path $root 'eyes/thinking/raw.png'
$sidecarPath = Join-Path $root 'thinking-eye-sets.json'
$gazeOrder = @(
    [ordered]@{id='up-left';label='Up-left';row=0;column=0},
    [ordered]@{id='up';label='Up';row=0;column=1},
    [ordered]@{id='up-right';label='Up-right';row=0;column=2},
    [ordered]@{id='left';label='Left';row=1;column=0},
    [ordered]@{id='center';label='Center';row=1;column=1},
    [ordered]@{id='right';label='Right';row=1;column=2},
    [ordered]@{id='down-left';label='Down-left';row=2;column=0},
    [ordered]@{id='down';label='Down';row=2;column=1},
    [ordered]@{id='down-right';label='Down-right';row=2;column=2}
)

foreach ($required in @($layoutPath,$thinkingLayoutPath,$provenancePath,$originalPath,$referencePath,$rawPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required thinking-eye input is missing: $required" }
}

$layout = Get-Content -LiteralPath $layoutPath -Raw -Encoding UTF8 | ConvertFrom-Json
$thinkingLayout = Get-Content -LiteralPath $thinkingLayoutPath -Raw -Encoding UTF8 | ConvertFrom-Json
$setup = Get-Content -LiteralPath $provenancePath -Raw -Encoding UTF8 | ConvertFrom-Json
$emotionId = $null
$emotionProperty = $thinkingLayout.PSObject.Properties['emotionId']
$hasBrowBoundaryPixels = $null -ne $thinkingLayout.PSObject.Properties['browBoundaryPixels']
if ($null -ne $emotionProperty) {
    if ($emotionProperty.Value -isnot [string] -or [string]::IsNullOrWhiteSpace($emotionProperty.Value)) { throw 'thinking-layout.emotionId must be one of neutral, happy, angry, sad, afraid, guarded' }
    $emotionId = [string]$emotionProperty.Value
    if (@('neutral','happy','angry','sad','afraid','guarded') -notcontains $emotionId) { throw "Unsupported thinking-layout.emotionId: $emotionId" }
}
$gazeLayout = 'nine-direction'
$gazeLayoutProperty = $thinkingLayout.PSObject.Properties['gazeLayout']
if ($null -ne $gazeLayoutProperty) {
    if ($gazeLayoutProperty.Value -isnot [string] -or [string]::IsNullOrWhiteSpace($gazeLayoutProperty.Value)) { throw 'thinking-layout.gazeLayout must be horizontal-pair when specified' }
    $gazeLayout = [string]$gazeLayoutProperty.Value
    if ($gazeLayout -ne 'horizontal-pair') { throw "Unsupported thinking-layout.gazeLayout: $gazeLayout" }
    if ($null -eq $emotionId) { throw 'horizontal-pair requires an explicit emotionId and emotion-bound sources' }
    $gazeOrder = @(
        [ordered]@{id='left';label='Left';row=0;column=0;openCell=0;halfCell=2},
        [ordered]@{id='right';label='Right';row=0;column=1;openCell=1;halfCell=3}
    )
}
function Get-ConfigRelativePath([string]$PropertyName, [string]$DefaultValue, [bool]$Optional) {
    $property = $thinkingLayout.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        if ($Optional) { return $null }
        return $DefaultValue
    }
    if ($property.Value -isnot [string] -or [string]::IsNullOrWhiteSpace($property.Value)) { throw "thinking-layout.$PropertyName must be a non-empty portrait-root-relative path" }
    return [string]$property.Value
}
$referenceUpperRelative = Get-ConfigRelativePath 'referenceUpperFile' 'source-upper.png' $false
$browSourceRelative = Get-ConfigRelativePath 'browSourceFile' $null $true
$halfSourceRelative = Get-ConfigRelativePath 'halfSourceFile' 'eyes/neutral/half.png' $false
$closedSourceRelative = Get-ConfigRelativePath 'closedSourceFile' 'eyes/neutral/closed.png' $false
$referenceUpperIsOriginal = $false
if ($null -ne $emotionId) {
    foreach ($propertyName in @('referenceUpperFile','browSourceFile','halfSourceFile','closedSourceFile')) {
        if ($null -eq $thinkingLayout.PSObject.Properties[$propertyName]) { throw "Emotion thinking-layout must explicitly declare $propertyName" }
    }
    $expectedSources = [ordered]@{
        referenceUpperFile="eyes/$emotionId/open.png"
        halfSourceFile="eyes/$emotionId/half.png"
        closedSourceFile="eyes/$emotionId/closed.png"
    }
    foreach ($propertyName in $expectedSources.Keys) {
        if (([string]$thinkingLayout.$propertyName).Replace('\\','/') -ne $expectedSources[$propertyName]) { throw "Emotion thinking-layout.$propertyName must be $($expectedSources[$propertyName])" }
    }
    if ($hasBrowBoundaryPixels) {
        $expectedBrowPrefix = "eyes/$emotionId/"
        if ($null -eq $browSourceRelative -or -not ([string]$browSourceRelative).Replace('\\','/').StartsWith($expectedBrowPrefix,[StringComparison]::Ordinal)) {
            throw "thinking-layout.browSourceFile with browBoundaryPixels must select an explicit source under $expectedBrowPrefix"
        }
    } elseif ($browSourceRelative -ne "eyes/$emotionId/open.png") {
        throw "Emotion thinking-layout.browSourceFile must be eyes/$emotionId/open.png"
    }
}
if ($null -eq $emotionId -and $null -eq $thinkingLayout.PSObject.Properties['referenceUpperFile'] -and -not (Test-Path -LiteralPath (Join-Path $root $referenceUpperRelative) -PathType Leaf)) {
    $referenceUpperRelative = 'original.png'
    $referenceUpperIsOriginal = $true
}
$referenceUpperPath = if ($referenceUpperIsOriginal) { $originalPath } else { Resolve-RootInputPath $referenceUpperRelative 'thinking-layout.referenceUpperFile' }
$browSourcePath = if ($null -ne $browSourceRelative) { Resolve-RootInputPath $browSourceRelative 'thinking-layout.browSourceFile' } else { $null }
$halfPath = Resolve-RootInputPath $halfSourceRelative 'thinking-layout.halfSourceFile'
$closedPath = Resolve-RootInputPath $closedSourceRelative 'thinking-layout.closedSourceFile'
$selectedSourcePaths = [Collections.Generic.List[string]]::new()
foreach ($path in @($referenceUpperPath,$halfPath,$closedPath)) { if (-not $selectedSourcePaths.Contains($path)) { [void]$selectedSourcePaths.Add($path) } }
if ($null -ne $browSourcePath -and -not $selectedSourcePaths.Contains($browSourcePath)) { [void]$selectedSourcePaths.Add($browSourcePath) }
foreach ($required in $selectedSourcePaths) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Selected thinking-eye source is missing: $required" }
}

if ($layout.width -ne 64 -or $layout.height -ne 64 -or $layout.splitY -ne 35) { throw 'layout.json must declare native 64x64 and splitY=35' }
if ($null -eq $layout.portraitRecord -or [string]::IsNullOrWhiteSpace([string]$layout.bank)) { throw 'layout.json must declare portraitRecord and bank' }
if ($null -eq $layout.browLockEndY) { throw 'layout.json must declare browLockEndY' }
Assert-Integer $layout.browLockEndY 'layout.browLockEndY'
$width = [int]$layout.width; $height = [int]$layout.height; $splitY = [int]$layout.splitY
$browEnd = [int]$layout.browLockEndY
if ($browEnd -lt 0 -or $browEnd -ge $splitY) { throw 'layout.browLockEndY must be from 0 through splitY-1' }
if ($null -eq $layout.eyeRegions -or @($layout.eyeRegions).Count -ne 2) { throw 'layout.json must declare exactly two eyeRegions' }
$eyeRegions = @($layout.eyeRegions)
for ($i=0; $i -lt 2; $i++) { Assert-Rect $eyeRegions[$i] "layout.eyeRegions[$i]" $width $splitY }
$overlapLeft = [Math]::Max($eyeRegions[0].x,$eyeRegions[1].x)
$overlapTop = [Math]::Max($eyeRegions[0].y,$eyeRegions[1].y)
$overlapRight = [Math]::Min(($eyeRegions[0].x+$eyeRegions[0].width),($eyeRegions[1].x+$eyeRegions[1].width))
$overlapBottom = [Math]::Min(($eyeRegions[0].y+$eyeRegions[0].height),($eyeRegions[1].y+$eyeRegions[1].height))
if ($overlapLeft -lt $overlapRight -and $overlapTop -lt $overlapBottom) { throw 'layout eyeRegions must be separate, non-overlapping masks' }
$originalLockRegions = @()
$originalLockProperty = $thinkingLayout.PSObject.Properties['originalLockRegions']
if ($null -ne $originalLockProperty) {
    if ($null -eq $originalLockProperty.Value -or $originalLockProperty.Value -is [string] -or $originalLockProperty.Value -isnot [array]) { throw 'thinking-layout.originalLockRegions must be an array of rectangles' }
    $originalLockRegions = @($originalLockProperty.Value)
    if ($originalLockRegions.Count -gt 0 -and $gazeLayout -ne 'horizontal-pair') { throw 'thinking-layout.originalLockRegions is supported only for horizontal-pair' }
    for ($i=0; $i -lt $originalLockRegions.Count; $i++) {
        $region = $originalLockRegions[$i]
        Assert-Rect $region "thinking-layout.originalLockRegions[$i]" $width $splitY
        if ($region.y -le $browEnd) { throw "thinking-layout.originalLockRegions[$i] must be below browLockEndY" }
        $containingEyeIndices = @()
        for ($eyeIndex=0; $eyeIndex -lt 2; $eyeIndex++) { if (Test-RectContains $eyeRegions[$eyeIndex] $region) { $containingEyeIndices += $eyeIndex } }
        if ($containingEyeIndices.Count -ne 1) { throw "thinking-layout.originalLockRegions[$i] must fit wholly inside exactly one eye mask" }
        for ($j=0; $j -lt $i; $j++) {
            $other = $originalLockRegions[$j]
            $left = [Math]::Max($region.x,$other.x); $top = [Math]::Max($region.y,$other.y)
            $right = [Math]::Min(($region.x+$region.width),($other.x+$other.width))
            $bottom = [Math]::Min(($region.y+$region.height),($other.y+$other.height))
            if ($left -lt $right -and $top -lt $bottom) { throw "thinking-layout.originalLockRegions[$i] overlaps another original-lock region" }
        }
    }
}
$referenceLockRegions = @()
$referenceLockProperty = $thinkingLayout.PSObject.Properties['referenceLockRegions']
if ($null -ne $referenceLockProperty) {
    if ($null -eq $referenceLockProperty.Value -or $referenceLockProperty.Value -isnot [array]) { throw 'thinking-layout.referenceLockRegions must be an array of rectangles' }
    $referenceLockRegions = @($referenceLockProperty.Value)
    if ($referenceLockRegions.Count -gt 0 -and $gazeLayout -ne 'horizontal-pair') { throw 'thinking-layout.referenceLockRegions is supported only for horizontal-pair' }
    for ($i=0; $i -lt $referenceLockRegions.Count; $i++) {
        $region = $referenceLockRegions[$i]
        if ($null -eq $region -or @($region.PSObject.Properties.Name).Count -ne 4 -or @($region.PSObject.Properties.Name | Where-Object { $_ -notin @('x','y','width','height') }).Count -gt 0) { throw "thinking-layout.referenceLockRegions[$i] must contain exactly x, y, width and height" }
        Assert-Rect $region "thinking-layout.referenceLockRegions[$i]" $width $splitY
        if ($region.y -le $browEnd) { throw "thinking-layout.referenceLockRegions[$i] must be below browLockEndY" }
        $containers = @($eyeRegions | Where-Object { Test-RectContains $_ $region })
        if ($containers.Count -ne 1) { throw "thinking-layout.referenceLockRegions[$i] must fit wholly inside exactly one eye mask" }
        $previousLocks = @($originalLockRegions)
        for ($j=0; $j -lt $i; $j++) { $previousLocks += $referenceLockRegions[$j] }
        foreach ($other in $previousLocks) {
            if ($region.x -lt ($other.x+$other.width) -and $other.x -lt ($region.x+$region.width) -and
                $region.y -lt ($other.y+$other.height) -and $other.y -lt ($region.y+$region.height)) {
                throw "thinking-layout.referenceLockRegions[$i] overlaps another original or reference lock region"
            }
        }
    }
}
$halfOcclusionRegionsProperty = $thinkingLayout.PSObject.Properties['halfOcclusionRegions']
$halfOcclusionHashProperty = $thinkingLayout.PSObject.Properties['halfOcclusionSourceSha256']
$halfOcclusionRegions = @()
$halfOcclusionSourceSha256 = $null
if ($null -ne $halfOcclusionRegionsProperty) {
    if ($gazeLayout -ne 'horizontal-pair') { throw 'thinking-layout.halfOcclusionRegions is supported only for horizontal-pair' }
    if ($null -eq $halfOcclusionRegionsProperty.Value -or $halfOcclusionRegionsProperty.Value -isnot [array]) { throw 'thinking-layout.halfOcclusionRegions must be an array of target/source rectangles' }
    $halfOcclusionRegions = @($halfOcclusionRegionsProperty.Value)
    if ($halfOcclusionRegions.Count -lt 1) { throw 'thinking-layout.halfOcclusionRegions must not be empty when declared' }
    if ($null -eq $halfOcclusionHashProperty -or $halfOcclusionHashProperty.Value -isnot [string] -or -not (Test-HexValue $halfOcclusionHashProperty.Value 64)) {
        throw 'thinking-layout.halfOcclusionSourceSha256 must pin the CLOSED source hash when halfOcclusionRegions is declared'
    }
    $halfOcclusionSourceSha256 = ([string]$halfOcclusionHashProperty.Value).ToLowerInvariant()
    $coveredOcclusionEyes = @($false,$false)
    for ($i=0; $i -lt $halfOcclusionRegions.Count; $i++) {
        $region = $halfOcclusionRegions[$i]
        if ($null -eq $region -or @($region.PSObject.Properties.Name).Count -ne 2 -or @($region.PSObject.Properties.Name | Where-Object { $_ -notin @('target','source') }).Count -gt 0) {
            throw "thinking-layout.halfOcclusionRegions[$i] must contain exactly target and source rectangles"
        }
        foreach ($part in @('target','source')) {
            $rect = $region.$part
            if ($null -eq $rect -or @($rect.PSObject.Properties.Name).Count -ne 4 -or @($rect.PSObject.Properties.Name | Where-Object { $_ -notin @('x','y','width','height') }).Count -gt 0) {
                throw "thinking-layout.halfOcclusionRegions[$i].$part must contain exactly x, y, width and height"
            }
            Assert-Rect $rect "thinking-layout.halfOcclusionRegions[$i].$part" $width $splitY
        }
        if ($region.target.width -ne $region.source.width -or $region.target.height -ne $region.source.height) {
            throw "thinking-layout.halfOcclusionRegions[$i] target/source dimensions must match for a one-to-one copy"
        }
        if ($region.target.y -le $browEnd) { throw "thinking-layout.halfOcclusionRegions[$i].target must be below browLockEndY" }
        if ($region.source.y -le $browEnd) { throw "thinking-layout.halfOcclusionRegions[$i].source must be below browLockEndY" }
        $targetEyeIndices = @(); $sourceEyeIndices = @()
        for ($eyeIndex=0; $eyeIndex -lt 2; $eyeIndex++) {
            if (Test-RectContains $eyeRegions[$eyeIndex] $region.target) { $targetEyeIndices += $eyeIndex; $coveredOcclusionEyes[$eyeIndex] = $true }
            if (Test-RectContains $eyeRegions[$eyeIndex] $region.source) { $sourceEyeIndices += $eyeIndex }
        }
        if ($targetEyeIndices.Count -ne 1) { throw "thinking-layout.halfOcclusionRegions[$i].target must fit wholly inside exactly one eye mask" }
        if ($sourceEyeIndices.Count -ne 1 -or $sourceEyeIndices[0] -ne $targetEyeIndices[0]) { throw "thinking-layout.halfOcclusionRegions[$i].source must fit in the same eye mask as its target" }
        foreach ($locked in @($originalLockRegions) + @($referenceLockRegions)) {
            if ($region.target.x -lt ($locked.x+$locked.width) -and $locked.x -lt ($region.target.x+$region.target.width) -and
                $region.target.y -lt ($locked.y+$locked.height) -and $locked.y -lt ($region.target.y+$region.target.height)) {
                throw "thinking-layout.halfOcclusionRegions[$i].target overlaps an original or reference lock region"
            }
        }
        for ($j=0; $j -lt $i; $j++) {
            $other = $halfOcclusionRegions[$j].target
            if ($region.target.x -lt ($other.x+$other.width) -and $other.x -lt ($region.target.x+$region.target.width) -and
                $region.target.y -lt ($other.y+$other.height) -and $other.y -lt ($region.target.y+$region.target.height)) {
                throw "thinking-layout.halfOcclusionRegions[$i].target overlaps another half-occlusion target"
            }
        }
    }
    if (-not $coveredOcclusionEyes[0] -or -not $coveredOcclusionEyes[1]) { throw 'thinking-layout.halfOcclusionRegions must include both eye masks' }
} elseif ($null -ne $halfOcclusionHashProperty) {
    throw 'thinking-layout.halfOcclusionSourceSha256 requires halfOcclusionRegions'
}
$browBoundaryPixelsProperty = $thinkingLayout.PSObject.Properties['browBoundaryPixels']
$browBoundaryHashProperty = $thinkingLayout.PSObject.Properties['browBoundarySourceSha256']
$browBoundaryPixels = @()
$browBoundarySourceSha256 = $null
$browBoundaryChangedPixelCount = 0
if ($null -ne $browBoundaryPixelsProperty) {
    if ($gazeLayout -ne 'horizontal-pair') { throw 'thinking-layout.browBoundaryPixels is supported only for horizontal-pair' }
    if ($null -eq $browBoundaryPixelsProperty.Value -or $browBoundaryPixelsProperty.Value -isnot [array] -or $browBoundaryPixelsProperty.Value.Count -lt 1) {
        throw 'thinking-layout.browBoundaryPixels must be a non-empty array'
    }
    if ($null -eq $browBoundaryHashProperty -or $browBoundaryHashProperty.Value -isnot [string] -or -not (Test-HexValue $browBoundaryHashProperty.Value 64)) {
        throw 'thinking-layout.browBoundarySourceSha256 must pin the explicit browSourceFile SHA-256'
    }
    if ($null -eq $browSourceRelative) { throw 'thinking-layout.browBoundaryPixels requires an explicit browSourceFile' }
    $browBoundarySourceSha256 = ([string]$browBoundaryHashProperty.Value).ToLowerInvariant()
    $seenBrowBoundaryPixels = @{}
    for ($i=0; $i -lt $browBoundaryPixelsProperty.Value.Count; $i++) {
        $pixel = $browBoundaryPixelsProperty.Value[$i]
        $name = "thinking-layout.browBoundaryPixels[$i]"
        if ($null -eq $pixel -or @($pixel.PSObject.Properties.Name).Count -ne 4 -or @($pixel.PSObject.Properties.Name | Where-Object { $_ -notin @('x','y','expectedOriginalRgb','expectedBrowRgb') }).Count -gt 0) {
            throw "$name must contain exactly x, y, expectedOriginalRgb and expectedBrowRgb"
        }
        Assert-Integer $pixel.x "$name.x"; Assert-Integer $pixel.y "$name.y"
        $x = [int]$pixel.x; $y = [int]$pixel.y
        if ($x -lt 0 -or $x -ge $width -or $y -lt 0 -or $y -ge $splitY) { throw "$name coordinate must be inside the native 64x35 upper crop" }
        if (Test-InEyeMask $x $y $eyeRegions) { throw "$name must be outside all eyeRegions" }
        if ($y -gt $browEnd) { throw "$name.y must be at or above browLockEndY and cannot point into the lid" }
        $adjacentEyes = @()
        for ($eyeIndex=0; $eyeIndex -lt $eyeRegions.Count; $eyeIndex++) {
            $eye = $eyeRegions[$eyeIndex]
            if ($y -ge $eye.y -and $y -lt ($eye.y+$eye.height) -and ($x -eq ($eye.x-1) -or $x -eq ($eye.x+$eye.width))) { $adjacentEyes += $eyeIndex }
        }
        if ($adjacentEyes.Count -ne 1) { throw "$name must be immediately laterally adjacent to exactly one eye mask" }
        $coordinateKey = "$x,$y"
        if ($seenBrowBoundaryPixels.ContainsKey($coordinateKey)) { throw "$name duplicates browBoundaryPixels coordinate ($x,$y)" }
        $seenBrowBoundaryPixels[$coordinateKey] = $true
        $normalizedRgb = @{}
        foreach ($field in @('expectedOriginalRgb','expectedBrowRgb')) {
            $rgb = $pixel.$field
            if ($null -eq $rgb -or $rgb -isnot [array] -or $rgb.Count -ne 3) { throw "$name.$field must be an RGB array of exactly three bytes" }
            $channels = @()
            for ($channel=0; $channel -lt 3; $channel++) {
                Assert-Integer $rgb[$channel] "$name.$field[$channel]"
                if ([int]$rgb[$channel] -lt 0 -or [int]$rgb[$channel] -gt 255) { throw "$name.$field channels must be from 0 through 255" }
                $channels += [int]$rgb[$channel]
            }
            $normalizedRgb[$field] = $channels
        }
        if (($normalizedRgb.expectedOriginalRgb -join ',') -eq ($normalizedRgb.expectedBrowRgb -join ',')) { throw "$name must declare a non-zero outside-mask brow write" }
        $browBoundaryChangedPixelCount++
        $browBoundaryPixels += [ordered]@{x=$x;y=$y;expectedOriginalRgb=$normalizedRgb.expectedOriginalRgb;expectedBrowRgb=$normalizedRgb.expectedBrowRgb}
    }
} elseif ($null -ne $browBoundaryHashProperty) {
    throw 'thinking-layout.browBoundarySourceSha256 requires browBoundaryPixels'
}
if ($setup.sourceSha256 -isnot [string] -or $setup.sourceSha256.Length -ne 64) { throw 'setup-provenance.json must declare sourceSha256' }
if ($setup.sourceWidth -ne $width -or $setup.sourceHeight -ne $height -or $setup.splitY -ne $splitY) { throw 'setup-provenance dimensions do not match the portrait layout' }
if ($setup.portraitRecord -ne $layout.portraitRecord) { throw 'setup-provenance portraitRecord differs from layout.json' }

$halfLidProperty = $thinkingLayout.PSObject.Properties | Where-Object { $_.Name -ieq 'halfLidRegions' } | Select-Object -First 1
$halfLidRegions = @()
if ($gazeLayout -eq 'horizontal-pair') {
    if ($null -ne $thinkingLayout.PSObject.Properties['halfLidOffsetsByGaze']) { throw 'horizontal-pair does not allow halfLidOffsetsByGaze' }
} else {
    if ($null -eq $halfLidProperty -or $null -eq $halfLidProperty.Value) { throw 'thinking-layout.json must declare halfLidRegions' }
    $halfLidRegions = @($halfLidProperty.Value)
    if ($halfLidRegions.Count -lt 1) { throw 'thinking-layout.json halfLidRegions must not be empty' }
}
$coveredEyeRegions = @($false,$false)
for ($i=0; $i -lt $halfLidRegions.Count; $i++) {
    $region = $halfLidRegions[$i]
    Assert-Rect $region "thinking-layout.halfLidRegions[$i]" $width $splitY
    if ($region.y -le $browEnd) { throw "thinking-layout.halfLidRegions[$i] must be below browLockEndY" }
    $containingEyeIndices = @()
    for ($eyeIndex=0; $eyeIndex -lt 2; $eyeIndex++) {
        if (Test-RectContains $eyeRegions[$eyeIndex] $region) { $containingEyeIndices += $eyeIndex }
    }
    if ($containingEyeIndices.Count -ne 1) { throw "thinking-layout.halfLidRegions[$i] must fit wholly inside exactly one eye mask" }
    $coveredEyeRegions[$containingEyeIndices[0]] = $true
    for ($j=0; $j -lt $i; $j++) {
        $other = $halfLidRegions[$j]
        $left = [Math]::Max($region.x,$other.x); $top = [Math]::Max($region.y,$other.y)
        $right = [Math]::Min(($region.x+$region.width),($other.x+$other.width))
        $bottom = [Math]::Min(($region.y+$region.height),($other.y+$other.height))
        if ($left -lt $right -and $top -lt $bottom) { throw "thinking-layout.halfLidRegions[$i] overlaps another approved half-lid region" }
    }
}
if ($gazeLayout -ne 'horizontal-pair' -and (-not $coveredEyeRegions[0] -or -not $coveredEyeRegions[1])) { throw 'thinking-layout.halfLidRegions must include a region inside both eye masks' }

$halfOffsets = [ordered]@{}
if ($gazeLayout -ne 'horizontal-pair') { foreach ($gaze in $gazeOrder) { $halfOffsets[$gaze.id] = 0 } }
$offsetProperty = $thinkingLayout.PSObject.Properties['halfLidOffsetsByGaze']
if ($null -ne $offsetProperty -and $gazeLayout -ne 'horizontal-pair') {
    if ($null -eq $offsetProperty.Value -or $offsetProperty.Value -is [Array] -or $offsetProperty.Value -is [string]) { throw 'thinking-layout.halfLidOffsetsByGaze must be an object keyed by known gaze IDs' }
    foreach ($property in $offsetProperty.Value.PSObject.Properties) {
        if (@($gazeOrder | ForEach-Object id) -notcontains $property.Name) { throw "Unknown halfLidOffsetsByGaze gaze ID: $($property.Name)" }
        Assert-Integer $property.Value "thinking-layout.halfLidOffsetsByGaze.$($property.Name)"
        if ([int]$property.Value -lt -3 -or [int]$property.Value -gt 3) { throw "thinking-layout.halfLidOffsetsByGaze.$($property.Name) must be from -3 through 3" }
        $halfOffsets[$property.Name] = [int]$property.Value
    }
}
foreach ($gaze in $gazeOrder) {
    $offset = [int]$halfOffsets[$gaze.id]
    $adjusted = @()
    for ($i=0; $i -lt $halfLidRegions.Count; $i++) {
        $region = $halfLidRegions[$i]
        $targetHeight = [int]($region.height + $offset)
        if ($targetHeight -le 0) { throw "halfLidOffsetsByGaze.$($gaze.id) removes half-lid region $i" }
        $target = [pscustomobject]@{x=[int]$region.x;y=[int]$region.y;width=[int]$region.width;height=$targetHeight}
        if ($target.y -le $browEnd) { throw "halfLidOffsetsByGaze.$($gaze.id) crosses browLockEndY" }
        $containingEyeIndices = @()
        for ($eyeIndex=0; $eyeIndex -lt 2; $eyeIndex++) { if (Test-RectContains $eyeRegions[$eyeIndex] $target) { $containingEyeIndices += $eyeIndex } }
        if ($containingEyeIndices.Count -ne 1) { throw "halfLidOffsetsByGaze.$($gaze.id) crosses an eye mask boundary" }
        foreach ($other in $adjusted) {
            $left=[Math]::Max($target.x,$other.x);$top=[Math]::Max($target.y,$other.y)
            $right=[Math]::Min(($target.x+$target.width),($other.x+$other.width));$bottom=[Math]::Min(($target.y+$target.height),($other.y+$other.height))
            if ($left -lt $right -and $top -lt $bottom) { throw "halfLidOffsetsByGaze.$($gaze.id) makes half-lid regions overlap" }
        }
        $adjusted += $target
    }
}

$expectedSourceHash = ([string]$setup.sourceSha256).ToLowerInvariant()
$sourceHashesBefore = [ordered]@{}
$originalUpperPath = $null
if ($originalLockRegions.Count -gt 0) {
    $originalUpperPath = Resolve-RootInputPath 'source-upper.png' 'thinking-layout.originalLockRegions source'
    if (-not $selectedSourcePaths.Contains($originalUpperPath)) { [void]$selectedSourcePaths.Add($originalUpperPath) }
}
foreach ($path in @($layoutPath,$thinkingLayoutPath,$provenancePath,$originalPath,$referencePath,$rawPath)+@($selectedSourcePaths)) {
    $sourceHashesBefore[$path] = Get-Sha256 $path
}
$originalHash = Get-Sha256 $originalPath
if ($originalHash -ne $expectedSourceHash) { throw "Original source hash mismatch: expected $expectedSourceHash got $originalHash" }
if ($thinkingLayout.sourceSha256 -and ([string]$thinkingLayout.sourceSha256).ToLowerInvariant() -ne $expectedSourceHash) { throw 'thinking-layout source hash does not match the original source hash' }

$original = [Drawing.Bitmap]::FromFile($originalPath)
$reference = [Drawing.Bitmap]::FromFile($referencePath)
$raw = [Drawing.Bitmap]::FromFile($rawPath)
$referenceUpper = if ($referenceUpperPath -eq $originalPath) { New-NativeCell $original ([ordered]@{left=0;top=0;width=64;height=35}) 64 35 } else { [Drawing.Bitmap]::FromFile($referenceUpperPath) }
$halfSource = [Drawing.Bitmap]::FromFile($halfPath)
$closedSource = [Drawing.Bitmap]::FromFile($closedPath)
$originalUpper = if ($null -ne $originalUpperPath) { [Drawing.Bitmap]::FromFile($originalUpperPath) } else { $null }
$browSource = if ($null -ne $browSourcePath) { [Drawing.Bitmap]::FromFile($browSourcePath) } else { $null }
$gazeCandidates = [Collections.Generic.List[Drawing.Bitmap]]::new()
$stagingRoot = Join-Path $root ('.thinking-build-' + [Guid]::NewGuid().ToString('N'))
$published = [Collections.Generic.List[string]]::new()
$stagedOutputs = [Collections.Generic.List[object]]::new()

try {
    if ($original.Width -ne $width -or $original.Height -ne $height) { throw 'original.png dimensions do not match layout.json' }
    if ($referenceUpper.Width -ne $width -or $referenceUpper.Height -ne $splitY) { throw 'Selected reference upper source must be native 64x35' }
    if ($halfSource.Width -ne $width -or $halfSource.Height -ne $splitY) { throw 'Selected half source must be native 64x35' }
    if ($closedSource.Width -ne $width -or $closedSource.Height -ne $splitY) { throw 'Selected closed source must be native 64x35' }
    foreach ($region in $halfOcclusionRegions) {
        for ($y=$region.source.y; $y -lt ($region.source.y+$region.source.height); $y++) {
            for ($x=$region.source.x; $x -lt ($region.source.x+$region.source.width); $x++) {
                if ($closedSource.GetPixel($x,$y).A -ne 255) { throw "halfOcclusionRegions requires opaque CLOSED source pixels at ($x,$y)" }
            }
        }
    }
    if ($null -ne $originalUpper -and ($originalUpper.Width -ne $width -or $originalUpper.Height -ne $splitY)) { throw 'source-upper.png must be native 64x35 for originalLockRegions' }
    if ($null -ne $originalUpper) {
        foreach ($region in $originalLockRegions) { for ($y=$region.y; $y -lt ($region.y+$region.height); $y++) { for ($x=$region.x; $x -lt ($region.x+$region.width); $x++) {
            if ($originalUpper.GetPixel($x,$y).ToArgb() -ne $original.GetPixel($x,$y).ToArgb()) { throw "source-upper.png differs from original.png at locked pixel ($x,$y)" }
        } } }
    }
    if ($null -ne $browSource -and ($browSource.Width -ne $width -or $browSource.Height -ne $splitY)) { throw 'Selected brow source must be native 64x35' }
    foreach ($region in $referenceLockRegions) { for ($y=$region.y; $y -lt ($region.y+$region.height); $y++) { for ($x=$region.x; $x -lt ($region.x+$region.width); $x++) {
        if ($referenceUpper.GetPixel($x,$y).A -ne 255) { throw "referenceLockRegions requires opaque referenceUpperFile pixels at ($x,$y)" }
    } } }
    for ($y=0; $y -lt $splitY; $y++) { for($x=0; $x -lt $width; $x++) { if (-not (Test-InEyeMask $x $y $eyeRegions) -and $referenceUpper.GetPixel($x,$y).ToArgb() -ne $original.GetPixel($x,$y).ToArgb()) { throw "Selected reference upper source changes exterior pixel ($x,$y)" } } }
    $sourceCheck = Assert-ReferenceGrid $reference $referenceUpper $halfSource $width $splitY $gazeLayout
    $rawAspect = $raw.Width / [double]$raw.Height
    $expectedAspect = $width / [double]$splitY
    if ([Math]::Abs(($rawAspect / $expectedAspect) - 1.0) -gt 0.01) { throw "Thinking gaze atlas aspect ratio differs by more than 1%: $($raw.Width)x$($raw.Height)" }
    $atlasColumns = if ($gazeLayout -eq 'horizontal-pair') { 2 } else { 3 }
    $atlasRows = if ($gazeLayout -eq 'horizontal-pair') { 2 } else { 3 }
    if ($raw.Width -lt $atlasColumns -or $raw.Height -lt $atlasRows) { throw "Thinking gaze atlas is too small to contain ${atlasColumns}x${atlasRows} cells" }
    if ($thinkingLayout.referenceSha256 -and ([string]$thinkingLayout.referenceSha256).ToLowerInvariant() -ne (Get-Sha256 $referencePath)) { throw 'thinking-reference-grid hash differs from thinking-layout referenceSha256' }

    $referenceHash = Get-Sha256 $referencePath
    $rawHash = Get-Sha256 $rawPath
    $halfHash = Get-Sha256 $halfPath
    $closedHash = Get-Sha256 $closedPath
    if ($halfOcclusionRegions.Count -gt 0 -and $halfOcclusionSourceSha256 -ne $closedHash) { throw 'thinking-layout.halfOcclusionSourceSha256 does not match the selected CLOSED source' }
    $referenceUpperHash = Get-Sha256 $referenceUpperPath
    $browSourceHash = if ($null -ne $browSourcePath) { Get-Sha256 $browSourcePath } else { $null }
    if ($browBoundaryPixels.Count -gt 0) {
        if ($browBoundarySourceSha256 -ne $browSourceHash) { throw 'thinking-layout.browBoundarySourceSha256 does not match the explicit browSourceFile' }
        foreach ($pixel in $browBoundaryPixels) {
            $originalPixel = $original.GetPixel([int]$pixel.x,[int]$pixel.y)
            $browPixel = $browSource.GetPixel([int]$pixel.x,[int]$pixel.y)
            if ($browPixel.A -ne 255) { throw "browBoundaryPixels requires opaque browSourceFile pixels at ($($pixel.x),$($pixel.y))" }
            $actualOriginalRgb = @($originalPixel.R,$originalPixel.G,$originalPixel.B)
            $actualBrowRgb = @($browPixel.R,$browPixel.G,$browPixel.B)
            if (($actualOriginalRgb -join ',') -ne ($pixel.expectedOriginalRgb -join ',')) { throw "browBoundaryPixels expectedOriginalRgb differs from original.png at ($($pixel.x),$($pixel.y))" }
            if (($actualBrowRgb -join ',') -ne ($pixel.expectedBrowRgb -join ',')) { throw "browBoundaryPixels expectedBrowRgb differs from browSourceFile at ($($pixel.x),$($pixel.y))" }
        }
    }
    for ($i=0; $i -lt ($atlasColumns*$atlasRows); $i++) {
        $bounds = Get-CellBounds $raw $i $atlasColumns $atlasRows
        $cellAspect = $bounds.width / [double]$bounds.height
        if ([Math]::Abs(($cellAspect / $expectedAspect) - 1.0) -gt 0.01) { throw "Thinking gaze cell $i aspect ratio differs by more than 1%" }
        [void]$gazeCandidates.Add((New-NativeCell $raw $bounds $width $splitY))
    }
    $centerMaster = if ($gazeLayout -eq 'horizontal-pair') { $gazeCandidates[0] } else { $gazeCandidates[4] }
    $browMaster = if ($null -ne $browSource) { $browSource } else { $centerMaster }

    for ($gazeIndex=0; $gazeIndex -lt $gazeOrder.Count; $gazeIndex++) {
        $gaze = $gazeOrder[$gazeIndex]
        $stagedGazeRoot = Join-Path $stagingRoot ('eyes/thinking/' + $gaze.id)
        New-Item -ItemType Directory -Path $stagedGazeRoot -Force | Out-Null
        foreach ($phase in @('open','half','closed')) {
            $frame = [Drawing.Bitmap]::new($width,$splitY,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                for ($y=0; $y -lt $splitY; $y++) {
                    for ($x=0; $x -lt $width; $x++) { $frame.SetPixel($x,$y,$original.GetPixel($x,$y)) }
                }
                $halfOffset = if ($gazeLayout -eq 'horizontal-pair') { 0 } else { [int]$halfOffsets[$gaze.id] }
                $targetHalfRegions = @()
                foreach ($region in $halfLidRegions) { $targetHalfRegions += [pscustomobject]@{x=[int]$region.x;y=[int]$region.y;width=[int]$region.width;height=[int]($region.height+$halfOffset)} }
                $halfOcclusionActive = $gazeLayout -eq 'horizontal-pair' -and $phase -eq 'half' -and $halfOcclusionRegions.Count -gt 0
                $candidateIndex = if ($phase -eq 'open' -or $halfOcclusionActive) { if ($gazeLayout -eq 'horizontal-pair') { [int]$gaze.openCell } else { $gazeIndex } } elseif ($phase -eq 'half') { if ($gazeLayout -eq 'horizontal-pair') { [int]$gaze.halfCell } else { $gazeIndex } } else { -1 }
                $assertCandidateIndex = if ($candidateIndex -ge 0) { $candidateIndex } elseif ($gazeLayout -eq 'horizontal-pair') { [int]$gaze.openCell } else { $gazeIndex }
                foreach ($eye in $eyeRegions) {
                    for ($y=$eye.y; $y -lt ($eye.y+$eye.height); $y++) {
                        for ($x=$eye.x; $x -lt ($eye.x+$eye.width); $x++) {
                            if ($gazeLayout -eq 'horizontal-pair' -and $phase -eq 'closed') {
                                if (Test-InRectList $x $y $originalLockRegions) { $frame.SetPixel($x,$y,$originalUpper.GetPixel($x,$y)) }
                                elseif (Test-InRectList $x $y $referenceLockRegions) { $frame.SetPixel($x,$y,$referenceUpper.GetPixel($x,$y)) }
                                else { Copy-Color $frame $x $y $closedSource $x $y }
                            } elseif ($halfOcclusionActive) {
                                if (Test-InRectList $x $y $originalLockRegions) { $frame.SetPixel($x,$y,$originalUpper.GetPixel($x,$y)) }
                                elseif (Test-InRectList $x $y $referenceLockRegions) { $frame.SetPixel($x,$y,$referenceUpper.GetPixel($x,$y)) }
                                else {
                                    $region = Get-HalfOcclusionRegion $x $y $halfOcclusionRegions
                                    if ($null -ne $region) {
                                        $sourceX = [int]$region.source.x + ($x - [int]$region.target.x)
                                        $sourceY = [int]$region.source.y + ($y - [int]$region.target.y)
                                        Copy-Color $frame $x $y $closedSource $sourceX $sourceY
                                    } else { Copy-Color $frame $x $y $gazeCandidates[$candidateIndex] $x $y }
                                }
                            } elseif ($gazeLayout -eq 'horizontal-pair') {
                                if (Test-InRectList $x $y $originalLockRegions) { $frame.SetPixel($x,$y,$originalUpper.GetPixel($x,$y)) }
                                elseif (Test-InRectList $x $y $referenceLockRegions) { $frame.SetPixel($x,$y,$referenceUpper.GetPixel($x,$y)) }
                                else { Copy-Color $frame $x $y $gazeCandidates[$candidateIndex] $x $y }
                            } elseif ($phase -eq 'closed' -and $y -gt $browEnd) {
                                Copy-Color $frame $x $y $closedSource $x $y
                            } elseif ($phase -eq 'half' -and $y -gt $browEnd -and (Test-InRectList $x $y $targetHalfRegions)) {
                                $region = @($halfLidRegions | Where-Object { $x -ge $_.x -and $x -lt ($_.x+$_.width) -and $y -ge $_.y -and $y -lt ($_.y+$_.height+$halfOffset) })[0]
                                $sourceY = Get-HalfSourceY $y $region $halfOffset
                                Copy-Color $frame $x $y $halfSource $x $sourceY
                            } else {
                                Copy-Color $frame $x $y $gazeCandidates[$gazeIndex] $x $y
                            }
                        }
                    }
                }
                # One stationary thoughtful brow master is used by all directions and phases.
                foreach ($eye in $eyeRegions) {
                    for ($y=$eye.y; $y -lt ($eye.y+$eye.height) -and $y -le $browEnd; $y++) {
                        for ($x=$eye.x; $x -lt ($eye.x+$eye.width); $x++) {
                            Copy-Color $frame $x $y $browMaster $x $y
                        }
                    }
                }
                foreach ($pixel in $browBoundaryPixels) { Copy-Color $frame ([int]$pixel.x) ([int]$pixel.y) $browMaster ([int]$pixel.x) ([int]$pixel.y) }

                $stagedPath = Join-Path $stagedGazeRoot ($phase + '.png')
                $frame.Save($stagedPath,[Drawing.Imaging.ImageFormat]::Png)
                $assertHalfOcclusionRegions = if ($halfOcclusionActive) { $halfOcclusionRegions } else { @() }
                $assertion = Assert-SavedFrame $stagedPath $original $gazeCandidates[$assertCandidateIndex] $browMaster $halfSource $closedSource $originalUpper $originalLockRegions $eyeRegions $halfLidRegions $halfOffset $browEnd $phase $width $splitY $gazeLayout $referenceUpper $referenceLockRegions $assertHalfOcclusionRegions $browBoundaryPixels
                $relative = 'eyes/thinking/' + $gaze.id + '/' + $phase + '.png'
                $browFile = if ($null -ne $browSourcePath) { Get-RootRelativePath $browSourcePath } else { 'eyes/thinking/raw.png' }
                if ($gazeLayout -eq 'horizontal-pair') {
                    $sourceFiles = @(
                        if ($phase -eq 'closed') { Get-RootRelativePath $closedPath }
                        else { 'eyes/thinking/raw.png' }
                    )
                    if ($halfOcclusionActive) { $sourceFiles += Get-RootRelativePath $closedPath }
                } else {
                    $sourceFiles = @(if ($phase -eq 'open') { @('eyes/thinking/raw.png') }
                        elseif ($phase -eq 'half') { @('eyes/thinking/raw.png',(Get-RootRelativePath $halfPath)) }
                        else { @('eyes/thinking/raw.png',(Get-RootRelativePath $closedPath)) })
                }
                if ($sourceFiles -notcontains $browFile) { $sourceFiles += $browFile }
                $sourceShaList = @(
                    [ordered]@{file='original.png';sha256=$originalHash}
                )
                if ($gazeLayout -ne 'horizontal-pair' -or $phase -ne 'closed') { $sourceShaList += [ordered]@{file='eyes/thinking/raw.png';sha256=$rawHash} }
                if ($null -ne $browSourcePath) { $sourceShaList += [ordered]@{file=$browFile;sha256=$browSourceHash} }
                if ($phase -eq 'half' -and $gazeLayout -ne 'horizontal-pair') { $sourceShaList += [ordered]@{file=(Get-RootRelativePath $halfPath);sha256=$halfHash} }
                if ($phase -eq 'closed') { $sourceShaList += [ordered]@{file=(Get-RootRelativePath $closedPath);sha256=$closedHash} }
                if ($halfOcclusionActive -and @($sourceShaList | Where-Object { $_.file -eq (Get-RootRelativePath $closedPath) }).Count -eq 0) {
                    $sourceShaList += [ordered]@{file=(Get-RootRelativePath $closedPath);sha256=$closedHash}
                }
                if ($referenceLockRegions.Count -gt 0) {
                    $referenceFile = Get-RootRelativePath $referenceUpperPath
                    if ($sourceFiles -notcontains $referenceFile) { $sourceFiles += $referenceFile }
                    if (@($sourceShaList | Where-Object { $_.file -eq $referenceFile }).Count -eq 0) { $sourceShaList += [ordered]@{file=$referenceFile;sha256=$referenceUpperHash} }
                }
                [void]$stagedOutputs.Add([ordered]@{
                    gaze=$gaze;phase=$phase;stagedPath=$stagedPath;relative=$relative
                    hash=(Get-Sha256 $stagedPath);sourceFiles=$sourceFiles;sourceHashes=$sourceShaList
                    geometry=[ordered]@{width=$width;height=$splitY;splitY=$splitY;cellRow=if($phase -eq 'closed'){$null}else{[int][Math]::Floor($candidateIndex/$atlasColumns)};cellColumn=if($phase -eq 'closed'){$null}else{($candidateIndex%$atlasColumns)};cellIndex=$candidateIndex;eyeRegions=$eyeRegions;originalLockRegions=$originalLockRegions;halfLidRegions=if($phase -eq 'half' -and $gazeLayout -ne 'horizontal-pair'){$targetHalfRegions}else{@()};halfLidOffset=$halfOffset;browLockEndY=$browEnd;nearestNeighbor=$true}
                    checks=$assertion
                })
                if ($halfOcclusionActive) {
                    $closedRelativePath = Get-RootRelativePath $closedPath
                    $halfGeometry = $stagedOutputs[$stagedOutputs.Count-1].geometry
                    $halfGeometry['halfOcclusionRegions'] = $halfOcclusionRegions
                    $halfGeometry['halfOcclusionSource'] = [ordered]@{file=$closedRelativePath;sha256=$closedHash}
                }
                if ($referenceLockRegions.Count -gt 0) { $stagedOutputs[$stagedOutputs.Count-1].geometry['referenceLockRegions'] = $referenceLockRegions }
                if ($browBoundaryPixels.Count -gt 0) {
                    $boundaryGeometry = $stagedOutputs[$stagedOutputs.Count-1].geometry
                    $boundaryGeometry['browBoundaryPixels'] = $browBoundaryPixels
                    $boundaryGeometry['browBoundarySource'] = [ordered]@{file=$browFile;sha256=$browSourceHash}
                }
            } finally { $frame.Dispose() }
        }
    }

    foreach ($path in $sourceHashesBefore.Keys) {
        if ((Get-Sha256 $path) -ne $sourceHashesBefore[$path]) { throw "Source input changed during thinking-eye build: $path" }
    }
    $expectedGazeCount = $gazeOrder.Count
    $expectedPhaseCount = $expectedGazeCount * 3
    if ($stagedOutputs.Count -ne $expectedPhaseCount) { throw "Expected $expectedPhaseCount thinking-eye phases; staged $($stagedOutputs.Count)" }

    $thinkingSets = [Collections.Generic.List[object]]::new()
    foreach ($gaze in $gazeOrder) {
        $phaseFiles = [ordered]@{}
        foreach ($phase in @('open','half','closed')) {
            $output = @($stagedOutputs | Where-Object { $_.gaze.id -eq $gaze.id -and $_.phase -eq $phase })[0]
            $phaseFiles[$phase] = [ordered]@{
                file=$output.relative
                sha256=$output.hash
                sourceFiles=$output.sourceFiles
                sourceHashes=$output.sourceHashes
                sourceSha256=$originalHash
                geometry=$output.geometry
                checks=$output.checks
            }
        }
        [void]$thinkingSets.Add([ordered]@{id=$gaze.id;label=$gaze.label;phases=$phaseFiles})
    }
    $inputReceipts = [Collections.Generic.List[object]]::new()
    foreach ($path in $sourceHashesBefore.Keys) {
        [void]$inputReceipts.Add([ordered]@{file=(Get-RootRelativePath $path);sha256=$sourceHashesBefore[$path]})
    }
    $sourceBindings = [ordered]@{
        original='original.png';sha256=$originalHash;referenceGrid='thinking-reference-grid.png';referenceSha256=$referenceHash;rawAtlas='eyes/thinking/raw.png';rawAtlasSha256=$rawHash
        referenceUpper=[ordered]@{file=(Get-RootRelativePath $referenceUpperPath);sha256=$referenceUpperHash}
        browMaster=[ordered]@{file=$browFile;sha256=$(if($null -ne $browSourcePath){$browSourceHash}else{$rawHash})}
        halfLid=[ordered]@{file=(Get-RootRelativePath $halfPath);sha256=$halfHash}
        closedEye=[ordered]@{file=(Get-RootRelativePath $closedPath);sha256=$closedHash}
    }
    if ($halfOcclusionRegions.Count -gt 0) {
        $sourceBindings['halfOcclusion'] = [ordered]@{file=(Get-RootRelativePath $closedPath);sha256=$closedHash}
    }
    if ($browBoundaryPixels.Count -gt 0) { $sourceBindings['browBoundary'] = [ordered]@{file=$browFile;sha256=$browSourceHash} }
    if ($null -ne $originalUpperPath) { $sourceBindings['originalUpper'] = [ordered]@{file=(Get-RootRelativePath $originalUpperPath);sha256=(Get-Sha256 $originalUpperPath)} }
    if ($referenceLockRegions.Count -gt 0) { $sourceBindings['referenceLock'] = [ordered]@{file=(Get-RootRelativePath $referenceUpperPath);sha256=$referenceUpperHash} }
    $sidecarGeometry = [ordered]@{width=$width;height=$height;splitY=$splitY;frameWidth=$width;frameHeight=$splitY;eyeRegions=$eyeRegions;originalLockRegions=$originalLockRegions;halfLidRegions=$halfLidRegions;halfLidOffsetsByGaze=$halfOffsets;browLockEndY=$browEnd;reference=$sourceCheck;rawAspect=$rawAspect;rawAspectTolerance=0.01;cellOrder=@($gazeOrder | ForEach-Object id)}
    $verification = [ordered]@{result='pixel-invariants-pass-not-visual-acceptance';phaseCount=$stagedOutputs.Count;expectedPhaseCount=$expectedPhaseCount;gazeSetCount=$thinkingSets.Count;expectedGazeSetCount=$expectedGazeCount;sourceHashesUnchanged=$true;referenceExact=$true;outsideMaskChangedPixels=0;noExistingOutputsOverwritten=$true}
    if ($referenceLockRegions.Count -gt 0) { $sidecarGeometry['referenceLockRegions'] = $referenceLockRegions }
    if ($halfOcclusionRegions.Count -gt 0) { $sidecarGeometry['halfOcclusionRegions'] = $halfOcclusionRegions }
    if ($browBoundaryPixels.Count -gt 0) {
        $outsideBoundaryWrites = $browBoundaryChangedPixelCount * $expectedPhaseCount
        $sidecarGeometry['browBoundaryPixels'] = $browBoundaryPixels
        $verification['outsideMaskChangedPixels'] = $outsideBoundaryWrites
        $verification['declaredOutsideMaskChangedPixels'] = $outsideBoundaryWrites
        $verification['undeclaredOutsideMaskChangedPixels'] = 0
    }
    if ($gazeLayout -eq 'horizontal-pair') { $sidecarGeometry['atlasColumns'] = $atlasColumns; $sidecarGeometry['atlasRows'] = $atlasRows; $verification['pixelComparisons'] = ($stagedOutputs.Count*$width*$splitY) }
    $sidecar = [ordered]@{
        schemaVersion=1
        portraitRecord=$layout.portraitRecord
        bank=[string]$layout.bank
        thinkingEyeSets=$thinkingSets.ToArray()
        source=$sourceBindings
        geometry=$sidecarGeometry
        inputHashes=$inputReceipts.ToArray()
        verification=$verification
    }
    if ($null -ne $emotionId) { $sidecar['emotionId'] = $emotionId }
    if ($gazeLayout -eq 'horizontal-pair') { $sidecar['gazeLayout'] = $gazeLayout }
    $stagedSidecar = Join-Path $stagingRoot 'thinking-eye-sets.json'
    $sidecar | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $stagedSidecar -Encoding UTF8

    $finalPaths = @($stagedOutputs | ForEach-Object { Join-Path $root $_.relative.Replace('/',[string][IO.Path]::DirectorySeparatorChar) }) + @($sidecarPath)
    foreach ($final in $finalPaths) { if (Test-Path -LiteralPath $final) { throw "Refusing to overwrite existing thinking-eye output: $final" } }
    foreach ($output in $stagedOutputs) {
        $destination = Join-Path $root $output.relative.Replace('/',[string][IO.Path]::DirectorySeparatorChar)
        $directory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        [IO.File]::Move($output.stagedPath,$destination)
        [void]$published.Add($destination)
        if ((Get-Sha256 $destination) -ne $output.hash) { throw "Published frame hash differs: $destination" }
    }
    [IO.File]::Move($stagedSidecar,$sidecarPath)
    [void]$published.Add($sidecarPath)
    "SPLIT_PORTRAIT_THINKING_PASS gazeSets=$($thinkingSets.Count) phases=$($stagedOutputs.Count) sidecar=thinking-eye-sets.json"
} catch {
    foreach ($path in $published) { if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force } }
    throw
} finally {
    foreach ($candidate in $gazeCandidates) { $candidate.Dispose() }
    $original.Dispose();$reference.Dispose();$raw.Dispose();$referenceUpper.Dispose();$halfSource.Dispose();$closedSource.Dispose();if($null -ne $originalUpper){$originalUpper.Dispose()};if($null -ne $browSource){$browSource.Dispose()}
    if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
}
