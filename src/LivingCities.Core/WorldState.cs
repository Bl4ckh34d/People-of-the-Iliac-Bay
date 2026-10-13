using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    [Serializable]
    public sealed class WorldState
    {
        public int Version = 3;
        public uint WorldSeed;
        public List<SettlementPopulation> Settlements = new List<SettlementPopulation>();
        public List<TownRoutineState> Routines = new List<TownRoutineState>();
    }
    public sealed class WorldStateStore
    {
        private WorldState state;
        public uint WorldSeed { get { return state.WorldSeed; } }
        public WorldStateStore(uint seed) { state = new WorldState { WorldSeed = seed }; }
        public SettlementPopulation GetOrCreateFamilies(int settlementId, IEnumerable<HomeProfile> homes, int maxResidents = 5)
        {
            foreach (SettlementPopulation existing in state.Settlements)
                if (existing.SettlementId == settlementId) return existing;
            SettlementPopulation created = Population.CreateFamilies(state.WorldSeed, settlementId, homes, maxResidents);
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
        public void Restore(WorldState incoming) { state = CopyValidated(incoming); }
        public TownRoutineState GetRoutines(int settlementId)
        { return Snapshot().Routines.Find(item => item.SettlementId == settlementId); }
        public void SetRoutines(TownRoutineState routines)
        {
            if (routines == null) throw new ArgumentNullException(nameof(routines));
            var incoming = Snapshot();
            incoming.Routines.RemoveAll(item => item.SettlementId == routines.SettlementId);
            incoming.Routines.Add(routines);
            // Preserve live resident object bindings held by actors when committing routes.
            var validated = CopyValidated(incoming);
            state.Routines = validated.Routines;
        }

        private static WorldState CopyValidated(WorldState source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Require(source.Version >= 1 && source.Version <= 3, "Unsupported save schema.");
            Require(source.Settlements != null, "Missing settlements.");
            var copy = new WorldState { WorldSeed = source.WorldSeed };
            var settlements = new HashSet<int>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (SettlementPopulation town in source.Settlements)
            {
                Require(town != null, "Null settlement.");
                Require(town.SettlementId >= 0 && settlements.Add(town.SettlementId), "Invalid or duplicate settlement.");
                Require(town.Households != null, "Missing households.");
                var townCopy = new SettlementPopulation { SettlementId = town.SettlementId };
                var homes = new HashSet<int>();
                foreach (Household home in town.Households)
                {
                    Require(home != null, "Null household.");
                    Require(home.BuildingKey >= 0 && homes.Add(home.BuildingKey), "Invalid or duplicate household.");
                    Require(home.Residents != null && home.Residents.Count <= (source.Version < 3 ? 3 : 5), "Missing or overcrowded household residents.");
                    var homeCopy = new Household { BuildingKey = home.BuildingKey,
                        SurnameSeed = source.Version < 3 ? StableSeed.Combine(source.WorldSeed, (uint)town.SettlementId,
                            (uint)home.BuildingKey, 0xfffffffe) : home.SurnameSeed };
                    foreach (Resident resident in home.Residents)
                    {
                        Require(resident != null, "Null resident.");
                        Require(!string.IsNullOrWhiteSpace(resident.Id) && identities.Add(resident.Id), "Missing or duplicate resident identity.");
                        Require(resident.HomeKey == home.BuildingKey, "Resident belongs to a different home.");
                        Require(resident.IsAlive || !resident.IsQuestProtected, "Protected resident cannot be dead.");
                        homeCopy.Residents.Add(new Resident { Id = resident.Id, HomeKey = resident.HomeKey,
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
            if (source.Version == 1) return copy;
            Require(source.Routines != null, "Missing routine cache.");
            var cachedTowns = new HashSet<int>();
            foreach (var town in source.Routines)
            {
                Require(town != null && settlements.Contains(town.SettlementId) && cachedTowns.Add(town.SettlementId), "Invalid routine settlement.");
                Require(town.Residents != null, "Missing resident routines.");
                var residents = new Dictionary<string, Resident>(StringComparer.Ordinal);
                foreach (var home in copy.Settlements.Find(item => item.SettlementId == town.SettlementId).Households)
                    foreach (var resident in home.Residents) residents.Add(resident.Id, resident);
                var cachedResidents = new HashSet<string>(StringComparer.Ordinal);
                var townCopy = new TownRoutineState { SettlementId = town.SettlementId };
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
                    var saved = new ResidentRoutine { ResidentId = routine.ResidentId, DestinationKey = routine.DestinationKey,
                        OpenMinute = routine.OpenMinute, CloseMinute = routine.CloseMinute };
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
                copy.Routines.Add(townCopy);
            }
            return copy;
        }
        private static void ValidateFamily(Household home)
        {
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
