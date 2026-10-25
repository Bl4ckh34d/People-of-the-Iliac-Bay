using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class KnowledgeBrowsingProbe
    {
        // Uses the existing owned native fixture, real SubmitFreeText and real
        // inventory collections. Only player observation and stock are staged.
        private static IEnumerator VerifyLocalActions(PeopleOfTheIliacBayMod host,StreetPopulation streets,
            WorldStateStore store,ResidentActor actor,DaggerfallTalkWindow window,MethodInfo submit)
        {
            var game=GameManager.Instance;string person=actor.Resident.Id;
            var transcript=typeof(DaggerfallTalkWindow).GetField("listboxConversation",Private).GetValue(window) as ListBox;
            var held=actor.transform.position;
            yield return Ask(host,window,submit,transcript,"Please wait here for 10 seconds.",answer=>{});
            var wait=RequireLocalAction(store,person,NpcActionKind.Wait);
            if(Math.Abs(wait.FinishAt-wait.AcceptedAt-10)>0.001 || Vector3.Distance(held,actor.transform.position)>0.05f)
                throw new InvalidOperationException("Native chat changed the requested wait duration or moved during Talk.");
            yield return ConversationMoodProbe.CloseTalk(host);
            float until=Time.realtimeSinceStartup+25;
            while(!NpcActionExecutor.IsTerminal(store.ReadNpcAction(wait.Id)) && Time.realtimeSinceStartup<until)
            {
                if(Vector3.Distance(held,actor.transform.position)>0.2f)
                    throw new InvalidOperationException("Waiting NPC left the agreed position.");
                yield return null;
            }
            if(store.ReadNpcAction(wait.Id).Phase!=NpcActionPhase.Completed)
                throw new InvalidOperationException("Native timed Wait did not complete.");
            yield return OpenFriendlyLocalTalk(host,store,actor,value=>window=value);
            transcript=typeof(DaggerfallTalkWindow).GetField("listboxConversation",Private).GetValue(window) as ListBox;
            yield return Ask(host,window,submit,transcript,"Please follow me for 60 seconds.",answer=>{});
            var follow=RequireLocalAction(store,person,NpcActionKind.Follow);
            yield return ConversationMoodProbe.CloseTalk(host);
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry=typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets) as TownGeometry;
            var origin=actor.transform.position;var local=directory.transform.InverseTransformPoint(origin-Vector3.up);
            bool observer=false;
            for(int bearing=0;bearing<16 && !observer;bearing++)
            {
                double angle=bearing*Math.PI/8;
                var point=new SavedRoutePoint{X=local.x+Math.Cos(angle)*4.5,Z=local.z+Math.Sin(angle)*4.5};
                var route=geometry.RouteBetweenPoints(new SavedRoutePoint{X=local.x,Z=local.z},point);
                if(route==null || route.Count<2)continue;
                game.PlayerMotor.transform.position=geometry.ScenePoint(new RoutePoint(point.X,point.Z))+Vector3.up;
                Physics.SyncTransforms();observer=true;
            }
            if(!observer)throw new InvalidOperationException("No connected follow observer point.");
            until=Time.realtimeSinceStartup+20;
            while(Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>1.85f && Time.realtimeSinceStartup<until)
            {FaceLeadForTest(game,actor);yield return null;}
            if(Vector3.Distance(origin,actor.transform.position)<0.5f ||
                Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>1.85f)
                throw new InvalidOperationException("Accepted Follow did not physically reach the moved player.");
            yield return OpenFriendlyLocalTalk(host,store,actor,value=>window=value);
            transcript=typeof(DaggerfallTalkWindow).GetField("listboxConversation",Private).GetValue(window) as ListBox;
            yield return Ask(host,window,submit,transcript,"Stop following me.",answer=>{});
            if(store.ReadNpcAction(follow.Id).Phase!=NpcActionPhase.Cancelled)
                throw new InvalidOperationException("Native conversation did not stop the accepted Follow.");

            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            if(!owner || owner.Binding==null)throw new InvalidOperationException("No native NPC inventory.");
            var arrows=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);arrows.stackCount=3;
            owner.Binding.Items.AddItem(arrows);owner.Capture();
            int playerBefore=CountLocalArrows(game.PlayerEntity.Items),npcBefore=CountLocalArrows(owner.Binding.Items);
            yield return Ask(host,window,submit,transcript,"Could you give me one arrow?",answer=>{});
            var gift=RequireLocalAction(store,person,NpcActionKind.Transfer);
            if(gift.Target.Quantity!=1 || gift.Target.TransferDirection!=NpcActionTransferDirection.NpcToCounterparty || gift.Target.PersonId!="player")
                throw new InvalidOperationException("Native chat did not preserve the requested one-arrow gift direction.");
            yield return ConversationMoodProbe.CloseTalk(host);
            until=Time.realtimeSinceStartup+25;
            while(!NpcActionExecutor.IsTerminal(store.ReadNpcAction(gift.Id)) && Time.realtimeSinceStartup<until)yield return null;
            if(store.ReadNpcAction(gift.Id).Phase!=NpcActionPhase.Completed ||
                CountLocalArrows(game.PlayerEntity.Items)!=playerBefore+1 || CountLocalArrows(owner.Binding.Items)!=npcBefore-1)
                throw new InvalidOperationException("Native gift did not move exactly one actual arrow.");
            yield return OpenFriendlyLocalTalk(host,store,actor,value=>window=value);
            transcript=typeof(DaggerfallTalkWindow).GetField("listboxConversation",Private).GetValue(window) as ListBox;
            yield return Ask(host,window,submit,transcript,"Take one arrow from me, as a gift.",answer=>{});
            var receive=RequireLocalAction(store,person,NpcActionKind.Transfer);
            if(receive.Target.Quantity!=1 || receive.Target.TransferDirection!=NpcActionTransferDirection.PlayerToNpc)
                throw new InvalidOperationException("Native chat did not preserve the incoming gift direction.");
            yield return ConversationMoodProbe.CloseTalk(host);
            until=Time.realtimeSinceStartup+25;
            while(!NpcActionExecutor.IsTerminal(store.ReadNpcAction(receive.Id)) && Time.realtimeSinceStartup<until)yield return null;
            if(store.ReadNpcAction(receive.Id).Phase!=NpcActionPhase.Completed ||
                CountLocalArrows(game.PlayerEntity.Items)!=playerBefore || CountLocalArrows(owner.Binding.Items)!=npcBefore)
                throw new InvalidOperationException("Native incoming gift did not restore the exact actual item counts.");
            yield return OpenFriendlyLocalTalk(host,store,actor,value=>window=value);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LOCAL_ACTIONS_PASS: nativeChat=true; waitSeconds=10; followWalk=true; stop=true; outgoingGift=1; incomingGift=1; actualInventory=true.");
        }

        private static NpcActionJob RequireLocalAction(WorldStateStore store,string person,NpcActionKind kind)
        {
            var action=store.ActiveNpcAction(person);
            if(action==null || action.Kind!=kind || action.Phase!=NpcActionPhase.Accepted)
                throw new InvalidOperationException("Native conversation did not accept "+kind+": "+
                    (action==null?"missing":action.Kind+" "+action.Phase+" "+action.FailureReason));
            return action;
        }

        private static IEnumerator OpenFriendlyLocalTalk(PeopleOfTheIliacBayMod host,WorldStateStore store,
            ResidentActor actor,Action<DaggerfallTalkWindow> opened)
        {
            yield return ConversationMoodProbe.OpenTalk(host,GameManager.Instance,actor,opened);
            // Native Talk rolls a fresh reaction on reopening. Keep this owned
            // willingness fixture friendly while retaining all earned offsets.
            store.SetConversationBaseline(actor.Resident.Id,95,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            typeof(PeopleOfTheIliacBayMod).GetField("conversationNativeDisposition",Private).SetValue(host,95);
            typeof(TalkManager).GetField("reactionToPlayer",Private).SetValue(GameManager.Instance.TalkManager,
                ConversationEffects.Reaction(95,store.ConversationDisposition(actor.Resident.Id)));
        }

        private static int CountLocalArrows(ItemCollection items)
        {
            int count=0;
            for(int i=0;i<items.Count;i++)
            {var item=items.GetItem(i);if(item.ItemGroup==ItemGroups.Weapons && item.TemplateIndex==(int)Weapons.Arrow)count+=item.stackCount;}
            return count;
        }
    }
}
