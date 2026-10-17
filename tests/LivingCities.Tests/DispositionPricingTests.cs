using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class DispositionPricingTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Disposition pricing follows the asymmetric disposition curve", DispositionChangesPurchasePriceAsSpecified));
            cases.Add(("Disposition pricing adjusts sale proceeds in the opposite direction", SalePricesMoveOppositePurchasePrices));
            cases.Add(("Disposition refuses paid services at the business boundary", PaidServiceRefusalThresholdIsInclusive));
            cases.Add(("Disposition pricing preserves free and positive-price bounds", FreeAndPositivePricesKeepBounds));
            cases.Add(("Native paid room quote parses the new input before duration validation", NewRoomQuoteUsesInputBeforeNativeStateChanges));
        }

        private static void DispositionChangesPurchasePriceAsSpecified()
        {
            Check.Equal(150, DispositionPricing.ApplyPurchaseDispositionPrice(100, -100));
            Check.Equal(150, DispositionPricing.ApplyRepairDispositionPrice(100, -100));
            Check.Equal(125, DispositionPricing.ApplyPurchaseDispositionPrice(100, -50));
            Check.Equal(100, DispositionPricing.ApplyPurchaseDispositionPrice(100, 0));
            Check.Equal(93, DispositionPricing.ApplyPurchaseDispositionPrice(100, 50));
            Check.Equal(85, DispositionPricing.ApplyPurchaseDispositionPrice(100, 100));
            Check.Equal(85, DispositionPricing.ApplyRepairDispositionPrice(100, 100));
            Check.Equal(150, DispositionPricing.ApplyPurchaseDispositionPrice(100, -150));
            Check.Equal(85, DispositionPricing.ApplyPurchaseDispositionPrice(100, 150));
            Check.Equal(2, DispositionPricing.ApplyPurchaseDispositionPrice(2, 1));
            Check.Equal(8, DispositionPricing.ApplyPurchaseDispositionPrice(9, 100));
            Check.Equal(9, DispositionPricing.ApplyPurchaseDispositionPrice(10, 100));
        }

        private static void FreeAndPositivePricesKeepBounds()
        {
            Check.Equal(0, DispositionPricing.ApplyPurchaseDispositionPrice(0, -100));
            Check.Equal(1, DispositionPricing.ApplyPurchaseDispositionPrice(1, 100));
            Check.Equal(int.MaxValue, DispositionPricing.ApplyPurchaseDispositionPrice(int.MaxValue, -100));
            Check.Equal(int.MaxValue, DispositionPricing.ApplyPurchaseDispositionPrice(int.MaxValue, -1));
            Check.Equal(int.MaxValue - (int.MaxValue * 100L * 15L / 10000L),
                DispositionPricing.ApplyPurchaseDispositionPrice(int.MaxValue, 100));
        }

        private static void SalePricesMoveOppositePurchasePrices()
        {
            Check.Equal(50, DispositionPricing.ApplySaleDispositionPrice(100, -100));
            Check.Equal(75, DispositionPricing.ApplySaleDispositionPrice(100, -50));
            Check.Equal(100, DispositionPricing.ApplySaleDispositionPrice(100, 0));
            Check.Equal(107, DispositionPricing.ApplySaleDispositionPrice(100, 50));
            Check.Equal(115, DispositionPricing.ApplySaleDispositionPrice(100, 100));
            Check.Equal(50, DispositionPricing.ApplySaleDispositionPrice(100, -150));
            Check.Equal(115, DispositionPricing.ApplySaleDispositionPrice(100, 150));
            Check.Equal(2, DispositionPricing.ApplySaleDispositionPrice(2, 1));
            Check.Equal(5, DispositionPricing.ApplySaleDispositionPrice(9, -100));
            Check.Equal(11, DispositionPricing.ApplySaleDispositionPrice(10, 100));
            Check.Equal(0, DispositionPricing.ApplySaleDispositionPrice(0, 100));
            Check.Equal(int.MaxValue, DispositionPricing.ApplySaleDispositionPrice(int.MaxValue, 100));
            Check.Equal(1, DispositionPricing.ApplySaleDispositionPrice(1, -100));
        }

        private static void PaidServiceRefusalThresholdIsInclusive()
        {
            Check.True(!DispositionPricing.AllowsPaidService(-100));
            Check.True(!DispositionPricing.AllowsPaidService(-50));
            Check.True(DispositionPricing.AllowsPaidService(-49));
            Check.True(DispositionPricing.AllowsPaidService(0));
            Check.True(DispositionPricing.AllowsPaidService(100));
        }

        private static void NewRoomQuoteUsesInputBeforeNativeStateChanges()
        {
            int days;
            Check.True(DispositionPricing.TryGetPaidTavernRoomDays("1", 0, out days));
            Check.Equal(1, days);
            Check.True(DispositionPricing.TryGetPaidTavernRoomDays("350", 0, out days));
            Check.Equal(350, days);
            Check.True(DispositionPricing.TryGetPaidTavernRoomDays("1", 349, out days));
            Check.Equal(1, days);
            Check.True(DispositionPricing.TryGetPaidTavernRoomDays("1", -10, out days));
            Check.True(!DispositionPricing.TryGetPaidTavernRoomDays("0", 0, out days));
            Check.Equal(0, days);
            Check.True(!DispositionPricing.TryGetPaidTavernRoomDays("351", 0, out days));
            Check.True(!DispositionPricing.TryGetPaidTavernRoomDays("1", 350, out days));
            Check.True(!DispositionPricing.TryGetPaidTavernRoomDays("not a number", 0, out days));
        }
    }
}
