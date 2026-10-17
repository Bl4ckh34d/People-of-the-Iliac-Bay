using System;

namespace LivingCities.Core
{
    /// <summary>Applies a bounded personal disposition adjustment after native pricing.</summary>
    public static class DispositionPricing
    {
        private const int MaximumDisposition = 100;

        /// <summary>Paid offers are refused at the shared hostile-business boundary.</summary>
        public static bool AllowsPaidService(int actualDisposition)
        {
            return actualDisposition > -50;
        }

        /// <summary>
        /// Negative disposition raises purchase and paid inn-room prices by up
        /// to 50%; positive disposition lowers them by up to 15%. The adjustment
        /// is linear over -100..100 and rounded down in absolute gold, so a
        /// partial gold unit never exceeds either cap.
        /// </summary>
        public static int ApplyPurchaseDispositionPrice(int vanillaPrice, int actualDisposition)
        {
            if (vanillaPrice <= 0) return vanillaPrice;

            int boundedDisposition = Math.Max(-MaximumDisposition, Math.Min(MaximumDisposition, actualDisposition));
            long price = vanillaPrice;
            if (boundedDisposition < 0)
            {
                long increase = price * -boundedDisposition * 50L / 10000L;
                return increase > int.MaxValue - price ? int.MaxValue : (int)(price + increase);
            }

            long discount = price * boundedDisposition * 15L / 10000L;
            return (int)Math.Max(1, price - discount);
        }

        /// <summary>Repair quotes use the same disposition curve as purchases.</summary>
        public static int ApplyRepairDispositionPrice(int vanillaPrice, int actualDisposition)
        {
            return ApplyPurchaseDispositionPrice(vanillaPrice, actualDisposition);
        }

        /// <summary>
        /// Sale proceeds move opposite purchase prices: hostility lowers proceeds
        /// by up to 50%, while positive disposition raises them by up to 15%.
        /// </summary>
        public static int ApplySaleDispositionPrice(int vanillaPrice, int actualDisposition)
        {
            if (vanillaPrice <= 0) return vanillaPrice;

            int boundedDisposition = Math.Max(-MaximumDisposition, Math.Min(MaximumDisposition, actualDisposition));
            long price = vanillaPrice;
            if (boundedDisposition < 0)
            {
                long discount = price * -boundedDisposition * 50L / 10000L;
                return (int)Math.Max(1, price - discount);
            }

            long increase = price * boundedDisposition * 15L / 10000L;
            return increase > int.MaxValue - price ? int.MaxValue : (int)(price + increase);
        }

        /// <summary>Parses the native room-duration field and applies its 350-day rental limit.</summary>
        public static bool TryGetPaidTavernRoomDays(string input, int daysAlreadyRented, out int daysToRent)
        {
            daysToRent = 0;
            if (!int.TryParse(input, out daysToRent) || daysToRent < 1)
            {
                daysToRent = 0;
                return false;
            }

            daysAlreadyRented = Math.Max(0, daysAlreadyRented);
            if ((long)daysToRent + daysAlreadyRented > 350)
            {
                daysToRent = 0;
                return false;
            }
            return true;
        }
    }
}
