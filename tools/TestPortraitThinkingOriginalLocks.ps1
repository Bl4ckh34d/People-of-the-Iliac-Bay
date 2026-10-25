[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
$builderPath = Join-Path $PSScriptRoot 'BuildSplitPortraitThinking.ps1'
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$runRoot = [IO.Path]::GetFullPath((Join-Path $tempBase ('portrait-thinking-original-locks-' + [Guid]::NewGuid().ToString('N'))))
$tempPrefix = $tempBase.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $runRoot.StartsWith($tempPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw "Computed test fixture escapes temp root: $runRoot" }
New-Item -ItemType Directory -Path $runRoot | Out-Null

function Assert-True([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-Equal($Expected,$Actual,[string]$Message) { if ($Expected -ne $Actual) { throw "$Message expected=$Expected actual=$Actual" } }

function New-Fixture([string]$Name,[object]$Locks,[bool]$IncludeLocks = $true) {
    $root = Join-Path $runRoot $Name
    foreach ($relative in @('eyes/neutral','eyes/thinking')) { New-Item -ItemType Directory -Path (Join-Path $root $relative) -Force | Out-Null }
    $layout = [ordered]@{
        portraitRecord = 777; bank = 'SYNTHETIC.RCI'; width = 64; height = 64; splitY = 35; browLockEndY = 17
        eyeRegions = @([ordered]@{x=15;y=15;width=16;height=17},[ordered]@{x=34;y=15;width=17;height=17})
        mouthRegion = [ordered]@{x=22;y=40;width=20;height=10}
    }
    $layout | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'layout.json') -Encoding UTF8

    $original = [Drawing.Bitmap]::new(64,64,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $upper = [Drawing.Bitmap]::new(64,35,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        for ($y=0; $y -lt 64; $y++) { for ($x=0; $x -lt 64; $x++) { $original.SetPixel($x,$y,[Drawing.Color]::FromArgb(255,30+($x%120),40+($y%110),50+(($x+$y)%100))) } }
        for ($y=0; $y -lt 35; $y++) { for ($x=0; $x -lt 64; $x++) { $upper.SetPixel($x,$y,$original.GetPixel($x,$y)) } }
        $original.Save((Join-Path $root 'original.png'),[Drawing.Imaging.ImageFormat]::Png)
        $upper.Save((Join-Path $root 'source-upper.png'),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $original.Dispose(); $upper.Dispose() }

    $sourceOriginal = [Drawing.Bitmap]::FromFile((Join-Path $root 'original.png'))
    try {
        foreach ($phase in @('open','half','closed')) {
            $bitmap = [Drawing.Bitmap]::new(64,35,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                for ($y=0; $y -lt 35; $y++) { for ($x=0; $x -lt 64; $x++) {
                    $color = $sourceOriginal.GetPixel($x,$y)
                    $inside = (($x -ge 15 -and $x -lt 31 -and $y -ge 15 -and $y -lt 32) -or ($x -ge 34 -and $x -lt 51 -and $y -ge 15 -and $y -lt 32))
                    if ($inside) {
                        if ($phase -eq 'open') { $color = [Drawing.Color]::FromArgb(255,160,80,25) }
                        elseif ($phase -eq 'half') { $color = [Drawing.Color]::FromArgb(255,25,160,80) }
                        else { $color = [Drawing.Color]::FromArgb(255,10,10,10) }
                    }
                    $bitmap.SetPixel($x,$y,$color)
                } }
                $bitmap.Save((Join-Path $root "eyes/neutral/$phase.png"),[Drawing.Imaging.ImageFormat]::Png)
            } finally { $bitmap.Dispose() }
        }
    } finally { $sourceOriginal.Dispose() }

    $thinkingLayout = [ordered]@{
        schemaVersion=1; emotionId='neutral'; gazeLayout='horizontal-pair'
        referenceUpperFile='eyes/neutral/open.png'; browSourceFile='eyes/neutral/open.png'
        halfSourceFile='eyes/neutral/half.png'; closedSourceFile='eyes/neutral/closed.png'
    }
    if ($IncludeLocks) { $thinkingLayout['originalLockRegions'] = $Locks }
    $thinkingLayout | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'thinking-layout.json') -Encoding UTF8

    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $root 'original.png') -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{schemaVersion=1;portraitRecord=777;sourceSha256=$sourceHash;sourceWidth=64;sourceHeight=64;splitY=35} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'setup-provenance.json') -Encoding UTF8

    $open = [Drawing.Bitmap]::FromFile((Join-Path $root 'eyes/neutral/open.png'))
    $half = [Drawing.Bitmap]::FromFile((Join-Path $root 'eyes/neutral/half.png'))
    try {
        $grid = [Drawing.Bitmap]::new(1024,560,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            for ($cell=0; $cell -lt 4; $cell++) {
                $sample = if ($cell -lt 2) { $open } else { $half }
                $cellX = $cell % 2; $cellY = [int][Math]::Floor($cell/2)
                for ($y=0; $y -lt 35; $y++) { for ($x=0; $x -lt 64; $x++) {
                    $color = $sample.GetPixel($x,$y)
                    for ($dy=0; $dy -lt 8; $dy++) { for ($dx=0; $dx -lt 8; $dx++) { $grid.SetPixel(($cellX*512)+($x*8)+$dx,($cellY*280)+($y*8)+$dy,$color) } }
                } }
            }
            $grid.Save((Join-Path $root 'thinking-reference-grid.png'),[Drawing.Imaging.ImageFormat]::Png)
        } finally { $grid.Dispose() }
    } finally { $open.Dispose(); $half.Dispose() }

    $atlas = [Drawing.Bitmap]::new(128,70,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        for ($y=0; $y -lt 70; $y++) { for ($x=0; $x -lt 128; $x++) {
            $cell = [int][Math]::Floor($y/35)*2 + [int][Math]::Floor($x/64)
            $color = switch ($cell) { 0 { [Drawing.Color]::FromArgb(255,220,15,30) } 1 { [Drawing.Color]::FromArgb(255,15,210,30) } 2 { [Drawing.Color]::FromArgb(255,15,30,220) } default { [Drawing.Color]::FromArgb(255,210,200,15) } }
            $atlas.SetPixel($x,$y,$color)
        } }
        $atlas.Save((Join-Path $root 'eyes/thinking/raw.png'),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $atlas.Dispose() }
    return $root
}

function Invoke-Build([string]$Root) { & $builderPath -PortraitRoot $Root | Out-Host }
function Get-Frame([string]$Root,[string]$Gaze,[string]$Phase) { [Drawing.Bitmap]::FromFile((Join-Path $Root "eyes/thinking/$Gaze/$Phase.png")) }

try {
    $valid = New-Fixture 'valid-locks' @([ordered]@{x=15;y=30;width=16;height=2},[ordered]@{x=34;y=30;width=17;height=2})
    Invoke-Build $valid
    $sourceUpper = [Drawing.Bitmap]::FromFile((Join-Path $valid 'source-upper.png'))
    try {
        foreach ($gaze in @('left','right')) { foreach ($phase in @('open','half','closed')) {
            $frame = Get-Frame $valid $gaze $phase
            try {
                for ($y=30; $y -lt 32; $y++) { foreach ($x in 15..30) { Assert-Equal $sourceUpper.GetPixel($x,$y).ToArgb() $frame.GetPixel($x,$y).ToArgb() "Locked left-eye pixel must equal source-upper ($gaze/$phase at $x,$y)" }; foreach ($x in 34..50) { Assert-Equal $sourceUpper.GetPixel($x,$y).ToArgb() $frame.GetPixel($x,$y).ToArgb() "Locked right-eye pixel must equal source-upper ($gaze/$phase at $x,$y)" } }
                $expectedAtlas = if ($phase -eq 'closed') { if ($gaze -eq 'left') { [Drawing.Color]::FromArgb(255,10,10,10).ToArgb() } else { [Drawing.Color]::FromArgb(255,10,10,10).ToArgb() } } elseif ($phase -eq 'open') { if ($gaze -eq 'left') { [Drawing.Color]::FromArgb(255,220,15,30).ToArgb() } else { [Drawing.Color]::FromArgb(255,15,210,30).ToArgb() } } else { if ($gaze -eq 'left') { [Drawing.Color]::FromArgb(255,15,30,220).ToArgb() } else { [Drawing.Color]::FromArgb(255,210,200,15).ToArgb() } }
                Assert-Equal $expectedAtlas $frame.GetPixel(20,28).ToArgb() "Adjacent iris row must remain generated/closed-covered ($gaze/$phase)"
            } finally { $frame.Dispose() }
        } }
    } finally { $sourceUpper.Dispose() }
    $receipt = Get-Content -LiteralPath (Join-Path $valid 'thinking-eye-sets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 2 @($receipt.geometry.originalLockRegions).Count 'Sidecar lock geometry count'

    # Missing lock configuration keeps the pre-existing horizontal pixel behavior.
    $legacy = New-Fixture 'no-locks' $null $false
    Invoke-Build $legacy
    foreach ($gaze in @('left','right')) {
        $openFrame = Get-Frame $legacy $gaze 'open'
        $closedFrame = Get-Frame $legacy $gaze 'closed'
        try {
            $openExpected = if ($gaze -eq 'left') { [Drawing.Color]::FromArgb(255,220,15,30).ToArgb() } else { [Drawing.Color]::FromArgb(255,15,210,30).ToArgb() }
            Assert-Equal $openExpected $openFrame.GetPixel(20,30).ToArgb() "No-lock open behavior unchanged ($gaze)"
            Assert-Equal ([Drawing.Color]::FromArgb(255,10,10,10).ToArgb()) $closedFrame.GetPixel(20,30).ToArgb() "No-lock closed behavior unchanged ($gaze)"
        } finally { $openFrame.Dispose(); $closedFrame.Dispose() }
    }

    $invalidCases = @(
        [pscustomobject]@{name='malformed';locks=@([ordered]@{x=15.5;y=30;width=16;height=2});match='must be an integer'},
        [pscustomobject]@{name='outside';locks=@([ordered]@{x=30;y=30;width=2;height=2});match='fit wholly inside exactly one eye mask'},
        [pscustomobject]@{name='over-brow';locks=@([ordered]@{x=15;y=17;width=16;height=2});match='must be below browLockEndY'}
    )
    foreach ($case in $invalidCases) {
        $root = New-Fixture $case.name $case.locks
        $rejected = $false
        try { Invoke-Build $root } catch { $rejected = $_.Exception.ToString().Contains($case.match) }
        Assert-True $rejected "Expected invalid $($case.name) lock configuration to be rejected before output"
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $root 'eyes/thinking/left'))) "Rejected $($case.name) lock configuration must not write frames"
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $root 'thinking-eye-sets.json'))) "Rejected $($case.name) lock configuration must not write sidecar"
    }
    'TEST_PORTRAIT_THINKING_ORIGINAL_LOCKS_PASS'
} finally {
    if (Test-Path -LiteralPath $runRoot -PathType Container) {
        $full = [IO.Path]::GetFullPath($runRoot)
        if ($full.StartsWith($tempPrefix,[StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $full -Recurse -Force }
    }
}
