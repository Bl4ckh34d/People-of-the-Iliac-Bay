using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcTraitRecoveryIntegrationTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Saved personality governs timed mood capture without changing relationships", () =>
            {
                string id; var store=NpcExperienceCommitTests.Create(out id);
                var snapshot=store.Snapshot(); var profile=new NpcPersonalityProfile { PersonId=id, Version=3 };
                snapshot.NpcPersonalities[0]=profile;
                profile.EmotionalStability="sensitive"; profile.Sensitivity="highly sensitive";
                profile.SelfEsteem="insecure";
                store.Restore(snapshot);
                long sequence;
                Check.True(store.TryCommitAcceptedSocialExchange(id,0,0,10,100,"date","place","hi","hello",out sequence));
                double now=100+MoodRecovery.HalfLifeSeconds(store.WorldSeed,id);
                int expected=MoodRecovery.ValueAt(10,100,now,MoodRecovery.HalfLifeSeconds(store.WorldSeed,store.ReadNpcPersonality(id)));
                Check.Equal(expected,store.CaptureConversationMood(id,now).Value);
                Check.Equal(0,store.ConversationDisposition(id));
                Check.True(store.ReadNpcRelationship(id,"player")==null);
                Check.Equal(10,store.CaptureConversationMood(id).Value);
            }));
        }
    }
}
