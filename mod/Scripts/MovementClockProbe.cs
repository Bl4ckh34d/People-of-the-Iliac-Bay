using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    internal static class MovementClockProbe
    {
        private static double Now()
        {var now=DaggerfallUnity.Instance.WorldTime.Now;return (double)now.ToSeconds()+now.Second-Math.Floor(now.Second);}
        public static IEnumerator Run(LivingCitiesMod host,StreetPopulation streets,string character)
        {
            var saves=GameManager.Instance.SaveLoadManager;
            saves.EnumerateSaves(); int slot=saves.FindSaveFolderByNames(character,"People - Daggerfall day");
            if(slot<0) throw new InvalidOperationException("Movement probe requires current Daggerfall day fixture.");
            bool loaded=false; SaveLoadManager.OnLoadEventHandler handler=data=>loaded=true;
            SaveLoadManager.OnLoad+=handler;
            try
            {
                saves.Load(slot); DaggerfallUI.Instance.PopToHUD();
                float deadline=Time.realtimeSinceStartup+90;
                while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<deadline) yield return null;
                if(!loaded || !streets.Ready) throw new InvalidOperationException("Movement fixture did not load.");
            }
            finally {SaveLoadManager.OnLoad-=handler;}
            var store=(WorldStateStore)typeof(LivingCitiesMod).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);
            var snapshot=store.Snapshot();
            var people=new System.Collections.Generic.Dictionary<string,Resident>();
            foreach(var town in snapshot.Settlements) foreach(var home in town.Households)
                foreach(var resident in home.Residents) people.Add(resident.Id,resident);
            int checkedLegs=0;
            foreach(var town in snapshot.Routines) foreach(var person in town.Residents)
                if(person.Itinerary!=null) foreach(var stop in person.Itinerary.Stops)
                {
                    double length=0;
                    for(int n=1;n<stop.Route.Count;n++)
                    {var a=stop.Route[n-1];var b=stop.Route[n];length+=Math.Sqrt((b.X-a.X)*(b.X-a.X)+(b.Z-a.Z)*(b.Z-a.Z));}
                    if(Math.Abs(length/stop.WalkSeconds*2-ResidentMovement.WalkSpeedFor(people[person.ResidentId].AppearanceSeed))>0.002)
                        throw new InvalidOperationException("Loaded route still has a clock-dependent physical speed.");
                    checkedLegs++;
                }
            if(checkedLegs==0) throw new InvalidOperationException("Movement fixture lacks saved daily routes.");
            GameObject floor=null,body=null;
            try
            {
                floor=GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name="Movement clock probe floor"; floor.transform.position=new Vector3(0,999.9f,0);
                floor.transform.localScale=new Vector3(300,0.2f,12);
                body=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject);
                var actor=body.AddComponent<ResidentActor>();
                actor.Initialize(new Resident {Id="movement-probe",FullName="Movement Probe",AppearanceSeed=42},1,
                    GameManager.Instance.StreamingWorld.GetCurrentCityNavigation());
                Physics.SyncTransforms(); yield return null;
                foreach(double speed in new[]{actor.WalkingSpeed,ResidentMovement.FleeSpeed(42)})
                {
                    var motion=body.GetComponent<CrowdMotion>(); motion.ResetPlacement();
                    actor.Place(new Vector3(0,1000,0),Vector3.right);
                    double gameStart=Now(); float realStart=Time.realtimeSinceStartup;
                    while(Time.realtimeSinceStartup-realStart<4)
                    {
                        double distance=(Now()-gameStart)/2*speed;
                        actor.Place(new Vector3((float)distance,1000,0),Vector3.right,false,speed>2);
                        yield return null;
                    }
                    double real=Time.realtimeSinceStartup-realStart;
                    double measured=body.transform.position.x/real,clock=(Now()-gameStart)/real;
                    if(Math.Abs(measured-speed)>speed*0.1 || Math.Abs(clock-2)>0.1)
                        throw new InvalidOperationException("Physical speed/clock mismatch: expected="+speed+"; measured="+measured+"; clock="+clock);
                    Debug.Log("LIVING_CITIES_MOVEMENT_SPEED: expected="+speed+"; measured="+measured+"; clock="+clock);
                }
            }
            finally {if(body) UnityEngine.Object.Destroy(body);if(floor) UnityEngine.Object.Destroy(floor);}
            Debug.Log("LIVING_CITIES_MOVEMENT_CLOCK_PASS: saved legs="+checkedLegs+"; native actor walking/running at real speed; world clock=2x.");
        }
    }
}
