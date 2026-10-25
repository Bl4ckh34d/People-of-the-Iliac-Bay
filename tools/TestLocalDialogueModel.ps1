param()
$ErrorActionPreference = 'Stop'
$resolver = Join-Path $PSScriptRoot 'ResolveLocalDialogueModel.ps1'
if (-not (Test-Path -LiteralPath $resolver)) { throw 'Model selection resolver is not implemented.' }
. $resolver
$fixture = Join-Path (Split-Path $PSScriptRoot -Parent) ('work/model-selection-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'models') -Force | Out-Null
$first = Join-Path $fixture 'models/qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf'
$second = Join-Path $fixture 'models/qwen2.5-7b-instruct-q4_k_m-00002-of-00002.gguf'
$alternate = Join-Path $fixture 'models/alternate.gguf'
Set-Content -LiteralPath $first -Value 'path-only fixture'
Set-Content -LiteralPath $second -Value 'path-only fixture'
Set-Content -LiteralPath $alternate -Value 'path-only fixture'
$config = Join-Path $fixture 'active-model.json'
$checks = 0
function Assert-Model([string]$actual, [string]$expected) {
    if ($actual -ine (Resolve-Path -LiteralPath $expected).Path) { throw "Wrong selected model: $actual; expected $expected" }
}
function Assert-Rejected([scriptblock]$action, [string]$message) {
    $rejected = $false
    try { & $action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw $message }
}
Assert-Model (Resolve-LocalDialogueModel -RuntimeRoot $fixture) $first
$checks++
'{"modelPath":"models/alternate.gguf"}' | Set-Content -LiteralPath $config
Assert-Model (Resolve-LocalDialogueModel -RuntimeRoot $fixture) $alternate
$checks++
'{"modelPath":"models/missing.gguf"}' | Set-Content -LiteralPath $config
Assert-Rejected { Resolve-LocalDialogueModel -RuntimeRoot $fixture } 'Missing selected model silently fell back.'
$checks++
Assert-Model (Resolve-LocalDialogueModel -RuntimeRoot $fixture -ModelPath $alternate) $alternate
$checks++
'{broken' | Set-Content -LiteralPath $config
Assert-Rejected { Resolve-LocalDialogueModel -RuntimeRoot $fixture } 'Malformed selection was accepted.'
$checks++
'{"modelPath":""}' | Set-Content -LiteralPath $config
Assert-Rejected { Resolve-LocalDialogueModel -RuntimeRoot $fixture } 'Empty selection silently fell back.'
$checks++
$incomplete = Join-Path $fixture 'models/incomplete-00001-of-00002.gguf'
Set-Content -LiteralPath $incomplete -Value 'path-only fixture'
Assert-Rejected { Resolve-LocalDialogueModel -RuntimeRoot $fixture -ModelPath $incomplete } 'Incomplete shard set was accepted.'
$checks++
foreach ($name in @('invalid-00002-of-00002.gguf','invalid-00001-of-00000.gguf','invalid-00001-of-00257.gguf')) {
    $invalid = Join-Path $fixture ('models/'+$name)
    Set-Content -LiteralPath $invalid -Value 'path-only fixture'
    Assert-Rejected { Resolve-LocalDialogueModel -RuntimeRoot $fixture -ModelPath $invalid } 'Invalid shard index or count was accepted.'
    $checks++
}
Write-Output "LOCAL_DIALOGUE_MODEL_SELECTION_PASS: $checks checks; default, configured, explicit, invalid and incomplete models."
