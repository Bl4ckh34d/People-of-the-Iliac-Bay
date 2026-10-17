using System;
using System.Reflection;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Game;
using LivingCities.Core;

namespace LivingCities.DFU
{
    public sealed partial class LivingCitiesMod
    {
        private static readonly FieldInfo conversationNpcDataField = typeof(TalkManager).GetField(
            "npcData", BindingFlags.Instance | BindingFlags.NonPublic);

        private static int CaptureDialogueFactionId(TalkManager manager)
        {
            try
            {
                if (manager == null || conversationNpcDataField == null) return -1;
                object npcData = conversationNpcDataField.GetValue(manager);
                if (npcData == null) return -1;
                FieldInfo factionField = npcData.GetType().GetField("factionData", BindingFlags.Instance | BindingFlags.Public);
                if (factionField == null) return -1;
                object faction = factionField.GetValue(npcData);
                if (!(faction is FactionFile.FactionData)) return -1;
                return ((FactionFile.FactionData)faction).id;
            }
            catch { return -1; }
        }

        internal bool TryGetFreshConversationDisposition(string personId, out int disposition)
        {
            disposition = 0;
            if (saveFault || !options.Enabled || !conversationDispositionEnabled || store == null ||
                string.IsNullOrWhiteSpace(personId) || GameManager.Instance == null || !GameManager.Instance.IsReady)
                return false;

            int capturedBaseline, offset, factionId;
            if (!store.TryGetConversationBaseline(personId, out capturedBaseline, out offset, out factionId)) return false;
            var game = GameManager.Instance;
            FactionFile.FactionData currentFaction;
            if (!game.PlayerEntity.FactionData.GetFactionData(factionId, out currentFaction)) return false;

            int currentBaseline = game.TalkManager.GetReactionToPlayer(currentFaction);
            if (currentBaseline < -100) currentBaseline = -100;
            else if (currentBaseline > 100) currentBaseline = 100;
            // A changed player or faction reaction invalidates the cached native baseline.
            if (currentBaseline != capturedBaseline) return false;
            disposition = ConversationEffects.Reaction(currentBaseline, offset);
            return true;
        }
    }
}
