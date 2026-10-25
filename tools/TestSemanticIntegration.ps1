param([string]$Endpoint='http://127.0.0.1:8080/completion')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
dotnet build "$repo/src/PeopleOfTheIliacBay.Core/PeopleOfTheIliacBay.Core.csproj" -c Release --nologo | Out-Null
Add-Type -Path "$repo/src/PeopleOfTheIliacBay.Core/bin/Release/netstandard2.0/PeopleOfTheIliacBay.Core.dll"
$c=[PeopleOfTheIliacBay.Core.LocalDialogueConnection]::new($Endpoint,10000)
function Ask($prompt,$history,$grammar){
 $r=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Classify($c,$prompt,$history,$grammar)
 $body=$r.JsonBody|ConvertFrom-Json;$body|Add-Member seed 47 -Force
 ([string](Invoke-RestMethod -Uri $Endpoint -Method Post -ContentType 'application/json' -Body ($body|ConvertTo-Json -Compress -Depth 9) -TimeoutSec 30).content).Trim()
}
$options=[System.Collections.Generic.List[PeopleOfTheIliacBay.Core.NpcActionOption]]::new()
foreach($i in 1..4){$o=[PeopleOfTheIliacBay.Core.NpcActionOption]::new();$o.Id=$i;$o.Kind='MoveTo';$o.Label=@('walk to North Mill','walk to Amber Hall','walk to Red Keep','walk to Willow Library')[$i-1];$o.Target=[PeopleOfTheIliacBay.Core.NpcActionTarget]::new();$o.Target.Kind='Building';$o.Target.SettlementId=1;$o.Target.BuildingKey=$i;$options.Add($o)}
foreach($i in 1..3){$o=[PeopleOfTheIliacBay.Core.NpcActionOption]::new();$o.Id=10+$i;$o.Kind='Transfer';$o.Label='give the player '+@('iron dagger','linen shirt','silver goblet')[$i-1]+' from my possessions';$o.Target=[PeopleOfTheIliacBay.Core.NpcActionTarget]::new();$o.Target.Kind='Person';$o.Target.PersonId='player';$o.Target.ItemId=[string]$i;$options.Add($o)}
$catalog=[PeopleOfTheIliacBay.Core.NpcActionCandidateCatalog]::new($options)
$cases=@(@{Text='Would you head over to Willow Library?';Id=4},@{Text='May I have that silver goblet you carry?';Id=13})
$failed=0
foreach($case in $cases){$group=Ask $catalog.Prompt($case.Text) '' $catalog.Grammar();$selected=$catalog.Select($group);$raw=if($selected){Ask ([PeopleOfTheIliacBay.Core.NpcActionChat]::PromptSemantic($case.Text,'You are willing to help a trusted friend. No competing duties; all listed routes and items are verified.',$selected)) '' ([PeopleOfTheIliacBay.Core.NpcActionChat]::GrammarSemantic($selected))}else{'INVALID'};$d=if($selected){[PeopleOfTheIliacBay.Core.NpcActionChat]::ParseSemantic($raw,$selected)};if($d -and $d.Option.Id -eq $case.Id){"PASS later target: $group $raw"}else{$failed++;"FAIL later target: $group $raw"}}
$offer=[PeopleOfTheIliacBay.Core.NpcActionOption]::new();$offer.Id=7;$offer.Kind='Follow';$offer.Label='accompany the player for two minutes';$offer.Target=[PeopleOfTheIliacBay.Core.NpcActionTarget]::new();$offer.Target.Kind='Person';$offer.Target.PersonId='player'
$history="Player: I feel unsafe walking alone.`nNPC: I could accompany you for two minutes. Would you like that?`n"
foreach($case in @(@{Text='That would put my mind at ease, thank you.';Expected='DIRECT|Follow|7|1|120'},@{Text='No, I would rather go on my own.';Expected='DECLINE'},@{Text='Could you instead hand me your silver goblet?';Expected='NONE'},@{Text='I told my sister I would accept, but I am declining now.';Expected='DECLINE'})){
$raw=Ask ([PeopleOfTheIliacBay.Core.NpcActionChat]::PromptOfferResponse($case.Text,'Friendly NPC, still willing; no action is running.',$offer,120)) $history ([PeopleOfTheIliacBay.Core.NpcActionChat]::GrammarSemantic([PeopleOfTheIliacBay.Core.NpcActionOption[]]@($offer)));if($raw -eq $case.Expected){"PASS offer: $raw"}else{$failed++;"FAIL offer: $raw expected $($case.Expected)"}}
$items=$catalog.Select('GROUP|2')
$history="Player: Which of your possessions is silver?`nNPC: My goblet is silver; my shirt is linen.`n"
$raw=Ask ([PeopleOfTheIliacBay.Core.NpcActionChat]::PromptSemantic('Would you hand that silver object over to me?','You are willing to give the item to your trusted friend.',$items)) $history ([PeopleOfTheIliacBay.Core.NpcActionChat]::GrammarSemantic($items))
$bound=[PeopleOfTheIliacBay.Core.NpcActionChat]::ParseSemantic($raw,$items)
if($bound -and $bound.Option.Id -eq 13){"PASS attributed history: $raw"}else{$failed++;"FAIL attributed history: $raw"}
$places=[System.Collections.Generic.List[PeopleOfTheIliacBay.Core.ConversationTopic]]::new()
foreach($kind in @('world-point','building')){
 $place=[PeopleOfTheIliacBay.Core.AppointmentPlace]::new();$place.Kind=$kind;$place.SettlementId=3;$place.BuildingKey=if($kind -eq 'building'){73}else{-1};$place.X=12.25;$place.Y=4;$place.Z=91.75
 $snapshot=[PeopleOfTheIliacBay.Core.ConversationLocationSnapshot]::new(50000,$place)
 $request=if($kind -eq 'building'){'Shall we meet back in this building tomorrow at 10:00 for one hour?'}else{'Shall we meet at this exact spot tomorrow at 10:00 for one hour?'}
 $prompt=[PeopleOfTheIliacBay.Core.SemanticAppointmentChat]::Prompt($request,'','The NPC is willing, available tomorrow, and trusts the player.',$places,500,$null,0,$snapshot)
 $raw=Ask $prompt '' ([PeopleOfTheIliacBay.Core.SemanticAppointmentChat]::GrammarFor($places,0,$snapshot))
 $bound=[PeopleOfTheIliacBay.Core.SemanticAppointmentChat]::Parse($raw,$places,500,0,$snapshot)
 if($bound -and $bound.Kind -eq 'BOOK' -and $bound.Place.Kind -eq $kind -and $bound.DayOffset -eq 1 -and $bound.MinuteOfDay -eq 600 -and $bound.DurationMinutes -eq 60){"PASS captured $kind HERE: $raw"}else{$failed++;"FAIL captured $kind HERE: $raw"}
}
"Failures: $failed. Model-only fixtures, not native inventory execution."
if($failed){exit 1}
