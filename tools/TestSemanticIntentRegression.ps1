param([string]$Endpoint='http://127.0.0.1:8080/completion')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/PeopleOfTheIliacBay.Core.csproj') -c Release --nologo | Out-Null
if($LASTEXITCODE -ne 0){throw 'Core build failed.'}
Add-Type -Path (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/bin/Release/netstandard2.0/PeopleOfTheIliacBay.Core.dll')
$connection=[PeopleOfTheIliacBay.Core.LocalDialogueConnection]::new($Endpoint,10000)
$cases=@(
    @{Text='Please take this Potion of Cure Poison as a gift from me.';Intent='action';Social='none'},
    @{Text='I want you to have this healing potion.';Intent='action';Social='none'},
    @{Text='May I give you this antidote?';Intent='action';Social='none'},
    @{Text='I brought you a gift. Will you accept it?';Intent='action';Social='none'},
    @{Text='I gave you a potion yesterday.';Intent='social';Social='none'},
    @{Text='I might give you a gift tomorrow.';Intent='social';Social='none'},
    @{Text='Thank you for the potion you gave me.';Intent='social';Social='none'},
    @{Text='She said "Please accept this potion." I am only quoting her.';Intent='social';Social='none'},
    @{Text='Thanks for your time. I really need to get home now.';Intent='social';Social='busy_pause'},
    @{Text='I have to get back to the barracks before nightfall.';Intent='social';Social='busy_pause'},
    @{Text='I cannot stay and talk any longer; I need to go.';Intent='social';Social='busy_pause'},
    @{Text='My work is busy tomorrow, but I have time to talk today.';Intent='social';Social='none'},
    @{Text='I had to leave early last week because I was ill.';Intent='social';Social='none'},
    @{Text='Would you be upset if I had to leave early?';Intent='social';Social='none'},
    @{Text='My sister said she needs to leave now. I can stay.';Intent='social';Social='none'},
    @{Text='I want your daughter to suffer for what you did.';Intent='social';Social='family_threat'},
    @{Text='My friend said your son should suffer. I disagree.';Intent='social';Social='none'},
    @{Text='I do not want your child harmed. I came to warn you.';Intent='social';Social='none'},
    @{Text='How has your morning been?';Intent='social';Social='none';History='Player: I will kill your wife. NPC: Please keep my family safe.'},
    @{Text='Come with me for a few minutes.';Intent='action';Social='none';History='Player: I must leave now. NPC: Understood.'}
)
$failures=[System.Collections.Generic.List[string]]::new()
foreach($case in $cases){
    $history=if($case.History){$case.History}else{'Player: Yesterday I had to leave. NPC: We can talk today.'}
    $prompt=[PeopleOfTheIliacBay.Core.ConversationKnowledge]::RoutingPrompt($case.Text,$history)
    $completion=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Classify($connection,$prompt,'',[PeopleOfTheIliacBay.Core.ConversationKnowledge]::RoutingGrammar())
    $payload=$completion.JsonBody|ConvertFrom-Json
    $messages=@(@{role='system';content="You classify game dialogue. Return only the requested classification, never an answer or instructions.`n"},
        @{role='user';content=$prompt.Replace('<|','< |')+"`n"})
    $rendered=Invoke-RestMethod -Uri (([Uri]::new([Uri]$Endpoint,'/apply-template').AbsoluteUri)) -Method Post -ContentType 'application/json' `
        -Body (@{messages=$messages;add_generation_prompt=$true;chat_template_kwargs=@{enable_thinking=$false}}|ConvertTo-Json -Depth 8 -Compress)
    $payload.prompt=$rendered.prompt
    $payload.stop=@($payload.stop|Where-Object {$_ -notin @('<|im_start|>','<|im_end|>')})
    $payload|Add-Member -NotePropertyName seed -NotePropertyValue 47 -Force
    $response=Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json' -Body ($payload|ConvertTo-Json -Depth 12 -Compress) -TimeoutSec 35
    $raw=([string]$response.content).Trim()
    $choice=[PeopleOfTheIliacBay.Core.ConversationKnowledge]::ParseRouting($raw)
    $ok=$choice -and $choice.Intent -eq $case.Intent -and $choice.SocialIntent -eq $case.Social
    if(-not $ok){$failure=$case.Text+' => '+$raw+' expected '+$case.Intent+'/'+$case.Social;$failures.Add($failure);Write-Output ('FAIL '+$failure)}
    else{Write-Output ('PASS '+$case.Text+' => '+$raw)}
}
Write-Output ('Semantic-intent holdouts: '+($cases.Count-$failures.Count)+'/'+$cases.Count+' passed. Model-only; no native action or pause was executed.')
if($failures.Count){throw ('Semantic-intent holdouts failed: '+$failures.Count)}
