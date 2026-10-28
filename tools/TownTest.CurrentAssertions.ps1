# Current native acceptance receipts restored after the damaged conversion.
# Dot-sourced by TownTest only after its owned process has emitted completion.
function Assert-TownMarker([string]$Marker) {
    if (-not $body.Contains($Marker)) { throw "Missing native proof: $Marker" }
}
function Assert-TownOnce([string]$Marker) {
    if ((Get-LiteralCount $body $Marker) -ne 1) { throw "Expected exactly one native proof: $Marker" }
}
function Assert-TownBefore([string]$First,[string]$Second) {
    Assert-TownOnce $First; Assert-TownOnce $Second
    if ($body.IndexOf($First,[StringComparison]::Ordinal) -ge $body.IndexOf($Second,[StringComparison]::Ordinal)) { throw "Native proof order invalid: $First then $Second" }
}
$positiveInteger = {param($v) $n=0L; (Test-DecimalValue $v) -and [long]::TryParse($v,[ref]$n) -and $n -gt 0}
$nonEmpty = {param($v) -not [string]::IsNullOrWhiteSpace($v)}
if ($SplitPortraitRuntimeProbe) {
    Assert-TownOnce 'PEOPLE_OF_THE_ILIAC_BAY_SPLIT_PORTRAIT_PASS:'
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_SPLIT_PORTRAIT_PASS:' @{galleryRecords='32'; gazes='center,left,right'; missing71='native-fallback'}
}
if ($LiveDialoguePresentationProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_LIVE_DIALOGUE_PRESENTATION_BASELINE_RESTORED:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_LIVE_DIALOGUE_PRESENTATION_PASS:'
    Assert-TownBefore 'PEOPLE_OF_THE_ILIAC_BAY_LIVE_DIALOGUE_PRESENTATION_BASELINE_RESTORED:' 'PEOPLE_OF_THE_ILIAC_BAY_LIVE_DIALOGUE_PRESENTATION_PASS:'
}
if ($NativeNpcPoisonAppraisalProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_RECEIVED_ITEM_DIALOGUE_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NOBLE_SKILL_DIALOGUE_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_SKILL_DIALOGUE_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_MOOD_CAUSE_DIALOGUE_PASS: nativePoison=true; acceptedInfluence=true; actualTalk=true; actualGemma=true; sourceInRequest=true; causeInVisibleReply=true; unknownActorPreserved=true'
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_RECEIVED_ITEM_DIALOGUE_PASS:' @{actualGemma='true'; nativeTransfer='true'; normalAppraisal='true'; nativeValue='true'; playerAttributed='true'; actualReplyLogged='true'}
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_NOBLE_SKILL_DIALOGUE_PASS:' @{actualNativePalace='true'; actualStaticNoble='true'; actualTalk='true'; actualGemma='true'; streetwiseUnchanged='true'; affinityIncreased='true'; sameDayCap='true'; nativeEtiquetteUses={param($v) $parts=$v.Split('->'); $parts.Count -eq 2 -and (Test-DecimalValue $parts[0]) -and (Test-DecimalValue $parts[1])}}
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_SKILL_DIALOGUE_PASS:' @{actualTalk='true'; actualGemma='true'; actualNativeGroup=$nonEmpty}
}
if ($NativeNpcPoisonExperienceProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_PROJECTION_MARKERS_PASS:'
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_PROJECTION_MARKERS_PASS:' @{source='actual-native-assignment'; nativeCursor=$positiveInteger; pending='controlled-source-admission-at-capacity'; nativeFullSerializer='true'; pendingDrainedOnce='true'; liveQueuePressure='not-tested'; liveStoreMutated='false'}
}
if ($NpcLeadProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_LEAD_PROBE_PASS:'
}
if ($NpcConversationCommandProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_COMMAND_PROBE_PASS: typed-admission=pass; native-wait=complete; same-turn-fresh-id=blocked; post-close=blocked;'
}
if ($NpcActionChatProbe -and $NpcBeerProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_BEER_CHAT_PASS: realFreeText=true; nativePayment=true; physicalKeeperAndReturn=true; nativeDrinkEffect=true; duplicatePayment=false; playerGoldUnchanged=true'
}
if ($FollowTransitionProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_UNPLANNED_BUILDING_RETURN_PASS:'
    foreach ($kind in @('Exit','Enter')) { Assert-TownOnce ('PEOPLE_OF_THE_ILIAC_BAY_BUILDING_ACTION_NATIVE_PASS: kind='+$kind+';') }
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_UNPLANNED_BUILDING_RETURN_PASS:' @{nativeSerializer='true'; sameNpcOutside='true'; routineReleased='true'; belongingsPreserved='true'}
}
if ($ShopLootSaleProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_SHOP_LOOT_SALE_PASS '
}
if ($NamedOtherHandoverProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NAMED_OTHER_HANDOVER_PASS '
    Assert-TownOnce 'PEOPLE_OF_THE_ILIAC_BAY_NAMED_OTHER_HANDOVER_PASS '
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_NAMED_OTHER_HANDOVER_PASS ' @{source=$nonEmpty; recipient=$nonEmpty; acquiredUid=$positiveInteger; price=$positiveInteger; task={param($v) $prefix='task:native-named-other-handover:'; $v.StartsWith($prefix) -and (Test-HexValue $v.Substring($prefix.Length) ($v.Length-$prefix.Length) -Lowercase) -and $v.Length -gt $prefix.Length}}
}
if ($TempleTrainingProbe) {
    Assert-TownOnce 'PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_TRAINING_NATIVE_PASS:'
    $trainingLine=@(Get-ProbeLines $body 'PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_TRAINING_NATIVE_PASS:')[0]
    if (-not $trainingLine.Contains('; serviceClock=10800; playerUnchanged=true; sameIdentity=true; currentNativeBody=true')) { throw 'Native lesson timer, player isolation or current body proof is missing.' }
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_COMPLETION_VALUES:' @{phase='Completed';serviceStep='1';completedOnce='True';playerUnchanged='True';sameActor='True';sameInventory='True';sameTrainer='True'}
    if ($body.Contains('PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_PAID_CHECKPOINT:')) {
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_PAID_CHECKPOINT:' @{serviceStep='1';actualPayment='true';noEarlySkill='true';noEarlyExperience='true'}
        Assert-TownBefore 'PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_PAID_CHECKPOINT:' 'PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_TRAINING_NATIVE_PASS:'
    } else {
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_PAID_RESUME:' @{actualBody='true';routeRepeated='false';paymentRepeated='false'}
        Assert-TownBefore 'PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_PAID_RESUME:' 'PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_TRAINING_NATIVE_PASS:'
    }
}
if ($EnterWaitProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_ENTER_WAIT_NATIVE_PASS:'
}
if ($KnowledgeBrowseOnly) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_LOCAL_ACTIONS_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_KNOWLEDGE_LEAD_PASS:'
}
if ($questPersonLocationFixture) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_QUEST_PERSON_LOCATION_PASS:'
}
if ($DialogueRefusalProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_SPEECH_BOUNDARY_NATIVE_PASS: kind=DirectThreat; action=EndAndReport; exactReceipt=true; exactTranscript=true; negativeMood=true; resentment=true;'
}
if ($HouseholdJointBodyProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_BODY_PASS: actualBodies=true; actualNativeRoute=true; talkingPausesBoth=true; missingPausesBoth=true; nativeSerializer=true; bothArrive=true; controlledEvent=true; liveFamily=false; purchases=false'
}
if ($HouseholdInnPhysicalProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_INN_PHYSICAL_PASS: actualInterior=true; realKeeper=true; separatePurchases=true; sameTable=true; conversationHoldsBoth=true; nativeReload=true; egressReload=true; sequentialDoor=true; actualDoorEndpoint=true; exteriorReturn=false; controlledOutdoor=true; liveFamily=false'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_INN_SERVICE_CATALOG_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_UNLOADED_INN_CATALOG_PASS:'
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_UNLOADED_INN_CATALOG_PASS:' @{actualRecords='true'; nativeKeeper='true'; nativeMenu='true'; pairs=$positiveInteger; noSurveyNpc='true'; registrationNoNpcSpawn='true'; noLiveFurniture='true'; walletsUnchanged='true'; cancelled='true'; nativeReload='true'; purchase='false'}
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_CONVERSATION_CONTEXT_PASS: actualPair=true; reciprocal=true; projections=4; playerNotMember=true; nativeAdapter=true; nativeTalk=false; movementUnchanged=true'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NATIVE_INN_CALENDAR_PASS: nativeCatalog=true; historicalHoliday=true; finalMinute=true; epochBoundaries=true; worldClockUnchanged=true'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_FAMILY_OFFSCREEN_PASS: nativeCatalog=true; nativeTariff=true; actualWallets=true; productionInnAdapter=true; repeatedHandoff=true; nativeSerializer=true; simulatedHome=true; physicalHome=false; duplicatePayment=false'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_FAMILY_RUNTIME_PASS: productionSelector=true; pairedNativeRoutes=true; exteriorBodies=true; conversationHoldsBoth=true; nativeActivateChildLock=true; nativeActivateHomeEntry=true; pairedHomeBodies=true; nativeSerializer=true; controlledClock=true'
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_INN_SERVICE_CATALOG_PASS:' @{nativeMenu='true'; compatiblePairs=$positiveInteger; cooperativeFrames=$positiveInteger; productionScheduler='true'; cancelledSource='true'; restoreEpoch='true'; walletsUnchanged='true'; nativeReload='true'; simulatedPurchase='false'}
}
if ($DailyItineraryProbe -and -not $HouseholdJointBodyProbe -and -not $HouseholdInnPhysicalProbe -and -not $DailyInteriorOnly -and -not $PrepareOutdoorFixture -and -not $IndoorCatchupProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_DAILY_EXPANSION_NATIVE_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_DAY_PERSISTENCE_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_JOINT_PROGRESS_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_INN_CODEC_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_RUNTIME_PASS:'
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_DAILY_EXPANSION_NATIVE_PASS:' @{realDoorways='true'; localSocialAfter16='true'; initialSocialAfter16='true'; nativeSerializer='true'}
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_DAY_PERSISTENCE_PASS:' @{atomicPair='true'; exactDate='true'; nativeSerializer='true'; appointmentPriority='true'; monotonicSuppression='true'; typedFamilyRecovery='true'}
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_JOINT_PROGRESS_PASS:' @{nativeSerializer='true'; sourceBound='true'; pairedCursors='true'; timeDoesNotMoveActors='true'; controlledPoints='true'; physicalFamily='false'}
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_INN_CODEC_PASS:' @{nativeSerializer='true'; sourceBound='true'; separateBuyers='true'; noPurchaseReceipts='true'; replayBlocked='true'; controlledEntry='true'; physicalInn='false'}
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_RUNTIME_PASS:' @{livePublication='true'; nativeGetState='true'; changedFromBaseline='true'; childcare='true'}
}
if ($OutdoorArrivalProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_SHARED_DOOR_PASS: ordinaryResidents=3;'
}
if ($OutdoorSceneProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NPC_MURDER_NATIVE_PASS: alreadyDead=False; nativeAttack=true; witnessReported=true; victimRemoved=true; sightBeforeCombat=true; killerCorpseRemoved=true; postReturned=true; innocentPlayer=true; nativeSerializer=true'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NPC_MURDER_NATIVE_PASS: alreadyDead=True; nativeAttack=true; witnessReported=true; victimRemoved=true; sightBeforeCombat=true; killerCorpseRemoved=true; postReturned=true; innocentPlayer=true; nativeSerializer=true'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NATURAL_MURDER_REPORT_PASS: actualWitness=true; reporterWalked=true; physicalDelivery=true; cleanup=true; identityBeforeSight=true; nativePursuit=true'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_STATIC_CORPSE_INQUIRY_PASS: actualWitness=true; cleanupFirst=true; walkedToWitness=true; physicalContact=true; sharedCase=true; hiddenSuspectNotLocated=true'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_SEARCH_CHAIN_NATIVE_PASS: existingGuards=true; actualActors=6; allRecruitsWalked=true; relayBeyondFirstGuard=true; seventhStaysFree=true; sharedDeadline=true; noInventedSuspect=true; nativeSerializer=true; allArrivedSearching=true; seventhVisibleContact=true'
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_NPC_MURDER_ATTACK_PASS:' @{armed='True'}
}
if ($NpcHealthCareProbe) {
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_NATIVE_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_ORIGINAL_BASELINE_RESTORED:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_CONDITIONS_NATIVE_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_CONDITION_FALLBACK_PASS: failedSpellDebited=true; actualAntidoteConsumed=true; poisonRemoved=true'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_HOME_SLEEP_NATIVE_PASS: nativeHourRate=true; realHomeBed=true; diseaseBlocks=true; fullNightRecovery=true; unloadedNight=true; noPotionOrGold=true'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_AUTONOMOUS_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_ROUTINE_UNBLOCKED:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_CORPSE_DEPARTURE_PERSISTENCE_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_PAID_SERVICE_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NPC_MAGIC_HEALING_NATIVE_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NATIVE_EXPERIENCE_PROJECTION_MARKERS_PASS:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_INBOX_V5_NATIVE_VERIFIED:'
    Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_INBOX_V5_NATIVE_PASS:'
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_CONDITIONS_NATIVE_PASS:' @{poisonPriority='true'; diseaseSpell='true'; poisonSpell='true'; exactMagicka='true'}
    if ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_MEDICAL_NATIVE_PASS:') -ne 2) { throw 'Both disease and poison native clinic proofs are required.' }
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_AUTONOMOUS_PASS:' @{sufficientRecovery='true'; careReleased='true'; ordinaryScheduleUnblocked='true'; extraConsumption='false'}
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_GOAL_BINDING_PASS:' @{generation=$positiveInteger; treatments=$positiveInteger; stableThroughRecovery='true'}
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_CORPSE_DEPARTURE_PERSISTENCE_PASS:' @{controlledFixture='true'; nativeSerializer='true'; absence30='true'; quickReturnPreserved='true'; guardDeadline60='true'; inquiryWithoutInventedWitness='true'; earlyCleanupAnchor='true'; fixedInquiry30='true'; sharedCaseSix='true'; typedSuspect='true'; physicalGameplay='false'}
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_NATIVE_PASS:' @{realPassiveHostileAlarm='true'; alarmInterruptedCare='true'; nativeMovementObserved='true'; careReplannedAfterAlarmExpiry='true'; paidServiceResumedAfterReturn='true'; exactPlayerCurrencyRestored='true'; exactBaselineRestored='true'}
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_PAID_SERVICE_PASS:' @{realPlayerFunding='true'; pausedBeforePotion='true'; alarmAndTreatmentBillingFrozen='true'; physicalReturnObservedWhilePaused='true'; resumedAfterPhysicalReturn='true'; pausedTimeNotBilled='true'}
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_NPC_MAGIC_HEALING_NATIVE_PASS:' @{realHealHealth='true'; failedExtraDebitRollback='true'; exactMagicka='true'; sourceBoundCare='true'; staleReplayRejected='true'; nativeSerializer='true'}
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_NATIVE_EXPERIENCE_PROJECTION_MARKERS_PASS:' @{sourceReplayWithoutRetirement='false'; retirementCheckedByV5Gate='true'}
    Assert-TownBefore 'PEOPLE_OF_THE_ILIAC_BAY_INBOX_V5_NATIVE_VERIFIED:' 'PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_ORIGINAL_BASELINE_RESTORED:'
    Assert-TownBefore 'PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_ORIGINAL_BASELINE_RESTORED:' 'PEOPLE_OF_THE_ILIAC_BAY_INBOX_V5_NATIVE_PASS:'
    Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_INBOX_V5_NATIVE_PASS:' @{source='actual-native-health'; appointment='controlled-Core-attendance'; deterministicNoLLM='true'; boundedRetirement='true'; pendingPinned='true'; monotoneCounters='true'; typedHistory='true'; nativeFullSerializer='true'; replayBlocked='true'; exactBaselineRestored='true'}
    $schemaLines=@([IO.File]::ReadAllLines((Join-Path $PSScriptRoot '../src/PeopleOfTheIliacBay.Core/WorldState.cs')) | Where-Object { $_.Contains('const int CurrentVersion =') })
    if ($schemaLines.Count -ne 1) { throw 'Current save schema declaration is ambiguous.' }
    $schema=$schemaLines[0].Split('=')[1].Split(';')[0].Trim()
    if (-not (Test-DecimalValue $schema)) { throw 'Invalid current save schema.' }
    Assert-ProbeReceipt $body 'NPC_FRAMEWORK_PERSISTENCE_NATIVE_PASS:' @{source='controlled-current-schema-fixture'; schema=$schema; 'FullSerializer-tempfile-roundtrip'='true'; 'speech-source-cursors-and-stale-ticket'='true'; 'operational-retention-old-task-groups-and-steps-pruned'='true'; 'retained-dependencies-and-command-cursor-roundtrip'='true'}
    Assert-TownMarker 'NPC_FRAMEWORK_SERVICE_SETTLEMENT_NATIVE_PASS: source=controlled-disposable-fixture;'
    Assert-TownMarker 'original-player-currency-and-clock-restored=true'
}

