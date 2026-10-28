using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        public NpcBiographyRecord ReadNpcBiography(string personId)
        {
            var record=state.NpcBiographies.Find(item=>item.PersonId==personId);
            return record==null?null:NpcBiographyChat.Copy(record);
        }
        public List<string> ReadNpcFamilyBiography(string familyId)
        {
            if(familyId==null)return null;
            var record=state.NpcBiographies.Find(item=>item.FamilyId==familyId);
            return record==null?null:new List<string>(record.FamilyFacts);
        }
        public NpcBiographyAnchor ReadNpcBiographyAnchor(string personId)
        {return BiographyAnchor(state,personId);}

        private static NpcBiographyAnchor BiographyAnchor(WorldState world,string personId)
        {
            if(string.IsNullOrWhiteSpace(personId))return null;
            foreach(var town in world.Settlements)
                foreach(var home in town.Households)
                {
                    var resident=home.Residents.Find(item=>item.Id==personId);
                    if(resident!=null)return ResidentBiographyAnchor(town.SettlementId,home,resident);
                }
            foreach(var retired in world.HouseholdHistory)
            {
                var resident=retired.Household.Residents.Find(item=>item.Id==personId);
                if(resident!=null)return ResidentBiographyAnchor(retired.SettlementId,retired.Household,resident);
            }
            var native=world.NativePeople.Find(item=>item.Id==personId);
            if(native!=null)
            {
                string anchor=NativeBiographyIdentity(native);
                return new NpcBiographyAnchor(personId,null,anchor,"Native person "+native.Name+"; existing settlement="+
                    native.SettlementId+"; existing home key="+native.HomeKey+". Birthplace and family are not established; do not invent either.");
            }
            // An unbound traveler has no reliable origin/family context for expansion.
            return null;
        }
        private static string NativeBiographyIdentity(NativePerson native)
        {return "native:"+native.Id+":"+native.SettlementId.ToString(CultureInfo.InvariantCulture);}
        private static NpcBiographyAnchor ResidentBiographyAnchor(int settlement,Household home,Resident resident)
        {
            var family=new List<Resident> {resident};
            for(int cursor=0;cursor<family.Count;cursor++)
                foreach(var candidate in home.Residents)
                    if(!family.Contains(candidate) && Related(family[cursor],candidate))family.Add(candidate);
            family.Sort((a,b)=>string.CompareOrdinal(a.Id,b.Id));
            string origin=settlement.ToString(CultureInfo.InvariantCulture)+":"+home.BuildingKey.ToString(CultureInfo.InvariantCulture)+":"+home.Generation.ToString(CultureInfo.InvariantCulture);
            string familyId=family.Count>1?"family:"+origin+":"+family[0].Id:null;
            var context=new StringBuilder("Existing resident ").Append(resident.FullName??resident.Id)
                .Append("; age group=").Append(resident.AgeGroup).Append("; actual origin household=").Append(origin)
                .Append("; partner ID=").Append(resident.PartnerId??"none").Append("; mother ID=").Append(resident.MotherId??"not established")
                .Append("; father ID=").Append(resident.FatherId??"not established")
                .Append(". Birthplace is not established. Existing family members only: ");
            var identities=new StringBuilder(origin);
            foreach(var member in family)
            {
                identities.Append('|').Append(member.Id).Append(':').Append(member.PartnerId).Append(':').Append(member.MotherId).Append(':').Append(member.FatherId);
                context.Append(member.FullName??member.Id).Append(" [").Append(member.Id).Append(", ").Append(member.AgeGroup).Append("]; ");
            }
            return new NpcBiographyAnchor(resident.Id,familyId,identities.ToString(),context.ToString());
        }
        private static bool Related(Resident a,Resident b)
        {
            return a.PartnerId==b.Id || b.PartnerId==a.Id || a.MotherId==b.Id || a.FatherId==b.Id ||
                b.MotherId==a.Id || b.FatherId==a.Id || !string.IsNullOrEmpty(a.MotherId) && a.MotherId==b.MotherId ||
                !string.IsNullOrEmpty(a.FatherId) && a.FatherId==b.FatherId;
        }
        public bool TryCommitNpcBiography(NpcBiographyAnchor expected,NpcBiographyRecord proposed)
        {
            if(expected==null || proposed==null || proposed.FamilyFacts==null)return false;
            var existing=ReadNpcBiography(expected.PersonId);
            var current=ReadNpcBiographyAnchor(expected.PersonId);
            if(current==null || current.IdentityAnchor!=expected.IdentityAnchor || current.FamilyId!=expected.FamilyId ||
                proposed.PersonId!=current.PersonId || proposed.FamilyId!=current.FamilyId || proposed.IdentityAnchor!=current.IdentityAnchor)return false;
            if(existing!=null)
            {
                // A native person can acquire a genuinely surveyed household after first contact.
                // Attach only its actual family; never regenerate existing personal recollections.
                if(existing.FamilyId!=null || current.FamilyId==null)return false;
                proposed=new NpcBiographyRecord {PersonId=proposed.PersonId,FamilyId=proposed.FamilyId,IdentityAnchor=proposed.IdentityAnchor,
                    CareerStart=existing.CareerStart,FormativeExperience=existing.FormativeExperience,Wish=existing.Wish,
                    FamilyFacts=new List<string>(proposed.FamilyFacts),CreatedAt=existing.CreatedAt};
            }
            var shared=ReadNpcFamilyBiography(current.FamilyId);
            if(shared!=null)
            {
                if(proposed.FamilyFacts.Count!=0 && !SameBiographyFacts(shared,proposed.FamilyFacts))return false;
                proposed=new NpcBiographyRecord {PersonId=proposed.PersonId,FamilyId=proposed.FamilyId,IdentityAnchor=proposed.IdentityAnchor,
                    CareerStart=proposed.CareerStart,FormativeExperience=proposed.FormativeExperience,Wish=proposed.Wish,
                    FamilyFacts=shared,CreatedAt=proposed.CreatedAt};
            }
            try
            {
                var saved=NpcBiographyChat.Copy(proposed);
                if(existing==null)state.NpcBiographies.Add(saved);
                else state.NpcBiographies[state.NpcBiographies.FindIndex(item=>item.PersonId==saved.PersonId)]=saved;
                return true;
            }
            catch(ArgumentException){return false;}
        }
        private static bool SameBiographyFacts(IList<string> a,IList<string> b)
        {if(a.Count!=b.Count)return false;for(int i=0;i<a.Count;i++)if(a[i]!=b[i])return false;return true;}
        private static List<NpcBiographyRecord> CopyNpcBiographies(WorldState source,WorldState validated)
        {
            Require(source.NpcBiographies!=null,"Missing NPC biography ledger.");
            var result=new List<NpcBiographyRecord>();var people=new HashSet<string>(StringComparer.Ordinal);
            var families=new Dictionary<string,List<string>>(StringComparer.Ordinal);
            foreach(var row in source.NpcBiographies)
            {
                var record=NpcBiographyChat.Copy(row);bool alive;
                Require(KnownNpcIdentity(validated,record.PersonId,out alive) && people.Add(record.PersonId),"Unknown or duplicate NPC biography identity.");
                var anchor=BiographyAnchor(validated,record.PersonId);
                var native=validated.NativePeople.Find(item=>item.Id==record.PersonId);
                bool nativeBeforeSurvey=record.FamilyId==null && native!=null && record.IdentityAnchor==NativeBiographyIdentity(native);
                Require(nativeBeforeSurvey || anchor!=null && anchor.FamilyId==record.FamilyId && anchor.IdentityAnchor==record.IdentityAnchor,
                    "NPC biography changed its fixed origin or family.");
                if(record.FamilyId!=null)
                {
                    List<string> facts;
                    if(families.TryGetValue(record.FamilyId,out facts))Require(SameBiographyFacts(facts,record.FamilyFacts),"Inconsistent shared family biography.");
                    else families.Add(record.FamilyId,record.FamilyFacts);
                }
                result.Add(record);
            }
            return result;
        }
    }
}
