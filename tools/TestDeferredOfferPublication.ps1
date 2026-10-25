$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$sourcePath = Join-Path $repo 'mod/Scripts/PeopleOfTheIliacBayMod.Dialogue.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$lifecycleSource = [IO.File]::ReadAllText((Join-Path $repo 'mod/Scripts/PeopleOfTheIliacBayMod.cs'))
$actionSource = [IO.File]::ReadAllText((Join-Path $repo 'mod/Scripts/PeopleOfTheIliacBayMod.DialogueActions.cs'))
$compactSource = -join @($source.ToCharArray() | Where-Object { -not [char]::IsWhiteSpace($_) })
$compactActions = -join @($actionSource.ToCharArray() | Where-Object { -not [char]::IsWhiteSpace($_) })

if (-not $compactSource.Contains('ActionappendAcceptedAnswer=()=>AppendUnrecordedDialoguePairWithSpeech(talk,playerText,answer,true,null,null,NpcExperienceAppliedEffectsMask.None);')) {
    throw 'Accepted social replies do not name the canonical transcript publication callback.'
}
if (-not $source.Contains('Action publishAcceptedAnswer=()=>{if(onAnswer!=null)onAnswer(answer);};')) {
    throw 'Accepted social replies do not name the post-publication action callback.'
}
if (-not $compactSource.Contains('if(!TryDeferAcceptedSpeechAppend(talk,snapshot,playerText,answer,appendAcceptedAnswer,publishAcceptedAnswer)){appendAcceptedAnswer();publishAcceptedAnswer();}')) {
    throw 'Deferred accepted speech does not publish action callbacks after the canonical transcript pair.'
}
if ($source.Contains('if(onAnswer!=null)onAnswer(answer);' + [Environment]::NewLine + '                            return;')) {
    throw 'Accepted action callback still runs before deferred transcript publication.'
}
if (-not $lifecycleSource.Contains('InvalidatePendingConversationAction();')) {
    throw 'Closing the native talk window does not invalidate its pending action offer.'
}
if (-not $compactActions.Contains('privatevoidInvalidatePendingConversationAction(){varoffer=pendingConversationAction;pendingConversationAction=null;pendingConversationActionHistory=null;if(offer!=null)offer.Invalidate();}')) {
    throw 'Pending action offer invalidation does not clear both bindings and consume captured references.'
}

Write-Output 'PASS accepted social callbacks publish after transcript; talk close invalidates pending offers.'
