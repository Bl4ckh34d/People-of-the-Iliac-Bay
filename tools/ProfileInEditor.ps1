param([string]$RunName='baseline', [string]$FixtureFolder='SAVE82', [switch]$UseDeployedSource,
    [switch]$CountersOnly, [switch]$Overnight, [switch]$ValidatePlaytest, [switch]$ValidateArrival,
    [switch]$ValidateWardrobe, [switch]$ValidateCrowd, [switch]$ValidateShopping, [switch]$ValidateSpeech, [switch]$ShoppingBuyerOnly,
    [switch]$ValidateLeisure, [switch]$ValidateMorningShop, [switch]$ValidateInnDiversion, [switch]$ValidateMurderReport,
    [switch]$ValidateStaticTrade, [switch]$ValidateStreetEncounter, [switch]$ValidateSceneObservers, [switch]$ValidateHousing,
    [switch]$ClimateDetails, [switch]$ValidateComplaint, [switch]$ValidateComplaintWait, [switch]$ValidateMurderReportChat, [switch]$ValidateBankCash,
    [switch]$ValidateCashReturn)
$ErrorActionPreference='Stop'
if($ValidateSpeech -and -not $ValidatePlaytest){throw '-ValidateSpeech requires -ValidatePlaytest so a real trade conversation precedes reopening.'}
if($ShoppingBuyerOnly -and -not $ValidateShopping){throw '-ShoppingBuyerOnly requires -ValidateShopping.'}
if($ValidateComplaintWait -and -not $ValidateComplaint){throw '-ValidateComplaintWait requires -ValidateComplaint.'}
if($ValidateMurderReportChat -and -not $ValidateMurderReport){throw '-ValidateMurderReportChat requires -ValidateMurderReport.'}
if($ValidateCashReturn -and -not $ValidateBankCash){throw '-ValidateCashReturn requires -ValidateBankCash.'}
if($ValidateStaticTrade -and $ValidatePlaytest){throw 'Run static and mobile personal trade as separate owned fixtures.'}
if($ClimateDetails -and ($ValidateArrival -or $ValidateWardrobe -or $ValidateCrowd -or $ValidateShopping -or
    $ValidateLeisure -or $ValidateMorningShop -or $ValidateInnDiversion -or $ValidateMurderReport -or
    $ValidateStaticTrade -or $ValidateStreetEncounter -or $ValidateSceneObservers -or $ValidateHousing -or $ValidateComplaint -or $ValidateBankCash)){
    throw '-ClimateDetails requires the normal loiter profile; focused fixtures bypass its timing hooks.'
}
$repo=Split-Path $PSScriptRoot -Parent
$dfu=Split-Path $repo -Parent
$editor=[IO.Path]::GetFullPath('C:/Program Files/Unity/Hub/Editor/2019.4.40f1/Editor/Unity.exe')
if(Get-Process DaggerfallUnity -ErrorAction SilentlyContinue){throw 'Close the game before starting the owned editor profiling fixture.'}
if(Get-Process Unity -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $editor}){throw 'The project editor is already running.'}
if($RunName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or $RunName.Contains('..')){throw 'Use a simple run name.'}
$runRoot=Join-Path $repo "work/playtest-2026-10-26/editor/$RunName"
if(Test-Path $runRoot){throw "Run already exists: $runRoot"}
$fixture=Join-Path $dfu "Player/PortableAppdata/Saves/$FixtureFolder"
$info=Get-Content -LiteralPath (Join-Path $fixture 'SaveInfo.txt') -Raw | ConvertFrom-Json
if(-not $info.characterName.StartsWith('PeopleOfTheIliacBayTest-',[StringComparison]::Ordinal)){throw 'Use an owned disposable fixture.'}
$effectSource=Join-Path $dfu 'daggerfall-unity/Assets/Scripts/Game/MagicAndEffects/EntityEffectBroker.cs'
if(-not ([IO.File]::ReadAllText($effectSource)).Contains('!myType.ContainsGenericParameters')){
    throw 'Apply the editor generic-effect enumeration guard documented in tools/Editor/README.md first.'
}
$layoutSource=Join-Path $dfu 'daggerfall-unity/Assets/Scripts/Utility/RMBLayout.cs'
if(-not ([IO.File]::ReadAllText($layoutSource)).Contains('"-peopleoftheiliacbay-editor-profile"')){
    throw 'Enable the opt-in release-player location cache documented in tools/Editor/README.md before comparing frame times.'
}
New-Item -ItemType Directory -Path (Join-Path $runRoot 'data/Saves') -Force | Out-Null
Copy-Item -LiteralPath $fixture -Destination (Join-Path $runRoot "data/Saves/$FixtureFolder") -Recurse
Copy-Item -LiteralPath (Join-Path $dfu 'Player/PortableAppdata/settings.ini') -Destination (Join-Path $runRoot 'data/settings.ini')
$modData=Join-Path $dfu 'Player/PortableAppdata/Mods/GameData'
$copiedModData=Join-Path $runRoot 'data/Mods/EditorData'
New-Item -ItemType Directory -Path $copiedModData -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $modData 'Mods.json') -Destination $copiedModData
foreach($settings in Get-ChildItem -LiteralPath $modData -Filter 'modsettings.json' -Recurse -File) {
    $relative=$settings.FullName.Substring($modData.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
    $target=Join-Path $copiedModData $relative
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $settings.FullName -Destination $target
}
# This editor-only probe is absent from the player's mod configuration.
$modList=@(Get-Content -LiteralPath (Join-Path $copiedModData 'Mods.json') -Raw | ConvertFrom-Json)
if(-not ($modList | Where-Object {$_.Title -eq 'People of the Iliac Bay Quest Binding Probe'})) {
    $modList+=@{Title='People of the Iliac Bay Quest Binding Probe';Enabled=$false;LoadPriority=999}
}
$modList | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $copiedModData 'Mods.json') -Encoding utf8
if(-not $UseDeployedSource){& (Join-Path $PSScriptRoot 'Deploy.ps1') -ReusePortraitExport}
$project=Join-Path $dfu 'daggerfall-unity'
$arguments='-projectPath "'+$project+'" -executeMethod PeopleOfTheIliacBayEditorProfile.Run '+
    '-peopleoftheiliacbay-editor-profile -peopleoftheiliacbay-town-test -peopleoftheiliacbay-performance-probe '+
    '-peopleoftheiliacbay-profile-only -peopleoftheiliacbay-fixture-character "'+$info.characterName+
    '" -peopleoftheiliacbay-fixture-save "'+$info.saveName+'" -logFile "'+(Join-Path $runRoot 'editor.log')+'"'
