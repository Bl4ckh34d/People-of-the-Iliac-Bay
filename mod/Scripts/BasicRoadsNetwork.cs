using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Lazy main-thread import. Call only after mods are ready; no per-frame polling.
    public sealed class BasicRoadsNetwork
    {
        private OverlandRoadNetwork network;
        // The owner must invalidate stillCurrent on settlement/load changes.
        // Dispose the coroutine on cancellation; no partial trip is committed here.
        public IEnumerator FindRoute(int fromX,int fromZ,int toX,int toZ,int maximumExpanded,
            Func<bool> stillCurrent,Action<IReadOnlyList<RoutePoint>> completed,int workPerFrame=64)
        {
            if(stillCurrent==null || completed==null || workPerFrame<1 || workPerFrame>256)
                throw new ArgumentException("Invalid road route request.");
            yield return BackgroundWork.Wait(stillCurrent);
            if(!stillCurrent()) yield break;
            OverlandRoadNetwork snapshot;
            if(!TryGet(out snapshot)) { completed(new RoutePoint[0]); yield break; }
            var search=snapshot.BeginSearch(fromX,fromZ,toX,toZ,maximumExpanded);
            try
            {
                while(!search.Complete)
                {
                    if(!stillCurrent()) yield break;
                    search.Advance(workPerFrame);
                    if(!search.Complete) yield return null;
                }
                if(stillCurrent()) completed(search.Result);
            }
            finally { search.Cancel(); }
        }
        public bool TryGet(out OverlandRoadNetwork result)
        {
            result=network;
            if(network!=null) return true;
            var manager=ModManager.Instance;
            var provider=manager.GetMod("BasicRoads");
            if(provider==null || !provider.Enabled || !provider.IsReady) return false;
            byte[] roads=null, tracks=null;
            manager.SendModMessage("BasicRoads","getPathData",0,(message,data)=>{
                if(message=="getPathData") roads=data as byte[];
            });
            manager.SendModMessage("BasicRoads","getPathData",1,(message,data)=>{
                if(message=="getPathData") tracks=data as byte[];
            });
            if(roads==null || tracks==null) return false;
            try { network=new OverlandRoadNetwork(roads,tracks,MapsFile.MaxMapPixelX,MapsFile.MaxMapPixelY); }
            catch(ArgumentException) { return false; }
            result=network;
            return true;
        }
    }
}
