using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Called only by the existing disposable HauntingDawnProbe opt-in.
    internal static class NativeHauntingTargetsProbe
    {
        internal static IEnumerator Run(Quest quest)
        {
            var game = GameManager.Instance;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            if (!directory || game.IsPlayerInside) throw new InvalidOperationException("Haunting combat probe needs an exterior town.");
            bool infighting = DaggerfallUnity.Settings.EnemyInfighting;
            var owned = new List<GameObject>();
            Vector3 ground = game.PlayerMotor.transform.position + Vector3.up * 120;
            try
            {
                DaggerfallUnity.Settings.EnemyInfighting = false;
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor);
                floor.name = "Disposable haunting combat floor";
                floor.transform.position = ground - Vector3.up * .1f;
                floor.transform.localScale = new Vector3(16, .2f, 16);
                var navigation = game.StreamingWorld.GetCurrentCityNavigation();
                var residentObject = UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject, directory.transform);
                owned.Add(residentObject);
                var resident = residentObject.AddComponent<ResidentActor>();
                resident.Initialize(new Resident { Id = "haunting-native-victim", FullName = "Haunting native victim", AppearanceSeed = 47 }, 1, navigation);
                resident.Place(ground, Vector3.forward, true); residentObject.SetActive(true);
                var civilian = resident.GetComponent<DaggerfallEntityBehaviour>();
                civilian.Entity.MaxHealth = 2000; civilian.Entity.SetHealth(2000, true);
                var local = directory.transform.InverseTransformPoint(ground);
                var roster = GuardRoster.Create(directory.MapID, 42, 1, new[] { new SavedRoutePoint { X = local.x, Z = local.z } });
                var geometry = new TownGeometry(directory, navigation, false);
                var guard = GuardActor.Create(roster, roster.Guards[0].Id, directory.transform, geometry,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                owned.Add(guard.gameObject); guard.enabled = false;
                guard.gameObject.SetActive(false);
                yield return null;

                foreach (var kind in new[] { MobileTypes.Ghost, MobileTypes.Wraith })
                {
                    // Existing guard combat remains native EnemyAttack; civilian damage
                    // must instead reach the real CivilianEntity through its source API.
                    foreach (bool cityWatch in new[] { false, true })
                    {
                        residentObject.SetActive(!cityWatch); guard.gameObject.SetActive(cityWatch);
                        var victim = cityWatch ? guard.GetComponent<DaggerfallEntityBehaviour>() : civilian;
                        victim.transform.position = ground + Vector3.up;
                        victim.Entity.MaxHealth = 2000; victim.Entity.SetHealth(2000, true);
                        var foe = Spawn(quest, kind, ground + Vector3.back * 1.1f + Vector3.up, directory.transform);
                        owned.Add(foe);
                        var motor = foe.GetComponent<EnemyMotor>();
                        var senses = foe.GetComponent<EnemySenses>();
                        var attack = foe.GetComponent<EnemyAttack>();
                        yield return null;
                        if (!foe.GetComponent<NativeHauntingTargets>())
                            throw new InvalidOperationException("Genuine haunting spawn has no targeting adapter: " + kind);
                        int before = victim.Entity.CurrentHealth;
                        float deadline = Time.realtimeSinceStartup + 25;
                        while (Time.realtimeSinceStartup < deadline && victim.Entity.CurrentHealth == before) yield return null;
                        if (victim.Entity.CurrentHealth >= before)
                            throw new InvalidOperationException("Haunting caused no native damage: " + kind + "; guard=" + cityWatch +
                                "; target=" + (senses.Target ? senses.Target.name : "null") + "; sight=" + senses.TargetInSight +
                                "; distance=" + senses.DistanceToTarget + "; attack=" + attack.enabled + "; hostile=" + motor.IsHostile);
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HAUNTING_DAMAGE_PASS: " + kind + "; guard=" + cityWatch +
                            "; hpBefore=" + before + "; hpAfter=" + victim.Entity.CurrentHealth + "; infighting=false; naturalAnimation=true.");
                        UnityEngine.Object.DestroyImmediate(foe); owned.Remove(foe);
                    }
                }

                guard.gameObject.SetActive(false); residentObject.SetActive(true);
                resident.Place(ground, Vector3.forward, true);
                var blocked = Spawn(quest, MobileTypes.Ghost, ground + Vector3.back * 3 + Vector3.up, directory.transform);
                owned.Add(blocked);
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(wall);
                wall.transform.position = ground + Vector3.back * 1.5f + Vector3.up * 2;
                wall.transform.localScale = new Vector3(8, 5, .3f);
                Physics.SyncTransforms();
                int health = civilian.Entity.CurrentHealth;
                yield return new WaitForSecondsRealtime(2);
                if (blocked.GetComponent<EnemySenses>().Target == civilian || civilian.Entity.CurrentHealth != health)
                    throw new InvalidOperationException("Haunting acquired or damaged a civilian through a wall.");
                UnityEngine.Object.DestroyImmediate(wall); owned.Remove(wall);
                var room = new GameObject("Disposable separate haunting interior"); owned.Add(room);
                room.transform.SetParent(directory.transform); room.AddComponent<DaggerfallInterior>();
                resident.transform.SetParent(room.transform, true);
                yield return new WaitForSecondsRealtime(1);
                if (blocked.GetComponent<EnemySenses>().Target == civilian || civilian.Entity.CurrentHealth != health)
                    throw new InvalidOperationException("Haunting acquired or damaged an indoor civilian.");
                resident.transform.SetParent(directory.transform, true);
                residentObject.SetActive(false);
                UnityEngine.Object.DestroyImmediate(blocked); owned.Remove(blocked);
                var control = GameObjectHelper.CreateFoeGameObjects(ground + Vector3.up, MobileTypes.Ghost, 1, MobileReactions.Hostile)[0];
                owned.Add(control); control.transform.SetParent(directory.transform, true); control.SetActive(true);
                yield return null;
                if (control.GetComponent<NativeHauntingTargets>())
                    throw new InvalidOperationException("Unowned ghost received haunting targeting.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HAUNTING_TARGETS_PASS: Ghost/Wraith damage real civilian/CityWatch; wall/interior/unowned control excluded.");
            }
            finally
            {
                for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) UnityEngine.Object.DestroyImmediate(owned[i]);
                DaggerfallUnity.Settings.EnemyInfighting = infighting;
                Physics.SyncTransforms();
            }
        }

        private static GameObject Spawn(Quest quest, MobileTypes kind, Vector3 position, Transform parent)
        {
            var resource = quest.GetFoe(new Symbol(kind == MobileTypes.Ghost ? "_F.00_" : "_wraith_"));
            if (resource == null) throw new InvalidOperationException("Missing haunting foe resource: " + kind);
            var foe = GameObjectHelper.CreateFoeGameObjects(position, kind, 1, MobileReactions.Hostile, resource)[0];
            foe.transform.SetParent(parent, true); foe.transform.forward = Vector3.forward; foe.SetActive(true);
            return foe;
        }
    }
}
