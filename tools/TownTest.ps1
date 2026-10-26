param([string]$DfuRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent),
    [switch]$Headless,
    [ValidateScript({ $prefix='PeopleOfTheIliacBayTest-'; $_.Length -eq ($prefix.Length+8) -and $_.StartsWith($prefix,[StringComparison]::Ordinal) -and @($_.Substring($prefix.Length).ToCharArray() | Where-Object { '0123456789abcdef'.IndexOf($_) -lt 0 }).Count -eq 0 })][string]$FixtureCharacter,
    [ValidateScript({ $_.Length -gt 0 -and @($_.ToCharArray() | Where-Object { 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 _-'.IndexOf($_) -lt 0 }).Count -eq 0 })][string]$FixtureSaveName,
    [switch]$QuestProbe,
    [switch]$FreshPreviews,
    [switch]$HauntingDawnProbe,
    [switch]$PerformanceProbe,
    [switch]$LocationBlockProbe,
    [switch]$NavigationTiming,
    [switch]$ResidentTripProbe,
    [switch]$CrossRegionTripProbe,
    [switch]$TavernSurveyProbe,
    [switch]$CityTravelerProbe,
    [switch]$GateGuardProbe,
    [switch]$GuardQuotaOnly,
    [switch]$WallQuarterProbe,
    [switch]$StationaryNpcProbe,
    [switch]$DialogueProbe,
    [switch]$DialogueClockOnly,
    [switch]$DialogueRefusalProbe,
    [switch]$SemanticConversationProbe,
    [switch]$ConversationContextSnapshotProbe,
    [switch]$DecisionShadowProbe,
    [switch]$DecisionRoutingProbe,
    [switch]$DecisionRoutingAppraisalReopenProbe,
    [switch]$DecisionRoutingAppraisalReopenSpeech,
    [switch]$DecisionRoutingSafeDowngradeProbe,
    [switch]$DecisionWillingnessProbe,
    [switch]$GoalBindingProbe,
    [switch]$GoalQwenWillingnessProbe,
    [switch]$GoalQwenBoundGoal,
    [switch]$GoalSequenceProbe,
    [switch]$GoalCancellationProbe,
    [switch]$GoalTwoPass,
    [ValidateSet(0,3,4,5)][int]$GoalChainLength = 0,
    [string]$DecisionShadowCaptureDirectory,
    [switch]$DecisionShadowWaitCandidateCapture,
    [switch]$ConversationContextLifecycleProbe,
    [switch]$ConversationContextSpeakerProbe,
    [switch]$KeeperWorkDutyProbe,
    [switch]$NativeNpcPoisonExperienceProbe,
    [switch]$NativeNpcPoisonAppraisalProbe,
    [switch]$ConversationReplyOutcomeProbe,
    [switch]$FriendshipProgressionProbe,
    [switch]$ConversationMoodProbe,
    [switch]$SocialDuplicateReplyProbe,
    [switch]$NpcExperienceAppraisalTransportProbe,
    [switch]$ExperienceDeferredInboxSerializationProbe,
    [switch]$BreezeTtsTrialProbe,
    [switch]$BreezeTtsCapacityProbe,
    [switch]$NpcIdentityIntegrationProbe,
    [switch]$SocialDialogueStreamingProbe,
    [switch]$BreezeTtsSettingsProbe,
    [switch]$NativeTopicTtsProbe,
    [switch]$TtsStreamingAcceptanceProbe,
    [switch]$BreezeTtsMixerReceiptProbe,
    [switch]$SpeechRevealPresentationProbe,
    [switch]$SplitPortraitRuntimeProbe,
    [switch]$LiveDialoguePresentationProbe,
    [switch]$NpcAcuteFearProbe,
    [switch]$NpcResentmentRecoveryProbe,
    [switch]$PortraitVisemePreview,
    [string]$TtsContrastManifest,
    [string]$TtsTrialDirectory,
    [switch]$ConversationActionMoodProbe,
    [switch]$ActionWordingProbe,
    [switch]$DialogueAvailabilityProbe,
    [switch]$KnowledgeBrowseOnly,
    [switch]$MemoryArchiveProbe,
    [switch]$MemoryArchiveRead,
    [switch]$MemoryArchiveModelProbe,
    [switch]$MovementClockProbe,
    [switch]$OccupiedApproachProbe,
    [switch]$BusinessIdentityProbe,
    [switch]$AppointmentProbe,
    [switch]$AppointmentPointProbe,
    [switch]$AppointmentNoShowProbe,
    [switch]$FollowTransitionProbe,
    [switch]$ShopCommerceProbe,
    [switch]$TempleTrainingProbe,
    [switch]$ShopLootSaleProbe,
    [switch]$NamedOtherHandoverProbe,
    [switch]$ShopCommerceChatProbe,
    [switch]$TaskFailureFeedbackProbe,
    [switch]$RoutineRecoveryPhysicalProbe,
    [switch]$RoutineRecoveryDayProbe,
    [switch]$HomeContainerDepositProbe,
    [switch]$MoveToWaitProbe,
    [switch]$EnterWaitProbe,
    [switch]$NpcLeadProbe,
    [switch]$NpcInteractionProbe,
    [switch]$NpcVitalsProbe,
    [switch]$NpcHealthCareProbe,
    [switch]$VerbalCourtProbe,
    [switch]$NpcHomeMealProbe,
    [switch]$NpcHomeReturnProbe,
    [switch]$NpcStartingBelongingsSceneProbe,
    [switch]$RoutineRecoverySceneChangeProbe,
    [ValidateSet('unavailable','context-limit','dispatch-rejected')][string]$TaskReplyFallbackProbe,
    [switch]$CommerceRestartPrepare,
    [ValidateSet('accepted','completed')][string]$CommerceRestartRead,
    [switch]$KeeperStandingProbe,
    [switch]$InnLodgerHomeProbe,
    [switch]$BedroomReplacementProbe,
    [switch]$BedProbe,
    [switch]$SleepProbe,
    [switch]$RunningPilotProbe,
    [switch]$InnAlarmProbe,
    [switch]$ResponseProbe,
    [switch]$GuardActorProbe,
    [switch]$GuardControllerProbe,
    [switch]$GuardPatrolProbe,
    [switch]$GuardDefenseProbe,
    [switch]$GuardBuildingProbe,
    [switch]$GuardWitnessProbe,
    [switch]$HousingProbe,
    [switch]$KeeperResponseProbe,
    [switch]$IndoorNoiseProbe,
    [switch]$IndoorStairsProbe,
    [switch]$CrimeSceneProbe,
    [switch]$OutdoorSceneProbe,
    [switch]$InteriorEscapeProbe,
    [switch]$QuestBindingProbe,
    [switch]$DeliveryProbe,
    [switch]$RefugeProbe,
    [switch]$RegionalProbe,
    [switch]$ShopVisitProbe,
    [switch]$CrowdProbe,
    [switch]$DailyItineraryProbe,
    [switch]$HouseholdJointBodyProbe,
    [switch]$HouseholdInnPhysicalProbe,
    [switch]$IndoorCatchupProbe,
    [switch]$IndoorCrowdProbe,
    [switch]$PhysicalDailyRoutine,
    [switch]$DailyInteriorOnly,
    [switch]$InnSeatOnly,
    [switch]$PrepareOutdoorFixture,
    [switch]$NpcActionProbe,
    [switch]$NpcConversationCommandProbe,
    [switch]$NpcActionChatProbe,
    [switch]$NpcActionChatQwenWillingnessProbe,
    [switch]$NpcActionDiskProbe,
    [switch]$NpcBeerProbe,
    [switch]$OutdoorArrivalProbe,
    [switch]$OutdoorNavigationProbe,
    [switch]$CitizenThreatProbe,
    [switch]$DefenseLedgerProbe,
    [switch]$WelfareProbe)
$GoalChainLengthProbe = $GoalChainLength -gt 0
if ($NamedOtherHandoverProbe) { $ShopLootSaleProbe = $true }
if ($ShopLootSaleProbe) { $ShopCommerceProbe = $true }
. (Join-Path $PSScriptRoot 'LogProtocol.ps1')
if ($TempleTrainingProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw 'TempleTrainingProbe requires the owned PeopleOfTheIliacBayTest-9a5f197b / People - Gothway Garden fixture.' }
if ($EnterWaitProbe) { $MoveToWaitProbe = $true }
$ErrorActionPreference = 'Stop'
if ($NpcHomeMealProbe -or $NpcHomeReturnProbe) { throw 'Home meal and hunger-driven home-return probes are retired with the removed hunger system.' }
if ($TavernSurveyProbe -and $DailyItineraryProbe) { throw 'TavernSurveyProbe and DailyItineraryProbe require separate owned test runs.' }
if ($NpcResentmentRecoveryProbe -and
    ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) {
    throw 'NpcResentmentRecoveryProbe requires the owned PeopleOfTheIliacBayTest-9a5f197b / People - Gothway Garden fixture.'
}
if ($BreezeTtsMixerReceiptProbe) {
    $allowedMixerReceiptOptions = @('BreezeTtsMixerReceiptProbe', 'DfuRoot', 'FixtureCharacter', 'FixtureSaveName')
    $unexpectedMixerReceiptOptions = @($PSBoundParameters.Keys | Where-Object { $_ -notin $allowedMixerReceiptOptions })
    if ($unexpectedMixerReceiptOptions.Count -gt 0) { throw 'BreezeTtsMixerReceiptProbe runs alone with only the exact owned character/save and optional DfuRoot.' }
    if ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden') {
        throw 'BreezeTtsMixerReceiptProbe requires the owned PeopleOfTheIliacBayTest-9a5f197b / People - Gothway Garden fixture.'
    }
}
$ownedProbeModes = @(
    'SplitPortraitRuntimeProbe',
    'LiveDialoguePresentationProbe',
    'NpcHealthCareProbe',
    'VerbalCourtProbe',
    'OutdoorSceneProbe',
    'NpcHomeMealProbe',
    'NpcHomeReturnProbe',
    'NpcIdentityIntegrationProbe', 'SocialDialogueStreamingProbe',
    'BreezeTtsSettingsProbe', 'NativeTopicTtsProbe', 'NpcExperienceAppraisalTransportProbe',
    'ExperienceDeferredInboxSerializationProbe',
    'TtsStreamingAcceptanceProbe', 'SpeechRevealPresentationProbe', 'NpcAcuteFearProbe', 'NpcResentmentRecoveryProbe', 'KeeperWorkDutyProbe', 'NativeNpcPoisonExperienceProbe', 'NativeNpcPoisonAppraisalProbe',
    'DecisionRoutingAppraisalReopenProbe', 'DecisionRoutingSafeDowngradeProbe', 'GoalQwenWillingnessProbe', 'NpcActionChatQwenWillingnessProbe',
    'SocialDuplicateReplyProbe', 'TempleTrainingProbe'
)
$selectedOwnedProbeModes = @($ownedProbeModes | Where-Object { (Get-Variable -Name $_ -ValueOnly) -eq $true })
if ($selectedOwnedProbeModes.Count -gt 1) { throw 'These controlled probes must run one at a time.' }
if ($selectedOwnedProbeModes.Count -eq 1) {
    $allowedOwnedProbeOptions = @($selectedOwnedProbeModes[0], 'DfuRoot', 'FixtureCharacter', 'FixtureSaveName')
    if ($selectedOwnedProbeModes[0] -eq 'GoalQwenWillingnessProbe') { $allowedOwnedProbeOptions += 'GoalQwenBoundGoal' }
    if ($selectedOwnedProbeModes[0] -eq 'NpcActionChatQwenWillingnessProbe') {
        $allowedOwnedProbeOptions += @('DailyItineraryProbe', 'PhysicalDailyRoutine', 'DailyInteriorOnly', 'NpcActionProbe', 'NpcActionChatProbe')
    }
    if ($selectedOwnedProbeModes[0] -eq 'DecisionRoutingAppraisalReopenProbe') { $allowedOwnedProbeOptions += 'DecisionRoutingAppraisalReopenSpeech' }
    $unexpectedOwnedProbeOptions = @($PSBoundParameters.Keys | Where-Object { $_ -notin $allowedOwnedProbeOptions })
    if ($unexpectedOwnedProbeOptions.Count -gt 0) {
        throw ($selectedOwnedProbeModes[0] + ' runs alone with only the exact owned character/save and optional DfuRoot.')
    }
    $expectedOwnedSave = if ($VerbalCourtProbe) { 'People - Daggerfall day 2' } else { 'People - Gothway Garden' }
    if ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne $expectedOwnedSave) {
        throw ($selectedOwnedProbeModes[0] + ' requires the owned PeopleOfTheIliacBayTest-9a5f197b / ' + $expectedOwnedSave + ' fixture.')
    }
}
if ($GoalQwenBoundGoal -and -not $GoalQwenWillingnessProbe) { throw 'GoalQwenBoundGoal is a controlled downstream arm of GoalQwenWillingnessProbe.' }
if ($DecisionRoutingAppraisalReopenSpeech -and -not $DecisionRoutingAppraisalReopenProbe) {
    throw 'DecisionRoutingAppraisalReopenSpeech requires DecisionRoutingAppraisalReopenProbe.'
}
if ($DecisionRoutingAppraisalReopenSpeech -and $DecisionRoutingSafeDowngradeProbe) {
    throw 'DecisionRoutingAppraisalReopenSpeech cannot be combined with DecisionRoutingSafeDowngradeProbe.'
}
if ($NpcActionChatQwenWillingnessProbe -and
    (-not $NpcActionProbe -or -not $NpcActionChatProbe -or -not $DailyItineraryProbe -or
     -not $PhysicalDailyRoutine -or -not $DailyInteriorOnly -or $NpcBeerProbe)) {
    throw 'NpcActionChatQwenWillingnessProbe requires the physical daily fixture, NpcActionProbe, and NpcActionChatProbe without beer.'
}
if ($NpcActionChatQwenWillingnessProbe -and
    ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) {
    throw 'NpcActionChatQwenWillingnessProbe requires the owned Gothway Garden fixture.'
}
if ($LocationBlockProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw 'LocationBlockProbe requires the owned PeopleOfTheIliacBayTest-9a5f197b / People - Gothway Garden fixture.' }
if ($LocationBlockProbe -and $PerformanceProbe) { throw 'Location block parity and cold performance must run in separate processes.' }

function Find-LogMarkerIndex([string[]]$Lines, [string]$Marker, [int]$StartIndex = 0) {
    for ($index = $StartIndex; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index].Contains($Marker)) { return $index }
    }
    return -1
}

function Test-SocialEffectSemanticTrace([string]$Text, [string]$Kind, [string]$Intent) {
    $lines = $Text.Replace("`r",'').Split([char]10)
    $intentIndices = @(
        for ($index = 0; $index -lt $lines.Count; $index++) {
            if ($lines[$index].StartsWith('PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_INTENT:',[StringComparison]::Ordinal)) { $index }
        }
    )
    if ($intentIndices.Count -eq 0) { return $false }

    # Intent is logged only after a successful structured classifier result;
    # The preceding scope row must belong to that same result.
    # Requiring the final adjacent pair ties this pass marker to the latest
    # completed turn and prevents an earlier trace from satisfying the probe.
    $intentIndex = [int]$intentIndices[-1]
    if ($intentIndex -eq 0 -or -not $lines[$intentIndex].EndsWith('; social=' + $Intent,[StringComparison]::Ordinal)) {
        return $false
    }
    $expectedScope = 'PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_EFFECT_SCOPE: proposed=' + $Kind + '; scope=PLAYER|DIRECT'
    return $lines[$intentIndex - 1] -ceq $expectedScope
}

