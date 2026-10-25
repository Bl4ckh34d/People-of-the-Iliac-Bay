$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$mobile = Get-Content -Raw -LiteralPath (Join-Path $root 'mod\Scripts\NativeNpcInventoryBridge.PersonalBarter.cs')
$static = Get-Content -Raw -LiteralPath (Join-Path $root 'mod\Scripts\NativeNpcInventoryBridge.StaticPersonalBarter.cs')

function Require([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Require ($mobile.Contains('Func<float,bool> npcCapacityForDelta')) 'Shared barter capacity must receive the proposed NPC weight delta.'
Require ($mobile.Contains('npcDelta=>npc.Items.GetWeight()+npcDelta<=npcEntity.MaxEncumbrance')) 'Mobile preflight must permit an overweight NPC to give enough weight away.'
Require ($mobile.Contains('!npcCapacityForDelta(npcDelta)')) 'Preflight must reject an incoming basket whose final NPC weight exceeds capacity.'
Require ($mobile.Contains('!npcCapacityForDelta(0)')) 'Commit must recheck actual post-transfer NPC capacity.'
Require ($mobile.Contains('player.CarriedWeight+playerDelta>player.MaxEncumbrance')) 'Player native encumbrance guard must remain intact.'
Require ($static.Contains('ignoredDelta=>StaticInventoryWithinBound(npc)')) 'Static owners must ignore physical weight and retain their fixed item-count bound.'

Write-Output 'NATIVE_PERSONAL_BARTER_CAPACITY_REGRESSION_PASS'
