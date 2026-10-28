using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeDialogueProbe
    {
        private static IEnumerator WaitForTalk(ResidentActor actor, Action<DaggerfallTalkWindow> assign)
        {
            float deadline = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                if (talk != null && talk.IsSetup && GameManager.Instance.TalkManager.CurrentNPCType == TalkManager.NPCType.Mobile &&
                    GameManager.Instance.TalkManager.MobileNPC == actor.Person)
                {
                    assign(talk);
                    yield break;
                }
                yield return null;
            }
            throw new InvalidOperationException("Native Talk setup timed out.");
        }

        private static IEnumerator WaitForUnusedManagedAdult(WorldStateStore store, Action<ResidentActor> assign,
            bool preferNearby, Vector3 playerPosition)
        {
            float deadline = Time.realtimeSinceStartup + 45f;
            while (Time.realtimeSinceStartup < deadline)
            {
                ResidentActor actor = FindUnusedManagedAdult(store, preferNearby, playerPosition);
                if (actor)
                {
                    assign(actor);
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            throw new InvalidOperationException("No unused managed adult appeared in the disposable fixture within 45 seconds.");
        }

        private static ResidentActor FindUnusedManagedAdult(WorldStateStore store, bool preferNearby, Vector3 playerPosition)
        {
            ResidentActor nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (!candidate || !candidate.Person || candidate.Resident == null || !candidate.Resident.IsAlive ||
                    candidate.Resident.AgeGroup != ResidentAgeGroup.Adult || candidate.IsSleeping || candidate.IsConversing)
                    continue;
                if (store.ConversationTranscript(candidate.Resident.Id, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).Count == 0)
                {
                    if (!preferNearby) return candidate;
                    float distance = Vector3.Distance(candidate.transform.position - Vector3.up, playerPosition - Vector3.up);
                    if (distance < nearestDistance) { nearest = candidate; nearestDistance = distance; }
                }
            }
            return nearest;
        }

    }
}
