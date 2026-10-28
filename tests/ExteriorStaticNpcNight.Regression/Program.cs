using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;
using UnityEngine;

internal static class Program
{
    private static int passed;
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private sealed class Fixture
    {
        public WorldStateStore Store = new WorldStateStore(42);
        public SettlementPopulation Town;
        public BuildingDirectory Directory;
        public PeopleOfTheIliacBayMod Mod;
        public Fixture(int town = 100)
        {
            GameManager.Instance = new GameManager(); QuestMachine.Instance = new QuestMachine();
            DaggerfallUI.UIManager.Open = false;
            Town = Store.GetOrCreate(town, new[] { 1 });
            Directory = new GameObject().AddComponent<BuildingDirectory>(); Directory.MapID = town;
            GameManager.Instance.StreamingWorld.Directory = Directory; GameManager.Instance.PlayerGPS.CurrentMapID = town;
            Mod = new PeopleOfTheIliacBayMod(Store, Town);
        }
        public StaticNPC Npc(int hash = 10)
        {
            var npc = new GameObject().AddComponent<StaticNPC>(); npc.transform.parent = Directory.transform;
            npc.Data = new StaticNPC.NPCData { context = StaticNPC.Context.Custom, hash = hash, billboardArchiveIndex = 182 };
            npc.transform.Position = hash; Directory.People.Add(npc); return npc;
        }
        public NativePerson Person(StaticNPC npc) => Store.RegisterNativePerson(Town.SettlementId, -1, npc.Data.hash,
            "Stationary person", ResidentSex.Male, 0, 0, 182, 0);
        public void At(double hour) { DaggerfallUnity.Instance.WorldTime.Seconds = hour * 3600; Mod.Tick(); }
    }
    private static void Case(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    private static int Main()
    {
        try
        {
            Case("Night hides same body; morning restores exact identity and native state", () => {
                var f = new Fixture(); var npc = f.Npc(); var person = f.Person(npc);
                f.At(21.999); Require(npc.gameObject.activeSelf, "Hidden before sleep");
                f.At(22); Require(!npc.gameObject.activeSelf, "Visible at sleep");
                Require(ReferenceEquals(person, f.Store.NativePersonInSettlement(100, person.Id)), "Identity changed");
                f.At(29.999); Require(!npc.gameObject.activeSelf, "Visible before wake");
                f.At(30); Require(npc.gameObject.activeSelf && npc.transform.Position == 10 && npc.gameObject.Inventory == "original item UID", "Morning changed native body");
            });
            Case("Configured waking hours and arbitrary time jumps", () => {
                var f = new Fixture(); var npc = f.Npc(); f.Mod.Configure(true, 7, 21);
                f.At(21); Require(!npc.gameObject.activeSelf, "Configured sleep ignored");
                f.At(24 * 20 + 7); Require(npc.gameObject.activeSelf, "Skipped days did not restore");
                f.At(24 * 30 + 2); Require(!npc.gameObject.activeSelf, "Skipped night did not hide");
                f.At(12); Require(npc.gameObject.activeSelf, "Backward jump did not restore");
            });
            Case("Unresolved Daggerfall haunting starts at 19; curse release uses normal window", () => {
                var f = new Fixture(1291010263); var npc = f.Npc(); f.At(18.999); Require(npc.gameObject.activeSelf, "Hidden before haunting");
                f.At(19); Require(!npc.gameObject.activeSelf, "Haunting start ignored");
                GameManager.Instance.PlayerEntity.GlobalVars.Lifted = true; f.At(20); Require(npc.gameObject.activeSelf, "Lifted curse not released");
                f.At(22); Require(!npc.gameObject.activeSelf, "Lifted curse removed ordinary sleep");
                f.At(30); Require(npc.gameObject.activeSelf, "Morning not restored");
            });
            Case("Haunting dawn at 05:00 still respects configured waking hour", () => {
                var f = new Fixture(1291010263); var npc = f.Npc(); f.Mod.Configure(true, 4, 22);
                f.At(4.999); Require(!npc.gameObject.activeSelf, "Haunting released before dawn");
                f.At(5); Require(npc.gameObject.activeSelf, "Haunting did not release at 05:00");
                f.Mod.Configure(true, 6, 22); f.At(5); Require(!npc.gameObject.activeSelf, "Haunting dawn bypassed normal wake");
                f.At(6); Require(npc.gameObject.activeSelf, "Normal wake did not release after dawn");
            });
            Case("Ordinary nonzero merchant faction remains subject to night absence", () => {
                var f = new Fixture(); var npc = f.Npc(); npc.Data.factionID = 50; f.Person(npc);
                f.At(23); Require(!npc.gameObject.activeSelf, "Ordinary faction was mistaken for quest ownership");
                f.At(30); Require(npc.gameObject.activeSelf, "Ordinary faction did not restore");
            });
            Case("Open native conversation survives until talk stack closes", () => {
                var f = new Fixture(); var npc = f.Npc(); GameManager.Instance.TalkManager.StaticNPC = npc;
                DaggerfallUI.UIManager.Open = true; f.At(23); Require(npc.gameObject.activeSelf, "Conversation interrupted");
                DaggerfallUI.UIManager.Open = false; f.At(23.1); Require(!npc.gameObject.activeSelf, "Conversation close did not hide");
            });
            Case("Interiors, dungeons, external roots and already inactive bodies stay unchanged", () => {
                var f = new Fixture(); var inside = f.Npc(); inside.Data.context = StaticNPC.Context.Building;
                var dungeon = f.Npc(11); dungeon.Data.context = StaticNPC.Context.Dungeon;
                var foreign = f.Npc(12); foreign.transform.parent = null;
                var inactive = f.Npc(13); inactive.gameObject.SetActive(false);
                f.At(23); f.At(30); Require(inside.gameObject.activeSelf && dungeon.gameObject.activeSelf && foreign.gameObject.activeSelf && !inactive.gameObject.activeSelf, "Foreign activation changed");
            });
            Case("Quest resources, questors, faction listeners and authored people retain ownership", () => {
                var f = new Fixture(); var resource = f.Npc(); resource.gameObject.AddComponent<QuestResourceBehaviour>();
                var action = f.Npc(11); action.gameObject.AddComponent<DaggerfallAction>();
                var questor = f.Npc(12); QuestMachine.Instance.Questors[12] = new Person();
                var listener = f.Npc(13); listener.Data.factionID = 88; QuestMachine.Instance.Listeners.Add(88);
                var authored = f.Npc(14); authored.Data.factionID = 999;
                f.At(23); foreach (var npc in f.Directory.People) Require(npc.gameObject.activeSelf, "Quest/authored actor hidden");
            });
            Case("Households, managed actors and guard roles have one owner", () => {
                var f = new Fixture(); var household = f.Npc(); var person = f.Person(household); person.HomeKey = 1;
                var resident = f.Npc(11); resident.gameObject.AddComponent<ResidentActor>();
                var guard = f.Npc(12); guard.gameObject.AddComponent<GuardActor>();
                var savedGuard = f.Npc(13); var guardPerson = f.Person(savedGuard);
                f.Store.RecordNpcRoleEvidence(guardPerson.Id, NpcSocialRank.Unknown, NpcCombatRole.Guard, NpcServiceBinding.Unknown, null, "native-guard", null);
                f.At(23); foreach (var npc in f.Directory.People) Require(npc.gameObject.activeSelf, "Managed actor hidden");
            });
            Case("A death while hidden never revives at dawn or cleanup", () => {
                var f = new Fixture(); var npc = f.Npc(); var person = f.Person(npc); f.At(23); person.IsAlive = false;
                f.At(30); f.Mod.Restore(); Require(!npc.gameObject.activeSelf, "Dead native actor revived");
            });
            Case("New hidden quest ownership is not forced visible", () => {
                var f = new Fixture(); var npc = f.Npc(); f.At(23);
                npc.gameObject.AddComponent<QuestResourceBehaviour>().TargetResource = new Person { IsHidden = true };
                f.At(30); f.Mod.Restore(); Require(!npc.gameObject.activeSelf, "Hidden quest revived");
            });
            Case("Scene switches release prior bodies and apply current night", () => {
                var f = new Fixture(); var npc = f.Npc(); f.At(23);
                var old = f.Directory; f.Directory = new GameObject().AddComponent<BuildingDirectory>(); f.Directory.MapID = 100;
                GameManager.Instance.StreamingWorld.Directory = f.Directory; var replacement = f.Npc(20); f.At(23.1);
                Require(npc.gameObject.activeSelf && !replacement.gameObject.activeSelf, "Scene ownership leaked");
                old.gameObject.SetActive(false); GameManager.Instance.PlayerEnterExit.IsPlayerInside = true; f.At(24);
                Require(replacement.gameObject.activeSelf, "Interior transition did not release owned activation");
            });
            Case("Settings, restore fault and lifecycle cleanup restore only owned visibility", () => {
                var f = new Fixture(); var npc = f.Npc(); var inactive = f.Npc(11); inactive.gameObject.SetActive(false); f.At(23);
                f.Mod.Configure(false); f.At(23); Require(npc.gameObject.activeSelf && !inactive.gameObject.activeSelf, "Disable visibility leak");
                f.Mod.Configure(true); f.At(23); f.Mod.SetFault(true); f.At(23); Require(npc.gameObject.activeSelf, "Restore fault retained hidden body");
                f.Mod.SetFault(false); f.At(23); f.Mod.Restore(); Require(npc.gameObject.activeSelf, "Lifecycle cleanup retained hidden body");
            });
            Case("Snapshot reload preserves person state; fresh scene derives night and morning", () => {
                var f = new Fixture(); var npc = f.Npc(); var person = f.Person(npc); f.At(23);
                var saved = f.Store.Snapshot(); f.Mod.Restore(); f.Store = new WorldStateStore(99); f.Store.Restore(saved);
                f.Mod = new PeopleOfTheIliacBayMod(f.Store, f.Store.CurrentPopulation(100));
                npc.gameObject.SetActive(false); var loaded = f.Npc(); f.At(23); Require(!loaded.gameObject.activeSelf, "Reload night visible");
                f.At(30); Require(loaded.gameObject.activeSelf && !npc.gameObject.activeSelf && f.Store.NativePersonInSettlement(100, person.Id).Name == person.Name, "Reload identity/visibility changed");
            });
            Case("Context describes outdoor post, absence and actual conversation exception", () => {
                var f = new Fixture(); var npc = f.Npc(); f.Person(npc); f.At(12);
                string day = f.Mod.Context(npc, 12 * 3600); Require(!string.IsNullOrEmpty(day) && day.Contains("06:00") && day.Contains("22:00") && day.Contains("outdoor post"), "Day context lacks routine facts");
                GameManager.Instance.TalkManager.StaticNPC = npc; DaggerfallUI.UIManager.Open = true; f.At(23);
                Require(f.Mod.Context(npc, 23 * 3600).Contains("conversation"), "Night conversation claims ordinary work");
            });
            Console.WriteLine(passed + "/" + passed + " passed"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
