param(
    [string]$Endpoint='http://127.0.0.1:8080/completion',
    [switch]$SkipBuild
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(-not $SkipBuild){
    dotnet build (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/PeopleOfTheIliacBay.Core.csproj') -c Release --nologo | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Core build failed.'}
}
Add-Type -Path (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/bin/Release/netstandard2.0/PeopleOfTheIliacBay.Core.dll')
$connection=[PeopleOfTheIliacBay.Core.LocalDialogueConnection]::new($Endpoint,10000)
$topics=[System.Collections.Generic.List[PeopleOfTheIliacBay.Core.ConversationTopic]]::new()
$topics.Add([PeopleOfTheIliacBay.Core.ConversationTopic]::new(42,'The Copper Kettle','Taverns / The Copper Kettle',$false))
$history='Player: Yesterday I asked about the weather. NPC: The sky was clear.'
$cases=@(
    @{Text='By tomorrow I will have your son killed.';Expected='family_threat';ScopeKind='family_threat';ScopeExpected='CURRENT'},
    @{Text='If you refuse to pay me, I will hurt your daughter.';Expected='family_threat';ScopeKind='family_threat';ScopeExpected='CURRENT'},
    @{Text='If you tell the guards, your brother will not live to see next week.';Expected='family_threat';ScopeKind='family_threat';ScopeExpected='CURRENT'},
    @{Text='He said, "By tomorrow I will have your son killed." I think he was bluffing.';Expected='none';ScopeKind='family_threat';ScopeExpected='NONCURRENT'},
    @{Text='If anyone threatens your daughter, tell me and I will protect her.';Expected='none';ScopeKind='family_threat';ScopeExpected='NONCURRENT'},
    @{Text='Suppose I told you I would kill your son. Would you believe me?';Expected='none';ScopeKind='family_threat';ScopeExpected='NONCURRENT'},
    @{Text='I will not hurt your daughter, even if you refuse.';Expected='none';ScopeKind='family_threat';ScopeExpected='NONCURRENT'},
    @{Text='My shift starts in ten minutes, so I need to get going. Goodnight.';Expected='busy_pause';ScopeKind='busy_pause';ScopeExpected='CURRENT'},
    @{Text='I have to return to the inn before curfew, so I should head out now.';Expected='busy_pause';ScopeKind='busy_pause';ScopeExpected='CURRENT'},
    @{Text='I should be going before it gets too late. Farewell.';Expected='busy_pause';ScopeKind='busy_pause';ScopeExpected='CURRENT'},
    @{Text='I can talk all evening; I do not have to leave until tomorrow.';Expected='none';ScopeKind='busy_pause';ScopeExpected='NONCURRENT'},
    @{Text='If my shift started tomorrow, I might leave early; for now I am staying.';Expected='none';ScopeKind='busy_pause';ScopeExpected='NONCURRENT'},
    @{Text='Would it be rude if I left when the bell rang tomorrow?';Expected='none';ScopeKind='busy_pause';ScopeExpected='NONCURRENT'},
    @{Text='My sister needs to go home now; I will stay with you.';Expected='none';ScopeKind='busy_pause';ScopeExpected='NONCURRENT'},
    @{Text='Please leave me alone for a while.';Expected='busy_pause';ScopeKind='busy_pause';ScopeExpected='CURRENT'}
)

function Invoke-LocalClassification([string]$Prompt,[string]$Grammar){
    $completion=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Classify($connection,$Prompt,'',$Grammar)
    $payload=$completion.JsonBody|ConvertFrom-Json
    $payload|Add-Member -NotePropertyName seed -NotePropertyValue 47 -Force
    $response=Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json' `
        -Body ($payload|ConvertTo-Json -Depth 12 -Compress) -TimeoutSec 35
    return ([string]$response.content).Trim()
}

$failures=[System.Collections.Generic.List[string]]::new()
$pipelinePass=0
$scopePass=0
foreach($case in $cases){
    $prompt=[PeopleOfTheIliacBay.Core.ConversationKnowledge]::Prompt($case.Text,$topics,-1,$history)
    $raw=Invoke-LocalClassification $prompt ([PeopleOfTheIliacBay.Core.ConversationKnowledge]::Grammar($topics))
    $choice=[PeopleOfTheIliacBay.Core.ConversationKnowledge]::Parse($raw,$topics,$case.Text,-1)
    $scope='not-needed'
    $final=$choice
    if($choice -and [PeopleOfTheIliacBay.Core.SocialEffectScope]::Required($choice)){
        $scope=Invoke-LocalClassification ([PeopleOfTheIliacBay.Core.SocialEffectScope]::Prompt($case.Text,$history,$choice.SocialIntent)) `
            ([PeopleOfTheIliacBay.Core.SocialEffectScope]::Grammar)
        if([PeopleOfTheIliacBay.Core.SocialEffectScope]::Valid($scope)){
            $final=[PeopleOfTheIliacBay.Core.SocialEffectScope]::Apply($choice,$scope)
        }else{$final=$null}
    }
    $actual=if($final){$final.SocialIntent}else{'invalid'}
    if($actual -eq $case.Expected){
        $pipelinePass++
        Write-Output ('PASS pipeline expected='+$case.Expected+' actual='+$actual+' scope='+$scope+' text='+$case.Text)
    }else{
        $failure='pipeline expected='+$case.Expected+' actual='+$actual+' first='+$raw+' scope='+$scope+' text='+$case.Text
        $failures.Add($failure);Write-Output ('FAIL '+$failure)
    }

    # Force a proposal independently to test the effect-scope classifier even
    # when the primary classifier correctly declines to propose an effect.
    $scopeOnly=Invoke-LocalClassification ([PeopleOfTheIliacBay.Core.SocialEffectScope]::Prompt($case.Text,$history,$case.ScopeKind)) `
        ([PeopleOfTheIliacBay.Core.SocialEffectScope]::Grammar)
    $scopeOnly=[PeopleOfTheIliacBay.Core.SocialEffectScope]::Interpret($scopeOnly)
    if($scopeOnly -eq $case.ScopeExpected){
        $scopePass++
        Write-Output ('PASS scope expected='+$case.ScopeExpected+' actual='+$scopeOnly+' kind='+$case.ScopeKind+' text='+$case.Text)
    }else{
        $failure='scope expected='+$case.ScopeExpected+' actual='+$scopeOnly+' kind='+$case.ScopeKind+' text='+$case.Text
        $failures.Add($failure);Write-Output ('FAIL '+$failure)
    }
}
Write-Output ('Social effect pipeline: '+$pipelinePass+'/'+$cases.Count+' passed; forced scope holdouts: '+$scopePass+'/'+$cases.Count+' passed. Model-only; no native effect, pause, attendance, or memory was executed.')
if($failures.Count){throw ('Social effect scope holdouts failed: '+$failures.Count)}
