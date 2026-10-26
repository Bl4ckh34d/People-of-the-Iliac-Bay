using System;

namespace PeopleOfTheIliacBay.Core
{
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
                p.JointInn?.PayerId!=null && (p.First.ResidentId==id || p.Second.ResidentId==id));
            if(plan==null)return null;
            double departure=day*86400d+Math.Max(plan.JointInn.First.DepartureSeconds,plan.JointInn.Second.DepartureSeconds);
            if(now>=departure && !state.HouseholdJointProgress.Exists(p=>p.HouseholdDayId==plan.Id && p.Active))return null;
            int required=0;
            foreach(string buyer in new[]{plan.First.ResidentId,plan.Second.ResidentId})
            {
                var receipt=IndexedInnConsumptionCursor(buyer);
                if(receipt?.HouseholdDayId==plan.Id && receipt.Receipt.Outcome==NpcInnConsumptionOutcome.Purchased)continue;
                required+=buyer==plan.First.ResidentId?plan.JointInn.FirstPriceGold:plan.JointInn.SecondPriceGold;
            }
            return new HouseholdInnFunding {HouseholdDayId=plan.Id,PayerId=plan.JointInn.PayerId,
                PartnerId=plan.JointInn.PayerId==plan.First.ResidentId?plan.Second.ResidentId:plan.First.ResidentId,
                InnKey=plan.JointInn.InnKey,RequiredGold=required,DepartureAt=departure};
        }

        private HouseholdDayPlan FundedHouseholdInn(string dayId,string buyer)
        {
            if(dayId==null)return null;
            return state.HouseholdDays.Find(p=>p.Id==dayId && p.Status=="planned" && !p.JointSuppressed &&
                p.JointInn?.PayerId!=null && (p.First.ResidentId==buyer || p.Second.ResidentId==buyer));
        }

        public int PlannedHouseholdInnMenu(string dayId,string buyer)
        {
            var plan=FundedHouseholdInn(dayId,buyer);
            return plan==null?-1:plan.First.ResidentId==buyer?plan.JointInn.FirstMenuIndex:plan.JointInn.SecondMenuIndex;
        }
    }
}
