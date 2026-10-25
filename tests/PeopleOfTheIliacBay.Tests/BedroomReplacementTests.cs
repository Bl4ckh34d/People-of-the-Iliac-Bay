using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class BedroomReplacementTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Bedroom replacement cannot evict an existing keeper family and failed relocation preserves home",()=>{
                var store=new WorldStateStore(42);store.GetOrCreateFamilies(1,new[]{new HomeProfile(10,1,0,false)});
                var owner=store.RegisterNativePerson(1,5,12,"Keeper Kinghart",ResidentSex.Male,1,0,182,2);
                Check.True(store.AssignNativeBed(owner.Id,new HomeProfile(5,0),new[]{new HomeProfile(10,1)}));
                Check.True(!store.PreferNativeWorkplaceBed(owner.Id,new HomeProfile(5,0)));Check.Equal(10,owner.HomeKey);
                Check.True(store.PreferNativeWorkplaceBed(owner.Id,new HomeProfile(5,1)));
                var family=store.CreateKeeperHousehold(owner.Id,new HomeProfile(5,1),true);
                Check.True(family!=null);Check.True(!store.RetireOrdinaryBedroomFlat(owner.Id));
                Check.True(!owner.RetiredBedroomFlat);Check.Equal(owner.Id,family.Residents[0].Id);
                var copy=new WorldStateStore(1);copy.Restore(store.Snapshot());Check.Equal(5,copy.NativePeopleFor(1)[0].HomeKey);
            }));
            cases.Add(("Protected staff bed zero leaves keeper and partner separate real beds",()=>{
                var store=new WorldStateStore(42);store.GetOrCreateFamilies(1,new[]{new HomeProfile(10,1)});var shop=new HomeProfile(5,3);
                var staff=store.RegisterNativePerson(1,5,11,"Protected Staff",ResidentSex.Male,1,0,182,1);
                Check.True(store.AssignNativeBed(staff.Id,shop,new HomeProfile[0]));
                var owner=store.RegisterNativePerson(1,5,12,"Keeper Kinghart",ResidentSex.Male,1,0,182,2);
                Check.True(store.PreferNativeWorkplaceBed(owner.Id,shop));Check.Equal(1,owner.BedSlot);
                var family=store.CreateKeeperHousehold(owner.Id,shop,true);Check.True(family!=null);Check.Equal(2,family.Residents.Count);
                Check.Equal(1,family.Residents[0].BedSlot);Check.Equal(2,family.Residents[1].BedSlot);
                var copy=new WorldStateStore(1);copy.Restore(store.Snapshot());Check.Equal(0,copy.NativePeopleFor(1).Find(p=>p.Id==staff.Id).BedSlot);
            }));
            cases.Add(("Shop owner relocates a former off-site bed and gets one female partner on spare capacity",()=>{
                foreach(var sex in new[]{ResidentSex.Male,ResidentSex.Female})
                {
                    var store=new WorldStateStore(42);store.GetOrCreateFamilies(1,new[]{new HomeProfile(10,3,0,false)});
                    var owner=store.RegisterNativePerson(1,5,12,"Keeper Kinghart",sex,1,0,182,2);
                    Check.True(store.AssignNativeBed(owner.Id,new HomeProfile(5,0),new[]{new HomeProfile(10,3)}));Check.Equal(10,owner.HomeKey);
                    var shop=new HomeProfile(5,4);Check.True(store.PreferNativeWorkplaceBed(owner.Id,shop));
                    Check.Equal(5,owner.HomeKey);var family=store.CreateKeeperHousehold(owner.Id,shop,true);
                    Check.Equal(2,family.Residents.Count);Check.Equal(ResidentSex.Female,family.Residents.Find(r=>r.Id!=owner.Id).Sex);
                    Check.Equal("Kinghart",family.Surname);Check.Equal(owner.Id,family.Residents.Find(r=>r.Id!=owner.Id).PartnerId);
                    var copy=new WorldStateStore(1);copy.Restore(store.Snapshot());Check.Equal(5,copy.NativePeopleFor(1)[0].HomeKey);
                }
            }));
            cases.Add(("Replacing an ordinary inn flat releases room and remote reservation without killing anyone",()=>{
                var store=new WorldStateStore(42);store.GetOrCreateFamilies(1,new[]{new HomeProfile(10,1)});
                var person=store.RegisterNativePerson(1,5,11,"Visiting Person",ResidentSex.Female,1,0,182,1);
                store.RegisterLodging(1,5,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed"}}});
                store.TryClaimLodgingBed(1,5,person.Id,"bed",100);
                var home=store.TryReserveInnLodgerHome(person.Id,"room",2,new HomeProfile(20,1),100);
                Check.True(home!=null);
                store.RememberConversation(person.Id,100,"6 Morning Star 405","Inn","My silver lute is missing.","I remember.","model");
                Check.True(store.RetireOrdinaryBedroomFlat(person.Id));Check.True(person.IsAlive);
                Check.True(store.InnLodgerHome(person.Id)==null);Check.True(store.NativeLodgingBed(person.Id)==null);
                Check.True(store.AvailableLodgingRoom(1,5,1,101,200)!=null);
                Check.True(!store.TryClaimLodgingBed(1,5,person.Id,"bed",101));
                Check.True(store.TryReserveInnLodgerHome(person.Id,"room",2,new HomeProfile(20,1),101)==null);
                var copy=new WorldStateStore(1);copy.Restore(store.Snapshot());
                Check.True(copy.NativePeopleFor(1).Find(p=>p.Id==person.Id).RetiredBedroomFlat);
                Check.True(copy.RecallConversation(person.Id,"lute",200,2000).Contains("silver lute"));
                Check.True(copy.RetireOrdinaryBedroomFlat(person.Id));
                Check.True(copy.TryBookLodging(1,5,"guest","visitor",3,false,1,101,200));
                Check.Equal(0,copy.CurrentPopulation(2).Households[0].Residents.Count);
            }));
            cases.Add(("Replacing shop tenant releases only that native bed and preserves other household members",()=>{
                var store=new WorldStateStore(42);store.GetOrCreateFamilies(1,new[]{new HomeProfile(10,1)});
                var tenant=store.RegisterNativePerson(1,5,11,"Shop Tenant",ResidentSex.Male,1,0,182,1);
                Check.True(store.AssignNativeBed(tenant.Id,new HomeProfile(5,2),new HomeProfile[0]));
                Check.True(store.RetireOrdinaryBedroomFlat(tenant.Id));Check.Equal(-1,tenant.HomeKey);
                var owner=store.RegisterNativePerson(1,5,12,"Shop Owner",ResidentSex.Male,1,0,182,2);
                Check.True(store.AssignNativeBed(owner.Id,new HomeProfile(5,2),new HomeProfile[0]));Check.Equal(0,owner.BedSlot);
                Check.Equal(1,store.CurrentPopulation(1).Households[0].Residents.Count);
                var copy=new WorldStateStore(1);copy.Restore(store.Snapshot());Check.True(copy.NativePeopleFor(1).Find(p=>p.Id==tenant.Id).RetiredBedroomFlat);
            }));
            cases.Add(("Bedroom replacement preserves dead identity and corpse home evidence",()=>{
                var store=new WorldStateStore(42);store.GetOrCreateFamilies(1,new[]{new HomeProfile(10,1)});
                var person=store.RegisterNativePerson(1,5,11,"Dead Visitor",ResidentSex.Male,1,0,182,1);
                store.RegisterLodging(1,5,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed"}}});
                store.TryClaimLodgingBed(1,5,person.Id,"bed",100);
                store.TryReserveInnLodgerHome(person.Id,"room",2,new HomeProfile(20,1),100);
                person.IsAlive=false;Check.True(!store.RetireOrdinaryBedroomFlat(person.Id));
                Check.True(store.InnLodgerHome(person.Id)!=null);Check.True(!person.RetiredBedroomFlat);
                var copy=new WorldStateStore(1);copy.Restore(store.Snapshot());Check.True(!copy.NativePeopleFor(1)[0].IsAlive);
            }));
        }
    }
}
