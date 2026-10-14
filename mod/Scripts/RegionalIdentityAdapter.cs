using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
namespace LivingCities.DFU
{
    public static class RegionalIdentityAdapter
    {
        public static void Initialize(SettlementPopulation population)
        {
            var gps=GameManager.Instance.PlayerGPS;
            var bank=gps.CurrentRegionIndex>=0 ? (NameHelper.BankTypes)MapsFile.RegionRaces[gps.CurrentRegionIndex] : NameHelper.BankTypes.Breton;
            var people=gps.ClimateSettings.People;
            int race=(int)(people==FactionFile.FactionRaces.Redguard ? Races.Redguard :
                people==FactionFile.FactionRaces.Nord ? Races.Nord : Races.Breton);
            uint previous=DFRandom.Seed;
            try
            {
                foreach(var home in population.Households)
                {
                    if(home.Surname!=null && home.Residents.TrueForAll(r=>r.FullName!=null && r.Race>=0)) continue;
                    var head=home.Residents.Find(r=>r.Id==home.HeadId);
                    int familyRace=head!=null && head.Race>=0 ? head.Race : race;
                    RegionalIdentity.Initialize(home,familyRace,(seed,sex)=>{
                        DFRandom.Seed=seed;
                        return DaggerfallUnity.Instance.NameHelper.FirstName(bank,sex==ResidentSex.Male ? Genders.Male : Genders.Female);
                    },seed=>{ DFRandom.Seed=seed; return DaggerfallUnity.Instance.NameHelper.Surname(bank); });
                }
            }
            finally { DFRandom.Seed=previous; }
        }
    }
}
