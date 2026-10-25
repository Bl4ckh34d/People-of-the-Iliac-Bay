using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private const double NpcHealthEpisodeQuietSeconds = 60d;
        private const double NpcHealthEpisodePollSeconds = 5d;
        private WorldStateStore healthEpisodePollStore;
        private object healthEpisodePollEpoch;
        private double nextHealthEpisodePollAt;

        // Parent calls this from Update. It only queues Core evidence; it never invokes a model.
        private void TickNpcHealthExperienceEpisodes()
        {
            var game = DaggerfallUnity.Instance;
            var gameManager = GameManager.Instance;
            if (store == null || saveFault || !options.Enabled || !isActiveAndEnabled ||
                game == null || !game.IsReady || game.WorldTime == null ||
                gameManager == null || !gameManager.IsReady || !gameManager.IsPlayingGame() ||
                GameManager.IsGamePaused || gameManager.PlayerEntity == null)
                return;

            // Do not compete with an open player exchange or another foreground task.
            if (conversationBusy || dialogueWasOpen || localDialogue != null && localDialogue.IsBusy ||
                DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)
                return;

            // This flag is a conservative deferral signal, not an exact combat-state API.
            // DFU raises it for hostile-enemy alerts and clears it on kill or alert expiry.
            if (gameManager.PlayerEntity.EnemyAlertActive)
                return;

            double now = game.WorldTime.Now.ToSeconds();
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                return;

            if (!ReferenceEquals(healthEpisodePollStore, store))
            {
                healthEpisodePollStore = store;
                healthEpisodePollEpoch = null;
                nextHealthEpisodePollAt = now;
            }

            object epoch = store.CaptureNpcVitalsWriteEpoch();
            if (!ReferenceEquals(healthEpisodePollEpoch, epoch))
            {
                healthEpisodePollEpoch = epoch;
                nextHealthEpisodePollAt = now;
                return;
            }
            if (now < nextHealthEpisodePollAt)
                return;

            nextHealthEpisodePollAt = now + NpcHealthEpisodePollSeconds;
            store.FlushNpcHealthExperiences(now, NpcHealthEpisodeQuietSeconds, 100);
        }
    }
}
