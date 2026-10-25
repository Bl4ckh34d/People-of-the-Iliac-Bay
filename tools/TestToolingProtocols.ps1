$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'LogProtocol.ps1')
function Assert-Check([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }

$rules=@{ accepted='true'; count={param($v) $n=0; (Test-DecimalValue $v) -and [int]::TryParse($v,[ref]$n) -and $n -gt 0}; hash={param($v) Test-HexValue $v 64 -Lowercase} }
$valid='PROBE: accepted=true; count=2; hash='+('a'*64)+'.'
Assert-Check (Test-ProbeReceipt $valid 'PROBE:' $rules) 'Valid native receipt rejected.'
foreach ($invalid in @(
    $valid.Replace('accepted=true','accepted=false'),
    $valid.Replace('count=2','count=0'),
    $valid.Replace('count=2','count=two'),
    $valid.Replace('hash='+('a'*64),'hash=short'),
    $valid.Replace('count=2; ','count=2; count=3; '),
    ('PROBE: accepted=true; count=2' + "`n" + 'OTHER: hash='+('a'*64))
)) { Assert-Check (-not (Test-ProbeReceipt $invalid 'PROBE:' $rules)) 'Malformed or cross-receipt evidence accepted.' }
Assert-Check (Test-ProbeDecimal '10.00' 2 10 900) 'Invariant decimal rejected.'
Assert-Check (-not (Test-ProbeDecimal 'NaN' 2 0 100)) 'Non-finite decimal accepted.'
Assert-Check (-not (Test-ProbeDecimal '10,00' 2 0 100)) 'Locale-dependent decimal accepted.'
Assert-Check (Test-NativeLogFailure "ordinary output`nSystem.InvalidOperationException: failure") 'Native exception was not detected.'
Assert-Check (Test-NativeLogFailure 'source.cs(1): error CS1000: compile failure') 'Compiler failure was not detected.'
Assert-Check (-not (Test-NativeLogFailure 'PASS Exception handling works')) 'Ordinary evidence was misclassified as an exception.'

# Execute only the receipt assertions, with actual native protocol examples.
# No launcher, process ownership, live save or model service is involved.
$SplitPortraitRuntimeProbe=$true
$body='PEOPLE_OF_THE_ILIAC_BAY_SPLIT_PORTRAIT_PASS: galleryRecords=20; gazes=center,left,right; missing71=native-fallback'
. (Join-Path $PSScriptRoot 'TownTest.CurrentAssertions.ps1')
$rejected=$false
try { $body=$body.Replace('galleryRecords=20','galleryRecords=19'); . (Join-Path $PSScriptRoot 'TownTest.CurrentAssertions.ps1') }
catch { $rejected=$true }
Assert-Check $rejected 'Town acceptance admitted an incomplete portrait gallery.'
$rejected=$false
try { $body=$body.Replace('galleryRecords=19','galleryRecords=20'); $body+="`n"+$body; . (Join-Path $PSScriptRoot 'TownTest.CurrentAssertions.ps1') }
catch { $rejected=$true }
Assert-Check $rejected 'Town acceptance admitted duplicate completion evidence.'
Write-Output 'PASS typed receipt parsing, failure detection, cross-receipt rejection, and town acceptance negatives.'
