param([Parameter(Mandatory=$true)][string]$RunDirectory)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $RunDirectory).Path
$frames=[Collections.Generic.List[object]]::new()
$sections=[Collections.Generic.List[object]]::new()
$framePrefix='PEOPLE_OF_THE_ILIAC_BAY_LOITER_PROFILE: '
$sectionPrefix='PEOPLE_OF_THE_ILIAC_BAY_PERF_SECTION: '
foreach($line in [IO.File]::ReadLines((Join-Path $root 'editor.log'))) {
    $prefix=if($line.StartsWith($framePrefix,[StringComparison]::Ordinal)){$framePrefix}
        elseif($line.StartsWith($sectionPrefix,[StringComparison]::Ordinal)){$sectionPrefix}else{$null}
    if($null -eq $prefix){continue}
    $entry=[ordered]@{}
    foreach($part in $line.Substring($prefix.Length).Split(';')) {
        $text=$part.Trim();$equals=$text.IndexOf('=')
        if($equals -lt 0){$entry['phase']=$text;continue}
        $key=$text.Substring(0,$equals);$value=$text.Substring($equals+1)
        if($key -eq 'phase' -or $key -eq 'name'){$entry[$key]=$value}
        else{$entry[$key]=[double]::Parse($value,[Globalization.CultureInfo]::InvariantCulture)}
    }
    if($prefix -eq $framePrefix){$frames.Add([pscustomobject]$entry)}else{$sections.Add([pscustomobject]$entry)}
}
$resultPath=Join-Path $root 'result.txt'
$result=if(Test-Path -LiteralPath $resultPath){[IO.File]::ReadAllText($resultPath).Trim()}else{'incomplete'}
$report=[ordered]@{result=$result;frames=@($frames.ToArray());sections=@($sections.ToArray())}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding utf8
$frames | Export-Csv -LiteralPath (Join-Path $root 'frames.csv') -NoTypeInformation
$sections | Export-Csv -LiteralPath (Join-Path $root 'sections.csv') -NoTypeInformation
Write-Output "Saved summary.json, frames.csv and sections.csv in $root; result=$result"
