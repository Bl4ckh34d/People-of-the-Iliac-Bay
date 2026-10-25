using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Admission boundary for the small synchronously observable native self-cast path.</summary>
    public static class NativeNpcMagicSelfCastGate
    {
        public static bool TryAuthorize(NpcMagicRequest request, NpcMagicKnownEffect nativeEffect,
            int currentMagicka, int effectValueBefore, bool adapterAvailable,
            bool dutyAvailable, bool questProtected, out NpcMagicAuthorization authorization)
        {
            authorization = null;
            if (request == null || nativeEffect == null || request.ActorId != request.TargetId ||
                request.Hostile || effectValueBefore < 0 || !adapterAvailable || !dutyAvailable || questProtected)
                return false;

            // Ranged and touch casts have asynchronous targeting. This gate admits only
            // positive self effects whose native transition can be read in the same call.
            if (request.EffectKind != NpcMagicEffectKind.Health &&
                request.EffectKind != NpcMagicEffectKind.Condition &&
                request.EffectKind != NpcMagicEffectKind.TimedEffect)
                return false;
            if (request.EffectKind == NpcMagicEffectKind.Condition && effectValueBefore == 0)
                return false;

            var facts = new NpcMagicAdmissionFacts {
                KnownEffects = new[] { nativeEffect }, CurrentMagicka = currentMagicka,
                TargetIsLegal = true, TargetIsAlive = true, TargetConsents = true,
                NativeAdapterAvailable = adapterAvailable, DutyAvailable = dutyAvailable,
                QuestProtected = questProtected
            };
            return NpcMagicActionContract.TryAuthorize(request, facts, out authorization);
        }
    }
}