if ($CommerceRestartPrepare -and $CommerceRestartRead) { throw 'Choose prepare or resume, not both.' }
if ($AppointmentNoShowProbe -and $AppointmentProbe) { throw 'Choose attendance or no-show appointment acceptance, not both.' }
if ($TaskFailureFeedbackProbe -and $ShopCommerceChatProbe) { throw 'Choose native task-failure feedback or shop-commerce chat, not both.' }
if ($SemanticConversationProbe -and ($ShopCommerceChatProbe -or $NpcActionChatProbe)) { throw 'Run the semantic conversation capture separately so its request/response JSONL contains only the five owned probe turns.' }
if (($CommerceRestartPrepare -or $CommerceRestartRead) -and $FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b') { throw 'Commerce restart requires the explicitly owned PeopleOfTheIliacBayTest-9a5f197b fixture.' }
if ($CommerceRestartPrepare) { $ShopCommerceProbe = $true }
if ($ShopCommerceChatProbe) { $ShopCommerceProbe = $true }
if ($TaskReplyFallbackProbe) { throw 'TaskReplyFallbackProbe is retired: accepted-task replies now bypass the second model call. Use -ConversationReplyOutcomeProbe for native acknowledgment checks.' }
if ($GoalTwoPass -and $GoalChainLength -notin @(3,4,5)) { throw 'GoalTwoPass requires -GoalChainLength 3, 4, or 5.' }
if ($TaskFailureFeedbackProbe) { $ShopCommerceProbe = $true }
if ($RoutineRecoveryPhysicalProbe) { $ShopCommerceProbe = $true }
if ($RoutineRecoveryDayProbe) { $RoutineRecoveryPhysicalProbe = $true; $ShopCommerceProbe = $true }
if ($NpcStartingBelongingsSceneProbe -and -not $FixtureCharacter) { throw 'Starting-belongings scene probe requires an owned disposable fixture character.' }
if ($DialogueRefusalProbe -and (-not $FixtureCharacter -or (-not (([string]$FixtureCharacter).StartsWith('PeopleOfTheIliacBayTest-',[StringComparison]::OrdinalIgnoreCase))))) { throw 'Dialogue refusal probe requires an owned disposable fixture character.' }
if ($SemanticConversationProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "Semantic conversation probe requires the owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' fixture." }
if ($ConversationContextSnapshotProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "Context snapshot probe requires the owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' fixture." }
if ($DecisionShadowProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "Decision shadow probe requires the owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' fixture." }
if ($DecisionRoutingProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "Decision routing probe requires the owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' fixture." }
if ($DecisionRoutingAppraisalReopenProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "Decision routing appraisal/reopen probe requires the owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' fixture." }
if ($DecisionRoutingSafeDowngradeProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "Decision routing safe-downgrade probe requires the owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' fixture." }
if ($DecisionWillingnessProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "Decision willingness probe requires the owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' fixture." }
if (($GoalBindingProbe -or $GoalQwenWillingnessProbe) -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw 'Goal binding/Qwen willingness probes require the owned Gothway Garden fixture.' }
if ($GoalBindingProbe -and ($DecisionShadowProbe -or $DecisionRoutingProbe -or $DecisionWillingnessProbe)) { throw 'Goal binding probe must run separately.' }
if (($GoalSequenceProbe -or $GoalCancellationProbe -or $GoalChainLengthProbe -or $GoalTwoPass) -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw 'Goal sequence/cancellation/chain-length probes require the owned Gothway Garden fixture.' }
if (@(@($DecisionShadowProbe, $DecisionRoutingProbe, $DecisionRoutingAppraisalReopenProbe, $DecisionRoutingSafeDowngradeProbe, $DecisionWillingnessProbe, $GoalBindingProbe, $GoalQwenWillingnessProbe, $GoalSequenceProbe, $GoalCancellationProbe, ($GoalChainLengthProbe -and -not $GoalTwoPass), $GoalTwoPass) | Where-Object { $_ }).Count -gt 1) { throw 'Run decision and goal probe modes separately.' }
if ($DecisionShadowCaptureDirectory -and -not $DecisionShadowProbe) { throw 'DecisionShadowCaptureDirectory requires DecisionShadowProbe.' }
if ($DecisionShadowWaitCandidateCapture -and -not $DecisionShadowProbe) { throw 'DecisionShadowWaitCandidateCapture requires DecisionShadowProbe.' }
if ($DecisionShadowWaitCandidateCapture -and -not $DecisionShadowCaptureDirectory) { throw 'DecisionShadowWaitCandidateCapture requires DecisionShadowCaptureDirectory.' }
if ($DecisionShadowCaptureDirectory) {
    if (-not [System.IO.Path]::IsPathRooted($DecisionShadowCaptureDirectory) -or
        $DecisionShadowCaptureDirectory.Contains('"') -or
        $DecisionShadowCaptureDirectory.Contains("`r") -or $DecisionShadowCaptureDirectory.Contains("`n")) {
        throw 'DecisionShadowCaptureDirectory must be an absolute path without quote or newline characters.'
    }
    $DecisionShadowCaptureDirectory = [System.IO.Path]::GetFullPath($DecisionShadowCaptureDirectory)
}
if (($ConversationContextLifecycleProbe -or $ConversationContextSpeakerProbe) -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "Context lifecycle/speaker probes require the owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' fixture." }
if ($KeeperWorkDutyProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "KeeperWorkDutyProbe requires the exact owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' baseline." }
if ($ConversationContextLifecycleProbe) { $ConversationContextSnapshotProbe = $true }
if ($ConversationReplyOutcomeProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "Reply outcome probe requires the owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' fixture." }
if ($FriendshipProgressionProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "Friendship progression probe requires the owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' fixture." }
if ($FriendshipProgressionProbe) {
    $unexpectedFriendshipOptions = @($PSBoundParameters.Keys | Where-Object { $_ -notin @('FriendshipProgressionProbe','FixtureCharacter','FixtureSaveName','DfuRoot') })
    if ($unexpectedFriendshipOptions.Count -gt 0) { throw 'FriendshipProgressionProbe runs alone with only the owned fixture character/save and optional DfuRoot.' }
}
if ($SocialDuplicateReplyProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "Social duplicate reply probe requires the owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' fixture." }
if ($ConversationMoodProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "Conversation mood probe requires the owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' fixture." }
if ($ConversationMoodProbe) {
    $unexpectedMoodOptions = @($PSBoundParameters.Keys | Where-Object { $_ -notin @('ConversationMoodProbe','FixtureCharacter','FixtureSaveName','DfuRoot') })
    if ($unexpectedMoodOptions.Count -gt 0) { throw 'ConversationMoodProbe runs alone with only the owned fixture character/save and optional DfuRoot.' }
}
if ($ConversationActionMoodProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "Conversation action mood probe requires the owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' fixture." }
if ($ConversationActionMoodProbe) {
    $unexpectedActionMoodOptions = @($PSBoundParameters.Keys | Where-Object { $_ -notin @('ConversationActionMoodProbe','FixtureCharacter','FixtureSaveName','DfuRoot') })
    if ($unexpectedActionMoodOptions.Count -gt 0) { throw 'ConversationActionMoodProbe runs alone with only the owned fixture character/save and optional DfuRoot.' }
}
if ($ActionWordingProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw "Action wording probe requires the owned PeopleOfTheIliacBayTest-9a5f197b / 'People - Gothway Garden' fixture." }
if ($ActionWordingProbe) {
    $unexpectedActionWordingOptions = @($PSBoundParameters.Keys | Where-Object { $_ -notin @('ActionWordingProbe','FixtureCharacter','FixtureSaveName','DfuRoot') })
    if ($unexpectedActionWordingOptions.Count -gt 0) { throw 'ActionWordingProbe runs alone with only the owned fixture character/save and optional DfuRoot.' }
}
if ($DialogueAvailabilityProbe -and (-not $FixtureCharacter -or (-not (([string]$FixtureCharacter).StartsWith('PeopleOfTheIliacBayTest-',[StringComparison]::Ordinal) -and ([string]$FixtureCharacter).Length -eq ('PeopleOfTheIliacBayTest-'.Length+8) -and (Test-HexValue ([string]$FixtureCharacter).Substring('PeopleOfTheIliacBayTest-'.Length) 8 -Lowercase))) -or -not $FixtureSaveName)) { throw 'Dialogue availability probe requires an owned disposable fixture character and an explicit city save.' }
if ($MoveToWaitProbe -and (-not $FixtureCharacter -or (-not (([string]$FixtureCharacter).StartsWith('PeopleOfTheIliacBayTest-',[StringComparison]::OrdinalIgnoreCase))))) { throw 'MoveTo/Wait probe requires an owned disposable fixture character.' }
if (($NpcInteractionProbe -or $NpcVitalsProbe) -and (-not $FixtureCharacter -or (-not (([string]$FixtureCharacter).StartsWith('PeopleOfTheIliacBayTest-',[StringComparison]::OrdinalIgnoreCase))))) { throw 'NPC interaction and vitals probes require an owned disposable fixture character.' }
if ($RoutineRecoverySceneChangeProbe -and -not $FixtureCharacter) { throw 'Routine recovery scene-change probe requires an owned disposable fixture character.' }
if ($AppointmentNoShowProbe -and -not $FixtureCharacter) { throw 'Appointment no-show probe requires an owned disposable fixture character.' }
if ($AppointmentPointProbe -and (-not $FixtureCharacter -or (-not (([string]$FixtureCharacter).StartsWith('PeopleOfTheIliacBayTest-',[StringComparison]::OrdinalIgnoreCase))))) { throw 'Appointment point probe requires an owned disposable fixture character.' }
if ($PhysicalDailyRoutine -and -not $DailyItineraryProbe) { throw 'PhysicalDailyRoutine requires DailyItineraryProbe.' }
if ($HouseholdJointBodyProbe -and (-not $DailyItineraryProbe -or $DailyInteriorOnly -or $PhysicalDailyRoutine -or $FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -notin @('People - Gothway Garden family dawn','People - Morning daily routines'))) { throw 'HouseholdJointBodyProbe requires an owned family dawn or prepared morning daily fixture alone.' }
if ($HouseholdInnPhysicalProbe -and (-not $DailyItineraryProbe -or $DailyInteriorOnly -or $PhysicalDailyRoutine -or $HouseholdJointBodyProbe -or $FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden family dawn')) { throw 'HouseholdInnPhysicalProbe requires the owned Gothway Garden family dawn daily fixture alone.' }
if ($HouseholdInnPhysicalProbe) {
    $familyInnAllowedParameters=@('HouseholdInnPhysicalProbe','DailyItineraryProbe','FixtureCharacter','FixtureSaveName','DfuRoot')
    foreach($boundName in $PSBoundParameters.Keys) { if($boundName -notin $familyInnAllowedParameters) { throw "HouseholdInnPhysicalProbe cannot combine with parameter $boundName." } }
}
if ($IndoorCatchupProbe -and -not $DailyItineraryProbe) { throw 'IndoorCatchupProbe requires DailyItineraryProbe.' }
if ($IndoorCrowdProbe -and -not $IndoorCatchupProbe) { throw 'IndoorCrowdProbe requires IndoorCatchupProbe.' }
if ($DailyInteriorOnly -and -not $PhysicalDailyRoutine) { throw 'DailyInteriorOnly requires PhysicalDailyRoutine.' }
if ($InnSeatOnly -and -not $DailyInteriorOnly) { throw 'InnSeatOnly requires DailyInteriorOnly.' }
if ($PrepareOutdoorFixture -and (-not $DailyItineraryProbe -or -not $FixtureCharacter -or -not $FixtureSaveName)) { throw 'PrepareOutdoorFixture requires DailyItineraryProbe and an explicit fixture character/save.' }
if ($NpcActionProbe -and -not $DailyInteriorOnly) { throw 'NpcActionProbe requires DailyInteriorOnly.' }
if ($NpcConversationCommandProbe -and (-not $NpcActionProbe -or -not $DailyItineraryProbe -or -not $PhysicalDailyRoutine -or $NpcBeerProbe -or $NpcActionChatProbe -or $FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) { throw 'NpcConversationCommandProbe requires the owned Gothway Garden physical daily interior action fixture without beer or model chat.' }
if ($NpcBeerProbe -and -not $NpcActionProbe) { throw 'NpcBeerProbe requires NpcActionProbe.' }
if ($NpcActionChatProbe -and -not $NpcActionProbe) { throw 'NpcActionChatProbe requires NpcActionProbe.' }
if ($NpcActionDiskProbe -and (-not $NpcActionProbe -or $NpcBeerProbe -or -not $FixtureCharacter)) { throw 'NpcActionDiskProbe requires an owned fixture and NpcActionProbe without NpcBeerProbe.' }
if ($TaskFailureFeedbackProbe -and -not $FixtureCharacter) { throw 'Native task probes require an explicitly owned disposable fixture character.' }
if ($DecisionWillingnessProbe -and ($DecisionShadowProbe -or $DecisionRoutingProbe)) { throw 'Decision willingness probe must run separately from shadow and routing probes.' }
if ($NpcActionChatProbe -or $ShopCommerceChatProbe -or $SemanticConversationProbe -or $DialogueRefusalProbe -or $DialogueAvailabilityProbe -or $DecisionRoutingProbe -or $DecisionRoutingAppraisalReopenProbe -or $DecisionRoutingSafeDowngradeProbe -or $DecisionWillingnessProbe -or $GoalBindingProbe -or $GoalQwenWillingnessProbe -or $GoalSequenceProbe -or $GoalCancellationProbe -or $GoalChainLengthProbe -or $GoalTwoPass -or $ActionWordingProbe -or $ConversationMoodProbe -or $ConversationActionMoodProbe) {
    try { $health = Invoke-RestMethod 'http://127.0.0.1:8080/health' -TimeoutSec 5 }
    catch { throw 'Semantic chat acceptance requires the local model server. Run tools/StartLocalDialogue.ps1 first.' }
    if ($health.status -ne 'ok') { throw 'Local model server is not ready for semantic chat acceptance.' }
    if ($DecisionWillingnessProbe -or $DecisionRoutingAppraisalReopenProbe -or $GoalBindingProbe -or $GoalChainLengthProbe -or $GoalTwoPass) {
        try { $d3Health = Invoke-RestMethod 'http://127.0.0.1:8093/health' -TimeoutSec 5 }
        catch { throw 'Decision willingness, appraisal/reopen, and goal chain acceptance require the owned D3 service on port 8093.' }
        if ($d3Health.status -ne 'ok') { throw 'Owned D3 service is not ready for decision willingness acceptance.' }
    }
}
if ($KnowledgeBrowseOnly -and -not $DialogueProbe) { throw 'KnowledgeBrowseOnly requires DialogueProbe.' }
if ($DialogueClockOnly -and (-not $DialogueProbe -or $KnowledgeBrowseOnly -or
    $FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) {
    throw 'DialogueClockOnly requires the exact owned dialogue fixture and cannot combine with KnowledgeBrowseOnly.'
}
if ($DialogueRefusalProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) {
    throw 'DialogueRefusalProbe requires the exact owned character and baseline save.'
}
if ($DialogueClockOnly -or $KnowledgeBrowseOnly -or $DialogueRefusalProbe) {
    $requiredDialoguePorts = @(8080)
    foreach ($port in $requiredDialoguePorts) {
        $clockHealth = Invoke-RestMethod -Uri ('http://127.0.0.1:' + $port + '/health') -TimeoutSec 10
        if ($clockHealth.status -ne 'ok') { throw ('Clock routing requires the ready local service on port ' + $port) }
    }
}
if ($GuardQuotaOnly -and -not $GateGuardProbe) { throw 'GuardQuotaOnly requires GateGuardProbe.' }
if ($RunningPilotProbe -and -not $FixtureCharacter) { throw 'Running pilot probe requires a disposable fixture character.' }
if ($InnAlarmProbe -and -not $FixtureCharacter) { throw 'Inn alarm probe requires a disposable fixture character.' }
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ResolveLocalDecisionModel.ps1')
. (Join-Path $PSScriptRoot 'ResolveLocalDialogueModel.ps1')
$player = Join-Path $DfuRoot 'Player/DaggerfallUnity.exe'
$playerDir = Split-Path $player -Parent
$log = Join-Path $playerDir 'PortableAppdata/Player.log'
if (Get-Process DaggerfallUnity -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $player }) { throw 'Isolated player already running; test will not interrupt it.' }

function Read-StrictDialogueSetting([object]$DialogueSettings, [string]$Name) {
    if ($null -eq $DialogueSettings -or $null -eq $DialogueSettings.PSObject.Properties[$Name]) {
        throw "Installed modsettings.json is missing Dialogue.$Name."
    }
    $raw = $DialogueSettings.PSObject.Properties[$Name].Value
    if ($raw -is [bool]) { return $raw }
    $parsed = $false
    if (-not [bool]::TryParse([string]$raw, [ref]$parsed)) {
        throw "Installed Dialogue.$Name must be a boolean, found '$raw'."
    }
    return $parsed
}

function Read-InstalledDialogueSettings {
    $manifestPath = Join-Path $repo 'mod/PeopleOfTheIliacBay.dfmod.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Mod manifest is missing: $manifestPath" }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace([string]$manifest.GUID)) { throw 'Mod manifest has no GUID.' }
    $settingsPath = Join-Path $DfuRoot ('Player/PortableAppdata/Mods/GameData/' + [string]$manifest.GUID + '/modsettings.json')
    if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) { throw "Installed mod settings are missing: $settingsPath" }
    $document = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json -ErrorAction Stop
    if ($null -eq $document.Values -or $null -eq $document.Values.Dialogue) { throw "Installed mod settings have no Dialogue section: $settingsPath" }
    return $document.Values.Dialogue
}

function Assert-LocalDialogueReady {
    $runtimeRoot = Join-Path $DfuRoot 'local-llm'
    $expectedModel = Resolve-LocalDialogueModel -RuntimeRoot $runtimeRoot
    try {
        $health = Invoke-RestMethod -Uri 'http://127.0.0.1:8080/health' -TimeoutSec 5 -ErrorAction Stop
        $props = Invoke-RestMethod -Uri 'http://127.0.0.1:8080/props' -TimeoutSec 5 -ErrorAction Stop
    } catch { throw 'Social dialogue streaming requires an already-running, ready Qwen service at 127.0.0.1:8080.' }
    if ($health.status -ne 'ok' -or -not [string]::Equals([string]$props.model_path, $expectedModel, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Social dialogue streaming requires the ready local model selected by $runtimeRoot."
    }
}

function Assert-LocalVoiceReady {
    $speechStarter = Join-Path $PSScriptRoot 'StartLocalSpeech.ps1'
    $persistentPath = Join-Path $DfuRoot 'Player/PortableAppdata'
    # ValidateOnly is deliberately read-only: it checks pinned assets, trusted
    # profiles, endpoint ownership, and health without starting or changing a process.
    & $speechStarter -DfuRoot $DfuRoot -PersistentDataPath $persistentPath -ValidateOnly -RequireServerReady | Out-Host
}

# Native poison capture does not route dialogue or require dialogue/model settings.
if ($selectedOwnedProbeModes.Count -eq 1 -and -not $OutdoorSceneProbe -and -not $NpcHealthCareProbe -and -not $VerbalCourtProbe -and -not $NpcHomeMealProbe -and -not $NpcHomeReturnProbe -and -not $SplitPortraitRuntimeProbe -and -not $NativeNpcPoisonExperienceProbe) {
    $dialogueSettings = Read-InstalledDialogueSettings
    $freeTextEnabled = Read-StrictDialogueSetting $dialogueSettings 'EnableFreeText'
    $voiceEnabled = Read-StrictDialogueSetting $dialogueSettings 'EnableNpcVoice'
    $socialStreamingEnabled = Read-StrictDialogueSetting $dialogueSettings 'EnableSocialSentenceStreaming'
    $prewarmEnabled = Read-StrictDialogueSetting $dialogueSettings 'PrewarmModel'
    $needsQwen = $selectedOwnedProbeModes[0] -notin @('NpcHealthCareProbe', 'KeeperWorkDutyProbe', 'NativeNpcPoisonExperienceProbe', 'SocialDuplicateReplyProbe', 'ConversationReplyOutcomeProbe') -and
        ($selectedOwnedProbeModes[0] -in @('SocialDialogueStreamingProbe', 'NpcActionChatQwenWillingnessProbe', 'NpcExperienceAppraisalTransportProbe', 'NativeNpcPoisonAppraisalProbe', 'DecisionRoutingAppraisalReopenProbe', 'DecisionRoutingSafeDowngradeProbe') -or ($freeTextEnabled -and $prewarmEnabled))
    $needsVoice = $selectedOwnedProbeModes[0] -notin @('NpcHealthCareProbe', 'KeeperWorkDutyProbe', 'NativeNpcPoisonExperienceProbe', 'NativeNpcPoisonAppraisalProbe', 'ConversationReplyOutcomeProbe') -and
        $selectedOwnedProbeModes[0] -notin @('DecisionRoutingSafeDowngradeProbe') -and
        ($voiceEnabled -or $selectedOwnedProbeModes[0] -in @('SocialDialogueStreamingProbe', 'BreezeTtsSettingsProbe', 'NativeTopicTtsProbe', 'TtsStreamingAcceptanceProbe'))
    if ($needsQwen) { Assert-LocalDialogueReady }
    if ($needsVoice) { Assert-LocalVoiceReady }
    switch ($selectedOwnedProbeModes[0]) {
        'LiveDialoguePresentationProbe' {
            if (-not $freeTextEnabled -or -not $voiceEnabled -or $socialStreamingEnabled -or $prewarmEnabled -or
                (Read-StrictDialogueSetting $dialogueSettings 'EnableDecisionPlanner')) {
                throw 'LiveDialoguePresentationProbe requires free text and voice enabled, sentence streaming, prewarm and decision planner disabled.'
            }
            Assert-LocalDialogueReady
        }
        'KeeperWorkDutyProbe' {
            # Read-only native context probe; no Qwen or TTS service is required.
        }
        'SocialDuplicateReplyProbe' {
            if (-not $freeTextEnabled -or $voiceEnabled -or $prewarmEnabled -or $socialStreamingEnabled) {
                throw 'SocialDuplicateReplyProbe requires EnableFreeText=true, EnableNpcVoice=false, PrewarmModel=false, and EnableSocialSentenceStreaming=false.'
            }
        }
        'NpcActionChatQwenWillingnessProbe' {
            if (-not $freeTextEnabled -or $voiceEnabled -or $prewarmEnabled -or $socialStreamingEnabled -or
                (Read-StrictDialogueSetting $dialogueSettings 'EnableDecisionPlanner')) {
                throw 'NpcActionChatQwenWillingnessProbe requires EnableFreeText=true, EnableNpcVoice=false, PrewarmModel=false, EnableSocialSentenceStreaming=false, and EnableDecisionPlanner=false.'
            }
            $timeoutMilliseconds = 0
            $timeoutRaw = $dialogueSettings.PSObject.Properties['TimeoutMilliseconds'].Value
            if (-not [int]::TryParse([string]$timeoutRaw, [ref]$timeoutMilliseconds) -or $timeoutMilliseconds -ne 300000) {
                throw 'NpcActionChatQwenWillingnessProbe requires TimeoutMilliseconds=300000.'
            }
        }
        'NativeNpcPoisonExperienceProbe' {
            # Native poison-event probe; no Qwen or TTS service is required.
        }
        'NativeNpcPoisonAppraisalProbe' {
            $appraisalModel = Resolve-LocalDialogueModel -RuntimeRoot (Join-Path $DfuRoot 'local-llm')
            if ((-not ([IO.Path]::GetFileName($appraisalModel).StartsWith('gemma',[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($appraisalModel).EndsWith('.gguf',[StringComparison]::OrdinalIgnoreCase)))) {
                throw 'NativeNpcPoisonAppraisalProbe requires the selected Gemma model.'
            }
            if (-not $freeTextEnabled -or $voiceEnabled -or $prewarmEnabled) {
                throw 'NativeNpcPoisonAppraisalProbe requires EnableFreeText=true, EnableNpcVoice=false, and PrewarmModel=false.'
            }
        }
        'DecisionRoutingAppraisalReopenProbe' {
            $timeoutMilliseconds = 0
            $timeoutRaw = $dialogueSettings.PSObject.Properties['TimeoutMilliseconds'].Value
            if (-not [int]::TryParse([string]$timeoutRaw, [ref]$timeoutMilliseconds) -or $timeoutMilliseconds -ne 300000 -or
                -not $freeTextEnabled -or $prewarmEnabled -or
                (-not $DecisionRoutingAppraisalReopenSpeech -and $voiceEnabled) -or
                ($DecisionRoutingAppraisalReopenSpeech -and (-not $voiceEnabled -or $socialStreamingEnabled))) {
                if ($DecisionRoutingAppraisalReopenSpeech) {
                    throw 'DecisionRoutingAppraisalReopenSpeech requires EnableFreeText=true, EnableNpcVoice=true, EnableSocialSentenceStreaming=false, PrewarmModel=false, and TimeoutMilliseconds=300000.'
                }
                throw 'DecisionRoutingAppraisalReopenProbe requires EnableFreeText=true, EnableNpcVoice=false, PrewarmModel=false, and TimeoutMilliseconds=300000.'
            }
            $decisionModel = Resolve-LocalDecisionModel -RuntimeRoot (Join-Path $DfuRoot 'local-llm')
            $d3Model = $decisionModel.ModelPath
            if ($DecisionRoutingAppraisalReopenSpeech -and $decisionModel.Profile -ne 'nano') {
                throw 'Appraisal/reopen speech acceptance requires selected D3-Nano.'
            }
            if (-not (Test-Path -LiteralPath $d3Model -PathType Leaf)) { throw 'Decision routing appraisal/reopen probe requires the selected D3 model file.' }
            try { $d3Props = Invoke-RestMethod -Uri 'http://127.0.0.1:8093/props' -TimeoutSec 10 -ErrorAction Stop }
            catch { throw 'Decision routing appraisal/reopen probe requires the ready owned D3 service on port 8093.' }
            if ($d3Props.model_path -ne $d3Model) { throw 'Decision routing appraisal/reopen probe found another model loaded in the D3 service.' }
            if ($DecisionRoutingAppraisalReopenSpeech) {
                $dialogueRuntimeRoot = Join-Path $DfuRoot 'local-llm'
                $qwenSelectionPath = Join-Path $dialogueRuntimeRoot 'active-model.json'
                if (-not (Test-Path -LiteralPath $qwenSelectionPath -PathType Leaf)) { throw 'Appraisal/reopen speech acceptance requires an explicit selected Qwen GPU model.' }
                $qwenSelection = Get-Content -LiteralPath $qwenSelectionPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
                $qwenModel = Resolve-LocalDialogueModel -RuntimeRoot $dialogueRuntimeRoot
                $expectedQwen3B = [IO.Path]::GetFullPath((Join-Path $dialogueRuntimeRoot 'models/qwen2.5-3b/qwen2.5-3b-instruct-q4_k_m.gguf'))
                if (-not [string]::Equals([string]$qwenSelection.executionDevice, 'GPU', [StringComparison]::OrdinalIgnoreCase) -or
                    -not [string]::Equals($qwenModel, $expectedQwen3B, [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Appraisal/reopen speech acceptance requires the selected Qwen2.5-3B model on GPU.'
                }
            }
        }
        'DecisionRoutingSafeDowngradeProbe' {
            $timeoutMilliseconds = 0
            $timeoutRaw = $dialogueSettings.PSObject.Properties['TimeoutMilliseconds'].Value
            if (-not [int]::TryParse([string]$timeoutRaw, [ref]$timeoutMilliseconds) -or $timeoutMilliseconds -ne 300000 -or
                -not $freeTextEnabled -or $voiceEnabled -or $prewarmEnabled) {
                throw 'DecisionRoutingSafeDowngradeProbe requires EnableFreeText=true, EnableNpcVoice=false, PrewarmModel=false, and TimeoutMilliseconds=300000.'
            }
        }
        'NpcIdentityIntegrationProbe' {
            # This probe has no model dependency. If user settings enable a
            # background prewarm or opening voice, those services were checked above.
        }
        'SocialDialogueStreamingProbe' {
            if (-not $freeTextEnabled -or -not $voiceEnabled -or -not $socialStreamingEnabled) {
                throw 'SocialDialogueStreamingProbe requires EnableFreeText, EnableNpcVoice, and EnableSocialSentenceStreaming already enabled in the installed settings.'
            }
        }
        'TtsStreamingAcceptanceProbe' {
            if (-not $voiceEnabled -or -not $socialStreamingEnabled) {
                throw 'TtsStreamingAcceptanceProbe requires EnableNpcVoice and EnableSocialSentenceStreaming already enabled in installed settings.'
            }
        }
        'SpeechRevealPresentationProbe' {
            if (-not $freeTextEnabled -or $voiceEnabled -or $prewarmEnabled) {
                throw 'SpeechRevealPresentationProbe requires EnableFreeText=true, EnableNpcVoice=false, and PrewarmModel=false; its isolated test bridge uses only the approved retained WAV.'
            }
        }
        { $_ -in @('NpcAcuteFearProbe', 'NpcResentmentRecoveryProbe') } {
            if ($freeTextEnabled -or $voiceEnabled -or $prewarmEnabled) {
                throw ($selectedOwnedProbeModes[0] + ' requires EnableFreeText=false, EnableNpcVoice=false, and PrewarmModel=false; it uses detached native state only.')
            }
        }
        { $_ -in @('BreezeTtsSettingsProbe', 'NativeTopicTtsProbe') } {
            if (-not $voiceEnabled -or $freeTextEnabled) {
                throw ($selectedOwnedProbeModes[0] + ' requires EnableNpcVoice=true and EnableFreeText=false in installed settings; TownTest does not rewrite settings.')
            }
        }
    }
}

New-Item -ItemType Directory -Path "$repo/work" -Force | Out-Null
if (Test-Path -LiteralPath $log) {
    Move-Item -LiteralPath $log -Destination (Join-Path "$repo/work" ('player-before-town-' + [Guid]::NewGuid().ToString('N') + '.log'))
}
$started = Get-Date
# Saving uses DFU's rendered screenshot coroutine, so this test needs graphics.
$arguments = '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -peopleoftheiliacbay-town-test'
if ($OutdoorNavigationProbe -and $env:POTIB_NAVMESH_TRIAL -eq '1' -and $env:POTIB_ROADMAP_PROBE -eq 'paid-companion') {
    $arguments += ' -peopleoftheiliacbay-paid-companion-probe'
}
if ($ShopCommerceChatProbe -or $NpcActionChatProbe -or $SemanticConversationProbe -or $KnowledgeBrowseOnly -or $DialogueClockOnly) {
    if ($SemanticConversationProbe -and ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden')) {
        throw 'Semantic classifier capture requires the owned PeopleOfTheIliacBayTest-9a5f197b / People - Gothway Garden fixture.'
    }
    $semanticCapturePath = Join-Path "$repo/work" 'semantic-action-model-capture.jsonl'
    if (($KnowledgeBrowseOnly -or $DialogueClockOnly) -and (Test-Path -LiteralPath $semanticCapturePath)) {
        $previousCapture = Join-Path "$repo/work" ('semantic-action-model-capture-before-' + [Guid]::NewGuid().ToString('N') + '.jsonl')
        Move-Item -LiteralPath $semanticCapturePath -Destination $previousCapture
    }
    # Chat integrations keep their existing cleanup behavior. The semantic
    # conversation probe resets lazily in ForStage(route-intent), after its
    # owned fixture and first native Talk submission have passed runtime checks.
    if (-not $SemanticConversationProbe -and (Test-Path -LiteralPath $semanticCapturePath)) {
        Remove-Item -LiteralPath $semanticCapturePath -Force
    }
    $arguments += ' -peopleoftheiliacbay-semantic-action-model-capture "' + $semanticCapturePath + '"'
}
if ($FreshPreviews) {
    if ($FixtureCharacter) { throw 'Fresh previews must start a new disposable character.' }
    $arguments += ' -peopleoftheiliacbay-fresh-previews'
}
if ($BreezeTtsCapacityProbe) {
    if ($FixtureCharacter -or $FixtureSaveName) { throw 'Breeze TTS capacity fixture owns isolated temporary roots and does not load or alter a saved character.' }
    $arguments += ' -peopleoftheiliacbay-breeze-tts-capacity-probe'
}
if ($FixtureCharacter) { $arguments += ' -peopleoftheiliacbay-fixture-character ' + $FixtureCharacter }
if ($PortraitVisemePreview -and -not ($BreezeTtsTrialProbe -or $TtsContrastManifest)) { throw 'PortraitVisemePreview requires the owned Breeze trial or contrast probe.' }
$questPersonLocationFixture = $QuestBindingProbe -and $env:POTIB_QUEST_PERSON_LOCATION_PROBE -eq '1' -and $FixtureCharacter -eq 'PeopleOfTheIliacBayTest-9a5f197b' -and $FixtureSaveName -eq 'People - Gothway Garden'
if ($FixtureSaveName -and -not $BreezeTtsTrialProbe -and -not $TtsContrastManifest) {
if (-not $FixtureCharacter -or -not ($questPersonLocationFixture -or $OutdoorSceneProbe -or $LocationBlockProbe -or $PerformanceProbe -or $TavernSurveyProbe -or $DailyItineraryProbe -or $OutdoorArrivalProbe -or $OutdoorNavigationProbe -or $ResidentTripProbe -or $CrossRegionTripProbe -or $GateGuardProbe -or $DialogueProbe -or $DialogueRefusalProbe -or $SemanticConversationProbe -or $ConversationContextSnapshotProbe -or $DecisionShadowProbe -or $DecisionRoutingProbe -or $DecisionRoutingAppraisalReopenProbe -or $DecisionRoutingSafeDowngradeProbe -or $DecisionWillingnessProbe -or $GoalBindingProbe -or $GoalQwenWillingnessProbe -or $GoalSequenceProbe -or $GoalCancellationProbe -or $GoalChainLengthProbe -or $GoalTwoPass -or $ConversationContextSpeakerProbe -or $KeeperWorkDutyProbe -or $NativeNpcPoisonExperienceProbe -or $NativeNpcPoisonAppraisalProbe -or $ConversationReplyOutcomeProbe -or $FriendshipProgressionProbe -or $SocialDuplicateReplyProbe -or $ConversationActionMoodProbe -or $ActionWordingProbe -or $DialogueAvailabilityProbe -or $MoveToWaitProbe -or $NpcInteractionProbe -or $NpcVitalsProbe -or $NpcHealthCareProbe -or $VerbalCourtProbe -or $NpcHomeMealProbe -or $NpcHomeReturnProbe -or $AppointmentProbe -or $AppointmentPointProbe -or $NpcLeadProbe -or $FollowTransitionProbe -or $ShopCommerceProbe -or $TempleTrainingProbe -or $NpcIdentityIntegrationProbe -or $SocialDialogueStreamingProbe -or $BreezeTtsSettingsProbe -or $NativeTopicTtsProbe -or $NpcExperienceAppraisalTransportProbe -or $ExperienceDeferredInboxSerializationProbe -or $TtsStreamingAcceptanceProbe -or $BreezeTtsMixerReceiptProbe -or $SpeechRevealPresentationProbe -or $SplitPortraitRuntimeProbe -or $LiveDialoguePresentationProbe -or $NpcAcuteFearProbe -or $NpcResentmentRecoveryProbe)) { throw 'FixtureSaveName requires FixtureCharacter and a city probe.' }
    $arguments += ' -peopleoftheiliacbay-fixture-save "' + $FixtureSaveName + '"'
}
if ($QuestProbe) { $arguments += ' -peopleoftheiliacbay-quest-probe' }
if ($LocationBlockProbe) { $arguments += ' -peopleoftheiliacbay-location-block-probe' }
if ($PerformanceProbe) {
    if (-not $FixtureCharacter) { throw 'Performance probe requires a disposable fixture character.' }
    $arguments += ' -peopleoftheiliacbay-performance-probe'
}
if ($NavigationTiming -and -not $PerformanceProbe) { $arguments += ' -peopleoftheiliacbay-performance-probe' }
if ($HauntingDawnProbe) {
    if (-not $FixtureCharacter) { throw 'Haunting dawn probe requires a disposable fixture character.' }
    $arguments += ' -peopleoftheiliacbay-haunting-dawn-probe'
}
if ($CrossRegionTripProbe) {
    if (-not $FixtureCharacter) { throw "Cross-region probe requires a disposable fixture character." }
    $arguments += " -peopleoftheiliacbay-cross-region-trip-probe"
}
if ($ResidentTripProbe) {
    if (-not $FixtureCharacter) { throw 'Resident trip probe requires a disposable fixture character.' }
    $arguments += ' -peopleoftheiliacbay-resident-trip-probe'
}
if ($InnAlarmProbe) { $arguments += ' -peopleoftheiliacbay-inn-alarm-probe' }
if ($BedProbe) { $arguments += ' -peopleoftheiliacbay-bed-probe' }
if ($SleepProbe) { $arguments += ' -peopleoftheiliacbay-sleep-probe' }
if ($RunningPilotProbe) {
    $arguments += ' -peopleoftheiliacbay-running-pilot-probe'
}
if ($ResponseProbe) { $arguments += ' -peopleoftheiliacbay-response-probe' }
if ($GuardActorProbe) { $arguments += ' -peopleoftheiliacbay-guard-actor-probe' }
if ($GuardControllerProbe) { $arguments += ' -peopleoftheiliacbay-guard-controller-probe' }
if ($GuardPatrolProbe) { $arguments += ' -peopleoftheiliacbay-guard-patrol-probe' }
if ($GuardDefenseProbe) { $arguments += ' -peopleoftheiliacbay-guard-defense-probe' }
if ($TavernSurveyProbe) { $arguments += ' -peopleoftheiliacbay-tavern-survey-probe' }
if ($CityTravelerProbe) { $arguments += ' -peopleoftheiliacbay-city-traveler-probe' }
if ($GateGuardProbe) { $arguments += ' -peopleoftheiliacbay-gate-guard-probe' }
if ($GuardQuotaOnly) { $arguments += ' -peopleoftheiliacbay-guard-quota-only' }
if ($WallQuarterProbe) { $arguments += ' -peopleoftheiliacbay-wall-quarter-probe' }
if ($StationaryNpcProbe) { $arguments += ' -peopleoftheiliacbay-stationary-probe' }
if ($DialogueProbe) { $arguments += ' -peopleoftheiliacbay-dialogue-probe' }
if ($DialogueClockOnly) { $arguments += ' -peopleoftheiliacbay-dialogue-clock-only' }
if ($DialogueRefusalProbe) {
    $arguments += ' -peopleoftheiliacbay-dialogue-refusal-probe'
}
if ($SemanticConversationProbe) { $arguments += ' -peopleoftheiliacbay-semantic-conversation-probe' }
if ($ConversationContextSnapshotProbe) { $arguments += ' -peopleoftheiliacbay-context-snapshot-probe' }
if ($DecisionShadowProbe) { $arguments += ' -peopleoftheiliacbay-decision-shadow-probe' }
if ($DecisionRoutingAppraisalReopenProbe) {
    $arguments += ' -peopleoftheiliacbay-decision-routing-appraisal-reopen-probe -peopleoftheiliacbay-decision-routing-probe -peopleoftheiliacbay-decision-routing -peopleoftheiliacbay-decision-model "' + $d3Model + '" -peopleoftheiliacbay-decision-endpoint http://127.0.0.1:8093/v1/systemone'
}
if ($DecisionRoutingAppraisalReopenSpeech) { $arguments += ' -peopleoftheiliacbay-appraisal-reopen-speech-probe' }
if ($DecisionRoutingSafeDowngradeProbe) {
    $arguments += ' -peopleoftheiliacbay-decision-routing-appraisal-reopen-probe -peopleoftheiliacbay-decision-routing-safe-downgrade-probe'
}
if ($DecisionRoutingProbe) { $arguments += ' -peopleoftheiliacbay-decision-routing-probe' }
if ($DecisionWillingnessProbe) { $arguments += ' -peopleoftheiliacbay-decision-willingness-probe' }
if ($GoalBindingProbe) { $arguments += ' -peopleoftheiliacbay-goal-binding-probe -peopleoftheiliacbay-goal-extraction' }
if ($GoalQwenWillingnessProbe) { $arguments += ' -peopleoftheiliacbay-goal-qwen-willingness-probe -peopleoftheiliacbay-goal-binding-probe -peopleoftheiliacbay-goal-extraction -peopleoftheiliacbay-qwen-willingness' }
if ($NpcActionChatQwenWillingnessProbe) { $arguments += ' -peopleoftheiliacbay-qwen-willingness' }
if ($GoalQwenBoundGoal) { $arguments += ' -peopleoftheiliacbay-goal-qwen-bound-probe' }
if ($GoalSequenceProbe) { $arguments += ' -peopleoftheiliacbay-goal-sequence-probe -peopleoftheiliacbay-goal-extraction' }
if ($GoalCancellationProbe) { $arguments += ' -peopleoftheiliacbay-goal-cancellation-probe -peopleoftheiliacbay-goal-extraction' }
if ($GoalChainLengthProbe) { $arguments += ' -peopleoftheiliacbay-goal-chain-length-probe -peopleoftheiliacbay-goal-chain-steps ' + $GoalChainLength + ' -peopleoftheiliacbay-goal-extraction' }
if ($GoalTwoPass) { $arguments += ' -peopleoftheiliacbay-goal-two-pass -peopleoftheiliacbay-goal-two-pass-probe' }
if ($DecisionShadowCaptureDirectory) {
    $arguments += ' -peopleoftheiliacbay-decision-shadow-capture-directory "' + $DecisionShadowCaptureDirectory + '"'
}
if ($DecisionShadowWaitCandidateCapture) { $arguments += ' -peopleoftheiliacbay-decision-shadow-wait-candidate-capture' }
if ($ConversationContextLifecycleProbe) { $arguments += ' -peopleoftheiliacbay-context-lifecycle-probe' }
if ($ConversationReplyOutcomeProbe) { $arguments += ' -peopleoftheiliacbay-conversation-reply-outcome-probe' }
if ($FriendshipProgressionProbe) { $arguments += ' -peopleoftheiliacbay-friendship-progression-probe' }
if ($ConversationMoodProbe) { $arguments += ' -peopleoftheiliacbay-conversation-mood-probe' }
if ($SocialDuplicateReplyProbe) { $arguments += ' -peopleoftheiliacbay-social-duplicate-reply-probe' }
if ($NpcAcuteFearProbe) { $arguments += ' -peopleoftheiliacbay-npc-acute-fear-probe' }
if ($NpcResentmentRecoveryProbe) { $arguments += ' -peopleoftheiliacbay-npc-resentment-recovery-probe' }
if ($NpcExperienceAppraisalTransportProbe) { $arguments += ' -peopleoftheiliacbay-experience-appraisal-transport-probe' }
if ($ExperienceDeferredInboxSerializationProbe) { $arguments += ' -peopleoftheiliacbay-experience-inbox-deferred-serialization-probe' }
if ($NpcIdentityIntegrationProbe) { $arguments += ' -peopleoftheiliacbay-npc-identity-integration-probe' }
if ($SplitPortraitRuntimeProbe) { $arguments += ' -peopleoftheiliacbay-split-portrait-probe' }
if ($LiveDialoguePresentationProbe) { $arguments += ' -peopleoftheiliacbay-live-dialogue-presentation-probe' }
if ($SocialDialogueStreamingProbe) { $arguments += ' -peopleoftheiliacbay-social-dialogue-stream-probe' }
if ($BreezeTtsSettingsProbe) { $arguments += ' -peopleoftheiliacbay-breeze-tts-settings-probe' }
if ($NativeTopicTtsProbe) { $arguments += ' -peopleoftheiliacbay-native-topic-tts-probe' }
if ($TtsStreamingAcceptanceProbe) { $arguments += ' -peopleoftheiliacbay-tts-streaming-acceptance-probe' }
if ($SocialDialogueStreamingProbe -or $TtsStreamingAcceptanceProbe) {
    $arguments += ' -peopleoftheiliacbay-allow-legacy-speech-streaming'
}
if ($BreezeTtsMixerReceiptProbe) { $arguments += ' -peopleoftheiliacbay-breeze-tts-mixer-receipt-probe' }
if ($SpeechRevealPresentationProbe) {
    if ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden') {
        throw 'SpeechRevealPresentationProbe requires the owned PeopleOfTheIliacBayTest-9a5f197b / People - Gothway Garden fixture.'
    }
    $retainedSpeechWav = Join-Path $repo 'work/breeze-tts-20261021/race-accents-02/accent-breton-female-v2.wav'
    if (-not (Test-Path -LiteralPath $retainedSpeechWav) -or
        (Get-FileHash -LiteralPath $retainedSpeechWav -Algorithm SHA256).Hash.ToLowerInvariant() -ne '1a52a457dc298d1ad0c3f594d373f5dd0c45a021051838b17540709fbdbf9a06') {
        throw 'SpeechRevealPresentationProbe requires the approved retained Breton female v2 WAV with its recorded SHA-256.'
    }
    $arguments += ' -peopleoftheiliacbay-speech-reveal-presentation-probe -peopleoftheiliacbay-speech-reveal-retained-wav "' + $retainedSpeechWav + '"'
}
if ($BreezeTtsTrialProbe -or $TtsContrastManifest) {
    if ($FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b' -or $FixtureSaveName -ne 'People - Gothway Garden') { throw 'Breeze trial requires the owned Gothway Garden fixture.' }
    if (-not [IO.Path]::IsPathRooted($TtsTrialDirectory) -or $TtsTrialDirectory.Contains('"')) { throw 'Breeze trial requires an absolute directory without quotes.' }
    $trialRoot = [IO.Path]::GetFullPath($TtsTrialDirectory)
    $workRoot = [IO.Path]::GetFullPath((Join-Path $repo 'work')) + [IO.Path]::DirectorySeparatorChar
    if (-not $trialRoot.StartsWith($workRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Breeze trial directory must be inside this checkout work directory.' }
    if ((Test-Path -LiteralPath $trialRoot) -and @(Get-ChildItem -LiteralPath $trialRoot -Force).Count -ne 0) { throw 'Breeze trial needs a fresh empty directory.' }
    $ttsHealth = Invoke-RestMethod 'http://127.0.0.1:8080/health' -TimeoutSec 5
    if ($ttsHealth.status -ne 'ok') { throw 'Qwen is not ready for Breeze trial.' }
    $ttsD3 = & (Join-Path $PSScriptRoot 'StartLocalDecision.ps1')
    $ttsD3Model = (Resolve-LocalDecisionModel -RuntimeRoot (Join-Path $DfuRoot 'local-llm')).ModelPath
    $arguments += ' -peopleoftheiliacbay-fixture-save "' + $FixtureSaveName + '" -peopleoftheiliacbay-tts-trial "' + $trialRoot + '"'
    if ($PortraitVisemePreview) { $arguments += ' -peopleoftheiliacbay-portrait-visemes' }
    if ($TtsContrastManifest) {
        if ($BreezeTtsTrialProbe -or -not [IO.Path]::IsPathRooted($TtsContrastManifest) -or $TtsContrastManifest.Contains('"') -or -not (Test-Path -LiteralPath $TtsContrastManifest -PathType Leaf)) { throw 'Contrast requires its own existing absolute manifest and cannot combine with conversation pilot.' }
        $arguments += ' -peopleoftheiliacbay-tts-contrast-manifest "' + [IO.Path]::GetFullPath($TtsContrastManifest) + '"'
    } else { $arguments += ' -peopleoftheiliacbay-breeze-tts-probe' }
    $arguments += ' -peopleoftheiliacbay-decision-routing -peopleoftheiliacbay-decision-model "' + $ttsD3Model + '" -peopleoftheiliacbay-decision-endpoint http://127.0.0.1:8093/v1/systemone'
}
if ($ConversationActionMoodProbe) { $arguments += ' -peopleoftheiliacbay-action-wording -peopleoftheiliacbay-conversation-action-mood-probe' }
if ($ActionWordingProbe) { $arguments += ' -peopleoftheiliacbay-action-wording -peopleoftheiliacbay-action-wording-probe' }
if ($ConversationContextSpeakerProbe) { $arguments += ' -peopleoftheiliacbay-context-speaker-probe' }
if ($KeeperWorkDutyProbe) { $arguments += ' -peopleoftheiliacbay-keeper-work-duty-probe' }
if ($NativeNpcPoisonExperienceProbe) { $arguments += ' -peopleoftheiliacbay-native-poison-experience-probe' }
if ($NativeNpcPoisonAppraisalProbe) { $arguments += ' -peopleoftheiliacbay-native-poison-appraisal-probe' }
if ($DialogueAvailabilityProbe) { $arguments += ' -peopleoftheiliacbay-dialogue-availability-probe' }
if ($MoveToWaitProbe) { $arguments += $(if ($EnterWaitProbe) { ' -peopleoftheiliacbay-enter-wait-probe' } else { ' -peopleoftheiliacbay-move-to-wait-probe' }) }
if ($NpcLeadProbe) {
    if (-not $FixtureCharacter -or -not $FixtureSaveName) { throw 'Lead probe requires explicit disposable character and city save.' }
    $arguments += ' -peopleoftheiliacbay-lead-probe'
}
if ($NpcInteractionProbe) { $arguments += ' -peopleoftheiliacbay-npc-interaction-probe' }
if ($NpcVitalsProbe) { $arguments += ' -peopleoftheiliacbay-npc-vitals-probe' }
if ($NpcHealthCareProbe) { $arguments += ' -peopleoftheiliacbay-npc-healthcare-probe' }
if ($VerbalCourtProbe) { $arguments += ' -peopleoftheiliacbay-verbal-court-probe' }
if ($NpcHomeMealProbe) { $arguments += ' -peopleoftheiliacbay-home-meal-probe' }
if ($NpcHomeReturnProbe) { $arguments += ' -peopleoftheiliacbay-home-return-probe' }
if ($KnowledgeBrowseOnly) { $arguments += ' -peopleoftheiliacbay-knowledge-browse-only' }
if ($OccupiedApproachProbe) {
    if (-not $FixtureCharacter) { throw 'Occupied approach probe requires a disposable character.' }
    $arguments += ' -peopleoftheiliacbay-occupied-approach-probe'
}
if ($KeeperStandingProbe) {
    if (-not $FixtureCharacter) { throw 'Keeper standing probe requires a disposable fixture character.' }
    $arguments += ' -peopleoftheiliacbay-keeper-standing-probe'
}
if ($BusinessIdentityProbe) {
    if (-not $FixtureCharacter) { throw 'Business identity probe requires a disposable fixture character.' }
    $arguments += ' -peopleoftheiliacbay-business-identity-probe'
}
if ($FollowTransitionProbe) {
    if (-not $FixtureCharacter -or -not $FixtureSaveName) { throw 'Follow transition probe requires explicit disposable character and city save.' }
    $arguments += ' -peopleoftheiliacbay-follow-transition-probe'
}
if ($ShopCommerceProbe) {
    if (-not $FixtureCharacter) { throw 'Shop commerce probe requires a disposable character.' }
    $arguments += ' -peopleoftheiliacbay-shop-commerce-probe'
}
if ($TempleTrainingProbe) { $arguments += ' -peopleoftheiliacbay-temple-training-probe' }
if ($ShopLootSaleProbe) { $arguments += ' -peopleoftheiliacbay-shop-loot-sale-probe' }
if ($NamedOtherHandoverProbe) { $arguments += ' -peopleoftheiliacbay-named-other-handover-probe' }
if ($ShopCommerceChatProbe) { $arguments += ' -peopleoftheiliacbay-shop-commerce-chat-probe' }
if ($TaskFailureFeedbackProbe) { $arguments += ' -peopleoftheiliacbay-task-failure-probe' }
if ($RoutineRecoverySceneChangeProbe) { $arguments += ' -peopleoftheiliacbay-routine-recovery-scenechange-probe' }
if ($AppointmentNoShowProbe) { $arguments += ' -peopleoftheiliacbay-appointment-no-show-probe' }
if ($RoutineRecoveryPhysicalProbe) {
    if (-not $FixtureCharacter -or $FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b') { throw 'Physical routine recovery requires the owned PeopleOfTheIliacBayTest-9a5f197b fixture.' }
    $arguments += ' -peopleoftheiliacbay-routine-recovery-physical-probe'
}
if ($HomeContainerDepositProbe) {
    if (-not $FixtureCharacter -or (-not (([string]$FixtureCharacter).StartsWith('PeopleOfTheIliacBayTest-',[StringComparison]::OrdinalIgnoreCase)))) { throw 'Home container deposit probe requires an owned disposable fixture character.' }
    $arguments += ' -peopleoftheiliacbay-container-deposit-probe'
}
if ($RoutineRecoveryDayProbe) {
    if (-not $FixtureCharacter -or $FixtureCharacter -ne 'PeopleOfTheIliacBayTest-9a5f197b') { throw 'Recovery day probe requires the owned PeopleOfTheIliacBayTest-9a5f197b fixture.' }
    $arguments += ' -peopleoftheiliacbay-routine-recovery-day-probe'
}
if ($NpcStartingBelongingsSceneProbe) { $arguments += ' -peopleoftheiliacbay-npc-starting-belongings-scene-probe' }
if ($CommerceRestartPrepare -or $CommerceRestartRead) { $arguments += ' -peopleoftheiliacbay-commerce-restart-probe' }
if ($CommerceRestartRead) { $arguments += ' -peopleoftheiliacbay-commerce-restart-read ' + $CommerceRestartRead }
if ($AppointmentProbe) {
    if (-not $FixtureCharacter) { throw 'Appointment probe requires a disposable fixture character.' }
    $arguments += ' -peopleoftheiliacbay-appointment-probe'
}
if ($AppointmentPointProbe) {
    if (-not $FixtureSaveName) { throw 'Appointment point probe requires an explicit saved exterior fixture name.' }
    $arguments += ' -peopleoftheiliacbay-appointment-point-probe'
}
if ($InnLodgerHomeProbe -or $BedroomReplacementProbe) {
    if (-not $FixtureCharacter) { throw 'Inn lodger home probe requires a disposable fixture character.' }
    $arguments += ' -peopleoftheiliacbay-inn-lodger-home-probe'
}
if ($MovementClockProbe) {
    if (-not $FixtureCharacter) { throw 'Movement clock probe requires a disposable character.' }
    $arguments += ' -peopleoftheiliacbay-movement-clock-probe'
}
if ($MemoryArchiveProbe) {
    if (-not $FixtureCharacter) { throw 'Memory archive probe requires a disposable character.' }
    $arguments += ' -peopleoftheiliacbay-memory-archive-probe'
    if ($MemoryArchiveRead) { $arguments += ' -peopleoftheiliacbay-memory-archive-read' }
}
if ($MemoryArchiveModelProbe) {
    if (-not $MemoryArchiveProbe) { throw 'MemoryArchiveModelProbe requires MemoryArchiveProbe.' }
    try { $health = Invoke-RestMethod 'http://127.0.0.1:8080/health' -TimeoutSec 5 }
    catch { throw 'Memory archive model acceptance requires the local model server. Run tools/StartLocalDialogue.ps1 first.' }
    if ($health.status -ne 'ok') { throw 'Local model server is not ready for memory archive model acceptance.' }
    $arguments += ' -peopleoftheiliacbay-memory-archive-model-probe'
}
if ($GuardBuildingProbe) { $arguments += ' -peopleoftheiliacbay-guard-building-probe' }
if ($GuardWitnessProbe) { $arguments += ' -peopleoftheiliacbay-guard-witness-probe' }
if ($HousingProbe) { $arguments += ' -peopleoftheiliacbay-housing-probe' }
if ($KeeperResponseProbe) { $arguments += ' -peopleoftheiliacbay-keeper-response-probe' }
if ($IndoorNoiseProbe) { $arguments += ' -peopleoftheiliacbay-indoor-noise-probe' }
if ($IndoorStairsProbe) { $arguments += ' -peopleoftheiliacbay-indoor-stairs-probe' }
if ($CrimeSceneProbe) { $arguments += ' -peopleoftheiliacbay-crime-scene-probe' }
if ($OutdoorSceneProbe) { $arguments += ' -peopleoftheiliacbay-outdoor-scene-probe' }
if ($InteriorEscapeProbe) { $arguments += ' -peopleoftheiliacbay-interior-escape-probe' }
if ($QuestBindingProbe) { $arguments += ' -peopleoftheiliacbay-quest-binding-probe' }
if ($DeliveryProbe) { $arguments += ' -peopleoftheiliacbay-delivery-probe' }
if ($RefugeProbe) {
    if (-not $FixtureCharacter) { throw 'Refuge survey requires a dedicated fixture character.' }
    $arguments += ' -peopleoftheiliacbay-refuge-probe'
}
if ($RegionalProbe) {
    if (-not $FixtureCharacter) { throw 'Regional probe requires the dedicated fixture character.' }
    $arguments += ' -peopleoftheiliacbay-regional-probe'
}
if ($ShopVisitProbe) {
    if (-not $FixtureCharacter) { throw 'Shop visit probe requires the dedicated fixture character.' }
    $arguments += ' -peopleoftheiliacbay-shop-visit-probe'
}
if ($CrowdProbe) {
    if (-not $FixtureCharacter) { throw 'Crowd probe requires the dedicated fixture character.' }
    $arguments += ' -peopleoftheiliacbay-crowd-probe'
}
if ($DailyItineraryProbe) {
    if (-not $FixtureCharacter) { throw 'Daily itinerary probe requires the dedicated fixture character.' }
    $arguments += ' -peopleoftheiliacbay-daily-itinerary-probe'
}
if ($HouseholdJointBodyProbe) { $arguments += ' -peopleoftheiliacbay-household-body-probe' }
if ($HouseholdInnPhysicalProbe) { $arguments += ' -peopleoftheiliacbay-household-inn-physical-probe' }
if ($OutdoorArrivalProbe) {
    if (-not $FixtureCharacter) { throw 'Outdoor arrival probe requires a disposable fixture character.' }
    $arguments += ' -peopleoftheiliacbay-outdoor-arrival-probe'
}
if ($OutdoorNavigationProbe) {
    if (-not $FixtureCharacter) { throw 'Outdoor navigation probe requires a disposable fixture character.' }
    $arguments += ' -peopleoftheiliacbay-outdoor-navigation-probe'
}
if ($CitizenThreatProbe) {
    if (-not $FixtureCharacter) { throw 'Citizen threat probe requires the dedicated fixture character.' }
    $arguments += ' -peopleoftheiliacbay-citizen-threat-probe'
}
if ($DefenseLedgerProbe) {
    if (-not $FixtureCharacter) { throw 'Defense ledger probe requires the dedicated fixture character.' }
    $arguments += ' -peopleoftheiliacbay-defense-ledger-probe'
}
if ($WelfareProbe) {
    if (-not $FixtureCharacter) { throw 'Welfare probe requires a dedicated fixture character.' }
    $arguments += ' -peopleoftheiliacbay-welfare-probe'
}
if ($PhysicalDailyRoutine) { $arguments += ' -peopleoftheiliacbay-physical-daily-routine' }
if ($IndoorCatchupProbe) { $arguments += ' -peopleoftheiliacbay-indoor-catchup-probe' }
if ($DailyInteriorOnly) { $arguments += ' -peopleoftheiliacbay-daily-interior-only' }
if ($InnSeatOnly) { $arguments += ' -peopleoftheiliacbay-inn-seat-only' }
if ($PrepareOutdoorFixture) { $arguments += ' -peopleoftheiliacbay-prepare-outdoor-fixture' }
if ($NpcActionProbe) { $arguments += ' -peopleoftheiliacbay-action-probe' }
if ($NpcConversationCommandProbe) { $arguments += ' -peopleoftheiliacbay-command-probe' }
if ($NpcActionChatProbe) { $arguments += ' -peopleoftheiliacbay-action-chat-probe' }
if ($NpcActionDiskProbe) { $arguments += ' -peopleoftheiliacbay-action-disk-probe' }
if ($IndoorCrowdProbe) { $arguments += ' -peopleoftheiliacbay-indoor-crowd-probe' }
if ($NpcBeerProbe) { $arguments += ' -peopleoftheiliacbay-beer-probe' }
if ($Headless) { $arguments += ' -batchmode -nographics' }
$owned = Start-Process -FilePath $player -WorkingDirectory $playerDir -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Owned town-test process: $($owned.Id)"
try {
    $passed = $false
    $reported = [System.Collections.Generic.HashSet[string]]::new()
    $testTimeout = if ($NpcLeadProbe) { 600 } elseif ($NpcHomeReturnProbe) { 1200 } elseif ($DecisionRoutingAppraisalReopenSpeech -or $NpcActionChatQwenWillingnessProbe) { 900 } elseif ($BreezeTtsMixerReceiptProbe) { 180 } elseif ($SpeechRevealPresentationProbe) { 240 } elseif ($BreezeTtsSettingsProbe) { 1200 } elseif ($BreezeTtsCapacityProbe) { 300 } elseif ($SocialDialogueStreamingProbe) { 360 } elseif ($NpcIdentityIntegrationProbe -or $NpcExperienceAppraisalTransportProbe -or $NativeNpcPoisonExperienceProbe -or $NpcResentmentRecoveryProbe) { 600 } elseif ($TtsStreamingAcceptanceProbe) { 600 } elseif ($NativeTopicTtsProbe) { 420 } elseif ($PhysicalDailyRoutine -or $PrepareOutdoorFixture) { 1200 } elseif ($GoalChainLengthProbe -or $DecisionWillingnessProbe -or $GoalQwenWillingnessProbe -or $NpcActionChatQwenWillingnessProbe) { 900 } elseif ($GoalSequenceProbe) { 600 } elseif ($GoalCancellationProbe) { 300 } elseif ($GoalBindingProbe) { 300 } elseif ($DialogueAvailabilityProbe) { 900 } elseif ($DecisionRoutingSafeDowngradeProbe -or $DecisionRoutingAppraisalReopenProbe -or $FriendshipProgressionProbe -or $ConversationMoodProbe -or $SocialDuplicateReplyProbe -or $ConversationActionMoodProbe -or $ActionWordingProbe -or $ShopVisitProbe -or $FreshPreviews -or $DialogueProbe -or $SemanticConversationProbe -or $ConversationContextLifecycleProbe -or $ConversationContextSpeakerProbe -or $ConversationReplyOutcomeProbe -or $FollowTransitionProbe -or $ShopCommerceProbe -or $TempleTrainingProbe -or $AppointmentPointProbe -or $CommerceRestartRead -or $IndoorCrowdProbe -or $RoutineRecoverySceneChangeProbe -or $NpcStartingBelongingsSceneProbe -or $AppointmentNoShowProbe -or $HomeContainerDepositProbe -or $MoveToWaitProbe -or $NpcInteractionProbe -or $NpcVitalsProbe -or $NpcHealthCareProbe -or $VerbalCourtProbe -or $NpcHomeMealProbe) { 600 } elseif ($GateGuardProbe -or $WallQuarterProbe -or $ResidentTripProbe -or $CrossRegionTripProbe -or $PerformanceProbe -or $DailyItineraryProbe -or $OutdoorArrivalProbe -or $TavernSurveyProbe -or $CityTravelerProbe -or $AppointmentProbe) { 360 } else { 180 }
    if ($NativeNpcPoisonAppraisalProbe) { $testTimeout = 480 }
    # Includes bounded natural traveler readiness, real dungeon entry/return,
    # and a second entry for Stop. Each inner phase retains its own deadline.
    if ($OutdoorNavigationProbe -and $env:POTIB_ROADMAP_PROBE -eq 'dungeon-follow') { $testTimeout = 600 }
    if ($NpcHealthCareProbe) { $testTimeout = 900 }
    if ($OutdoorSceneProbe) { $testTimeout = 900 }
    if ($VerbalCourtProbe) { $testTimeout = 600 }
    if ($HouseholdJointBodyProbe) { $testTimeout = 1200 }
    if ($HouseholdInnPhysicalProbe) { $testTimeout = 900 }
    if ($TavernSurveyProbe) { $testTimeout = 1200 }
    # The complete composed day includes up to five real outings; the focused
    # interior replay retains its existing timeout.
    if ($PhysicalDailyRoutine -and -not $DailyInteriorOnly) { $testTimeout = 1800 }
    if ($SplitPortraitRuntimeProbe) { $testTimeout = 600 }
    if ($LiveDialoguePresentationProbe) { $testTimeout = 600 }
    if ($BreezeTtsTrialProbe -or $TtsContrastManifest) { $testTimeout = 1800 }
    if ($OutdoorNavigationProbe -and $env:POTIB_NAVMESH_CATALOG_EXPORT) { $testTimeout = 3600 }
    while (((Get-Date) - $started).TotalSeconds -lt $testTimeout) {
        if ($owned.HasExited) { throw "Player exited early ($($owned.ExitCode))." }
        if (Test-Path -LiteralPath $log) {
            $body = Get-Content -LiteralPath $log -Raw
            # Empty files return AutomationNull on this PowerShell host; an
            # expression cast alone does not produce a callable empty string.
            if ($null -eq $body) { $body = '' }
            foreach ($line in (([string]$body).Replace("`r",'').Split([char]10) | Where-Object { ((([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GOAL_CHAIN_LENGTH_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GOAL_TWO_PASS_STAGE:',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TEST_SAVE:',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_STARTING_BELONGINGS_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_MOVE_TO_WAIT_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SEMANTIC_CONVERSATION_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_AVAILABILITY_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_MOOD_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_INTENT:',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_EFFECT_SCOPE:',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_NPC_IDENTITY_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_EVENT_ONLY_PROFILE_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_APPRAISAL_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_APPRAISAL_REOPEN_SPEECH_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DIALOGUE_STREAM_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('BREEZE_TTS_NATIVE_SETTINGS_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('BREEZE_TTS_MIXER_RECEIPT_PROBE_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_NATIVE_TOPIC_TTS_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SPEECH_REVEAL_',[StringComparison]::OrdinalIgnoreCase) -ge 0) -or (([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_NPC_RESENTMENT_',[StringComparison]::OrdinalIgnoreCase) -ge 0)) })) {
                if ($reported.Add($line)) { Write-Output $line.Trim() }
            }
            if ((Test-NativeLogFailure $body)) { throw 'Town test failed; inspect work/town-test.log.' }
            if ($selectedOwnedProbeModes.Count -eq 1) {
                if ($SocialDuplicateReplyProbe) {
                    $restoredMarker = 'PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_BASELINE_RESTORED:'
                    $redMarker = 'PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_EXPECTED_RED:'
                    $greenMarker = 'PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_REPLY_PASS:'
                    if ($body.Contains($restoredMarker) -and ($body.Contains($redMarker) -or $body.Contains($greenMarker))) { $passed = $true; break }
                }
                $requiredModeMarker = switch ($selectedOwnedProbeModes[0]) {
                    'NpcHealthCareProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_NATIVE_PASS:'; break }
                    'OutdoorSceneProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: targeted city probe.'; break }
                    'NpcHomeMealProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_HOME_MEAL_NATIVE_PASS:'; break }
                    'NpcHomeReturnProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_HOME_RETURN_NATIVE_PASS:'; break }
                    'NpcIdentityIntegrationProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_NPC_IDENTITY_BASELINE_RESTORED:'; break }
                    'SocialDialogueStreamingProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DIALOGUE_STREAM_PROBE_PASS:'; break }
                    'BreezeTtsSettingsProbe' { 'BREEZE_TTS_NATIVE_SETTINGS_PASS:'; break }
                    'NativeTopicTtsProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_NATIVE_TOPIC_TTS_PASS:'; break }
                    'VerbalCourtProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_VERBAL_COURT_NATIVE_PASS:'; break }
                    'NpcExperienceAppraisalTransportProbe' { 'EXPERIENCE_APPRAISAL_TRANSPORT_PROBE_PASS:'; break }
                    'NativeNpcPoisonAppraisalProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_APPRAISAL_PASS:'; break }
                    'ExperienceDeferredInboxSerializationProbe' { 'EXPERIENCE_APPRAISAL_DEFERRED_SERIALIZATION_PASS:'; break }
                    'KeeperWorkDutyProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WORK_PASS:'; break }
                    'NativeNpcPoisonExperienceProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_PASS:'; break }
                    'TtsStreamingAcceptanceProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_TTS_STREAMING_ACCEPTANCE_PASS:'; break }
                    'SpeechRevealPresentationProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_SPEECH_REVEAL_PRESENTATION_PASS:'; break }
                    'SplitPortraitRuntimeProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_SPLIT_PORTRAIT_PASS:'; break }
                    'LiveDialoguePresentationProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_LIVE_DIALOGUE_PRESENTATION_PASS:'; break }
                    'NpcAcuteFearProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_NPC_ACUTE_FEAR_NATIVE_PASS:'; break }
                    'NpcResentmentRecoveryProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_NPC_RESENTMENT_RECOVERY_NATIVE_PASS:'; break }
                    'DecisionRoutingAppraisalReopenProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_PASS:'; break }
                    'DecisionRoutingSafeDowngradeProbe' { 'PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_SAFE_DOWNGRADE_PASS:'; break }
                    'GoalQwenWillingnessProbe' {
                        if ($GoalQwenBoundGoal) { 'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_BOUND_GOAL_PASS:' }
                        else { 'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_WILLINGNESS_PASS:' }
                        break
                    }
                }
                if ($requiredModeMarker -and $body.Contains($requiredModeMarker)) { $passed = $true; break }
            } elseif (($BreezeTtsMixerReceiptProbe -and ((([string]$body).IndexOf('BREEZE_TTS_MIXER_RECEIPT_PROBE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)) -or ($BreezeTtsTrialProbe -and ((([string]$body).IndexOf('BREEZE_TTS_NATIVE_PILOT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or ($TtsContrastManifest -and ((([string]$body).IndexOf('BREEZE_TTS_NATIVE_CONTROLLED_CONTRAST_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { $passed = $true; break }
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $passed) { throw "Town test did not finish within $testTimeout seconds." }
    $body = Get-Content -LiteralPath $log -Raw
    . (Join-Path $PSScriptRoot 'TownTest.CurrentAssertions.ps1')
    $seatTaskProbe = $NpcActionProbe -and $env:POTIB_ROADMAP_PROBE -eq 'seat-task'
    $surveyedTavernCount = 0
    if ($TavernSurveyProbe) {
        $surveyRows = @(Get-ProbeLines $body 'PEOPLE_OF_THE_ILIAC_BAY_TAVERN_SURVEY_PASS:')
        if ($surveyRows.Count -ne 1) { throw 'Expected one exact native tavern survey count.' }
        $surveyFields = Get-ProbeFields $surveyRows[0] 'PEOPLE_OF_THE_ILIAC_BAY_TAVERN_SURVEY_PASS:'
        if ($null -eq $surveyFields -or -not $surveyFields.ContainsKey('taverns') -or
            -not (Test-DecimalValue $surveyFields['taverns']) -or
            -not [int]::TryParse($surveyFields['taverns'],[ref]$surveyedTavernCount) -or $surveyedTavernCount -lt 1) { throw 'Invalid native tavern count.' }
    }
    if ($SocialDuplicateReplyProbe) {
        $restoredMarker = 'PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_BASELINE_RESTORED:'
        $redMarker = 'PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_EXPECTED_RED:'
        $greenMarker = 'PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_REPLY_PASS:'
        $outcomeMarker = if ($body.Contains($redMarker)) { $redMarker } else { $greenMarker }
        if ((Get-LiteralCount $body $restoredMarker) -ne 1 -or
            ((Get-LiteralCount $body $redMarker) + (Get-LiteralCount $body $greenMarker)) -ne 1 -or
            $body.IndexOf($restoredMarker, [StringComparison]::Ordinal) -gt $body.IndexOf($outcomeMarker, [StringComparison]::Ordinal)) {
            throw 'SocialDuplicateReplyProbe requires exact baseline restoration before exactly one RED or PASS outcome marker.'
        }
        if ($body.Contains($redMarker)) { Write-Output 'SocialDuplicateReplyProbe: expected RED reproduced; baseline restored.' }
    }
    if ($BreezeTtsMixerReceiptProbe) {
        if ((Get-LiteralCount $body 'BREEZE_TTS_MIXER_RECEIPT_PROBE_PASS:') -ne 1 -or
            (Get-LiteralCount $body 'BREEZE_TTS_MIXER_RECEIPT_PROBE_CASE_PASS: case=counted-before-update; starts=0; terminals=1; outcome=pre-playback-terminal') -ne 1 -or
            (Get-LiteralCount $body 'BREEZE_TTS_MIXER_RECEIPT_PROBE_CASE_PASS: case=zero-output; starts=0; terminals=1; outcome=pre-playback-terminal') -ne 1 -or
            (Get-LiteralCount $body 'BREEZE_TTS_MIXER_RECEIPT_PROBE_CASE_PASS: case=onset-published; starts=1; terminals=0; outcome=onset-receipt') -ne 1) {
            throw 'BreezeTtsMixerReceiptProbe requires exactly one pass marker and all three exact event-delivery case markers.'
        }
    }
    if ($NpcAcuteFearProbe) {
        $witnessComplete = 'PEOPLE_OF_THE_ILIAC_BAY_NPC_ACUTE_FEAR_WITNESS_PASS:'
        if ((Get-LiteralCount $body $witnessComplete) -ne 1) {
            throw 'NpcAcuteFearProbe requires exactly one production witness aggregate marker.'
        }
        foreach ($caseName in @('hostile-registry','unobstructed-direct-witness','blocking-wall','out-of-range',
                'sleeping-callback','quest-protected','inactive-actor','nonhostile-target','allied-target','dead-target')) {
            $caseMarker = 'PEOPLE_OF_THE_ILIAC_BAY_NPC_ACUTE_FEAR_WITNESS_CASE_PASS: case=' + $caseName + ';'
            if ((Get-LiteralCount $body $caseMarker) -ne 1) {
                throw ('NpcAcuteFearProbe requires exactly one witness case marker: ' + $caseName)
            }
        }
        $restored = 'PEOPLE_OF_THE_ILIAC_BAY_NPC_ACUTE_FEAR_BASELINE_UNCHANGED:'
        $complete = 'PEOPLE_OF_THE_ILIAC_BAY_NPC_ACUTE_FEAR_NATIVE_PASS:'
        foreach ($marker in @($restored, $complete)) {
            if ((Get-LiteralCount $body $marker) -ne 1) {
                throw ('NpcAcuteFearProbe requires exactly one ' + $marker + ' marker.')
            }
        }
        if ($body.IndexOf($restored, [StringComparison]::Ordinal) -ge $body.IndexOf($complete, [StringComparison]::Ordinal)) {
            throw 'NpcAcuteFearProbe PASS must follow baseline verification.'
        }
        if ($body.IndexOf($restored, [StringComparison]::Ordinal) -ge $body.IndexOf($witnessComplete, [StringComparison]::Ordinal) -or
            $body.IndexOf($witnessComplete, [StringComparison]::Ordinal) -ge $body.IndexOf($complete, [StringComparison]::Ordinal)) {
            throw 'Witness acceptance must follow baseline verification and precede final actor PASS.'
        }
    }
    if ($NpcResentmentRecoveryProbe) {
        $restored = 'PEOPLE_OF_THE_ILIAC_BAY_NPC_RESENTMENT_BASELINE_RESTORED:'
        $complete = 'PEOPLE_OF_THE_ILIAC_BAY_NPC_RESENTMENT_RECOVERY_NATIVE_PASS:'
        foreach ($marker in @($restored, $complete)) {
            if ((Get-LiteralCount $body $marker) -ne 1) {
                throw ('NpcResentmentRecoveryProbe requires exactly one ' + $marker + ' marker.')
            }
        }
        if ($body.IndexOf($restored, [StringComparison]::Ordinal) -ge
            $body.IndexOf($complete, [StringComparison]::Ordinal)) {
            throw 'NpcResentmentRecoveryProbe PASS must follow exact baseline reload.'
        }
        if ((-not ((([string]$body).IndexOf('supplied-game-days=16; actual-gameplay-days=not-tested; controlled-source-bound-events=true',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or
            (-not ((([string]$body).IndexOf('FullSerializer-anchor-rate-fraction=true; cold-real-Talk-projection=true; schema42-roundtrip=true; schema41-unknown-resentment-clock-migrated=true; trust-unchanged=true;',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) {
            throw 'NpcResentmentRecoveryProbe PASS lacks bounded supplied-time, serializer, migration, Talk, or trust evidence.'
        }
    }
    if ($SpeechRevealPresentationProbe) {
        foreach ($marker in @('PEOPLE_OF_THE_ILIAC_BAY_SPEECH_REVEAL_BASELINE_RESTORED:', 'PEOPLE_OF_THE_ILIAC_BAY_SPEECH_REVEAL_PRESENTATION_PASS:')) {
            if ((Get-LiteralCount $body $marker) -ne 1) {
                throw ('SpeechRevealPresentationProbe requires exactly one ' + $marker + ' marker.')
            }
        }
        if ($body.IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SPEECH_REVEAL_BASELINE_RESTORED:', [StringComparison]::Ordinal) -gt
            $body.IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SPEECH_REVEAL_PRESENTATION_PASS:', [StringComparison]::Ordinal)) {
            throw 'SpeechRevealPresentationProbe PASS preceded exact baseline restoration.'
        }
    }
    if ($NpcIdentityIntegrationProbe) {
        foreach ($marker in @('PEOPLE_OF_THE_ILIAC_BAY_EVENT_ONLY_PROFILE_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_NPC_IDENTITY_ACTION_SNAPSHOT_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_NPC_IDENTITY_BASELINE_RESTORED:')) {
            if ((Get-LiteralCount $body $marker) -ne 1) { throw "NPC identity integration is missing exactly one required proof marker: $marker" }
        }
    }
    if ($SocialDialogueStreamingProbe -and (Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DIALOGUE_STREAM_PROBE_PASS:') -ne 1) { throw 'Social dialogue streaming requires exactly one probe-specific pass marker.' }
    if ($BreezeTtsSettingsProbe -and (Get-LiteralCount $body 'BREEZE_TTS_NATIVE_SETTINGS_PASS:') -ne 1) { throw 'Breeze settings probe requires exactly one probe-specific pass marker.' }
    if ($NativeTopicTtsProbe -and (Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_NATIVE_TOPIC_TTS_PASS:') -ne 1) { throw 'Native topic TTS requires exactly one probe-specific pass marker.' }
    if ($NpcExperienceAppraisalTransportProbe -and (Get-LiteralCount $body 'EXPERIENCE_APPRAISAL_TRANSPORT_PROBE_PASS:') -ne 1) { throw 'Appraisal transport requires exactly one probe-specific pass marker.' }
    if ($NativeNpcPoisonAppraisalProbe) {
        if ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_APPRAISAL_PASS:') -ne 1 -or
            (Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_APPRAISAL_BASELINE_RESTORED:') -ne 1) {
            throw 'Native poison appraisal requires exactly one probe pass and exact baseline-restored marker.'
        }
    }
    if ($ExperienceDeferredInboxSerializationProbe -and (Get-LiteralCount $body 'EXPERIENCE_APPRAISAL_DEFERRED_SERIALIZATION_PASS:') -ne 1) { throw 'Deferred appraisal inbox serialization requires exactly one probe-specific pass marker.' }
    if ($KeeperWorkDutyProbe) {
        if ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WORK_PASS:') -ne 1 -or
            (Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WORK_BASELINE_RESTORED:') -ne 1) {
            throw 'Keeper work requires exactly one probe pass and exact baseline-restored marker.'
        }
    }
    if ($NativeNpcPoisonExperienceProbe) {
        if ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_PASS:') -ne 1 -or
            (Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_BASELINE_RESTORED:') -ne 1) {
            throw 'Native poison experience probe requires exactly one probe pass and exact baseline-restored marker.'
        }
    }
    if ($TtsStreamingAcceptanceProbe -and (Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_TTS_STREAMING_ACCEPTANCE_PASS:') -ne 1) { throw 'TTS streaming acceptance requires exactly one probe-specific pass marker.' }
    if ($BreezeTtsTrialProbe -and (-not ((([string]$body).IndexOf('BREEZE_TTS_NATIVE_PILOT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Real Breeze playback pilot did not complete.' }
    if ($TtsContrastManifest -and (-not ((([string]$body).IndexOf('BREEZE_TTS_NATIVE_CONTROLLED_CONTRAST_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Controlled voice comparison did not complete.' }
    if ($LocationBlockProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_LOCATION_BLOCK_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Incremental block parity and cancellation did not complete.' }
    if ($DailyInteriorOnly -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_DAY_INTERIOR_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Focused interior day diagnostic did not complete.' }
    if ($ConversationContextSnapshotProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SNAPSHOT_CAPTURE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Conversation context snapshot capture, stale-stage recovery, or session teardown assertions did not complete.' }
    if ($GoalBindingProbe -and (-not $body.Contains('PEOPLE_OF_THE_ILIAC_BAY_GOAL_BINDING_MISMATCH_PASS:') -or
        -not $body.Contains('PEOPLE_OF_THE_ILIAC_BAY_GOAL_BINDING_MISSING_DURATION_PASS:') -or
        -not $body.Contains('PEOPLE_OF_THE_ILIAC_BAY_GOAL_PARAMETER_REPAIR_PASS: case=duration;') -or
        -not $body.Contains('PEOPLE_OF_THE_ILIAC_BAY_GOAL_PARAMETER_REPAIR_PASS: case=quantity;') -or
        -not $body.Contains('PEOPLE_OF_THE_ILIAC_BAY_GOAL_BINDING_DETERMINISTIC_ACCEPT_PASS:') -or
        -not $body.Contains('PEOPLE_OF_THE_ILIAC_BAY_GOAL_BINDING_LIVE_PASS:'))) { throw 'Native goal extraction/binding probe did not complete.' }
    if ($GoalQwenWillingnessProbe) {
        $commonQwenMarkers = @(
            'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_D3_OUTAGE_GUARD_PASS:',
            'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_CONTROLLED_CASE_PASS: case=DECLINE;',
            'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_CONTROLLED_CASE_PASS: case=Unavailable;',
            'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_CONTROLLED_CASE_PASS: case=duplicate-ACCEPT;',
            'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_CONTROLLED_CASE_PASS: case=closed-stale-ACCEPT;',
            'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_BASELINE_RESTORED:'
        )
        if ($GoalQwenBoundGoal) {
            $requiredQwenMarkers = $commonQwenMarkers + @(
                'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_BOUND_TRACE:',
                'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_BOUND_GOAL_PASS:'
            )
        } else {
            $requiredQwenMarkers = $commonQwenMarkers + @(
                'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_LIVE_TRACE:',
                'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_WILLINGNESS_PASS:'
            )
        }
        foreach ($marker in $requiredQwenMarkers) {
            if ((Get-LiteralCount $body $marker) -ne 1) {
                throw ('Native planner-off Qwen goal acceptance requires exactly one ' + $marker + ' marker.')
            }
        }
        if ($GoalQwenBoundGoal) {
            if (((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_LIVE_TRACE:') + (Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_WILLINGNESS_PASS:')) -ne 0 -or
                (-not (Test-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_BOUND_TRACE:' @{'completeOriginalExtraction'='false'; 'targetBindingRequestResponse'={param($v) $true}; 'targetBindingClassificationCount'={param($v) $n=0L; (Test-DecimalValue $v) -and [long]::TryParse($v,[ref]$n) -and $n -ge 2}; 'qwenRequest'={param($v) $true}; 'qwenResponse'={param($v) $true}; 'qwenRawChoice'={param($v) $true}; 'qwenRequestSha256'={param($v) Test-HexValue $v 64 -Lowercase}; 'qwenResponseSha256'={param($v) Test-HexValue $v 64 -Lowercase}; 'plannerOff'='true'; 'd3Session'='absent'; 'binding'='bound'; 'targetEvidence'='true'; 'qwenEvidence'='true'; 'goalWillingnessDispatchDelta'='1'; 'qwenDispatchDelta'='1'; 'qwenCompletionDelta'='1'; 'task'={param($v) $true}; 'taskCountBefore'={param($v) Test-DecimalValue $v}; 'taskCountAfter'={param($v) Test-DecimalValue $v}; 'transcript'={param($v) $true}; 'actualProductionTargetSelection'='true'; 'actualProductionWillingness'='true'; 'audibleSpeech'='false'})) -or
                (-not (Test-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_BOUND_GOAL_PASS:' @{'controlledGoal'='true'; 'completeOriginalExtraction'='false'; 'productionTargetBinding'='true'; 'productionQwenWillingness'='true'; 'onceOnlySavedTwoStepTask'='true'; 'baselineRestored'='true'}))) {
                throw 'Controlled-goal downstream acceptance lacks actual production target binding, Qwen willingness, one saved two-step task, or clear separation from original extraction.'
            }
        } elseif ((-not (Test-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_GOAL_QWEN_LIVE_TRACE:' @{'plannerOff'='true'; 'd3Session'='absent'; 'completeOriginalExtraction'='true'; 'separateExtractionTrace'='true'; 'goalWillingnessDispatchDelta'='1'; 'qwenDispatchDelta'='1'; 'qwenCompletionDelta'='1'; 'task'={param($v) $true}}))) {
            throw 'Native Qwen goal acceptance lacks complete original live extraction, independent willingness, exact once-only saved task, or raw trace evidence.'
        }
    }
    if ($GoalSequenceProbe -and -not $body.Contains('PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_PASS:')) { throw 'Native ordered goal probe did not complete.' }
    if ($GoalCancellationProbe -and -not $body.Contains('PEOPLE_OF_THE_ILIAC_BAY_GOAL_CANCELLATION_PASS:')) { throw 'Native goal cancellation probe did not complete.' }
    if ($GoalChainLengthProbe -and -not $body.Contains('PEOPLE_OF_THE_ILIAC_BAY_GOAL_CHAIN_LENGTH_PASS:')) { throw 'Native requested goal chain length did not complete.' }
    if ($GoalSequenceProbe -or $GoalChainLengthProbe) {
        $baselineMarker = 'PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_BASELINE_RESTORED:'
        if ((Get-LiteralCount $body $baselineMarker) -ne 1) {
            throw 'Native goal sequence acceptance requires exactly one exact-fixture baseline restoration marker.'
        }
        if ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_BASELINE_RESTORED: character=PeopleOfTheIliacBayTest-9a5f197b; save=People - Gothway Garden; actionTaskFingerprintExact=true; playerPositionExact=true; hostFlagsRestored=true; sessionOwnershipRestored=true.',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) {
            throw 'Native goal sequence baseline marker lacks exact owned-save, player-position, host-state, and fingerprint restoration evidence.'
        }
        $baselineAt = $body.IndexOf($baselineMarker, [StringComparison]::Ordinal)
        foreach ($passMarker in @('PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_GOAL_CHAIN_LENGTH_PASS:')) {
            $passAt = $body.IndexOf($passMarker, [StringComparison]::Ordinal)
            if ($passAt -ge 0 -and $passAt -lt $baselineAt) {
                throw 'Native goal sequence PASS marker appeared before exact-fixture baseline restoration.'
            }
        }
    }
    if ($GoalTwoPass) {
        $sequencePass = @(([string]$body).Replace("`r",'').Split([char]10) | Where-Object { ((([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GOAL_CHAIN_LENGTH_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)) })
        if ($sequencePass.Count -ne 1 -or (-not (Test-ProbeReceipt $sequencePass[0] 'PEOPLE_OF_THE_ILIAC_BAY_GOAL_CHAIN_LENGTH_PASS:' @{'liveExtraction'='true'; 'decompositionDispatchCount'='1'; 'decompositionCompletionCount'='1'; 'parameterDispatchCount'='1'; 'parameterCompletionCount'='1'; 'repairDispatchCount'=@('0','1'); 'repairCompletionCount'={param($v,$f) $v -ceq $f['repairDispatchCount']}; 'stagesValidBeforeBinding'='true'; 'targetBinding'='production-local-model'; 'modelSemanticEvidence'='fixture-specific'; 'universalSemanticClaim'='false'}))) {
            throw 'Native two-pass goal acceptance lacks live extraction, exact stage counts, pre-binding validity, production target binding, or bounded semantic evidence.'
        }
        foreach ($passName in @('decomposition','parameters')) {
            $stageMarkers = @(([string]$body).Replace("`r",'').Split([char]10) | Where-Object { $_.Contains('PEOPLE_OF_THE_ILIAC_BAY_GOAL_TWO_PASS_STAGE: pass=' + $passName + ';') })
            if ($stageMarkers.Count -ne 1 -or (-not (Test-ProbeReceipt $stageMarkers[0] 'PEOPLE_OF_THE_ILIAC_BAY_GOAL_TWO_PASS_STAGE:' @{'dispatchCount'='1'; 'completionCount'='1'; 'traceComplete'='true'; 'prompt'={param($v) -not [string]::IsNullOrEmpty($v)}; 'output'={param($v) -not [string]::IsNullOrEmpty($v)}; 'request'={param($v) -not [string]::IsNullOrEmpty($v)}; 'response'={param($v) -not [string]::IsNullOrEmpty($v)}}))) {
                throw "Native two-pass $passName stage lacks exactly one completed dispatch and raw prompt/output/request/response evidence."
            }
        }
    }
    if ($ConversationContextLifecycleProbe) {
        $fearFreshnessMarker = 'PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_ACUTE_FEAR_FRESHNESS_PASS:'
        if ((Get-LiteralCount $body $fearFreshnessMarker) -ne 1 -or
            $body.IndexOf($fearFreshnessMarker, [StringComparison]::Ordinal) -lt
            $body.IndexOf('PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_BASELINE_RESTORED:', [StringComparison]::Ordinal)) {
            throw 'Context lifecycle requires one acute-fear freshness marker after baseline restoration.'
        }
        foreach ($marker in @('PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_REOPEN_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SAME_BRANCH_SAVE_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_DISK_RESTORE_INVALIDATION_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_BASELINE_RESTORED:')) {
            if ((-not (([string]($body)).Contains($marker)))) { throw ('Conversation lifecycle assertion missing: ' + $marker) }
        }
    }
    if ($ConversationContextSpeakerProbe) {
        foreach ($kind in @('managed-mobile','unmanaged-guard','unmanaged-mobile','static-keeper','static-unknown-role')) {
            if ((-not (([string]($body)).Contains('PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SPEAKER_PASS: ' + $kind)))) { throw ('Conversation speaker assertion missing: ' + $kind) }
        }
    }
    if ($ConversationReplyOutcomeProbe) {
        foreach ($marker in @('PEOPLE_OF_THE_ILIAC_BAY_REPLY_OUTCOME_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_REPLY_OUTCOME_BASELINE_RESTORED:')) {
            if ((-not (([string]($body)).Contains($marker)))) { throw ('Native action acknowledgment assertion missing: ' + $marker) }
        }
    }
    if ($FriendshipProgressionProbe) {
        foreach ($marker in @('PEOPLE_OF_THE_ILIAC_BAY_FRIENDSHIP_PROGRESSION_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_FRIENDSHIP_HOME_CONTEXT_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_FRIENDSHIP_BASELINE_RESTORED:')) {
            if ((-not (([string]($body)).Contains($marker)))) { throw ('Native friendship assertion missing: ' + $marker) }
        }
    }
    if ($ConversationMoodProbe) {
        if ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PENDING_OFFER_GROUNDING_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Pending-offer outdoor grounding probe did not complete.' }
        foreach ($marker in @('PEOPLE_OF_THE_ILIAC_BAY_MOOD_LIVE_SOCIAL_COMPLETION_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_MOOD_CONTROLLED_CALLBACK_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_MOOD_DISK_ROLLBACK_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_MOOD_BASELINE_RESTORED:')) {
            if ((-not (([string]($body)).Contains($marker)))) { throw ('Native conversation mood assertion missing: ' + $marker) }
        }
    }
    if ($ConversationActionMoodProbe) {
        foreach ($marker in @('PEOPLE_OF_THE_ILIAC_BAY_ACTION_MOOD_LIVE_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_ACTION_MOOD_DISK_ROLLBACK_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_ACTION_MOOD_BASELINE_RESTORED:')) {
            if ((-not (([string]($body)).Contains($marker)))) { throw ('Native action mood assertion missing: ' + $marker) }
        }
    }
    if ($ActionWordingProbe) {
        foreach ($marker in @('PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_UNRESOLVED_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_BASELINE_RESTORED:', 'PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_DUPLICATE_CONTROL_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_CLOSE_LATE_RESULT_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_SAVE_LATE_RESULT_PASS:')) {
            if ((-not (([string]($body)).Contains($marker)))) { throw ('Native action wording assertion missing: ' + $marker) }
        }
        $indicatorCapture = Join-Path $env:USERPROFILE 'AppData/LocalLow/Daggerfall Workshop/Daggerfall Unity/PeopleOfTheIliacBay-action-wording-pending-indicator.png'
        if (-not (Test-Path -LiteralPath $indicatorCapture)) { throw 'Action wording pending-indicator screenshot was not captured.' }
        $indicatorArtifactDirectory = Join-Path $repo 'work/action-wording-20261020'
        New-Item -ItemType Directory -Path $indicatorArtifactDirectory -Force | Out-Null
        Copy-Item -LiteralPath $indicatorCapture -Destination (Join-Path $indicatorArtifactDirectory 'pending-indicator.png') -Force
    }
    if ($InnSeatOnly -and ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SITTING_POSE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INN_SEATS_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))))) { throw 'Native sitting render and seat entry/departure checks did not complete.' }
    if ($InnSeatOnly -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_STANDING_POSE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Standing bearings, scale and lifecycle checks did not complete.' }
    $navigationMarker = if ($env:POTIB_NAVMESH_TRIAL -eq '1') { 'PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_TRIAL_PASS:' } else { 'PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_NAVIGATION_PASS:' }
    if ($OutdoorNavigationProbe -and -not $body.Contains($navigationMarker)) { throw 'Native navigation acceptance did not complete.' }
    if ($PhysicalDailyRoutine -and -not $DailyInteriorOnly -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_DAY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Physical daily routine did not complete.' }
    if ($NpcActionProbe -and -not $seatTaskProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ACTION_PROBE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Physical NPC action did not complete.' }
    if ($NpcActionProbe -and -not $seatTaskProbe -and -not $NpcBeerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SIMPLE_ACTIONS_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Wait, follow, stop and native handover checks did not complete.' }
    if ($NpcActionProbe -and -not $seatTaskProbe -and -not $NpcBeerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INBOUND_TRANSFER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Native inbound transfer task did not complete.' }
    if ($NpcActionProbe -and -not $seatTaskProbe -and -not $NpcBeerProbe -and ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ACTION_FEEDBACK_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_NPC_DELIVERY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))))) { throw 'Native action feedback and NPC delivery checks did not complete.' }
    if ($NpcActionChatProbe -and -not $NpcBeerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ACTION_CHAT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Native chat action integration did not complete.' }
    if ($NpcActionChatProbe -and -not $NpcBeerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ACTION_TASK_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Native sequential chat task and whole-task cancellation did not complete.' }
    if ($NpcActionChatQwenWillingnessProbe) {
        if ((-not (Test-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_ACTION_CHAT_PASS:' @{'qwenWillingnessOptIn'='true'; 'typedCancellation'='skipped-unverified'}))) {
            throw 'Opt-in native chat did not report Qwen willingness and its typed-cancellation coverage limit.'
        }
        foreach ($stage in @('single-wait', 'pending-offer-assent', 'ordered-task-10s')) {
            $consentMarker = 'PEOPLE_OF_THE_ILIAC_BAY_ACTION_CHAT_QWEN_CONSENT: stage=' + $stage + '; dispatch=1; completion=1; rawChoice=ACCEPT.'
            if (-not $body.Contains($consentMarker)) { throw ('Qwen willingness did not accept exactly once for ' + $stage + '.') }
        }
    }
    if ($NpcActionDiskProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ACTION_DISK_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Native action disk save/reload acceptance did not complete.' }
    if ($IndoorCrowdProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INDOOR_CROWD_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Continuous inn crowd progress did not complete.' }
    if ($OccupiedApproachProbe -and ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_OCCUPIED_APPROACH_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FREE_BROWSE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))))) { throw 'Occupied approach and free browse checks did not finish.' }
    if ($KeeperStandingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_KEEPER_STANDING_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Directional keeper animation checks did not finish.' }
    if ($BusinessIdentityProbe -and ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_BUSINESS_IDENTITY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_NO_INN_KEYS_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ACTIVE_SPEAKER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))))) { throw 'Business identity, active speaker and inn key checks did not finish.' }
    if ($BusinessIdentityProbe -and ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SPEAKER_SWITCH_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TRANSCRIPT_REOPEN_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))))) { throw 'Speaker switching and transcript reopening checks did not finish.' }
    if ($FollowTransitionProbe -and ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_FIXTURE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_RECOVERY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_TRANSITION_PASS direction=outdoor',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_TRANSITION_PASS direction=indoor',[StringComparison]::OrdinalIgnoreCase) -ge 0))))) { throw 'Follow both-door, identity and routine recovery checks did not finish.' }
    if ($ShopCommerceProbe -and -not $ShopLootSaleProbe -and -not $CommerceRestartPrepare -and -not $TaskFailureFeedbackProbe -and ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SHOP_COMMERCE_FIXTURE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SHOP_COMMERCE_SERIALIZER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))))) { throw 'Native purchase, handover and serialization checks did not finish.' }
    if ($RoutineRecoveryPhysicalProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_COMMITTED:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Production doorway recovery save did not complete.' }
    if ($RoutineRecoveryPhysicalProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_PHYSICAL_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Physical routine recovery acceptance did not complete.' }
    if ($RoutineRecoverySceneChangeProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_SCENE_CHANGE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Outdoor recovery scene-change acceptance did not complete.' }
    if ($AppointmentNoShowProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_PLAYER_NO_SHOW_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Physically witnessed appointment no-show checks did not complete.' }
    if ($RoutineRecoveryDayProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_DAY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Later-stop and home physical routine recovery acceptance did not complete.' }
    if ($HomeContainerDepositProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_HOME_CONTAINER_DEPOSIT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Native home-container deposit, serialization and reload checks did not complete.' }
    if ($HomeContainerDepositProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_HOME_CONTAINER_RETRIEVAL_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Physical household-item retrieval, save/reload and replay checks did not complete.' }
    if ($MoveToWaitProbe -and -not $EnterWaitProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_MOVE_TO_WAIT_NATIVE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Named-building MoveTo arrival and bound Wait acceptance did not complete.' }
    if ($NpcStartingBelongingsSceneProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_STARTING_BELONGINGS_SCENE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Natural first-spawn belongings and disk save/reload acceptance did not complete.' }
    if ($ShopCommerceChatProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SHOP_COMMERCE_CHAT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Live model/native chat purchase task acceptance did not finish.' }
    if ($ShopCommerceChatProbe -and (-not (Test-Path -LiteralPath $semanticCapturePath) -or (Get-Item -LiteralPath $semanticCapturePath).Length -eq 0)) { throw 'Opt-in semantic action model capture was not written to work JSONL.' }
    if ($TaskFailureFeedbackProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TASK_FAILURE_NATIVE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Native composed task failure feedback checks did not finish.' }
    if ($CommerceRestartPrepare -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_COMMERCE_RESTART_PREPARED:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Commerce disk checkpoints were not prepared.' }
    if ($CommerceRestartRead -and (-not $body.Contains(('PEOPLE_OF_THE_ILIAC_BAY_COMMERCE_RESTART_PASS: checkpoint=' + $CommerceRestartRead + ';')))) { throw 'Independent-process commerce recovery did not pass.' }
    if ($AppointmentProbe -and ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_NATIVE_CALENDAR: PASS',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_DIALOGUE_BINDING_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_SAME_DAY_REFUSAL_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))))) { throw 'Appointment calendar and dialogue binding checks did not finish.' }
    if ($AppointmentProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_RUNTIME_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Appointment booking, serialization and schedule checks did not finish.' }
    if ($AppointmentPointProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_PHYSICAL_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Exact-point physical arrival, auto-Talk opener, or native disk save/load did not finish.' }
    if (($InnLodgerHomeProbe -or $BedroomReplacementProbe) -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_BEDROOM_REPLACEMENT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Bedroom replacement checks did not finish.' }
    if ($MovementClockProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_MOVEMENT_CLOCK_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Physical NPC speed and 2x clock checks did not complete.' }
    if ($PerformanceProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PERFORMANCE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Performance samples did not complete.' }
    if ($PerformanceProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_MURDER_WITNESS_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Murder witness persistence probe did not complete.' }
    if ($HauntingDawnProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_HAUNTING_DAWN_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Haunting dawn lifetime check did not complete.' }
    if ($CrossRegionTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_CROSS_REGION_TRIP_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Cross-region journey check did not pass.' }
    if ($CrossRegionTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_STOP_SAVE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Intermediate road-stop serialization check did not pass.' }
    if ($CrossRegionTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_STOP_RECEPTION_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Intermediate road-stop reception check did not pass.' }
    if ($CrossRegionTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_STOP_ADMISSION_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Intermediate road-stop admission check did not pass.' }
    if ($CrossRegionTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_STOP_REFUSAL_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Intermediate road-stop refusal check did not pass.' }
    if ($CrossRegionTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_SETTLEMENT_INDEX_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Road settlement index check did not pass.' }
    if ($CrossRegionTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_PASSAGE_SAVE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Through-town route serialization check did not pass.' }
    if ($CrossRegionTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_INN_SURVEY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Real intermediate inn route survey did not pass.' }
    if ($CrossRegionTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_PASSAGE_PLAN_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Real intermediate inn trip insertion did not pass.' }
    if ($CrossRegionTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_STOP_CRIME_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Intermediate visitor crime identity check did not pass.' }
    if ($CrossRegionTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_KEEPER_PLAN_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Intermediate inn keeper preparation did not pass.' }
    if ($CrossRegionTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INCOMING_DISCOVERY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Historical incoming discovery check did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_RESIDENT_TRIP_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Resident trip identity and home absence check did not complete.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_NETWORK_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Basic Roads runtime network check did not complete.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_SLICES_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Sliced road search check did not complete.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_ACTOR_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Road resident actor check did not complete.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_HOME_CONNECTOR_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Resident home connector check did not complete.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_JOIN_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Resident road join check did not complete.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_TRANSFER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Resident actor ownership transfer check did not complete.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_SAVE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Resident destination serialization check did not complete.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_CRIME_SAVE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Resident destination crime serialization check did not complete.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_ACTOR_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Resident destination actor check did not complete.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_INTERIOR_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Resident destination interior check did not complete.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SERVICE_EXIT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Resident service exit check did not complete.' }
    if ($HauntingDawnProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_HAUNTING_INN_DAWN_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Haunting inn dawn check did not complete.' }
    if ($HauntingDawnProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_HAUNTING_PENDING_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Haunting delayed placement check did not complete.' }
    if ($CityTravelerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_CITY_TRAVELER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Daggerfall traveler gate test did not complete.' }
    if ($GateGuardProbe -and -not $GuardQuotaOnly -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GATE_GUARD_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Daggerfall gate sentry probe did not complete.' }
    if ($GateGuardProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARD_QUOTA_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard quota and native restore check did not complete.' }
    if ($GateGuardProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GATE_GUARD_VIEW_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Daggerfall gate sentry rendering check did not complete.' }
    if ($GateGuardProbe -and -not $GuardQuotaOnly -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GATE_INDOOR_RECALL_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Gate recall while inside an inn did not complete.' }
    if ($GateGuardProbe -and -not $GuardQuotaOnly -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_WALL_DUTY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Wall patrol and morning return did not complete.' }
    if ($GateGuardProbe -and -not $GuardQuotaOnly -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_WALL_SLICES_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Wall survey equivalence and cancellation did not complete.' }
    if ($GateGuardProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARD_SETUP_SLICES_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard setup survey equivalence and cancellation did not complete.' }
    if ($WallQuarterProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_QUARTER_BEDS_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Wall quarter furniture check did not complete.' }
    if ($CityTravelerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_ACTOR_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Daggerfall traveler actor test did not complete.' }
    if ($CityTravelerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_ROOM_ARRIVAL_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Traveler physical bedroom arrival test did not complete.' }
    if ($CityTravelerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_REFUSAL_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Daggerfall full-inn traveler departure test did not complete.' }
    if ($StationaryNpcProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_STATIONARY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Ordinary stationary NPC test did not complete.' }
    if ($QuestProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_QUEST_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Quest probe did not complete.' }
    if ($BedProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_BEDS_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Bed probe did not complete.' }
    if ($ShopVisitProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SHOP_VISIT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Shop activity probe did not complete.' }
    if ($ShopVisitProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_SHOP_DEPARTURE_PASS:') -ne 2)) { throw 'Both shopper purchase modes must physically reach the exit.' }
    if ($ShopVisitProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FOREIGN_SHOP_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Foreign shopper activity probe did not complete.' }
    if ($ShopVisitProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FOREIGN_SHOP_DEPARTURE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Foreign shopper physical departure did not complete.' }
    if ($ShopVisitProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FOREIGN_CORPSE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Foreign shopper corpse response check did not complete.' }
    if ($ShopVisitProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FOREIGN_REPORT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Foreign shopper physical report check did not complete.' }
    if ($ShopVisitProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FOREIGN_GUARDED_EXIT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Foreign guarded-shop exit check did not complete.' }
    if ($CrowdProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_CROWD_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Crowd movement probe did not complete.' }
    if ($CrowdProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_COMFORT_AVOIDANCE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Early comfort avoidance did not complete.' }
    if ($DailyItineraryProbe -and -not $HouseholdJointBodyProbe -and -not $DailyInteriorOnly -and -not $PrepareOutdoorFixture -and -not $IndoorCatchupProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DAILY_ITINERARY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Generated daily itinerary probe did not complete.' }
    if ($IndoorCatchupProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INDOOR_CATCHUP_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Indoor time-skip catch-up probe did not complete.' }
    if ($PrepareOutdoorFixture -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FIXTURE_PREPARED_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Prepared outdoor fixture did not save and reload successfully.' }
    if ($OutdoorArrivalProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_ARRIVAL_CONTINUITY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Physical outdoor arrival probe did not complete.' }
    if ($OutdoorArrivalProbe -and (Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_LOITER_CATCHUP_PASS:') -ne 1) { throw 'Outdoor arrival probe requires exactly one native one-hour loiter catch-up proof.' }
    if ($OutdoorArrivalProbe -and (Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_LOITER_TRAVEL_CATCHUP_PASS:') -ne 1) { throw 'Outdoor arrival probe requires exactly one native one-hour remaining-travel catch-up proof.' }
    if ($CrowdProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_CROWD_WALL_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Crowd wall collision check did not complete.' }
    if ($CitizenThreatProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_CITIZEN_THREAT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Citizen threat probe did not complete.' }
    if ($CitizenThreatProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FLIGHT_SAFETY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Attacker-aware street flight did not complete.' }
    if ($CitizenThreatProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SHELTER_LOCK_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Shelter lock/save/expiry check did not complete.' }
    if ($CitizenThreatProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_RETREAT_CORRIDOR_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Local retreat collision check did not complete.' }
    if ($DefenseLedgerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_NATIVE_DEFENSE_LEDGER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Native defense ledger probe did not complete.' }
    if ($DefenseLedgerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_WELFARE_RECREATE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Welfare serialization and actor recreation probe did not complete.' }
    if ($DefenseLedgerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DEFENSE_RELAY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Physical defense witness handoff probe did not complete.' }
    if ($WelfareProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_WELFARE_CHAIN_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Welfare encounter chain did not complete.' }
    if ($WelfareProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_WELFARE_SLOT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Actual welfare approach save/load did not complete.' }
    if ($CitizenThreatProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_KEEPER_THREAT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Keeper threat probe did not complete.' }
    if ($CitizenThreatProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INDOOR_DEFENDER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Indoor defender combat probe did not complete.' }
    if ($CitizenThreatProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INDOOR_DEFENSE_REPORT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Indoor witness defense report did not complete.' }
    if ($CitizenThreatProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INDOOR_DEFENSE_ARRIVAL_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Indoor defense arrival did not complete.' }
    if ($CitizenThreatProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INDOOR_ASSAULT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Indoor player assault probe did not complete.' }
    if ($CitizenThreatProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INDOOR_ENEMY_DEATH_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Indoor enemy death probe did not complete.' }
    if ($CitizenThreatProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARD_EFFECT_RECREATE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard effect recreation probe did not complete.' }
    if ($CitizenThreatProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARD_EFFECT_ELAPSED_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard effect elapsed-time probe did not complete.' }
    if ($GuardBuildingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARD_DOOR_EFFECT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard doorway effect probe did not complete.' }
    if ($GuardBuildingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARD_EFFECT_LOAD_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard effect game reload probe did not complete.' }
    if ($SleepProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_SLEEP_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Sleep probe did not complete.' }
    if ($RunningPilotProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_RUNNING_PILOT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Running pilot preview probe did not complete.' }
    if ($InnAlarmProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INN_ALARM_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Inn alarm probe did not complete.' }
    if ($InnAlarmProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INN_TRAVELER_CORPSE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Inn traveler corpse discovery probe did not complete.' }
    if ($InnAlarmProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_WITNESSED_INSPECTION_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Witnessed inn murder inspection did not complete.' }
    if ($ResponseProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_RESPONSE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Response probe did not complete.' }
    if ($GuardActorProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_GUARD_ACTOR_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard actor probe did not complete.' }
    if ($GuardActorProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARD_TORCH_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard torch probe did not complete.' }
    if ($GuardControllerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_GUARD_CONTROLLER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard controller probe did not complete.' }
    if ($GuardControllerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SURRENDER_REFUSAL_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard surrender refusal probe did not complete.' }
    if ($GuardControllerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PURSUIT_RECRUIT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Nearby guard pursuit recruitment probe did not complete.' }
    if ($GuardControllerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PURSUIT_REACQUIRE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard pursuit reacquisition probe did not complete.' }
    if ($InnAlarmProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_REPLACEMENT_INSPECTOR_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Replacement inspector probe did not complete.' }
    if ($GuardPatrolProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PATROL_WITNESS_ROUTE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Moving patrol witness route probe did not complete.' }
    if ($GuardControllerProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARD_TORCH_CONTROLLER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard torch controller probe did not complete.' }
    if ($GuardActorProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARD_QUIET_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Peaceful guard audio probe did not complete.' }
    if ($GuardPatrolProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARD_PATROL_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard patrol probe did not complete.' }
    if ($GuardDefenseProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARD_DEFENSE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard defense probe did not complete.' }
    if ($GuardDefenseProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_WELFARE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Outdoor welfare encounter did not complete.' }
    if ($TavernSurveyProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TAVERN_SURVEY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Tavern interior survey did not complete.' }
    if ($TavernSurveyProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INN_SOCIAL_MOVEMENT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Rendered inn patron movement did not complete.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_TAVERN_PATRON_PASS:') -ne $surveyedTavernCount)) { throw 'Local patron routing checks did not pass in all surveyed taverns.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_UNLOADED_SETTLEMENT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Unloaded settlement navigation comparison did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_UNLOADED_BOUNDARY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Unloaded settlement wilderness boundary comparison did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_UNLOADED_ROAD_ACCESS_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Unloaded street-to-road access comparison did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ROAD_BUILDING_ROUTE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Regional road-to-building route comparison did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SERVICE_INN_ROUTE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Service-to-inn route comparison did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_OVERNIGHT_TIMING_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Generated overnight timing did not pass native route and serialization checks.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_OVERNIGHT_ROUTE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Original-resident multi-day route generation did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_OVERNIGHT_CAPACITY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Remote household/inventory preparation and full-inn fallback did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PLANNED_RECEPTION_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Remote planned reception, native restore and delayed payment did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_KEEPER_ADMISSION_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Dead remote keeper admission and departure did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SAME_INN_STAY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Single-inn social overnight planning did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_LODGING_FIRST_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Late-arrival lodging-first planning did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_RESIDENT_PLANNER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Original resident trip planner did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TRAVEL_COMMIT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Resident travel commit lifecycle did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ADJACENT_ROAD_SAVE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Adjacent road native persistence did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DAILY_TRAVEL_BATCH_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Live daily travel batch did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_AUTOMATIC_OVERNIGHT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Automatic multi-day trip check did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DAY_VISIT_LODGING_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Late day visit lodging check did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_RESERVED_HOUSEHOLD_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Reserved traveler household check did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_UNVISITED_ORIGIN_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Unvisited origin automatic departure check did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_VISITOR_ORIGINS_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Visitor origin catalog and weighting check did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_WORK_STAGGER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Background work staggering did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_MODEL_CLEARANCE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Unloaded model approach collision comparison did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_MODEL_ROUTE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Unloaded model route collision comparison did not pass.' }
    if ($ResidentTripProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_UNLOADED_GATES_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Unloaded gate crossing comparison did not pass.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_RESIDENT_INN_ROOM_PASS:') -ne $surveyedTavernCount)) { throw 'Resident booked-room sleeping did not pass in all surveyed inns.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_VISIT_SETTLEMENT_SAVE_PASS:') -ne 6)) { throw 'Visit settlement persistence and legacy inference did not pass paid/refused cases in all surveyed inns.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_VISIT_ID_SAVE_PASS:') -ne 6)) { throw 'Stable visit ID persistence and legacy inference did not pass paid/refused cases in all surveyed inns.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_RESIDENT_INN_REFUSAL_PASS:') -ne $surveyedTavernCount)) { throw 'Resident refusal exits did not pass in all surveyed inns.' }
    if ($TavernSurveyProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TAVERN_ROOMS_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Stable tavern room partition did not complete.' }
    if ($TavernSurveyProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TAVERN_MARKER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Native rental-marker room mapping did not complete.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_NATIVE_RENTAL_PASS:') -ne $surveyedTavernCount)) { throw 'Native paid rental import did not pass in all surveyed taverns.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_UNRENTED_INVENTORY_PASS:') -ne $surveyedTavernCount)) { throw 'Unrented guest inventory did not pass in all surveyed taverns.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_UNLOADED_TAVERN_PASS:') -ne $surveyedTavernCount)) { throw 'Unloaded tavern inventory did not match all surveyed actual interiors.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_SLICED_TAVERN_PASS:') -ne $surveyedTavernCount)) { throw 'Sliced tavern inventory did not match all surveyed synchronous surveys.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_REMOTE_INN_PASS:') -ne $surveyedTavernCount)) { throw 'Remote inn records did not match all surveyed native inventories.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_REMOTE_RECEPTION_PASS:') -ne $surveyedTavernCount)) { throw 'Remote keeper routes did not match all surveyed loaded inns.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_REMOTE_CLAIMS_PASS:') -ne $surveyedTavernCount)) { throw 'Remote native inn claims were not verified for all surveyed inns.' }
    if ($TavernSurveyProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_REMOTE_CANCEL_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Remote survey cancellation and queue cleanup were not verified.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_REMOTE_RECEIPT_PASS:') -ne $surveyedTavernCount)) { throw 'Remote paid-room reconciliation was not verified for all surveyed inns.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_RESIDENT_ROOM_HOLD_PASS:') -ne $surveyedTavernCount)) { throw 'Multi-visit room ownership was not verified for all surveyed inns.' }
    if ($TavernSurveyProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_EXTERIOR_INVENTORY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Exterior inventory and paid receipt import did not complete.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_ROUTE_PASS:') -ne $surveyedTavernCount)) { throw 'Physical traveler road routes did not reach all surveyed taverns.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_RENTAL_PAYMENT_PASS:') -ne $surveyedTavernCount)) { throw 'Rental payment/vacancy checks did not pass in all surveyed taverns.' }
    if ($TavernSurveyProbe -and ((Get-LiteralCount $body 'PEOPLE_OF_THE_ILIAC_BAY_TAVERN_REST_PASS:') -ne $surveyedTavernCount)) { throw 'Occupied-bed rest checks did not pass in all surveyed taverns.' }
    if ($GuardBuildingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_GUARD_BUILDING_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard building probe did not complete.' }
    if ($GuardBuildingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PUBLIC_POSTS_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Public guard post check did not complete.' }
    if ($GuardBuildingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARD_DOOR_PURSUIT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Doorway pursuit check did not complete.' }
    if ($GuardBuildingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PENDING_GUARD_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Pending native guard report probe did not complete.' }
    if ($GuardWitnessProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_GUARD_WITNESS_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guard witness probe did not complete.' }
    if ($InteriorEscapeProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_WITNESS_MOVING_ATTACKER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Moving attacker witness probe did not complete.' }
    if ($KnowledgeBrowseOnly -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_KNOWLEDGE_BROWSE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Knowledge browse checks did not complete.' }
    if ($DialogueClockOnly -and ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_CLOCK_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or
        (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_RESULT: status=Ok;',[StringComparison]::OrdinalIgnoreCase) -ge 0))))) {
        throw 'The clock turn did not complete its live D3 routing and fresh model reply.'
    }
    if ($DialogueProbe -and -not $KnowledgeBrowseOnly -and -not $DialogueClockOnly -and ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_FOCUS_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GROUNDED_DIALOGUE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FLIRT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_CHILD_DIALOGUE_REPORT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_MEMORY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_HOME_DIALOGUE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))))) { throw 'Grounded dialogue, focus, flirt, child-report, memory or home checks did not complete.' }
    if ($HousingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_HOUSING_LIVE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Housing probe did not complete.' }
    if ($HousingProbe -and ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ENTRY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ENTRY_GUARD_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))))) { throw 'Entry witness probes did not complete.' }
    if ($HousingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ENTRY_MESSENGER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Entry messenger probe did not complete.' }
    if ($HousingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_KEEPER_IDENTITY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Keeper identity/bed reload probe did not complete.' }
    if ($HousingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_KEEPER_FAMILY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Keeper family/identity probe did not complete.' }
    if ($HousingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_BEREAVEMENT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Bereavement dialogue check did not complete.' }
    if ($HousingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_KEEPER_RESPONSE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Keeper response/physical reporting probe did not complete.' }
    if ($KeeperResponseProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_KEEPER_RESPONSE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Keeper response probe did not complete.' }
    if ($IndoorNoiseProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INDOOR_NOISE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Indoor hearing/waking probe did not complete.' }
    if ($IndoorStairsProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INTERIOR_STAIRS_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Upper-floor escape probe did not complete.' }
    if ($IndoorStairsProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INTERIOR_STAIRS_MOVEMENT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Actual upper-floor actor movement did not complete.' }
    if ($IndoorNoiseProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INDOOR_NOISE_REPORT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Indoor neutral reporting probe did not complete.' }
    if ($IndoorNoiseProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_INDOOR_NOISE_RELOAD_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Actual indoor wake reload probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WEAPON_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Native weapon keeper witness probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_KEEPER_DEATH_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Native keeper death/reload probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_KEEPER_VISITOR_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Dead keeper arriving-visitor regression did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_CORPSE_VISITOR_CONTACT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Corpse visitor physical guard-contact regression did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_NEUTRAL_INSPECTION_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Neutral report corpse inspection did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARDED_VISITOR_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Guarded crime scene visitor rejection did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_ORDINARY_SHOP_DEATH_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Ordinary shop NPC death/reload probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_KEEPER_SLEEP_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Native keeper sleep/wake/reload probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_CIVILIAN_DOORS_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Civilian door permission probe did not complete.' }
    if ($CrimeSceneProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_CRIME_SCENE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Crime scene probe did not complete.' }
    if ($CrimeSceneProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_UNCACHED_GUARD_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Uncached residential guard probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_UNCACHED_KEEPER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Uncached native keeper guard probe did not complete.' }
    if ($OutdoorSceneProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_SCENE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Outdoor scene probe did not complete.' }
    if ($InteriorEscapeProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_INTERIOR_ESCAPE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Interior escape probe did not complete.' }
    if ($QuestBindingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_QUEST_BINDING_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Quest binding probe did not complete.' }
    if ($QuestBindingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_QUEST_LEDGER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Quest ledger save/load probe did not complete.' }
    if ($QuestBindingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_QUEST_OFFER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Quest offer accept/reject probe did not complete.' }
    if ($QuestBindingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_QUEST_RECIPIENT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Quest recipient activation probe did not complete.' }
    if ($DeliveryProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_TOWN_DELIVERY_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Vanilla delivery hand-in did not complete.' }
    if ($MemoryArchiveProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_MEMORY_ARCHIVE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Shared memory archive probe did not complete.' }
    if ($DialogueRefusalProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_REFUSAL_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Real family-threat closure, saved memory, and reopened-chat refusal probe did not complete.' }
    if ($DecisionShadowProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DECISION_SHADOW_NATIVE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Native-snapshot deterministic shadow callback or no-dispatch assertions did not complete.' }
    if ($DecisionRoutingProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_NATIVE_PASS: live-stage=true; success=SOCIAL; technical-failure=null; callbacks=2; main-thread=true; cancelled=4; invalidations=close-save-scene-disable; archive-dialogue-action-state-unchanged=true; submit-free-text=true; exact-turn=true; route-callback-before-qwen=true; qwen-reply=remembered',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Live decision-routing native acceptance did not complete deterministic callbacks, invalidations, and the production Qwen reply.' }
    if ($DecisionRoutingProbe -and (-not (Test-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_NATIVE_PASS:' @{'selected-route'=@('SOCIAL','ACTION_REQUEST','OFFER_RESPONSE','COMMITMENT_UPDATE')}))) { throw 'Production SubmitFreeText did not report an accepted live D3 route.' }
    if ($DecisionRoutingAppraisalReopenProbe) {
        foreach ($marker in @('PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_BASELINE_RESTORED:', 'PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_PASS:')) {
            if ((Get-LiteralCount $body $marker) -ne 1) { throw ('DecisionRoutingAppraisalReopenProbe requires exactly one ' + $marker + ' marker.') }
        }
        if ($body.IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_BASELINE_RESTORED:', [StringComparison]::Ordinal) -gt $body.IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_PASS:', [StringComparison]::Ordinal)) { throw 'DecisionRoutingAppraisalReopenProbe PASS marker precedes baseline restoration.' }
    }
    if ($DecisionRoutingAppraisalReopenSpeech) {
        $speechTurnMarker = 'PEOPLE_OF_THE_ILIAC_BAY_APPRAISAL_REOPEN_SPEECH_TURN_PASS:'
        $speechPassMarker = 'PEOPLE_OF_THE_ILIAC_BAY_APPRAISAL_REOPEN_SPEECH_PASS:'
        if ((Get-LiteralCount $body $speechTurnMarker) -ne 3 -or
            (Get-LiteralCount $body $speechPassMarker) -ne 1) {
            throw 'Appraisal/reopen speech acceptance requires exactly three speech-turn markers and one speech PASS marker.'
        }
        $lastSpeechTurnAt = $body.LastIndexOf($speechTurnMarker, [StringComparison]::Ordinal)
        $speechPassAt = $body.IndexOf($speechPassMarker, [StringComparison]::Ordinal)
        $baselineAt = $body.IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_BASELINE_RESTORED:', [StringComparison]::Ordinal)
        if ($baselineAt -lt 0 -or $lastSpeechTurnAt -ge $baselineAt -or $baselineAt -ge $speechPassAt) {
            throw 'Appraisal/reopen speech PASS preceded all three speech turns or baseline restoration.'
        }
    }
    if ($DecisionRoutingSafeDowngradeProbe) {
        foreach ($marker in @('PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_SAFE_DOWNGRADE_BASELINE_RESTORED:', 'PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_SAFE_DOWNGRADE_CONTROLLED_ROUTE_PASS:', 'PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_SAFE_DOWNGRADE_PASS:')) {
            if ((Get-LiteralCount $body $marker) -ne 1) { throw ('DecisionRoutingSafeDowngradeProbe requires exactly one ' + $marker + ' marker.') }
        }
        $controlledAt = $body.IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_SAFE_DOWNGRADE_CONTROLLED_ROUTE_PASS:', [StringComparison]::Ordinal)
        $restoredAt = $body.IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_SAFE_DOWNGRADE_BASELINE_RESTORED:', [StringComparison]::Ordinal)
        $passAt = $body.IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_SAFE_DOWNGRADE_PASS:', [StringComparison]::Ordinal)
        if ($controlledAt -gt $restoredAt -or $restoredAt -gt $passAt) {
            throw 'DecisionRoutingSafeDowngradeProbe required controlled route, baseline restoration, then final PASS ordering.'
        }
    }
    if ($DecisionWillingnessProbe -and (-not (Test-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_DECISION_WILLINGNESS_NATIVE_PASS:' @{'native-snapshot'='true'; 'verified-wait-candidate'='true'; 'ordered-two-step'=@('available','unavailable'); 'deterministic-route'='ACTION_REQUEST'; 'route-no-effect'='true'; 'deterministic-willingness'='ACCEPT-DECLINE-invalid-null'; 'callbacks'=@('4','5'); 'main-thread'='true'; 'cancellation'='close-no-callback'; 'synthetic-accept'=@('true','false'); 'requested-duration-seconds'={param($v) $n=0; (Test-DecimalValue $v) -and [int]::TryParse($v,[ref]$n) -and $n -ge 10 -and $n -le 900}; 'requested-duration-matched'=@('true','false'); 'physical-wait'='completed'; 'wait-scheduled'={param($v) Test-ProbeDecimal $v 2 10 900.99}; 'wait-actual'={param($v) Test-ProbeDecimal $v 2 0 999.99}; 'wait-drift'={param($v) Test-ProbeDecimal $v 3 0 ([double]::MaxValue)}; 'routine-owner-released'='true'; 'submit-free-text'='true'; 'route-purpose'='npc-speech-act-route-v2'; 'route'=@('SOCIAL','ACTION_REQUEST','OFFER_RESPONSE','COMMITMENT_UPDATE'); 'willingness-purpose'='npc-action-willingness-v1'; 'willingness'=@('ACCEPT','DECLINE','not-observed'); 'wire-purpose-order'={param($v) -not [string]::IsNullOrEmpty($v)}; 'willingness-call'=@('true','false'); 'qwen-reply'='true'; 'turn'='Would-you-wait-here-for-ten-seconds'}))) { throw 'Native willingness probe missed deterministic choices, cancellation, live route, Qwen reply, or native Wait execution evidence.' }
    if ($DecisionShadowWaitCandidateCapture -and (-not (Test-ProbeReceipt $body 'PEOPLE_OF_THE_ILIAC_BAY_DECISION_SHADOW_NATIVE_PASS:' @{'native-wait-candidate'='captured'}))) { throw 'Native candidate capture did not complete with an actual Wait candidate.' }
    if ($DecisionShadowWaitCandidateCapture -and -not (Test-Path -LiteralPath (Join-Path $DecisionShadowCaptureDirectory 'native-wait-candidate.json'))) { throw 'Native Wait candidate sidecar was not written.' }
    if ($DialogueAvailabilityProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_AVAILABILITY_PASS: online-model=4; outage=unavailable; recovery=model; exact-transcript=6; disk-reload=true; speaker-identity=true; tracked-effects=inventory-wallet-action-task-unchanged.',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Native social replies, isolated outage persistence, recovery, or disk reload did not complete.' }
    if ($DialogueAvailabilityProbe -and ((([string]$body).IndexOf('Invalid NPC memory entry.',[StringComparison]::OrdinalIgnoreCase) -ge 0))) { throw 'Dialogue availability probe hit rejected unavailable memory persistence.' }
    if ($DialogueRefusalProbe) {
        if ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_REACTIONS_PASS: ordinaryQuestion=true; criticism=true; insultCloses=true; reopenRefusal=true; apologyAllowsTalk=true; resentmentRetained=true; noUnfoundedReport=true; fullSubmitFreeText=true',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) {
            throw 'Ordinary questions, criticism, insult closure/reopen and sincere apology did not pass through real chat.'
        }
        $traceLines = ([string]$body).Replace("`r",'').Split([char]10)
        $mobilePassIndex = Find-LogMarkerIndex $traceLines 'PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_MOBILE_REFUSAL_PASS:'
        $staticPassIndex = Find-LogMarkerIndex $traceLines 'PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_STATIC_REFUSAL_PASS:'
        if ($mobilePassIndex -lt 0 -or $staticPassIndex -le $mobilePassIndex) { throw 'Family-threat semantic trace markers are missing or out of order.' }
        $mobileTrace = ($traceLines[0..$mobilePassIndex] -join "`n")
        $staticTrace = ($traceLines[($mobilePassIndex + 1)..$staticPassIndex] -join "`n")
        if (-not (Test-SocialEffectSemanticTrace $mobileTrace 'family_threat' 'family_threat') -or
            -not (Test-SocialEffectSemanticTrace $staticTrace 'family_threat' 'family_threat')) {
            throw 'Family-threat refusal passed without two model-confirmed PLAYER|DIRECT scope traces for the actual mobile and static submissions.'
        }
        foreach ($phaseTrace in @($mobileTrace,$staticTrace)) {
            if ((-not ((([string]$phaseTrace).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_RESULT: status=Ok;',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) {
                throw 'Family-threat refusal missed a real accepted D3 route before its typed classification.'
            }
        }
        if ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_BASELINE_RESTORED:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) {
            throw 'Family-threat refusal did not restore the exact owned baseline.'
        }
    }
    if ($SemanticConversationProbe -and ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_BUSY_INTENT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_PAUSE_DISK_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_BUSY_CONTROL_PASS: index=0;',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_BUSY_CONTROL_PASS: index=1;',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_BUSY_CONTROL_PASS: index=2;',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_BUSY_CONTROL_PASS: index=3;',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SEMANTIC_CONVERSATION_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))))) { throw 'Semantic busy attribution, quoted/negative/future controls, disk restore, or manual Talk acceptance did not complete.' }
    if ($SemanticConversationProbe) {
        $traceLines = ([string]$body).Replace("`r",'').Split([char]10)
        $positivePassIndex = Find-LogMarkerIndex $traceLines 'PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_BUSY_INTENT_PASS:'
        if ($positivePassIndex -lt 0) { throw 'Semantic busy positive result marker is missing.' }
        $positiveTrace = ($traceLines[0..$positivePassIndex] -join "`n")
        if (-not (Test-SocialEffectSemanticTrace $positiveTrace 'busy_pause' 'busy_pause')) {
            throw 'Busy pause passed without model-confirmed PLAYER|DIRECT scope followed by the semantic busy intent.'
        }

        $previousIndex = $positivePassIndex + 1
        for ($control = 0; $control -lt 4; $control++) {
            $passMarker = 'PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_BUSY_CONTROL_PASS: index=' + $control + ';'
            $controlPassIndex = Find-LogMarkerIndex $traceLines $passMarker $previousIndex
            if ($controlPassIndex -lt 0) { throw "Busy control $control marker is missing or out of order." }
            $intentLines = @()
            if ($controlPassIndex -gt $previousIndex) {
                $intentLines = @($traceLines[$previousIndex..($controlPassIndex - 1)] |
                    Where-Object { (([string]$_).StartsWith('PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_INTENT:',[StringComparison]::OrdinalIgnoreCase)) })
            }
            if ($intentLines.Count -ne 1 -or (-not (([string]$intentLines[0]).EndsWith('; social=none',[StringComparison]::OrdinalIgnoreCase)))) {
                throw "Busy control $control did not produce exactly one semantic NONE result; offline reply alone is insufficient."
            }
            $wrongDirectScope = @($traceLines[$previousIndex..($controlPassIndex - 1)] |
                Where-Object { ((([string]$_).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_EFFECT_SCOPE: proposed=busy_pause; scope=PLAYER|DIRECT',[StringComparison]::OrdinalIgnoreCase) -ge 0)) })
            if ($wrongDirectScope.Count -gt 0) { throw "Busy control $control was marked PLAYER|DIRECT despite its no-pause expectation." }
            $previousIndex = $controlPassIndex + 1
        }

        if (-not (Test-Path -LiteralPath $semanticCapturePath) -or (Get-Item -LiteralPath $semanticCapturePath).Length -eq 0) {
            throw 'Owned semantic conversation probe completed without writing exact classifier request/response JSONL.'
        }
        $captureRows = @(
            foreach ($line in Get-Content -LiteralPath $semanticCapturePath) {
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                try { $line | ConvertFrom-Json -ErrorAction Stop }
                catch { throw 'Semantic classifier capture contains invalid JSONL.' }
            }
        )
        $routeRows = @($captureRows | Where-Object { $_.stage -eq 'route-intent' })
        $scopeRows = @($captureRows | Where-Object { $_.stage -eq 'social-effect-scope' })
        if ($routeRows.Count -ne 5 -or $scopeRows.Count -lt 1) {
            throw "Semantic classifier capture must contain five fresh route-intent traces and the positive social-effect-scope trace; got routes=$($routeRows.Count), scopes=$($scopeRows.Count)."
        }
    }
    if ($NpcInteractionProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_NPC_INTERACTION_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Reciprocal factual NPC interaction-memory probe did not complete.' }
    $vitalsMarker = if ($env:POTIB_PLAYER_HEALING_PROBE -eq '1') { 'PEOPLE_OF_THE_ILIAC_BAY_PLAYER_HEALING_PASS:' } else { 'PEOPLE_OF_THE_ILIAC_BAY_NPC_VITALS_PASS:' }
    if ($NpcVitalsProbe -and -not $body.Contains($vitalsMarker)) { throw 'Selected native NPC vitals probe did not complete.' }
    if ($MemoryArchiveModelProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_MEMORY_RETRIEVAL_CONTEXT_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Restored archive retrieval-context assertion did not complete.' }
    if ($MemoryArchiveModelProbe -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_MEMORY_MODEL_ANSWER_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Live restored-archive model answer assertion did not complete.' }
} finally {
    try {
        if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination "$repo/work/town-test.log" -Force }
    } finally {
        if (-not $owned.HasExited) { Stop-Process -InputObject $owned }
        if (-not $owned.WaitForExit(10000)) { throw 'Owned test player did not exit within 10 seconds.' }
        if ($QuestBindingProbe) {
            $probePackage = Join-Path $playerDir 'DaggerfallUnity_Data/StreamingAssets/Mods/peopleoftheiliacbayquestbindingprobe.dfmod'
            if (Test-Path -LiteralPath $probePackage) { Remove-Item -LiteralPath $probePackage }
        }
    }
}
