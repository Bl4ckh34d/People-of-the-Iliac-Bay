using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable] public sealed class NpcCashVisit
    {
        public int ProviderKey,ReturnKey;
        public bool Bank,Returning,WaitingForBank;
        public double ServiceAt;
        public NpcActionTarget Position;
        public ObservedVisit CounterJourney;
        public ObservedVisit OutsideJourney;
        public int ClerkHash;
        public string WaitingPartnerId;
        public string PlannedHouseholdDayId;
        public int RequiredGold;
        public NpcActionTarget Rendezvous,PartnerWaitPosition;

        public static ObservedVisit CreateCounterJourney(string id,int settlement,int building,double now,
            IEnumerable<GuardPoint3> entryToClerk,double secondsPerMetre,double timeScale,int dwellSeconds)
        {
            if(dwellSeconds<8 || dwellSeconds>15 || double.IsNaN(timeScale) || double.IsInfinity(timeScale) || timeScale<=0)
                throw new ArgumentOutOfRangeException(nameof(dwellSeconds));
            var route=new List<GuardPoint3>(entryToClerk);
            if(route.Count<2)throw new ArgumentException("Bank clerk requires a reachable route.");
            var returning=new List<GuardPoint3>(route);returning.Reverse();
            var visit=new ObservedVisit {ResidentId=id,SettlementId=settlement,BuildingKey=building,ObservedAt=now,
                SecondsPerMetre=secondsPerMetre,Legs=new List<ObservedVisitLeg>{
                    new ObservedVisitLeg {Route=route,DwellSeconds=dwellSeconds*timeScale,AfterPhase=ShopVisitPhase.Haggling},
                    new ObservedVisitLeg {Route=returning,AfterPhase=ShopVisitPhase.Complete}}};
            visit.PlannedEndAt=visit.ResumeUntil=visit.EndsAt;
            return ObservedVisit.Copy(visit);
        }

        public static bool CounterServiceFinished(ObservedVisit journey,double now)
        {
            if(journey==null || now<journey.ObservedAt)return false;
            var phase=journey.Evaluate(now).Phase;
            return phase==ShopVisitPhase.Leaving || phase==ShopVisitPhase.Complete;
        }
    }

    public sealed partial class WorldStateStore
    {
        public NpcCashVisit ReadNpcCashVisit(string id)
        {var progress=IndexedNpcProgress(id);return progress==null?null:CopyCashVisit(progress.CashVisit);}

        public NpcCashVisit ReadNpcCashWait(string id)
        {
            var person=ResolveResident(id);if(person==null || person.PartnerId==null)return null;
            var visit=ReadNpcCashVisit(person.PartnerId);
            return visit!=null && visit.WaitingPartnerId==id?visit:null;
        }

        public bool NeedsNpcCashErrand(string id,double now)
        {
            var funding=ReadPlannedHouseholdInnFunding(id,now);
            if(funding!=null)
            {
                if(id!=funding.PayerId)return false;
                var wallet=IndexedNpcInventory(id);
                return wallet!=null && wallet.WalletGold<funding.RequiredGold;
            }
            if(!HasEmptyNpcWallet(id))return false;
            var cursor=IndexedInnConsumptionCursor(id);
            if(cursor!=null && cursor.Receipt.Outcome==NpcInnConsumptionOutcome.Purchased)
            {
                if(cursor.HouseholdDayId!=null && state.HouseholdJointProgress.Exists(p=>p.Active && p.HouseholdDayId==cursor.HouseholdDayId &&
                    (p.FirstResidentId==id || p.SecondResidentId==id)))return false;
                var visit=IndexedObservedVisit(id);
                if(visit!=null && now>=visit.ObservedAt && now<visit.EndsAt &&
                    cursor.Receipt.VisitId==NpcInnConsumption.VisitId(visit))return false;
            }
            return true;
        }

        public bool SaveNpcCashVisit(object epoch,string id,NpcCashVisit visit)
        {
            if(!IsNpcVitalsWriteEpochCurrent(epoch))return false;
            var current=IndexedNpcProgress(id);bool alive;
            if(!KnownNpcIdentity(state,id,out alive) || !alive)return false;
            if(current==null && visit==null)return true;
            ValidateCashVisit(visit,id);
            if(visit!=null && visit.WaitingPartnerId!=null)
                Require(ResolveResident(id)?.PartnerId==visit.WaitingPartnerId,"Cash errand waiting partner does not match the resident.");
            var copy=current==null?new NpcActorProgress {PersonId=id,Archetype=ResolveNpcArchetype(id)}:CopyNpcProgress(current);
            if(!copy.VitalsInitialized)
            {
                var baseline=ReadNpcVitals(id,copy.Archetype);
                copy.VitalsInitialized=true;copy.CurrentHealth=baseline.CurrentHealth;copy.CurrentMagicka=baseline.CurrentMagicka;
                copy.VitalsRevision=1;
            }
            copy.CashVisit=CopyCashVisit(visit);
            if(current==null)state.NpcActors.Add(copy);
            else state.NpcActors[state.NpcActors.IndexOf(current)]=copy;
            npcActorIndex[id]=copy;return true;
        }

        public bool TryCompleteNpcCashVisit(object epoch,string id,long inventoryRevision,int providerKey,int gold,double now)
        {
            if(!IsNpcVitalsWriteEpochCurrent(epoch) || !Finite(now) || now<0 || gold<1 || gold>NpcInventoryLimits.MaximumWalletGold)return false;
            var current=IndexedNpcProgress(id);var visit=current==null?null:current.CashVisit;
            var inventory=ReadNpcInventory(id);bool alive;
            if(visit==null || visit.Returning || visit.WaitingForBank || visit.ServiceAt<=0 || now<visit.ServiceAt || visit.ProviderKey!=providerKey ||
                visit.Bank && !NpcCashVisit.CounterServiceFinished(visit.CounterJourney,now) ||
                inventory==null || inventory.Revision!=inventoryRevision ||
                (visit.PlannedHouseholdDayId==null?inventory.WalletGold!=0:inventory.WalletGold>=visit.RequiredGold) ||
                gold>NpcInventoryLimits.MaximumWalletGold-inventory.WalletGold ||
                visit.PlannedHouseholdDayId!=null && inventory.WalletGold+gold<visit.RequiredGold ||
                !KnownNpcIdentity(state,id,out alive) || !alive)return false;
            var copy=CopyNpcProgress(current);copy.CashVisit.Returning=true;copy.CashVisit.ServiceAt=0;
            if(!CommitNpcInventoryTransaction("cash-visit:"+inventoryRevision,new[]{new NpcInventoryUpdate {
                PersonId=id,ExpectedRevision=inventoryRevision,WalletGold=inventory.WalletGold+gold,Items=inventory.Items}}))return false;
            state.NpcActors[state.NpcActors.IndexOf(current)]=copy;npcActorIndex[id]=copy;
            RememberSceneEvent(id,"cash-visit:"+inventoryRevision,NpcSceneEventKind.CashVisit,now,
                visit.Position.SettlementId,providerKey,(visit.PlannedHouseholdDayId==null?"I had run out of spending money, so I collected ":
                "I needed enough money to pay for my partner and me at our planned Inn visit, so I collected ")+gold+
                " gold "+(visit.Bank?"from the bank":"at home")+" before continuing my plans.",1,id);
            return true;
        }

        private static void ValidateCashVisit(NpcCashVisit visit,string id)
        {
            if(visit==null)return;
            var point=visit.Position;
            Require(visit.PlannedHouseholdDayId==null?visit.RequiredGold==0:
                visit.PlannedHouseholdDayId.Length<=256 && visit.RequiredGold>=1 && visit.RequiredGold<=510,
                "Invalid planned shared Inn cash requirement.");
            Require(visit.ProviderKey>=0 && visit.ReturnKey>=0 && Finite(visit.ServiceAt) && visit.ServiceAt>=0 &&
                point!=null && (point.Kind==NpcActionTargetKind.WorldPoint || point.Kind==NpcActionTargetKind.IndoorPoint) &&
                Finite(point.X) && Finite(point.Y) && Finite(point.Z) && point.PersonId==null && point.ItemId==null &&
                point.ContainerId==null,"Invalid NPC cash visit.");
            if(visit.CounterJourney!=null)
            {
                var route=ObservedVisit.Copy(visit.CounterJourney);
                Require(visit.Bank && !visit.WaitingForBank && route.BuildingKey==visit.ProviderKey &&
                    route.ResidentId==id && route.SettlementId==point.SettlementId,"Invalid bank counter visit.");
            }
            if(visit.OutsideJourney!=null)
            {var route=ObservedVisit.Copy(visit.OutsideJourney);Require(route.ResidentId==id && route.SettlementId==point.SettlementId,"Invalid cash travel owner.");}
            Require(visit.WaitingPartnerId==null?visit.Rendezvous==null && visit.PartnerWaitPosition==null:
                !string.IsNullOrWhiteSpace(visit.WaitingPartnerId) && visit.WaitingPartnerId.Length<=240 &&
                visit.Rendezvous!=null && visit.PartnerWaitPosition!=null,"Invalid cash errand rendezvous.");
            foreach(var destination in new[]{visit.Rendezvous,visit.PartnerWaitPosition})
                if(destination!=null)Require((destination.Kind==NpcActionTargetKind.WorldPoint || destination.Kind==NpcActionTargetKind.IndoorPoint) &&
                    destination.SettlementId==point.SettlementId && Finite(destination.X) && Finite(destination.Y) && Finite(destination.Z),
                    "Invalid cash rendezvous position.");
        }

        private static NpcCashVisit CopyCashVisit(NpcCashVisit visit)
        {return visit==null?null:new NpcCashVisit {ProviderKey=visit.ProviderKey,ReturnKey=visit.ReturnKey,Bank=visit.Bank,
            Returning=visit.Returning,WaitingForBank=visit.WaitingForBank,ServiceAt=visit.ServiceAt,
            PlannedHouseholdDayId=visit.PlannedHouseholdDayId,RequiredGold=visit.RequiredGold,
            ClerkHash=visit.ClerkHash,CounterJourney=visit.CounterJourney==null?null:ObservedVisit.Copy(visit.CounterJourney),
            OutsideJourney=visit.OutsideJourney==null?null:ObservedVisit.Copy(visit.OutsideJourney),
            WaitingPartnerId=visit.WaitingPartnerId,Rendezvous=NpcActionExecutor.CopyTarget(visit.Rendezvous),
            PartnerWaitPosition=NpcActionExecutor.CopyTarget(visit.PartnerWaitPosition),
            Position=NpcActionExecutor.CopyTarget(visit.Position)};}
    }
}
