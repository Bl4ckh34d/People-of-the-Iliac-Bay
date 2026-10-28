using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private static IEnumerable<ConversationContextTurn> ReadConversationContextHistory(DaggerfallTalkWindow window)
        {
            var transcript = window == null || conversationListField == null ? null : conversationListField.GetValue(window) as ListBox;
            var turns = new List<ConversationContextTurn>();
            if (transcript == null) return turns;
            for (int i = 0; i < transcript.Count; i++)
            {
                var row = transcript.GetItem(i);
                if (row == null || row.textLabel == null || string.IsNullOrWhiteSpace(row.textLabel.Text) ||
                    row.textLabel.Name == "PeopleOfTheIliacBayHistoryDay" || row.textLabel.Name == "PeopleOfTheIliacBayHistoryRule" ||
                    row.textLabel.Name == UnavailableTranscriptRowName) continue;
                turns.Add(new ConversationContextTurn(row.textColor == DaggerfallUI.DaggerfallQuestionTextColor
                    ? ConversationTextSpeaker.Player : ConversationTextSpeaker.Npc, row.textLabel.Text));
            }
            return turns;
        }

        private void CancelConversationWork()
        {
            CancelNpcExperienceAppraisal();
            if (localDialogue) localDialogue.Cancel();
        }

        private void OnDisable()
        {
            if (splitPortraitOverlay) splitPortraitOverlay.DetachAndRestore();
            ResetPortraitPresentation();
            CancelConversationWork();
            if (breezeTtsTrial) breezeTtsTrial.EndConversation(conversationContextSessionId);
        }
    }
}
