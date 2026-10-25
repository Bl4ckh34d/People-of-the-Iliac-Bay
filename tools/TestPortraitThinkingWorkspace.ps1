[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
$preparePath = Join-Path $PSScriptRoot 'PreparePortraitThinkingWorkspace.ps1'
$savePath = Join-Path $PSScriptRoot 'SavePortraitThinkingRawOutput.ps1'
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$runRoot = [IO.Path]::GetFullPath((Join-Path $tempBase ('portrait-thinking-horizontal-test-' + [Guid]::NewGuid().ToString('N'))))
$tempPrefix = $tempBase.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $runRoot.StartsWith($tempPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw "Computed test fixture escapes temp root: $runRoot" }
New-Item -ItemType Directory -Path $runRoot | Out-Null

function Assert-True([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-Equal($Expected,$Actual,[string]$Message) {
    if ($Expected -is [array] -or $Actual -is [array]) {
        if (($Expected -join ',') -ne ($Actual -join ',')) { throw "$Message expected=[$($Expected -join ',')] actual=[$($Actual -join ',')]" }
    } elseif ($Expected -ne $Actual) { throw "$Message expected=$Expected actual=$Actual" }
}
function New-Fixture([string]$Name,[string]$GazeLayout,[bool]$WrongEmotionHalf = $false) {
    $root = Join-Path $runRoot $Name
    foreach ($relative in @('eyes/neutral','eyes/happy')) { New-Item -ItemType Directory -Path (Join-Path $root $relative) -Force | Out-Null }
    $layout = [ordered]@{
        portraitRecord = 777
        bank = 'SYNTHETIC.RCI'
        width = 64
        height = 64
        splitY = 35
        eyeRegions = @(
            [ordered]@{x=15;y=15;width=16;height=17},
            [ordered]@{x=34;y=15;width=17;height=17}
        )
        mouthRegion = [ordered]@{x=22;y=40;width=20;height=10}
    }
    $layout | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'layout.json') -Encoding UTF8

    $original = New-Object Drawing.Bitmap(64,64,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        for ($y=0; $y -lt 64; $y++) { for ($x=0; $x -lt 64; $x++) { $original.SetPixel($x,$y,[Drawing.Color]::FromArgb(255,30+($x%120),40+($y%110),50+(($x+$y)%100))) } }
        $original.Save((Join-Path $root 'original.png'),[Drawing.Imaging.ImageFormat]::Png)
        $upper = New-Object Drawing.Bitmap(64,35,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try { for ($y=0; $y -lt 35; $y++) { for ($x=0; $x -lt 64; $x++) { $upper.SetPixel($x,$y,$original.GetPixel($x,$y)) } }; $upper.Save((Join-Path $root 'source-upper.png'),[Drawing.Imaging.ImageFormat]::Png) }
        finally { $upper.Dispose() }
    } finally { $original.Dispose() }

    foreach ($phase in @('open','half','closed')) {
        $bitmap = New-Object Drawing.Bitmap(64,35,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $source = [Drawing.Bitmap]::FromFile((Join-Path $root 'original.png'))
            try {
                for ($y=0; $y -lt 35; $y++) { for ($x=0; $x -lt 64; $x++) {
                    $color = $source.GetPixel($x,$y)
                    $inside = (($x -ge 15 -and $x -lt 31 -and $y -ge 15 -and $y -lt 32) -or ($x -ge 34 -and $x -lt 51 -and $y -ge 15 -and $y -lt 32))
                    if ($inside) {
                        if ($phase -eq 'open') { $color = [Drawing.Color]::FromArgb(255,150+($x%50),40+($y%70),20+(($x+$y)%50)) }
                        elseif ($phase -eq 'half') { $color = [Drawing.Color]::FromArgb(255,20+($x%50),120+($y%70),170+(($x+$y)%50)) }
                        else { $color = [Drawing.Color]::FromArgb(255,10,10,10) }
                    }
                    $bitmap.SetPixel($x,$y,$color)
                } }
            } finally { $source.Dispose() }
            $bitmap.Save((Join-Path $root "eyes/neutral/$phase.png"),[Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
    }
    Copy-Item -LiteralPath (Join-Path $root 'eyes/neutral/half.png') -Destination (Join-Path $root 'eyes/happy/half.png')
    $halfPath = if ($WrongEmotionHalf) { 'eyes/happy/half.png' } else { 'eyes/neutral/half.png' }
    $thinkingLayout = [ordered]@{
        schemaVersion = 1
        emotionId = 'neutral'
        referenceUpperFile = 'eyes/neutral/open.png'
        browSourceFile = 'eyes/neutral/open.png'
        halfSourceFile = $halfPath
        closedSourceFile = 'eyes/neutral/closed.png'
    }
    if (-not [string]::IsNullOrEmpty($GazeLayout)) { $thinkingLayout.gazeLayout = $GazeLayout }
    $thinkingLayout | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'thinking-layout.json') -Encoding UTF8
    return $root
}
function Invoke-Prepare([string]$Root) { & $preparePath -PortraitRoot $Root | Out-Host }
function Assert-GridCellSources([string]$Root,[int]$CellColumns,[int]$CellRows) {
    $grid = [Drawing.Bitmap]::FromFile((Join-Path $Root 'thinking-reference-grid.png'))
    $open = [Drawing.Bitmap]::FromFile((Join-Path $Root 'eyes/neutral/open.png'))
    $half = [Drawing.Bitmap]::FromFile((Join-Path $Root 'eyes/neutral/half.png'))
    try {
        Assert-Equal (512*$CellColumns) $grid.Width 'Grid width'
        Assert-Equal (280*$CellRows) $grid.Height 'Grid height'
        for ($y=0; $y -lt 35; $y++) { for ($x=0; $x -lt 64; $x++) {
            $sourceColor = $open.GetPixel($x,$y).ToArgb()
            foreach ($cellX in 0..($CellColumns-1)) {
                $top = $grid.GetPixel(($cellX*512)+($x*8)+4,($y*8)+4).ToArgb()
                Assert-Equal $sourceColor $top "Open cell ($cellX,0) pixel ($x,$y)"
                if ($CellRows -eq 2) {
                    $bottom = $grid.GetPixel(($cellX*512)+($x*8)+4,280+($y*8)+4).ToArgb()
                    Assert-Equal $half.GetPixel($x,$y).ToArgb() $bottom "Half cell ($cellX,1) pixel ($x,$y)"
                } else {
                    $repeat = $grid.GetPixel(($cellX*512)+($x*8)+4,560+($y*8)+4).ToArgb()
                    Assert-Equal $sourceColor $repeat "Repeated open cell ($cellX,2) pixel ($x,$y)"
                }
            }
        } }
    } finally { $grid.Dispose(); $open.Dispose(); $half.Dispose() }
}
function New-GeneratedAtlas([string]$Root) {
    $path = Join-Path $Root 'generated.png'; $bitmap=New-Object Drawing.Bitmap(128,70,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try { for ($y=0; $y -lt 70; $y++) { for ($x=0; $x -lt 128; $x++) { $bitmap.SetPixel($x,$y,[Drawing.Color]::FromArgb(255,($x%256),($y%256),(($x+$y)%256))) } }; $bitmap.Save($path,[Drawing.Imaging.ImageFormat]::Png) }
    finally { $bitmap.Dispose() }
    return $path
}
function New-RequestFiles([string]$Root,[string]$GeneratedPath) {
    $prompt = 'synthetic horizontal pair fixture'
    $requestPath = Join-Path $Root 'request.json'; $argsPath=Join-Path $Root 'arguments.json'
    [ordered]@{prompt=$prompt;output_hint="fixture generated output at $GeneratedPath"} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $requestPath -Encoding UTF8
    [ordered]@{prompt=$prompt;referenced_image_paths=@([IO.Path]::GetFullPath((Join-Path $Root 'thinking-reference-grid.png')));transparent_background=$false} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $argsPath -Encoding UTF8
    return @($requestPath,$argsPath)
}
function Invoke-Save([string]$Root,[string]$GeneratedPath) {
    $paths=New-RequestFiles $Root $GeneratedPath
    & $savePath -PortraitRoot $Root -GeneratedPath $GeneratedPath -RequestJsonPath $paths[0] -ArgumentsJsonPath $paths[1] | Out-Host
}

try {
    # Horizontal-pair reference: exact same-emotion open/open above and half/half below.
    $horizontal = New-Fixture 'horizontal-pair' 'horizontal-pair'
    $beforeOpen = (Get-FileHash -LiteralPath (Join-Path $horizontal 'eyes/neutral/open.png') -Algorithm SHA256).Hash
    $beforeHalf = (Get-FileHash -LiteralPath (Join-Path $horizontal 'eyes/neutral/half.png') -Algorithm SHA256).Hash
    Invoke-Prepare $horizontal
    Assert-GridCellSources $horizontal 2 2
    Assert-Equal $beforeOpen ((Get-FileHash -LiteralPath (Join-Path $horizontal 'eyes/neutral/open.png') -Algorithm SHA256).Hash) 'Open source hash unchanged'
    Assert-Equal $beforeHalf ((Get-FileHash -LiteralPath (Join-Path $horizontal 'eyes/neutral/half.png') -Algorithm SHA256).Hash) 'Half source hash unchanged'

    # The wrapper must accept the 2x2 phase atlas and record both actual same-emotion inputs.
    $validSave = New-Fixture 'valid-save' 'horizontal-pair'
    Invoke-Prepare $validSave
    $validGenerated = New-GeneratedAtlas $validSave
    Invoke-Save $validSave $validGenerated
    $receipt = Get-Content -LiteralPath (Join-Path $validSave 'eyes/thinking/reference-association.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'horizontal-pair' $receipt.gazeLayout 'Receipt gazeLayout'
    Assert-Equal @('open','half') @($receipt.referenceSources | ForEach-Object { [string]$_.phase }) 'Receipt phase source order'
    Assert-Equal 2 @($receipt.referenceSources).Count 'Receipt source count'
    Assert-Equal 1024 $receipt.actualReference.width 'Receipt grid width'
    Assert-Equal 560 $receipt.actualReference.height 'Receipt grid height'

    # A bad lower-phase row cannot be accepted or copied to eyes/thinking.
    $badRow = New-Fixture 'bad-half-row' 'horizontal-pair'
    Invoke-Prepare $badRow
    $badGrid = [Drawing.Bitmap]::FromFile((Join-Path $badRow 'thinking-reference-grid.png'))
    $wrongSource = [Drawing.Bitmap]::FromFile((Join-Path $badRow 'eyes/neutral/open.png'))
    try { $badGrid.SetPixel(16*8+4,280+15*8+4,$wrongSource.GetPixel(16,15)); $badGrid.Save((Join-Path $badRow 'bad-grid.png'),[Drawing.Imaging.ImageFormat]::Png) }
    finally { $badGrid.Dispose(); $wrongSource.Dispose() }
    Copy-Item -LiteralPath (Join-Path $badRow 'bad-grid.png') -Destination (Join-Path $badRow 'thinking-reference-grid.png') -Force
    $badGenerated = New-GeneratedAtlas $badRow
    $rejectedBadRow = $false
    try { Invoke-Save $badRow $badGenerated } catch { $rejectedBadRow = $_.Exception.Message.Contains('selected half-phase source') }
    Assert-True $rejectedBadRow 'Save helper must reject a lower grid row copied from open phase'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $badRow 'eyes/thinking'))) 'Rejected lower row must not leave thinking output'

    # Explicit emotion path contract still rejects a phase source from a different emotion.
    $wrongEmotion = New-Fixture 'wrong-emotion-source' 'horizontal-pair' $true
    $rejectedWrongEmotion = $false
    try { Invoke-Prepare $wrongEmotion } catch { $rejectedWrongEmotion = $_.Exception.Message.Contains('halfSourceFile must select') }
    Assert-True $rejectedWrongEmotion 'Prepare helper must reject half source from a different emotion'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $wrongEmotion 'thinking-reference-grid.png'))) 'Wrong-emotion source must not leave a reference grid'

    # Both selected phase sources must retain the approved face outside the eye masks.
    $badHalfExterior = New-Fixture 'bad-half-exterior' 'horizontal-pair'
    $badHalfPath = Join-Path $badHalfExterior 'eyes/neutral/half.png'
    $halfBitmap = [Drawing.Bitmap]::FromFile($badHalfPath)
    try { $halfBitmap.SetPixel(2,2,[Drawing.Color]::FromArgb(255,1,2,3)); $halfBitmap.Save(($badHalfPath + '.tmp.png'),[Drawing.Imaging.ImageFormat]::Png) }
    finally { $halfBitmap.Dispose() }
    Copy-Item -LiteralPath ($badHalfPath + '.tmp.png') -Destination $badHalfPath -Force
    $rejectedHalfExterior = $false
    try { Invoke-Prepare $badHalfExterior } catch { $rejectedHalfExterior = $_.Exception.Message.Contains('Selected emotional half-phase source differs from original outside eye masks at (2,2)') }
    Assert-True $rejectedHalfExterior 'Prepare helper must reject half source changes outside eye masks'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $badHalfExterior 'thinking-reference-grid.png'))) 'Exterior mismatch must not leave a reference grid'

    # Omitting gazeLayout keeps the original repeated 3x3 open-source grid.
    $legacy = New-Fixture 'legacy-default' ''
    Invoke-Prepare $legacy
    Assert-GridCellSources $legacy 3 3

    'TEST_PORTRAIT_THINKING_WORKSPACE_PASS'
} finally {
    if (Test-Path -LiteralPath $runRoot -PathType Container) {
        $full = [IO.Path]::GetFullPath($runRoot)
        if ($full.StartsWith($tempPrefix,[StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $full -Recurse -Force }
    }
}
