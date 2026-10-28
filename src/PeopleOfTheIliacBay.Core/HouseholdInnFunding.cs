using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class HouseholdInnBudget
    {
        public static int GroupReserve(NpcInnServiceCatalog catalog)
        {
            var source=NpcInnServiceCatalog.Copy(catalog);int maximum=1;
            foreach(var item in source.Menu)maximum=Math.Max(maximum,item.BasePriceGold);
            // Native holiday 37 can only reduce prices. No future menu/tariff is selected.
            return maximum*2;
        }
    }
    public sealed class HouseholdInnFunding
    {
        public string HouseholdDayId,PayerId,PartnerId;
        public int InnKey,RequiredGold;
        public double DepartureAt;
    }

    public sealed partial class WorldStateStore
    {
        public HouseholdInnFunding ReadPlannedHouseholdInnFunding(string id,double now)
        {
            if(!ValidTime(now))return null;
            int day=(int)Math.Floor(now/86400d);
            var plan=state.HouseholdDays.Find(p=>p.AbsoluteDay==day && p.Status=="planned" && !p.JointSuppressed &&
                p.JointInn!=null && (p.First.ResidentId==id || p.Second.ResidentId==id));
            if(plan==null)return null;
            double departure=day*86400d+Math.Max(plan.JointInn.First.DepartureSeconds,plan.JointInn.Second.DepartureSeconds);
            if(now>=departure && !state.HouseholdJointProgress.Exists(p=>p.HouseholdDayId==plan.Id && p.Active))return null;
            int unpaid=HouseholdUnpaidParticipants(plan);
            int reserve=plan.JointInn.ReserveGold;
            if(reserve==0)
            {
                var catalog=ReadNpcInnServiceCatalog(plan.SettlementId,plan.JointInn.InnKey);
                if(catalog==null)return null;
                reserve=HouseholdInnBudget.GroupReserve(catalog);
            }
            string payer=HouseholdPayer(plan);
            return new HouseholdInnFunding {HouseholdDayId=plan.Id,PayerId=payer,
                PartnerId=payer==plan.First.ResidentId?plan.Second.ResidentId:plan.First.ResidentId,
                InnKey=plan.JointInn.InnKey,RequiredGold=(reserve+1)/2*unpaid,DepartureAt=departure};
        }
        private bool HouseholdMemberPaid(HouseholdDayPlan plan,string buyer)
        {
            var cursor=IndexedInnConsumptionCursor(buyer);
            return cursor?.HouseholdDayId==plan.Id && cursor.Receipt.Outcome==NpcInnConsumptionOutcome.Purchased;
        }
        private int HouseholdUnpaidParticipants(HouseholdDayPlan plan)
        {return (HouseholdMemberPaid(plan,plan.First.ResidentId)?0:1)+(HouseholdMemberPaid(plan,plan.Second.ResidentId)?0:1);}
        private string HouseholdPayer(HouseholdDayPlan plan)
        {
            if(plan.JointInn.PayerId!=null)return plan.JointInn.PayerId;
            // An unassigned group retains its settled payer or chooses one wallet for everyone.
            foreach(string buyer in new[]{plan.First.ResidentId,plan.Second.ResidentId})
            {var cursor=IndexedInnConsumptionCursor(buyer);if(cursor?.HouseholdDayId==plan.Id && cursor.Receipt.PayerId!=null)return cursor.Receipt.PayerId;}
            int first=IndexedNpcInventory(plan.First.ResidentId)?.WalletGold??0,second=IndexedNpcInventory(plan.Second.ResidentId)?.WalletGold??0;
            return first>=second?plan.First.ResidentId:plan.Second.ResidentId;
        }
        private HouseholdDayPlan FundedHouseholdInn(string dayId,string buyer)
        {
            if(dayId==null)return null;
            return state.HouseholdDays.Find(p=>p.Id==dayId && p.Status=="planned" && !p.JointSuppressed &&
                p.JointInn!=null && (p.First.ResidentId==buyer || p.Second.ResidentId==buyer));
        }
        public int HouseholdInnCounterBudget(string dayId,string buyer,int individualWalletGold)
        {
            var plan=FundedHouseholdInn(dayId,buyer);
            if(plan==null)return individualWalletGold;
            var wallet=IndexedNpcInventory(HouseholdPayer(plan));
            return wallet==null?0:wallet.WalletGold/Math.Max(1,HouseholdUnpaidParticipants(plan));
        }
    }
}
