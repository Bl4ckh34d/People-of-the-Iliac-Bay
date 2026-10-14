using System;
namespace LivingCities.Core
{
    public static class RegionalIdentity
    {
        public static void Initialize(Household home,int race,Func<uint,ResidentSex,string> firstName,Func<uint,string> surname)
        {
            if(home==null || home.Residents==null || race<0 || race>255 || firstName==null || surname==null)
                throw new ArgumentException("Invalid household identity inputs.");
            string family=home.Surname ?? surname(home.SurnameSeed);
            if(string.IsNullOrWhiteSpace(family)) throw new ArgumentException("Missing family surname.");
            var names=new string[home.Residents.Count];
            for(int i=0;i<names.Length;i++)
            {
                var resident=home.Residents[i];
                names[i]=resident.FullName ?? firstName(resident.AppearanceSeed,resident.Sex)+" "+family;
                if(string.IsNullOrWhiteSpace(names[i]) || names[i].Length>256) throw new ArgumentException("Invalid resident name.");
            }
            home.Surname=family;
            for(int i=0;i<names.Length;i++)
            {
                home.Residents[i].FullName=names[i];
                if(home.Residents[i].Race<0) home.Residents[i].Race=race;
            }
        }
    }
}
