using System;
using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public sealed class ShopVisitGeometry
    {
        private readonly DaggerfallInterior room;
        private readonly InteriorGeometry geometry;
        private readonly List<Vector3> shelves=new List<Vector3>();
        private Vector3? counter;
        private readonly Dictionary<string,List<Vector3>> reserved=new Dictionary<string,List<Vector3>>();
        public ShopVisitGeometry(DaggerfallInterior room,InteriorGeometry geometry)
        {
            this.room=room; this.geometry=geometry;
            foreach(var loot in room.GetComponentsInChildren<DaggerfallLoot>())
                if(loot.ContainerType==LootContainerTypes.ShopShelves) shelves.Add(room.transform.InverseTransformPoint(loot.transform.position));
            shelves.Sort((a,b)=>{ int x=a.x.CompareTo(b.x); return x!=0 ? x : a.z.CompareTo(b.z); });
            foreach(var npc in room.GetComponentsInChildren<StaticNPC>())
            {
                FactionFile.FactionData faction;
                if(GameManager.Instance.PlayerEntity.FactionData.GetFactionData(npc.Data.factionID,out faction) &&
                    faction.sgroup==(int)FactionFile.SocialGroups.Merchants)
                { counter=room.transform.InverseTransformPoint(npc.transform.position); break; }
            }
        }
        public Vector3 Face(Vector3 position,ShopVisitPhase phase)
        {
            if(phase==ShopVisitPhase.Haggling && counter.HasValue) return counter.Value-position;
            Vector3 target=position; float best=float.MaxValue;
            foreach(var shelf in shelves) { float d=(shelf-position).sqrMagnitude; if(d<best) { best=d; target=shelf; } }
            return target-position;
        }
        public void ResetReservations() { reserved.Clear(); }
        public ShopVisitJourney Create(string id,uint seed,double start,double end)
        {
            if(shelves.Count==0 || !counter.HasValue || !geometry.EntryGround.HasValue) return null;
            var first=Near(shelves[(int)(seed%(uint)shelves.Count)]);
            var second=Near(shelves[(int)((seed+1)%(uint)shelves.Count)]);
            var keeper=Near(counter.Value);
            if(!first.HasValue || !second.HasValue || !keeper.HasValue) return null;
            var entry=room.transform.InverseTransformPoint(geometry.EntryGround.Value);
            var routes=new List<List<GuardPoint3>> {
                geometry.RouteBetween(entry,first.Value),geometry.RouteBetween(first.Value,second.Value),
                geometry.RouteBetween(second.Value,keeper.Value),geometry.RouteBetween(keeper.Value,entry) };
            try
            {
                var journey=ShopVisitJourney.Create(start,end,routes,ResidentMovement.WalkSpeed,12);
                reserved[id]=new List<Vector3> { first.Value,second.Value,keeper.Value };
                return journey;
            }
            catch(ArgumentException) { return null; }
        }
        private Vector3? Near(Vector3 target)
        {
            float distance=16; Vector3? selected=null;
            foreach(var anchor in geometry.LocalAnchors)
            {
                bool taken=false;
                foreach(var stops in reserved.Values) if(stops.Contains(anchor)) { taken=true; break; }
                if(taken) continue;
                float d=(anchor-target).sqrMagnitude;
                if(d>=distance || Mathf.Abs(anchor.y-target.y)>2) continue;
                selected=anchor; distance=d;
            }
            return selected;
        }
    }
}
