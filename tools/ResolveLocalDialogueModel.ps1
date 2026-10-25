. (Join-Path $PSScriptRoot 'TextProtocol.ps1')
function Resolve-LocalDialogueModel {
    param(
        [Parameter(Mandatory = $true)] [string]$RuntimeRoot,
        [string]$ModelPath
    )

    if ([string]::IsNullOrWhiteSpace($ModelPath)) {
        $selectionPath = Join-Path $RuntimeRoot 'active-model.json'
        if (Test-Path -LiteralPath $selectionPath) {
            $selection = Get-Content -LiteralPath $selectionPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            if ($selection.modelPath -isnot [string] -or [string]::IsNullOrWhiteSpace($selection.modelPath)) {
                throw "Expected a nonempty modelPath in $selectionPath."
            }
            $ModelPath = $selection.modelPath
        } else {
            $ModelPath = 'models/qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf'
        }
    }
    if (-not [System.IO.Path]::IsPathRooted($ModelPath)) { $ModelPath = Join-Path $RuntimeRoot $ModelPath }
    if (-not (Test-Path -LiteralPath $ModelPath -PathType Leaf) -or [System.IO.Path]::GetExtension($ModelPath) -ine '.gguf') {
        throw "Missing GGUF model: $ModelPath"
    }
    $resolved = (Resolve-Path -LiteralPath $ModelPath -ErrorAction Stop).Path
    $name = [System.IO.Path]::GetFileName($resolved)
    $parts = [IO.Path]::GetFileNameWithoutExtension($name).Split([char]'-')
    if ($name.EndsWith('.gguf',[StringComparison]::OrdinalIgnoreCase) -and $parts.Count -ge 4 -and
        $parts[-2] -eq 'of' -and $parts[-1].Length -eq 5 -and $parts[-3].Length -eq 5 -and
        (Test-DecimalValue $parts[-1]) -and (Test-DecimalValue $parts[-3])) {
        $count = [int]$parts[-1]
        if ([int]$parts[-3] -ne 1 -or $count -lt 1 -or $count -gt 256) { throw "Invalid first model shard: $resolved" }
        $prefix = $parts[0..($parts.Count-4)] -join '-'
        for ($index = 1; $index -le $count; $index++) {
            $shard = Join-Path (Split-Path $resolved -Parent) ('{0}-{1:D5}-of-{2:D5}.gguf' -f $prefix, $index, $count)
            if (-not (Test-Path -LiteralPath $shard -PathType Leaf)) { throw "Missing GGUF model shard: $shard" }
        }
    }
    return $resolved
}
