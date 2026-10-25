using System;
using System.Globalization;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        internal const string CompanionHealthHandoffReasonPrefix="COMPANION_HEALTH_REMEDY_RECEIVED: ";
        private const double CompanionHealthHandoffWindowSeconds=600;

        private bool TryCurrentCompanionHealthRequest(ResidentActor actor,double now,
            out string condition,out string contactId,out string followId)
        {
            condition=null;contactId=null;followId=null;
            if(!TryObservedCompanionHealthCondition(actor,now,out condition))return false;
            var follow=worldStore.ActiveNpcAction(actor.Resident.Id);
            if(follow==null || NpcActionExecutor.IsTerminal(follow) || follow.Kind!=NpcActionKind.Follow ||
                follow.Target==null || follow.Target.Kind!=NpcActionTargetKind.Person ||
                follow.Target.PersonId!=NpcDirectedRelationship.PlayerTargetId)return false;
            var snapshot=worldStore.Snapshot();
            if(string.IsNullOrEmpty(snapshot.LastProactiveContactId))return false;
            var receipt=snapshot.RecentProactiveContacts.Find(item=>item.ContactId==snapshot.LastProactiveContactId);
            if(receipt==null || receipt.Kind!=ProactiveContactKind.CompanionHealthRequest ||
                receipt.ResidentId!=actor.Resident.Id || receipt.Purpose!=condition || receipt.OpenedAt<0 ||
                receipt.OpenedAt>now || now-receipt.OpenedAt>CompanionHealthHandoffWindowSeconds ||
                snapshot.LastProactiveContactAt!=receipt.OpenedAt)return false;
            contactId=receipt.ContactId;followId=follow.Id;return true;
        }

        private static string CompanionCureEffect(string condition)
        {return condition=="poison"?CurePoison.EffectKey:condition=="disease"?CureDisease.EffectKey:null;}

        private bool IsCompanionHealthRemedy(ResidentActor actor,DaggerfallUnityItem item,double now)
        {
            if(!TransferableActionItem(item) || !TryCurrentCompanionHealthRequest(actor,now,out string condition,
                out _,out _))return false;
            string effect=CompanionCureEffect(condition);
            return effect!=null && NativeNpcConditionTreatment.MatchesPotion(item,effect);
        }

        private bool IsCompanionHealthHandoff(ResidentActor actor,NpcActionJob job,double now)
        {
            if(job==null || job.Kind!=NpcActionKind.Transfer || job.Target==null ||
                job.Target.Kind!=NpcActionTargetKind.Person ||
                job.Target.TransferDirection!=NpcActionTransferDirection.PlayerToNpc ||
                job.Target.PersonId!=NpcDirectedRelationship.PlayerTargetId || job.Target.Quantity!=1 ||
                !ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out ulong uid))return false;
            var player=GameManager.Instance==null?null:GameManager.Instance.PlayerEntity;
            return player!=null && player.Items!=null && IsCompanionHealthRemedy(actor,player.Items.GetItem(uid),now);
        }

        // Called only for an already selected Transfer option and a live typed
        // conversation turn. The inventory bridge commits one exact native unit;
        // the existing condition-care tick drinks it after Talk closes.
        private bool TryCompleteCompanionHealthHandoff(ResidentActor actor,NpcActionJob job,
            NpcConversationCommandTurn turn,double now,out string reason)
        {
            reason="The offered remedy or companion request is no longer current.";
            if(turn==null || !IsCompanionHealthHandoff(actor,job,now) ||
                !TryCurrentCompanionHealthRequest(actor,now,out string condition,
                    out string contactId,out string followId))return false;
            var game=GameManager.Instance;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            if(!owner || owner.Binding==null || owner.Binding.PersonId!=actor.Resident.Id ||
                !body || body.Entity==null ||
                game.TalkManager.CurrentNPCType!=TalkManager.NPCType.Mobile ||
                game.TalkManager.MobileNPC!=actor.Person ||
                !(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow) ||
                Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>CompanionHealthTalkDistance ||
                !CivilianResponses.Sees(actor.transform.position+Vector3.up,
                    game.PlayerMotor.transform.position+Vector3.up,CompanionHealthTalkDistance))return false;
            if(!worldStore.TryPrepareNpcConversationAction(turn,job,false,out var prepared) ||
                !worldStore.TryIssueNpcConversationAction(turn,prepared,out var capability) || capability==null)
            {reason="The selected player item is no longer bound to this conversation turn.";return false;}
            try
            {
                ulong uid=ulong.Parse(job.Target.ItemId,CultureInfo.InvariantCulture);
                string transactionId=prepared.Id+":companion-health-handoff";
                var playerItems=game.PlayerEntity.Items;
                int before=CountOwnedCompanionRemedy(owner.Binding.Items,condition);
                bool received=NativeNpcInventoryBridge.TryReceiveFromExternal(worldStore,playerItems,owner.Binding,
                    uid,1,()=>{
                        if(!actor || !actor.gameObject.activeInHierarchy ||
                            game.TalkManager.CurrentNPCType!=TalkManager.NPCType.Mobile ||
                            game.TalkManager.MobileNPC!=actor.Person ||
                            Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>CompanionHealthTalkDistance ||
                            !CivilianResponses.Sees(actor.transform.position+Vector3.up,
                                game.PlayerMotor.transform.position+Vector3.up,CompanionHealthTalkDistance) ||
                            !TryCurrentCompanionHealthRequest(actor,ActionNow(),out string current,
                                out string currentContact,out string currentFollow) ||
                            current!=condition || currentContact!=contactId || currentFollow!=followId)return false;
                        var item=playerItems.GetItem(uid);
                        return item!=null && NativeNpcConditionTreatment.MatchesPotion(item,CompanionCureEffect(condition)) &&
                            body.Entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()<=body.Entity.MaxEncumbrance;
                    },transactionId,out reason);
                if(!received)return false;
                var saved=worldStore.ReadNpcInventory(actor.Resident.Id);
                if(saved==null || saved.LastTransactionId!=transactionId ||
                    CountOwnedCompanionRemedy(owner.Binding.Items,condition)!=before+1)
                {reason="The native item transfer completed but its owned cure receipt could not be verified.";return false;}
                reason=CompanionHealthHandoffReasonPrefix+"one exact owned "+condition+" remedy was received from the player. " +
                    "I can try it after this conversation; no cure has been claimed yet.";
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_HEALTH_HANDOFF: resident="+actor.Resident.Id+
                    "; contact="+contactId+"; follow="+followId+"; itemUid="+uid+"; quantity=1; transaction="+transactionId);
                return true;
            }
            finally {worldStore.TryCloseNpcConversationCommandTurn(turn);}
        }

        private static int CountOwnedCompanionRemedy(ItemCollection items,string condition)
        {
            int count=0;string effect=CompanionCureEffect(condition);
            if(items==null || effect==null)return 0;
            for(int i=0;i<items.Count;i++)
            {
                var item=items.GetItem(i);
                if(NativeNpcConditionTreatment.MatchesPotion(item,effect))count+=item.stackCount;
            }
            return count;
        }
    }
}
