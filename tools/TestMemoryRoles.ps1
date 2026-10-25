param([string]$Endpoint='http://127.0.0.1:8080/completion')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/PeopleOfTheIliacBay.Core.csproj') -c Release --nologo | Out-Null
if($LASTEXITCODE -ne 0){throw 'Core build failed.'}
Add-Type -Path (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/bin/Release/netstandard2.0/PeopleOfTheIliacBay.Core.dll')
$memory=[PeopleOfTheIliacBay.Core.WorldStateStore]::new(42)
$memory.RememberConversation('isabella',100,'6 Morning Star 405','The Black Wolf Inn','My brother is named Marius. Do you have a brother?','My brother is Daric. He works in a bakery.','model')
$history="Player: My brother is named Marius. Do you have a brother?`nNPC: My brother is Daric. He works in a bakery.`n"
for($turn=0;$turn -lt 90;$turn++){
    $question='I saw bird number '+$turn+' near the town wall today. It flew toward the inn.'
    $answer='That sounds pleasant. I hope it found somewhere warm to rest in this cold weather.'
    $memory.RememberConversation('isabella',200+$turn,'6 Morning Star 405','The Black Wolf Inn',$question,$answer,'model')
    $history+="Player: $question`nNPC: $answer`n"
}
$connection=[PeopleOfTheIliacBay.Core.LocalDialogueConnection]::new($Endpoint,10000)
$baseUri=[Uri]::new($Endpoint)
$properties=Invoke-RestMethod -Uri ([Uri]::new($baseUri,'/props'))
$contextSize=[int]$properties.default_generation_settings.n_ctx
if($contextSize -lt 1024){throw 'Server context size unavailable.'}
$tokenCounter=[Func[string,int]]{
    param([string]$text)
    $body=@{content=$text;add_special=$true} | ConvertTo-Json -Compress
    $result=Invoke-RestMethod -Uri ([Uri]::new($baseUri,'/tokenize')) -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 30
    return [int]$result.tokens.Count
}
foreach($scenario in @(
    @{Query='What was my brother called?';Expected='Marius';Other='Daric'},
    @{Query='What was your brother called?';Expected='Daric';Other='Marius'}
)){
    $context='You are Isabella. The player is Neris. Current place is The Black Wolf Inn. '+$memory.RecallConversation('isabella',$scenario.Query,400,2000)
    $request=[PeopleOfTheIliacBay.Core.FreeTextDialogue]::Create($scenario.Query,'Isabella','Breton','Female','local patron',60,$context,$history)
    $completion=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Build($connection,$request)
    $fitted=[PeopleOfTheIliacBay.Core.DialogueHistoryWindow]::Fit($completion.Prompt,$history,$contextSize-$completion.ResponseTokens-32,$tokenCounter,$true)
    if(-not $fitted -or $fitted.Length -ge $completion.Prompt.Length){throw 'Fixture did not exercise history trimming.'}
    if(-not $fitted.Contains('Marius') -or -not $fitted.Contains('Daric')){throw 'Retrieved old speaker facts were lost while trimming history.'}
    $payload=$completion.JsonBody | ConvertFrom-Json
    $payload.prompt=$fitted
    $payload | Add-Member -NotePropertyName seed -NotePropertyValue 47 -Force
    $response=Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json' -Body ($payload | ConvertTo-Json -Depth 12 -Compress) -TimeoutSec 30
    $reply=[string]$response.content
    $expectedAt=$reply.IndexOf($scenario.Expected,[StringComparison]::OrdinalIgnoreCase)
    $otherAt=$reply.IndexOf($scenario.Other,[StringComparison]::OrdinalIgnoreCase)
    if($expectedAt -lt 0 -or ($otherAt -ge 0 -and $otherAt -lt $expectedAt)){throw ('Speaker attribution requires review: '+$scenario.Query+' / '+$reply)}
    Write-Output ('PASS trimmed long-chat role recall: '+$scenario.Query+' / '+$reply.Trim())
}
