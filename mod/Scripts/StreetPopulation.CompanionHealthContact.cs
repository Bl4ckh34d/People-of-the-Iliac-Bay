using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.MagicAndEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Follow's arrival radius is 1.8 m; native mobile activation permits
        // 6.4 m. Leave room for the standing actor/player height offset.
        internal const float CompanionHealthTalkDistance=2.5f;
        // An accepted Follow action or an active player-paid Follow contract is
        // the existing player binding. A nearby unrelated resident never qualifies.
        internal bool TryObservedCompanionHealthCondition(ResidentActor actor,double now,out string condition)
        {
            condition=null;
            if(worldStore==null || !actor || actor.Resident==null || !actor.gameObject.activeInHierarchy ||
                !actor.Resident.IsAlive || actor.Resident.AgeGroup!=ResidentAgeGroup.Adult ||
                actor.Resident.IsQuestProtected || QuestOwned(actor.Resident) || actor.IsSleeping ||
                IsAlarmed(actor.Resident.Id) || worldStore.IsResidentAway(actor.Resident.Id,now))return false;
            var game=GameManager.Instance;
            if(game==null || !game.IsReady || game.PlayerEnterExit==null || game.PlayerEnterExit.IsPlayerInsideDungeon ||
                game.PlayerEntity==null || game.PlayerMotor==null || game.AreEnemiesNearby(false,false))return false;
            bool sameScene=interior ? actor.transform.parent==interior.transform && game.PlayerEnterExit.IsPlayerInsideBuilding :
                directory && actor.transform.parent==directory.transform && !game.PlayerEnterExit.IsPlayerInsideBuilding;
            if(!sameScene)return false;

            var action=worldStore.ActiveNpcAction(actor.Resident.Id);
            bool following=action!=null && !NpcActionExecutor.IsTerminal(action) &&
                action.Kind==NpcActionKind.Follow && action.Target!=null &&
                action.Target.Kind==NpcActionTargetKind.Person &&
                action.Target.PersonId==NpcDirectedRelationship.PlayerTargetId;
            if(action!=null && !following)return false;
            if(!following)
            {
                if(worldStore.ActiveNpcTask(actor.Resident.Id)!=null)return false;
                var ticket=worldStore.CaptureNpcServiceContractWriteTicket();
                foreach(var contract in ticket.SourceLedger.Contracts)
                    if(contract.NpcId==actor.Resident.Id &&
                        contract.PayerId==NpcDirectedRelationship.PlayerTargetId &&
                        contract.Status==NpcServiceContractStatus.Active && now>=contract.BlockStartAt &&
                        now<contract.BlockEndAt && (contract.Coverage & NpcServiceCoverage.Follow)!=0)
                    {following=true;break;}
                if(!following)return false;
            }

            var effects=actor.GetComponent<EntityEffectManager>();
            var inventory=actor.GetComponent<NativeNpcInventoryOwner>();
            if(!effects || !inventory || inventory.Binding==null)return false;
            condition=effects.PoisonCount>0?"poison":effects.DiseaseCount>0?"disease":null;
            if(condition==null || NativeNpcConditionTreatment.HasOwnTreatment(actor,worldStore,()=>
                actor && actor.gameObject.activeInHierarchy))
            {condition=null;return false;}
            return true;
        }

        private void ObserveCompanionHealthContacts(double now)
        {
            var game=GameManager.Instance;
            if(worldStore==null || game==null || GameManager.IsGamePaused || !game.IsPlayerOnHUD ||
                game.PlayerEntity.IsInvisible || game.PlayerEntity.IsMagicallyConcealed ||
                worldStore.IsNpcProactiveContactSuppressed(now))return;
            var host=GetComponent<PeopleOfTheIliacBayMod>();
            if(!host)return;
            Vector3 player=game.PlayerMotor.transform.position;
            foreach(var pair in actors)
            {
                var actor=pair.Value;
                if(!actor || actor.IsConversing ||
                    Vector3.Distance(actor.transform.position,player)>CompanionHealthTalkDistance ||
                    !CivilianResponses.Sees(actor.transform.position+Vector3.up,player+Vector3.up,
                        CompanionHealthTalkDistance))continue;
                if(!TryObservedCompanionHealthCondition(actor,now,out string condition))continue;
                if(!worldStore.TryGetCompanionHealthContactOffer(pair.Key,condition,now,out var offer))continue;
                if(offer.NoticedAt<0)
                {
                    if(!worldStore.TryNoticeProactiveContactPlayer(offer,now,now+120))continue;
                }
                host.TryInitiateProactiveContactConversation(pair.Key,offer.ContactId);
                break;
            }
        }
    }
}
