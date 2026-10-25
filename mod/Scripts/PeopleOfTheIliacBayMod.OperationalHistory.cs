using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private float nextOperationalHistoryCheck;

        private void TickOperationalHistory()
        {
            if(Time.unscaledTime<nextOperationalHistoryCheck)return;
            nextOperationalHistoryCheck=Time.unscaledTime+2f;
            var game=DaggerfallUnity.Instance;
            var manager=GameManager.Instance;
            if(!isActiveAndEnabled || !options.Enabled || saveFault || store==null ||
                game==null || !game.IsReady || game.WorldTime==null || manager==null ||
                !manager.IsReady || !manager.IsPlayingGame() || GameManager.IsGamePaused ||
                conversationBusy || dialogueWasOpen)return;
            try
            {
                // Read the authoritative clock only after readiness checks. Project effects
                // and release expired lodging before considering terminal task groups.
                // Candidate budgets do not include reference scans or publication validation.
                double now=game.WorldTime.Now.ToSeconds();
                store.FlushNpcAppointmentExperiences(now,4);
                store.ProjectPendingNpcTreatmentOutcomes(now,4);
                // Saved poison assignments retry without depending on the actor
                // or native effect bundle still being streamed in.
                store.DrainPendingNpcPoisonOnsets(8);
                store.DrainPendingNpcObservedSpeech(8);
                store.FinalizeUnavailableNpcInteractions(now,4);
                store.RetireExpiredNpcHealthRestLodging(now,4);
                // Retire projected experience detail before terminal source tasks can be
                // pruned. Pending appraisal, mood causes and archive provenance remain
                // Core-owned pins; the row budget does not bound validation/IO costs.
                store.MaintainNpcExperienceInboxHistory(now,16,4);
                store.MaintainNpcOperationalHistory(now,4,1);
            }
            catch(ArgumentException exception)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HISTORY: maintenance rejected: "+exception.Message);
                nextOperationalHistoryCheck=Time.unscaledTime+30f;
            }
        }
    }
}
