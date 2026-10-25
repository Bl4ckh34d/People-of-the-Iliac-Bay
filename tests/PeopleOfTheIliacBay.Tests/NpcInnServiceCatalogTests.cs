using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcInnServiceCatalogTests
    {
        private static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true};
        private static object Invoke(Type type,object target,string method,params object[] args)
        {
            var api=type.GetMethod(method);if(api==null)throw new InvalidOperationException("Missing Inn catalog API: "+method);
            try{return api.Invoke(target,args);}catch(TargetInvocationException error){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        private static Type CatalogType()
        {return typeof(WorldState).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcInnServiceCatalog")??throw new InvalidOperationException("Missing source-bound native Inn catalog.");}
        private static T Field<T>(object value,string name){return (T)value.GetType().GetField(name).GetValue(value);}
        private static void Set(object value,string name,object data){value.GetType().GetField(name).SetValue(value,data);}
        private static object Point(double x){return new {X=x,Y=0,Z=0};}
        private static object Draft(string keeper)
        {
            var menu=new List<object>();for(int i=0;i<11;i++)menu.Add(new {MenuIndex=i,Kind=i<4?1:2,ProductName="native-item-"+i,BasePriceGold=i==3?3:1});
            var snapshot=new {ModelVersion=1,SettlementId=1,BuildingKey=20,BlockIndex=7,RecordIndex=2,Climate=224,
                KeeperId=keeper,KeeperHash=900,CapturedAt=100d,OpenMinute=480,CloseMinute=1320,
                Menu=menu,EntryToCounter=new[]{Point(0),Point(2)},Pairs=new[]{new {
                    First=new{Id="chair-a",TableId="table-1",X=3d,Z=0d,Height=0.5,FacingX=1d,FacingZ=0d},
                    Second=new{Id="chair-b",TableId="table-1",X=4d,Z=0d,Height=0.5,FacingX=-1d,FacingZ=0d},
                    FirstCounterToSeat=new[]{Point(2),Point(3)},SecondCounterToSeat=new[]{Point(2),Point(4)},
                    FirstSeatToEntry=new[]{Point(3),Point(0)},SecondSeatToEntry=new[]{Point(4),Point(0)}
                }}};
            return JsonSerializer.Deserialize(JsonSerializer.Serialize(snapshot),CatalogType(),Json);
        }
        private static object Seal(object draft){return Invoke(CatalogType(),null,"Seal",draft);}
        private static object Copy(object catalog){return Invoke(CatalogType(),null,"Copy",catalog);}
        private static WorldStateStore Store(out NativePerson keeper)
        {
            var store=new WorldStateStore(17);store.GetOrCreate(1,new[]{10,20},1);
            keeper=store.RegisterNativePerson(1,20,900,"Inn keeper",ResidentSex.Female,0,0,182,0);return store;
        }
        private static object Read(WorldStateStore store){return Invoke(typeof(WorldStateStore),store,"ReadNpcInnServiceCatalog",1,20);}
        private static void Publish(WorldStateStore store,object catalog){Invoke(typeof(WorldStateStore),store,"RegisterNpcInnServiceCatalog",catalog);}
        private static object Tariff(int day,int holiday)
        {
            var type=typeof(WorldState).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcInnServiceTariff");
            if(type==null)throw new InvalidOperationException("Missing date-bound native calendar tariff.");
            var value=Activator.CreateInstance(type);Set(value,"AbsoluteDay",day);Set(value,"HolidayId",holiday);return value;
        }
        private static object Quote(object catalog,int index,int day,int holiday,double? at=null)
        {return Invoke(CatalogType(),null,"Quote",catalog,index,at??(day*86400d+12*3600),Tariff(day,holiday));}
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Catalog cannot give one native chair different poses in separate pair options",()=>{
                var store=Store(out var keeper);var draft=Draft(keeper.Id);var pairs=(IList)Field<object>(draft,"Pairs");
                var other=((IList)Field<object>(Draft(keeper.Id),"Pairs"))[0];
                Set(Field<object>(other,"First"),"X",4d);Set(Field<object>(other,"Second"),"X",5d);
                Set(Field<object>(other,"Second"),"Id","chair-c");
                Set(((IList)Field<object>(other,"FirstCounterToSeat"))[1],"X",4d);
                Set(((IList)Field<object>(other,"FirstSeatToEntry"))[0],"X",4d);
                Set(((IList)Field<object>(other,"SecondCounterToSeat"))[1],"X",5d);
                Set(((IList)Field<object>(other,"SecondSeatToEntry"))[0],"X",5d);
                pairs.Add(other);Check.Throws<ArgumentException>(()=>Seal(draft));
            }));
            cases.Add(("Native catalog zero coordinates have one canonical source fingerprint",()=>{
                var store=Store(out var keeper);var positive=Seal(Draft(keeper.Id));var draft=Draft(keeper.Id);
                Set(((IList)Field<object>(draft,"EntryToCounter"))[0],"Z",BitConverter.Int64BitsToDouble(long.MinValue));
                Check.Equal(Field<string>(positive,"Fingerprint"),Field<string>(Seal(draft),"Fingerprint"));
            }));
            cases.Add(("Native Inn catalog retains keeper source and detached route/menu snapshots",()=>{
                var store=Store(out var keeper);var draft=Draft(keeper.Id);var catalog=Seal(draft);Publish(store,catalog);
                Set(((IList)Field<object>(draft,"Menu"))[0],"BasePriceGold",99);
                var read=Read(store);Check.Equal(keeper.Id,Field<string>(read,"KeeperId"));
                Check.Equal(1,Field<int>(((IList)Field<object>(read,"Menu"))[0],"BasePriceGold"));
                Set(((IList)Field<object>(read,"Menu"))[0],"BasePriceGold",88);
                Check.Equal(1,Field<int>(((IList)Field<object>(Read(store),"Menu"))[0],"BasePriceGold"));
                Check.Equal(0,store.Snapshot().NpcInventories.Count);Check.Equal(0,store.Snapshot().ObservedVisits.Count);
            }));
            cases.Add(("Inn quote derives native base price and binds holiday adjustment to service day",()=>{
                var store=Store(out var keeper);var catalog=Seal(Draft(keeper.Id));
                Check.Equal(3,Field<int>(Quote(catalog,3,10,0),"PriceGold"));
                var holiday=Quote(catalog,3,10,37);Check.Equal(1,Field<int>(holiday,"PriceGold"));
                Check.Equal(3,Field<int>(Quote(catalog,3,11,0),"PriceGold"));
                Check.Equal(Field<string>(catalog,"Fingerprint"),Field<string>(holiday,"CatalogFingerprint"));
                Check.Throws<ArgumentException>(()=>Quote(catalog,3,10,37,11*86400d+12*3600));
                Check.Throws<ArgumentException>(()=>Quote(catalog,3,10,0,10*86400d+7*3600));
                Check.Throws<ArgumentException>(()=>Quote(catalog,11,10,0));
            }));
            cases.Add(("Catalog fingerprint prevents edited quotes or route geometry from reusing old proof",()=>{
                var store=Store(out var keeper);var catalog=Seal(Draft(keeper.Id));string fingerprint=Field<string>(catalog,"Fingerprint");
                Set(((IList)Field<object>(catalog,"Menu"))[3],"BasePriceGold",5);
                Check.Throws<ArgumentException>(()=>Copy(catalog));
                Check.Throws<ArgumentException>(()=>Quote(catalog,3,10,0));
                var replacement=Seal(catalog);Check.True(Field<string>(replacement,"Fingerprint")!=fingerprint);
                catalog=Seal(Draft(keeper.Id));Set(((IList)Field<object>(catalog,"EntryToCounter"))[1],"X",2.1d);
                Check.Throws<ArgumentException>(()=>Copy(catalog));
            }));
            cases.Add(("Catalog requires real compatible pair geometry and connected service routes",()=>{
                var store=Store(out var keeper);var draft=Draft(keeper.Id);var pair=((IList)Field<object>(draft,"Pairs"))[0];
                Set(Field<object>(pair,"Second"),"FacingX",1d);Check.Throws<ArgumentException>(()=>Seal(draft));
                draft=Draft(keeper.Id);pair=((IList)Field<object>(draft,"Pairs"))[0];Set(Field<object>(pair,"Second"),"TableId","other-table");
                Check.Throws<ArgumentException>(()=>Seal(draft));
                draft=Draft(keeper.Id);pair=((IList)Field<object>(draft,"Pairs"))[0];
                Set(((IList)Field<object>(pair,"FirstCounterToSeat"))[0],"X",2.5d);
                Check.Throws<ArgumentException>(()=>Seal(draft));
            }));
            cases.Add(("Catalog rejects incomplete native menus and mismatched food/drink slots",()=>{
                var store=Store(out var keeper);var draft=Draft(keeper.Id);((IList)Field<object>(draft,"Menu")).RemoveAt(10);
                Check.Throws<ArgumentException>(()=>Seal(draft));draft=Draft(keeper.Id);
                var item=((IList)Field<object>(draft,"Menu"))[0];Set(item,"Kind",NpcInnConsumptionKind.Food);
                Check.Throws<ArgumentException>(()=>Seal(draft));
            }));
            cases.Add(("Catalog restore keeps native source and rejects future data in schema72",()=>{
                var store=Store(out var keeper);var catalog=Seal(Draft(keeper.Id));Publish(store,catalog);
                var serializer=new FullSerializer.fsSerializer();FullSerializer.fsData data;
                Check.True(serializer.TrySerialize(store.Snapshot(),out data).Succeeded);WorldState decoded=null;
                Check.True(serializer.TryDeserialize(data,ref decoded).Succeeded);var restored=new WorldStateStore(0);restored.Restore(decoded);
                Check.Equal(Field<string>(catalog,"Fingerprint"),Field<string>(Read(restored),"Fingerprint"));
                var saved=store.Snapshot();saved.Version=72;Check.Throws<ArgumentException>(()=>restored.Restore(saved));
                ((IList)typeof(WorldState).GetField("NpcInnServiceCatalogs").GetValue(saved)).Clear();restored.Restore(saved);Check.True(Read(restored)==null);
            }));
            cases.Add(("Catalog cannot claim a different keeper building or duplicate its saved owner",()=>{
                var store=Store(out var keeper);var draft=Draft(keeper.Id);Set(draft,"BuildingKey",10);
                Check.Throws<ArgumentException>(()=>Publish(store,Seal(draft)));Check.True(Read(store)==null);
                var catalog=Seal(Draft(keeper.Id));Publish(store,catalog);var saved=store.Snapshot();
                ((IList)typeof(WorldState).GetField("NpcInnServiceCatalogs").GetValue(saved)).Add(Copy(catalog));
                Check.Throws<ArgumentException>(()=>store.Restore(saved));
                Check.Equal(Field<string>(catalog,"Fingerprint"),Field<string>(Read(store),"Fingerprint"));
            }));
        }
    }
}
