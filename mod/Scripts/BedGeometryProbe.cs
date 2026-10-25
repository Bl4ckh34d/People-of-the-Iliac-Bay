using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Runs only after the family fixture in the explicitly requested bed probe.
    public static class BedGeometryProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var interior = game.PlayerEnterExit.Interior;
            if (!interior) throw new InvalidOperationException("Bed probe requires loaded family home.");
            var beds = BedGeometry.Read(interior);
            if (beds.Count == 0) throw new InvalidOperationException("Family fixture contains no actual bed.");
            var adapter = UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var world = (WorldState)adapter.GetSaveData();
            var town = world.Settlements.Find(t => t.SettlementId == game.PlayerGPS.CurrentLocation.MapTableData.MapId);
            var home = town.Households.Find(h => h.BuildingKey == interior.EntryDoor.buildingKey);
            var keys = new List<int>();
            var verifiedModels = new HashSet<uint>();
            foreach (var bed in beds)
            {
                keys.Add(bed.RecordIndex);
                VerifySurface(interior, bed); verifiedModels.Add(bed.ModelId);
            }
            var claims = HomeBeds.Assign(home, keys);
            int living = home.Residents.FindAll(r => r.IsAlive).Count;
            if (claims.Count != living) throw new InvalidOperationException("Compliant family is missing an exclusive bed.");
            var occupied = new HashSet<int>();
            foreach (var claim in claims)
                if (claim.SharingCount != 1 || !occupied.Add(claim.BedRecordIndex))
                    throw new InvalidOperationException("Multiple residents assigned to one bed.");
            string before = Signature(claims);
            // Reload the existing disposable fixture, then reconstruct from new scene objects.
            int save = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "People of the Iliac Bay - family home");
            bool loaded = false;
            SaveLoadManager.OnLoadEventHandler handler = ignored => loaded = true;
            SaveLoadManager.OnLoad += handler;
            try
            {
                game.SaveLoadManager.Load(save); DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 45;
                while (!loaded && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded) throw new InvalidOperationException("Bed fixture reload timed out.");
                yield return new WaitForSecondsRealtime(1);
            }
            finally { SaveLoadManager.OnLoad -= handler; }
            interior = game.PlayerEnterExit.Interior;
            if (!interior) throw new InvalidOperationException("Bed fixture lost interior.");
            keys.Clear(); foreach (var bed in BedGeometry.Read(interior)) keys.Add(bed.RecordIndex);
            world = (WorldState)adapter.GetSaveData();
            home = world.Settlements.Find(t => t.SettlementId == town.SettlementId).Households.Find(h => h.BuildingKey == home.BuildingKey);
            if (Signature(HomeBeds.Assign(home, keys)) != before) throw new InvalidOperationException("Bed assignment changed after real save/load.");
            // Visit real interiors containing the other supported models, rather
            // than inferring their loaded collision surfaces from the first bed.
            for (uint modelId = 41000; modelId <= 41002; modelId++)
            {
                if (verifiedModels.Contains(modelId)) continue;
                game.PlayerEnterExit.TransitionExterior();
                yield return new WaitForSecondsRealtime(1);
                var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                DaggerfallStaticDoors owner = null; StaticDoor selected = default(StaticDoor);
                foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                {
                    if (owner) break;
                    foreach (var door in candidate.Doors)
                    {
                        if (door.doorType != DoorTypes.Building) continue;
                        var block = DaggerfallUnity.Instance.ContentReader.BlockFileReader.GetBlock(door.blockIndex);
                        var records = block.RmbBlock.SubRecords[door.recordIndex].Interior.Block3dObjectRecords;
                        bool found = false;
                        foreach (var record in records) if (record.ModelIdNum == modelId) { found = true; break; }
                        if (found) { owner = candidate; selected = door; break; }
                    }
                }
                if (!owner) throw new InvalidOperationException("Town lacks a real interior fixture for bed " + modelId);
                TownSmokeHarness.EnterInterior(owner.transform, selected);
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                yield return new WaitForSecondsRealtime(1);
                interior = game.PlayerEnterExit.Interior;
                foreach (var bed in BedGeometry.Read(interior))
                    if (bed.ModelId == modelId) { VerifySurface(interior, bed); verifiedModels.Add(modelId); }
                if (!verifiedModels.Contains(modelId)) throw new InvalidOperationException("Real bed fixture did not contain requested model.");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_BEDS_PASS: all three loaded mattress models, exclusive family beds and save/load; familyBeds=" + beds.Count + "; residents=" + living);
        }
        private static void VerifySurface(DaggerfallInterior interior, BedAnchor bed)
        {
            Vector3 center = interior.transform.TransformPoint(bed.LocalCenter);
            // Independent collision evidence, including the mattress below a canopy.
            RaycastHit hit;
            if (!Physics.Raycast(center + Vector3.up * 0.03f, Vector3.down, out hit, 0.06f, ~0, QueryTriggerInteraction.Ignore) ||
                !hit.collider.transform.IsChildOf(interior.transform) || hit.normal.y < 0.99f ||
                Vector3.Distance(hit.point, center) > 0.005f)
                throw new InvalidOperationException("Calculated mattress does not meet the loaded collider: " + bed.ModelId + " at " + center);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_BED_SURFACE: model=" + bed.ModelId + "; building=" + interior.EntryDoor.buildingKey +
                "; record=" + bed.RecordIndex + "; center=" + center + "; hit=" + hit.point + "; size=" + bed.Length + "x" + bed.Width);
        }
        private static string Signature(List<BedAssignment> claims)
        {
            claims.Sort((a, b) => string.CompareOrdinal(a.ResidentId, b.ResidentId));
            string value = "";
            foreach (var claim in claims) value += claim.ResidentId + ":" + claim.BedRecordIndex + ":" + claim.SharingSlot + ":" + claim.SharingCount + "|";
            return value;
        }
    }
}
