using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
static class Program
{
    static int checks;static void Need(bool value,string text){if(!value)throw new InvalidOperationException(text);checks++;Console.WriteLine("PASS "+text);}
    static NpcActionTarget Point(string space,double x)=>new NpcActionTarget{Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId=space,SettlementId=1,X=x};
    static int Main()
    {
        var store=new WorldStateStore(56);var town=store.GetOrCreate(1,new[]{10,20},1);var person=town.Households[0].Residents[0];
        var roster=store.GetOrCreateGuards(1,2,new[]{new SavedRoutePoint(),new SavedRoutePoint{X=10}});var guard=roster.Guards[0];
        roster.DispatchDefense("actual-case",person.Id,43200,new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,new List<SavedRoutePoint>{guard.Post,guard.Post}}},1,2,2);
        roster.Engage(guard.Id,43200);store.InitializeNpcInventory(person.Id,50,new NpcInventoryItem[0]);
        var portal=new NpcCustodyPortal {EntranceId="native:1:10:0:0",Profile="actual-profile",Variant="actual-variant",
            Exterior=Point("city-1",0),Entry=Point("dungeon-1",1),CellOutside=Point("dungeon-1",3),GuardPosition=Point("city-1",0)};
        portal.CellMinimum=Point("dungeon-1",3.5);portal.CellMinimum.Z=-1;
        portal.CellMaximum=Point("dungeon-1",6);portal.CellMaximum.Y=3;portal.CellMaximum.Z=1;
        var proposed=new NpcCustodyRecord{PersonId=person.Id,GuardId=guard.Id,CaseId="actual-case",SettlementId=1,NativeCrime=4,
            CellDoorId="42",Cell=Point("dungeon-1",4),ReleasePoint=Point("city-1",8),LastPosition=Point("city-1",0),UpdatedAt=43200,Portal=portal};
        Need(store.TryBeginNpcCustody(proposed,true),"qualified local portal keeps the same actual case at its exterior source");
        var row=store.ReadNpcCustody(person.Id);
        Need(!store.TryBeginNpcCustodyPortal(person.Id,row.Revision,43201,true,"wrong-door",true),"different native entrance cannot begin custody crossing");
        Need(!store.TryBeginNpcCustodyPortal(person.Id,row.Revision,43201,true,portal.EntranceId,false),"missing physical pair at actual doorway cannot begin crossing");
        Need(store.TryBeginNpcCustodyPortal(person.Id,row.Revision,43201,true,portal.EntranceId,true),"actual native entry begins pending crossing without arrival");
        row=store.ReadNpcCustody(person.Id);Need(row.LastPosition.WorldSpaceId=="city-1" && row.Portal.Stage==NpcCustodyPortalStage.Entering && row.ConfinedAt==0,
            "pending entry preserves source coordinates and cannot start sentence");
        Need(!store.TryCompleteNpcCustodyPortal(person.Id,row.Revision,43202,Point("dungeon-1",1),false),"no native transition cannot complete entry");
        Need(!store.TryCompleteNpcCustodyPortal(person.Id,row.Revision,43202,Point("dungeon-1",20),true),"remote dungeon point cannot replace the genuine entrance landing");
        Need(store.TryAbortNpcCustodyPortal(person.Id,row.Revision,43202),"failed native transition returns to same exterior source");
        row=store.ReadNpcCustody(person.Id);Need(row.LastPosition.WorldSpaceId=="city-1" && row.Portal.Stage==NpcCustodyPortalStage.Exterior,"abort fabricates no arrival");
        Need(store.TryBeginNpcCustodyPortal(person.Id,row.Revision,43203,true,portal.EntranceId,true),"same real case can retry the exact native entrance");
        row=store.ReadNpcCustody(person.Id);Need(store.TryCompleteNpcCustodyPortal(person.Id,row.Revision,43204,Point("dungeon-1",1),true,Point("dungeon-1",1.5)),"actual native landing commits only the entrance space change");
        row=store.ReadNpcCustody(person.Id);Need(!store.TryConfineNpc(person.Id,row.Revision,43205,true,true,new int[25]),"native portal landing does not substitute for actual cell walk");
        Need(store.TryObserveNpcCustodyGuard(person.Id,row.Revision,Point("dungeon-1",1),43205),"actual guard position remains separately qualified");
        row=store.ReadNpcCustody(person.Id);Need(store.TryObserveNpcCustody(person.Id,row.Revision,Point("dungeon-1",4),43206),"real prisoner movement reaches cell after portal");
        row=store.ReadNpcCustody(person.Id);Need(store.TryConfineNpc(person.Id,row.Revision,43207,true,true,new int[25]),"real cell arrival and native locked door start sentence");
        Need(store.HasNpcCustodyEscort(guard.Id),"confined prisoner retains guard duty until physical portal return");
        row=store.ReadNpcCustody(person.Id);Need(store.TryReturnNpcCustodyGuard(person.Id,row.Revision,43208,Point("city-1",0),portal.EntranceId,true),"actual guard at native entry can return through matching exterior seam");
        Need(!store.HasNpcCustodyEscort(guard.Id),"actual guard return releases its existing duty owner");
        var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());row=restored.ReadNpcCustody(person.Id);
        Need(row.Portal.GuardReturned && row.Portal.EntranceId==portal.EntranceId && row.Portal.Stage==NpcCustodyPortalStage.Interior,"current save retains exact portal/cell connection without migration");
        Need(restored.TryReleaseNpcCustody(person.Id,row.Revision,row.ReleaseAt),"native sentence expiry begins existing physical release");
        row=restored.ReadNpcCustody(person.Id);Need(!restored.TryBeginNpcCustodyPortal(person.Id,row.Revision,row.UpdatedAt+1,false,portal.EntranceId,true),"cell position cannot fake reaching the dungeon exit");
        Need(restored.TryObserveNpcCustody(person.Id,row.Revision,Point("dungeon-1",1),row.UpdatedAt+1),"actual return walk reaches the native entry");
        row=restored.ReadNpcCustody(person.Id);Need(restored.TryBeginNpcCustodyPortal(person.Id,row.Revision,row.UpdatedAt+1,false,portal.EntranceId,true),"actual native exit begins same-case release crossing");
        row=restored.ReadNpcCustody(person.Id);Need(restored.TryCompleteNpcCustodyPortal(person.Id,row.Revision,row.UpdatedAt+1,Point("city-1",0),true),"native exterior landing preserves pending physical return");
        row=restored.ReadNpcCustody(person.Id);Need(row.Phase==NpcCustodyPhase.Returning,"exterior portal never fabricates the final routine arrival");
        Need(restored.TryObserveNpcCustody(person.Id,row.Revision,Point("city-1",8),row.UpdatedAt+1),"actual walk back to original release point completes same custody case");
        Need(restored.ReadNpcCustody(person.Id).Phase==NpcCustodyPhase.Released,"ordinary routine resumes only after actual return");
        Console.WriteLine(checks+" checks passed");return 0;
    }
}