if ($OutdoorNavigationProbe) {
    if ($env:POTIB_NAVMESH_TRIAL -eq '1' -and $env:POTIB_ROADMAP_PROBE -eq 'scene-summary') {
        Assert-TownOnce 'PEOPLE_OF_THE_ILIAC_BAY_SCENE_SUMMARY_PASS:'
        if ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_SCENE_SUMMARY_BODY_PASS:') -ne 2) { throw 'Scene summaries require both native mobile and stationary observation/serialization proofs.' }
    }
    if ($env:POTIB_NAVMESH_TRIAL -eq '1' -and $env:POTIB_ROADMAP_PROBE -eq 'market-commute' -and $env:POTIB_MARKET_MORNING_PERSON) {
        Assert-TownBefore 'PEOPLE_OF_THE_ILIAC_BAY_MARKET_MORNING_SETUP:' 'PEOPLE_OF_THE_ILIAC_BAY_MARKET_MORNING_PASS:'
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_MARKET_MORNING_SETUP:' @{person='npc:1291010263:-1:813365456'; ordinaryOffscreenReconstruction='true'; eveningWalkNewlyClaimed='false'; priorEveningEvidence='market-commute22-player.log'; home='True'}
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_MARKET_MORNING_PASS:' @{person='npc:1291010263:-1:813365456'; eveningMetres='0'; eveningWalkNewlyClaimed='False'; sameStaticBody='true'; sameInventory='true'; realBed='true'; sleepContext='true'; sharedHomeSleepHeal='true'; nativeMobileDuplicate='false'}
        if ($body.Contains('phase=evening-walk;') -or $body.Contains('PEOPLE_OF_THE_ILIAC_BAY_MARKET_COMMUTE_PASS:')) { throw 'Morning-only proof must not repeat or claim a new evening walk.' }
    }
    $roadmapMarkers=@{'starting-spells'='STARTING_SPELL_CAST_NATIVE_PASS'; 'combat-assist'='COMBAT_ASSIST_NATIVE_PASS'; 'guard-assault'='GUARD_ASSAULT_NATIVE_PASS'; 'dungeon-modules'='NAVMESH_RDB_MODULE_PROBE_PASS'; 'interior-module'='NAVMESH_INTERIOR_MODULE_PASS'; 'paid-companion'='PAID_COMPANION_PROBE_PASS'}
    if ($env:POTIB_PAID_COMPANION_XP -eq '1') { $roadmapMarkers['paid-companion']='PAID_COMPANION_XP_NATIVE_PASS' }
    if ($env:POTIB_PAID_COMPANION_PREVIEW -eq '1') { $roadmapMarkers['paid-companion']='PAID_COMPANION_OFFER_PREVIEW_PASS' }
    if ($env:POTIB_PAID_COMPANION_CHECKPOINT -eq '1') { $roadmapMarkers['paid-companion']='PAID_COMPANION_SETUP_NATIVE_PASS' }
    $roadmapMarkers['companion-interior-reload']='FAST_TRAVEL_INTERIOR_RELOAD_PASS'
    $roadmapMarkers['family-inn-return']='FAMILY_INN_RETURN_PASS'
    if ($env:POTIB_ROADMAP_PROBE -in @('controlled-guild-arrival','controlled-camp-arrival')) {
        Assert-TownBefore 'PEOPLE_OF_THE_ILIAC_BAY_CONTROLLED_SHELTER_SETUP:' 'PEOPLE_OF_THE_ILIAC_BAY_CONTROLLED_SHELTER_SELECTED:'
        Assert-TownBefore 'PEOPLE_OF_THE_ILIAC_BAY_CONTROLLED_SHELTER_SELECTED:' 'PEOPLE_OF_THE_ILIAC_BAY_CONTROLLED_SHELTER_ARRIVAL_PASS:'
        $expectedCamping=if ($env:POTIB_ROADMAP_PROBE -eq 'controlled-camp-arrival') {'True'} else {'False'}
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_CONTROLLED_SHELTER_SETUP:' @{existingPeopleOnly='true'; futureVisitControlled='true'; realNativeRoute='true'; membershipInjected='false'; preparedRestore='true'; npcMovedByProbe='false'}
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_CONTROLLED_SHELTER_ARRIVAL_PASS:' @{camping=$expectedCamping; actualArrival='true'; nativeSerializer='true'; reservationsControlled='true'; visitControlled='true'; membershipInjected='false'; npcMovedByProbe='false'}
    }
    if ($env:POTIB_ROADMAP_PROBE -eq 'natural-shelter-census') {
        Assert-TownOnce 'PEOPLE_OF_THE_ILIAC_BAY_NATURAL_SHELTER_CENSUS_DONE:'
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_NATURAL_SHELTER_CENSUS_DONE:' @{physicalArrivalClaimed='false'; probeBookings='false'; probeMembershipGrant='false'}
        if ($env:POTIB_SHELTER_REQUIRE_VISITOR_REPORT -eq '1') {
            Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_CORPSE_REPORT_PASS:' @{foreignVisitor='true'; nativeCorpse='true'; actualVisibleDiscovery='true'; nativeSerializer='true'; validatedRestore='true'; probeWitnessInjection='false'; probeCombatInjection='false'}
        }
    }
    if ($env:POTIB_ROADMAP_PROBE -eq 'natural-checkout') {
        Assert-TownBefore 'PEOPLE_OF_THE_ILIAC_BAY_NATURAL_CHECKOUT_ROOM_READY:' 'PEOPLE_OF_THE_ILIAC_BAY_NATURAL_CHECKOUT_RETURN_STARTED:'
        Assert-TownBefore 'PEOPLE_OF_THE_ILIAC_BAY_NATURAL_CHECKOUT_RETURN_STARTED:' 'PEOPLE_OF_THE_ILIAC_BAY_NATURAL_CHECKOUT_PASS:'
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_NATURAL_CHECKOUT_PASS:' @{person='s:1356029978:h:65792:r:0'; actualDoorReached='true'; bookingExpired='true'; itineraryChanged='false'; regionalReturnObserved='false'}
    }
    if ($env:POTIB_ROADMAP_PROBE -eq 'natural-fallback') {
        Assert-TownBefore 'PEOPLE_OF_THE_ILIAC_BAY_NATURAL_FALLBACK_SOURCE_FULL:' 'PEOPLE_OF_THE_ILIAC_BAY_NATURAL_FALLBACK_PASS:'
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_NATURAL_FALLBACK_SOURCE_FULL:' @{person='s:1276329198:h:262:r:0'; availableOffer='false'; productionBookingUnchanged='true'}
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_NATURAL_FALLBACK_PASS:' @{person='s:1276329198:h:262:r:0'; existingVisit='true'; newBookingsByProbe='false'; guildOrCamp='false'; unobservedExteriorWalkClaimed='false'}
    }
    if ($env:POTIB_ROADMAP_PROBE -eq 'companion-fast-travel' -and $env:POTIB_FAST_TRAVEL_MULTIPLE -eq '1') { Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_PARTY_PASS:' }
    $roadmapMarkers['companion-interior-entry']='FAST_TRAVEL_INTERIOR_ENTRY_PASS'
    $roadmapMarkers['companion-dungeon-fast-travel']='FAST_TRAVEL_DUNGEON_PASS'
    $roadmapMarkers['companion-dungeon-entry']='FAST_TRAVEL_DUNGEON_PASS'
    if ($env:POTIB_ROADMAP_PROBE -eq 'dungeon-follow' -and $env:POTIB_DUNGEON_DANGER_BOUNDARY -eq '1') {
        Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_DANGER_BOUNDARY_PASS:'
    }
    if ($env:POTIB_ROADMAP_PROBE -eq 'dungeon-follow' -and $env:POTIB_DUNGEON_DANGER_EXIT -eq '1') { Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_DANGER_EXIT_PASS:' }
    if ($env:POTIB_ROADMAP_PROBE -eq 'dungeon-follow' -and $env:POTIB_DUNGEON_CONTAINER_LOOT -eq '1') {
        if ($env:POTIB_DUNGEON_CONTAINER_STACK -eq '1') { Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_COMPANION_CONTAINER_STACK_PASS:' }
        else { Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_COMPANION_CONTAINER_PASS:' }
    }
    if ($env:POTIB_ROADMAP_PROBE -eq 'dungeon-follow' -and $env:POTIB_DUNGEON_ENTERPRISE_DIALOGUE -eq '1') { Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_ENTERPRISE_DIALOGUE_PASS:' }
    if ($env:POTIB_ROADMAP_PROBE -eq 'dungeon-follow' -and $env:POTIB_DUNGEON_MULTITHREAT -eq '1') { Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_MULTITHREAT_PASS:' }
    if ($env:POTIB_ROADMAP_PROBE -eq 'dungeon-follow' -and $env:POTIB_DUNGEON_AUTOMATIC_OBSTACLE -eq '1') {
        Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_COMPANION_AUTOMATIC_OBSTACLE_WAIT_PASS:'
        Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_COMPANION_AUTOMATIC_OBSTACLE_RESUME_PASS:'
    }
    if ($env:POTIB_ROADMAP_PROBE -eq 'dungeon-follow' -and $env:POTIB_DUNGEON_FOLLOW_FIXTURE) {
        $dungeonFixture = $env:POTIB_DUNGEON_FOLLOW_FIXTURE | ConvertFrom-Json
        if ($dungeonFixture.verifyLoot) { Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_COMPANION_LOOT_PASS:' }
    }
    $roadmapMarkers['companion-interior-expiry']='FAST_TRAVEL_INTERIOR_EXPIRY_PASS'
    if ($roadmapMarkers.ContainsKey([string]$env:POTIB_ROADMAP_PROBE)) { Assert-TownMarker ('PEOPLE_OF_THE_ILIAC_BAY_'+$roadmapMarkers[$env:POTIB_ROADMAP_PROBE]+':') }
    if ($env:POTIB_ROADMAP_PROBE -eq 'companion-interior-expiry' -and $env:POTIB_FAST_TRAVEL_INTERIOR_UNPAID -eq '1') {
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_INTERIOR_EXPIRY_PASS:' @{requiredUnpaid='True'; originalConsentEnded='true'; uniqueSameBody='true'; routineReturnNotExercised='true'}
    }
    if ($env:POTIB_ROADMAP_PROBE -eq 'companion-interior-expiry' -and $env:POTIB_FAST_TRAVEL_INTERIOR_EXIT -eq '1') {
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_RETIRED_COMPANION_EXIT_PASS:' @{nativeExit='true'; noExteriorBody='true'; noRoadOwner='true'; actionRemainsEnded='true'; sameInventoryAndRelationship='true'}
    }
    if ($env:POTIB_NAVMESH_INTERIOR -and $env:POTIB_NAVMESH_PRODUCTION -eq '1') { Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_INTERIOR_PASS:' }
    if ($env:POTIB_NAVMESH_TRIAL -eq '1' -and $env:POTIB_ROADMAP_PROBE -eq 'static-personal-trade') {
        foreach ($case in @('CANCEL','BUY','SELL')) { Assert-TownOnce ('PEOPLE_OF_THE_ILIAC_BAY_STATIC_PERSONAL_TRADE_'+$case+'_PASS:') }
    }
    if ($env:POTIB_NAVMESH_TRIAL -eq '1' -and $env:POTIB_ROADMAP_PROBE -eq 'personal-trade') {
        foreach ($case in @('CANCEL','BUY','SELL')) { Assert-TownOnce ('PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_TRADE_'+$case+'_PASS:') }
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_TRADE_CANCEL_PASS:' @{nativeWindow='true'; lowMercantile20='true'; previewUnchanged='true'; cancelUnchanged='true'}
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_TRADE_BUY_PASS:' @{nativeWindow='true'; lowMercantile20='true'; exactItem='true'; exactGold='true'; nativeMercantile='true'}
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_TRADE_SELL_PASS:' @{nativeWindow='true'; exactItem='true'; exactGold='true'}
        Assert-TownOnce 'PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_TRADE_PARCHMENT_PASS:'
        Assert-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_TRADE_PARCHMENT_PASS:' @{nativeZeroValue='true'; bothDirections='true'; walletsUnchanged='true'; eligibilityFiltered='true'}
        if ($env:POTIB_TRADE_EXTENDED -eq '1') {
            Assert-TownOnce 'PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_HAGGLING_NATIVE_PASS:'
            Assert-TownOnce 'PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_LOAN_NATIVE_PASS:'
        }
    }
}
if ($NpcActionProbe -and $env:POTIB_ROADMAP_PROBE -eq 'seat-task') { Assert-TownMarker 'PEOPLE_OF_THE_ILIAC_BAY_SEAT_TASK_CHAIN_PASS:' }
