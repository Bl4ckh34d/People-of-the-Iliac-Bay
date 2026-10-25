using System;
using System.Reflection;
using PeopleOfTheIliacBay.DFU;

internal static class Program
{
    private static int Main()
    {
        try
        {
            var money = typeof(PersonalTradeStaging).Assembly.GetType("PeopleOfTheIliacBay.DFU.PersonalTradeMoney");
            Need(money != null, "Missing directional gold input model.");
            var parse = money.GetMethod("TryParseAmount");
            var format = money.GetMethod("FieldText");
            foreach (var side in new[] { PersonalTradeSide.Player, PersonalTradeSide.Npc })
            {
                foreach (string invalid in new[] { "", " ", "30 ", " 30", "+30", "-30", "3.0", "3,0", "3e1", "thirty", "٣٠", "３０", "2147483648" })
                    Need(!Parse(parse, side, invalid, out _), "Accepted invalid amount: " + invalid);
                foreach (int amount in new[] { 0, 1, 30, 99999 })
                {
                    Need(Parse(parse, side, amount.ToString(), out int net), "Rejected valid amount.");
                    Need(net == (side == PersonalTradeSide.Npc ? -amount : amount), "Payment direction changed.");
                    string expected = amount == 0 && side==PersonalTradeSide.Npc ? "" : amount + " g";
                    Need((string)format.Invoke(null, new object[] { side, (int?)net }) == expected, "Wrong paying field.");
                    var opposite = side == PersonalTradeSide.Npc ? PersonalTradeSide.Player : PersonalTradeSide.Npc;
                    Need((string)format.Invoke(null, new object[] { opposite, (int?)net }) ==
                        (amount==0 && opposite==PersonalTradeSide.Player?"0 g":""), "Amount duplicated in non-paying field.");
                }
                Need((string)format.Invoke(null, new object[] { side, null }) == "", "Unknown amount shown as money.");
            }
            Console.WriteLine("PASS digits-only amounts, payment direction, five-digit totals, and empty opposite field");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static bool Parse(MethodInfo parse, PersonalTradeSide payer, string input, out int net)
    {
        object[] arguments = { payer, input, 0 };
        bool accepted = (bool)parse.Invoke(null, arguments);
        net = (int)arguments[2];
        return accepted;
    }
    private static void Need(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
