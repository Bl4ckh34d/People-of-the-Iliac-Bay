using System;
using System.Collections;
using System.Reflection;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// No-model regression probe for the snapshot-null legacy NPC life-context
    /// builder. Uses an exact native mobile prefab clone without ResidentActor.
    /// </summary>
    internal static class LegacyWeatherContextProbe
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host)
        {
            if (!host) throw new InvalidOperationException("Legacy weather probe requires the live mod host.");
            GameManager game = GameManager.Instance;
            DaggerfallUnity dfu = DaggerfallUnity.Instance;
            if (game == null || !game.IsReady || game.PlayerMotor == null || dfu == null ||
                !dfu.Option_MobileNPCPrefab || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Legacy weather probe requires a ready outdoor native fixture and MobileNPC prefab.");

            FieldInfo enabledField = RequireField(typeof(PeopleOfTheIliacBayMod), "localDialogueEnabled");
            FieldInfo currentSnapshotField = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationContextCurrentSnapshot");
            FieldInfo revisionField = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationContextRequestRevision");
            FieldInfo sessionOpenField = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationSessionOpen");
            FieldInfo contextSessionField = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationContextSessionId");
            FieldInfo dialogueField = RequireField(typeof(PeopleOfTheIliacBayMod), "localDialogue");
            MethodInfo beginMethod = RequireMethod(typeof(PeopleOfTheIliacBayMod), "BeginGroundedConversation");
            MethodInfo beginContextSession = RequireMethod(typeof(PeopleOfTheIliacBayMod), "BeginConversationContextSession");
            MethodInfo lifeContext = RequireMethod(typeof(PeopleOfTheIliacBayMod), "CurrentConversationLifeContext");
            MethodInfo weatherContext = RequireMethod(typeof(PeopleOfTheIliacBayMod), "CurrentConversationWeather");
            PropertyInfo requestCount = typeof(LocalDialogueClient).GetProperty("ReplyRequestCount", PrivateInstance);
            if (requestCount == null) throw new InvalidOperationException("LocalDialogueClient reply count is unavailable for no-model assertion.");

            bool oldEnabled = Convert.ToBoolean(enabledField.GetValue(host));
            var localDialogue = dialogueField.GetValue(host) as LocalDialogueClient;
            if (localDialogue == null) throw new InvalidOperationException("Legacy weather probe has no shared dialogue client.");
            long requestsBefore = Convert.ToInt64(requestCount.GetValue(localDialogue, null));
            GameObject body = null;
            DaggerfallTalkWindow talk = null;
            try
            {
                // Suppress all model dispatch only for this no-input fixture.
                enabledField.SetValue(host, false);
                body = UnityEngine.Object.Instantiate(dfu.Option_MobileNPCPrefab.gameObject);
                if (body == null) throw new InvalidOperationException("Could not clone DFU's native MobileNPC prefab.");
                if (body.scene != game.PlayerMotor.gameObject.scene)
                    SceneManager.MoveGameObjectToScene(body, game.PlayerMotor.gameObject.scene);
                body.transform.SetParent(null);
                body.transform.position = game.PlayerMotor.transform.position + game.PlayerMotor.transform.forward * 3f + Vector3.up;
                MobilePersonNPC person = body.GetComponent<MobilePersonNPC>();
                if (!person || body.GetComponent<ResidentActor>())
                    throw new InvalidOperationException("Native prefab clone lacks MobilePersonNPC or unexpectedly has ResidentActor.");
                MobilePersonMotor motor = body.GetComponent<MobilePersonMotor>();
                CityNavigation navigation = game.StreamingWorld == null ? null : game.StreamingWorld.GetCurrentCityNavigation();
                if (!motor || !navigation || !motor.MobileAsset)
                    throw new InvalidOperationException("Native mobile clone cannot be initialized without its motor, billboard, and current city navigation.");
                // Match the native actor setup needed by MobilePersonNPC's Asset
                // and MobilePersonMotor's unconditional OnDestroy/ClearMotor path,
                // without adding ResidentActor or population ownership.
                motor.cityNavigation = navigation;
                motor.enabled = false;
                person.Motor = motor;
                person.Asset = motor.MobileAsset;
                person.IsGuard = false;
                person.raceToBeSet = Races.Breton;
                person.genderToBeSet = Genders.Female;
                person.outfitVariantToBeSet = 0;
                person.ApplyPersonSettingsViaInspector();
                person.NameNPC = "Weather Context Probe";
                body.SetActive(true);
                yield return null;

                BuildingDirectory directory = game.StreamingWorld == null ? null : game.StreamingWorld.GetCurrentBuildingDirectory();
                if (!directory) throw new InvalidOperationException("Legacy weather probe requires the actual current exterior BuildingDirectory.");
                if (directory && body.transform.IsChildOf(directory.transform))
                    throw new InvalidOperationException("Native weather fixture unexpectedly belongs to BuildingDirectory.");

                game.TalkManager.TalkToMobileNPC(person);
                float deadline = Time.realtimeSinceStartup + 20f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    talk = DaggerfallUI.UIManager == null ? null : DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                    if (talk != null && talk.IsSetup && game.TalkManager.CurrentNPCType == TalkManager.NPCType.Mobile &&
                        game.TalkManager.MobileNPC == person) break;
                    yield return null;
                }
                if (talk == null || !talk.IsSetup || game.TalkManager.MobileNPC != person)
                    throw new InvalidOperationException("Public TalkToMobileNPC did not bind exact native prefab clone.");

                if (!(bool)sessionOpenField.GetValue(host))
                {
                    beginMethod.Invoke(host, null);
                }
                if (string.IsNullOrEmpty(contextSessionField.GetValue(host) as string))
                    beginContextSession.Invoke(host, new object[] { talk });

                string capturedText = "Snapshotless native weather probe; no dialogue request is submitted.";
                ConversationContextSnapshot snapshot = CaptureSnapshot(host, talk, revisionField, capturedText);
                AssertUnknownEphemeralWeather(snapshot, "detached");
                currentSnapshotField.SetValue(host, null);
                AssertUnknownLegacyContext(host, lifeContext, weatherContext, person, game, "detached");

                // Positive control: the same exact clone is briefly parented to
                // DFU's real current exterior directory, the only location proof
                // accepted by CaptureNpcOutdoorWeather for an unmanaged mobile.
                body.transform.SetParent(directory.transform, true);
                snapshot = CaptureSnapshot(host, talk, revisionField, capturedText);
                bool verifiedOutdoors;
                ConversationNpcWeatherObservation verifiedWeather;
                if (snapshot == null || !snapshot.NpcOutdoors.TryGetVerified(out verifiedOutdoors) || !verifiedOutdoors)
                    throw new InvalidOperationException("Native directory-owned positive control did not verify the NPC as outdoors.");
                if (!snapshot.Weather.TryGetVerified(out verifiedWeather))
                    throw new InvalidOperationException("Native directory-owned positive control did not verify current outdoor weather.");
                currentSnapshotField.SetValue(host, null);
                int positiveDisposition = snapshot.EffectiveDisposition;
                string positiveWeather = weatherContext.Invoke(host, null) as string;
                string positiveLife = lifeContext.Invoke(host, new object[] { person.NameNPC, positiveDisposition }) as string;
                if (string.IsNullOrWhiteSpace(positiveWeather) ||
                    positiveWeather.IndexOf("Current weather outside: " + verifiedWeather.Weather,
                        StringComparison.OrdinalIgnoreCase) < 0 ||
                    string.IsNullOrWhiteSpace(positiveLife) ||
                    positiveLife.IndexOf("You are physically outside.", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Verified exterior positive control lost its NPC outdoor/weather context.");
                string positiveMoodCue = WeatherMoodCue(verifiedWeather.Weather, positiveDisposition);
                if (positiveMoodCue != null && positiveLife.IndexOf(positiveMoodCue, StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Verified exterior positive control lost its weather mood modifier: " + positiveMoodCue);

                // Return to the original detached fixture and prove location
                // context becomes unknown again instead of sticking to the actor.
                body.transform.SetParent(null, true);
                snapshot = CaptureSnapshot(host, talk, revisionField, capturedText);
                AssertUnknownEphemeralWeather(snapshot, "detached-again");
                currentSnapshotField.SetValue(host, null);
                AssertUnknownLegacyContext(host, lifeContext, weatherContext, person, game, "detached-again");
                long requestsAfter = Convert.ToInt64(requestCount.GetValue(localDialogue, null));
                if (requestsAfter != requestsBefore)
                    throw new InvalidOperationException("Legacy weather probe submitted an unexpected dialogue model request.");

                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LEGACY_WEATHER_CONTEXT_PASS: nativePrefabClone=true; residentActor=false; directoryOwner=false; " +
                    "detachedNpcOutdoors=Unknown; detachedNpcWeather=Unknown; directoryPositiveControl=true; unknownRestoredAfterDetach=true; " +
                    "playerInside=false; playerWeather=" + PlayerWeather(game) + "; snapshotlessNpcWeatherCues=none; modelRequestDelta=" +
                    (requestsAfter - requestsBefore) + ".");
            }
            finally
            {
                if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
                if (body)
                {
                    body.transform.SetParent(null, true);
                    UnityEngine.Object.Destroy(body);
                }
                currentSnapshotField.SetValue(host, null);
                enabledField.SetValue(host, oldEnabled);
            }

            yield return null;
            if (talk != null && DaggerfallUI.UIManager != null && DaggerfallUI.UIManager.TopWindow == talk)
                throw new InvalidOperationException("Legacy weather probe Talk window remained open after cleanup.");
            if (body) throw new InvalidOperationException("Legacy weather probe retained its ephemeral native prefab clone.");
        }

        private static string PlayerWeather(GameManager game)
        {
            return game.WeatherManager && game.WeatherManager.PlayerWeather
                ? game.WeatherManager.PlayerWeather.WeatherType.ToString() : "Unknown";
        }

        private static ConversationContextSnapshot CaptureSnapshot(PeopleOfTheIliacBayMod host,
            DaggerfallTalkWindow talk, FieldInfo revisionField, string text)
        {
            long revision = checked(Convert.ToInt64(revisionField.GetValue(host)) + 1L);
            return host.CaptureConversationContext(talk, text, revision);
        }

        private static void AssertUnknownEphemeralWeather(ConversationContextSnapshot snapshot, string stage)
        {
            if (snapshot == null || snapshot.Binding == null || snapshot.Binding.IdentityKind != ConversationIdentityKind.EphemeralSpeaker ||
                snapshot.Binding.ActorCategory != ConversationActorCategory.Mobile ||
                snapshot.NpcOutdoors.Status != ConversationContextFactStatus.Unknown ||
                snapshot.Weather.Status != ConversationContextFactStatus.Unknown)
                throw new InvalidOperationException("Detached native mobile did not produce an ephemeral snapshot with Unknown NPC outdoors/weather facts at " + stage + ".");
        }

        private static void AssertUnknownLegacyContext(PeopleOfTheIliacBayMod host, MethodInfo lifeContext,
            MethodInfo weatherContext, MobilePersonNPC person, GameManager game, string stage)
        {
            string life = lifeContext.Invoke(host, new object[] { person.NameNPC, 0 }) as string;
            if (string.IsNullOrWhiteSpace(life))
                throw new InvalidOperationException("Snapshotless life-context fallback returned no context at " + stage + ".");
            string[] cues = { "You are physically outside.", "eager to get out of the rain", "unsettled by the storm",
                "cold and eager for warmth", "uneasy in the fog", "sunshine lifting spirits" };
            foreach (string cue in cues)
                if (life.IndexOf(cue, StringComparison.Ordinal) >= 0)
                    throw new InvalidOperationException("Snapshotless fallback borrowed player outdoors/weather as NPC fact at " + stage +
                        ": cue=" + cue + "; playerWeather=" + PlayerWeather(game) + "; life=" + life);
            string weather = weatherContext.Invoke(host, null) as string;
            if (!string.IsNullOrEmpty(weather))
                throw new InvalidOperationException("CurrentConversationWeather emitted weather for an unverified NPC location at " + stage +
                    ": weather=" + weather);
            if (life.IndexOf("Stable personality traits:", StringComparison.Ordinal) < 0 ||
                life.IndexOf("Current mood:", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Removing unsupported weather facts also removed unrelated stable personality or base mood context.");
        }

        private static string WeatherMoodCue(string weather, int disposition)
        {
            switch ((weather ?? string.Empty).ToLowerInvariant())
            {
                case "rain": return "eager to get out of the rain";
                case "thunder": return "unsettled by the storm and seeking shelter";
                case "snow": return "cold and eager for warmth";
                case "fog": return "uneasy in the fog";
                case "sunny": return disposition > -20 ? "sunshine lifting spirits" : null;
                default: return null;
            }
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, PrivateInstance);
            if (field == null) throw new InvalidOperationException("Missing native weather probe field: " + name + ".");
            return field;
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            MethodInfo method = type.GetMethod(name, PrivateInstance);
            if (method == null) throw new InvalidOperationException("Missing native weather probe method: " + name + ".");
            return method;
        }
    }
}
