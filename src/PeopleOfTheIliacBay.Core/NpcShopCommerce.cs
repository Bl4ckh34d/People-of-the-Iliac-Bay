using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public static class NpcShopCommerce
    {
        public const string ServiceCode="shop-stock";
        // One exact owned item is left with a verified native repair keeper.
        // This is not merchant-stock commerce or a general service framework.
        public const string RepairServiceCode="native-item-repair";
        public const string RepairTargetContainerId="native-repair";
        // Single native trainer purchase. Runtime owns eligibility, time, and
        // native skill result; core binds only immutable request identity.
        public const string TrainingServiceCode="native-temple-training";
        public const string TrainingTargetContainerId="native-temple-training";

        public static string ServiceCodeForPurchaseTarget(NpcActionTarget target)
        { return target != null && target.ContainerId == TrainingTargetContainerId ? TrainingServiceCode : ServiceCode; }
        public static bool TryPriceForBuyer(WorldStateStore store,string personId,int nativeUnitCost,int quantity,int quality,bool halfPrice,out int price)
        {
            price=0;if(store==null || string.IsNullOrWhiteSpace(personId))return false;
            // Use stable generated identity, including an Inn traveler's class,
            // rather than a native entity's transient random stats.
            var buyer=store.ReadNpcActor(personId,store.ResolveNpcArchetype(personId));
            // Indices are native DFCareer.Skills.Mercantile and Personality.
            return TryPrice(nativeUnitCost,quantity,quality,buyer.Skill(14),buyer.Profile.Attribute(5),halfPrice,out price);
        }

        // DFU 1.1.1 buying formula, with the buyer's stats explicitly supplied.
        // FormulaHelper.CalculateTradePrice uses PlayerEntity and must not price
        // an NPC's purchase from the player's skills or personal relationship.
        public static bool TryPrice(int nativeUnitCost,int quantity,int quality,int mercantile,int personality,bool halfPrice,out int price)
        {
            price=0;
            if(nativeUnitCost<1 || quantity<1 || quantity>1000000 || quality<0 || quality>20 ||
                mercantile<0 || mercantile>100 || personality<0 || personality>100) return false;
            long cost=(long)nativeUnitCost*quantity;if(halfPrice) cost/=2;
            long merchant=5*(quality-10)+50;
            long trade=((merchant<<8)/200+128)*(((100-mercantile)<<8)/200+128)>>8;
            long social=(((merchant<<8)/200+128)*(((100-personality)<<8)/200+128)>>8)<<6;
            long total=((((192*trade)>>8)+(social>>8))*cost)>>8;
            total=Math.Max(1,total);
            if(total>NpcInventoryLimits.MaximumWalletGold) return false;
            price=(int)total;return true;
        }

        public static void ValidateService(NpcActionJob value)
        {
            if (NpcPaidCompanionAction.ContractId(value) != null)
            {
                if (!NpcPaidCompanionAction.IsPaidFollowJob(value))
                    throw new ArgumentException("Invalid paid companion Follow action.");
                return;
            }
            if(value.ServiceCode==ServiceCode)
            {
                ulong stock,item;
                if(value.Kind!=NpcActionKind.Purchase || value.ServicePrice<1 || value.ServicePrice>NpcInventoryLimits.MaximumWalletGold ||
                    value.ServiceStep!=0 || value.Target.Kind!=NpcActionTargetKind.IndoorPoint || value.Target.QuotedGold!=value.ServicePrice ||
                    string.IsNullOrWhiteSpace(value.Target.PersonId) || value.Target.PersonId.Length>240 || value.Target.PersonId==value.ResidentId ||
                    !ulong.TryParse(value.Target.ContainerId,NumberStyles.None,CultureInfo.InvariantCulture,out stock) || stock==0 ||
                    !ulong.TryParse(value.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out item) || item==0 ||
                    value.Target.Quantity<1 || value.Target.Quantity>1000000)
                    throw new ArgumentException("Invalid NPC shop purchase.");
                return;
            }
            if(value.ServiceCode==NpcShopLootSale.ServiceCode)
            {
                NpcShopLootSale.ValidateService(value);
                return;
            }
            if(value.ServiceCode==TrainingServiceCode)
            {
                int skill;
                if(value.Kind!=NpcActionKind.Purchase || value.ServicePrice<1 || value.ServicePrice>NpcInventoryLimits.MaximumWalletGold ||
                    value.ServiceStep<0 || value.ServiceStep>1 || value.Target==null || value.Target.Kind!=NpcActionTargetKind.IndoorPoint ||
                    value.Target.QuotedGold!=value.ServicePrice || value.Target.ContainerId!=TrainingTargetContainerId ||
                    string.IsNullOrWhiteSpace(value.Target.PersonId) || value.Target.PersonId.Length>240 || value.Target.PersonId==value.ResidentId ||
                    !int.TryParse(value.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out skill) || skill<0 || skill>34 ||
                    value.Target.Quantity!=1 ||
                    value.ServiceStep==0 && (value.FinishAt!=0 || value.Phase==NpcActionPhase.Completed) ||
                    value.ServiceStep==1 && (value.FinishAt<=0 || value.Phase!=NpcActionPhase.Executing && value.Phase!=NpcActionPhase.Completed &&
                        value.Phase!=NpcActionPhase.Failed && value.Phase!=NpcActionPhase.Cancelled))
                    throw new ArgumentException("Invalid NPC native training service.");
                return;
            }
            if(value.ServiceCode==RepairServiceCode)
            {
                ulong item;
                if(value.Kind!=NpcActionKind.Purchase || value.ServicePrice<0 || value.ServicePrice>NpcInventoryLimits.MaximumWalletGold ||
                    value.ServiceStep!=0 || value.Target.Kind!=NpcActionTargetKind.IndoorPoint || value.Target.QuotedGold!=value.ServicePrice ||
                    string.IsNullOrWhiteSpace(value.Target.PersonId) || value.Target.PersonId.Length>240 || value.Target.PersonId==value.ResidentId ||
                    value.Target.ContainerId!=RepairTargetContainerId ||
                    !ulong.TryParse(value.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out item) || item==0 || value.Target.Quantity!=1)
                    throw new ArgumentException("Invalid NPC native repair service.");
                return;
            }
            if(value.ServiceCode!=null && (value.Kind!=NpcActionKind.Purchase || value.ServiceCode!="tavern-beer" ||
                value.ServicePrice<0 || value.ServicePrice>3 || value.ServiceStep<0 || value.ServiceStep>1) ||
                value.ServiceCode==null && (value.ServicePrice!=0 || value.ServiceStep!=0) ||
                value.ServiceCode!=null && value.Phase==NpcActionPhase.Completed && value.ServiceStep!=1)
                throw new ArgumentException("Invalid NPC service action progress.");
        }
    }
}
