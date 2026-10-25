using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private string pendingNativeSocialSkillTone;

        private void RememberNativeSocialSkillTone(string tone)
        {
            pendingNativeSocialSkillTone = tone;
        }

        private int CurrentNativeSocialGroup()
        {
            var game = GameManager.Instance;
            if (game == null || game.PlayerEntity == null || game.TalkManager == null)
                return -1;
            int factionId = CaptureDialogueFactionId(game.TalkManager);
            FactionFile.FactionData faction;
            return factionId >= 0 && game.PlayerEntity.FactionData.GetFactionData(factionId, out faction)
                ? faction.sgroup : -1;
        }

        internal void TryAwardNativeSocialSkillUse(WorldStateStore owner, string personId,
            long committedSequence, NpcObservedSpeechReceipt speech, string tone,
            int nativeSocialGroup, double committedAt)
        {
            var game = GameManager.Instance;
            if (owner == null || speech == null || game == null || !game.IsReady ||
                game.PlayerEntity == null || saveFault || !options.Enabled)
                return;
            NativeSocialSkillKind skill;
            if (!owner.TryMarkAcceptedSocialSkillUse(personId, committedSequence,
                speech.Evidence.ReceiptId, nativeSocialGroup, tone, committedAt, out skill))
                return;
            game.PlayerEntity.TallySkill(skill == NativeSocialSkillKind.Etiquette
                ? DFCareer.Skills.Etiquette : DFCareer.Skills.Streetwise, 1);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_SKILL_USE: skill=" + skill +
                "; person=" + personId + "; sequence=" + committedSequence);
        }

        private string CurrentNativeSocialSkillContext(ConversationContextSnapshot snapshot = null)
        {
            var game = GameManager.Instance;
            if (game == null || !game.IsReady || game.PlayerEntity == null || game.TalkManager == null)
                return string.Empty;
            int factionId = CaptureDialogueFactionId(game.TalkManager);
            FactionFile.FactionData faction;
            if (factionId < 0 || !game.PlayerEntity.FactionData.GetFactionData(factionId, out faction))
                return string.Empty;
            var player = game.PlayerEntity;
            int etiquette = player.Skills.GetLiveSkillValue(DFCareer.Skills.Etiquette);
            int streetwise = player.Skills.GetLiveSkillValue(DFCareer.Skills.Streetwise);
            int mercantile = player.Skills.GetLiveSkillValue(DFCareer.Skills.Mercantile);
            int personality = player.Stats.LivePersonality;
            int? trust = null;
            if (snapshot != null)
            {
                if (snapshot.Binding == null || snapshot.Binding.PersonId != conversationPersonId)
                    return string.Empty;
                var captured = snapshot.DirectedRelationship;
                if (captured != null && captured.TrustKnown)
                    trust = captured.ReliabilityTrust;
            }
            else if (!saveFault && store != null && conversationPersonId != null)
            {
                var relationship = store.ReadNpcRelationship(conversationPersonId,
                    NpcDirectedRelationship.PlayerTargetId);
                if (relationship != null && relationship.TrustKnown)
                    trust = relationship.ReliabilityTrust;
            }
            return " " + NativeSocialSkillContext.Format(faction.sgroup, etiquette, streetwise,
                mercantile, personality, trust);
        }
    }
}
