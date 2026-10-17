using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    [Serializable]
    public sealed class WorldState
    {
        public int Version = 22;
        public uint WorldSeed;
        public List<ConversationAttitude> ConversationAttitudes = new List<ConversationAttitude>();
        public List<NpcConversationMemory> ConversationMemories = new List<NpcConversationMemory>();
        public HauntingRoutineDay HauntingDay;
        public List<SettlementPopulation> Settlements = new List<SettlementPopulation>();
        public List<TownRoutineState> Routines = new List<TownRoutineState>();
        public List<QuestResidentBinding> QuestBindings = new List<QuestResidentBinding>();
        public List<CivilianIncident> AlarmIncidents = new List<CivilianIncident>();
        public List<GuardRoster> Garrisons = new List<GuardRoster>();
        public List<EntryReport> EntryReports = new List<EntryReport>();
        public List<NativePerson> NativePeople = new List<NativePerson>();
        public List<DefenseCombatant> DefenseCombatants = new List<DefenseCombatant>();
        public List<TavernLodging> Lodgings = new List<TavernLodging>();
        public List<TravelerPerson> Travelers = new List<TravelerPerson>();
        public List<TravelerVisit> TravelerVisits = new List<TravelerVisit>();
        public List<VisitDeparture> VisitDepartures = new List<VisitDeparture>();
        public List<ObservedVisit> ObservedVisits = new List<ObservedVisit>();
        public List<ShelterLock> ShelterLocks = new List<ShelterLock>();
        public List<ResidentTrip> ResidentTrips = new List<ResidentTrip>();
        public List<RetiredHousehold> HouseholdHistory = new List<RetiredHousehold>();
        public List<HouseholdRecoveryReservation> HouseholdRecoveries = new List<HouseholdRecoveryReservation>();
        public List<DialogueComplaintRecord> DialogueComplaints = new List<DialogueComplaintRecord>();
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
        public uint WorldSeed { get { return state.WorldSeed; } }
        public WorldStateStore(uint seed) { state = new WorldState { WorldSeed = seed }; }
        public bool HasSettlement(int settlementId) { return state.Settlements.Exists(s=>s.SettlementId==settlementId); }
        public Household ReserveHousehold(int settlementId,HomeProfile home)
        {
            if(home==null || home.BedCount<=0) throw new ArgumentException("A reserved household requires surveyed beds.");
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
            Require(retired.Residents.Count > 0 && retired.Residents.TrueForAll(r => !r.IsAlive),
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
                var liveHome=state.Settlements[townIndex].Households.Find(h=>h.HeadId==native.Id);
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
            if (loadedGameTime.HasValue && incoming.Version < 22) MigrateClockRate(candidate, loadedGameTime.Value);
            candidate = CopyValidated(candidate);
            state = candidate;
            IndexResidentTrips();
            IndexObservedVisits();
            TravelerVisitRevision++;
            bindingEpoch = new object();
        }
        public TownRoutineState GetRoutines(int settlementId)
        {
            Require(state.Version >= 1 && state.Version <= 22, "Unsupported save schema.");
            if (state.Version == 1) return null;
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
                        DestinationKey = routine.DestinationKey, OpenMinute = routine.OpenMinute,
                        CloseMinute = routine.CloseMinute, ItineraryDepartureSeconds = routine.ItineraryDepartureSeconds };
                    if (routine.Itinerary != null)
                        result.Itinerary = ResidentVisitJourney.Copy(routine.Itinerary);
                    return result;
                }
            }
            return null;
        }
        public void SetRoutines(TownRoutineState routines)
        {
            if (routines == null) throw new ArgumentNullException(nameof(routines));
            Require(state.Version >= 1 && state.Version <= 22, "Unsupported save schema.");
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
            // Replacing only the routine list preserves live resident bindings and every other town.
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
            int itineraryVersion = source.Version >= 18 ? town.ItineraryVersion : 0;
            Require(itineraryVersion >= 0 && itineraryVersion <= 1, "Unsupported routine itinerary version.");
            var townCopy = new TownRoutineState { SettlementId = town.SettlementId, ItineraryVersion = itineraryVersion };
            foreach (var routine in town.Residents)
            {
                Require(routine != null && routine.ResidentId != null && residents.ContainsKey(routine.ResidentId) &&
                    cachedResidents.Add(routine.ResidentId), "Invalid or duplicate resident routine.");
                Require(routine.Route != null && routine.Route.Count <= 10000, "Invalid route points.");
                if (routine.DestinationKey == -1)
                    Require(routine.Route.Count == 0 && routine.OpenMinute == 0 && routine.CloseMinute == 0, "Invalid home routine.");
                else
                    Require(residents[routine.ResidentId].AgeGroup == ResidentAgeGroup.Adult &&
                        routine.DestinationKey >= 0 && routine.DestinationKey != residents[routine.ResidentId].HomeKey &&
                        routine.OpenMinute >= 0 && routine.OpenMinute < routine.CloseMinute && routine.CloseMinute <= 1440 &&
                        routine.Route.Count >= 2, "Invalid visit routine.");
                ResidentDestination itinerary = null;
                double departureSeconds = 0;
                if (source.Version >= 18 && routine.Itinerary != null)
                {
                    var resident = residents[routine.ResidentId];
                    Require(itineraryVersion == 1 && resident.AgeGroup == ResidentAgeGroup.Adult,
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
                else if (source.Version >= 18)
                    Require(routine.ItineraryDepartureSeconds == 0,
                        "Missing itinerary for saved departure time.");
                Require(itineraryVersion == 1 || itinerary == null,
                    "Routine itinerary version is missing.");
                var saved = new ResidentRoutine { ResidentId = routine.ResidentId, DestinationKey = routine.DestinationKey,
                    OpenMinute = routine.OpenMinute, CloseMinute = routine.CloseMinute, Itinerary = itinerary,
                    ItineraryDepartureSeconds = itinerary == null ? 0 : departureSeconds };
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
            Require(source.Version >= 1 && source.Version <= 22, "Unsupported save schema.");
            Require(source.Settlements != null, "Missing settlements.");
            var copy = new WorldState { WorldSeed = source.WorldSeed };
            copy.ConversationAttitudes = CopyConversationAttitudes(source);
            copy.ConversationMemories = CopyConversationMemories(source);
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
                    int bedCapacity = source.Version < 5 ? -1 : home.BedCapacity;
                    Require(home.Generation >= 0 && bedCapacity >= -1 && (home.Generation == 0 || bedCapacity > 0),
                        "Invalid saved household generation or bed capacity.");
                    Require(home.Residents != null && home.Residents.Count <=
                        (source.Version < 3 ? 3 : bedCapacity >= 0 ? bedCapacity : 5), "Missing or overcrowded household residents.");
                    var homeCopy = new Household { BuildingKey = home.BuildingKey,
                        Generation = source.Version >= 20 ? home.Generation : 0,
                        HeadId = home.HeadId, Surname = home.Surname,
                        BedCapacity = bedCapacity,
                        SurnameSeed = source.Version < 3 ? StableSeed.Combine(source.WorldSeed, (uint)town.SettlementId,
                            (uint)home.BuildingKey, 0xfffffffe) : home.SurnameSeed };
                    foreach (Resident resident in home.Residents)
                    {
                        Require(resident != null, "Null resident.");
                        Require(!string.IsNullOrWhiteSpace(resident.Id) && identities.Add(resident.Id), "Missing or duplicate resident identity.");
                        Require(resident.HomeKey == home.BuildingKey, "Resident belongs to a different home.");
                        Require(resident.IsAlive || !resident.IsQuestProtected, "Protected resident cannot be dead.");
                        homeCopy.Residents.Add(new Resident { Id = resident.Id, HomeKey = resident.HomeKey,
                            FullName = resident.FullName, Race = resident.Race, BedSlot = resident.BedSlot,
                            AppearanceSeed = resident.AppearanceSeed, IsAlive = resident.IsAlive, IsQuestProtected = resident.IsQuestProtected,
                            AgeGroup = source.Version < 3 ? ResidentAgeGroup.Adult : resident.AgeGroup,
                            Sex = source.Version < 3 ? ((resident.AppearanceSeed & 1) == 0 ? ResidentSex.Male : ResidentSex.Female) : resident.Sex,
                            PartnerId = source.Version < 3 ? null : resident.PartnerId,
                            MotherId = source.Version < 3 ? null : resident.MotherId,
                            FatherId = source.Version < 3 ? null : resident.FatherId });
                    }
                    ValidateFamily(homeCopy);
                    townCopy.Households.Add(homeCopy);
                }
                copy.Settlements.Add(townCopy);
            }
            if (source.Version >= 20)
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
                    Require(archived.Residents.Count > 0 && archived.Residents.TrueForAll(r => !r.IsAlive),
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
            if (source.Version == 1) return copy;
            Require(source.Routines != null, "Missing routine cache.");
            var cachedTowns = new HashSet<int>();
            foreach (var town in source.Routines)
            {
                Require(town != null && settlements.Contains(town.SettlementId) && cachedTowns.Add(town.SettlementId), "Invalid routine settlement.");
                var settlement = copy.Settlements.Find(item => item.SettlementId == town.SettlementId);
                copy.Routines.Add(CopyValidatedRoutineTown(source, town, settlement));
            }
            if (source.Version >= 4)
            {
                copy.QuestBindings = source.QuestBindings;
                copy.QuestBindings = CopyQuestBindings(copy);
            }
            if (source.Version >= 5) copy.AlarmIncidents = CopyAlarms(source);
            if (source.Version >= 6)
            {
                Require(source.Garrisons != null, "Missing garrison ledger.");
                var garrisons = new HashSet<int>();
                foreach (var garrison in source.Garrisons)
                {
                    Require(garrison != null && settlements.Contains(garrison.SettlementId) && garrisons.Add(garrison.SettlementId), "Unknown or duplicate garrison settlement.");
                    copy.Garrisons.Add(GuardRoster.CopyValidated(garrison));
                }
            }
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
            copy.DefenseCombatants = CopyDefense(source,copy);
            copy.Lodgings = CopyLodgings(source,copy);
            CopyTravelers(source,copy,identities);
            copy.VisitDepartures=CopyVisitDepartures(source);
            if(source.Version>=19) copy.ObservedVisits=CopyObservedVisits(source);
            copy.ShelterLocks=CopyShelterLocks(source);
            copy.ResidentTrips=CopyResidentTrips(source,copy);
            copy.HouseholdRecoveries=CopyHouseholdRecoveries(source,copy);
            if (source.Version >= 21) copy.DialogueComplaints=CopyDialogueComplaints(source);
            foreach (var native in copy.NativePeople)
            {
                var home = copy.Settlements.Find(t => t.SettlementId == native.SettlementId).Households.Find(h => h.HeadId == native.Id);
                if (home != null) home.Residents.Find(r => r.Id == native.Id).NativeIdentity = native;
            }
            return copy;
        }
        private static void MigrateClockRate(WorldState world, double now)
        {
            Require(Finite(now) && now >= 0, "Invalid game time for clock migration.");
            foreach (var alarm in world.AlarmIncidents)
                foreach (var person in alarm.People)
                    if (person.Escape != null)
                    {
                        person.Escape.MigrateClockScale(now, 12, 2);
                        if (person.EscapeAdvancedAt > 0)
                            person.EscapeAdvancedAt = Math.Max(person.EscapeAdvancedAt,
                                Math.Max(person.Escape.StartedAt, person.Escape.RedirectedAt));
                    }
            foreach (var report in world.EntryReports)
                if (report.Messenger != null) report.Messenger.MigrateClockScale(now, 12, 2);
            foreach (var departure in world.VisitDepartures)
                departure.Motion.MigrateClockScale(now, 12, 2);
            foreach (var visit in world.ObservedVisits)
                visit.MigrateClockRate(now, 12, 2);
            foreach (var roster in world.Garrisons)
            {
                GuardRoster.MigrateInteriorClock(roster, now, 12, 2);
                GuardRoster.MigrateTravelClock(roster, now, 12, 2);
                GuardRoster.MigratePatrolAndRefugeClock(roster, now, 12, 2);
            }
        }
        private static string HistoryKey(int settlementId, int buildingKey, int generation)
        { return settlementId + ":" + buildingKey + ":" + generation; }
        private static Household CopyHousehold(WorldState source, int settlementId, Household home, HashSet<string> identities)
        {
            Require(home != null && home.BuildingKey >= 0 && home.Generation >= 0, "Invalid household generation.");
            int bedCapacity = source.Version < 5 ? -1 : home.BedCapacity;
            Require(bedCapacity >= -1 && (home.Generation == 0 || bedCapacity > 0) && home.Residents != null && home.Residents.Count <=
                (source.Version < 3 ? 3 : bedCapacity >= 0 ? bedCapacity : 5), "Missing or overcrowded household residents.");
            var result = new Household { BuildingKey = home.BuildingKey, Generation = source.Version >= 20 ? home.Generation : 0,
                HeadId = home.HeadId, Surname = home.Surname, BedCapacity = bedCapacity,
                SurnameSeed = source.Version < 3 ? StableSeed.Combine(source.WorldSeed, (uint)settlementId,
                    (uint)home.BuildingKey, 0xfffffffe) : home.SurnameSeed };
            foreach (var resident in home.Residents)
            {
                Require(resident != null && !string.IsNullOrWhiteSpace(resident.Id) && identities.Add(resident.Id) &&
                    resident.HomeKey == home.BuildingKey && (resident.IsAlive || !resident.IsQuestProtected),
                    "Invalid or duplicate resident identity.");
                result.Residents.Add(new Resident { Id = resident.Id, HomeKey = resident.HomeKey, FullName = resident.FullName,
                    Race = resident.Race, BedSlot = resident.BedSlot, AppearanceSeed = resident.AppearanceSeed,
                    IsAlive = resident.IsAlive, IsQuestProtected = resident.IsQuestProtected,
                    AgeGroup = source.Version < 3 ? ResidentAgeGroup.Adult : resident.AgeGroup,
                    Sex = source.Version < 3 ? ((resident.AppearanceSeed & 1) == 0 ? ResidentSex.Male : ResidentSex.Female) : resident.Sex,
                    PartnerId = source.Version < 3 ? null : resident.PartnerId,
                    MotherId = source.Version < 3 ? null : resident.MotherId,
                    FatherId = source.Version < 3 ? null : resident.FatherId });
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
