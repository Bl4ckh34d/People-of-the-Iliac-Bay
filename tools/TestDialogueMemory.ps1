param([string]$Endpoint='http://127.0.0.1:8080/completion')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $repo 'src/LivingCities.Core/LivingCities.Core.csproj') -c Release --nologo | Out-Null
if($LASTEXITCODE -ne 0){throw 'Core build failed.'}
Add-Type -Path (Join-Path $repo 'src/LivingCities.Core/bin/Release/netstandard2.0/LivingCities.Core.dll')
$store=[LivingCities.Core.WorldStateStore]::new(42)
$store.RememberConversation('alice',100,'6 Morning Star 405','The Black Wolf Inn, Daggerfall','My sister is named Mirabelle.','I hope she is well.','model')
for($i=0;$i -lt 80;$i++){
    $store.RememberConversation('alice',200+$i,'7 Morning Star 405','Daggerfall','I saw bird number '+$i,'Interesting.','model')
}
$copy=[LivingCities.Core.WorldStateStore]::new(7)
$copy.Restore($store.Snapshot())
$connection=[LivingCities.Core.LocalDialogueConnection]::new($Endpoint,10000)
$query='What was my sister''s name, from our earlier conversation?'
$context=$copy.RecallConversation('alice',$query,86500,2000)
$request=[LivingCities.Core.FreeTextDialogue]::Create($query,'Alice','Breton','Female','resident',30,$context,'')
$completion=[LivingCities.Core.LocalDialogueCompletion]::Build($connection,$request)
$response=Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json' -Body $completion.JsonBody -TimeoutSec 30
if($response.content -notmatch 'Mirabelle'){throw ('Local model failed saved recall: '+$response.content)}
Write-Output ('PASS saved old memory: '+$response.content.Trim())
$context=$copy.RecallConversation('bob',$query,86500,2000)
$request=[LivingCities.Core.FreeTextDialogue]::Create($query,'Bob','Breton','Male','resident',30,$context,'')
$completion=[LivingCities.Core.LocalDialogueCompletion]::Build($connection,$request)
$response=Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json' -Body $completion.JsonBody -TimeoutSec 30
if($response.content -match 'Mirabelle' -or $response.content -notmatch "(?i)remind|don.t (know|recall|remember)|do not (know|recall|remember)|not sure|can.t (recall|remember)|cannot (recall|remember)|haven.t"){
    throw ('Local model invented missing personal history: '+$response.content)
}
Write-Output ('PASS different NPC: '+$response.content.Trim())
