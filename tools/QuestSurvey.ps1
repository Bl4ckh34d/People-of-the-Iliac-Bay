param(
    [string]$QuestRoot = (Join-Path (Split-Path $PSScriptRoot -Parent | Split-Path -Parent) 'daggerfall-unity/Assets/StreamingAssets/Quests'),
    [string]$OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'work/quest-source-survey.json')
)
$ErrorActionPreference = 'Stop'
# A syntactic inventory, not a quest parser or a permanent-residency classifier.
$records = @(foreach ($file in Get-ChildItem -LiteralPath $QuestRoot -Filter '*.txt' | Sort-Object Name) {
    $lines = [IO.File]::ReadAllLines($file.FullName)
    $inQbn = $false; $hasQuest = $false; $definitions = @(); $placements = @()
    for ($index = 0; $index -lt $lines.Length; $index++) {
        $line = $lines[$index].Trim()
        if ($line -match '^quest\s*:') { $hasQuest = $true }
        if ($line -match '^qbn\s*:') { $inQbn = $true; continue }
        if ($line -match '^qrc\s*:') { $inQbn = $false; continue }
        if (-not $inQbn -or $line.StartsWith('-')) { continue }
        if ($line -match '^(Person|Place)\s+(\S+)\s+(.+)$') {
            $kind = $Matches[1].ToLowerInvariant(); $symbol = $Matches[2]; $spec = $Matches[3]
            $route = if ($kind -eq 'person') {
                if ($spec -match '\bnamed\s+') { 'fixed-individual' }
                elseif ($spec -match '\bgroup\s+Questor\b') { 'questor' }
                elseif ($spec -match '\blocal\b') { 'generated-local-home' }
                elseif ($spec -match '\bremote\b') { 'generated-remote-home' }
                else { 'generated-unspecified-home' }
            } else {
                if ($spec -match '^randompermanent\b') { 'random-fixed-site' }
                elseif ($spec -match '^permanent\b') { 'fixed-site' }
                elseif ($spec -match '^local\b') { 'local-site' }
                elseif ($spec -match '^remote\b') { 'remote-site' }
                else { 'unclassified-site' }
            }
            $definitions += [pscustomobject]@{line=$index+1; kind=$kind; symbol=$symbol; route=$route; source=$line}
        }
        elseif ($line -match '^place\s+.*\s+at\s+') { $placements += [pscustomobject]@{line=$index+1; source=$line} }
    }
    if ($hasQuest) { [pscustomobject]@{file=$file.Name; definitions=$definitions; placementActions=$placements} }
})
# Action statements also begin with "place"; keep them out of resource definitions.
foreach ($record in $records) {
    $actions = @($record.definitions | Where-Object { $_.kind -eq 'place' -and $_.source -match '^place\s+.*\s+at\s+' })
    $record.placementActions = @($record.placementActions) + @($actions | ForEach-Object { [pscustomobject]@{line=$_.line; source=$_.source} })
    $record.definitions = @($record.definitions | Where-Object { $_ -notin $actions })
}
$report = [pscustomobject]@{schemaVersion=1; sourceRoot=(Resolve-Path $QuestRoot).Path; questCount=$records.Count;
    routes=@($records.definitions | Group-Object route | Sort-Object Name | ForEach-Object { [pscustomobject]@{route=$_.Name; count=$_.Count} }); quests=$records}
New-Item -ItemType Directory -Path (Split-Path $OutputPath -Parent) -Force | Out-Null
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$report | Select-Object questCount,routes | ConvertTo-Json -Depth 4
