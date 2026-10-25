using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcRepairServiceTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Native repair service binds one owned damaged UID, repair keeper, price and approach", () =>
            {
                NpcActionExecutor.Validate(Job());

                var malformed = NpcActionExecutor.Copy(Job());
                malformed.Target.ItemId = "0";
                Check.Throws<ArgumentException>(() => NpcActionExecutor.Validate(malformed));

                malformed = NpcActionExecutor.Copy(Job());
                malformed.Target.PersonId = null;
                Check.Throws<ArgumentException>(() => NpcActionExecutor.Validate(malformed));

                malformed = NpcActionExecutor.Copy(Job());
                malformed.ServiceStep = 1;
                Check.Throws<ArgumentException>(() => NpcActionExecutor.Validate(malformed));
            }));
            
            cases.Add(("Native repair collection binds same exact UID with no second charge", () =>
            {
                var collect = Job();
                collect.ServicePrice = 0;
                collect.Target.QuotedGold = 0;
                NpcActionExecutor.Validate(collect);

                collect = NpcActionExecutor.Copy(collect);
                collect.Target.QuotedGold = 1;
                Check.Throws<ArgumentException>(() => NpcActionExecutor.Validate(collect));
            }));
        }

        private static NpcActionJob Job()
        {
            return new NpcActionJob
            {
                Id = "repair:1",
                ProposalId = "request:1",
                ResidentId = "owner",
                Kind = NpcActionKind.Purchase,
                ServiceCode = "native-item-repair",
                ServicePrice = 9,
                Phase = NpcActionPhase.Accepted,
                Revision = 1,
                AcceptedAt = 100,
                UpdatedAt = 100,
                DeadlineAt = 200,
                Target = new NpcActionTarget
                {
                    Kind = NpcActionTargetKind.IndoorPoint,
                    SettlementId = 1,
                    BuildingKey = 10,
                    PersonId = "repair-keeper",
                    ContainerId = "native-repair",
                    ItemId = "42",
                    Quantity = 1,
                    QuotedGold = 9,
                },
            };
        }
    }
}
