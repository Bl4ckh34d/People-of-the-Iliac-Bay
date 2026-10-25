. (Join-Path $PSScriptRoot 'TextProtocol.ps1')

function Get-ProbeLines([string]$Text, [string]$Marker) {
    foreach ($line in $Text.Replace("`r",'').Split([char]10)) {
        $offset = $line.IndexOf($Marker,[StringComparison]::Ordinal)
        if ($offset -ge 0) { $line.Substring($offset) }
    }
}

function Get-ProbeFields([string]$Line, [string]$Marker) {
    if (-not $Line.StartsWith($Marker,[StringComparison]::Ordinal)) { return $null }
    $fields = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($part in $Line.Substring($Marker.Length).Trim().TrimEnd('.').Split([char]';')) {
        $separator = $part.IndexOf('=')
        if ($separator -lt 1) { continue }
        $key = $part.Substring(0,$separator).Trim()
        if ($fields.ContainsKey($key)) { return $null }
        $fields.Add($key,$part.Substring($separator+1).Trim())
    }
    return ,$fields
}

function Test-ProbeReceipt([string]$Text, [string]$Marker, [hashtable]$Rules) {
    foreach ($line in @(Get-ProbeLines $Text $Marker)) {
        $fields = Get-ProbeFields $line $Marker
        if ($null -eq $fields) { continue }
        $valid = $true
        foreach ($key in $Rules.Keys) {
            if (-not $fields.ContainsKey($key)) { $valid=$false; break }
            $rule=$Rules[$key]; $value=$fields[$key]
            if ($rule -is [scriptblock]) { $accepted = & $rule $value $fields }
            else { $accepted = $value -cin @($rule) }
            if (-not $accepted) { $valid=$false; break }
        }
        if ($valid) { return $true }
    }
    return $false
}

function Assert-ProbeReceipt([string]$Text, [string]$Marker, [hashtable]$Rules) {
    if (-not (Test-ProbeReceipt $Text $Marker $Rules)) { throw "Missing or invalid native receipt: $Marker" }
}

function Test-ProbeDecimal([string]$Value, [int]$Places, [double]$Minimum, [double]$Maximum) {
    $parts=$Value.Split([char]'.'); $number=0.0
    return $parts.Count -eq 2 -and $parts[1].Length -eq $Places -and
        (Test-DecimalValue $parts[0]) -and (Test-DecimalValue $parts[1]) -and
        [double]::TryParse($Value,[Globalization.NumberStyles]::AllowDecimalPoint,
            [Globalization.CultureInfo]::InvariantCulture,[ref]$number) -and
        $number -ge $Minimum -and $number -le $Maximum
}

function Test-NativeLogFailure([string]$Text) {
    foreach ($line in $Text.Replace("`r",'').Split([char]10)) {
        if ($line.Contains('PEOPLE_OF_THE_ILIAC_BAY_TOWN_FAIL') -or
            $line.Contains('PEOPLE_OF_THE_ILIAC_BAY_TRAVEL_PLANNING_FAILED') -or
            $line.Contains('PEOPLE_OF_THE_ILIAC_BAY_GUARD_PROBE_FAIL')) { return $true }
        $errorAt=$line.IndexOf('error CS',[StringComparison]::Ordinal)
        if ($errorAt -ge 0 -and $line.Length -gt $errorAt+8 -and [char]::IsDigit($line[$errorAt+8])) { return $true }
        $first=$line.TrimStart().Split([char[]]@(' ',':','('),[StringSplitOptions]::RemoveEmptyEntries) | Select-Object -First 1
        if ($first -and $first.EndsWith('Exception',[StringComparison]::Ordinal)) { return $true }
    }
    return $false
}