if($CountersOnly){$arguments+=' -peopleoftheiliacbay-profile-counters-only'}
if($ClimateDetails){$arguments+=' -peopleoftheiliacbay-profile-climate'}
if($ValidateComplaint){$arguments+=' -peopleoftheiliacbay-verify-complaint'}
if($ValidateComplaintWait){$arguments+=' -peopleoftheiliacbay-verify-complaint-wait'}
if($Overnight){$arguments+=' -peopleoftheiliacbay-profile-overnight'}
if($ValidatePlaytest){$arguments+=' -peopleoftheiliacbay-verify-playtest'}
if($ValidateArrival){$arguments+=' -peopleoftheiliacbay-verify-arrival'}
if($ValidateWardrobe){$arguments+=' -peopleoftheiliacbay-verify-wardrobe'}
if($ValidateLeisure){$arguments+=' -peopleoftheiliacbay-verify-leisure'}
if($ValidateMorningShop){$arguments+=' -peopleoftheiliacbay-verify-morning-shop'}
if($ValidateInnDiversion){$arguments+=' -peopleoftheiliacbay-verify-inn-diversion'}
if($ValidateMurderReport){$arguments+=' -peopleoftheiliacbay-verify-murder-report'}
if($ValidateMurderReportChat){
    $captureRoot=Join-Path $runRoot 'work'
    New-Item -ItemType Directory -Path $captureRoot -Force | Out-Null
    $arguments+=' -peopleoftheiliacbay-verify-murder-report-chat -peopleoftheiliacbay-semantic-action-model-capture "'+
        (Join-Path $captureRoot 'semantic-action-model-capture.jsonl')+'"'
}
if($ValidateStaticTrade){$arguments+=' -peopleoftheiliacbay-verify-static-trade'}
if($ValidateStreetEncounter){$arguments+=' -peopleoftheiliacbay-verify-street-encounter'}
if($ValidateSceneObservers){$arguments+=' -peopleoftheiliacbay-verify-scene-observers'}
if($ValidateHousing){$arguments+=' -peopleoftheiliacbay-verify-housing'}
if($ValidateBankCash){$arguments+=' -peopleoftheiliacbay-verify-bank-cash'}
if($ValidateCashReturn){$arguments+=' -peopleoftheiliacbay-verify-cash-return'}
if($ValidateCrowd){$arguments+=' -peopleoftheiliacbay-verify-crowd'}
if($ValidateShopping){$arguments+=' -peopleoftheiliacbay-verify-shopping'}
if($ValidateSpeech){$arguments+=' -peopleoftheiliacbay-verify-return-voice'}
if($ShoppingBuyerOnly){$arguments+=' -peopleoftheiliacbay-shop-buyer-only'}
$previousRoot=$env:POTIB_EDITOR_PROFILE_ROOT
$previousProbe=$env:POTIB_ROADMAP_PROBE
try{
    $env:POTIB_EDITOR_PROFILE_ROOT=$runRoot
    if($ValidatePlaytest){$env:POTIB_ROADMAP_PROBE='personal-trade'}
    if($ValidateStaticTrade){$env:POTIB_ROADMAP_PROBE='static-personal-trade'}
    $owned=Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $owned.Id | Set-Content -LiteralPath (Join-Path $runRoot 'editor-pid.txt')
    Write-Output "Owned Unity profiling PID $($owned.Id); results: $runRoot"
}finally{$env:POTIB_EDITOR_PROFILE_ROOT=$previousRoot;$env:POTIB_ROADMAP_PROBE=$previousProbe}
