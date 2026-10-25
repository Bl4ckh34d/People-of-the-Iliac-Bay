using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativePersonalTradePricing
    {
        // Ordinary residents have no shop-quality rating. Ten is DFU's neutral
        // merchant; both cost and bargaining use the same native functions.
        internal static int Quote(DaggerfallUnityItem item,int quantity,bool playerBuying,int quality=10,int favour=0)
        {
            if(item==null || quantity<1 || quantity>item.stackCount)return -1;
            int value=item.value;
            if(item.IsPotion)
            {
                var recipe=GameManager.Instance.EntityEffectBroker.GetPotionRecipe(item.PotionRecipeKey);
                if(recipe==null)return -1;
                value=recipe.Price;
            }
            if(value==0)return 0; // Explicitly permitted worthless parchment exchange.
            if(value<0)return -1;
            int buy=NativeQuote(value,quantity,quality,-1,false);
            int sell=NativeQuote(value,quantity,quality,item.ConditionPercentage,true);
            if(buy<0 || sell<0)return -1;
            int price=NpcSocialPricing.MerchantQuote(buy,sell,playerBuying,favour);
            return price>NpcInventoryLimits.MaximumWalletGold?-1:price;
        }
        private static int NativeQuote(int value,int quantity,int quality,int condition,bool selling)
        {
            long cost=(long)FormulaHelper.CalculateCost(value,quality,condition)*quantity;
            if(cost<0 || cost>int.MaxValue)return -1;
            return Math.Max(1,FormulaHelper.CalculateTradePrice((int)cost,quality,selling));
        }
    }
}
