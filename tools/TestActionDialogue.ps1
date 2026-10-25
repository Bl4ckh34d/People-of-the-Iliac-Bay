param([string]$Endpoint='http://127.0.0.1:8080/completion')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/PeopleOfTheIliacBay.Core.csproj') -c Release --nologo | Out-Null
if($LASTEXITCODE -ne 0){throw 'Core build failed.'}
Add-Type -Path (Join-Path $repo 'src/PeopleOfTheIliacBay.Core/bin/Release/netstandard2.0/PeopleOfTheIliacBay.Core.dll')
$connection=[PeopleOfTheIliacBay.Core.LocalDialogueConnection]::new($Endpoint,10000)
$options=[System.Collections.Generic.List[PeopleOfTheIliacBay.Core.NpcActionOption]]::new()
$follow=[PeopleOfTheIliacBay.Core.NpcActionOption]::new()
$follow.Id=1;$follow.Kind=[PeopleOfTheIliacBay.Core.NpcActionKind]::Follow
$follow.Label='walk with you around this place'
$follow.Target=[PeopleOfTheIliacBay.Core.NpcActionTarget]::new()
$follow.Target.Kind=[PeopleOfTheIliacBay.Core.NpcActionTargetKind]::Person;$follow.Target.PersonId='player'
$options.Add($follow)
$context='You are Celeste, a friendly local patron. The player is your trusted friend Neris; disposition 90. Both are inside The Black Wolf Inn. You have no appointment, job or travel conflict in the next hour. You feel cheerful and would enjoy company. No physical action has been accepted.'
foreach($case in @(
    @{Text='Follow me.';Proposal=$false;Expected='accept'},
    @{Text='I wish I had someone to walk with me around here.';Proposal=$true;Expected='offer'},
    @{Text='Yesterday you offered to follow me. I do not want company now.';Proposal=$true;Expected='none'}
)){
    if($case.Proposal){
        $instructions=[PeopleOfTheIliacBay.Core.NpcActionChat]::ProposalPrompt($case.Text,$context,$options)
        $grammar=[PeopleOfTheIliacBay.Core.NpcActionChat]::ProposalGrammar($options)
    }else{
        $instructions=[PeopleOfTheIliacBay.Core.NpcActionChat]::Prompt($case.Text,$context,$options)
        $grammar=[PeopleOfTheIliacBay.Core.NpcActionChat]::Grammar($case.Text,$options)
    }
    $completion=[PeopleOfTheIliacBay.Core.LocalDialogueCompletion]::Classify($connection,$instructions,'',$grammar)
    $response=Invoke-RestMethod -Uri $completion.Endpoint -Method Post -ContentType 'application/json' -Body $completion.JsonBody -TimeoutSec 30
    $output=([string]$response.content).Trim()
    if($case.Proposal){$decision=[PeopleOfTheIliacBay.Core.NpcActionChat]::ParseProposal($output,$options)}
    else{$decision=[PeopleOfTheIliacBay.Core.NpcActionChat]::Parse($output,$case.Text,$options)}
    if($case.Expected -eq 'none'){
        if($output -ne 'NONE' -or $decision){throw ('Unwanted proposal: '+$output)}
    }elseif(-not $decision){
        throw ('Controlled friendly fixture did not exercise '+$case.Expected+': '+$output)
    }
    if($case.Expected -eq 'offer'){
        $offer=[PeopleOfTheIliacBay.Core.NpcActionOffer]::new('celeste',7,100,$decision)
        $accepted=$null
        if($offer.TryAccept('someone-else',7,101,'yes',[ref]$accepted)){throw 'Another speaker accepted the offer.'}
        if(-not $offer.TryAccept('celeste',7,101,'yes',[ref]$accepted)){throw 'Explicit same-speaker assent failed.'}
        if($offer.TryAccept('celeste',7,102,'yes',[ref]$accepted)){throw 'Assent replay accepted.'}
    }
    Write-Output ('PASS local-model action '+$case.Expected+': '+$output)
}
Write-Output 'Classifier/assent fixture only; does not replace native chat UI or physical execution acceptance.'
