param([string]$Endpoint='http://127.0.0.1:8080/completion')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/PeopleOfTheIliacBay.Core.csproj') -c Release --nologo | Out-Null
if($LASTEXITCODE -ne 0){throw 'Core build failed.'}
Add-Type -Path (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/bin/Release/netstandard2.0/PeopleOfTheIliacBay.Core.dll')
$connection=[PeopleOfTheIliacBay.Core.LocalDialogueConnection]::new($Endpoint,10000)
$topics=[System.Collections.Generic.List[PeopleOfTheIliacBay.Core.ConversationTopic]]::new()
$topics.Add([PeopleOfTheIliacBay.Core.ConversationTopic]::new(42,'The Copper Kettle','Taverns / The Copper Kettle',$false))
$failures=[System.Collections.Generic.List[string]]::new()
$cases=@(
    @{Text='I need to get back to the barracks before nightfall.';Social='busy_pause'},
    @{Text='An urgent matter came up; I should get back to work.';Social='busy_pause'},
    @{Text='Sorry, I am bussy. I must be off.';Social='busy_pause'},
    @{Text='Ich muss jetzt weiter, wir reden später.';Social='busy_pause'},
    @{Text='I would love to stay, but my duties cannot wait. Farewell.';Social='busy_pause'},
    @{Text='I am not busy; I can stay and talk.';Social='none'},
    @{Text='Do I have to leave?';Social='none'},
    @{Text='He said, "I have to go now."';Social='none'},
    @{Text='Tomorrow I will be busy, but I can talk now.';Social='none'},
    @{Text='If I had to leave, would you be upset?';Social='none'},
    @{Text='If I say I am busy next week, I will need to leave then.';Social='none'},
    @{Text='Could you leave me alone for a while?';Social='busy_pause'},
    @{Text='Your wife will not live to see another dawn. I will see to that.';Social='family_threat'},
    @{Text='I will not hurt your family. They are safe with me.';Social='none'},
    @{Text='Someone threatened your family. I want to help protect them.';Social='none'},
    @{Text='He said, "I will kill your son." What should we do?';Social='none'},
    @{Text='Would you mind accompanying me for a little while?';Social='none';Intent='action'},
    @{Text='Before I leave, come along with me for a few minutes.';Social='none';Intent='action'},
    @{Text='Please remain where you are until I return.';Social='none';Intent='action'},
    @{Text='Tomorrow, 16:30 at the Copper Kettle: shall we spend an hour together?';Social='none';Intent='appointment'},
    @{Text='We should scrap our arrangement for tomorrow.';Social='none';Intent='appointment'},
    @{Text='Put a little cross on my chart where the Copper Kettle stands.';Social='none';Intent='map'}
)
foreach($case in $cases){
    $prompt=[PeopleOfTheIliacBay.Core.ConversationKnowledge]::Prompt($case.Text,$topics,-1,'Player: Yesterday I had to go. NPC: We can talk today.')
    $completion=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Classify($connection,$prompt,'',[PeopleOfTheIliacBay.Core.ConversationKnowledge]::Grammar($topics))
    $payload=$completion.JsonBody|ConvertFrom-Json
    $payload|Add-Member -NotePropertyName seed -NotePropertyValue 47 -Force
    $response=Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json' -Body ($payload|ConvertTo-Json -Depth 12 -Compress) -TimeoutSec 30
    $raw=([string]$response.content).Trim()
    $choice=[PeopleOfTheIliacBay.Core.ConversationKnowledge]::Parse($raw,$topics,$case.Text,-1)
    if([PeopleOfTheIliacBay.Core.SocialEffectScope]::Required($choice)){
        $scopePrompt=[PeopleOfTheIliacBay.Core.SocialEffectScope]::Prompt($case.Text,'Player: Yesterday I had to go. NPC: We can talk today.',$choice.SocialIntent)
        $scopeCompletion=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Classify($connection,$scopePrompt,'',[PeopleOfTheIliacBay.Core.SocialEffectScope]::Grammar)
        $scopePayload=$scopeCompletion.JsonBody|ConvertFrom-Json
        $scopePayload|Add-Member -NotePropertyName seed -NotePropertyValue 47 -Force
        $scopeResponse=Invoke-RestMethod -Uri $scopeCompletion.Endpoint -Method Post -ContentType 'application/json' -Body ($scopePayload|ConvertTo-Json -Depth 12 -Compress) -TimeoutSec 30
        $scope=([string]$scopeResponse.content).Trim()
        if(-not [PeopleOfTheIliacBay.Core.SocialEffectScope]::Valid($scope)){throw "Invalid scope output: $scope"}
        $choice=[PeopleOfTheIliacBay.Core.SocialEffectScope]::Apply($choice,$scope)
        $raw+='; scope='+$scope
    }
    if(-not $choice -or $choice.SocialIntent -ne $case.Social -or ($case.Intent -and $choice.Intent -ne $case.Intent)){
        $failure=$case.Text+' => '+$raw+' expected '+$case.Intent+'/'+$case.Social
        $failures.Add($failure);Write-Output ('FAIL '+$failure)
    }else{Write-Output ('PASS '+$case.Text+' => '+$raw)}
}
Write-Output ('Semantic classifier model fixtures: '+($cases.Count-$failures.Count)+'/'+$cases.Count+' passed. Model-only; does not prove native execution.')
if($failures.Count){throw ('Semantic fixtures failed: '+$failures.Count)}
