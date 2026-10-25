using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcNamedAcquiredHandoverTests
    {
        private static NpcActionOption Purchase()
        {
            return new NpcActionOption {Id=7,Kind=NpcActionKind.Purchase,
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=10,
                    PersonId="shopkeeper",ContainerId="42",ItemId="900",Quantity=2,QuotedGold=13}};
        }

        private static NpcActionOption Bind(NpcActionOption source,NpcActionTarget recipient)
        {
            var type=typeof(NpcActionSequenceChat).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcNamedAcquiredHandover");
            Check.True(type!=null,"Named acquired handover binding needs a core helper.");
            var method=type.GetMethod("TryBind",BindingFlags.Public|BindingFlags.Static);
            Check.True(method!=null,"Named acquired handover helper needs TryBind.");
            return (NpcActionOption)method.Invoke(null,new object[]{0,source,recipient,"Mara"});
        }

        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Named acquired handover binds only a verified recipient ID",()=>{
                    Check.True(Bind(Purchase(),new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"})==null);
                    Check.True(Bind(Purchase(),new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="resident:mara"})!=null);
                }));

            cases.Add(("Named acquired handover binds verified non-player recipient",()=>{
                var recipient=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="resident:mara"};
                var handover=Bind(Purchase(),recipient);
                Check.True(handover!=null);
                Check.Equal(NpcActionKind.Transfer,handover.Kind);
                Check.Equal(1,handover.ItemSourceStep);
                Check.Equal(NpcActionTargetKind.Person,handover.Target.Kind);
                Check.Equal("resident:mara",handover.Target.PersonId);
                Check.Equal("900",handover.Target.ItemId);
                Check.Equal(2,handover.Target.Quantity);
                Check.Equal(NpcActionTransferDirection.NpcToCounterparty,handover.Target.TransferDirection);
                Check.True(handover.Label.Contains("Mara"));
            }));

            cases.Add(("Named acquired handover rejects guessed or player recipient",()=>{
                Check.True(Bind(Purchase(),new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"})==null);
                Check.True(Bind(Purchase(),new NpcActionTarget {Kind=NpcActionTargetKind.Person})==null);
                Check.True(Bind(Purchase(),new NpcActionTarget {Kind=NpcActionTargetKind.Building,PersonId="resident:mara"})==null);
                var bad=Purchase();bad.Target.ContainerId=null;
                Check.True(Bind(bad,new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="resident:mara"})==null);
            }));
        }
    }
}
