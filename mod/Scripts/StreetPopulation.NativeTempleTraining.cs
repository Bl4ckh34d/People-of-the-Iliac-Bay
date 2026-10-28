using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private void OfferNativeTempleTrainingAction(ResidentActor actor, string text, List<NpcActionOption> options, bool semantic)
        {
            if (options == null) return;
            int maximum = semantic ? NpcActionCandidateCatalog.MaximumCandidates : 24;
            // Publish each eligible native skill so semantic selection can bind
            // the requested lesson, not just the first skill in the native enum.
            for (int skill = 0; skill < 35 && options.Count < maximum; skill++)
            {
                StaticNPC trainer; NativeNpcTempleTrainingOffer offer; Vector3 approach; string ignored;
                if (!TryFindTempleTrainingOffer(actor, skill, null, true, out trainer, out offer, out approach, out ignored)) continue;
                var target = ActionPosition(approach);
                target.PersonId = offer.Trainer.Id; target.ContainerId = NativeNpcTempleTraining.TargetContainerId;
                target.ItemId = offer.Skill.ToString(CultureInfo.InvariantCulture); target.Quantity = 1; target.QuotedGold = offer.Price;
                AddActionOption(options, NpcActionKind.Purchase, target,
                    "buy one native temple training lesson in " + DaggerfallUnity.Instance.TextProvider.GetSkillName((DFCareer.Skills)offer.Skill) +
                    " for " + offer.Price.ToString(CultureInfo.InvariantCulture) + " gold from my own wallet; it advances only my saved skill and costs three game hours",
                    text, semantic);
            }
        }

        private bool IsNativeTempleTrainingTarget(NpcActionTarget target)
        {
            int skill;
            return target != null && target.Kind == NpcActionTargetKind.IndoorPoint && target.Quantity == 1 &&
                target.ContainerId == NativeNpcTempleTraining.TargetContainerId && !string.IsNullOrWhiteSpace(target.PersonId) &&
                int.TryParse(target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out skill) && skill >= 0 && skill < 35;
        }

        private bool PrepareNativeTempleTrainingAction(ResidentActor actor, NpcActionJob job, out string reason)
        {
            reason = "The selected native temple lesson is unavailable.";
            if (!actor || job == null || job.Kind != NpcActionKind.Purchase || !IsNativeTempleTrainingTarget(job.Target) ||
                actor.Resident == null || actor.Resident.Id != job.ResidentId || QuestOwned(actor.Resident) ||
                actor.Resident.IsQuestProtected || IsAlarmed(job.ResidentId)) return false;
            int skill; if (!int.TryParse(job.Target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out skill)) return false;
            StaticNPC trainer; NativeNpcTempleTrainingOffer offer; Vector3 approach;
            if (!TryFindTempleTrainingOffer(actor, skill, job.Target.PersonId, true, out trainer, out offer, out approach, out reason) ||
                offer.Price != job.Target.QuotedGold) { reason = "The quoted native temple lesson changed. A new agreement is required."; return false; }
            var target = ActionPosition(approach);
            target.PersonId = offer.Trainer.Id; target.ContainerId = NativeNpcTempleTraining.TargetContainerId;
            target.ItemId = skill.ToString(CultureInfo.InvariantCulture); target.Quantity = 1; target.QuotedGold = offer.Price;
            job.Target = target; job.ServiceCode = NativeNpcTempleTraining.ServiceCode; job.ServicePrice = offer.Price;
            job.ServiceStep = 0; job.FinishAt = 0; job.ExpectedInventoryRevision = offer.ExpectedInventoryRevision;
            job.DeadlineAt = Math.Max(job.DeadlineAt, ActionNow() + NativeNpcTempleTraining.DurationSeconds + 600d);
            return true;
        }

        private bool ExecuteNativeTempleTrainingAction(NpcActionJob job, ResidentActor actor, double now, out bool completed, out string reason)
        {
            completed = false;
            reason = "The selected native temple lesson is unavailable.";
            if (!actor || job == null || job.ServiceCode != NativeNpcTempleTraining.ServiceCode || !IsNativeTempleTrainingTarget(job.Target)) return false;
            StaticNPC trainer; NativeNpcTempleTrainingOffer offer; Vector3 approach;
            if (!TryFindTempleTrainingOffer(actor, int.Parse(job.Target.ItemId, CultureInfo.InvariantCulture), job.Target.PersonId, job.ServiceStep == 0,
                out trainer, out offer, out approach, out reason) || Vector3.Distance(actor.transform.position - Vector3.up, ActionWorldPoint(job.Target)) > .55f ||
                Vector3.Distance(actor.transform.position, trainer.transform.position) > 2.2f ||
                !CivilianResponses.Sees(actor.transform.position, trainer.transform.position + Vector3.up, 2.2f)) return false;
            return NativeNpcTempleTraining.TryExecuteStage(worldStore, job, actor, trainer, KeeperPerson,
                interior.BuildingData.FactionId, now, out completed, out reason);
        }

        private bool TryFindTempleTrainingOffer(ResidentActor actor, int requestedSkill, string trainerId, bool requireFunds,
            out StaticNPC foundTrainer, out NativeNpcTempleTrainingOffer foundOffer, out Vector3 approach, out string reason)
        {
            foundTrainer = null; foundOffer = null; approach = Vector3.zero;
            reason = "No current native temple trainer can offer this resident a lesson.";
            if (!actor || !interior || indoorGeometry == null || interior.BuildingData.BuildingType != DFLocation.BuildingTypes.Temple ||
                !PlayerActivate.IsBuildingOpen(interior.BuildingData.BuildingType) || worldStore == null) return false;
            int buildingFactionId = interior.BuildingData.FactionId;
            var trainers = new List<StaticNPC>(interior.GetComponentsInChildren<StaticNPC>());
            trainers.Sort((left, right) => string.CompareOrdinal(left == null ? string.Empty : left.Data.hash.ToString(CultureInfo.InvariantCulture),
                right == null ? string.Empty : right.Data.hash.ToString(CultureInfo.InvariantCulture)));
            foreach (var trainer in trainers)
            {
                if (!trainer || !trainer.gameObject.activeInHierarchy || trainer.gameObject.scene != interior.gameObject.scene ||
                    trainer.GetComponentInParent<DaggerfallInterior>() != interior) continue;
                for (int skill = requestedSkill < 0 ? 0 : requestedSkill; skill < (requestedSkill < 0 ? 35 : requestedSkill + 1); skill++)
                {
                    NativeNpcTempleTrainingOffer offer; string candidateReason;
                    if (!NativeNpcTempleTraining.TryPrepareOffer(worldStore, actor, trainer, KeeperPerson, buildingFactionId,
                        skill, requireFunds, out offer, out candidateReason) || offer == null || offer.Trainer == null ||
                        trainerId != null && offer.Trainer.Id != trainerId) { if (requestedSkill >= 0) break; continue; }
                    Vector3 candidateApproach;
                    if (!TryTempleTrainingApproach(actor, trainer, out candidateApproach)) continue;
                    foundTrainer = trainer; foundOffer = offer; approach = candidateApproach; reason = null; return true;
                }
            }
            return false;
        }

        private bool TryTempleTrainingApproach(ResidentActor actor, StaticNPC trainer, out Vector3 point)
        {
            point = Vector3.zero;
            Vector3 actual = actor.transform.position - Vector3.up;
            // Native RMB people are positioned at their billboard centre.
            // Route and floor queries, like existing keeper standing positions,
            // require the actual native foot point instead.
            var billboard=trainer.GetComponent<DaggerfallBillboard>();
            Vector3 target = trainer.transform.position-Vector3.up*(billboard?billboard.Summary.Size.y*.5f:1f);
            Vector3 away = actual - target; away.y = 0;
            if (away.sqrMagnitude < .001f) { away = -trainer.transform.forward; away.y = 0; }
            if (away.sqrMagnitude < .001f) away = Vector3.forward;
            List<GuardPoint3> route = IndoorPersonApproachRoute(actual, target, target + away.normalized, 1.8f);
            if (route == null || route.Count == 0) return false;
            GuardPoint3 end = route[route.Count - 1];
            point = interior.transform.TransformPoint(new Vector3((float)end.X, (float)end.Y, (float)end.Z));
            return Vector3.Distance(point, target) <= 1.8f && CivilianResponses.Sees(point + Vector3.up, target + Vector3.up, 1.8f);
        }
    }
}
