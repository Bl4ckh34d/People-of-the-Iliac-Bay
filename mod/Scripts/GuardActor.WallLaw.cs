using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardActor
    {
        // Only the exact native HandleAttackFromSource callback supplies this
        // target. A global hostility update is not evidence of a guard hit.
        internal DaggerfallEntityBehaviour WallAttacker { get; private set; }
        internal bool WallLawActive { get; private set; }
        internal void RememberWallAttacker(DaggerfallEntityBehaviour attacker)
        { WallAttacker=attacker; }

        private bool OfferWallArrest(DaggerfallEntityBehaviour target)
        {
            var game=GameManager.Instance;var player=game.PlayerEntity;
            if(target!=game.PlayerEntityBehaviour || target.Entity.IsInvisible ||
                player.CrimeCommitted==PlayerEntity.Crimes.None || player.HaveShownSurrenderToGuardsDialogue ||
                GameManager.IsGamePaused || !CivilianResponses.Sees(transform.position,target.transform.position,20))return false;
            // Vanilla offers this window only after a damaging hit. A wall
            // guard can make visible contact without walking off its post.
            // Reuse native text, pursuit flag, reputation and court admission.
            player.LowerRepForCrime();
            var offer=new DaggerfallMessageBox(DaggerfallUI.UIManager);
            offer.SetTextTokens(DaggerfallUnity.Instance.TextProvider.GetRSCTokens(15));
            offer.ParentPanel.BackgroundColor=Color.clear;
            offer.AddButton(DaggerfallMessageBox.MessageBoxButtons.Yes);
            offer.AddButton(DaggerfallMessageBox.MessageBoxButtons.No);
            offer.OnButtonClick+=(sender,button)=> {
                sender.CloseWindow();
                if(button==DaggerfallMessageBox.MessageBoxButtons.Yes)player.SurrenderToCityGuards(true);
                // No hit has occurred. A refusal leaves actual native combat
                // to deal damage rather than inventing a hit for this contact.
            };
            player.HaveShownSurrenderToGuardsDialogue=true;
            offer.Show();return true;
        }
    }
}
