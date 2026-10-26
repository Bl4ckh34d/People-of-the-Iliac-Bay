using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Opt-in only. The shop cases execute actual DoSteal and installed Harmony
        // hooks. Private cases stage a house-owned container in this shop and prove
        // the transfer adapter, not ordinary private-home ownership/consent gameplay.
        internal IEnumerator VerifyTheftMemories()
        {
            var game = GameManager.Instance;
            if (!game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerEnterExit.Interior ||
                game.PlayerEnterExit.BuildingDiscoveryData.buildingKey != 131343)
                throw new InvalidOperationException("Theft memory probe requires the native Daggerfall shop 131343.");
            SurveyStationaryObservers();
            StationaryParticipant stationary = null; ResidentActor mobile = null;
            foreach (var entry in stationaryParticipants.Values)
                if (StaticObserverAwake(entry.Npc, entry.Person)) { stationary = entry; break; }
            foreach (var actor in actors.Values)
                if (actor && actor.Resident != null && actor.Resident.IsAlive && !actor.IsSleeping) { mobile = actor; break; }
            if (stationary == null || !mobile)
                throw new InvalidOperationException("Theft memory probe requires an ordinary stationary person and resident body.");
            foreach (string kind in new[] { "stationary", "mobile" })
            {
                foreach (string condition in new[] { "visible", "behind", "range", "invisible", "sleep", "inactive", "wall" })
                    yield return VerifyTheftTransfer(stationary, mobile, kind == "mobile", condition, false);
                yield return VerifyTheftTransfer(stationary, mobile, kind == "mobile", "visible", true);
                yield return VerifyTheftTransfer(stationary, mobile, kind == "mobile", "sleep", true);
                yield return VerifyTheftTransfer(stationary, mobile, kind == "mobile", "source-behind", true);
                yield return VerifyTheftTransfer(stationary, mobile, kind == "mobile", "wall", true);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_THEFT_MEMORY_PASS: cases=22; realShopDoSteal=true; installedHooks=true; mobileAndStationary=true; privateContainerCollider=true; privateContainerStagedInShop=true; privateHomeAcceptance=false");
        }

        private IEnumerator VerifyTheftTransfer(StationaryParticipant stationary, ResidentActor mobile,
            bool useMobile, string condition, bool privateTransfer)
        {
            var game = GameManager.Instance; var player = game.PlayerEntity;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var original = worldStore; var baseline = original.Snapshot();
            var body = useMobile ? mobile.gameObject : stationary.Npc.gameObject;
            string id = useMobile ? mobile.Resident.Id : stationary.Person.Id;
            baseline.SceneMemories.RemoveAll(row => row.ObserverId == id);
            var scratch = new WorldStateStore(1); scratch.Restore(baseline);
            var item = ItemBuilder.CreateWeapon(Weapons.Dagger, WeaponMaterialTypes.Iron);
            string itemName = item.LongName;
            var oldParent = body.transform.parent; var oldPosition = body.transform.position; var oldRotation = body.transform.rotation;
            bool oldActive = body.activeSelf, stationaryActive = stationary.Npc.gameObject.activeSelf;
            var playerPosition = game.PlayerMotor.transform.position; var concealment = player.MagicalConcealmentFlags;
            var priorCrime = player.CrimeCommitted; var random = UnityEngine.Random.state;
            var skillUses = (short[])player.SkillUses.Clone();
            var tallyField = typeof(PlayerEntity).GetField("thievesGuildRequirementTally", flags);
            var letterField = typeof(PlayerEntity).GetField("timeForThievesGuildLetter", flags);
            object tally = tallyField.GetValue(player), letter = letterField.GetValue(player);
            var sleepField = typeof(ResidentActor).GetField("sleeping", flags);
            var oldSleepReference = useMobile ? sleepField.GetValue(mobile) : null;
            SleepingPose sleep = body.GetComponent<SleepingPose>(); bool addedSleep = !sleep, oldSleep = sleep && sleep.Active;
            GameObject wall = null, container = null;
            DaggerfallInventoryWindow inventory = null; DaggerfallTradeWindow trade = null;
            bool bound = false;
            try
            {
                if (privateTransfer)
                {
                    container = new GameObject("Owned staged theft-memory container");
                    container.transform.SetParent(game.PlayerEnterExit.Interior.transform, false);
                    var loot = container.AddComponent<DaggerfallLoot>(); loot.houseOwned = true; loot.Items.AddItem(item);
                    container.AddComponent<BoxCollider>().size = new Vector3(0.5f, 0.5f, 0.5f);
                    inventory = new DaggerfallInventoryWindow(DaggerfallUI.UIManager) { LootTarget = loot };
                    DaggerfallUI.UIManager.PushWindow(inventory);
                }
                else
                {
                    trade = new DaggerfallTradeWindow(DaggerfallUI.UIManager, null, DaggerfallTradeWindow.WindowModes.Buy);
                    DaggerfallUI.UIManager.PushWindow(trade);
                }
                yield return null;
                // No yields from controlled body/store changes through restoration.
                worldStore = scratch;
                var pose = stationary.Npc.GetComponent<DirectionalKeeperPose>();
                Vector3 heading = pose ? pose.Heading : stationary.Npc.transform.forward; heading.y = 0; heading.Normalize();
                if (useMobile)
                {
                    body.transform.SetParent(game.PlayerEnterExit.Interior.transform, true);
                    body.transform.position = stationary.Npc.transform.position;
                    body.transform.rotation = Quaternion.LookRotation(heading);
                    stationary.Npc.gameObject.SetActive(false);
                }
                body.SetActive(true);
                NativeNpcSceneObserver observer;
                if (useMobile) { NativeNpcSceneObserver.BindTo(body, scratch); observer = body.GetComponent<NativeNpcSceneObserver>(); }
                else { observer = NativeNpcSceneObserver.BindTo(stationary.Npc, stationary.Person, scratch, this); stationary.Observer = observer; }
                bound = true;
                if (!sleep) sleep = body.AddComponent<SleepingPose>();
                if (useMobile) sleepField.SetValue(mobile, sleep);
                typeof(SleepingPose).GetProperty("Active").SetValue(sleep, false, null);
                var observerPose = body.GetComponent<DirectionalKeeperPose>();
                heading = observerPose ? observerPose.Heading : body.transform.forward; heading.y = 0; heading.Normalize();
                Vector3 front = body.transform.position + heading * 3;
                game.PlayerMotor.transform.position = front; player.MagicalConcealmentFlags = MagicalConcealmentFlags.None;
                // Keep the real source collider beside the player sight line; its
                // own target ray may ignore it, but the player ray must not.
                if (container) container.transform.position = body.transform.position + heading * 2.5f + Vector3.Cross(heading, Vector3.up) * 0.75f;
                Physics.SyncTransforms();
                if (!observer.Sees(front, 20) || container && !observer.Sees(container.transform.position, 20, container.transform))
                    throw new InvalidOperationException("Theft fixture has no clear native initial view: " + id);
                if (condition == "behind") game.PlayerMotor.transform.position = body.transform.position - heading * 3;
                if (condition == "range") game.PlayerMotor.transform.position = body.transform.position + heading * 30;
                if (condition == "source-behind" && container) container.transform.position = body.transform.position - heading * 3;
                if (condition == "invisible") player.MagicalConcealmentFlags = MagicalConcealmentFlags.InvisibleNormal;
                if (condition == "sleep") typeof(SleepingPose).GetProperty("Active").SetValue(sleep, true, null);
                if (condition == "inactive") body.SetActive(false);
                if (condition == "wall")
                {
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Owned theft-memory occluder";
                    wall.transform.position = (body.transform.position + front) * 0.5f;
                    wall.transform.localScale = new Vector3(1.2f, 3, 1.2f);
                }
                Physics.SyncTransforms();
                if (privateTransfer)
                {
                    typeof(DaggerfallInventoryWindow).GetMethod("DoTransferItem", flags).Invoke(inventory,
                        new object[] { item, inventory.LootTarget.Items, player.Items, false });
                    if (inventory.LootTarget.Items.Contains(item)) throw new InvalidOperationException("Native private transfer retained its source item.");
                }
                else
                {
                    var basket = (ItemCollection)typeof(DaggerfallTradeWindow).GetField("basketItems", flags).GetValue(trade);
                    basket.AddItem(item); typeof(DaggerfallTradeWindow).GetField("cost", flags).SetValue(trade, 1);
                    // Native detection is clamped to 95. Choose a success draw, then
                    // restore its seed so DoSteal executes the real native roll.
                    int seed = 0;
                    for (; seed < 10000; seed++) { UnityEngine.Random.InitState(seed); if (UnityEngine.Random.Range(0, 100) >= 95) break; }
                    if (seed == 10000) throw new InvalidOperationException("No deterministic native theft success seed.");
                    UnityEngine.Random.InitState(seed);
                    typeof(DaggerfallTradeWindow).GetMethod("DoSteal", flags).Invoke(trade, null);
                    if (basket.Contains(item) || !scratch.IsKnownStolenPlayerItem(item.UID))
                        throw new InvalidOperationException("Native shop transfer or exact stolen-item provenance missing.");
                }
                if (!player.Items.Contains(item) || player.CrimeCommitted != priorCrime)
                    throw new InvalidOperationException("Native theft transfer failed or deterministic success unexpectedly raised crime.");
                var memories = scratch.Snapshot().SceneMemories.FindAll(row => row.ObserverId == id && row.Kind == NpcSceneEventKind.Theft);
                int expected = condition == "visible" ? 1 : 0;
                if (memories.Count != expected) throw new InvalidOperationException("Theft memory count: " + (useMobile ? "mobile" : "stationary") +
                    "/" + condition + "; private=" + privateTransfer + "; expected=" + expected + "; actual=" + memories.Count);
                if (expected == 1)
                {
                    var memory = memories[0];
                    if (memory.ResponsibleId != NpcDirectedRelationship.PlayerTargetId || memory.SubjectId != null ||
                        memory.BuildingKey != game.PlayerEnterExit.BuildingDiscoveryData.buildingKey || memory.Valence >= 0 ||
                        !memory.Observation.Contains(itemName) || !memory.Observation.Contains("do not know the motive") || memory.ExpiresAt <= memory.At)
                        throw new InvalidOperationException("Theft memory invented ownership or lost native item/location/appraisal facts.");
                }
                // Waking and restored visibility alone have no transfer to publish.
                typeof(SleepingPose).GetProperty("Active").SetValue(sleep, false, null); body.SetActive(true);
                player.MagicalConcealmentFlags = MagicalConcealmentFlags.None; game.PlayerMotor.transform.position = front;
                if (wall) wall.SetActive(false); Physics.SyncTransforms();
                typeof(NativeNpcSceneObserver).GetMethod("Update", flags).Invoke(observer, null);
                if (scratch.Snapshot().SceneMemories.FindAll(row => row.ObserverId == id && row.Kind == NpcSceneEventKind.Theft).Count != expected)
                    throw new InvalidOperationException("Waking or visibility fabricated a repeated theft.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_THEFT_MEMORY_CASE_PASS: kind=" + (useMobile ? "mobile" : "stationary") +
                    "; condition=" + condition + "; nativeTransfer=true; privateStaged=" + privateTransfer + "; memories=" + expected);
            }
            finally
            {
                worldStore = original;
                if (sleep) typeof(SleepingPose).GetProperty("Active").SetValue(sleep, oldSleep, null);
                if (useMobile) sleepField.SetValue(mobile, oldSleepReference);
                if (addedSleep && sleep) DestroyImmediate(sleep);
                if (wall) DestroyImmediate(wall);
                player.Items.RemoveItem(item);
                if (inventory != null && inventory.LootTarget) inventory.LootTarget.Items.RemoveItem(item);
                DaggerfallUI.Instance.PopToHUD();
                if (container) DestroyImmediate(container);
                body.transform.SetParent(oldParent, true); body.transform.position = oldPosition; body.transform.rotation = oldRotation;
                body.SetActive(oldActive); stationary.Npc.gameObject.SetActive(stationaryActive);
                player.MagicalConcealmentFlags = concealment; player.CrimeCommitted = priorCrime; player.SkillUses = skillUses;
                tallyField.SetValue(player, tally); letterField.SetValue(player, letter); UnityEngine.Random.state = random;
                game.PlayerMotor.transform.position = playerPosition; Physics.SyncTransforms();
                if (bound)
                {
                    if (useMobile) NativeNpcSceneObserver.BindTo(body, original);
                    else stationary.Observer = NativeNpcSceneObserver.BindTo(stationary.Npc, stationary.Person, original, this);
                }
            }
        }
    }
}
