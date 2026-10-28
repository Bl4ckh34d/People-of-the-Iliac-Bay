using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        internal void InterruptResidentInteractionForFlight(ResidentActor actor)
        {
            if(!actor || actor.Resident==null)return;
            bool ownsTalk=conversationActor==actor || conversationPersonId==actor.Resident.Id &&
                DaggerfallUI.UIManager.ContainsWindow(DaggerfallUI.Instance.TalkWindow);
            bool ownsTrade=personalCommerceCatalog!=null && personalCommerceCatalog.PersonId==actor.Resident.Id ||
                personalCommerceBeforeClose!=null && memoryPersonId==actor.Resident.Id;
            if(!ownsTalk && !ownsTrade)return;
            if(localDialogue)localDialogue.Cancel();
            InvalidatePendingConversationAction();InvalidatePersonalCommerceCatalog();
            DaggerfallUI.Instance.PopToHUD();
            GameManager.Instance.PauseGame(false);
        }
    }
}
