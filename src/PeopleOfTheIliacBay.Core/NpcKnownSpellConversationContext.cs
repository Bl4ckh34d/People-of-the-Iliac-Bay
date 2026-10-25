using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // Presentation only: the native adapter remains the sole reader of a live spellbook.
    public static class NpcKnownSpellConversationContext
    {
        public static string Format(NpcCompanionCastSpec[] knownAffordable,NpcCompanionCastSpec[] currentCandidates)
        {
            if(knownAffordable==null || currentCandidates==null)return string.Empty;
            var descriptions=new List<string>();
            foreach(var source in knownAffordable)
            {
                NpcCompanionCastSpec spell;
                try {spell=NpcCompanionCastSpec.CopyValidated(source);}
                catch(ArgumentException){continue;}
                string label=Label(spell);
                if(label==null)continue;
                string status=Contains(currentCandidates,spell)?"currently needed":"not a current action candidate";
                descriptions.Add(label+" ("+spell.MagickaCost+" magicka; "+status+")");
            }
            if(descriptions.Count==0)
                return "Verified native spellbook: no currently affordable healing, cure, or protection spell. Do not infer magical ability from class, health, or magicka.";
            return "Verified native spellbook; affordable now: "+string.Join("; ",descriptions.ToArray())+
                ". These exact spells do not imply any other magical ability.";
        }

        private static bool Contains(NpcCompanionCastSpec[] candidates,NpcCompanionCastSpec spell)
        {
            foreach(var candidate in candidates) if(NpcCompanionCastSpec.Same(candidate,spell))return true;
            return false;
        }

        private static string Label(NpcCompanionCastSpec spell)
        {
            string effect=spell.EffectId=="Heal-Health"?"heal":spell.EffectId=="Cure-Poison"?"cure poison":
                spell.EffectId=="Cure-Disease"?"cure disease":spell.EffectId=="Shield"?"shield":null;
            if(effect==null)return null;
            return effect+(spell.Delivery==NpcCompanionCastDelivery.Self?" self":" player by touch");
        }
    }
}
