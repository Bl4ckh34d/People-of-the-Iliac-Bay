param(
    [Parameter(Mandatory=$true)][string]$CasesPath,
    [Parameter(Mandatory=$true)][string]$RunDirectory,
    [Parameter(Mandatory=$true)][string]$ExpectedModel,
    [string]$Endpoint = 'http://127.0.0.1:8080/completion'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$uri = [Uri]$Endpoint
$address = $null
if ($uri.Scheme -ne 'http' -or -not [Net.IPAddress]::TryParse($uri.Host,[ref]$address) -or
    -not [Net.IPAddress]::IsLoopback($address) -or $uri.AbsolutePath -ne '/completion' -or
    $uri.UserInfo -or $uri.Query -or $uri.Fragment) { throw 'Only a loopback HTTP /completion endpoint is supported.' }
$workRoot = [IO.Path]::GetFullPath((Join-Path $repo 'work')).TrimEnd('\') + '\'
$runPath = if ([IO.Path]::IsPathRooted($RunDirectory)) { [IO.Path]::GetFullPath($RunDirectory) } else { [IO.Path]::GetFullPath((Join-Path $repo $RunDirectory)) }
if (-not $runPath.StartsWith($workRoot,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $runPath)) {
    throw 'RunDirectory must be a fresh directory under repository work.'
}
$casesFile = (Resolve-Path -LiteralPath $CasesPath).Path
$fixture = Get-Content -LiteralPath $casesFile -Raw | ConvertFrom-Json
if ($fixture.schemaVersion -ne 1 -or -not $fixture.factualContext -or $fixture.cases.Count -lt 1 -or $fixture.cases.Count -gt 32) { throw 'Invalid bounded case fixture.' }
if (@($fixture.cases.id | Select-Object -Unique).Count -ne $fixture.cases.Count) { throw 'Case identifiers must be unique.' }
$assembly = Join-Path $repo 'src/PeopleOfTheIliacBay.Core/bin/Release/netstandard2.0/PeopleOfTheIliacBay.Core.dll'
Add-Type -Path $assembly
$bindingConstructor = [PeopleOfTheIliacBay.Core.ConversationContextBinding].GetConstructors()[0]
$binding = $bindingConstructor.Invoke([object[]]@(
    [PeopleOfTheIliacBay.Core.ConversationActorCategory]::NativeMobile,
    's:206728581:h:131074:r:0',$null,'two-pass-diagnostic-20261020',[long]0,[long]4,
    '197cf882a6f0bfd2835ab2fa8c9543cf','','DaggerfallUnityGame',[long]0))
$connection = [PeopleOfTheIliacBay.Core.LocalDialogueConnection]::new($Endpoint,10000)
$base = $uri.GetLeftPart([UriPartial]::Authority)
function Assert-Model {
    $ids = @((Invoke-RestMethod -Uri ($base+'/v1/models') -TimeoutSec 5).data.id)
    if ($ids.Count -ne 1 -or $ids[0] -cne $ExpectedModel) { throw 'Selected model changed or differs from ExpectedModel.' }
}
Assert-Model
$contextSize = [int](Invoke-RestMethod -Uri ($base+'/props') -TimeoutSec 5).default_generation_settings.n_ctx
if ($contextSize -le 0) { $contextSize = [int](Invoke-RestMethod -Uri ($base+'/props') -TimeoutSec 5).n_ctx }
if ($contextSize -le 0) { throw 'Server context size unavailable.' }
New-Item -ItemType Directory -Path $runPath | Out-Null
Copy-Item -LiteralPath $casesFile -Destination (Join-Path $runPath 'cases.json')
$hashes = @($casesFile,$assembly,$PSCommandPath) | ForEach-Object { @{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash} }
@{model=$ExpectedModel;endpoint=$Endpoint;caseCount=$fixture.cases.Count;contextSize=$contextSize;
    hashes=$hashes;started=(Get-Date -Format o);execution='Interpretation only; no candidate binding, D3 consent or native effects.';
    retries=0;temperature=0} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runPath 'manifest.json') -Encoding utf8
$ledger = Join-Path $runPath 'calls.jsonl'
$resultsFile = Join-Path $runPath 'results.jsonl'
$script:callNumber = 0
function Invoke-Extraction([string]$id,[string]$stage,[string]$instructions,[string]$history,[string]$grammar) {
    Assert-Model
    $completion = [PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Extract($connection,$instructions,$history,$grammar,1536)
    $tokenCounter = [Func[string,int]]{
        param($prompt)
        $tokens = Invoke-RestMethod -Uri ($base+'/tokenize') -Method Post -ContentType 'application/json; charset=utf-8' -Body (@{content=$prompt;add_special=$true}|ConvertTo-Json -Compress) -TimeoutSec 10
        return [int]$tokens.tokens.Count
    }
    $fitted = [PeopleOfTheIliacBay.Core.DialogueHistoryWindow]::Fit($completion.Prompt,$completion.History,($contextSize-1536-32),$tokenCounter,$completion.StructuredHistory)
    if ($null -eq $fitted) { throw 'Fixed extraction input exceeds model context; no call was made.' }
    $payload = $completion.JsonBody | ConvertFrom-Json
    $payload.prompt = $fitted
    $body = $payload | ConvertTo-Json -Depth 12 -Compress
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $raw = $null
    $failure = $null
    try {
        $reply = Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json; charset=utf-8' -Body $body -TimeoutSec 35
        $raw = [string]$reply.content
    } catch { $failure = $_.Exception.Message }
    $watch.Stop()
    $script:callNumber++
    $row = @{call=$script:callNumber;caseId=$id;stage=$stage;request=$payload;raw=$raw;error=$failure;elapsedMs=$watch.Elapsed.TotalMilliseconds}
    $row | ConvertTo-Json -Depth 15 -Compress | Add-Content -LiteralPath $ledger -Encoding utf8
    return [pscustomobject]@{Raw=$raw;ElapsedMs=$watch.Elapsed.TotalMilliseconds;Error=$failure}
}
function Test-SemanticExpectation($proposal,$expected) {
    $errors = [Collections.Generic.List[string]]::new()
    if ($null -eq $proposal) { $errors.Add('No Core-valid proposal.'); return ,$errors.ToArray() }
    if ($proposal.Intent.ToString() -cne $expected.intent) { $errors.Add('Intent differs.') }
    if ($proposal.Steps.Count -ne $expected.kinds.Count) { $errors.Add('Action count differs.'); return ,$errors.ToArray() }
    for ($i=0; $i -lt $proposal.Steps.Count; $i++) {
        $step=$proposal.Steps[$i]; $gold=$expected.steps[$i]
        if ($step.Kind -cne $expected.kinds[$i]) { $errors.Add('Action kind/order differs at '+$i) }
        if ($null -ne $gold.targets -and -not (@($gold.targets) -ccontains $step.TargetText)) { $errors.Add('Target differs at '+$i) }
        if ($null -eq $gold.duration) {
            if ($null -ne $step.Duration) { $errors.Add('Unrequested duration at '+$i) }
        } elseif ($null -eq $step.Duration -or $step.Duration.Amount -ne [decimal]$gold.duration.amount -or $step.Duration.Unit.ToString() -cne $gold.duration.unit) {
            $errors.Add('Duration differs at '+$i)
        }
        if ($null -eq $gold.quantity) {
            if ($null -ne $step.Quantity) { $errors.Add('Unrequested quantity at '+$i) }
        } elseif ($null -eq $step.Quantity -or $step.Quantity.Amount -ne [int]$gold.quantity) { $errors.Add('Quantity differs at '+$i) }
    }
    foreach ($field in @('Conditions','Exclusions')) {
        if (($proposal.$field.Count -gt 0) -ne ($expected.$field.Count -gt 0)) { $errors.Add($field+' presence differs.') }
    }
    return ,$errors.ToArray()
}
$rows = [Collections.Generic.List[object]]::new()
foreach ($case in $fixture.cases) {
    $text=[string]$case.text; $history=[string]$case.history
    $one=Invoke-Extraction $case.id 'one-pass' ([PeopleOfTheIliacBay.Core.NpcGoalChat]::ExtractionInstructions($text,$fixture.factualContext)) $history ([PeopleOfTheIliacBay.Core.NpcGoalChat]::Grammar())
    $oneParsed=[PeopleOfTheIliacBay.Core.NpcGoalChat]::Parse($one.Raw,$binding,$text)
    $first=Invoke-Extraction $case.id 'decomposition' ([PeopleOfTheIliacBay.Core.NpcGoalTwoPassChat]::DecompositionInstructions($text,$fixture.factualContext)) $history ([PeopleOfTheIliacBay.Core.NpcGoalTwoPassChat]::DecompositionGrammar())
    $decomposed=[PeopleOfTheIliacBay.Core.NpcGoalTwoPassChat]::ParseDecomposition($first.Raw,$binding,$text)
    $twoParsed=$null; $second=$null
    if ($decomposed.Status.ToString() -eq 'Valid') {
        $d=$decomposed.Decomposition
        $second=Invoke-Extraction $case.id 'parameters' ([PeopleOfTheIliacBay.Core.NpcGoalTwoPassChat]::ParameterInstructions($d)) $history ([PeopleOfTheIliacBay.Core.NpcGoalTwoPassChat]::ParameterGrammar($d))
        $twoParsed=[PeopleOfTheIliacBay.Core.NpcGoalTwoPassChat]::ParseParameters($second.Raw,$d)
    }
    $oneErrors=Test-SemanticExpectation $oneParsed.Proposal $case.expected
    $twoErrors=Test-SemanticExpectation $twoParsed.Proposal $case.expected
    $row=[pscustomobject]@{id=$case.id;group=$case.group;expected=$case.expected;
        onePass=@{status=$oneParsed.Status.ToString();reason=$oneParsed.Reason;proposal=$oneParsed.Proposal;fieldErrors=@($oneErrors);fieldPass=($oneErrors.Count-eq0);elapsedMs=$one.ElapsedMs};
        twoPass=@{decompositionStatus=$decomposed.Status.ToString();decompositionReason=$decomposed.Reason;kinds=@($decomposed.Decomposition.Kinds);
            status=if($null-eq$twoParsed){'NotRun'}else{$twoParsed.Status.ToString()};reason=$twoParsed.Reason;proposal=$twoParsed.Proposal;
            fieldErrors=@($twoErrors);fieldPass=($twoErrors.Count-eq0);elapsedMs=($first.ElapsedMs+$second.ElapsedMs)}}
    $rows.Add($row)
    $row | ConvertTo-Json -Depth 25 -Compress | Add-Content -LiteralPath $resultsFile -Encoding utf8
    Write-Output ($case.id+': one='+$row.onePass.status+'/'+$row.onePass.fieldPass+'; two='+$row.twoPass.status+'/'+$row.twoPass.fieldPass)
}
$summary=@{caseCount=$rows.Count;modelCalls=$script:callNumber;
    onePassProtocolValid=@($rows|Where-Object {$_.onePass.status-eq'Valid'}).Count;
    twoPassProtocolValid=@($rows|Where-Object {$_.twoPass.status-eq'Valid'}).Count;
    onePassFieldPass=@($rows|Where-Object {$_.onePass.fieldPass}).Count;
    twoPassFieldPass=@($rows|Where-Object {$_.twoPass.fieldPass}).Count;
    nativeEffects='None; interpretation diagnostic only.';generalAccuracyClaim=$false}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runPath 'summary.json') -Encoding utf8
$summary | ConvertTo-Json
