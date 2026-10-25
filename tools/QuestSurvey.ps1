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
        $colon = $line.IndexOf(':')
        $section = if ($colon -ge 0) { $line.Substring(0,$colon).Trim().ToLowerInvariant() } else { '' }
        if ($section -eq 'quest') { $hasQuest = $true }
        if ($section -eq 'qbn') { $inQbn = $true; continue }
        if ($section -eq 'qrc') { $inQbn = $false; continue }
        if (-not $inQbn -or $line.StartsWith('-')) { continue }
        $tokens = @($line.Split([char[]]@(' ',"`t"),[StringSplitOptions]::RemoveEmptyEntries))
        if ($tokens.Count -ge 4 -and $tokens[0] -ieq 'place' -and $tokens[2] -ieq 'at') {
            $placements += [pscustomobject]@{line=$index+1; source=$line}
            continue
        }
        if ($tokens.Count -ge 3 -and $tokens[0] -iin @('Person','Place')) {
            $kind = $tokens[0].ToLowerInvariant(); $symbol = $tokens[1]; $spec = $tokens[2..($tokens.Count-1)]
            $route = if ($kind -eq 'person') {
                if ($spec -icontains 'named') { 'fixed-individual' }
                elseif ((0..($spec.Count-2) | Where-Object { $_ -ge 0 -and $_+1 -lt $spec.Count -and $spec[$_] -ieq 'group' -and $spec[$_+1] -ieq 'Questor' } | Measure-Object).Count -gt 0) { 'questor' }
                elseif ($spec -icontains 'local') { 'generated-local-home' }
                elseif ($spec -icontains 'remote') { 'generated-remote-home' }
                else { 'generated-unspecified-home' }
            } else {
                if ($spec[0] -ieq 'randompermanent') { 'random-fixed-site' }
                elseif ($spec[0] -ieq 'permanent') { 'fixed-site' }
                elseif ($spec[0] -ieq 'local') { 'local-site' }
                elseif ($spec[0] -ieq 'remote') { 'remote-site' }
                else { 'unclassified-site' }
            }
            $definitions += [pscustomobject]@{line=$index+1; kind=$kind; symbol=$symbol; route=$route; source=$line}
        }
    }
    if ($hasQuest) { [pscustomobject]@{file=$file.Name; definitions=$definitions; placementActions=$placements} }
})
$report = [pscustomobject]@{schemaVersion=1; sourceRoot=(Resolve-Path $QuestRoot).Path; questCount=$records.Count;
    routes=@($records.definitions | Group-Object route | Sort-Object Name | ForEach-Object { [pscustomobject]@{route=$_.Name; count=$_.Count} }); quests=$records}
New-Item -ItemType Directory -Path (Split-Path $OutputPath -Parent) -Force | Out-Null
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$report | Select-Object questCount,routes | ConvertTo-Json -Depth 4
