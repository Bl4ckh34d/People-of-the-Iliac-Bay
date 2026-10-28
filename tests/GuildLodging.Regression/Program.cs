using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static void Need(bool value,string message){if(!value)throw new Exception(message);}
    private static void Set(Resident resident,string name,object value)
    {var field=typeof(Resident).GetField(name);Need(field!=null,"Missing saved NPC guild identity field: "+name);field.SetValue(resident,value);}
    private static WorldStateStore Fixture(out Resident resident,out TravelerVisit visit)
    {
        var store=new WorldStateStore(1);store.GetOrCreate(1,new int[0]);
        var home=store.GetOrCreate(2,new[]{20},1);resident=home.Households[0].Residents[0];resident.FullName="Guild Guest";resident.Race=1;
        store.RegisterLodgingBeds(1,7,new[]{new LodgingBed {Id="bed:0"}});
        var register=typeof(WorldStateStore).GetMethod("RegisterGuildLodgingBeds");
        Need(register!=null,"Missing actual guild bed registration");
        register.Invoke(store,new object[]{1,8,999,new[]{new LodgingBed {Id="bed:0"}}});
        var person=new TravelerPerson {Id=resident.Id,Name=resident.FullName,HomeSettlement=2,HomeKey=20,Race=resident.Race,
            AppearanceSeed=resident.AppearanceSeed,Sex=resident.Sex};
        visit=new TravelerVisit {Id="guild-visit",PersonId=person.Id,SettlementId=1,TavernKey=7,Nights=1,DesiredArrival=100,CommuteSeconds=10,
            Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {X=10}}};
        store.RegisterTraveler(person);store.RegisterTravelerVisit(visit);var journey=store.TravelerJourneyFor(visit.Id);
        store.TryBookLodging(1,7,"full","other",2,false,1,journey.CheckInAt,journey.CheckOutAt);
        Need(!store.TryAdmitTraveler(visit.Id,journey.CheckInAt),"Preferred Inn must actually refuse a full stay");return store;
    }
    private static int Main()
    {
        Resident resident;TravelerVisit visit;var store=Fixture(out resident,out visit);
        var route=new[]{new SavedRoutePoint {X=10},new SavedRoutePoint {X=30}};
        Need(!store.TryRedirectTraveler(visit.Id,8,false,route,20,110),"Unknown membership must not reserve a guild bed");
        Set(resident,"GuildIdentityKnown",true);Set(resident,"GuildFactionId",998);Set(resident,"GuildRank",0);Set(resident,"GuildHomeKey",77);
        Need(!store.TryRedirectTraveler(visit.Id,8,false,route,20,110),"Wrong guild membership must not reserve a bed");
        Set(resident,"GuildFactionId",999);
        Need(store.TryRedirectTraveler(visit.Id,8,false,route,20,110),"Actual matching known membership should reserve the real free guild bed");
        Need(store.TravelerBed(visit.Id,129)==null && store.TravelerBed(visit.Id,130)=="bed:0","Guild bed starts only on measured arrival");
        var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
        var copy=restored.TravelerOriginResident(resident.Id);
        Need((bool)typeof(Resident).GetField("GuildIdentityKnown").GetValue(copy) &&
            (int)typeof(Resident).GetField("GuildFactionId").GetValue(copy)==999 &&
            (int)typeof(Resident).GetField("GuildRank").GetValue(copy)==0 &&
            (int)typeof(Resident).GetField("GuildHomeKey").GetValue(copy)==77,"Saved origin membership must survive current restore");
        var corrupt=store.Snapshot();var forged=corrupt.Settlements.Find(t=>t.SettlementId==2).Households[0].Residents[0];
        Set(forged,"GuildFactionId",998);bool rejected=false;
        try{restored.Restore(corrupt);}catch(ArgumentException){rejected=true;}
        Need(rejected,"Current save must reject guild booking without matching origin membership");
        Need(store.KillTraveler(resident.Id,200),"Actual guild guest can die after admission");
        restored.Restore(store.Snapshot());
        Need(!restored.Traveler(resident.Id).IsAlive,"Historical guild booking must not invalidate a deceased guest's current save");
        Need(!store.TryBookLodging(1,8,"after-death",resident.Id,2,false,1,100000,110000),"A deceased guild member cannot make a new booking");
        store=Fixture(out resident,out visit);Set(resident,"GuildIdentityKnown",true);Set(resident,"GuildFactionId",999);Set(resident,"GuildRank",0);Set(resident,"GuildHomeKey",77);
        Need(store.TryBookLodging(1,8,"guild-full",resident.Id,2,false,1,130,90000),"Known member can occupy actual guild bed");
        Need(!store.TryRedirectTraveler(visit.Id,8,false,route,20,110),"Full guild must refuse another overlapping booking");
        Need(store.TryRedirectTraveler(visit.Id,-1,true,new[]{new SavedRoutePoint{X=10},new SavedRoutePoint(),new SavedRoutePoint{X=-20}},30,110),
            "Full guild leaves actual outside camp available");
        Console.WriteLine("PASS guild identity persistence, matching membership, exact free bed timing, forged save rejection and full-guild camp fallback");return 0;
    }
}
