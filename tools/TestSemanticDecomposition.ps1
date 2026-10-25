param([string]$Endpoint='http://127.0.0.1:8080/completion')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/PeopleOfTheIliacBay.Core.csproj') -c Release --nologo | Out-Null
if($LASTEXITCODE -ne 0){throw 'Core build failed.'}
Add-Type -Path (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/bin/Release/netstandard2.0/PeopleOfTheIliacBay.Core.dll')
$connection=[PeopleOfTheIliacBay.Core.LocalDialogueConnection]::new($Endpoint,10000)
$context='The NPC can follow the player, purchase two arrows from the current local shop, move to the offered corner or north gate, wait at a reached point, and transfer owned goods to the player. Available task candidates include Follow(player), MoveTo(corner), Wait(60 seconds at reached target), and MoveTo(player). No action has been accepted yet. Interpret only the current player message.'
$shopHandoverContext='The NPC can purchase one Steel Tanto from the current local shop and transfer that exact acquired item to the player. The purchase and delivery are distinct supported actions. No task has been accepted yet. Interpret only the current player message.'
$emoji=[char]::ConvertFromUtf32(0x1F600)
$cases=@(
    @{Text='Please wait here for 10 seconds.';Kind='Single';Steps=1},
    @{Text='Please follow me for 60 seconds.';Kind='Single';Steps=1},
    @{Text='Could you give me one arrow?';Kind='Single';Steps=1},
    @{Text='Take one arrow from me, as a gift.';Kind='Single';Steps=1},
    @{Text='Wait here for 10 seconds, then follow me for 10 seconds.';Kind='Chain';Steps=2;Starts=@()},
    @{Text='Buy two arrows then give them to me.';Kind='Chain';Steps=2;Starts=@('give')},
    @{Text=('Buy two arrows'+$emoji+', then give them to me.');Kind='Chain';Steps=2;Starts=@('give')},
    @{Text='Buy two arrows, then wait by the gate, and finally give them to me.';Kind='Chain';Steps=3;Starts=@('wait','give')},
    @{Text='Go to the corner, wait there for one minute, then return to me.';Kind='Chain';Steps=3;Starts=@('wait there for one minute','return to me')},
    @{Text='Could you buy one Steel Tanto, then give it to me?';Context=$shopHandoverContext;Kind='Chain';Steps=2;Starts=@('give it to me')},
    @{Text='Would you buy one Steel Tanto and hand it over to me afterwards?';Context=$shopHandoverContext;Kind='Chain';Steps=2;Starts=@()},
    @{Text='He said, "Buy arrows then give them to me," but I am only telling you what he said.';Kind='None';Steps=0}
)
$failures=[System.Collections.Generic.List[string]]::new()
foreach($case in $cases){
    $caseContext=if($case.ContainsKey('Context')){$case.Context}else{$context}
    $prompt=[PeopleOfTheIliacBay.Core.NpcActionDecomposition]::Prompt($case.Text,$caseContext)
    $completion=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Classify($connection,$prompt,'',[PeopleOfTheIliacBay.Core.NpcActionDecomposition]::Grammar(),192)
    $payload=$completion.JsonBody|ConvertFrom-Json
    # Use the selected server model's native template, like the game client.
    $messages=@(@{role='system';content="You classify game dialogue. Return only the requested classification, never an answer or instructions.`n"},
        @{role='user';content=$prompt.Replace('<|','< |')+"`n"})
    $rendered=Invoke-RestMethod -Uri (([Uri]::new([Uri]$Endpoint,'/apply-template').AbsoluteUri)) -Method Post -ContentType 'application/json' `
        -Body (@{messages=$messages;add_generation_prompt=$true;chat_template_kwargs=@{enable_thinking=$false}}|ConvertTo-Json -Depth 8 -Compress)
    $payload.prompt=$rendered.prompt
    $payload.stop=@($payload.stop|Where-Object {$_ -notin @('<|im_start|>','<|im_end|>')})
    $payload|Add-Member -NotePropertyName seed -NotePropertyValue 47 -Force
    $response=Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json; charset=utf-8' -Body ($payload|ConvertTo-Json -Depth 12 -Compress) -TimeoutSec 45
    $raw=([string]$response.content).Trim()
    $parsed=[PeopleOfTheIliacBay.Core.NpcActionDecomposition]::Parse($raw,$case.Text)
    $ok=$parsed -and $parsed.Kind.ToString() -eq $case.Kind -and $parsed.Clauses.Count -eq $case.Steps
    if($ok -and $case.Kind -eq 'Chain'){
        $rebuilt=[string]::Concat([string[]]$parsed.Clauses)
        $ok=$rebuilt -ceq $case.Text
        for($i=0;$ok -and $i -lt $case.Starts.Count;$i++){
            $ok=$parsed.Clauses[$i+1].TrimStart().StartsWith($case.Starts[$i],[System.StringComparison]::OrdinalIgnoreCase)
        }
    }
    if(-not $ok){
        $failure=$case.Text+' => '+$raw+' expected '+$case.Kind+'/'+$case.Steps
        $failures.Add($failure);Write-Output ('FAIL '+$failure)
    }else{
        Write-Output ('PASS local-model decomposition '+$case.Kind+'/'+$case.Steps+': '+$raw)
        if($parsed.Kind -eq [PeopleOfTheIliacBay.Core.NpcActionDecompositionKind]::Chain){
            for($i=0;$i -lt $parsed.Clauses.Count;$i++){Write-Output ('  span '+($i+1)+': ['+$parsed.Clauses[$i]+']')}
        }
    }
}
Write-Output ('Local-model decomposition fixtures: '+($cases.Count-$failures.Count)+'/'+$cases.Count+' passed. This tests model span output, not native task acceptance/execution.')
if($failures.Count){throw ('Semantic decomposition fixtures failed: '+$failures.Count)}

function New-ActionTarget([PeopleOfTheIliacBay.Core.NpcActionTargetKind]$Kind,[string]$ItemId,[int]$Quantity=1) {
    $target=[PeopleOfTheIliacBay.Core.NpcActionTarget]::new()
    $target.Kind=$Kind;$target.ItemId=$ItemId;$target.PersonId='player';$target.Quantity=$Quantity
    return $target
}
function New-HandoverCategoryCatalog([string]$ItemName,[bool]$IncludeDependency=$true) {
    $options=[System.Collections.Generic.List[PeopleOfTheIliacBay.Core.NpcActionOption]]::new()
    if($IncludeDependency) {
        $handover=[PeopleOfTheIliacBay.Core.NpcActionOption]::new()
        $handover.Id=1;$handover.Kind=[PeopleOfTheIliacBay.Core.NpcActionKind]::Transfer;$handover.ItemSourceStep=1
        $handover.Label='hand over the exact '+$ItemName+' acquired by the previous step'
        $handover.Target=New-ActionTarget ([PeopleOfTheIliacBay.Core.NpcActionTargetKind]::Item) 'native-item-uid' 1
        $options.Add($handover)
    }
    $follow=[PeopleOfTheIliacBay.Core.NpcActionOption]::new();$follow.Id=2;$follow.Kind=[PeopleOfTheIliacBay.Core.NpcActionKind]::Follow
    $follow.Label='follow the player';$follow.Target=New-ActionTarget ([PeopleOfTheIliacBay.Core.NpcActionTargetKind]::Person) $null; $options.Add($follow)
    $wait=[PeopleOfTheIliacBay.Core.NpcActionOption]::new();$wait.Id=3;$wait.Kind=[PeopleOfTheIliacBay.Core.NpcActionKind]::Wait
    $wait.Label='wait at the player';$wait.Target=New-ActionTarget ([PeopleOfTheIliacBay.Core.NpcActionTargetKind]::WorldPoint) $null; $options.Add($wait)
    $move=[PeopleOfTheIliacBay.Core.NpcActionOption]::new();$move.Id=4;$move.Kind=[PeopleOfTheIliacBay.Core.NpcActionKind]::MoveTo
    $move.Label='walk to the player';$move.Target=New-ActionTarget ([PeopleOfTheIliacBay.Core.NpcActionTargetKind]::Person) $null; $options.Add($move)
    foreach($id in 5,6) {
        $interact=[PeopleOfTheIliacBay.Core.NpcActionOption]::new();$interact.Id=$id;$interact.Kind=[PeopleOfTheIliacBay.Core.NpcActionKind]::Interact
        $interact.Label='interact with '+$id;$interact.Target=New-ActionTarget ([PeopleOfTheIliacBay.Core.NpcActionTargetKind]::Person) ('person-'+$id); $options.Add($interact)
    }
    for($id=7;$id -le 38;$id++) {
        $purchase=[PeopleOfTheIliacBay.Core.NpcActionOption]::new();$purchase.Id=$id;$purchase.Kind=[PeopleOfTheIliacBay.Core.NpcActionKind]::Purchase
        $purchase.Label='buy '+$ItemName+' from this verified shop';$purchase.Target=New-ActionTarget ([PeopleOfTheIliacBay.Core.NpcActionTargetKind]::Item) ('stock-'+$id); $options.Add($purchase)
    }
    $receive=[PeopleOfTheIliacBay.Core.NpcActionOption]::new();$receive.Id=39;$receive.Kind=[PeopleOfTheIliacBay.Core.NpcActionKind]::Transfer
    $receive.Label='receive an item from the player';$receive.Target=New-ActionTarget ([PeopleOfTheIliacBay.Core.NpcActionTargetKind]::Item) 'player-item'
    $receive.Target.TransferDirection=[PeopleOfTheIliacBay.Core.NpcActionTransferDirection]::PlayerToNpc;$options.Add($receive)
    $purchaseStep=[PeopleOfTheIliacBay.Core.NpcActionOption]::new();$purchaseStep.Id=7;$purchaseStep.Kind=[PeopleOfTheIliacBay.Core.NpcActionKind]::Purchase
    $purchaseStep.Label='buy one '+$ItemName+' from this verified shop stock with my own wallet; one-item quoted price=27 gold; current wallet=100000 gold'
    $purchaseStep.Target=New-ActionTarget ([PeopleOfTheIliacBay.Core.NpcActionTargetKind]::Item) 'native-item-uid' 1
    $previous=[PeopleOfTheIliacBay.Core.NpcActionDecision]::new();$previous.Option=$purchaseStep;$previous.DurationSeconds=300
    return @{Catalog=[PeopleOfTheIliacBay.Core.NpcActionCandidateCatalog]::new($options);Previous=$previous}
}
$categoryCases=@(
    @{Text='Could you buy one Steel Tanto, then ';Item='Steel Tanto';ExpectedKind='Purchase';Expected='GROUP|5';SourceStep=-1;Previous=$false;IncludeDependency=$false;Original='Could you buy one Steel Tanto, then give it to me?'},
    @{Text='give it to me?';Item='Steel Tanto';Expected='GROUP|1';SourceStep=1;Previous=$true;Original='Could you buy one Steel Tanto, then give it to me?'},
    @{Text='give it to me';Item='Steel Tanto';Expected='GROUP|1';SourceStep=1;Previous=$true;Original='Buy one Steel Tanto, then give it to me.'},
    @{Text='give the Steel Tanto to me?';Item='Steel Tanto';Expected='GROUP|1';SourceStep=1;Previous=$true;Original='Could you buy one Steel Tanto, then give me the Steel Tanto?'},
    @{Text='Could you give it to me?';Item='Steel Tanto';Expected='GROUP|1';SourceStep=1;Previous=$true;Original='Buy one Steel Tanto, then could you give it to me?'},
    @{Text='hand the Iron Mace to me';Item='Iron Mace';Expected='GROUP|1';SourceStep=1;Previous=$true;Original='Buy an Iron Mace, then hand the Iron Mace to me.'},
    @{Text='He said, "give it to me?" but I am only reporting what he said.';Item='Steel Tanto';Expected='NONE';SourceStep=0;Previous=$true;Original='Could you buy one Steel Tanto, then give it to me?'},
    @{Text='I do not want you to give it to me.';Item='Steel Tanto';Expected='NONE';SourceStep=0;Previous=$true;Original='Could you buy one Steel Tanto, then give it to me?'}
)
$categoryFailures=[System.Collections.Generic.List[string]]::new()
foreach($case in $categoryCases) {
    $includeDependency=$true;if($case.ContainsKey('IncludeDependency')){$includeDependency=[bool]$case.IncludeDependency}
    $fixture=New-HandoverCategoryCatalog $case.Item $includeDependency
    $previous=$null;if($case.Previous){$previous=$fixture.Previous}
    $prompt=$fixture.Catalog.Prompt($case.Text,$previous,$case.Original)
    $completion=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Classify($connection,$prompt,'',$fixture.Catalog.Grammar(),64)
    $payload=$completion.JsonBody|ConvertFrom-Json;$payload|Add-Member -NotePropertyName seed -NotePropertyValue 47 -Force
    $response=Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json; charset=utf-8' -Body ($payload|ConvertTo-Json -Depth 12 -Compress) -TimeoutSec 45
    $raw=([string]$response.content).Trim();$selected=$fixture.Catalog.Select($raw)
    $ok=$raw -ceq $case.Expected
    if($case.ExpectedKind){$ok=$selected -and $selected.Count -gt 0 -and $selected[0].Kind -eq [Enum]::Parse([PeopleOfTheIliacBay.Core.NpcActionKind],[string]$case.ExpectedKind)}
    if($case.SourceStep -gt 0){$ok=$ok -and $selected -and $selected.Count -gt 0 -and $selected[0].ItemSourceStep -eq 1}
    if($case.SourceStep -eq 0){$ok=$ok -and $selected -eq $null}
    if(-not $ok){$categoryFailures.Add($case.Text+' => '+$raw+' expected '+$case.Expected);Write-Output ('FAIL category dependency '+$categoryFailures[$categoryFailures.Count-1])}
    else{Write-Output ('PASS category dependency '+$case.Text+': '+$raw)}
}
Write-Output ('Local-model planned dependency category fixtures: '+($categoryCases.Count-$categoryFailures.Count)+'/'+$categoryCases.Count+' passed. This tests retrieval framing, not NPC willingness or task execution.')
if($categoryFailures.Count){throw ('Semantic category dependency fixtures failed: '+$categoryFailures.Count)}

$waitOptions=[System.Collections.Generic.List[PeopleOfTheIliacBay.Core.NpcActionOption]]::new()
$wait=[PeopleOfTheIliacBay.Core.NpcActionOption]::new();$wait.Id=51;$wait.Kind=[PeopleOfTheIliacBay.Core.NpcActionKind]::Wait
$wait.Label='wait at the exact position reached by the previous MoveTo step';$wait.Target=New-ActionTarget ([PeopleOfTheIliacBay.Core.NpcActionTargetKind]::WorldPoint) $null;$waitOptions.Add($wait)
$otherMove=[PeopleOfTheIliacBay.Core.NpcActionOption]::new();$otherMove.Id=52;$otherMove.Kind=[PeopleOfTheIliacBay.Core.NpcActionKind]::MoveTo
$otherMove.Label='walk to the offered north gate';$otherMove.Target=New-ActionTarget ([PeopleOfTheIliacBay.Core.NpcActionTargetKind]::WorldPoint) $null;$waitOptions.Add($otherMove)
$previousMove=[PeopleOfTheIliacBay.Core.NpcActionDecision]::new();$previousMove.Option=$otherMove;$previousMove.DurationSeconds=300
$waitCatalog=[PeopleOfTheIliacBay.Core.NpcActionCandidateCatalog]::new($waitOptions)
$waitText='wait there for one minute'
$waitOriginal='Go to the corner, wait there for one minute, then return to me.'
$waitPrompt=$waitCatalog.Prompt($waitText,$previousMove,$waitOriginal)
$waitCompletion=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Classify($connection,$waitPrompt,'',$waitCatalog.Grammar(),64)
$waitPayload=$waitCompletion.JsonBody|ConvertFrom-Json;$waitPayload|Add-Member -NotePropertyName seed -NotePropertyValue 47 -Force
$waitResponse=Invoke-RestMethod -Uri $waitCompletion.Endpoint -Method Post -ContentType 'application/json; charset=utf-8' -Body ($waitPayload|ConvertTo-Json -Depth 12 -Compress) -TimeoutSec 45
$waitRaw=([string]$waitResponse.content).Trim();$waitSelected=$waitCatalog.Select($waitRaw)
if($waitRaw -cne 'GROUP|1' -or -not $waitSelected -or $waitSelected.Count -ne 1 -or $waitSelected[0].Kind -ne [PeopleOfTheIliacBay.Core.NpcActionKind]::Wait) {
    throw ('Independent MoveTo/Wait retrieval control failed: '+$waitRaw)
}
Write-Output ('PASS independent MoveTo/Wait chain step: '+$waitRaw)
