using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class NpcInnMenuItem
    {
        public int MenuIndex,BasePriceGold;
        public NpcInnConsumptionKind Kind;
        public string ProductName;
    }
    [Serializable]
    public sealed class NpcInnServiceSeatPair
    {
        public TableSeatPosition First,Second;
        public List<GuardPoint3> FirstCounterToSeat,SecondCounterToSeat,FirstSeatToEntry,SecondSeatToEntry;
    }
    [Serializable]
    public sealed class NpcInnServiceTariff
    {
        // Supplied by the native calendar adapter for the service timestamp,
        // including historical timestamps after a world-time jump.
        public string Provenance="native-calendar";
        public int AbsoluteDay,RegionIndex,HolidayId;
    }
    public sealed class NpcInnMenuQuote
    {
        public string CatalogFingerprint,KeeperId,ProductName;
        public int MenuIndex,PriceGold,AbsoluteDay,HolidayId;
        public NpcInnConsumptionKind Kind;
    }
    [Serializable]
    public sealed class NpcInnServiceCatalog
    {
        public int ModelVersion=1,SettlementId,BuildingKey,BlockIndex,RecordIndex,Climate,RegionIndex,KeeperHash,OpenMinute,CloseMinute;
        public string KeeperId,Fingerprint;
        public double CapturedAt;
        public List<NpcInnMenuItem> Menu=new List<NpcInnMenuItem>();
        public List<GuardPoint3> EntryToCounter=new List<GuardPoint3>();
        public List<NpcInnServiceSeatPair> Pairs=new List<NpcInnServiceSeatPair>();

        public static NpcInnServiceCatalog Seal(NpcInnServiceCatalog source)
        {var copy=CopyData(source);copy.Fingerprint=Hash(copy);return copy;}
        // Unity world/local round trips can differ by a few micrometres. Apply
        // this only to a newly measured draft before sealing. Persisted catalogs
        // keep their original payload and fingerprint during Copy().
        public static void CanonicalizeMeasuredRoutes(NpcInnServiceCatalog source)
        {
            if(source==null || source.EntryToCounter==null || source.EntryToCounter.Count<2 || source.Pairs==null)
                throw new ArgumentException("Incomplete measured Inn service routes.");
            var entry=source.EntryToCounter[0];var counter=source.EntryToCounter[source.EntryToCounter.Count-1];
            foreach(var pair in source.Pairs)
            {
                if(pair==null || pair.First==null || pair.Second==null || !RoutePair(pair.FirstCounterToSeat,pair.FirstSeatToEntry) ||
                    !RoutePair(pair.SecondCounterToSeat,pair.SecondSeatToEntry) ||
                    !Connected(counter,pair.FirstCounterToSeat[0]) || !Connected(counter,pair.SecondCounterToSeat[0]) ||
                    !Connected(entry,pair.FirstSeatToEntry[pair.FirstSeatToEntry.Count-1]) ||
                    !Connected(entry,pair.SecondSeatToEntry[pair.SecondSeatToEntry.Count-1]) ||
                    !Connected(pair.FirstCounterToSeat[pair.FirstCounterToSeat.Count-1],pair.FirstSeatToEntry[0]) ||
                    !Connected(pair.SecondCounterToSeat[pair.SecondCounterToSeat.Count-1],pair.SecondSeatToEntry[0]) ||
                    !AtConnectedSeat(pair.FirstSeatToEntry[0],pair.First) || !AtConnectedSeat(pair.SecondSeatToEntry[0],pair.Second))
                    throw new ArgumentException("Inn catalog routes do not connect the real entry, counter and selected seats.");
                pair.FirstCounterToSeat[0]=Point(counter);pair.SecondCounterToSeat[0]=Point(counter);
                pair.FirstSeatToEntry[pair.FirstSeatToEntry.Count-1]=Point(entry);
                pair.SecondSeatToEntry[pair.SecondSeatToEntry.Count-1]=Point(entry);
                CanonicalizeSeatJoin(pair.FirstCounterToSeat,pair.FirstSeatToEntry,pair.First);
                CanonicalizeSeatJoin(pair.SecondCounterToSeat,pair.SecondSeatToEntry,pair.Second);
            }
        }
        public static NpcInnServiceCatalog Copy(NpcInnServiceCatalog source)
        {
            var copy=CopyData(source);copy.Fingerprint=Hash(copy);
            if(copy.Fingerprint!=source.Fingerprint)throw new ArgumentException("Inn catalog payload does not match its source fingerprint.");
            return copy;
        }
        public static NpcInnMenuQuote Quote(NpcInnServiceCatalog source,int menuIndex,double serviceAt,NpcInnServiceTariff tariff)
        {
            var catalog=Copy(source);
            if(menuIndex<0 || menuIndex>=11 || !Time(serviceAt) || tariff==null || tariff.Provenance!="native-calendar" ||
                tariff.AbsoluteDay<0 || tariff.AbsoluteDay!=Math.Floor(serviceAt/86400d) || tariff.RegionIndex!=catalog.RegionIndex ||
                tariff.HolidayId<0 || tariff.HolidayId>53)
                throw new ArgumentException("Inn quote needs a valid native tariff for the exact service day and region.");
            double minute=serviceAt%86400d/60d;
            bool open=catalog.OpenMinute<catalog.CloseMinute?minute>=catalog.OpenMinute && minute<catalog.CloseMinute:
                minute>=catalog.OpenMinute || minute<catalog.CloseMinute;
            if(!open)throw new ArgumentException("Inn service is outside its native hours.");
            var item=catalog.Menu[menuIndex];
            return new NpcInnMenuQuote{CatalogFingerprint=catalog.Fingerprint,KeeperId=catalog.KeeperId,ProductName=item.ProductName,
                MenuIndex=menuIndex,Kind=item.Kind,AbsoluteDay=tariff.AbsoluteDay,HolidayId=tariff.HolidayId,
                PriceGold=tariff.HolidayId==37?Math.Max(1,item.BasePriceGold/2):item.BasePriceGold};
        }
        private static NpcInnServiceCatalog CopyData(NpcInnServiceCatalog source)
        {
            if(source==null || source.ModelVersion!=1 || source.SettlementId<0 || source.BuildingKey<0 || source.BlockIndex<0 ||
                source.RecordIndex<0 || source.RecordIndex>16383 || source.Climate<0 || source.Climate>65535 ||
                source.RegionIndex<0 || source.RegionIndex>255 || !Text(source.KeeperId,240) || !Time(source.CapturedAt) ||
                source.OpenMinute<0 || source.OpenMinute>=1440 || source.CloseMinute<0 || source.CloseMinute>1440 ||
                source.OpenMinute==source.CloseMinute || source.Menu==null || source.Menu.Count!=11 ||
                source.Pairs==null || source.Pairs.Count>32 || source.EntryToCounter==null ||
                source.Pairs.Count>0 && source.EntryToCounter.Count<2)
                throw new ArgumentException("Incomplete native Inn catalog source, hours or menu.");
            var copy=new NpcInnServiceCatalog{ModelVersion=source.ModelVersion,SettlementId=source.SettlementId,BuildingKey=source.BuildingKey,
                BlockIndex=source.BlockIndex,RecordIndex=source.RecordIndex,Climate=source.Climate,RegionIndex=source.RegionIndex,
                KeeperId=source.KeeperId,KeeperHash=source.KeeperHash,CapturedAt=source.CapturedAt,OpenMinute=source.OpenMinute,CloseMinute=source.CloseMinute};
            for(int i=0;i<11;i++)
            {
                var item=source.Menu[i];
                if(item==null || item.MenuIndex!=i || !NpcInnConsumption.MatchesNativeMenuKind(item.Kind,i) ||
                    !Text(item.ProductName,80) || item.BasePriceGold<1 || item.BasePriceGold>255)
                    throw new ArgumentException("Inn catalog does not preserve the complete native menu.");
                copy.Menu.Add(new NpcInnMenuItem{MenuIndex=i,Kind=item.Kind,ProductName=item.ProductName,BasePriceGold=item.BasePriceGold});
            }
            int budget=20000;
            if(source.EntryToCounter.Count==0 && source.Pairs.Count==0)return copy;
            copy.EntryToCounter=Route(source.EntryToCounter,ref budget);
            var entry=copy.EntryToCounter[0];var counter=copy.EntryToCounter[copy.EntryToCounter.Count-1];
            var pairs=new HashSet<string>(StringComparer.Ordinal);
            var seats=new Dictionary<string,TableSeatPosition>(StringComparer.Ordinal);
            var grounds=new Dictionary<string,GuardPoint3>(StringComparer.Ordinal);
            foreach(var pair in source.Pairs)
            {
                if(pair==null || !SharedTableSeats.CanShare(pair.First,pair.Second))
                    throw new ArgumentException("Inn catalog lacks two compatible native table seats.");
                var first=Seat(pair.First);var second=Seat(pair.Second);
                string a=first.Id,b=second.Id;if(StringComparer.Ordinal.Compare(a,b)>0){a=second.Id;b=first.Id;}
                if(!pairs.Add(a.Length.ToString(CultureInfo.InvariantCulture)+":"+a+b))throw new ArgumentException("Duplicate Inn seat pair.");
                var p=new NpcInnServiceSeatPair{First=first,Second=second,FirstCounterToSeat=Route(pair.FirstCounterToSeat,ref budget),
                    SecondCounterToSeat=Route(pair.SecondCounterToSeat,ref budget),FirstSeatToEntry=Route(pair.FirstSeatToEntry,ref budget),
                    SecondSeatToEntry=Route(pair.SecondSeatToEntry,ref budget)};
                if(!Same(counter,p.FirstCounterToSeat[0]) || !Same(counter,p.SecondCounterToSeat[0]) ||
                    !Same(entry,p.FirstSeatToEntry[p.FirstSeatToEntry.Count-1]) || !Same(entry,p.SecondSeatToEntry[p.SecondSeatToEntry.Count-1]) ||
                    !Same(p.FirstCounterToSeat[p.FirstCounterToSeat.Count-1],p.FirstSeatToEntry[0]) ||
                    !Same(p.SecondCounterToSeat[p.SecondCounterToSeat.Count-1],p.SecondSeatToEntry[0]) ||
                    !AtSeat(p.FirstSeatToEntry[0],first) || !AtSeat(p.SecondSeatToEntry[0],second))
                    throw new ArgumentException("Inn catalog routes do not connect the real entry, counter and selected seats.");
                RememberSeat(seats,grounds,first,p.FirstSeatToEntry[0]);RememberSeat(seats,grounds,second,p.SecondSeatToEntry[0]);
                copy.Pairs.Add(p);
            }
            return copy;
        }
        private static void RememberSeat(Dictionary<string,TableSeatPosition> seats,Dictionary<string,GuardPoint3> grounds,
            TableSeatPosition seat,GuardPoint3 ground)
        {
            TableSeatPosition old;
            if(seats.TryGetValue(seat.Id,out old))
            {
                if(old.TableId!=seat.TableId || old.X!=seat.X || old.Z!=seat.Z || old.Height!=seat.Height ||
                    old.FacingX!=seat.FacingX || old.FacingZ!=seat.FacingZ || !Same(grounds[seat.Id],ground))
                    throw new ArgumentException("One native Inn seat cannot have contradictory poses across pair options.");
            }
            else{seats.Add(seat.Id,seat);grounds.Add(seat.Id,ground);}
        }
        private static TableSeatPosition Seat(TableSeatPosition source)
        {
            if(!Text(source.Id,256) || !Text(source.TableId,256) || !Number(source.X) || !Number(source.Z) || !Number(source.Height) ||
                !Number(source.FacingX) || !Number(source.FacingZ))throw new ArgumentException("Invalid Inn seat metadata.");
            return new TableSeatPosition{Id=source.Id,TableId=source.TableId,X=source.X,Z=source.Z,Height=source.Height,FacingX=source.FacingX,FacingZ=source.FacingZ};
        }
        private static List<GuardPoint3> Route(List<GuardPoint3> source,ref int budget)
        {
            if(source==null || source.Count<2 || source.Count>10000 || (budget-=source.Count)<0)throw new ArgumentException("Missing or unbounded Inn service route.");
            var result=new List<GuardPoint3>();double length=0;
            foreach(var p in source)
            {
                if(p==null || !Number(p.X) || !Number(p.Y) || !Number(p.Z))throw new ArgumentException("Invalid Inn service route point.");
                if(result.Count>0){var previous=result[result.Count-1];double x=p.X-previous.X,y=p.Y-previous.Y,z=p.Z-previous.Z;length+=Math.Sqrt(x*x+y*y+z*z);}
                result.Add(new GuardPoint3{X=p.X,Y=p.Y,Z=p.Z});
            }
            if(length<=0 || length>100000)throw new ArgumentException("Inn service route has no measured movement.");
            return result;
        }
        private static void CanonicalizeSeatJoin(List<GuardPoint3> fromCounter,List<GuardPoint3> toEntry,TableSeatPosition seat)
        {
            var ground=toEntry[0];
            var canonical=new GuardPoint3{X=seat.X,Y=ground.Y,Z=seat.Z};
            fromCounter[fromCounter.Count-1]=Point(canonical);toEntry[0]=Point(canonical);
        }
        private static bool RoutePair(List<GuardPoint3> fromCounter,List<GuardPoint3> toEntry)
        {return fromCounter!=null && fromCounter.Count>=2 && toEntry!=null && toEntry.Count>=2;}
        private static GuardPoint3 Point(GuardPoint3 source){return new GuardPoint3{X=source.X,Y=source.Y,Z=source.Z};}
        private const double CoordinateRoundTripTolerance=0.00001;
        private static bool Connected(GuardPoint3 a,GuardPoint3 b){return Math.Abs(a.X-b.X)<=CoordinateRoundTripTolerance &&
            Math.Abs(a.Y-b.Y)<=CoordinateRoundTripTolerance && Math.Abs(a.Z-b.Z)<=CoordinateRoundTripTolerance;}
        private static bool AtConnectedSeat(GuardPoint3 p,TableSeatPosition seat){return Math.Abs(p.X-seat.X)<=CoordinateRoundTripTolerance &&
            Math.Abs(p.Z-seat.Z)<=CoordinateRoundTripTolerance;}
        private static bool AtSeat(GuardPoint3 p,TableSeatPosition seat){return Math.Abs(p.X-seat.X)<=0.000001 && Math.Abs(p.Z-seat.Z)<=0.000001;}
        private static bool Same(GuardPoint3 a,GuardPoint3 b){return Math.Abs(a.X-b.X)<=0.000001 && Math.Abs(a.Y-b.Y)<=0.000001 && Math.Abs(a.Z-b.Z)<=0.000001;}
        private static bool Time(double number){return !double.IsNaN(number) && !double.IsInfinity(number) && number>=0;}
        private static bool Number(double number){return !double.IsNaN(number) && !double.IsInfinity(number) && Math.Abs(number)<=1000000;}
        private static bool Text(string text,int max){return !string.IsNullOrWhiteSpace(text) && text.Length<=max;}
        private static string Hash(NpcInnServiceCatalog source)
        {
            var text=new StringBuilder("native-inn-service:1:");
            Action<string> add=value=>text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
            Action<double> number=value=>add(value==0?"0":value.ToString("G15",CultureInfo.InvariantCulture));
            Action<List<GuardPoint3>> route=points=>{number(points.Count);foreach(var p in points){number(p.X);number(p.Y);number(p.Z);}};
            Action<TableSeatPosition> seat=p=>{add(p.Id);add(p.TableId);number(p.X);number(p.Z);number(p.Height);number(p.FacingX);number(p.FacingZ);};
            number(source.SettlementId);number(source.BuildingKey);number(source.BlockIndex);number(source.RecordIndex);number(source.Climate);number(source.RegionIndex);
            add(source.KeeperId);number(source.KeeperHash);number(source.OpenMinute);number(source.CloseMinute);
            foreach(var item in source.Menu){number(item.MenuIndex);number((int)item.Kind);add(item.ProductName);number(item.BasePriceGold);}
            route(source.EntryToCounter);number(source.Pairs.Count);
            foreach(var pair in source.Pairs){seat(pair.First);seat(pair.Second);route(pair.FirstCounterToSeat);route(pair.SecondCounterToSeat);route(pair.FirstSeatToEntry);route(pair.SecondSeatToEntry);}
            using(var sha=SHA256.Create()){var result=new StringBuilder();foreach(byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())))result.Append(b.ToString("x2",CultureInfo.InvariantCulture));return result.ToString();}
        }
    }
    public sealed partial class WorldStateStore
    {
        public NpcInnServiceCatalog ReadNpcInnServiceCatalog(int settlement,int building)
        {var catalog=state.NpcInnServiceCatalogs.Find(c=>c.SettlementId==settlement && c.BuildingKey==building);return catalog==null?null:NpcInnServiceCatalog.Copy(catalog);}
        public void RegisterNpcInnServiceCatalog(NpcInnServiceCatalog source)
        {
            var copy=NpcInnServiceCatalog.Copy(source);ValidateInnCatalogKeeper(state,copy,true);
            int index=state.NpcInnServiceCatalogs.FindIndex(c=>c.SettlementId==copy.SettlementId && c.BuildingKey==copy.BuildingKey);
            if(index>=0)
            {
                var old=state.NpcInnServiceCatalogs[index];if(old.Fingerprint==copy.Fingerprint)return;
                Require(copy.CapturedAt>=old.CapturedAt,"Older Inn survey cannot replace the current catalog.");state.NpcInnServiceCatalogs[index]=copy;
            }
            else{Require(state.NpcInnServiceCatalogs.Count<100000,"Inn service catalog ledger is full.");state.NpcInnServiceCatalogs.Add(copy);}
        }
        private static void ValidateInnCatalogKeeper(WorldState world,NpcInnServiceCatalog catalog,bool alive)
        {Require(world.NativePeople.Exists(p=>p.Id==catalog.KeeperId && p.SettlementId==catalog.SettlementId && p.WorkKey==catalog.BuildingKey &&
            p.NativeHash==catalog.KeeperHash && (!alive || p.IsAlive)),"Inn catalog keeper does not match its native building source.");}
        private static List<NpcInnServiceCatalog> CopyNpcInnServiceCatalogs(WorldState source,WorldState validated)
        {
            var result=new List<NpcInnServiceCatalog>();
            Require(source.NpcInnServiceCatalogs!=null && source.NpcInnServiceCatalogs.Count<=100000,"Missing or unbounded Inn catalogs.");
            var owners=new HashSet<string>(StringComparer.Ordinal);
            foreach(var sourceCatalog in source.NpcInnServiceCatalogs)
            {var copy=NpcInnServiceCatalog.Copy(sourceCatalog);Require(owners.Add(copy.SettlementId+":"+copy.BuildingKey),"Duplicate Inn service catalog owner.");ValidateInnCatalogKeeper(validated,copy,false);result.Add(copy);}
            return result;
        }
    }
}
