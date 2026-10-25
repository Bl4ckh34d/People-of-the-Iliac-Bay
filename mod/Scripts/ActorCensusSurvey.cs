using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed class ActorCensusReport
    {
        public ActorCensus Census = new ActorCensus();
        public int MapId, VisitedNodes, Frames, ClaimSteps, ReadOnlyChecks;
        public double MaximumSliceMilliseconds;
        public bool Aborted, TraversalTruncated, ClaimsTruncated;
        public string Status = "pending", ProviderState = "unknown";
        public List<string> Providers = new List<string>();
        public List<string> Errors = new List<string>();
    }

    // Explicitly invoked diagnostic. No Awake/Start/Update, component creation, or asset loading.
    public static class ActorCensusSurvey
    {
        public static bool IsRunning { get; private set; }
        public static ActorCensusReport LastReport { get; private set; }
        private const int SliceSteps = 32, MaximumNodes = 100000, MaximumClaimSteps = 100000;
        private const double SliceMilliseconds = 2;
        private sealed class Cursor { public Transform Node; public int Child; public bool Sampled; }

        public static IEnumerator Run(WorldStateStore store, Action<ActorCensusReport> completed = null)
        {
            if (IsRunning) throw new InvalidOperationException("An actor census is already running.");
            if (store == null) throw new ArgumentNullException(nameof(store));
            var game = GameManager.Instance;
            if (!game || game.SaveLoadManager.LoadInProgress) throw new InvalidOperationException("Load must finish before actor census.");
            IsRunning = true;
            var report = new ActorCensusReport { MapId = game.PlayerGPS.CurrentMapID };
            bool loading = false;
            SaveLoadManager.OnStartLoadEventHandler onLoad = data => loading = true;
            SaveLoadManager.OnStartLoad += onLoad;
            string context = Context();
            var watch = new Stopwatch();
            int steps = 0;
            try
            {
                // Providers have asynchronous entry handlers. This is a settling window, not
                // a promise that arbitrary providers have finished; late actors need a new run.
                for (int frame = 0; frame < 30; frame++) { report.Frames++; yield return null; }
                if (loading || Context() != context) { report.Aborted = true; report.Status = "context changed while settling"; yield break; }
                var mods = ModManager.Instance ? ModManager.Instance.GetAllMods() : new Mod[0];
                bool ready = true;
                foreach (var mod in mods)
                    if (mod.Enabled)
                    {
                        ready &= mod.IsReady;
                        report.Providers.Add(mod.LoadPriority + ":" + mod.GUID + ":" + mod.Title + ":ready=" + mod.IsReady);
                    }
                report.ProviderState = ready ? "enabled mods report IsReady; late callbacks still possible" : "one or more enabled mods not ready; incomplete provider coverage";
                var claims = new Dictionary<string, List<ActorCensusClaim>>(StringComparer.Ordinal);
                watch.Start();
                var iterator = store.ReadActorCensusClaims(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).GetEnumerator();
                using (iterator)
                {
                    while (!loading && Context() == context)
                    {
                        bool hasNext;
                        try { hasNext = iterator.MoveNext(); }
                        catch (InvalidOperationException ex) { report.Errors.Add(ex.Message); report.Aborted = true; break; }
                        if (!hasNext) break;
                        report.ClaimSteps++;
                        if (report.ClaimSteps > MaximumClaimSteps) { report.ClaimsTruncated = true; break; }
                        var claim = iterator.Current;
                        if (claim != null && !string.IsNullOrEmpty(claim.Identity))
                        {
                            List<ActorCensusClaim> existing;
                            if (!claims.TryGetValue(claim.Identity, out existing)) { existing = new List<ActorCensusClaim>(); claims.Add(claim.Identity, existing); }
                            existing.Add(claim);
                        }
                        if (++steps >= SliceSteps || watch.Elapsed.TotalMilliseconds >= SliceMilliseconds)
                        { FinishSlice(report, watch); steps = 0; yield return null; watch.Restart(); }
                    }
                }
                if (loading || Context() != context || report.Aborted) { report.Aborted = true; report.Status = "context changed during claim inventory"; yield break; }
                var roots = new List<GameObject>();
                for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
                {
                    var scene = SceneManager.GetSceneAt(sceneIndex);
                    if (!scene.isLoaded) continue;
                    roots.Clear(); scene.GetRootGameObjects(roots);
                    for (int root = 0; root < roots.Count; root++)
                    {
                        if (!roots[root]) continue;
                        var stack = new Stack<Cursor>(); stack.Push(new Cursor { Node = roots[root].transform });
                        while (stack.Count > 0)
                        {
                            if (loading || Context() != context) { report.Aborted = true; report.Status = "context changed during scene inventory"; yield break; }
                            var cursor = stack.Peek();
                            if (!cursor.Node) stack.Pop();
                            else if (!cursor.Sampled)
                            {
                                cursor.Sampled = true; report.VisitedNodes++;
                                if (report.VisitedNodes > MaximumNodes) { report.TraversalTruncated = true; report.Status = "node cap reached"; yield break; }
                                try
                                {
                                    var npc = cursor.Node.GetComponent<StaticNPC>();
                                    var data = npc ? npc.Data : default(StaticNPC.NPCData);
                                    var position = cursor.Node.localPosition; var rotation = cursor.Node.localRotation;
                                    var scale = cursor.Node.localScale; bool active = cursor.Node.gameObject.activeSelf;
                                    uint seed = DFRandom.Seed; var random = UnityEngine.Random.state;
                                    var observation = Read(cursor.Node, report.MapId, mods, claims);
                                    if (!cursor.Node || position != cursor.Node.localPosition || rotation != cursor.Node.localRotation ||
                                        scale != cursor.Node.localScale || active != cursor.Node.gameObject.activeSelf ||
                                        (npc && !data.Equals(npc.Data)) || seed != DFRandom.Seed || !random.Equals(UnityEngine.Random.state))
                                        throw new InvalidOperationException("Census read changed actor or shared random state.");
                                    if (observation != null) { report.ReadOnlyChecks++; report.Census.Observe(observation); }
                                }
                                catch (Exception ex)
                                {
                                    if (report.Errors.Count < 64) report.Errors.Add((cursor.Node ? cursor.Node.name : "destroyed node") + ":" + ex.GetType().Name + ":" + ex.Message);
                                }
                            }
                            else if (cursor.Child < cursor.Node.childCount) stack.Push(new Cursor { Node = cursor.Node.GetChild(cursor.Child++) });
                            else stack.Pop();
                            if (++steps >= SliceSteps || watch.Elapsed.TotalMilliseconds >= SliceMilliseconds)
                            { FinishSlice(report, watch); steps = 0; yield return null; watch.Restart(); }
                        }
                    }
                }
                FinishSlice(report, watch);
                report.Status = "loaded-scene survey complete; hidden/unloaded scenes, batched props, and late provider actors are not certified";
            }
            finally
            {
                SaveLoadManager.OnStartLoad -= onLoad;
                IsRunning = false; LastReport = report;
                if (completed != null) completed(report);
            }
        }
        private static void FinishSlice(ActorCensusReport report, Stopwatch watch)
        { report.MaximumSliceMilliseconds = Math.Max(report.MaximumSliceMilliseconds, watch.Elapsed.TotalMilliseconds); report.Frames++; watch.Stop(); }
        private static string Context()
        {
            var game = GameManager.Instance;
            return game.PlayerGPS.CurrentMapID + ":" + game.PlayerEnterExit.IsPlayerInside + ":" +
                game.PlayerEnterExit.IsPlayerInsideDungeon + ":" + game.PlayerEnterExit.BuildingDiscoveryData.buildingKey + ":" + SceneManager.sceneCount;
        }
        private static ActorCensusObservation Read(Transform node, int map, Mod[] mods, Dictionary<string, List<ActorCensusClaim>> claims)
        {
            var npc = node.GetComponent<StaticNPC>();
            var resident = node.GetComponent<ResidentActor>();
            var guard = node.GetComponent<GuardActor>();
            var mobile = node.GetComponent<MobilePersonNPC>();
            var quest = node.GetComponent<QuestResourceBehaviour>();
            var enemy = node.GetComponent<DaggerfallEnemy>();
            var billboard = node.GetComponent<DaggerfallBillboard>();
            bool decorativeNpc = billboard && billboard.Summary.FlatType == FlatTypes.NPC;
            if (!npc && !resident && !guard && !mobile && !quest && !enemy && !decorativeNpc) return null;
            // Visual-only children do not create extra actors for their known parent.
            if (!npc && !resident && !guard && !mobile && !quest && !enemy &&
                (Ancestor<StaticNPC>(node) || Ancestor<ResidentActor>(node) || Ancestor<GuardActor>(node) ||
                    Ancestor<MobilePersonNPC>(node) || Ancestor<DaggerfallEnemy>(node))) return null;
            var observation = new ActorCensusObservation { Instance = node.gameObject.GetInstanceID().ToString(CultureInfo.InvariantCulture), Active = node.gameObject.activeInHierarchy };
            var interior = Ancestor<DaggerfallInterior>(node);
            observation.Building = interior ? map + ":" + interior.EntryDoor.buildingKey + ":" + interior.BuildingData.BuildingType : "no interior ancestor; exterior/dungeon/unknown";
            observation.Source = "unknown creation provider; " + node.name;
            observation.Quest = quest ? "uid:" + quest.QuestUID + ":symbol:" + (quest.TargetSymbol == null ? "unknown" : quest.TargetSymbol.Original) +
                ":name:" + (quest.TargetQuest == null ? "unknown" : quest.TargetQuest.QuestName) : "no QuestResourceBehaviour on actor";
            bool questIdentity = quest && quest.QuestUID != 0 && quest.TargetSymbol != null;
            if (resident && resident.Resident != null)
            {
                observation.Identity = resident.Resident.Id; observation.PersistentIdentity = true;
                observation.Role = "People of the Iliac Bay resident representation"; observation.Source = "People of the Iliac Bay/ResidentActor";
            }
            else if (guard)
            {
                observation.Identity = guard.GuardId; observation.PersistentIdentity = !string.IsNullOrEmpty(guard.GuardId);
                observation.Role = "People of the Iliac Bay roster guard"; observation.Source = "People of the Iliac Bay/GuardActor";
                observation.Disposition = "already roster-owned; no adoption performed";
            }
            else if (npc)
            {
                var data = npc.Data;
                observation.Signature = data.context + ":" + data.mapID + ":" + data.buildingKey + ":" + data.hash + ":" + data.nameSeed;
                observation.Appearance = "layout:" + data.billboardArchiveIndex + ":" + data.billboardRecordIndex;
                observation.Role = "StaticNPC; faction:" + data.factionID + "; context:" + data.context;
                observation.Source += "; native identity tuple:" + observation.Signature;
                if (data.context == StaticNPC.Context.Building)
                {
                    // Keep the original native identity even when a quest temporarily binds it.
                    observation.Identity = "static:" + observation.Signature; observation.PersistentIdentity = true;
                    ApplyClaims(observation, "npc:" + data.mapID + ":" + data.buildingKey + ":" + data.hash, claims);
                }
                else if (questIdentity) { observation.Identity = "quest:" + quest.QuestUID + ":" + quest.TargetSymbol.Original; observation.PersistentIdentity = true; }
                observation.Service = Service(npc, interior);
                FactionFile.FactionData faction;
                bool individual = GameManager.Instance.PlayerEntity.FactionData.GetFactionData(data.factionID, out faction) && faction.type == (int)FactionFile.FactionTypes.Individual;
                if (quest || QuestMachine.Instance.HasFactionListener(data.factionID)) observation.Disposition += "; excluded: native quest ownership/listener";
                if (individual) observation.Disposition += "; excluded: individual faction";
                if (node.GetComponent<DaggerfallAction>()) observation.Disposition += "; excluded: DaggerfallAction ownership";
                observation.Unknown = "layout override author and visibility controller not exposed by StaticNPC; native hash/nameSeed collision requires review";
            }
            else if (questIdentity)
            { observation.Identity = "quest:" + quest.QuestUID + ":" + quest.TargetSymbol.Original; observation.PersistentIdentity = true; observation.Role = "native quest resource"; }
            else if (enemy && enemy.LoadID != 0)
            { observation.Identity = "enemy:" + enemy.LoadID; observation.PersistentIdentity = true; observation.Role = "native enemy representation"; }
            else observation.Role = mobile ? "unowned MobilePersonNPC" : decorativeNpc ? "NPC-type flat without actor identity" : "unidentified native actor";
            if (quest) observation.Disposition += "; protected quest resource; no adoption performed";
            if (billboard) observation.Appearance += "; rendered:" + billboard.Summary.Archive + ":" + billboard.Summary.Record;
            if (mobile) observation.Appearance += "; mobile race:" + mobile.Race + ":gender:" + mobile.Gender + ":outfit:" + mobile.PersonOutfitVariant + ":face:" + mobile.PersonFaceRecordId;
            observation.AppearanceProvider = AppearanceProvider(node, mods);
            if (observation.Identity != null) ApplyClaims(observation, observation.Identity, claims);
            if (!observation.PersistentIdentity) observation.Unknown += "; no verified persistent key: session instance only; do not compare across reload";
            return observation;
        }
        private static void ApplyClaims(ActorCensusObservation observation, string identity, Dictionary<string, List<ActorCensusClaim>> claims)
        {
            List<ActorCensusClaim> found;
            if (!claims.TryGetValue(identity, out found)) return;
            var homes = new SortedSet<string>(StringComparer.Ordinal); var beds = new SortedSet<string>(StringComparer.Ordinal); var kinds = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var claim in found) { if (claim.Home != "unknown") homes.Add(claim.Home); if (claim.Bed != "unknown") beds.Add(claim.Bed); kinds.Add(claim.Kind); }
            if (homes.Count > 0) observation.Home = string.Join(" | ", homes);
            if (beds.Count > 0) observation.Bed = string.Join(" | ", beds);
            observation.Disposition += "; existing ledger: " + string.Join(" | ", kinds);
        }
        private static string Service(StaticNPC npc, DaggerfallInterior interior)
        {
            int factionId = npc.Data.factionID;
            if (npc.GetComponent<QuestResourceBehaviour>() || QuestMachine.Instance.HasFactionListener(factionId)) return "quest has native click precedence; service eligibility not evaluated";
            if (npc.IsChildNPC) return "child; no keeper service";
            if (Services.HasGuildService(factionId)) return "registered guild service; building/guild access conditions still apply";
            FactionFile.FactionData faction;
            if (!GameManager.Instance.PlayerEntity.FactionData.GetFactionData(factionId, out faction)) return "unknown faction data";
            if (faction.sgroup == (int)FactionFile.SocialGroups.Merchants)
            {
                if (Services.HasCustomMerchantService(factionId)) return "registered custom merchant service";
                if (!interior) return "merchant faction; no verified interior service context";
                var type = interior.BuildingData.BuildingType;
                if (RMBLayout.IsRepairShop(type)) return "native repair";
                if (RMBLayout.IsShop(type)) return "native selling";
                if (type == DFLocation.BuildingTypes.Bank) return "native banking";
                if (type == DFLocation.BuildingTypes.Tavern) return "native tavern rental/services";
            }
            if (faction.type == (int)FactionFile.FactionTypes.WitchesCoven) return "native coven service";
            return "no service found in native faction/building routing";
        }
        private static string AppearanceProvider(Transform node, Mod[] mods)
        {
            var renderer = node.GetComponent<Renderer>();
            var material = renderer ? renderer.sharedMaterial : null;
            var texture = material ? material.mainTexture : null;
            if (!texture) return "unknown; no texture on actor renderer (child/replacement renderers not attributed)";
            string result = "unknown actual provider; runtime texture:" + texture.name;
            // HasAsset inspects bundle membership only. Never loads assets or assumes membership
            // proves ownership: loose files, cached variants and custom renderers can override it.
            foreach (var mod in mods)
                if (mod.Enabled && mod.HasAsset(texture.name)) result += "; asset-name candidate:" + mod.GUID + ":" + mod.Title;
            return result;
        }
        // The installed Unity API lacks GetComponentInParent(includeInactive). Inspecting
        // each ancestor directly also retains the context of intentionally hidden actors.
        private static T Ancestor<T>(Transform node) where T : Component
        {
            while (node)
            {
                var component = node.GetComponent<T>();
                if (component) return component;
                node = node.parent;
            }
            return null;
        }
    }
}
