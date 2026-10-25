using System;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var offer=NpcMagicCarePort.Discover(new NpcMagicCareFacts {ActorId="npc:mage",CurrentHealth=10,
            MaximumHealth=20,CurrentMagicka=30,PoisonCount=1,DiseaseCount=1,NativeAdapterAvailable=true,
            DutyAvailable=true,KnownEffects=new[] {new NpcMagicKnownEffect {SpellId="book:0:abc",
                EffectId="Cure-Poison",EffectKind=NpcMagicEffectKind.Condition,MagickaCost=7}}})[0];
        var task=NpcMagicCareTask.Reserve("care:poison:1",offer,1,100);
        Check(task.Kind==NpcActionKind.CastSpell && task.SpellId=="book:0:abc" &&
            task.EffectId=="Cure-Poison" && task.MagickaCost==7 && task.ConditionBefore==1,
            "persist exact cast and condition");
        Check(task.AttemptDeadlineAt==160,"cast attempt has bounded deadline");
        Check(!NpcMagicCareTask.CanRetry(task,100),"reserved task suppresses retry");
        var observed=new NpcMagicObservedOutcome {ActorId="npc:mage",TargetId="npc:mage",
            SpellId="book:0:abc",EffectId="Cure-Poison",EffectKind=NpcMagicEffectKind.Condition,
            NativeAdapterExecuted=true,MagickaBefore=30,MagickaAfter=23,EffectValueBefore=1,EffectValueAfter=0};
        Check(NpcMagicCareTask.MatchesObservation(task,offer.Authorization,observed),
            "exact native observation matches reservation");
        Check(!NpcMagicCareTask.MatchesObservation(task,offer.Authorization,new NpcMagicObservedOutcome()),
            "unverified effect cannot complete");
        Check(!NpcMagicCareTask.CanRetry(task,101),"reserved task suppresses retry");
        var store=new WorldStateStore(17);var town=store.GetOrCreate(11,new[] {23});
        string resident=town.Households[0].Residents[0].Id;
        var residentOffer=NpcMagicCarePort.Discover(new NpcMagicCareFacts {ActorId=resident,
            CurrentHealth=10,MaximumHealth=20,CurrentMagicka=30,PoisonCount=1,DiseaseCount=0,
            NativeAdapterAvailable=true,DutyAvailable=true,KnownEffects=new[] {
                new NpcMagicKnownEffect {SpellId="book:0:abc",EffectId="Cure-Poison",
                    EffectKind=NpcMagicEffectKind.Condition,MagickaCost=7}}})[0];
        Check(store.TryReserveNpcMagicCareTask("magic:episode:1",residentOffer,1,100),"reserve exact care task");
        Check(!store.TryReserveNpcMagicCareTask("magic:episode:2",residentOffer,1,101),"one owner per active need");
        var diseaseOffer=NpcMagicCarePort.Discover(new NpcMagicCareFacts {ActorId=resident,
            CurrentHealth=10,MaximumHealth=20,CurrentMagicka=30,PoisonCount=0,DiseaseCount=1,
            NativeAdapterAvailable=true,DutyAvailable=true,KnownEffects=new[] {
                new NpcMagicKnownEffect {SpellId="book:1:def",EffectId="Cure-Disease",
                    EffectKind=NpcMagicEffectKind.Condition,MagickaCost=7}}})[0];
        Check(!store.TryReserveNpcMagicCareTask("magic:episode:disease",diseaseOffer,1,101),
            "one reserved native action per resident");
        var saved=store.Snapshot();var loaded=new WorldStateStore(17);loaded.Restore(saved);
        Check(loaded.ReadNpcMagicCareTask("magic:episode:1").SpellId=="book:0:abc","restore exact reservation");
        Check(loaded.TryBlockNpcMagicCareTask("magic:episode:1",1,102,"native effect unavailable"),"block failed cast");
        Check(!loaded.TryRetryNpcMagicCareTask("magic:episode:1",2,residentOffer,1,103),"cooldown blocks immediate retry");
        Check(loaded.TryRetryNpcMagicCareTask("magic:episode:1",2,residentOffer,1,402),"retry at cooldown boundary");
        var completeStore=new WorldStateStore(19);var completeTown=completeStore.GetOrCreate(12,new[] {24});
        string caster=completeTown.Households[0].Residents[0].Id;
        var baseline=completeStore.ReadNpcVitals(caster,NpcArchetype.Citizen);
        Check(baseline.CurrentMagicka>0,"caster has magicka");
        Check(completeStore.TryCaptureNpcVitals(caster,NpcArchetype.Citizen,baseline.CurrentHealth-1,
            baseline.CurrentMagicka,90,baseline.Revision),"persist native vitals");
        var persisted=completeStore.ReadNpcVitals(caster,NpcArchetype.Citizen);
        var cureOffer=NpcMagicCarePort.Discover(new NpcMagicCareFacts {ActorId=caster,
            CurrentHealth=persisted.CurrentHealth,MaximumHealth=persisted.MaximumHealth,
            CurrentMagicka=persisted.CurrentMagicka,PoisonCount=1,DiseaseCount=0,
            NativeAdapterAvailable=true,DutyAvailable=true,KnownEffects=new[] {
                new NpcMagicKnownEffect {SpellId="book:0:abc",EffectId="Cure-Poison",
                    EffectKind=NpcMagicEffectKind.Condition,MagickaCost=1}}})[0];
        Check(completeStore.TryReserveNpcMagicCareTask("magic:episode:complete",cureOffer,1,100),
            "reserve persisted cure episode");
        var cureOutcome=new NpcMagicObservedOutcome {ActorId=caster,TargetId=caster,
            SpellId="book:0:abc",EffectId="Cure-Poison",EffectKind=NpcMagicEffectKind.Condition,
            NativeAdapterExecuted=true,MagickaBefore=persisted.CurrentMagicka,
            MagickaAfter=persisted.CurrentMagicka-1,EffectValueBefore=1,EffectValueAfter=0};
        var method=typeof(WorldStateStore).GetMethod("TryCompleteNpcMagicCareTask",
            BindingFlags.Instance|BindingFlags.NonPublic);
        Check(method!=null,"trusted native commit boundary exists");
        int nativeCalls=0;
        var staleArgs=new object[] {"magic:episode:complete",0L,101d,NpcArchetype.Citizen,persisted.Revision,
            new Func<NpcMagicCareOffer>(()=>{nativeCalls++;return cureOffer;}),
            new Func<NpcMagicObservedOutcome>(()=>{nativeCalls++;return cureOutcome;})};
        Check(!(bool)method.Invoke(completeStore,staleArgs) && nativeCalls==0,
            "stale reservation never reaches native adapter");
        staleArgs[1]=1L;staleArgs[2]=160d;
        Check(!(bool)method.Invoke(completeStore,staleArgs) && nativeCalls==0,
            "expired reservation never reaches native adapter");
        var unverified=new NpcMagicObservedOutcome {ActorId=caster,TargetId=caster,
            SpellId="book:0:abc",EffectId="Cure-Poison",EffectKind=NpcMagicEffectKind.Condition,
            NativeAdapterExecuted=false,MagickaBefore=persisted.CurrentMagicka,
            MagickaAfter=persisted.CurrentMagicka-1,EffectValueBefore=1,EffectValueAfter=0};
        var unverifiedArgs=new object[] {"magic:episode:complete",1L,101d,NpcArchetype.Citizen,
            persisted.Revision,new Func<NpcMagicCareOffer>(()=>cureOffer),
            new Func<NpcMagicObservedOutcome>(()=>unverified)};
        Check(!(bool)method.Invoke(completeStore,unverifiedArgs) &&
            completeStore.ReadNpcMagicCareTask("magic:episode:complete").Receipt==null &&
            completeStore.ReadNpcVitals(caster,NpcArchetype.Citizen).Revision==persisted.Revision,
            "unverified cast cannot write receipt or vitals");
        var args=new object[] {"magic:episode:complete",1L,101d,NpcArchetype.Citizen,persisted.Revision,
            new Func<NpcMagicCareOffer>(()=>cureOffer),new Func<NpcMagicObservedOutcome>(()=>cureOutcome)};
        Check((bool)method.Invoke(completeStore,args),"observed self cure commits");
        var committed=completeStore.ReadNpcMagicCareTask("magic:episode:complete");
        Check(committed.Receipt!=null && committed.Receipt.ConditionAfter==0 &&
            completeStore.ReadNpcVitals(caster,NpcArchetype.Citizen).CurrentMagicka==persisted.CurrentMagicka-1,
            "receipt and magicka persist together");
        Check(committed.HasObservedClear,"native cure clears condition episode owner");
        Check(completeStore.ReadNpcMagicCareEvents(caster,100).Length==0,
            "future native event stays hidden");
        var events=completeStore.ReadNpcMagicCareEvents(caster,101);
        Check(events.Length==1 && events[0].ReceiptId=="magic:episode:complete:treatment" &&
            events[0].ConditionBefore==1 && events[0].ConditionAfter==0,
            "event projection carries exact native receipt");
        var recall=completeStore.ReadNpcMagicCareRecall(caster,101);
        Check(recall!=null && recall.Contains("Cure-Poison") && recall.Contains("1 to 0"),
            "recall names observed cure only");
        var committedSave=completeStore.Snapshot();var restored=new WorldStateStore(0);restored.Restore(committedSave);
        Check(restored.ReadNpcMagicCareTask("magic:episode:complete").Receipt.SpellId=="book:0:abc",
            "observed receipt survives restore");
        committedSave.NpcMagicCareTasks[0].Receipt.ConditionAfter=1;
        bool rejected=false;try {new WorldStateStore(0).Restore(committedSave);} catch(ArgumentException) {rejected=true;}
        Check(rejected,"tampered native receipt rejects restore");
        var legacy=completeStore.Snapshot();legacy.Version=56;legacy.NpcMagicCareTasks=null;
        var migrated=new WorldStateStore(0);migrated.Restore(legacy);
        Check(migrated.Snapshot().NpcMagicCareTasks.Count==0 &&
            migrated.Snapshot().Version==WorldState.CurrentVersion,
            "schema 56 migrates without invented magic care");
        Console.WriteLine("28/28 passed");return 0;
    }
    private static void Check(bool value,string name)
    {if(!value)throw new Exception(name);}
}
