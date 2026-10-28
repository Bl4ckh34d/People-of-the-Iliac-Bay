using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class WorldState
    {
        public const int CurrentVersion = 85;
        public int Version = CurrentVersion;
        public string NpcFrameworkScopeId = Guid.NewGuid().ToString("N");
        public List<NpcMotivationState> NpcMotivations = new List<NpcMotivationState>();
        public List<NpcConversationCommandCursor> NpcConversationCommandCursors = new List<NpcConversationCommandCursor>();
        public List<NpcObservedSpeechSourceState> NpcObservedSpeechSources = new List<NpcObservedSpeechSourceState>();
        public List<NpcKnowledgeState> NpcKnowledge = new List<NpcKnowledgeState>();
        public PlayerItemProvenanceState PlayerItemProvenance;
        public PlayerCommerceReceiptLedgerState PlayerCommerceReceipts;
        public PersonalBarterReceiptLedgerState PersonalBarterReceipts;
        public NpcServiceContractLedgerState NpcServiceContracts;
        public NpcServiceNativeEvidenceState NpcServiceEvidence = new NpcServiceNativeEvidenceState();

        public WorldState()
        {
            NpcServiceContracts = NpcServiceContractLedger.Create().ToSaveState(NpcFrameworkScopeId);
            PlayerItemProvenance = PlayerItemProvenanceLedger.Create(NpcFrameworkScopeId);
            PlayerCommerceReceipts = new PlayerCommerceReceiptLedger(NpcFrameworkScopeId).Snapshot();
            PersonalBarterReceipts = new PersonalBarterReceiptLedger(NpcFrameworkScopeId).Snapshot();
        }
        public uint WorldSeed;
        public List<ConversationAttitude> ConversationAttitudes = new List<ConversationAttitude>();
        public List<ConversationMood> ConversationMoods = new List<ConversationMood>();
        public List<NpcConversationMemory> ConversationMemories = new List<NpcConversationMemory>();
        public List<NpcSceneMemory> SceneMemories = new List<NpcSceneMemory>();
        public List<NpcSceneMemorySummary> SceneMemorySummaries = new List<NpcSceneMemorySummary>();
        public List<ClosedShopVisit> ClosedShopVisits = new List<ClosedShopVisit>();
        public ConversationArchiveCheckpoint ConversationArchive;
        public ConversationCueArchiveCheckpoint ConversationCueArchive;
        public HauntingRoutineDay HauntingDay;
        public List<SettlementPopulation> Settlements = new List<SettlementPopulation>();
        public List<TownRoutineState> Routines = new List<TownRoutineState>();
        public List<HouseholdDayPlan> HouseholdDays = new List<HouseholdDayPlan>();
        public List<HouseholdJointProgress> HouseholdJointProgress = new List<HouseholdJointProgress>();
        public List<HouseholdJointSimulation> HouseholdJointSimulations = new List<HouseholdJointSimulation>();
        public List<NpcInnServiceCatalog> NpcInnServiceCatalogs = new List<NpcInnServiceCatalog>();
        public List<HouseholdInnServiceAdmission> HouseholdInnServices = new List<HouseholdInnServiceAdmission>();
        public List<QuestResidentBinding> QuestBindings = new List<QuestResidentBinding>();
        public List<CivilianIncident> AlarmIncidents = new List<CivilianIncident>();
        public long NextIncidentSerial = 1;
        public List<GuardRoster> Garrisons = new List<GuardRoster>();
        public List<NpcCustodyRecord> NpcCustodies = new List<NpcCustodyRecord>();
        public List<EntryReport> EntryReports = new List<EntryReport>();
        public List<NativePerson> NativePeople = new List<NativePerson>();
        public List<InnLodgerHomeReservation> InnLodgerHomes = new List<InnLodgerHomeReservation>();
        public List<DefenseCombatant> DefenseCombatants = new List<DefenseCombatant>();
        public List<TavernLodging> Lodgings = new List<TavernLodging>();
        public List<TravelerPerson> Travelers = new List<TravelerPerson>();
        public List<TravelerVisit> TravelerVisits = new List<TravelerVisit>();
        public List<VisitDeparture> VisitDepartures = new List<VisitDeparture>();
        public List<ObservedVisit> ObservedVisits = new List<ObservedVisit>();
        public List<NpcInnConsumptionCursor> NpcInnConsumptionCursors = new List<NpcInnConsumptionCursor>();
        public List<ShelterLock> ShelterLocks = new List<ShelterLock>();
        public List<ResidentTrip> ResidentTrips = new List<ResidentTrip>();
        public List<RetiredHousehold> HouseholdHistory = new List<RetiredHousehold>();
        public List<HouseholdRecoveryReservation> HouseholdRecoveries = new List<HouseholdRecoveryReservation>();
        public List<DialogueComplaintRecord> DialogueComplaints = new List<DialogueComplaintRecord>();
        public NpcVerbalCharge NpcVerbalCharge;
        public List<AppointmentRecord> Appointments = new List<AppointmentRecord>();
        public List<ProactiveContactRecord> RecentProactiveContacts = new List<ProactiveContactRecord>();
        public string ActiveProactiveContactId;
        public string LastProactiveContactId;
        public double LastProactiveContactAt = -1;
        public double PlayerProactiveContactBlockedUntil;
        public List<NpcActorProgress> NpcActors = new List<NpcActorProgress>();
        public List<NpcInventoryState> NpcInventories = new List<NpcInventoryState>();
        public List<NpcActionJob> NpcActions = new List<NpcActionJob>();
        public List<NpcTaskPlan> NpcTasks = new List<NpcTaskPlan>();
        public List<NpcTreatmentOutcomeHistory> NpcTreatmentOutcomeHistories = new List<NpcTreatmentOutcomeHistory>();
        public List<NpcHealthCareRecord> NpcHealthCares = new List<NpcHealthCareRecord>();
        public List<NpcMagicCareTask> NpcMagicCareTasks = new List<NpcMagicCareTask>();
        public List<NpcPersonalityProfile> NpcPersonalities = new List<NpcPersonalityProfile>();
        public List<NpcBiographyRecord> NpcBiographies = new List<NpcBiographyRecord>();
        public List<NpcAcuteFearState> NpcAcuteFears = new List<NpcAcuteFearState>();
        public List<NpcRoleState> NpcRoles = new List<NpcRoleState>();
        public NpcRelationshipLedger NpcRelationships = NpcRelationshipLedger.Create();
        public NpcExperienceInbox NpcExperienceInbox = NpcExperienceInbox.Create();
        public List<NpcMoodCauseHistoryFloor> NpcMoodCauseHistoryFloors = new List<NpcMoodCauseHistoryFloor>();
        public List<NpcPoisonOnsetState> NpcPoisonOnsets = new List<NpcPoisonOnsetState>();
        public List<RoutineRecoveryPlan> RoutineRecoveries = new List<RoutineRecoveryPlan>();

        // Private transaction candidate: only these three ledgers may be changed.
        // Other collections are read-only inputs to the final full validation.
        internal WorldState CandidateForOperationalRetention()
        {
            var candidate=(WorldState)MemberwiseClone();
            candidate.NpcTasks=new List<NpcTaskPlan>(NpcTasks);
            candidate.NpcActions=new List<NpcActionJob>(NpcActions);
            candidate.NpcMotivations=new List<NpcMotivationState>();
            foreach(var motivation in NpcMotivations)
                candidate.NpcMotivations.Add(NpcMotivationState.CopyValidated(motivation));
            return candidate;
        }

        internal WorldState CandidateForPoisonProjection()
        {
            var candidate=(WorldState)MemberwiseClone();
            candidate.NpcPoisonOnsets=new List<NpcPoisonOnsetState>();
            foreach(var onset in NpcPoisonOnsets)
                candidate.NpcPoisonOnsets.Add(NpcPoisonOnsetState.CopyValidated(onset));
            return candidate;
        }

        internal WorldState CandidateForExperienceRetention(Func<string,bool> knownNpcIdentity)
        {
            var candidate=(WorldState)MemberwiseClone();
            candidate.NpcExperienceInbox=NpcExperienceInbox.CopyValidated(NpcExperienceInbox,knownNpcIdentity);
            candidate.NpcMoodCauseHistoryFloors=new List<NpcMoodCauseHistoryFloor>();
            foreach(var floor in NpcMoodCauseHistoryFloors)
                candidate.NpcMoodCauseHistoryFloors.Add(NpcMoodCauseHistoryFloor.CopyValidated(floor));
            return candidate;
        }

        internal WorldState CandidateForPlayerCommerce()
        {
            var candidate = (WorldState)MemberwiseClone();
            candidate.NpcInventories = new List<NpcInventoryState>(NpcInventories);
            return candidate;
        }

        internal WorldState CandidateForPersonalBarter()
        {
            var candidate = (WorldState)MemberwiseClone();
            candidate.NpcInventories = new List<NpcInventoryState>(NpcInventories);
            return candidate;
        }
    }
    [Serializable]
    public sealed class RetiredHousehold
    {
        public int SettlementId;
        public double RetiredAt;
        public Household Household;
    }
    public sealed partial class WorldStateStore
    {
        private WorldState state;
        public string NpcFrameworkScopeId { get { return state.NpcFrameworkScopeId; } }
        public uint WorldSeed { get { return state.WorldSeed; } }
        public WorldStateStore(uint seed) { state = new WorldState { WorldSeed = seed }; }
        public bool HasSettlement(int settlementId) { return state.Settlements.Exists(s=>s.SettlementId==settlementId); }
        public Household ReserveHousehold(int settlementId,HomeProfile home)
        {
            if(home==null || home.BedCount<=0 || !home.AvailableForResidents || HousingReservedForRecovery(settlementId,home.BuildingKey))
                throw new ArgumentException("A reserved household requires available surveyed beds.");
            var generated=Population.CreateFamilies(state.WorldSeed,settlementId,new[]{home}).Households[0];
            var town=state.Settlements.Find(s=>s.SettlementId==settlementId);
            if(town!=null)
            {
                var existing=town.Households.Find(h=>h.BuildingKey==home.BuildingKey);
                if(existing!=null) return existing;
                if(!town.IsPartial) throw new ArgumentException("Reserved home is absent from the completed settlement.");
            }
            else
            {
                town=new SettlementPopulation {SettlementId=settlementId,IsPartial=true};
                state.Settlements.Add(town);
            }
            town.Households.Add(generated);town.Households.Sort((a,b)=>a.BuildingKey.CompareTo(b.BuildingKey));
            return generated;
        }
        public SettlementPopulation GetOrCreateFamilies(int settlementId, IEnumerable<HomeProfile> homes)
        {
            foreach (SettlementPopulation existing in state.Settlements)
                if (existing.SettlementId == settlementId)
                {
                    if(!existing.IsPartial) return existing;
                    // Validate the complete layout before replacing anything.
                    // Reserved people retain all names, relations and death state.
                    var complete=Population.CreateFamilies(state.WorldSeed,settlementId,homes);
                    foreach(var reserved in existing.Households)
                    {
                        int index=complete.Households.FindIndex(h=>h.BuildingKey==reserved.BuildingKey);
                        if(index<0 || complete.Households[index].BedCapacity<reserved.Residents.Count)
                            throw new ArgumentException("Reserved household no longer fits its surveyed home.");
                        complete.Households[index]=reserved;
                    }
                    existing.Households=complete.Households;existing.IsPartial=false;
                    return existing;
                }
            SettlementPopulation created = Population.CreateFamilies(state.WorldSeed, settlementId, homes);
            state.Settlements.Add(created);
            return created;
        }
        public SettlementPopulation GetOrCreate(int settlementId, IEnumerable<int> homeKeys, int maxResidents = 3)
        {
            foreach (SettlementPopulation existing in state.Settlements)
                if (existing.SettlementId == settlementId) return existing;
            SettlementPopulation created = Population.Create(state.WorldSeed, settlementId, homeKeys, maxResidents);
            state.Settlements.Add(created);
            return created;
        }
        public WorldState Snapshot() { return CopyValidated(state); }
        // Scalar read of current ownership; callers must not copy the entire
        // world's routes, memories and inventories to materialize one key.
        public bool TryResolveVerifiedResidentHome(Resident resident,out int settlementId)
        { return NpcStartingBelongings.TryResolveVerifiedResidentHome(state,resident,out settlementId); }
        public Resident ResolveResident(string residentId)
        {
            if (string.IsNullOrWhiteSpace(residentId)) return null;
            foreach (var town in state.Settlements)
            {
                var resident = ResolveResident(state, town.SettlementId, residentId);
                if (resident != null) return resident;
            }
            return null;
        }
        public Resident ResolveResident(int settlementId, string residentId)
        { return ResolveResident(state, settlementId, residentId); }
        public void ArchiveHouseholdAndInstall(int settlementId, int buildingKey, int expectedGeneration,
            double retiredAt, Household replacement)
        {
            Require(settlementId >= 0 && buildingKey >= 0 && expectedGeneration >= 0 && Finite(retiredAt) && retiredAt >= 0,
                "Invalid household retirement request.");
            Require(replacement != null && replacement.BuildingKey == buildingKey &&
                replacement.Generation == expectedGeneration + 1, "Replacement generation does not follow retired household.");
            var candidate = Snapshot();
            var town = candidate.Settlements.Find(s => s.SettlementId == settlementId);
            Require(town != null, "Unknown household settlement.");
            int index = town.Households.FindIndex(h => h.BuildingKey == buildingKey);
            Require(index >= 0 && town.Households[index].Generation == expectedGeneration,
                "Current household generation changed before retirement.");
            var retired = town.Households[index];
            Require(!candidate.InnLodgerHomes.Exists(r=>r.HomeSettlement==settlementId && r.HomeKey==buildingKey && r.Generation==expectedGeneration &&
                candidate.NativePeople.Exists(p=>p.Id==r.PersonId && p.IsAlive)),"A living absent lodger still belongs to this household.");
            Require((retired.Residents.Count > 0 || candidate.InnLodgerHomes.Exists(r=>r.HomeSettlement==settlementId && r.HomeKey==buildingKey && r.Generation==expectedGeneration)) && retired.Residents.TrueForAll(r => !r.IsAlive),
                "Every household member must be confirmed dead before retirement.");
            candidate.HouseholdHistory.Add(new RetiredHousehold { SettlementId = settlementId,
                RetiredAt = retiredAt, Household = retired });
            town.Households[index] = replacement;
            var recovery=candidate.HouseholdRecoveries.Find(r=>r.SettlementId==settlementId && r.BuildingKey==buildingKey && r.Generation==expectedGeneration);
            if(recovery!=null)
            {
                Require(recovery.Outcome==HouseholdVacancyOutcome.NewFamily && recovery.Status==HouseholdRecoveryStatus.Pending && retiredAt>=recovery.RecoveryAt,
                    "Household recovery is reserved for another outcome or is not due.");
                recovery.Status=HouseholdRecoveryStatus.Settled;
            }
            candidate.Routines.RemoveAll(r => r.SettlementId == settlementId);
            var validated=CopyValidated(candidate);
            // Preserve live guards, incidents and native identities held by runtime controllers.
            // Only this household transaction's records change after full validation succeeds.
            int townIndex=state.Settlements.FindIndex(s=>s.SettlementId==settlementId);
            state.Settlements[townIndex]=validated.Settlements.Find(s=>s.SettlementId==settlementId);
            state.HouseholdHistory=validated.HouseholdHistory;
            state.HouseholdRecoveries=validated.HouseholdRecoveries;
            state.Routines.RemoveAll(r=>r.SettlementId==settlementId);
            foreach(var native in state.NativePeople)
            {
                var liveHome=state.Settlements[townIndex].Households.Find(h=>h.Residents.Exists(r=>r.Id==native.Id));
                if(liveHome!=null) liveHome.Residents.Find(r=>r.Id==native.Id).NativeIdentity=native;
            }
            TravelerVisitRevision++;
        }
        public void Restore(WorldState incoming) { RestoreCore(incoming, null); }
        public void Restore(WorldState incoming, double loadedGameTime)
        { RestoreCore(incoming, loadedGameTime); }
        private void RestoreCore(WorldState incoming, double? loadedGameTime)
        {
            var candidate = CopyValidated(incoming);
            candidate = CopyValidated(candidate);
            var restoredMemories=PrepareMemoryIndex(candidate);
            // Shared-exchange aggregates predate source-aware replies. Rebuild them
            // from the complete selected ancestry plus pending journal tail when it
            // is available; retain orphan aggregates when no journal can validate them.
            foreach(var attitude in candidate.ConversationAttitudes)
            {
                NpcConversationMemory journal;
                if(!restoredMemories.TryGetValue(attitude.PersonId,out journal))continue;
                int successful=0;
                foreach(var entry in journal.Entries)
                    if(successful<1000000 && ConversationMemoryProvenance.IsSuccessfulSharedExchange(entry))successful++;
                attitude.SharedExchangeCount=successful;
            }
            if(conversationArchive!=null && candidate.ConversationArchive==null)
                candidate.ConversationArchive=SharedConversationArchive.LegacyCheckpoint(candidate.WorldSeed);
            if(conversationArchive!=null && candidate.ConversationCueArchive==null)
                candidate.ConversationCueArchive=new ConversationCueArchiveCheckpoint {
                    ArchiveId=candidate.ConversationArchive.ArchiveId};
            state = candidate;
            BumpAppointmentRevision();
            BumpHouseholdDayRevision();
            BumpHouseholdJointRevision();
            householdJointControllers.Clear();
            innLodgerHomeIndex.Clear();foreach(var home in state.InnLodgerHomes) innLodgerHomeIndex.Add(home.PersonId,home);
            memoryIndex=restoredMemories; memoryIndexOwner=state;
            IndexResidentTrips();
            IndexObservedVisits();
            IndexRoutineRecoveries();
            BumpRoutineRevision();
            TravelerVisitRevision++;
            bindingEpoch = new object();
            experienceBranchId = Guid.NewGuid().ToString("N");
            foreach (var poisonTicket in poisonAssignments.Values) poisonTicket.Revoked = true;
            poisonAssignments.Clear();
            pendingPoisonDrainCursor = 0;
        }
        public TownRoutineState GetRoutines(int settlementId)
        {
            Require(state.Version == WorldState.CurrentVersion, "Unsupported save schema. Start with fresh mod data.");
            Require(state.Settlements != null, "Missing settlements.");
            Require(state.Routines != null, "Missing routine cache.");
            TownRoutineState found = null;
            foreach (var item in state.Routines)
            {
                if (item == null || item.SettlementId != settlementId) continue;
                Require(found == null, "Invalid or duplicate routine settlement.");
                found = item;
            }
            if (found == null) return null;
            var settlement = FindRoutineSettlement(state, settlementId);
            return CopyValidatedRoutineTown(state, found, settlement);
        }
        // Dialogue needs one resident's stop list, not a deep copy of every town route.
        public ResidentRoutine GetResidentDialogueRoutine(int settlementId, string residentId)
        {
            if (string.IsNullOrEmpty(residentId)) return null;
            foreach (var town in state.Routines)
            {
                if (town == null || town.SettlementId != settlementId) continue;
                foreach (var routine in town.Residents)
                {
                    if (routine.ResidentId != residentId) continue;
                    var result = new ResidentRoutine { ResidentId = routine.ResidentId,
                        RouteGeometryVersion = routine.RouteGeometryVersion,
                        PersonalLaneVersion = routine.PersonalLaneVersion,
                        DestinationKey = routine.DestinationKey, OpenMinute = routine.OpenMinute,
                        CloseMinute = routine.CloseMinute, ItineraryDepartureSeconds = routine.ItineraryDepartureSeconds };
                    if (routine.Itinerary != null)
                        result.Itinerary = ResidentVisitJourney.Copy(routine.Itinerary);
                    else foreach(var point in routine.Route)
                        result.Route.Add(new SavedRoutePoint{X=point.X,Z=point.Z});
                    result.AdditionalOutings=ResidentRoutineDay.CopyAdditionalOutings(routine);
                    return result;
                }
            }
            return null;
        }
        public void SetRoutines(TownRoutineState routines)
        {
            if (routines == null) throw new ArgumentNullException(nameof(routines));
            Require(state.Version == WorldState.CurrentVersion, "Unsupported save schema. Start with fresh mod data.");
            Require(state.Settlements != null, "Missing settlements.");
            Require(state.Routines != null, "Missing routine cache.");
            var settlement = FindRoutineSettlement(state, routines.SettlementId);
            // Validate and deep-copy before replacing any live cache entry.
            var validated = CopyValidatedRoutineTown(state, routines, settlement);
            int existingIndex = -1;
            for (int i = 0; i < state.Routines.Count; i++)
            {
                var existing = state.Routines[i];
                if (existing != null && existing.SettlementId == routines.SettlementId)
                {
                    Require(existingIndex < 0, "Invalid or duplicate routine settlement.");
                    existingIndex = i;
                }
            }
            if (existingIndex < 0) state.Routines.Add(validated);
            else state.Routines[existingIndex] = validated;
            BumpRoutineRevision();
            // Replacing only the routine list preserves live resident bindings and every other town.
        }

        public void SetResidentRoutine(int settlementId,ResidentRoutine routine)
        {
            if(routine==null) throw new ArgumentNullException(nameof(routine));
            var settlement=FindRoutineSettlement(state,settlementId);
            var town=state.Routines.Find(item=>item.SettlementId==settlementId);
            Require(town!=null,"Missing resident routine town.");
            int index=town.Residents.FindIndex(item=>item.ResidentId==routine.ResidentId);
            Require(index>=0,"Missing resident routine.");
            if(town.ItineraryVersion<2 && routine.AdditionalOutings!=null && routine.AdditionalOutings.Count>0)
            {
                var candidate=new TownRoutineState{SettlementId=settlementId,ItineraryVersion=2,
                    Residents=new List<ResidentRoutine>(town.Residents)};
                candidate.Residents[index]=routine;
                // Validate the whole promoted format before publishing any change.
                var promoted=CopyValidatedRoutineTown(state,candidate,settlement);
                state.Routines[state.Routines.IndexOf(town)]=promoted;
                BumpRoutineRevision();
                return;
            }
            var verified=CopyValidatedRoutineTown(state,new TownRoutineState {SettlementId=settlementId,
                ItineraryVersion=town.ItineraryVersion,Residents=new List<ResidentRoutine>{routine}},settlement).Residents[0];
            town.Residents[index]=verified;
            BumpRoutineRevision();
        }

        private static SettlementPopulation FindRoutineSettlement(WorldState source, int settlementId)
        {
            Require(settlementId >= 0 && source.Settlements != null, "Invalid routine settlement.");
            SettlementPopulation found = null;
            foreach (var settlement in source.Settlements)
            {
                if (settlement == null || settlement.SettlementId != settlementId) continue;
                Require(found == null, "Invalid or duplicate settlement.");
                found = settlement;
            }
            Require(found != null, "Invalid routine settlement.");
            return found;
        }

        private static TownRoutineState CopyValidatedRoutineTown(WorldState source, TownRoutineState town,
            SettlementPopulation settlement)
        {
            Require(town != null && settlement != null && town.SettlementId == settlement.SettlementId,
                "Invalid routine settlement.");
            Require(town.Residents != null, "Missing resident routines.");
            var residents = new Dictionary<string, Resident>(StringComparer.Ordinal);
            Require(settlement.Households != null, "Missing households.");
            foreach (var home in settlement.Households)
            {
                Require(home != null && home.Residents != null, "Missing household residents.");
                foreach (var resident in home.Residents)
                {
                    Require(resident != null && !string.IsNullOrWhiteSpace(resident.Id), "Invalid resident identity.");
                    residents.Add(resident.Id, resident);
                }
            }
            var cachedResidents = new HashSet<string>(StringComparer.Ordinal);
            int itineraryVersion = town.ItineraryVersion;
            Require(itineraryVersion >= 0 && itineraryVersion <= 2, "Unsupported routine itinerary version.");
            var townCopy = new TownRoutineState { SettlementId = town.SettlementId, ItineraryVersion = itineraryVersion };
            foreach (var routine in town.Residents)
            {
                Require(routine != null && routine.ResidentId != null && residents.ContainsKey(routine.ResidentId) &&
                    cachedResidents.Add(routine.ResidentId), "Invalid or duplicate resident routine.");
                Require(routine.Route != null && routine.Route.Count <= 10000, "Invalid route points.");
                Require(itineraryVersion>=2 || routine.AdditionalOutings==null || routine.AdditionalOutings.Count==0,
                    "Additional outings require the multi-outing format.");
                Require(routine.RouteGeometryVersion>=0 && routine.RouteGeometryVersion<=OutdoorRoutineMigration.CurrentVersion,
                    "Unsupported route geometry version.");
                Require(routine.PersonalLaneVersion==0 && routine.RouteGeometryVersion<=OutdoorRoutineMigration.CenterlineVersion ||
                    routine.PersonalLaneVersion>=1 && routine.PersonalLaneVersion<=OutdoorRoutineMigration.CurrentPersonalLaneVersion &&
                    routine.RouteGeometryVersion==OutdoorRoutineMigration.CurrentVersion,"Invalid saved personal lane version.");
                if (routine.DestinationKey == -1)
                    Require(routine.Route.Count == 0 && routine.OpenMinute == 0 && routine.CloseMinute == 0, "Invalid home routine.");
                else
                    Require(residents[routine.ResidentId].AgeGroup == ResidentAgeGroup.Adult &&
                        routine.DestinationKey >= 0 && routine.DestinationKey != residents[routine.ResidentId].HomeKey &&
                        routine.OpenMinute >= 0 && routine.OpenMinute < routine.CloseMinute && routine.CloseMinute <= 1440 &&
                        routine.Route.Count >= 2, "Invalid visit routine.");
                ResidentDestination itinerary = null;
                double departureSeconds = 0;
                if (routine.Itinerary != null)
                {
                    var resident = residents[routine.ResidentId];
                    Require(itineraryVersion >= 1 && resident.AgeGroup == ResidentAgeGroup.Adult,
                        "Only adult residents with planned itinerary schema may have itineraries.");
                    departureSeconds = routine.ItineraryDepartureSeconds;
                    Require(Finite(departureSeconds) && departureSeconds >= 0 && departureSeconds < 86400,
                        "Invalid itinerary departure time.");
                    itinerary = ResidentVisitJourney.Copy(routine.Itinerary);
                    Require(!itinerary.ThroughRoute, "Daily itinerary must return to its home route origin.");
                    foreach (var stop in itinerary.Stops)
                    {
                        Require(stop.Purpose != ResidentVisitPurpose.Inn, "Daily itineraries cannot reserve overnight inns.");
                        ValidateItineraryPoints(stop.Route);
                    }
                    ValidateItineraryPoints(itinerary.ExitRoute);
                    var journey = ResidentVisitJourney.Create(departureSeconds, itinerary);
                    Require(journey.EndsAt < 86400, "Daily itinerary must return before the next day.");
                }
                else
                    Require(routine.ItineraryDepartureSeconds == 0,
                        "Missing itinerary for saved departure time.");
                Require(itineraryVersion >= 1 || itinerary == null,
                    "Routine itinerary version is missing.");
                var saved = new ResidentRoutine { ResidentId = routine.ResidentId, DestinationKey = routine.DestinationKey,
                    RouteGeometryVersion = routine.RouteGeometryVersion,
                    PersonalLaneVersion = routine.PersonalLaneVersion,
                    OpenMinute = routine.OpenMinute, CloseMinute = routine.CloseMinute, Itinerary = itinerary,
                    ItineraryDepartureSeconds = itinerary == null ? 0 : departureSeconds };
                saved.AdditionalOutings=ResidentRoutineDay.CopyAdditionalOutings(routine);
                foreach(var outing in saved.AdditionalOutings)
                {
                    foreach(var stop in outing.Itinerary.Stops)ValidateItineraryPoints(stop.Route);
                    ValidateItineraryPoints(outing.Itinerary.ExitRoute);
                }
                double length = 0; SavedRoutePoint previous = null;
                foreach (var point in routine.Route)
                {
                    Require(point != null && !double.IsNaN(point.X) && !double.IsNaN(point.Z) &&
                        Math.Abs(point.X) <= 1000000 && Math.Abs(point.Z) <= 1000000, "Invalid route coordinate.");
                    saved.Route.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
                    if (previous != null) length += Math.Abs(point.X - previous.X) + Math.Abs(point.Z - previous.Z);
                    previous = point;
                }
                Require(routine.DestinationKey == -1 || length > 0, "Empty route length.");
                townCopy.Residents.Add(saved);
            }
            return townCopy;
        }

        private static WorldState CopyValidated(WorldState source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Require(source.Version == WorldState.CurrentVersion, "Unsupported save schema. Start with fresh mod data.");
            Require(source.Settlements != null, "Missing settlements.");
            var copy = new WorldState { WorldSeed = source.WorldSeed };
            Guid frameworkScope;
            Require(Guid.TryParseExact(source.NpcFrameworkScopeId, "N", out frameworkScope), "Invalid NPC framework save scope.");
            copy.NpcFrameworkScopeId = source.NpcFrameworkScopeId;
            copy.PlayerItemProvenance = PlayerItemProvenanceLedger.CopyValidated(source.PlayerItemProvenance, copy.NpcFrameworkScopeId);
            Require(PlayerCommerceReceiptLedger.Validate(source.PlayerCommerceReceipts,
                copy.NpcFrameworkScopeId), "Invalid player commerce receipt ledger.");
            copy.PlayerCommerceReceipts = source.PlayerCommerceReceipts.Copy();
            Require(PersonalBarterReceiptLedger.Validate(source.PersonalBarterReceipts,
                copy.NpcFrameworkScopeId), "Invalid personal barter receipt ledger.");
            copy.PersonalBarterReceipts = source.PersonalBarterReceipts.Copy();
            copy.NpcServiceContracts = NpcServiceContractLedger.Create().ToSaveState(copy.NpcFrameworkScopeId);
            copy.ConversationAttitudes = CopyConversationAttitudes(source);
            copy.ConversationMoods = CopyConversationMoods(source);
            copy.ConversationMemories = CopyConversationMemories(source);
            copy.SceneMemories = CopySceneMemories(source);
            copy.SceneMemorySummaries = CopySceneMemorySummaries(source);
            copy.ClosedShopVisits = CopyClosedShopVisits(source);
            copy.ConversationArchive = ConversationArchiveCheckpoint.CopyValidated(source.ConversationArchive);
            copy.ConversationCueArchive = ConversationCueArchiveCheckpoint.CopyValidated(source.ConversationCueArchive);
            Require((copy.ConversationArchive == null) == (copy.ConversationCueArchive == null) &&
                (copy.ConversationArchive == null ||
                 copy.ConversationArchive.ArchiveId == copy.ConversationCueArchive.ArchiveId),
                "Conversation cue checkpoint must match its memory archive namespace.");
            if(source.HauntingDay!=null)
            {
                double day=source.HauntingDay.Day;
                Require(!double.IsNaN(day) && !double.IsInfinity(day) && day>=0 && day==Math.Floor(day),"Invalid haunting routine day.");
                copy.HauntingDay=new HauntingRoutineDay { Day=day,Restricted=source.HauntingDay.Restricted };
            }
            var settlements = new HashSet<int>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (SettlementPopulation town in source.Settlements)
            {
                Require(town != null, "Null settlement.");
                Require(town.SettlementId >= 0 && settlements.Add(town.SettlementId), "Invalid or duplicate settlement.");
                Require(town.Households != null, "Missing households.");
                var townCopy = new SettlementPopulation { SettlementId = town.SettlementId, IsPartial=town.IsPartial };
                var homes = new HashSet<int>();
                foreach (Household home in town.Households)
                {
                    Require(home != null, "Null household.");
                    Require(home.BuildingKey >= 0 && homes.Add(home.BuildingKey), "Invalid or duplicate household.");
                    int bedCapacity = home.BedCapacity;
                    Require(home.Generation >= 0 && bedCapacity >= -1 && (home.Generation == 0 || bedCapacity > 0),
                        "Invalid saved household generation or bed capacity.");
                    Require(home.Residents != null && home.Residents.Count <=
                        (bedCapacity >= 0 ? bedCapacity : 5), "Missing or overcrowded household residents.");
                    var homeCopy = new Household { BuildingKey = home.BuildingKey,
                        Generation = home.Generation,
                        HeadId = home.HeadId, Surname = home.Surname,
                        BedCapacity = bedCapacity,
                        SurnameSeed = home.SurnameSeed };
                    foreach (Resident resident in home.Residents)
                    {
                        Require(resident != null, "Null resident.");
                        Require(!string.IsNullOrWhiteSpace(resident.Id) && identities.Add(resident.Id), "Missing or duplicate resident identity.");
                        Require(resident.HomeKey == home.BuildingKey, "Resident belongs to a different home.");
                        Require(resident.IsAlive || !resident.IsQuestProtected, "Protected resident cannot be dead.");
                        homeCopy.Residents.Add(new Resident { Id = resident.Id, HomeKey = resident.HomeKey,
                            FullName = resident.FullName, Race = resident.Race, BedSlot = resident.BedSlot,
                            AppearanceSeed = resident.AppearanceSeed, IsAlive = resident.IsAlive, IsQuestProtected = resident.IsQuestProtected,
                            AgeGroup = resident.AgeGroup, Sex = resident.Sex,
                            GuildIdentityKnown = resident.GuildIdentityKnown, GuildFactionId = resident.GuildFactionId,
                            GuildRank = resident.GuildRank, GuildHomeKey = resident.GuildHomeKey,
                            PartnerId = resident.PartnerId, MotherId = resident.MotherId, FatherId = resident.FatherId });
                    }
                    ValidateFamily(homeCopy);
                    townCopy.Households.Add(homeCopy);
                }
                copy.Settlements.Add(townCopy);
            }
            {
                Require(source.HouseholdHistory != null, "Missing household history.");
                var historyKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var retired in source.HouseholdHistory)
                {
                    Require(retired != null && settlements.Contains(retired.SettlementId) && Finite(retired.RetiredAt) &&
                        retired.RetiredAt >= 0 && retired.Household != null, "Invalid retired household record.");
                    var activeTown = copy.Settlements.Find(t => t.SettlementId == retired.SettlementId);
                    var active = activeTown.Households.Find(h => h.BuildingKey == retired.Household.BuildingKey);
                    Require(retired.Household.BuildingKey >= 0 && retired.Household.Generation >= 0 && active != null &&
                        retired.Household.Generation < active.Generation &&
                        historyKeys.Add(HistoryKey(retired.SettlementId, retired.Household.BuildingKey, retired.Household.Generation)),
                        "Invalid or duplicate retired household generation.");
                    var archived = CopyHousehold(source, retired.SettlementId, retired.Household, identities);
                    bool hasLodger=source.InnLodgerHomes!=null && source.InnLodgerHomes.Exists(r=>r!=null &&
                        r.HomeSettlement==retired.SettlementId && r.HomeKey==archived.BuildingKey && r.Generation==archived.Generation &&
                        source.NativePeople!=null && source.NativePeople.Exists(p=>p.Id==r.PersonId && !p.IsAlive));
                    Require((archived.Residents.Count > 0 || hasLodger) && archived.Residents.TrueForAll(r => !r.IsAlive),
                        "Retired household contains a living member.");
                    copy.HouseholdHistory.Add(new RetiredHousehold { SettlementId = retired.SettlementId,
                        RetiredAt = retired.RetiredAt, Household = archived });
                }
                foreach (var town in copy.Settlements)
                foreach (var home in town.Households)
                    for (int generation = 0; generation < home.Generation; generation++)
                        Require(historyKeys.Contains(HistoryKey(town.SettlementId, home.BuildingKey, generation)),
                            "Household history has a missing prior generation.");
            }
            Require(source.Routines != null, "Missing routine cache.");
            var cachedTowns = new HashSet<int>();
            foreach (var town in source.Routines)
            {
                Require(town != null && settlements.Contains(town.SettlementId) && cachedTowns.Add(town.SettlementId), "Invalid routine settlement.");
                var settlement = copy.Settlements.Find(item => item.SettlementId == town.SettlementId);
                copy.Routines.Add(CopyValidatedRoutineTown(source, town, settlement));
            }
            {
                copy.QuestBindings = source.QuestBindings;
                copy.QuestBindings = CopyQuestBindings(copy);
            }
            copy.AlarmIncidents = CopyAlarms(source);
            {
                Require(source.NextIncidentSerial > 0 && source.NextIncidentSerial < long.MaxValue &&
                    copy.AlarmIncidents.TrueForAll(x => x.GenerationSerial < source.NextIncidentSerial),
                    "Invalid next incident generation serial.");
                copy.NextIncidentSerial = source.NextIncidentSerial;
            }
            {
                Require(source.Garrisons != null, "Missing garrison ledger.");
                var garrisons = new HashSet<int>();
                foreach (var garrison in source.Garrisons)
                {
                    Require(garrison != null && settlements.Contains(garrison.SettlementId) && garrisons.Add(garrison.SettlementId), "Unknown or duplicate garrison settlement.");
                    copy.Garrisons.Add(GuardRoster.CopyValidated(garrison));
                }
            }
            copy.NpcCustodies=CopyNpcCustodies(source,copy);
            ValidateOutdoorCorpseInquiryWindows(copy);
            ValidateCorpseSuspectEvidence(copy);
            foreach (var alarm in copy.AlarmIncidents)
                if (alarm.Scene != null && alarm.Scene.Discovered)
                {
                    var roster = copy.Garrisons.Find(g => g.SettlementId == alarm.SettlementId);
                    var report = roster == null ? null : roster.Reports.Find(r => r.Id == "discovery:" + alarm.Id);
                    Require(report != null && report.Investigation && report.BuildingKey == alarm.BuildingKey &&
                        report.HasArrival && report.FirstArrivalAt == alarm.Scene.DiscoveredAt,
                        "Discovered scene lacks matching guard arrival evidence.");
                }
            copy.EntryReports = CopyEntries(source);
            copy.NativePeople = CopyNativePeople(source);
            copy.Lodgings = CopyLodgings(source,copy);
            copy.InnLodgerHomes=CopyInnLodgerHomes(source,copy);
            CopyTravelers(source,copy,identities);
            // Corpse/defense witnesses can be actual visitors with an origin
            // household, so validate their visit identity before their evidence.
            copy.DefenseCombatants = CopyDefense(source,copy);
            copy.VisitDepartures=CopyVisitDepartures(source);
            copy.ObservedVisits=CopyObservedVisits(source);
            copy.ShelterLocks=CopyShelterLocks(source);
            copy.ResidentTrips=CopyResidentTrips(source,copy);
            copy.HouseholdRecoveries=CopyHouseholdRecoveries(source,copy);
            copy.NpcPersonalities=CopyNpcPersonalities(source,copy);
            copy.NpcBiographies=CopyNpcBiographies(source,copy);
            copy.NpcAcuteFears=CopyNpcAcuteFears(source,copy);
            copy.NpcRelationships=CopyNpcRelationships(source,copy);
            copy.NpcExperienceInbox=CopyNpcExperienceInbox(source,copy);
            copy.NpcMoodCauseHistoryFloors=CopyNpcMoodCauseHistoryFloors(source,copy);
            copy.NpcObservedSpeechSources=CopyNpcObservedSpeechSources(source,copy);
            ValidateNpcConversationBoundaries(copy);
            copy.NpcVerbalCharge=CopyNpcVerbalCharge(source,copy);
            copy.NpcPoisonOnsets=CopyNpcPoisonOnsets(source,copy);
            copy.NpcMotivations=CopyNpcMotivations(source,copy);
            {
                Require(source.NpcConversationCommandCursors != null, "Missing NPC conversation command cursors.");
                var commandOwners = new HashSet<string>(StringComparer.Ordinal);
                foreach (var cursor in source.NpcConversationCommandCursors)
                {
                    var checkedCursor = NpcConversationCommandCursor.CopyValidated(cursor);
                    bool alive;
                    Require(commandOwners.Add(checkedCursor.PersonId) && KnownNpcIdentity(copy, checkedCursor.PersonId, out alive),
                        "Unknown or duplicate NPC conversation command cursor owner.");
                    copy.NpcConversationCommandCursors.Add(checkedCursor);
                }
            }
            copy.NpcKnowledge=CopyNpcKnowledge(source,copy);
            copy.NpcRoles=CopyNpcRoles(source,copy);
            Func<string, bool> knownServiceActor = id =>
            {
                bool alive;
                return KnownNpcIdentity(copy, id, out alive);
            };
            copy.NpcServiceEvidence = CopyNpcServiceNativeEvidence(source, copy, knownServiceActor);
            copy.NpcServiceContracts = CopyNpcServiceContracts(source, copy, copy.NpcServiceEvidence, knownServiceActor);
            copy.DialogueComplaints=CopyDialogueComplaints(source);
            copy.Appointments=CopyAppointments(source,copy);
            copy.RecentProactiveContacts=CopyProactiveContacts(source,copy);
            copy.ActiveProactiveContactId=source.ActiveProactiveContactId;
            copy.LastProactiveContactId=source.LastProactiveContactId;
            copy.LastProactiveContactAt=source.LastProactiveContactAt;
            Require(Finite(source.PlayerProactiveContactBlockedUntil)&&source.PlayerProactiveContactBlockedUntil>=0,
                "Invalid proactive-contact suppression time.");
            copy.PlayerProactiveContactBlockedUntil=source.PlayerProactiveContactBlockedUntil;
            ValidateProactiveContactPointers(copy);
            copy.NpcActors=CopyNpcActors(source,copy);
            foreach (var motivation in copy.NpcMotivations)
            {
                var actor = copy.NpcActors.Find(x => x.PersonId == motivation.OwnerPersonId);
                long vitalsRevision = actor == null ? 0 : actor.VitalsRevision;
                Require(motivation.LastHealthNeedVitalsRevision <= vitalsRevision &&
                    motivation.LastHealthGoalVitalsRevision <= vitalsRevision &&
                    motivation.LastStartedSafetySerial < copy.NextIncidentSerial,
                    "Native motivation cursor exceeds its saved source.");
            }
            copy.NpcInventories=CopyNpcInventories(source,copy);
            foreach (var receipt in copy.PlayerCommerceReceipts.Entries)
            {
                var owner = copy.NpcInventories.Find(x => x.PersonId == receipt.NpcId);
                Require(owner != null && owner.Revision >= receipt.NpcInventoryRevisionAfter &&
                    receipt.NpcWalletRevisionAfter == receipt.NpcInventoryRevisionAfter - (receipt.Gold == 0 ? 1 : 0) &&
                    (owner.Revision != receipt.NpcInventoryRevisionAfter ||
                     owner.LastTransactionId == receipt.TransactionId),
                    "Player commerce receipt exceeds its saved NPC inventory source.");
            }
            foreach (var entry in copy.PersonalBarterReceipts.Entries)
            {
                var owner = copy.NpcInventories.Find(x => x.PersonId == entry.Receipt.NpcId);
                Require(owner != null && owner.Revision >= entry.Receipt.NpcInventoryRevisionAfter &&
                    (owner.Revision != entry.Receipt.NpcInventoryRevisionAfter ||
                     owner.LastTransactionId == entry.Receipt.TransactionId),
                    "Personal barter receipt exceeds its saved NPC inventory source.");
            }
            copy.NpcInnConsumptionCursors=CopyNpcInnConsumptionCursors(source,copy);
            copy.NpcActions=CopyNpcActions(source,copy);
            copy.NpcTasks=CopyNpcTasks(source,copy);
            copy.NpcTreatmentOutcomeHistories=CopyNpcTreatmentOutcomeHistories(source,copy);
            copy.NpcHealthCares=CopyNpcHealthCareRecords(source,copy);
            copy.NpcMagicCareTasks=CopyNpcMagicCareTasks(source,copy);
            ValidateNpcOperationAdmissions(copy);
            foreach (var native in copy.NativePeople)
            {
                if(native.NativeLayoutKey!=null)continue; // Layout-only NPCs never head a simulated household.
                var home = copy.Settlements.Find(t => t.SettlementId == native.SettlementId).Households.Find(h => h.Residents.Exists(r => r.Id == native.Id));
                if (home != null) home.Residents.Find(r => r.Id == native.Id).NativeIdentity = native;
            }
            copy.HouseholdDays=CopyHouseholdDays(source,copy);
            copy.HouseholdJointProgress=CopyHouseholdJointProgress(source,copy);
            copy.HouseholdJointSimulations=CopyHouseholdJointSimulations(source,copy);
            copy.NpcInnServiceCatalogs=CopyNpcInnServiceCatalogs(source,copy);
            copy.HouseholdInnServices=CopyHouseholdInnServices(source,copy);
            foreach(var visit in copy.ObservedVisits)
                if(visit.HouseholdDayId!=null)ValidateHouseholdInnVisit(copy,visit,false);
            copy.RoutineRecoveries=CopyRoutineRecoveries(source,copy);
            return copy;
        }
        private static string HistoryKey(int settlementId, int buildingKey, int generation)
        { return settlementId + ":" + buildingKey + ":" + generation; }
        private static Household CopyHousehold(WorldState source, int settlementId, Household home, HashSet<string> identities)
        {
            Require(home != null && home.BuildingKey >= 0 && home.Generation >= 0, "Invalid household generation.");
            int bedCapacity = home.BedCapacity;
            Require(bedCapacity >= -1 && (home.Generation == 0 || bedCapacity > 0) && home.Residents != null && home.Residents.Count <=
                (bedCapacity >= 0 ? bedCapacity : 5), "Missing or overcrowded household residents.");
            var result = new Household { BuildingKey = home.BuildingKey, Generation = home.Generation,
                HeadId = home.HeadId, Surname = home.Surname, BedCapacity = bedCapacity,
                SurnameSeed = home.SurnameSeed };
            foreach (var resident in home.Residents)
            {
                Require(resident != null && !string.IsNullOrWhiteSpace(resident.Id) && identities.Add(resident.Id) &&
                    resident.HomeKey == home.BuildingKey && (resident.IsAlive || !resident.IsQuestProtected),
                    "Invalid or duplicate resident identity.");
                result.Residents.Add(new Resident { Id = resident.Id, HomeKey = resident.HomeKey, FullName = resident.FullName,
                    Race = resident.Race, BedSlot = resident.BedSlot, AppearanceSeed = resident.AppearanceSeed,
                    IsAlive = resident.IsAlive, IsQuestProtected = resident.IsQuestProtected,
                    AgeGroup = resident.AgeGroup, Sex = resident.Sex,
                    GuildIdentityKnown = resident.GuildIdentityKnown, GuildFactionId = resident.GuildFactionId,
                    GuildRank = resident.GuildRank, GuildHomeKey = resident.GuildHomeKey,
                    PartnerId = resident.PartnerId, MotherId = resident.MotherId, FatherId = resident.FatherId });
            }
            ValidateFamily(result);
            return result;
        }
        private static Resident ResolveResident(WorldState world, int settlementId, string residentId)
        {
            var town = world.Settlements.Find(s => s.SettlementId == settlementId);
            if (town != null)
                foreach (var home in town.Households)
                { var resident = home.Residents.Find(r => r.Id == residentId); if (resident != null) return resident; }
            foreach (var retired in world.HouseholdHistory)
                if (retired.SettlementId == settlementId)
                { var resident = retired.Household.Residents.Find(r => r.Id == residentId); if (resident != null) return resident; }
            // A bound visitor is the actual origin resident. Standalone native
            // fixtures still use a detached projection without a household.
            var traveler=world.Travelers.Find(p=>p.Id==residentId);
            if(traveler!=null && world.TravelerVisits.Exists(v=>v.PersonId==residentId && v.SettlementId==settlementId))
                return OriginResident(world,traveler) ?? TravelerAlarmResident(traveler);
            return null;
        }
        private static void ValidateItineraryPoints(List<SavedRoutePoint> points)
        {
            foreach (var point in points)
                Require(point != null && Finite(point.X) && Finite(point.Z) && Math.Abs(point.X) <= 1000000 &&
                    Math.Abs(point.Z) <= 1000000, "Invalid itinerary route coordinate.");
        }
        private static bool Finite(double number) { return !double.IsNaN(number) && !double.IsInfinity(number); }
        private static void ValidateFamily(Household home)
        {
            Require(home.Surname == null || !string.IsNullOrWhiteSpace(home.Surname) && home.Surname.Length <= 256, "Invalid household surname.");
            Require(home.HeadId == null || home.Surname != null && home.Residents.Exists(r => r.Id == home.HeadId && r.AgeGroup == ResidentAgeGroup.Adult), "Invalid household head.");
            foreach (var person in home.Residents)
            {
                NpcGuildMembership.Validate(person);
                Require(person.Race >= -1 && person.Race <= 255 && (person.FullName == null || !string.IsNullOrWhiteSpace(person.FullName) && person.FullName.Length <= 256 &&
                    (home.Surname == null || person.FullName.EndsWith(" " + home.Surname, StringComparison.Ordinal))), "Invalid resident identity metadata.");
                Require(person.BedSlot >= -1 && (person.BedSlot < 0 || home.BedCapacity >= 0 && person.BedSlot < home.BedCapacity), "Invalid explicit resident bed.");
            }
            Require(FamilyBedSlots(home).Count == home.Residents.Count, "Household residents share a bed slot.");
            var members = new Dictionary<string, Resident>(StringComparer.Ordinal);
            foreach (var resident in home.Residents) members.Add(resident.Id, resident);
            foreach (var resident in home.Residents)
            {
                Require(resident.AgeGroup == ResidentAgeGroup.Adult || resident.AgeGroup == ResidentAgeGroup.Child, "Invalid age group.");
                Require(resident.Sex == ResidentSex.Male || resident.Sex == ResidentSex.Female, "Invalid sex.");
                if (resident.PartnerId != null)
                {
                    Resident partner;
                    Require(resident.AgeGroup == ResidentAgeGroup.Adult && resident.PartnerId != resident.Id &&
                        members.TryGetValue(resident.PartnerId, out partner) && partner.AgeGroup == ResidentAgeGroup.Adult &&
                        partner.PartnerId == resident.Id, "Invalid partner relationship.");
                }
                if (resident.AgeGroup == ResidentAgeGroup.Child)
                    Require(resident.FatherId != null && resident.MotherId != null, "Missing child parents.");
                ValidateParent(resident, resident.FatherId, ResidentSex.Male, members);
                ValidateParent(resident, resident.MotherId, ResidentSex.Female, members);
                // Deceased parents remain in the household record. No reference depends
                // on a currently spawned actor or a relative still being alive.
                var pending = new Stack<string>(); var visited = new HashSet<string>(StringComparer.Ordinal);
                if (resident.FatherId != null) pending.Push(resident.FatherId);
                if (resident.MotherId != null) pending.Push(resident.MotherId);
                while (pending.Count > 0)
                {
                    string id = pending.Pop();
                    Require(id != resident.Id, "Cyclic ancestry.");
                    if (!visited.Add(id)) continue;
                    Resident ancestor;
                    Require(members.TryGetValue(id, out ancestor), "Unknown ancestor.");
                    if (ancestor.FatherId != null) pending.Push(ancestor.FatherId);
                    if (ancestor.MotherId != null) pending.Push(ancestor.MotherId);
                }
            }
        }
        private static void ValidateParent(Resident child, string parentId, ResidentSex sex, Dictionary<string, Resident> members)
        {
            if (parentId == null) return;
            Resident parent;
            Require(parentId != child.Id && members.TryGetValue(parentId, out parent) &&
                parent.AgeGroup == ResidentAgeGroup.Adult && parent.Sex == sex, "Invalid parent relationship.");
        }
        private static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
    }
}
