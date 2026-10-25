using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdOutingConversationTests
    {
        private static WorldStateStore Prepared(out HouseholdDayPlan plan,out HouseholdJointMovement motion,bool depart=true)
        {
            var request=HouseholdDayAppointmentTests.SharedRequest();plan=HouseholdDayPlanTests.Create(request);
            var store=HouseholdDayPlanTests.Store(request);
            Check.True(store.TryPublishHouseholdDay(plan,request.PlannedAt,out var reason),reason);
            motion=HouseholdJointMovement.Create(plan,request.PlannedAt);store.SaveHouseholdJointProgress(motion.Snapshot());
            if(depart){motion.Depart(Departure(plan),new SavedRoutePoint(),new SavedRoutePoint());store.SaveHouseholdJointProgress(motion.Snapshot());}
            return store;
        }
        private static double Departure(HouseholdDayPlan plan){return plan.AbsoluteDay*86400d+plan.JointInn.First.DepartureSeconds;}
        private static object Read(WorldStateStore store,string person,double now)
        {
            var method=typeof(WorldStateStore).GetMethod("ReadHouseholdOutingConversation");
            Check.True(method!=null,"Missing observer-bound shared outing conversation projection.");
            try{return method.Invoke(store,new object[]{person,now});}
            catch(TargetInvocationException e) when(e.InnerException!=null){throw e.InnerException;}
        }
        private static T Value<T>(object source,string property){return (T)source.GetType().GetProperty(property).GetValue(source);}
        private static ConversationContextSnapshot Snapshot(string person,double at,WorldStateStore store=null)
        {
            var binding=new ConversationContextBinding(ConversationActorCategory.Mobile,person,null,"shared-talk",1,1,
                "0123456789abcdef0123456789abcdef","aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","town",1);
            var unknown=ConversationContextFact<string>.Unknown();
            var personal=ConversationContextPersonalState.Unknown();
            var traits=unknown;var name=unknown;var clock=unknown;
            if(store!=null)
            {
                personal=new ConversationContextPersonalState(personal.Race,personal.Gender,personal.FamiliarWithPlayer,
                    personal.KnownPlayerName,personal.Grieving,personal.Alarmed,store.ReadNpcHouseholdKnowledge(person));
                traits=ConversationContextFact<string>.Verified(NpcPersonalityGenerator.Generate(person,771,NpcSocialBackground.Commoner).Description(),
                    ConversationContextFactSource.Derived,at);
                name=ConversationContextFact<string>.Verified(store.ResolveResident(person).FullName,ConversationContextFactSource.Save,at);
                clock=ConversationContextFact<string>.Verified("13:00, 7th Loredas, Morning Star, 405",ConversationContextFactSource.Save,at);
            }
            return new ConversationContextSnapshot(binding,at,clock,name,ConversationContextFact<ConversationDialogueRole>.Unknown(),
                unknown,unknown,ConversationContextFact<ConversationNpcPosition>.Unknown(),ConversationContextFact<ConversationNpcActivity>.Unknown(),
                ConversationContextFact<bool>.Unknown(),ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(),traits,0,1,null,
                Array.Empty<ConversationContextCommitment>(),Array.Empty<ConversationContextSourceHandle>(),personal,new ConversationContextMood(0,0));
        }
        private static ConversationContextSnapshot Attach(ConversationContextSnapshot snapshot,object outing)
        {
            var method=typeof(ConversationContextSnapshot).GetMethod("WithHouseholdOuting");
            Check.True(method!=null,"Missing captured shared outing snapshot attachment.");
            try{return (ConversationContextSnapshot)method.Invoke(snapshot,new[]{outing});}
            catch(TargetInvocationException e) when(e.InnerException!=null){throw e.InnerException;}
        }
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Shared outing context requires actual departure and exact participating speaker",()=>{
                var store=Prepared(out var plan,out var motion,false);double at=Departure(plan);
                Check.True(Read(store,"mother",at)==null,"Dated intent must not claim current company.");
                motion.Depart(at,new SavedRoutePoint(),new SavedRoutePoint());store.SaveHouseholdJointProgress(motion.Snapshot());
                Check.True(Read(store,"child",at)==null);Check.True(Read(store,"player",at)==null);
                Check.True(Read(store,"mother",at-1)==null);Check.True(Read(store,"mother",double.NaN)==null);
                var first=Read(store,"mother",at);var second=Read(store,"father",at);
                Check.Equal("father",Value<string>(first,"PartnerId"));Check.Equal("mother",Value<string>(second,"PartnerId"));
                Check.Equal("Father Example",Value<string>(first,"PartnerName"));Check.Equal("Mother Example",Value<string>(second,"PartnerName"));
                Check.Equal(10,Value<int>(first,"DestinationBuildingKey"));
            }));
            cases.Add(("Shared outing reaches every dialogue projection without adding the player to the group",()=>{
                var store=Prepared(out var plan,out var motion);double at=Departure(plan);
                foreach(string person in new[]{"mother","father"})
                {
                    var group=Read(store,person,at);var snapshot=Attach(Snapshot(person,at),group);
                    foreach(var projection in new Func<ConversationContextSnapshot,int,string>[] {
                        ConversationContextFormatter.ForRouting,ConversationContextFormatter.ForSemanticClassifier,
                        ConversationContextFormatter.ForSocialReply,ConversationContextFormatter.ForActionVoice})
                    {
                        string text=projection(snapshot,3072);
                        Check.True(text.Contains(Value<string>(group,"PartnerName")),"Projection lost the actual NPC companion.");
                        Check.True(text.Contains("player is not a group member"));
                        Check.True(text.Contains("outbound"));Check.True(!text.Contains("already bought"));
                    }
                }
            }));
            cases.Add(("Shared outing context is immutable and retained by disposition successors",()=>{
                var store=Prepared(out var plan,out var motion);double at=Departure(plan);
                var group=Read(store,"mother",at);var initial=Snapshot("mother",at);var snapshot=Attach(initial,group);
                foreach(var property in group.GetType().GetProperties())Check.True(!property.CanWrite,"Mutable group property: "+property.Name);
                Check.True(snapshot.IsSuccessorOf(initial));
                var successor=snapshot.WithCommittedDisposition(-10);
                Check.True(ConversationContextFormatter.ForSocialReply(successor,3072).Contains("Father Example"));
                Check.Throws<ArgumentException>(()=>Attach(Snapshot("father",at),group));
                Check.Throws<ArgumentException>(()=>Attach(Snapshot("mother",at+1),group));
            }));
            cases.Add(("Retired and unloaded owners cannot claim current physical company after reload",()=>{
                var store=Prepared(out var plan,out var motion);double at=Departure(plan);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.True(Read(restored,"mother",at)!=null);
                restored.BeginHouseholdJointSimulation(plan.Id,motion.Snapshot().Sequence,at);
                Check.True(Read(restored,"mother",at)==null,"Virtual source must not claim current physical group presence.");
                Check.True(store.TryRetireHouseholdJointAtHome(2,7,at+1,id=>true));
                Check.True(Read(store,"mother",at+1)==null);
            }));
            cases.Add(("Expired shared commitment is described as interrupted rather than fabricated ongoing leisure",()=>{
                var store=Prepared(out var plan,out var motion);double at=Departure(plan)+86400;
                var context=Read(store,"mother",at);
                Check.True(context!=null);Check.True(Value<bool>(context,"Interrupted"));
                string text=ConversationContextFormatter.ForSocialReply(Attach(Snapshot("mother",at),context),3072);
                Check.True(text.Contains("interrupted"));Check.True(text.Contains("not evidence of purchases"));
                Check.Near(0,store.ReadHouseholdJointProgress(2,7).First.Distance);
            }));
            cases.Add(("Changed pair progress invalidates captured ownership and dead partners fail closed",()=>{
                var store=Prepared(out var plan,out var motion);double at=Departure(plan);
                var before=(HouseholdOutingConversation)Read(store,"mother",at);
                motion.Observe(at+1,new SavedRoutePoint{X=.5},new SavedRoutePoint{X=.5});store.SaveHouseholdJointProgress(motion.Snapshot());
                Check.True(!before.HasSameOwnership((HouseholdOutingConversation)Read(store,"mother",at+1)));
                var saved=store.Snapshot();saved.Settlements[0].Households[0].Residents.Find(p=>p.Id=="father").IsAlive=false;
                store.Restore(saved);Check.True(Read(store,"mother",at+1)==null);
            }));
            cases.Add(("Observed group details stay detached and fit the actual classifier and voice budgets",()=>{
                var store=Prepared(out var plan,out var motion);double at=Departure(plan);
                var group=(HouseholdOutingConversation)Read(store,"mother",at);
                var observed=group.WithObservation("The Native Inn",true);
                Check.True(group.DestinationName==null && !group.PartnerObservedNearby);
                var snapshot=Attach(Snapshot("mother",at,store),observed);
                Check.True(NpcMotivationConversationProjection.TryCapture(snapshot,
                    NpcPersonalityGenerator.Generate("mother",771,NpcSocialBackground.Commoner),store,out var motivation));
                snapshot=snapshot.WithMotivationContext(motivation);
                string voice=ConversationContextFormatter.ForActionVoice(snapshot,2400);
                string classifier=ConversationContextFormatter.ForSemanticClassifier(snapshot,2048);
                string social=ConversationContextFormatter.ForSocialReply(snapshot,3072);
                Check.True(voice.Contains("The Native Inn") && voice.Contains("Father Example"));
                Check.True(classifier.Contains("Partner observed nearby"));
                Check.True(voice.Length<=2400 && classifier.Length<=2048);
                Check.True(social.Length<=3072 && social.Contains("Father Example") && social.Contains("Saved self-knowledge"));
            }));
            cases.Add(("Pair context distinguishes outside arrival, native entry, homeward destination and completion",()=>{
                var store=Prepared(out var plan,out var motion);double at=Departure(plan);
                for(int i=1;i<=20;i++)
                {motion.Observe(at+i,new SavedRoutePoint{X=i*.5},new SavedRoutePoint{X=i*.5});store.SaveHouseholdJointProgress(motion.Snapshot());}
                var arrival=(HouseholdOutingConversation)Read(store,"mother",at+20);
                Check.True(arrival.ToContext().Contains("entry is not confirmed"));
                motion.EnterInn(at+21);store.SaveHouseholdJointProgress(motion.Snapshot());
                var visiting=(HouseholdOutingConversation)Read(store,"father",at+21);
                Check.Equal(HouseholdJointPhase.Visiting,visiting.Phase);
                Check.True(visiting.ToContext().Contains("entry is not confirmed"));
                motion.BeginReturn(at+22,new SavedRoutePoint{X=10},new SavedRoutePoint{X=10});store.SaveHouseholdJointProgress(motion.Snapshot());
                var returning=(HouseholdOutingConversation)Read(store,"mother",at+22);
                Check.Equal(7,returning.DestinationBuildingKey);Check.True(returning.ToContext().Contains("shared home"));
                for(int i=1;i<=20;i++)
                {motion.Observe(at+22+i,new SavedRoutePoint{X=10-i*.5},new SavedRoutePoint{X=10-i*.5});store.SaveHouseholdJointProgress(motion.Snapshot());}
                Check.True(Read(store,"mother",at+43)==null);
            }));
        }
    }
}
