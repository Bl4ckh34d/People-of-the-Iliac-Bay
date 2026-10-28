using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeInnOccupants
    {
        // Called only after native paid receipts have been reconciled.
        public static void Claim(WorldStateStore store,BuildingDirectory directory,StaticDoor entry,
            List<LodgingBed> beds,Dictionary<int,string> occupants,double now,int? keeperHash=null)
        { Claim(store,directory.MapID,directory.LocationData,entry,beds,occupants,now,keeperHash); }
        public static void Claim(WorldStateStore store,int settlement,DFLocation location,StaticDoor entry,
            List<LodgingBed> beds,Dictionary<int,string> occupants,double now,int? keeperHash=null)
        {
            if(location.RegionIndex<0 || location.RegionIndex>=MapsFile.RegionRaces.Length)
                throw new System.ArgumentException("Invalid destination region.");
            var game=GameManager.Instance;
            var dfu=DaggerfallUnity.Instance;
            DFBlock block;
            long blockStarted=RuntimeTiming.Start("inn-occupants-block-read");
            try { block=dfu.ContentReader.BlockFileReader.GetBlock(entry.blockIndex); }
            finally { RuntimeTiming.End("inn-occupants-block-read",blockStarted); }
            uint previous=DFRandom.Seed;
            long claimStarted=RuntimeTiming.Start("inn-occupants-claim");
            try
            {
                foreach(var record in block.RmbBlock.SubRecords[entry.recordIndex].Interior.BlockPeopleRecords)
                {
                    string bed;
                    int hash=StaticNPC.GetPositionHash(record.XPos,record.YPos,record.ZPos);
                    bool assignedBed=occupants.TryGetValue(hash,out bed) && beds.Exists(value=>value.Id==bed);
                    if(!assignedBed && (!keeperHash.HasValue || hash!=keeperHash.Value)) continue;
                    FactionFile.FactionData faction;
                    if(game.PlayerEntity.FactionData.GetFactionData(record.FactionID,out faction) &&
                        faction.type==(int)FactionFile.FactionTypes.Individual) continue;
                    var data=new StaticNPC.NPCData();
                    StaticNPC.SetLayoutData(ref data,record.XPos,record.YPos,record.ZPos,record.Flags,record.FactionID,
                        record.TextureArchive,record.TextureRecord,record.Position,settlement,
                        location.LocationIndex,entry.buildingKey);
                    // Native fallback reads PlayerGPS; remote people belong to the destination.
                    data.race=(Races)(MapsFile.RegionRaces[location.RegionIndex]+1);
                    if(record.FactionID!=0 && game.PlayerEntity.FactionData.GetFactionData(record.FactionID,out faction))
                    {
                        var race=RaceTemplate.GetRaceFromFactionRace((FactionFile.FactionRaces)faction.race);
                        if(race!=Races.None) data.race=race;
                    }
                    DFRandom.srand(data.nameSeed);
                    string name=dfu.NameHelper.FullName((NameHelper.BankTypes)MapsFile.RegionRaces[location.RegionIndex],data.gender);
                    var person=store.RegisterNativePerson(settlement,entry.buildingKey,hash,name,
                        data.gender==Genders.Male ? ResidentSex.Male : ResidentSex.Female,(int)data.race,
                        data.factionID,data.billboardArchiveIndex,data.billboardRecordIndex);
                    // Only loaded, verified retirement releases an authored claim.
                    // Passive faction metadata alone cannot exclude quest/actions.
                    if(person.IsAlive && assignedBed) store.TryClaimLodgingBed(settlement,entry.buildingKey,person.Id,bed,now);
                }
            }
            finally { DFRandom.Seed=previous; RuntimeTiming.End("inn-occupants-claim",claimStarted); }
        }
    }
}
