using System;
using System.Globalization;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        // Called after D3 has accepted a typed paid-Follow candidate. The quote remains
        // visible until the player answers the native yes/no box; no gold moves beforehand.
        private bool TryBeginPaidCompanionConsent(DaggerfallTalkWindow window,string text,
            ConversationContextSnapshot snapshot,int session,string person,string proposalId,
            NpcActionDecision decision)
        {
            if (decision == null || decision.Option == null || !decision.Option.PaidCompanion) return false;
            int dailyGold;
            string quote = "The local NPC scene is no longer available.";
            if (streets == null || !streets.TryQuotePaidCompanion(person,decision,out dailyGold,out quote))
            {
                conversationResponseCue += " Paid companion offer unavailable: " + quote + " No action was accepted.";
                SubmitConversationActionReply(window,text,snapshot,ConversationActionReply.NoChange());
                return true;
            }
            string offer = "Hire this companion for " + dailyGold.ToString(CultureInfo.InvariantCulture) +
                " gold for 24 game hours of Follow and travel? No automatic renewal. " +
                "Dismissal settles earned pay and refunds unused time.";
            var confirm = new PersonalCommerceConfirmBox(offer, accepted =>
            {
                if (!CurrentMemorySession(window,session) || conversationPersonId != person) return;
                if (!accepted)
                {
                    conversationResponseCue += " The player declined the paid companion offer. No action was accepted or payment taken.";
                    SubmitConversationActionReply(window,text,snapshot,ConversationActionReply.NoChange());
                    return;
                }
                string reason = "The local NPC scene is no longer available.";
                bool committed = streets != null && streets.TryHirePaidCompanion(person,proposalId,decision,
                    CommandTurnFor(snapshot),out reason);
                if (committed)
                    conversationResponseCue += " ACTION ACCEPTED AND SAVED: paid Follow for 24 game hours at " +
                        dailyGold.ToString(CultureInfo.InvariantCulture) + " gold. " + reason +
                        " Acknowledge only accepted intent; do not claim physical arrival or automatic renewal.";
                else
                {
                    conversationResponseCue += " Paid companion hire failed: " + reason +
                        " Do not claim agreement or payment unless the native service ledger confirms it.";
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_PAID_COMPANION_REJECTED: " + reason);
                }
                SubmitConversationActionReply(window,text,snapshot,
                    SavedConversationActionReply(snapshot,proposalId,false,committed,NpcActionKind.Follow));
            });
            DaggerfallUI.UIManager.PushWindow(confirm);
            return true;
        }
    }
}
