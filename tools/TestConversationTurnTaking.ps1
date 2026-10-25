param(
    [string]$Endpoint='http://127.0.0.1:8080/completion',
    [string]$BaselineSourceZip=(Join-Path (Split-Path $PSScriptRoot -Parent) 'work/checkpoints/2026-10-19-semantic-072456/source.zip'),
    [string]$TopicsFile=''
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(-not (Test-Path -LiteralPath $BaselineSourceZip)){throw "Archived pre-change source is required: $BaselineSourceZip"}

# Read the archived pre-change intent rule. Build the baseline request by removing
# only the turn-taking wording added in the working source; never edit/revert source.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $BaselineSourceZip))
try {
    $entry=$archive.GetEntry('src/PeopleOfTheIliacBay.Core/ConversationKnowledge.cs')
    if(-not $entry){throw 'Checkpoint does not contain ConversationKnowledge.cs.'}
    $reader=[System.IO.StreamReader]::new($entry.Open())
    try{$baselineSource=$reader.ReadToEnd()}finally{$reader.Dispose()}
} finally {$archive.Dispose()}
$baselineRule='topic for a factual question matching an offered topic;'
if(-not $baselineSource.Contains($baselineRule)){throw 'Checkpoint does not contain the expected pre-change topic rule.'}

$artifacts=Join-Path $repo 'work/conversation-turntaking/artifacts'
dotnet build (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/PeopleOfTheIliacBay.Core.csproj') -c Release --artifacts-path $artifacts --nologo | Out-Null
if($LASTEXITCODE -ne 0){throw 'Core build failed.'}
Add-Type -Path (Join-Path $artifacts 'bin/PeopleOfTheIliacBay.Core/release/PeopleOfTheIliacBay.Core.dll')
$connection=[PeopleOfTheIliacBay.Core.LocalDialogueConnection]::new($Endpoint,10000)
$topics=[System.Collections.Generic.List[PeopleOfTheIliacBay.Core.ConversationTopic]]::new()
$topicSource='stress-style synthetic topics; not a native capture'
if($TopicsFile){
    if(-not (Test-Path -LiteralPath $TopicsFile)){throw "Topic capture was not found: $TopicsFile"}
    $topicSource='provided topic capture: '+(Resolve-Path -LiteralPath $TopicsFile).Path
    foreach($item in (Get-Content -LiteralPath $TopicsFile -Raw|ConvertFrom-Json)){
        $topics.Add([PeopleOfTheIliacBay.Core.ConversationTopic]::new([int]$item.Id,[string]$item.Caption,[string]$item.Path,[bool]$item.Group))
    }
    if(-not ($topics|Where-Object Id -eq 105) -or -not ($topics|Where-Object Id -eq 106)){throw 'Topic capture must include offered IDs 105 and 106.'}
}else{
    # Daggerfall-style offer set keeps the salient IDs and fills the list with
    # ordinary locations, people, services, and lore topics to stress prompt size.
    $captions=@(
        'King Lysandus','Daggerfall','King Gothryd','Queen Akorithi','Morgiah','Lord Woodborne','The Iliac Bay','Wayrest','Sentinel','Gothway Garden',
        'Daggerfall Castle','Daggerfall Cathedral','Direnni Tower','The Mages Guild','The Fighters Guild','The Dark Brotherhood','The Temple of Kynareth','The Temple of Julianos','The Temple of Arkay','The Temple of Stendarr',
        'The Rusty Dagger','The Black Horse','The Mermaid Song','The Copper Kettle','The Fox and Bird','The King and Queen','The White Horse','The Daggerfall Market','The City Gates','The Harbor',
        'The Knights of the Dragon','The Knights of the Rose','The Blades','The Underking','Mannimarco','Nulfaga','The Mantella','The Numidium','The Warp in the West','The Bay Kings',
        'The Wrothgarian Mountains','Balfiera','Betony','Glenpoint','Koegria','Shalgora','Tulune','Mournoth','Lainlyn','Anticlere',
        'The local ruler','The city guard','The royal family','The town council','The local clergy','The merchants','The common folk','The harbor master','The innkeeper','The guildmaster',
        'A place to stay','A place to eat','A healer','A blacksmith','A bookseller','A map seller','A temple','A tavern','A bank','A pawn shop',
        'The royal succession','The ghost of the king','The royal tomb','The city history','The local customs','The regional politics','The current war','The kingdoms','The royal court','The people of Daggerfall',
        'Directions to the castle','Directions to the cathedral','Directions to the harbor','Directions to the market','Directions to the Mages Guild','Directions to the Fighters Guild','Directions to a tavern','Directions to a temple','Directions to the city gates','Directions around town',
        'The weather','The roads','The local laws','The city watch','The festivals','The local rumors','The nearby villages','The surrounding region','The bay','The royal haunting'
    )
    for($i=0;$i -lt $captions.Count;$i++){
        $id=if($i -eq 0){105}elseif($i -eq 1){106}else{$i}
        $path=if($i -ge 70 -and $i -lt 80){'Tell me about / History'}elseif($i -ge 80){'Where is / Location'}elseif($i -ge 50){'Tell me about / People'}else{'Where is / Location'}
        $topics.Add([PeopleOfTheIliacBay.Core.ConversationTopic]::new($id,$captions[$i],$path,$false))
    }
}
if($topics.Count -lt 80){throw "Expected a large offered-topic set; found $($topics.Count)."}
$grammar=[PeopleOfTheIliacBay.Core.ConversationKnowledge]::Grammar($topics)
$history='Player: I have heard a little about the kingdoms around the bay.'+"`nNPC: There are many histories here, and most are disputed.`n"+
    'Player: I enjoy learning how people remember their rulers.'+"`nNPC: A city's stories often outlast its kings.`n"+
    'Player: I have been asking about the roads and places in this region.'+"`nNPC: Travelers always have questions about the roads.`n"+
    'Player: I am interested in local customs and old rumors.'+"`nNPC: Rumors travel faster than any messenger.`n"+
    'Player: I like hearing how different people describe the same event.'+"`nNPC: Every witness remembers something different.`n"+
    'Player: I may visit a few landmarks while I am here.'+"`nNPC: There are plenty of places to see.`n"+
    'Player: I prefer to understand a place before I explore it.'+"`nNPC: Then ask around; people know their own city.`n"+
    'NPC: What brings you to Daggerfall?`n'+
    "Player: An important quest to solve the mystery around late King Lysandus haunting the city.`n"+
    "NPC: Why Daggerfall specifically?`n"
$cases=@(
    @{Name='NPC-question answer';Text="It is where the King's haunting takes place and the best place to start the investigation.";Intent='social';TopicId=-1},
    @{Name='explanatory statement';Text="I came to investigate the late king's haunting in Daggerfall.";Intent='social';TopicId=-1},
    @{Name='direct factual question';Text='What do you know about King Lysandus?';Intent='topic';TopicId=105}
)

function Invoke-IntentFixture($Version,$Case){
    $prompt=[PeopleOfTheIliacBay.Core.ConversationKnowledge]::Prompt($Case.Text,$topics,105,$history)
    if($Version -in @('baseline','candidate')){
        $candidateRule='topic only when the player asks the NPC a factual question matching an offered topic; an explanatory statement answering the NPC is social, even if it mentions an offered topic;'
        if(-not $prompt.Contains($baselineRule)){throw 'Prompt no longer contains the archived baseline rule.'}
        if($Version -eq 'candidate'){$prompt=$prompt.Replace($baselineRule,$candidateRule)}
    }
    $completion=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Classify($connection,$prompt,'',$grammar,32)
    $payload=$completion.JsonBody|ConvertFrom-Json
    $payload|Add-Member -NotePropertyName seed -NotePropertyValue 47 -Force
    $response=Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json' `
        -Body ($payload|ConvertTo-Json -Depth 12 -Compress) -TimeoutSec 35
    $raw=([string]$response.content).Trim()
    $choice=[PeopleOfTheIliacBay.Core.ConversationKnowledge]::Parse($raw,$topics,$Case.Text,105)
    $passed=$choice -and $choice.Intent -eq $Case.Intent -and $choice.TopicId -eq $Case.TopicId
    $state=if($passed){'PASS'}else{'FAIL'}
    Write-Host ("$state $Version/$($Case.Name): $raw; expected $($Case.Intent)|$($Case.TopicId)")
    return [pscustomobject]@{Version=$Version;Name=$Case.Name;Passed=[bool]$passed;Raw=$raw}
}

$results=[System.Collections.Generic.List[object]]::new()
foreach($case in $cases){$results.Add((Invoke-IntentFixture 'baseline' $case))}
foreach($case in $cases){$results.Add((Invoke-IntentFixture 'candidate' $case))}
$baselineFailures=@($results|Where-Object Version -eq 'baseline'|Where-Object {-not $_.Passed})
$currentFailures=@($results|Where-Object Version -eq 'candidate'|Where-Object {-not $_.Passed})
Write-Output ("Archived-rule baseline: {0}/{1} passed; concise candidate: {2}/{3} passed. Fixed seed=47, temperature=0, topic-count={4}, grammar=ConversationKnowledge.Grammar." -f `
    (3-$baselineFailures.Count),3,(3-$currentFailures.Count),3,$topics.Count)
if($baselineFailures.Count){Write-Output 'ARCHIVED-RULE STRESS FIXTURE FAILURE OBSERVED (this does not establish a native candidate-set reproduction).'}
if($currentFailures.Count){throw 'Concise turn-taking candidate model fixtures failed.'}
Write-Output ("Topic source: $topicSource")
Write-Output 'Model-only classifier check; no native dialogue, action, map effect, or save was executed.'
