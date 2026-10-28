using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
static class Program
{
    static int checks;static void Need(bool value,string text){if(!value)throw new InvalidOperationException(text);checks++;Console.WriteLine("PASS "+text);}
    static NpcActionTarget Point(double x)=>new NpcActionTarget{Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="dungeon-1",SettlementId=1,X=x};
    static int Main()
    {
        var store=new WorldStateStore(56);var town=store.GetOrCreate(1,new[]{10,20},1);var person=town.Households[0].Residents[0];
        var guards=store.GetOrCreateGuards(1,2,new[]{new SavedRoutePoint(),new SavedRoutePoint{X=10}});var guard=guards.Guards[0];
        guards.DispatchDefense("actual-case",person.Id,43200,new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,new List<SavedRoutePoint>{guard.Post,guard.Post}}},1,2,2);
        store.InitializeNpcInventory(person.Id,50,new NpcInventoryItem[0]);
        var proposed=new NpcCustodyRecord{PersonId=person.Id,GuardId=guard.Id,CaseId="actual-case",SettlementId=1,NativeCrime=4,
            CellDoorId="42",Cell=Point(4),ReleasePoint=Point(0),LastPosition=Point(0),UpdatedAt=43200};
        Need(!store.TryBeginNpcCustody(proposed,false),"unavailable actual cell cannot admit custody");
        proposed.Cell.SettlementId=2;
        Need(!store.TryBeginNpcCustody(proposed,true),"cell from another settlement cannot admit local custody");
        proposed.Cell.SettlementId=1;proposed.Cell.WorldSpaceId=null;
        Need(!store.TryBeginNpcCustody(proposed,true),"custody position without actual space identity is rejected");
        proposed.Cell.WorldSpaceId="dungeon-1";
        Need(store.TryBeginNpcCustody(proposed,true),"actual cell and assigned guard admit one custody record");
        Need(!store.TryBeginNpcCustody(proposed,true),"same person cannot be arrested twice");
        var custody=store.ReadNpcCustody(person.Id);
        Need(custody.Phase==NpcCustodyPhase.Escorting && custody.ReleaseAt==0,"escort does not start sentence clock or fabricate confinement");
        Need(!store.TryConfineNpc(person.Id,custody.Revision,43201,true,true,new int[25]),"distant prisoner cannot enter cell by time alone");
        Need(store.TryObserveNpcCustody(person.Id,custody.Revision,Point(4),43202),"actual observed body arrival updates existing position");
        custody=store.ReadNpcCustody(person.Id);
        Need(!store.TryConfineNpc(person.Id,custody.Revision,43203,false,true,new int[25]),"open native cell door cannot admit confinement");
        Need(store.TryConfineNpc(person.Id,custody.Revision,43203,true,true,new int[25]),"closed locked actual cell starts native-length sentence");
        custody=store.ReadNpcCustody(person.Id);Need(custody.PrisonDays==75 && custody.ReleaseAt==43203+75*86400,"native assault sentence is NPC time without global clock mutation");
        var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
        Need(restored.ReadNpcCustody(person.Id).ReleaseAt==custody.ReleaseAt,"current save preserves exact confinement and release time");
        Need(!store.TryReleaseNpcCustody(person.Id,custody.Revision,custody.ReleaseAt-1),"time before sentence end cannot release prisoner");
        Need(store.TryReleaseNpcCustody(person.Id,custody.Revision,custody.ReleaseAt),"actual sentence end begins physical release");
        custody=store.ReadNpcCustody(person.Id);Need(custody.Phase==NpcCustodyPhase.Returning,"elapsed sentence does not fabricate completed return");
        Need(store.TryObserveNpcCustody(person.Id,custody.Revision,Point(0),custody.ReleaseAt+1),"actual return arrival releases existing routine owner");
        Need(store.ReadNpcCustody(person.Id).Phase==NpcCustodyPhase.Released,"physical return completes bounded custody lifecycle");
        Console.WriteLine(checks+" checks passed");return 0;
    }
}
