using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcKnownSpellConversationContextTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Known spell dialogue context reports exact affordable native spells",ReportsExactAffordableSpells));
            cases.Add(("Known spell dialogue context does not infer a missing affordable spell",ReportsNoAffordableSpell));
        }

        private static NpcCompanionCastSpec Spell(string effect,int cost,NpcCompanionCastDelivery delivery=NpcCompanionCastDelivery.Self)
        {return new NpcCompanionCastSpec {SpellId="native:spellbook:"+effect,EffectId=effect,
            EffectKind=effect=="Heal-Health"?NpcMagicEffectKind.Health:effect=="Shield"?NpcMagicEffectKind.TimedEffect:NpcMagicEffectKind.Condition,
            TargetId=delivery==NpcCompanionCastDelivery.Self?"resident":"player",Delivery=delivery,MagickaCost=cost};}

        private static void ReportsExactAffordableSpells()
        {
            var heal=Spell("Heal-Health",7);
            var shield=Spell("Shield",12,NpcCompanionCastDelivery.PlayerTouch);
            string context=NpcKnownSpellConversationContext.Format(new[]{heal,shield},new[]{shield});
            Check.True(context.Contains("heal self (7 magicka; not a current action candidate)"));
            Check.True(context.Contains("shield player by touch (12 magicka; currently needed)"));
            Check.True(!context.Contains("Mage") && !context.Contains("Healer"));
        }

        private static void ReportsNoAffordableSpell()
        {
            string context=NpcKnownSpellConversationContext.Format(new NpcCompanionCastSpec[0],new NpcCompanionCastSpec[0]);
            Check.True(context.Contains("no currently affordable healing, cure, or protection spell"));
            Check.True(context.Contains("Do not infer magical ability"));
        }
    }
}
