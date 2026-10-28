using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcExperienceDutyProjectionTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Terminal action return movement does not discard an unrelated experience appraisal", () =>
            {
                var store = NewStore(out string owner);
                var action = MoveAction(owner, "duty:completed-return", 10, 80);
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, action, true, true, true, out string reason), reason);
                Check.True(store.ObserveNpcAction(action.Id, 1, 11, NpcActionObservation.Cancelled("Stop requested.")));
                Check.True(store.SaveNpcActionPosition(action.Id, 2, action.Target, true, 12));
                Enqueue(store, owner, "during-completed-return", 20);
                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 20, out var ticket));
                Check.True(!HasDutyId(ticket.Request.MotivationContext, action.Id));
                Check.True(store.SaveNpcActionPosition(action.Id, 3, action.Target, true, 21));
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket,
                    NpcExperienceCommitTests.Result(ticket.Request, mood: 1)),
                    "A position-only update of a terminal return discarded the appraisal.");
                Check.Equal(0, store.ReadPendingNpcExperiences(owner, 21).Count);
            }));

            cases.Add(("A new live action replacing a terminal return still invalidates an experience appraisal", () =>
            {
                var store = NewStore(out string owner);
                var action = MoveAction(owner, "duty:old-return", 10, 80);
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, action, true, true, true, out string reason), reason);
                Check.True(store.ObserveNpcAction(action.Id, 1, 11, NpcActionObservation.Cancelled("Stop requested.")));
                Check.True(store.SaveNpcActionPosition(action.Id, 2, action.Target, true, 12));
                Enqueue(store, owner, "before-new-action", 20);
                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 20, out var ticket));
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,
                    MoveAction(owner, "duty:new-action", 21, 80), true, true, true, out reason), reason);
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket,
                    NpcExperienceCommitTests.Result(ticket.Request, mood: 1)));
                Check.Equal(1, store.ReadPendingNpcExperiences(owner, 21).Count);
            }));

            cases.Add(("Unrelated NPC service writes do not discard a completed observer appraisal", () =>
            {
                var store = NewStore(out string owner);
                string other = store.GetOrCreate(20, new[] { 200, 201 }, 1).Households[1].Residents[0].Id;
                AddFundedActiveService(store, owner, "service:owner-stable", 10, 10);
                Enqueue(store, owner, "service-unrelated-write", 20);
                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 20, out var ticket));
                AddFundedActiveService(store, other, "service:other-added", 10, 10);
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket,
                    NpcExperienceCommitTests.Result(ticket.Request, mood: 1)));
                Check.Equal(0, store.ReadPendingNpcExperiences(owner, 20).Count);
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket,
                    NpcExperienceCommitTests.Result(ticket.Request, mood: 1)));
            }));

            cases.Add(("A new observer service contract still invalidates its captured appraisal", () =>
            {
                var store = NewStore(out string owner);
                Enqueue(store, owner, "service-owner-write", 20);
                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 20, out var ticket));
                AddFundedActiveService(store, owner, "service:owner-added", 10, 10);
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket,
                    NpcExperienceCommitTests.Result(ticket.Request, mood: 1)));
                Check.Equal(1, store.ReadPendingNpcExperiences(owner, 20).Count);
            }));

            cases.Add(("An observer service lifecycle change still invalidates its captured appraisal", () =>
            {
                var store = NewStore(out string owner);
                AddFundedActiveService(store, owner, "service:owner-suspending", 10, 10);
                Enqueue(store, owner, "service-owner-suspended", 20);
                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 20, out var appraisal));
                var write = store.CaptureNpcServiceContractWriteTicket();
                var contract = write.SourceLedger.Read("service:owner-suspending");
                Check.True(contract.TrySuspend(contract.Revision, 20, out var suspended));
                Check.True(NpcServiceContractLedger.TryReplace(write.SourceLedger, write.ExpectedLedgerRevision,
                    suspended, out var updated));
                Check.True(store.TryCommitNpcServiceContracts(write, updated,
                    new List<NpcServiceTransferReceipt>(), new List<NpcServiceRenewalAuthorization>(), out string error), error);
                Check.True(!store.TryCommitNpcExperienceAppraisal(appraisal,
                    NpcExperienceCommitTests.Result(appraisal.Request, mood: 1)));
                Check.Equal(1, store.ReadPendingNpcExperiences(owner, 20).Count);
            }));

            cases.Add(("Native event appraisal captures the observer's appointment and active action duties", () =>
            {
                var store = NewStore(out string owner);
                Check.True(store.TryReserveAppointment(new AppointmentRecord
                {
                    Id = "duty:appointment", ResidentId = owner, Purpose = "meet", StartsAt = 100,
                    EndsAt = 160, GraceSeconds = 30, Importance = 1,
                    Place = new AppointmentPlace { Kind = "building", SettlementId = 20, BuildingKey = 200 }
                }, 10, out string reason), reason);
                var action = MoveAction(owner, "duty:action", 10, 80);
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, action, true, true, true, out reason), reason);
                Enqueue(store, owner, "duty-context", 10);

                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 10, out var ticket));
                NpcMotivationContextSnapshot context = ticket.Request.MotivationContext;
                Check.True(context != null);
                Check.Equal(2, context.Duties.Count);
                Check.True(HasDuty(context, "duty:appointment", NpcMotivationDutyKind.Appointment,
                    NpcMotivationDutyStatus.Accepted, NpcMotivationDutyScope.Schedule));
                Check.True(HasDuty(context, action.Id, NpcMotivationDutyKind.Personal,
                    NpcMotivationDutyStatus.Accepted, NpcMotivationDutyScope.Travel));
                Check.Equal(0, context.Capabilities.Count);
                Check.Equal(0, context.Resources.Count);
                Check.Equal(0, context.Threats.Count);
            }));

            cases.Add(("Past attended appointments cannot exhaust current appraisal duties", () =>
            {
                var store = NewStore(out string owner);
                for (int i = 0; i < 33; i++)
                {
                    double start = 1000 + i * 1000;
                    string id = "past-attended-" + i;
                    Check.True(store.TryReserveAppointment(new AppointmentRecord {
                        Id = id, ResidentId = owner, Purpose = "meet", StartsAt = start,
                        EndsAt = start + 60, GraceSeconds = 30, PatienceSeconds = 600,
                        Importance = 1, Place = new AppointmentPlace {
                            Kind = "building", SettlementId = 20, BuildingKey = 200 }
                    }, start - 500, out string reason), reason);
                    store.ObserveAppointment(id, start, true, false, false);
                    store.ObserveAppointment(id, start + 10, true, true, false);
                    Check.Equal("attended", store.FindAppointment(id).Status);
                }
                Check.Equal(33, store.ReadPendingNpcExperiences(owner, 100).Count);
                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 33100, out var past),
                    "Past attended duties blocked appraisal capture.");
                Check.Equal(0, past.Request.MotivationContext.Duties.Count);

                const string current = "current-attended";
                Check.True(store.TryReserveAppointment(new AppointmentRecord {
                    Id = current, ResidentId = owner, Purpose = "meet", StartsAt = 34000,
                    EndsAt = 34060, GraceSeconds = 30, PatienceSeconds = 600,
                    Importance = 1, Place = new AppointmentPlace {
                        Kind = "building", SettlementId = 20, BuildingKey = 200 }
                }, 33500, out string currentReason), currentReason);
                store.ObserveAppointment(current, 34000, true, false, false);
                store.ObserveAppointment(current, 34010, true, true, false);
                Check.Equal("attended", store.FindAppointment(current).Status);
                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 34030, out var active),
                    "Current attended duty blocked appraisal capture.");
                Check.Equal(1, active.Request.MotivationContext.Duties.Count);
                Check.True(HasDuty(active.Request.MotivationContext, current,
                    NpcMotivationDutyKind.Appointment, NpcMotivationDutyStatus.Active,
                    NpcMotivationDutyScope.Schedule), "Current attended appointment duty was absent.");
                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 34060, out var atEnd),
                    "At-end appraisal capture failed.");
                Check.True(HasDutyId(atEnd.Request.MotivationContext, current),
                    "Current attended duty ended before agreed boundary.");
                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 34061, out var afterEnd),
                    "After-end appraisal capture failed.");
                Check.Equal(0, afterEnd.Request.MotivationContext.Duties.Count);

                const string unresolved = "accepted-in-grace";
                Check.True(store.TryReserveAppointment(new AppointmentRecord {
                    Id = unresolved, ResidentId = owner, Purpose = "meet", StartsAt = 35000,
                    EndsAt = 35060, GraceSeconds = 30, PatienceSeconds = 600,
                    Importance = 1, Place = new AppointmentPlace {
                        Kind = "building", SettlementId = 20, BuildingKey = 200 }
                }, 34500, out string graceReason), graceReason);
                Check.True(AppointmentTiming.ResolutionDeadline(store.FindAppointment(unresolved)) > 35070);
                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 35070, out var grace),
                    "Grace-window appraisal capture failed.");
                Check.True(HasDuty(grace.Request.MotivationContext, unresolved,
                    NpcMotivationDutyKind.Appointment, NpcMotivationDutyStatus.Accepted,
                    NpcMotivationDutyScope.Schedule),
                    "Unresolved appointment lost its valid grace-window duty.");
            }));

            cases.Add(("Native event appraisal includes only funded current service duties for its owner", () =>
            {
                var store = NewStore(out string owner);
                SettlementPopulation population = store.GetOrCreate(20, new[] { 200, 201 }, 1);
                string other = population.Households[1].Residents[0].Id;
                AddFundedActiveService(store, owner, "duty:funded-owner", 10, 10);
                AddFundedActiveService(store, other, "duty:funded-other", 10, 10);
                Enqueue(store, owner, "service-duty-context", 20);

                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 20, out var ticket));
                NpcMotivationContextSnapshot context = ticket.Request.MotivationContext;
                Check.True(HasDuty(context, "service:duty:funded-owner", NpcMotivationDutyKind.Service,
                    NpcMotivationDutyStatus.Active, NpcMotivationDutyScope.Travel | NpcMotivationDutyScope.Combat));
                Check.True(!HasDutyId(context, "service:duty:funded-other"));
            }));

            cases.Add(("Native event appraisal captures the current multi-step task as a personal duty", () =>
            {
                var store = NewStore(out string owner);
                var task = new NpcTaskPlan { Id = "duty:task", ProposalId = "duty:task", ResidentId = owner,
                    AcceptedAt = 5, UpdatedAt = 5, DeadlineAt = 60 };
                task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 10,
                    Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" } });
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task, true, out string reason), reason);
                Enqueue(store, owner, "task-duty-context", 10);

                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 10, out var ticket));
                Check.True(HasDuty(ticket.Request.MotivationContext, task.Id, NpcMotivationDutyKind.Personal,
                    NpcMotivationDutyStatus.Accepted, NpcMotivationDutyScope.Assistance));
            }));

            cases.Add(("Native event appraisal excludes appointments and actions owned by another resident", () =>
            {
                var store = NewStore(out string owner);
                SettlementPopulation population = store.GetOrCreate(20, new[] { 200, 201 }, 1);
                string other = population.Households[1].Residents[0].Id;
                Check.True(store.TryReserveAppointment(new AppointmentRecord
                {
                    Id = "duty:foreign-appointment", ResidentId = other, Purpose = "meet", StartsAt = 100,
                    EndsAt = 160, GraceSeconds = 30, Importance = 1,
                    Place = new AppointmentPlace { Kind = "building", SettlementId = 20, BuildingKey = 201 }
                }, 10, out string reason), reason);
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, MoveAction(other, "duty:foreign-action", 10, 80),
                    true, true, true, out reason), reason);
                Enqueue(store, owner, "foreign-duty-context", 10);

                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 10, out var ticket));
                Check.Equal(0, ticket.Request.MotivationContext.Duties.Count);
            }));

            cases.Add(("Native event appraisal rejects commit after an appointment source revision changes", () =>
            {
                var store = NewStore(out string owner);
                Check.True(store.TryReserveAppointment(new AppointmentRecord
                {
                    Id = "duty:stale-appointment", ResidentId = owner, Purpose = "meet", StartsAt = 100,
                    EndsAt = 160, GraceSeconds = 30, Importance = 1,
                    Place = new AppointmentPlace { Kind = "building", SettlementId = 20, BuildingKey = 200 }
                }, 10, out string reason), reason);
                Enqueue(store, owner, "stale-duty-context", 10);
                Check.True(store.TryCaptureNpcExperienceAppraisal(owner, 10, out var ticket));
                store.ObserveAppointment("duty:stale-appointment", 100, true, false, false);

                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket,
                    NpcExperienceCommitTests.Result(ticket.Request, mood: 1)));
                Check.Equal(1, store.ReadPendingNpcExperiences(owner, 10).Count);
            }));
        }

        private static WorldStateStore NewStore(out string owner)
        {
            var store = new WorldStateStore(109);
            owner = store.GetOrCreate(20, new[] { 200, 201 }, 2).Households[0].Residents[0].Id;
            store.GetOrCreateNpcPersonality(owner, 109, NpcSocialBackground.Commoner);
            return store;
        }

        private static NpcActionJob MoveAction(string owner, string id, double acceptedAt, double deadlineAt)
        {
            return new NpcActionJob
            {
                Id = id, ProposalId = id, ResidentId = owner, Kind = NpcActionKind.MoveTo,
                Target = new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint,
                    SettlementId = 20, BuildingKey = 200, X = 1, Z = 1 },
                Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = acceptedAt,
                UpdatedAt = acceptedAt, DeadlineAt = deadlineAt
            };
        }

        private static void AddFundedActiveService(WorldStateStore store, string owner, string contractId,
            double acceptedAt, double startAt)
        {
            const string payer = NpcDirectedRelationship.PlayerTargetId;
            var ticket = store.CaptureNpcServiceContractWriteTicket();
            Check.True(NpcServiceContractTerms.TryCreate(contractId, payer, owner, "home:duty-test",
                NpcServiceRole.Fighter, NpcServiceCoverage.Follow | NpcServiceCoverage.Combat,
                120, false, NpcServiceRiskScope.OrdinaryCombat, Array.Empty<NpcServiceProtectedTarget>(),
                NpcServiceRestTreatment.RestNotIncluded, NpcServiceResupplyResponsibility.SeparatelyNegotiated,
                Array.Empty<NpcServiceReservedObligation>(), id => id == payer || id == owner,
                home => home == "home:duty-test", (id, category, revision) => true,
                (id, revision, kind, start, end) => true, out var terms));
            var funding = NpcServiceTransferReceipt.Create("receipt:" + contractId, contractId,
                NpcServiceTransferKind.Funding, payer, owner, 120, acceptedAt);
            Check.True(NpcServiceContractLedger.TryReserve(ticket.SourceLedger, ticket.ExpectedLedgerRevision,
                terms, funding, out var reserved));
            Check.True(NpcDailyPaymentFixture.Commit(store,ticket,reserved,funding,out string error), error);
            ticket = store.CaptureNpcServiceContractWriteTicket();
            reserved = ticket.SourceLedger;
            Check.True(reserved.Read(contractId).TryStart(reserved.Read(contractId).Revision, startAt, out var active));
            Check.True(NpcServiceContractLedger.TryReplace(reserved, reserved.Revision, active, out var updated));
            Check.True(store.TryCommitNpcServiceContracts(ticket, updated,
                new List<NpcServiceTransferReceipt>(), new List<NpcServiceRenewalAuthorization>(), out error), error);
        }

        private static bool HasDuty(NpcMotivationContextSnapshot context, string id,
            NpcMotivationDutyKind kind, NpcMotivationDutyStatus status, NpcMotivationDutyScope scope)
        {
            foreach (NpcMotivationDuty duty in context.Duties)
                if (duty.Id == id && duty.Kind == kind && duty.Status == status && duty.Scope == scope) return true;
            return false;
        }

        private static bool HasDutyId(NpcMotivationContextSnapshot context, string id)
        {
            foreach (NpcMotivationDuty duty in context.Duties) if (duty.Id == id) return true;
            return false;
        }

        private static void Enqueue(WorldStateStore store, string owner, string outcome, double at)
        {
            Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, NpcExperienceEvent.FromNativeOutcome(owner, owner,
                "receipt:" + outcome, "episode:duty", outcome, NpcExperienceEventKind.Healing, at,
                NpcResponsibleActorAttribution.Known, NpcDirectedRelationship.PlayerTargetId)));
        }
    }
}
