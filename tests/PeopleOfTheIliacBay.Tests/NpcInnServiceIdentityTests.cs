using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcInnServiceIdentityTests
    {
        private static object Identity(string buyer="mother",double starts=100,double ends=200,bool family=true)
        {
            var type=typeof(WorldState).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcInnServiceIdentity");
            if(type==null)throw new InvalidOperationException("Missing physical-independent Inn service identity.");
            var identity=Activator.CreateInstance(type);
            foreach(var field in new Dictionary<string,object>{{"BuyerId",buyer},{"SettlementId",2},{"BuildingKey",10},
                {"StartsAt",starts},{"EndsAt",ends},{"HouseholdDayId",family?"family-day":null},
                {"HouseholdSourceFingerprint",family?new string('a',64):null}})
                type.GetField(field.Key).SetValue(identity,field.Value);
            return identity;
        }
        private static object Call(string method,params object[] args)
        {
            var api=typeof(NpcInnConsumption).GetMethod(method,BindingFlags.Public|BindingFlags.Static);
            if(api==null)throw new InvalidOperationException("Missing independent service API: "+method);
            try{return api.Invoke(null,args);}
            catch(TargetInvocationException error){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        private static string Hash(string text)
        {
            using(var hash=SHA256.Create())
            {var output=new StringBuilder("inn:");foreach(byte b in hash.ComputeHash(Encoding.UTF8.GetBytes(text)))output.Append(b.ToString("x2"));return output.ToString();}
        }
        private static ObservedVisit Physical(string buyer,double starts,double ends,bool family)
        {return new ObservedVisit{ResidentId=buyer,SettlementId=2,BuildingKey=10,ObservedAt=starts,PlannedEndAt=ends,
            HouseholdDayId=family?"family-day":null,HouseholdSourceFingerprint=family?new string('a',64):null};}
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Independent Inn identity preserves the existing family payment hash",()=>{
                string expected=Hash("mother\n2\n10\n200\nfamily-day\n"+new string('a',64));
                Check.Equal(expected,(string)Call("ServiceVisitId",Identity()));
                Check.Equal(expected,NpcInnConsumption.VisitId(Physical("mother",150,200,true)));
                Check.Equal(expected,(string)Call("ServiceVisitId",Identity(starts:175)));
            }));
            cases.Add(("Ordinary and family Inn identities retain native fractional time precision",()=>{
                double end=12597570456.123456;
                string legacy=Hash("mother\n2\n10\n200.125");
                Check.Equal(legacy,(string)Call("ServiceVisitId",Identity(ends:200.125,family:false)));
                Check.Equal(legacy,NpcInnConsumption.VisitId(Physical("mother",101,200.125,false)));
                string family=Hash("mother\n2\n10\n12597570456.1235\nfamily-day\n"+new string('a',64));
                Check.Equal(family,(string)Call("ServiceVisitId",Identity(ends:end)));
                Check.Equal(family,NpcInnConsumption.VisitId(Physical("mother",101,end,true)));
                string ordinary=Hash("mother\n2\n10\n12597570456.1235");
                Check.Equal(ordinary,(string)Call("ServiceVisitId",Identity(ends:end,family:false)));
                Check.Equal(ordinary,(string)Call("ServiceVisitId",Identity(ends:12597570456.1235,family:false)));
            }));
            cases.Add(("Independent service receipts validate without a fabricated observed visit",()=>{
                object identity=Identity();string id=(string)Call("ServiceVisitId",identity);
                var receipt=new NpcInnConsumptionReceipt{VisitId=id,BuyerId="mother",KeeperId="keeper",Kind=NpcInnConsumptionKind.Food,
                    Outcome=NpcInnConsumptionOutcome.Purchased,MenuIndex=4,ProductName="Bread",QuotedPriceGold=3,ChargedGold=3,
                    CompletedAt=120,FoodFulfilled=true};
                var copied=(NpcInnConsumptionReceipt)Call("CopyServiceReceipt",receipt,identity);
                Check.Equal(id,copied.VisitId);Check.Equal(3,copied.ChargedGold);
                Check.True(!ReferenceEquals(receipt,copied));copied.ChargedGold=0;Check.Equal(3,receipt.ChargedGold);
                Check.Throws<ArgumentException>(()=>Call("CopyServiceReceipt",receipt,Identity("father")));
                receipt.CompletedAt=202;Check.Throws<ArgumentException>(()=>Call("CopyServiceReceipt",receipt,identity));
            }));
            cases.Add(("Independent receipt validation cannot turn drink or refusal into a fulfilled meal",()=>{
                object identity=Identity();string id=(string)Call("ServiceVisitId",identity);
                var invalid=new NpcInnConsumptionReceipt{VisitId=id,BuyerId="mother",KeeperId="keeper",Kind=NpcInnConsumptionKind.Food,
                    Outcome=NpcInnConsumptionOutcome.Purchased,MenuIndex=1,ProductName="Beer",QuotedPriceGold=1,ChargedGold=1,
                    CompletedAt=120,FoodFulfilled=true};
                Check.Throws<ArgumentException>(()=>Call("CopyServiceReceipt",invalid,identity));
                invalid.Kind=NpcInnConsumptionKind.Drink;invalid.DrinkFulfilled=true;
                Check.Throws<ArgumentException>(()=>Call("CopyServiceReceipt",invalid,identity));
                invalid.Outcome=NpcInnConsumptionOutcome.Refused;
                Check.Throws<ArgumentException>(()=>Call("CopyServiceReceipt",invalid,identity));
            }));
        }
    }
}
