using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdRecoveryReservationTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Recovery reservation preserves deadline through serialization", Roundtrip));
            cases.Add(("Recovery reservation rejects invalid timestamps and property identity", Corruption));
            cases.Add(("Recovery reservation cannot bypass current ownership or cleanup gates", Gates));
        }
        private static HouseholdRecoveryEligibility Facts() => new HouseholdRecoveryEligibility {
            AllPermanentResidentsConfirmedDead=true,PriorPermanentResidentCount=3,BedCount=3 };
        private static HouseholdRecoveryReservation Fixture() => HouseholdRecoveryReservation.Create(
            HouseholdRecoveryPolicy.Evaluate(42,3,17,0,100,HouseholdRecoveryTier.DenseCity,Facts()));
        private static void Roundtrip()
        {
            var original=Fixture();
            var json=new JsonSerializerOptions {IncludeFields=true};
            var restored=HouseholdRecoveryReservation.Copy(JsonSerializer.Deserialize<HouseholdRecoveryReservation>(
                JsonSerializer.Serialize(original,json),json));
            Check.Equal(original.RecoveryAt,restored.RecoveryAt);
            Check.Equal(original.ConfirmedLastDeathAt,restored.ConfirmedLastDeathAt);
            Check.True(restored.CanActivate(restored.RecoveryAt,Facts(),true,true));
            Check.True(!restored.CanActivate(restored.RecoveryAt-1,Facts(),true,true));
        }
        private static void Corruption()
        {
            for(int i=0;i<6;i++)
            {
                var value=Fixture();
                if(i==0) value.RecoveryAt=double.NaN;
                if(i==1) value.RecoveryAt=value.ConfirmedLastDeathAt;
                if(i==2) value.Generation=-1;
                if(i==3) value.BuildingKey=-1;
                if(i==4) value.DelayDays=99;
                if(i==5) value.RecoveryAt+=1;
                Check.Throws<ArgumentException>(()=>HouseholdRecoveryReservation.Copy(value));
            }
        }
        private static void Gates()
        {
            var value=Fixture();var facts=Facts();
            Check.True(!value.CanActivate(value.RecoveryAt,facts,false,true));
            Check.True(!value.CanActivate(value.RecoveryAt,facts,true,false));
            facts.PlayerOwned=true;
            Check.True(!value.CanActivate(value.RecoveryAt,facts,true,true));
            Check.Throws<ArgumentException>(()=>HouseholdRecoveryReservation.Create(
                HouseholdRecoveryPolicy.Evaluate(42,3,17,0,100,HouseholdRecoveryTier.DenseCity,facts)));
            facts.PlayerOwned=false;facts.HasLivingPermanentResident=true;
            Check.True(!value.CanActivate(value.RecoveryAt,facts,true,true));
        }
    }
}
