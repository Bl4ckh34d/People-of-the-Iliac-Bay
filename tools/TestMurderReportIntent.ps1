param([string]$Endpoint='http://127.0.0.1:8080/completion')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/PeopleOfTheIliacBay.Core.csproj') -c Release --nologo | Out-Null
if($LASTEXITCODE -ne 0){throw 'Core build failed.'}
Add-Type -Path (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/bin/Release/netstandard2.0/PeopleOfTheIliacBay.Core.dll')
$connection=[PeopleOfTheIliacBay.Core.LocalDialogueConnection]::new($Endpoint,10000)
function Invoke-Classification([string]$Prompt,[string]$Grammar,[int]$ResponseTokens=32) {
    $completion=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Classify($connection,$Prompt,'',$Grammar,$ResponseTokens)
    $payload=$completion.JsonBody|ConvertFrom-Json
    # Preserve the production classifier framing, including its system message
    # and trailing newlines. A shorter substitute instruction hid a native failure.
    $messages=@()
    foreach($segment in $completion.Prompt.Split([string[]]@('<|im_start|>'),[StringSplitOptions]::RemoveEmptyEntries)) {
        if($segment -eq "assistant`n"){continue}
        $line=$segment.IndexOf("`n");$end=$segment.IndexOf('<|im_end|>',[StringComparison]::Ordinal)
        if($line -lt 0 -or $end -le $line){throw 'Malformed classifier message framing.'}
        $messages+=@{role=$segment.Substring(0,$line);content=$segment.Substring($line+1,$end-$line-1)}
    }
    $rendered=Invoke-RestMethod -Uri (([Uri]::new([Uri]$Endpoint,'/apply-template').AbsoluteUri)) -Method Post -ContentType 'application/json' -Body (@{messages=$messages;add_generation_prompt=$true;chat_template_kwargs=@{enable_thinking=$false}}|ConvertTo-Json -Depth 8 -Compress)
    $payload.prompt=$rendered.prompt
    $payload.stop=@($payload.stop|Where-Object {$_ -notin @('<|im_start|>','<|im_end|>')})
    $response=Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json' -Body ($payload|ConvertTo-Json -Depth 12 -Compress) -TimeoutSec 45
    return ([string]$response.content).Trim()
}
$options=[System.Collections.Generic.List[PeopleOfTheIliacBay.Core.NpcActionOption]]::new()
$report=[PeopleOfTheIliacBay.Core.NpcActionOption]::new()
$report.Id=1;$report.Kind=[PeopleOfTheIliacBay.Core.NpcActionKind]::ReportMurder
$report.Label='go to the nearest reachable guard and deliver the player''s anonymous report of a murder; unverified hearsay, no identified suspect, victim or crime location, no arrest promised'
$report.Target=[PeopleOfTheIliacBay.Core.NpcActionTarget]::new()
$report.Target.Kind=[PeopleOfTheIliacBay.Core.NpcActionTargetKind]::Person;$report.Target.PersonId='g:1:n:2';$report.Target.SettlementId=1
$options.Add($report)
$follow=[PeopleOfTheIliacBay.Core.NpcActionOption]::new()
$follow.Id=2;$follow.Kind=[PeopleOfTheIliacBay.Core.NpcActionKind]::Follow;$follow.Label='follow the player locally'
$follow.Target=[PeopleOfTheIliacBay.Core.NpcActionTarget]::new()
$follow.Target.Kind=[PeopleOfTheIliacBay.Core.NpcActionTargetKind]::Person;$follow.Target.PersonId='player'
$options.Add($follow)
$catalog=[PeopleOfTheIliacBay.Core.NpcActionCandidateCatalog]::new($options)
$context='You are an adult resident outdoors. A reachable guard is available. No action has been accepted. The player previously said they witnessed a murder by an unknown killer. That is their unverified claim, not your eyewitness knowledge.'
# The native decomposition call supplies available kinds, not social context.
$decompositionContext=(@('ReportMurder','Follow','Wait','MoveTo','Enter','Transfer') | ForEach-Object {
    $_+': native targets available; later target selection supplies exact identities'
}) -join "`n"
$cases=@(
    @{Text='I saw a murder. Please find a guard and report it anonymously.';Expected=$true},
    @{Text='Bitte geh zur naechsten Wache und melde anonym den Mord, den ich gesehen habe.';Expected=$true},
    @{Text='Could you tell the guards about what I just saw?';Expected=$true},
    @{Text='I saw a murder by someone I don''t know. Please find the nearest guard and report it anonymously.';Expected=$true},
    @{Text='I saw a murder yesterday.';Expected=$false},
    @{Text='Do not report the murder to the guards.';Expected=$false},
    @{Text='Accuse Thomas of murder and have him arrested.';Expected=$false},
    @{Text='He said "Please report the murder". I am only quoting him.';Expected=$false}
)
$failures=[System.Collections.Generic.List[string]]::new()
foreach($case in $cases) {
    if($case.Expected) {
        $routePrompt=[PeopleOfTheIliacBay.Core.ConversationKnowledge]::RoutingPrompt($case.Text,$context)
        $routeRaw=Invoke-Classification $routePrompt ([PeopleOfTheIliacBay.Core.ConversationKnowledge]::RoutingGrammar())
        $route=[PeopleOfTheIliacBay.Core.ConversationKnowledge]::ParseRouting($routeRaw)
        if(-not $route -or $route.Intent -ne 'action'){$failures.Add('Routing: '+$case.Text+' => '+$routeRaw)}
        $decomposition=Invoke-Classification ([PeopleOfTheIliacBay.Core.NpcActionDecomposition]::Prompt($case.Text,$decompositionContext)) ([PeopleOfTheIliacBay.Core.NpcActionDecomposition]::Grammar()) 192
        if($decomposition -ne 'SINGLE'){$failures.Add('Decomposition: '+$case.Text+' => '+$decomposition)}
        $group=Invoke-Classification ($catalog.Prompt($case.Text)) ($catalog.Grammar())
        $selected=$catalog.Select($group)
        if(-not $selected -or $selected[0].Kind -ne $report.Kind){$failures.Add('Category: '+$case.Text+' => '+$group)}
    }
    $prompt=[PeopleOfTheIliacBay.Core.NpcActionChat]::PromptSemantic($case.Text,$context,$options,$false,$true)
    $raw=Invoke-Classification $prompt ([PeopleOfTheIliacBay.Core.NpcActionChat]::GrammarSemantic($options,$false))
    $decision=$null
    $bound=[PeopleOfTheIliacBay.Core.NpcActionIntent]::TryBind($raw,$options,[ref]$decision)
    $matches=$bound -and $decision.Option.Kind -eq $report.Kind
    if($matches -ne $case.Expected -or (-not $case.Expected -and $bound)){$failures.Add('Intent: '+$case.Text+' => '+$raw)}
    Write-Output ($case.Text+' => '+$raw)
}
foreach($text in @('Report the murder to a guard, then wait there for 30 seconds.',
    'Wait here for 20 seconds, then go and report the murder to a guard.')) {
    $raw=Invoke-Classification ([PeopleOfTheIliacBay.Core.NpcActionDecomposition]::Prompt($text,$decompositionContext)) ([PeopleOfTheIliacBay.Core.NpcActionDecomposition]::Grammar()) 192
    $parsed=[PeopleOfTheIliacBay.Core.NpcActionDecomposition]::Parse($raw,$text)
    if(-not $parsed -or $parsed.Kind -ne 'Chain' -or $parsed.Clauses.Count -ne 2){$failures.Add('Two separate effects: '+$text+' => '+$raw)}
    Write-Output ($text+' => '+$raw)
}
foreach($failure in $failures){Write-Output ('FAIL '+$failure)}
if($failures.Count){throw ('Murder-report model checks failed: '+$failures.Count)}
Write-Output 'PASS 8 intent cases, positive routing/category/decomposition and 2 separate-effect controls. Model interpretation only; native consent and delivery are tested separately.'
