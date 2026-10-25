param(
    [string]$Endpoint = 'http://127.0.0.1:8080/completion',
    [switch]$RunInference,
    [string]$ResultPath
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($ResultPath)) {
    $ResultPath = Join-Path $repo 'work\dialogue-grounding-model-results.json'
}

dotnet build (Join-Path $repo 'src\PeopleOfTheIliacBay.Core\PeopleOfTheIliacBay.Core.csproj') -c Release --nologo | Out-Null
$coreAssembly = Join-Path $repo 'src\PeopleOfTheIliacBay.Core\bin\Release\netstandard2.0\PeopleOfTheIliacBay.Core.dll'
Add-Type -Path $coreAssembly

$clockMethod = [PeopleOfTheIliacBay.Core.ConversationClock].GetMethod(
    'Context', [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
if ($null -eq $clockMethod) {
    throw 'ConversationClock.Context(hour, minute, date, year, isNight) is not available; refusing to substitute fixture clock text.'
}
$hauntingMethod = [PeopleOfTheIliacBay.Core.ConversationLifeContext].GetMethod(
    'Haunting', [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
if ($null -eq $hauntingMethod) {
    throw 'ConversationLifeContext.Haunting(minuteOfDay) is not available; refusing to substitute fixture haunting context.'
}

$history = @'
Conversation: 21:17, 2nd Morning Star, 3E 405, night
Player: Lysandus haunts Daggerfall. I have heard the stories all my life.
NPC: Yes, the late King Lysandus haunts Daggerfall. I avoid going outside after dark while the haunting troubles the city.
Player: We are speaking at the Crown and Anchor Inn.
NPC: I keep this inn. My own house is elsewhere.
'@

$place = 'Current verified place: the player is physically inside the Crown and Anchor Inn in Daggerfall. You are the innkeeper, working at the inn. This public inn is not your private house; your home is elsewhere.'
$hauntingFact = 'Current verified local fact: King Lysandus haunts Daggerfall.'

$fixtureSpecs = @(
    @{ Id = 'daytime-outing'; Hour = 10; Minute = 9; Date = '13th Morning Star'; Year = 405; IsNight = $false;
       PlayerText = 'Will you come outside with me right now?';
       Expected = 'Must not claim it is currently night, evening, or after dark. It may accept or decline for another supported reason.' },
    @{ Id = 'nighttime-outing'; Hour = 22; Minute = 9; Date = '13th Morning Star'; Year = 405; IsNight = $true;
       PlayerText = 'Will you come outside with me right now?';
       Expected = 'May decline because the current verified time is after dark and the haunting restriction applies; must not claim a different current time.' },
    @{ Id = 'remember-haunting'; Hour = 10; Minute = 9; Date = '13th Morning Star'; Year = 405; IsNight = $false;
       PlayerText = 'I already told you earlier that Lysandus haunts Daggerfall. Did you forget?';
       Expected = 'Must acknowledge the earlier statement from transcript and avoid a stranger-style greeting or asking what brings the player to Daggerfall.' },
    @{ Id = 'inn-is-not-home'; Hour = 10; Minute = 9; Date = '13th Morning Star'; Year = 405; IsNight = $false;
       PlayerText = 'We are inside your house, right?';
       Expected = 'Must clarify this is the public inn where the NPC works, not the NPC''s private house.' }
)

$connection = [PeopleOfTheIliacBay.Core.LocalDialogueConnection]::new($Endpoint, 10000)
$results = [System.Collections.Generic.List[object]]::new()
foreach ($spec in $fixtureSpecs) {
    $clock = [string]$clockMethod.Invoke($null, [object[]]@(
        [int]$spec.Hour, [int]$spec.Minute, [string]$spec.Date, [int]$spec.Year, [bool]$spec.IsNight))
    $haunting = [string]$hauntingMethod.Invoke($null, [object[]]@([int]$spec.Hour * 60 + [int]$spec.Minute))
    $context = $clock + ' ' + $hauntingFact + ' ' + $haunting + ' ' + $place
    $request = [PeopleOfTheIliacBay.Core.FreeTextDialogue]::Create(
        [string]$spec.PlayerText, 'Mara', 'Breton', 'Female', 'innkeeper', 20,
        $context, $history, '')
    $completion = [PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Build($connection, $request)
    $answer = $null
    if ($RunInference) {
        $response = Invoke-RestMethod -Uri $Endpoint -Method Post -ContentType 'application/json' `
            -Body $completion.JsonBody -TimeoutSec 90
        $answer = [string]$response.content
    }
    $results.Add([pscustomobject]@{
        id = $spec.Id
        currentClock = $clock
        playerText = $spec.PlayerText
        expectedManualReview = $spec.Expected
        answer = $answer
        prompt = $completion.Prompt
    })
}

$directory = Split-Path -Parent $ResultPath
if (-not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
$results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
if ($RunInference) {
    Write-Output "Model outputs saved for manual review: $ResultPath"
} else {
    Write-Output "Prompt fixtures built without inference; answer fields are empty: $ResultPath"
    Write-Output 'Use -RunInference only after the live game is closed and inference is cleared.'
}
