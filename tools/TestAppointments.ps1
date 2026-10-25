param([string]$Endpoint='http://127.0.0.1:8080/completion')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/PeopleOfTheIliacBay.Core.csproj') -c Release --nologo | Out-Null
if($LASTEXITCODE -ne 0){throw 'Core build failed.'}
Add-Type -Path (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/bin/Release/netstandard2.0/PeopleOfTheIliacBay.Core.dll')
$connection=[PeopleOfTheIliacBay.Core.LocalDialogueConnection]::new($Endpoint,10000)
$places=[System.Collections.Generic.List[PeopleOfTheIliacBay.Core.ConversationTopic]]::new()
$places.Add([PeopleOfTheIliacBay.Core.ConversationTopic]::new(42,'The Black Wolf Inn','Known local inn', $false))
$context='Current game time: 17:39. Disposition +75; current mood cheerful, familiar with player. NPC already wants another meeting and is settling its details. The current building is The Black Wolf Inn, buildingkey=42. Tomorrow has no travel conflicts. No booking has been committed.'
$history="Player: I would like to have another drink with you tomorrow.`nNPC: I would like that. When shall we meet?"
$query='Could we meet tomorrow at the same time here?'
$prompt=[PeopleOfTheIliacBay.Core.AppointmentChat]::Prompt($query,$history,$context,$places,1059)
$completion=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Classify($connection,$prompt,$history,[PeopleOfTheIliacBay.Core.AppointmentChat]::GrammarFor($query,$places,1059))
$payload=$completion.JsonBody | ConvertFrom-Json
$payload | Add-Member -NotePropertyName seed -NotePropertyValue 47 -Force
$response=Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json' -Body ($payload | ConvertTo-Json -Depth 12 -Compress) -TimeoutSec 30
$choice=[PeopleOfTheIliacBay.Core.AppointmentChat]::Parse($response.content,$places,$query,1059)
if(-not $choice -or $choice.Kind -ne 'BOOK' -or $choice.DayOffset -ne 1 -or $choice.MinuteOfDay -ne 1059 -or $choice.BuildingKey -ne 42){
    throw ('Known willing NPC failed grounded tomorrow/same-time/here extraction: '+$response.content)
}
Write-Output ('PASS willing appointment extraction: '+$response.content.Trim())
$query='Could we meet tomorrow at 16:30 at The Black Wolf Inn?'
$prompt=[PeopleOfTheIliacBay.Core.AppointmentChat]::Prompt($query,$history,$context,$places,1059)
$completion=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Classify($connection,$prompt,$history,[PeopleOfTheIliacBay.Core.AppointmentChat]::GrammarFor($query,$places,1059))
$response=Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json' -Body $completion.JsonBody -TimeoutSec 30
$choice=[PeopleOfTheIliacBay.Core.AppointmentChat]::Parse($response.content,$places,$query,1059)
if(-not $choice -or $choice.Kind -ne 'BOOK' -or $choice.DayOffset -ne 1 -or $choice.MinuteOfDay -ne 990 -or $choice.BuildingKey -ne 42){
    throw ('Explicit appointment time was changed: '+$response.content)
}
Write-Output ('PASS exact 16:30 appointment extraction: '+$response.content.Trim())
$query='Could we meet here sometime?'
$prompt=[PeopleOfTheIliacBay.Core.AppointmentChat]::Prompt($query,'',$context,$places,1059)
$completion=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Classify($connection,$prompt,'',[PeopleOfTheIliacBay.Core.AppointmentChat]::GrammarFor($query,$places,1059))
$response=Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json' -Body $completion.JsonBody -TimeoutSec 30
$choice=[PeopleOfTheIliacBay.Core.AppointmentChat]::Parse($response.content,$places,$query,1059)
if(-not $choice -or $choice.Kind -ne 'CLARIFY'){throw ('Missing day/time invented a booking: '+$response.content)}
Write-Output ('PASS missing details: '+$response.content.Trim())
