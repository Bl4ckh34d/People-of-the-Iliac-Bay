using System;
using PeopleOfTheIliacBay.Core;
using FullSerializer;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition,string message)
    {checks++;if(!condition)throw new InvalidOperationException(message);}

    private static (WorldStateStore Store,Household Home,Resident Parent,Resident Partner,Resident Child,int Town) Family()
    {
        var store=new WorldStateStore(42);
        for(int k=1;k<100;k++)
        {
            var town=store.GetOrCreateFamilies(100+k,new[]{new HomeProfile(k,5,100)});
            var home=town.Households[0];var child=home.Residents.Find(p=>p.AgeGroup==ResidentAgeGroup.Child);
            if(child==null)continue;
            var parent=home.Residents.Find(p=>p.Id==child.MotherId);
            return (store,home,parent,home.Residents.Find(p=>p.Id==parent.PartnerId),child,town.SettlementId);
        }
        throw new Exception("Missing family fixture.");
    }

    private static CivilianIncident Death(WorldStateStore store,int town,Resident victim,int building,double at=100)
    {
        var death=store.RecordViolence(town,victim.Id,at);
        store.LocateViolence(death.Id,building,0,0,0);victim.IsAlive=false;
        store.ConfirmCorpse(death.Id,at,building,0,0,0);return death;
    }

    private static void Main()
    {
        var f=Family();var death=Death(f.Store,f.Town,f.Partner,f.Home.BuildingKey);
        Check(f.Store.BereavementFor(f.Parent,101)==null,"Remote corpse must not reveal a partner's death.");
        Check(!f.Store.ReadNpcHouseholdKnowledge(f.Parent.Id).ToContext().Contains("deceased"),"Household context cannot reveal remote death.");
        f.Store.Witness(death.Id,f.Parent.Id,110);
        Check(f.Store.BereavementFor(f.Parent,111)!=null,"Actual late discovery admits grief.");
        Check(!f.Store.HoldsPartnerGrudge(f.Parent,111),"Late corpse discovery cannot identify player killer.");
        f.Store.MarkPlayerMurder(death.Id);
        Check(!f.Store.HoldsPartnerGrudge(f.Parent,111),"Global player attribution does not upgrade a corpse observer.");
        Check(f.Store.ReadNpcHouseholdKnowledge(f.Parent.Id).ToContext().Contains("known deceased"),"Known death reaches family context.");
        f=Family();death=Death(f.Store,f.Town,f.Partner,f.Home.BuildingKey);
        f.Store.Witness(death.Id,f.Parent.Id,100);
        Check(!f.Store.HoldsPartnerGrudge(f.Parent,101),"Unknown killer is not player attribution.");
        var beforeAttribution=f.Store.Snapshot().SceneMemories.Find(m=>m.Kind==NpcSceneEventKind.Bereavement);
        var beforeInboxCount=f.Store.Snapshot().NpcExperienceInbox.Entries.Count;
        f.Store.Restore(f.Store.Snapshot());
        f.Store.MarkPlayerMurder(death.Id);
        Check(f.Store.HoldsPartnerGrudge(f.Parent,101),"Actual player murder eyewitness retains grudge.");
        Check(f.Store.RecallSceneEvents(f.Parent.Id,101,2000).Contains("I saw the player kill them."),"Attributed eyewitness recall is grounded.");
        f.Store.MarkPlayerMurder(death.Id);
        var afterAttribution=f.Store.Snapshot();
        var attributed=afterAttribution.SceneMemories.FindAll(m=>m.Kind==NpcSceneEventKind.Bereavement);
        Check(attributed.Count==1 && attributed[0].At==beforeAttribution.At && attributed[0].ExpiresAt==beforeAttribution.ExpiresAt,
            "Deferred explicit attribution refines one persisted memory without renewing retention.");
        Check(afterAttribution.NpcExperienceInbox.Entries.Count==beforeInboxCount,"Attribution refinement creates no duplicate appraisals.");
        Check(!f.Store.RecallSceneEvents(f.Parent.Id,101,2000).Contains("I cannot identify the killer from this memory"),
            "Deferred attribution also refines existing eyewitness memory without contradictory authority.");
        Bereavement grief;
        Check(!f.Store.TryBereavementGreeting(f.Parent,101,out grief),"Murder witness does not show neutral mourning greeting.");

        f=Family();death=f.Store.RecordViolence(f.Town,f.Partner.Id,100);
        f.Store.Witness(death.Id,f.Parent.Id,100);
        f.Store.LocateViolence(death.Id,f.Home.BuildingKey,0,0,0);f.Partner.IsAlive=false;
        f.Store.ConfirmCorpse(death.Id,120);
        Check(f.Store.BereavementFor(f.Parent,121)==null,"An earlier assault witness does not know a later unseen death.");
        Check(f.Store.Warn(death.Id,f.Parent.Id,f.Child.Id,122),"Existing warning source accepts nearby recipient.");
        Check(f.Store.BereavementFor(f.Child,123)==null,"Pre-death knowledge cannot notify death.");

        f=Family();death=Death(f.Store,f.Town,f.Partner,f.Home.BuildingKey);
        f.Store.Witness(death.Id,f.Child.Id,100);
        Check(f.Store.BereavementFor(f.Child,101).Relationship=="father","Child can grieve a known parent death.");
        Check(f.Store.Warn(death.Id,f.Child.Id,f.Parent.Id,110),"Actual eyewitness can notify partner.");
        grief=f.Store.BereavementFor(f.Parent,111);
        Check(grief!=null && grief.KnownAt==110 && !grief.WitnessedPlayerKilling,"Notification retains learning time without killer attribution.");
        Check(f.Store.BereavementFor(f.Parent,109)==null,"Future notification cannot leak backward.");
        Check(f.Store.RecallSceneEvents(f.Parent.Id,111,2000).Contains("A witness who knew about the death told me"),"Recall distinguishes notification source.");
        Check(!f.Store.Warn(death.Id,f.Child.Id,f.Parent.Id,112),"Repeated notification remains bounded.");
        f.Store.MarkPlayerMurder(death.Id);
        Check(!f.Store.HoldsPartnerGrudge(f.Parent,113),"Notification cannot manufacture witnessed-player grudge.");
        Check(!f.Store.HoldsPartnerGrudge(f.Child,113),"A grieving child does not enter adult retaliation combat.");
        var notifiedReload=new WorldStateStore(99);notifiedReload.Restore(f.Store.Snapshot());
        Check(notifiedReload.BereavementFor(f.Parent,114)!=null,"Delivered notification survives validated restore.");

        f=Family();death=Death(f.Store,f.Town,f.Child,-1);
        f.Store.NoticeOutdoorCorpse(death.Id,f.Parent.Id,false,105);
        grief=f.Store.BereavementFor(f.Parent,106);
        Check(grief!=null && grief.Relationship=="child","Outdoor discovery admits loss of child.");
        var memory=f.Store.Snapshot().SceneMemories.Find(m=>m.ObserverId==f.Parent.Id && m.Kind==NpcSceneEventKind.Bereavement);
        Check(memory!=null && memory.Valence==-5 && memory.ResponsibleId==null,"Grief uses negative personal memory without unknown blame.");
        Check(memory.ExpiresAt-memory.At>=360*86400d && memory.ExpiresAt-memory.At<=720*86400d,"Existing stable retention has one-to-two-year grief range.");
        f.Store.NoticeOutdoorCorpse(death.Id,f.Parent.Id,false,106);
        Check(f.Store.Snapshot().SceneMemories.FindAll(m=>m.Kind==NpcSceneEventKind.Bereavement).Count==1,"Repeated discovery does not repeat memory.");
        Check(f.Store.BereavementFor(f.Parent,106+180*86400d).MentionChance<grief.MentionChance,"Mention probability fades.");
        while(StableSeed.Combine(f.Parent.AppearanceSeed,0,0x67726965,0)%10000>=5900)f.Parent.AppearanceSeed++;
        Check(f.Store.TryBereavementGreeting(f.Parent,110,out grief),"Controlled stable roll permits greeting.");
        var serializer=new fsSerializer();fsData data;serializer.TrySerialize(f.Store.Snapshot(),out data).AssertSuccess();
        WorldState saved=null;serializer.TryDeserialize(data,ref saved).AssertSuccess();
        var restored=new WorldStateStore(99);restored.Restore(saved);
        Check(!restored.TryBereavementGreeting(f.Parent,111,out grief),"FullSerializer reload preserves greeting suppression.");
        Check(restored.BereavementFor(f.Parent,111)!=null,"FullSerializer reload preserves knowledge and grief.");
        Check(restored.Snapshot().SceneMemories.Find(m=>m.Kind==NpcSceneEventKind.Bereavement).ExpiresAt==memory.ExpiresAt,"Reload does not reroll retention.");
        death.Corpse.Cleared=true;death.Corpse.ClearedAt=120;
        Check(f.Store.BereavementFor(f.Parent,121)!=null,"Corpse cleanup does not erase grief.");
        Check(f.Store.BereavementFor(f.Parent,memory.ExpiresAt)==null,"Retention boundary ends active grief.");
        restored.RememberSceneEvent(f.Parent.Id,"later",NpcSceneEventKind.Conversation,memory.ExpiresAt+1,f.Town,-1,"A later conversation.");
        restored.NoticeOutdoorCorpse(death.Id,f.Parent.Id,false,memory.ExpiresAt+2);
        Check(restored.BereavementFor(f.Parent,memory.ExpiresAt+3)==null,"Pruned memory cannot restart from repeated corpse discovery.");
        f.Parent.IsAlive=false;
        Check(f.Store.BereavementFor(f.Parent,121)==null && !f.Store.HoldsPartnerGrudge(f.Parent,121),"Dead observer has no reaction.");
        Check(f.Store.BereavementFor(new Resident {Id="missing",PartnerId=f.Child.Id},121)==null,"Unknown observer has no reaction.");

        f=Family();death=Death(f.Store,f.Town,f.Partner,f.Home.BuildingKey);
        f.Store.NoticeInteriorCorpse(death.Id,f.Parent.Id,f.Home.BuildingKey,110);
        Check(f.Store.BereavementFor(f.Parent,111)!=null,"Interior perception source admits grief.");
        var interiorKnowledge=f.Store.Snapshot().SceneMemories.Find(m=>m.Kind==NpcSceneEventKind.Bereavement);
        Check(interiorKnowledge.Observation.Contains("I found the body.") && !interiorKnowledge.Observation.Contains("player kill"),"Interior discovery recall does not invent a killer.");
        Check(f.Store.RecallSceneEvents(f.Parent.Id,111,128).Length<=128,"Grief recall respects existing bounded budget.");
        f.Partner.IsAlive=true;
        Check(f.Store.BereavementFor(f.Parent,111)==null,"Living subject cannot trigger grief from stale memory.");
        f.Partner.IsAlive=false;
        while(StableSeed.Combine(f.Parent.AppearanceSeed,0,0x67726965,0)%10000<6000)f.Parent.AppearanceSeed++;
        Check(!f.Store.TryBereavementGreeting(f.Parent,111,out grief),"Stable failed roll consumes daily greeting attempt.");
        var failedRollReload=new WorldStateStore(1);failedRollReload.Restore(f.Store.Snapshot());
        while(StableSeed.Combine(failedRollReload.ResolveResident(f.Parent.Id).AppearanceSeed,0,0x67726965,0)%10000>=5900)
            failedRollReload.ResolveResident(f.Parent.Id).AppearanceSeed++;
        Check(!failedRollReload.TryBereavementGreeting(f.Parent,112,out grief),"Reload cannot reroll an unsuccessful daily attempt.");
        var stranger=f.Store.GetOrCreate(f.Town+1000,new[]{999},1).Households[0].Residents[0];
        f.Store.RememberSceneEvent(stranger.Id,"arbitrary",NpcSceneEventKind.Bereavement,111,f.Town,-1,"Unverified death claim.",subject:f.Partner.Id);
        Check(f.Store.BereavementFor(stranger,112)==null,"Generic memory or unrelated identity cannot create known grief.");
        f.Store.RememberObservation(f.Child.Id,111,"day","home","Someone claims my parent died.");
        Check(f.Store.BereavementFor(f.Child,112)==null,"Attributed conversation text never confirms death.");
        var copy=f.Store.Snapshot();var receipt=copy.AlarmIncidents[0].People.Find(p=>p.ResidentId==f.Parent.Id);
        receipt.DeathKnownAt=99;bool rejected=false;
        try {new WorldStateStore(1).Restore(copy);}catch(ArgumentException){rejected=true;}
        Check(rejected,"Restore rejects knowledge before actual death.");

        f=Family();DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.Seconds=1000;
        PeopleOfTheIliacBay.DFU.PeopleOfTheIliacBayMod.RunSourceProbe(f.Store,f.Parent.Id);
        Check(UnityEngine.Debug.LastMessage.Contains("no repeat after native restore"),"Actual greeting adapter preserves overlay and persisted cooldown under UI stubs.");
        Console.WriteLine("Known bereavement regressions passed: "+checks+" assertions.");
    }
}
