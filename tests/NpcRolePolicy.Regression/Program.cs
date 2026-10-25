using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static readonly JsonSerializerOptions SaveJsonOptions = new JsonSerializerOptions { IncludeFields = true };

    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("rank and combat policy remain separate", RankAndCombatPolicy),
            ("danger fear scales combat experience only", DangerFearScaling),
            ("retreat requires independent wounded fighter and valid roll", RetreatEligibility),
            ("invalid health and roll values never cause retreat", RetreatRejectsInvalidInputs),
            ("role evidence requires known identity and sourced valid facts", EvidenceValidation),
            ("role evidence merges facts and advances revision only on change", EvidenceMergeAndRevision),
            ("role state copies and schema 42 migration preserve save boundaries", SaveAndCopyIsolation),
            ("acute fear scales only incoming combat baseline and preserves residual", AcuteFearObservationUsesRoleBaseline),
            ("acute fear reads cap legacy combat residual without mutating history", AcuteFearProjectionPreservesHistory),
            ("acute fear and role survive restore with low combat fear", AcuteFearAndRoleRestoreTogether),
            ("city guards flee two percent while palace guards and soldiers never flee", GuardRetreatChances),
            ("only independent combatants can continue direct combat assistance", DirectCombatAssistEligibility)
        };

        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void RankAndCombatPolicy()
    {
        True(NpcRolePolicy.SuppressPlayerFear(Role(rank: NpcSocialRank.Nobility)), "nobility suppresses player-directed fear");
        True(NpcRolePolicy.SuppressPlayerFear(Role(rank: NpcSocialRank.HighAuthority)), "high authority suppresses player-directed fear");
        True(!NpcRolePolicy.SuppressPlayerFear(Role(rank: NpcSocialRank.Commoner)), "commoner rank keeps normal player-directed fear");
        True(!NpcRolePolicy.SuppressPlayerFear(Role()), "unknown rank keeps normal player-directed fear");
        True(!NpcRolePolicy.SuppressPlayerFear(null), "missing role keeps normal player-directed fear");

        foreach (NpcCombatRole role in new[] { NpcCombatRole.Guard, NpcCombatRole.Soldier, NpcCombatRole.Warrior,
            NpcCombatRole.Rogue, NpcCombatRole.Assassin, NpcCombatRole.Combatant, NpcCombatRole.PalaceGuard })
            True(NpcRolePolicy.IsCombatExperienced(Role(combat: role)), role + " is combat-experienced");
        True(!NpcRolePolicy.IsCombatExperienced(Role(combat: NpcCombatRole.Civilian)), "civilian is not combat-experienced");
        True(!NpcRolePolicy.IsCombatExperienced(Role()), "unknown combat role is not experienced");
        True(!NpcRolePolicy.IsCombatExperienced(null), "missing role is not experienced");

        True(!NpcRolePolicy.FightsToDeath(Role(combat: NpcCombatRole.Guard)), "city guard may flee rather than die");
        True(NpcRolePolicy.FightsToDeath(Role(combat: NpcCombatRole.Soldier, service: NpcServiceBinding.Independent)),
            "soldier fights to the death even with independent service");
        True(NpcRolePolicy.FightsToDeath(Role(combat: NpcCombatRole.PalaceGuard, service: NpcServiceBinding.NobleKingdom)),
            "palace guard fights to the death regardless of retreat roll");
        var persistedCombatRoles = new[] { NpcCombatRole.Unknown, NpcCombatRole.Civilian, NpcCombatRole.Guard,
            NpcCombatRole.Soldier, NpcCombatRole.Warrior, NpcCombatRole.Rogue, NpcCombatRole.Assassin,
            NpcCombatRole.Combatant };
        for (int index = 0; index < persistedCombatRoles.Length; index++)
            Equal(index, (int)persistedCombatRoles[index], "existing combat-role save value stays stable at " + index);
        Equal(8, (int)NpcCombatRole.PalaceGuard, "palace guard appends after all persisted combat-role values");
        True(!NpcRolePolicy.FightsToDeath(Role(combat: NpcCombatRole.Warrior, service: NpcServiceBinding.NobleKingdom)),
            "kingdom service alone does not give other combatants death duty");
        True(!NpcRolePolicy.FightsToDeath(Role(combat: NpcCombatRole.Civilian)), "civilian does not get death duty");
    }

    private static void DangerFearScaling()
    {
        Equal(4d, NpcRolePolicy.DangerFear(Role(combat: NpcCombatRole.Rogue), 40d), "combat danger fear uses 0.1 scale");
        Equal(10d, NpcRolePolicy.DangerFear(Role(combat: NpcCombatRole.Assassin), 100d), "combat danger fear caps at 10");
        Equal(40d, NpcRolePolicy.DangerFear(Role(combat: NpcCombatRole.Civilian), 40d), "civilian danger fear stays unscaled");
        Equal(100d, NpcRolePolicy.DangerFear(Role(), 100d), "unknown combat role stays unscaled");
        Equal(100d, NpcRolePolicy.DangerFear(null, 100d), "missing role stays unscaled");
        Equal(0d, NpcRolePolicy.DangerFear(Role(combat: NpcCombatRole.Guard), -5d), "negative raw fear clamps to zero");
        Equal(10d, NpcRolePolicy.DangerFear(Role(combat: NpcCombatRole.Soldier), 500d), "excess raw fear stays within cap");
        Equal(0d, NpcRolePolicy.DangerFear(Role(combat: NpcCombatRole.Guard), double.NaN), "non-finite fear fails safely");
    }

    private static void DirectCombatAssistEligibility()
    {
        True(NpcRolePolicy.CanDirectCombatAssist(Role(combat: NpcCombatRole.Warrior,
            service: NpcServiceBinding.Independent)), "independent warrior may continue an admitted direct combat action");
        True(!NpcRolePolicy.CanDirectCombatAssist(Role(combat: NpcCombatRole.Civilian,
            service: NpcServiceBinding.Independent)), "civilian keeps flight response during a threat");
        True(!NpcRolePolicy.CanDirectCombatAssist(Role(combat: NpcCombatRole.Soldier,
            service: NpcServiceBinding.Independent)), "soldier retains duty response even with independent service fact");
        True(!NpcRolePolicy.CanDirectCombatAssist(Role(combat: NpcCombatRole.PalaceGuard,
            service: NpcServiceBinding.NobleKingdom)), "palace guard remains with duty owner, not direct assistance");
    }

    private static void RetreatEligibility()
    {
        var independentAssassin = Role(combat: NpcCombatRole.Assassin, service: NpcServiceBinding.Independent);
        True(NpcRolePolicy.ShouldRetreat(independentAssassin, 25, 100, 34), "independent assassin retreats at threshold on eligible roll");
        True(!NpcRolePolicy.ShouldRetreat(independentAssassin, 25, 100, 35), "roll at 35 is outside 35-percent chance");
        True(!NpcRolePolicy.ShouldRetreat(independentAssassin, 26, 100, 0), "health above 25 percent prevents retreat");
        True(!NpcRolePolicy.ShouldRetreat(independentAssassin, 26, 103, 0), "health above ratio threshold prevents retreat");
        True(!NpcRolePolicy.ShouldRetreat(Role(combat: NpcCombatRole.Warrior, service: NpcServiceBinding.Independent),
            50, 100, 0), "healthy independent warrior does not retreat");
        True(!NpcRolePolicy.ShouldRetreat(Role(combat: NpcCombatRole.Rogue, service: NpcServiceBinding.NobleKingdom),
            10, 100, 0), "kingdom-bound rogue does not retreat");
        True(!NpcRolePolicy.ShouldRetreat(Role(combat: NpcCombatRole.Rogue), 10, 100, 0), "unknown service does not imply independence");
        True(NpcRolePolicy.ShouldRetreat(Role(combat: NpcCombatRole.Guard, service: NpcServiceBinding.Independent),
            1, 100, 0), "city guard can take rare retreat roll at critical health");
        True(!NpcRolePolicy.ShouldRetreat(Role(combat: NpcCombatRole.Soldier, service: NpcServiceBinding.Independent),
            1, 100, 0), "soldier never retreats from injury");
        var palaceGuard = Role(combat: NpcCombatRole.Guard, service: NpcServiceBinding.NobleKingdom);
        True(!NpcRolePolicy.FightsToDeath(palaceGuard), "service binding alone does not make city guard death-bound");
        True(!NpcRolePolicy.ShouldRetreat(Role(combat: NpcCombatRole.PalaceGuard,
            service: NpcServiceBinding.NobleKingdom), 1, 100, 0), "palace guard never retreats from injury");
        True(!NpcRolePolicy.ShouldRetreat(Role(combat: NpcCombatRole.Civilian, service: NpcServiceBinding.Independent),
            1, 100, 0), "civilian is not an experienced retreating combatant");
    }

    private static void GuardRetreatChances()
    {
        foreach (NpcServiceBinding service in new[] { NpcServiceBinding.Unknown,
            NpcServiceBinding.NobleKingdom, NpcServiceBinding.Independent })
        {
            var cityGuard = Role(combat: NpcCombatRole.Guard, service: service);
            int retreats = 0;
            for (int roll = NpcRolePolicy.MinimumEncounterRoll; roll <= NpcRolePolicy.MaximumEncounterRoll; roll++)
                if (NpcRolePolicy.ShouldRetreat(cityGuard, 25, 100, roll)) retreats++;
            Equal(2, retreats, "city guard flees on exactly two rolls regardless of service " + service);
        }

        var healthyCityGuard = Role(combat: NpcCombatRole.Guard, service: NpcServiceBinding.NobleKingdom);
        int healthyGuardRetreats = 0;
        for (int roll = NpcRolePolicy.MinimumEncounterRoll; roll <= NpcRolePolicy.MaximumEncounterRoll; roll++)
            if (NpcRolePolicy.ShouldRetreat(healthyCityGuard, 26, 100, roll)) healthyGuardRetreats++;
        Equal(0, healthyGuardRetreats, "healthy city guard never retreats");

        var palaceGuard = Role(combat: NpcCombatRole.PalaceGuard, service: NpcServiceBinding.NobleKingdom);
        int palaceGuardRetreats = 0;
        for (int roll = NpcRolePolicy.MinimumEncounterRoll; roll <= NpcRolePolicy.MaximumEncounterRoll; roll++)
            if (NpcRolePolicy.ShouldRetreat(palaceGuard, 1, 100, roll)) palaceGuardRetreats++;
        Equal(0, palaceGuardRetreats, "palace guard never flees at critical health");

        var soldier = Role(combat: NpcCombatRole.Soldier, service: NpcServiceBinding.Independent);
        int soldierRetreats = 0;
        for (int roll = NpcRolePolicy.MinimumEncounterRoll; roll <= NpcRolePolicy.MaximumEncounterRoll; roll++)
            if (NpcRolePolicy.ShouldRetreat(soldier, 1, 100, roll)) soldierRetreats++;
        Equal(0, soldierRetreats, "soldier never flees at critical health");

        var independentCombatant = Role(combat: NpcCombatRole.Combatant, service: NpcServiceBinding.Independent);
        int independentCombatantRetreats = 0;
        for (int roll = NpcRolePolicy.MinimumEncounterRoll; roll <= NpcRolePolicy.MaximumEncounterRoll; roll++)
            if (NpcRolePolicy.ShouldRetreat(independentCombatant, 25, 100, roll)) independentCombatantRetreats++;
        Equal(35, independentCombatantRetreats, "independent combatant keeps 35-percent retreat chance");
    }

    private static void RetreatRejectsInvalidInputs()
    {
        var independentWarrior = Role(combat: NpcCombatRole.Warrior, service: NpcServiceBinding.Independent);
        foreach (var values in new[] { (-1, 100, 0), (0, 100, 0), (101, 100, 0), (1, 0, 0), (1, -100, 0),
            (1, 100, -1), (1, 100, 100) })
            True(!NpcRolePolicy.ShouldRetreat(independentWarrior, values.Item1, values.Item2, values.Item3),
                "invalid health or roll must not cause retreat");
        True(!NpcRolePolicy.ShouldRetreat(null, 1, 100, 0), "missing role must not cause retreat");
    }

    private static void EvidenceValidation()
    {
        var store = NewStore(out string knownPersonId);
        True(store.ReadNpcRole("npc:unknown") == null, "unknown person has no role view");
        True(!store.RecordNpcRoleEvidence("npc:unknown", NpcSocialRank.Nobility, NpcCombatRole.Unknown,
            NpcServiceBinding.Unknown, "native:rank", null, null), "unknown identity cannot receive role facts");
        True(!store.RecordNpcRoleEvidence(knownPersonId, NpcSocialRank.Nobility, NpcCombatRole.Unknown,
            NpcServiceBinding.Unknown, null, null, null), "non-Unknown rank requires explicit source");
        True(!store.RecordNpcRoleEvidence(knownPersonId, NpcSocialRank.Unknown, NpcCombatRole.Assassin,
            NpcServiceBinding.Unknown, null, null, null), "non-Unknown combat role requires explicit source");
        True(!store.RecordNpcRoleEvidence(knownPersonId, NpcSocialRank.Unknown, NpcCombatRole.Unknown,
            NpcServiceBinding.Independent, null, null, null), "non-Unknown service requires explicit source");
        True(!store.RecordNpcRoleEvidence(knownPersonId, (NpcSocialRank)99, NpcCombatRole.Unknown,
            NpcServiceBinding.Unknown, "native:rank", null, null), "undefined enum value is rejected");
        True(!store.RecordNpcRoleEvidence(knownPersonId, NpcSocialRank.Nobility, NpcCombatRole.Unknown,
            NpcServiceBinding.Unknown, new string('r', 513), null, null), "oversized source ID is rejected");
        True(!store.RecordNpcRoleEvidence(new string('p', 129), NpcSocialRank.Nobility, NpcCombatRole.Unknown,
            NpcServiceBinding.Unknown, "native:rank", null, null), "oversized person ID is rejected");
        True(!store.RecordNpcRoleEvidence(knownPersonId, NpcSocialRank.Nobility, NpcCombatRole.Unknown,
            NpcServiceBinding.Unknown, "native:\nrank", null, null), "control characters in source ID are rejected");
        True(store.ReadNpcRole(knownPersonId) == null, "rejected evidence allocates no saved row");
    }

    private static void EvidenceMergeAndRevision()
    {
        var store = NewStore(out string personId);
        True(store.RecordNpcRoleEvidence(personId, NpcSocialRank.Nobility, NpcCombatRole.Unknown,
            NpcServiceBinding.Unknown, "native:social-group:royal", null, null), "first native rank is recorded");
        var first = store.ReadNpcRole(personId);
        Equal(1L, first.Revision, "first role fact starts revision at one");
        Equal("native:social-group:royal", first.RankSourceId, "rank source is saved separately");

        True(store.RecordNpcRoleEvidence(personId, NpcSocialRank.Unknown, NpcCombatRole.Assassin,
            NpcServiceBinding.Independent, null, "native:career:assassin", "native:service:independent"),
            "combat and service evidence merges without erasing rank");
        var merged = store.ReadNpcRole(personId);
        Equal(NpcSocialRank.Nobility, merged.SocialRank, "Unknown input cannot replace known rank");
        Equal("native:social-group:royal", merged.RankSourceId, "unknown rank cannot erase rank source");
        Equal(NpcCombatRole.Assassin, merged.CombatRole, "combat role is stored independently");
        Equal(NpcServiceBinding.Independent, merged.ServiceBinding, "service binding is stored independently");
        Equal(2L, merged.Revision, "merged facts increment revision once");

        True(!store.RecordNpcRoleEvidence(personId, NpcSocialRank.Nobility, NpcCombatRole.Assassin,
            NpcServiceBinding.Independent, "native:other-rank-source", "native:career:assassin", "native:service:independent"),
            "same facts do not write a revision even if duplicate rank evidence has another source");
        Equal(2L, store.ReadNpcRole(personId).Revision, "duplicate facts keep revision stable");
        Equal("native:social-group:royal", store.ReadNpcRole(personId).RankSourceId, "duplicate facts preserve original provenance");

        True(store.RecordNpcRoleEvidence(personId, NpcSocialRank.Commoner, NpcCombatRole.Unknown,
            NpcServiceBinding.Unknown, "native:social-group:commoner", null, null), "new explicit rank replaces prior rank fact");
        Equal(NpcSocialRank.Commoner, store.ReadNpcRole(personId).SocialRank, "changed rank fact is saved");
        Equal(3L, store.ReadNpcRole(personId).Revision, "changed fact advances monotonic revision");
    }

    private static void SaveAndCopyIsolation()
    {
        var store = NewStore(out string personId);
        True(store.RecordNpcRoleEvidence(personId, NpcSocialRank.HighAuthority, NpcCombatRole.Warrior,
            NpcServiceBinding.Independent, "native:rank:high", "native:career:warrior", "native:service:independent"),
            "role facts are written before save");
        var detached = store.ReadNpcRole(personId);
        detached.SocialRank = NpcSocialRank.Commoner;
        detached.CombatRole = NpcCombatRole.Civilian;
        detached.RankSourceId = "mutated-copy";
        Equal(NpcSocialRank.HighAuthority, store.ReadNpcRole(personId).SocialRank, "read result is detached");
        Equal(NpcCombatRole.Warrior, store.ReadNpcRole(personId).CombatRole, "read result cannot mutate saved combat role");

        var snapshot = store.Snapshot();
        Equal(WorldState.CurrentVersion, snapshot.Version, "new saves use the current schema");
        Equal(1, snapshot.NpcRoles.Count, "role row is included in save copy");
        snapshot.NpcRoles[0].ServiceBinding = NpcServiceBinding.NobleKingdom;
        snapshot.NpcRoles.Clear();
        Equal(NpcServiceBinding.Independent, store.ReadNpcRole(personId).ServiceBinding, "snapshot mutation cannot alter live role facts");

        string json = JsonSerializer.Serialize(store.Snapshot(), SaveJsonOptions);
        WorldState serialized = JsonSerializer.Deserialize<WorldState>(json, SaveJsonOptions);
        var restored = new WorldStateStore(999);
        restored.Restore(serialized);
        var reloaded = restored.ReadNpcRole(personId);
        Equal(NpcSocialRank.HighAuthority, reloaded.SocialRank, "serialized rank survives reload");
        Equal(NpcCombatRole.Warrior, reloaded.CombatRole, "serialized combat role survives reload");
        Equal(NpcServiceBinding.Independent, reloaded.ServiceBinding, "serialized service survives reload");
        Equal("native:career:warrior", reloaded.CombatSourceId, "serialized source IDs survive reload");
        Equal(1L, reloaded.Revision, "serialized role revision survives reload");

        var legacySource = store.Snapshot();
        legacySource.Version = 42;
        // A historical header must carry the historical inbox format as well.
        legacySource.NpcExperienceInbox.Version = 4;
        string legacyJson = JsonSerializer.Serialize(legacySource, SaveJsonOptions);
        JsonObject legacyDocument = JsonNode.Parse(legacyJson).AsObject();
        legacyDocument.Remove(nameof(WorldState.NpcRoles));
        WorldState legacySave = legacyDocument.Deserialize<WorldState>(SaveJsonOptions);
        var migrated = new WorldStateStore(0);
        migrated.Restore(legacySave);
        Equal(WorldState.CurrentVersion, migrated.Snapshot().Version, "schema 42 save upgrades to current schema");
        True(migrated.ReadNpcRole(personId) == null, "schema 42 migration invents no role evidence");

        legacySource.NpcRoles[0].SocialRank = NpcSocialRank.Unknown;
        var fromMalformedLegacy = new WorldStateStore(0);
        legacySource.NpcRoles[0].SocialRank = NpcSocialRank.HighAuthority;
        fromMalformedLegacy.Restore(legacySource);
        True(fromMalformedLegacy.ReadNpcRole(personId) == null, "future role field in schema 42 is ignored");
    }

    private static void AcuteFearObservationUsesRoleBaseline()
    {
        var civilianStore = NewStore(out string civilianId);
        RecordCombatRole(civilianStore, civilianId, NpcCombatRole.Civilian);
        True(civilianStore.TryObserveNpcAcuteFear(civilianId, "native:threat:1", NpcFearSeverity.Severe, 100),
            "civilian severe danger observation is saved");
        var civilianFear = civilianStore.ReadNpcAcuteFear(civilianId, 100);
        Equal(65d, civilianFear.Intensity, "civilian severe baseline remains 65 in saved intensity");
        Equal(65d, civilianFear.CurrentFear, "civilian severe fear stays at normal intensity");

        var combatantStore = NewStore(out string combatantId);
        RecordCombatRole(combatantStore, combatantId, NpcCombatRole.Rogue);
        True(combatantStore.TryObserveNpcAcuteFear(combatantId, "native:threat:1", NpcFearSeverity.Severe, 100),
            "combatant severe danger observation is saved");
        var combatantFear = combatantStore.ReadNpcAcuteFear(combatantId, 100);
        Equal(6.5d, combatantFear.Intensity, "combatant severe baseline is scaled before save");
        Equal(6.5d, combatantFear.CurrentFear, "newly scaled fear is not scaled again on read");

        var expectedHalfLife = NpcFearRecoveryPolicy.Create(2 * 60 * 60, 0.70, 1.50)
            .HalfLifeSeconds(null, NpcFearSeverity.Severe);
        Equal(expectedHalfLife, combatantFear.RecoveryHalfLifeSeconds,
            "role scaling does not change default severe recovery half-life");

        True(!combatantStore.TryObserveNpcAcuteFear(combatantId, "native:threat:2", NpcFearSeverity.Severe, 130),
            "same baseline inside 60 seconds remains throttled");
        Equal(100d, combatantStore.ReadNpcAcuteFear(combatantId, 130).ObservedGameTime,
            "rejected pulse leaves observation anchor unchanged");
        True(combatantStore.TryObserveNpcAcuteFear(combatantId, "native:threat:2", NpcFearSeverity.Severe, 160),
            "same baseline is accepted at 60-second boundary");
    }

    private static void AcuteFearProjectionPreservesHistory()
    {
        var store = NewStore(out string personId);
        RecordCombatRole(store, personId, NpcCombatRole.Civilian);
        True(store.TryObserveNpcAcuteFear(personId, "native:threat:legacy", NpcFearSeverity.Severe, 100),
            "civilian records high historical danger fear");
        var beforeRoleChange = store.Snapshot();
        Equal(65d, beforeRoleChange.NpcAcuteFears[0].Intensity, "historical civilian intensity is 65 before role change");

        RecordCombatRole(store, personId, NpcCombatRole.Assassin);
        var projected = store.ReadNpcAcuteFear(personId, 100);
        Equal(10d, projected.CurrentFear, "legacy high fear is capped at ten for combat role");
        Equal(65d, projected.Intensity, "projected view retains raw historical intensity");
        Equal(65d, store.Snapshot().NpcAcuteFears[0].Intensity,
            "role-aware read does not rewrite saved historical intensity");

        var oversizedLegacy = store.Snapshot();
        oversizedLegacy.NpcAcuteFears[0].Intensity = 80;
        var restoredLegacy = new WorldStateStore(456);
        restoredLegacy.Restore(oversizedLegacy);
        Equal(10d, restoredLegacy.ReadNpcAcuteFear(personId, 100).CurrentFear,
            "legacy raw intensity 80 projects to combat fear cap ten");
        Equal(80d, restoredLegacy.Snapshot().NpcAcuteFears[0].Intensity,
            "legacy raw intensity 80 remains available for forensics");
    }

    private static void AcuteFearAndRoleRestoreTogether()
    {
        var store = NewStore(out string personId);
        RecordCombatRole(store, personId, NpcCombatRole.Warrior);
        True(store.TryObserveNpcAcuteFear(personId, "native:threat:restore", NpcFearSeverity.Severe, 100),
            "combatant fear is observed before save");
        string json = JsonSerializer.Serialize(store.Snapshot(), SaveJsonOptions);
        WorldState saved = JsonSerializer.Deserialize<WorldState>(json, SaveJsonOptions);
        var restored = new WorldStateStore(789);
        restored.Restore(saved);

        Equal(NpcCombatRole.Warrior, restored.ReadNpcRole(personId).CombatRole,
            "combat role survives the same save as acute fear");
        var fear = restored.ReadNpcAcuteFear(personId, 100);
        Equal(6.5d, fear.CurrentFear, "restored low combat fear stays 6.5");
        Equal(6.5d, fear.Intensity, "restored saved intensity is not multiplied again");
    }

    private static void RecordCombatRole(WorldStateStore store, string personId, NpcCombatRole combatRole)
    {
        True(store.RecordNpcRoleEvidence(personId, NpcSocialRank.Unknown, combatRole, NpcServiceBinding.Unknown,
            null, "native:career:" + combatRole, null), "combat role evidence is recorded: " + combatRole);
    }

    private static WorldStateStore NewStore(out string personId)
    {
        var store = new WorldStateStore(123);
        var town = store.GetOrCreate(71, new[] { 91 }, 1);
        personId = town.Households[0].Residents[0].Id;
        return store;
    }

    private static NpcRoleState Role(NpcSocialRank rank = NpcSocialRank.Unknown,
        NpcCombatRole combat = NpcCombatRole.Unknown, NpcServiceBinding service = NpcServiceBinding.Unknown)
    {
        return new NpcRoleState { SocialRank = rank, CombatRole = combat, ServiceBinding = service };
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message + "; expected " + expected + ", got " + actual);
    }
}
