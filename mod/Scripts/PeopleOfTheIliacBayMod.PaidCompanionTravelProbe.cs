using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        // The supplied callback must walk through a real seamless exterior gate.
        // It deliberately neither hires a traveler nor changes the player's world.
        public IEnumerator ProbePaidCompanionSeamlessExterior(string residentId,string expectedCharacter,
            Func<IEnumerator> crossExterior)
        {
            var game=GameManager.Instance;
            PaidTravelProbeRequire(Array.IndexOf(Environment.GetCommandLineArgs(),
                "-peopleoftheiliacbay-paid-companion-travel-probe")>=0 && game!=null && game.PlayerEntity!=null &&
                !string.IsNullOrEmpty(expectedCharacter) && expectedCharacter.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal) &&
                game.PlayerEntity.Name==expectedCharacter && !game.IsPlayerInside && streets!=null && streets.Ready &&
                roadActors!=null && store!=null && crossExterior!=null,
                "Paid travel probe requires an owned exterior fixture and a native exterior-crossing callback.");
            if(string.IsNullOrEmpty(residentId))residentId=FindActivePaidTravelProbeCandidate();
            var traveler=store.Traveler(residentId);
            var actor=streets.FindActor(residentId);
            var job=store.ActiveNpcAction(residentId);
            NpcServiceContract contract=null;
            double now=PaidTravelProbeNow();
            PaidTravelProbeRequire(traveler!=null && traveler.IsAlive && actor && actor.gameObject.activeInHierarchy &&
                !roadActors.OwnsPaidCompanion(residentId) && job!=null && job.PaidTravelHandoff==null &&
                NpcPaidCompanionTravel.TryReadActiveTravel(store,job,now,out contract),
                "Paid travel probe requires an already hired, visible Inn traveler with one active paid Follow contract covering Travel; it never hires or substitutes a candidate.");

            actor.CaptureState();
            string inventory=InventorySignature(store.ReadNpcInventory(residentId));
            string vitals=VitalsSignature(store.ReadNpcVitals(residentId,store.ResolveNpcArchetype(residentId)));
            int bodyId=actor.GetInstanceID();
            int personId=actor.Person ? actor.Person.GetInstanceID() : 0;
            string actionId=job.Id,serviceCode=job.ServiceCode;
            double acceptedAt=job.AcceptedAt,finishAt=job.FinishAt,deadlineAt=job.DeadlineAt;
            var contractBefore=ContractSignature(contract);
            yield return crossExterior();

            float until=Time.realtimeSinceStartup+12f;
            while(Time.realtimeSinceStartup<until && (!roadActors.OwnsPaidCompanion(residentId) ||
                !ReferenceEquals(roadActors.FindPaidCompanion(residentId),actor))) yield return null;
            job=store.ReadNpcAction(actionId);
            NpcServiceContract contractAfter=null;
            PaidTravelProbeRequire(!game.IsPlayerInside && actor && actor.gameObject.activeInHierarchy &&
                roadActors.OwnsPaidCompanion(residentId) && ReferenceEquals(roadActors.FindPaidCompanion(residentId),actor) &&
                actor.GetInstanceID()==bodyId && actor.Person && actor.Person.GetInstanceID()==personId &&
                actor.transform.parent==roadActors.transform && job!=null && job.Id==actionId && job.ServiceCode==serviceCode &&
                job.AcceptedAt==acceptedAt && job.FinishAt==finishAt && job.DeadlineAt==deadlineAt && job.PaidTravelHandoff!=null &&
                NpcPaidCompanionTravel.TryReadActiveTravel(store,job,PaidTravelProbeNow(),out contractAfter) &&
                ContractSignature(contractAfter)==contractBefore,
                "Native exterior handoff lost the paid body, changed its contract, or did not transfer the same actor to road ownership.");
            PaidTravelProbeRequire(CountResidentBodies(residentId)==1,
                "Native exterior handoff created duplicate bodies for the paid traveler.");
            actor.CaptureState();
            PaidTravelProbeRequire(InventorySignature(store.ReadNpcInventory(residentId))==inventory &&
                VitalsSignature(store.ReadNpcVitals(residentId,store.ResolveNpcArchetype(residentId)))==vitals,
                "Native exterior handoff changed the companion inventory or vital resources.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PAID_TRAVEL_SEAMLESS_PASS: resident="+residentId+
                "; action="+actionId+"; body="+bodyId+"; owner=road; single-body=true; contract-inventory-vitals=unchanged");
        }

        public string FindActivePaidTravelProbeCandidate()
        {
            if(streets==null || !streets.Ready || store==null)return null;
            double now=PaidTravelProbeNow();
            foreach(var traveler in store.Snapshot().Travelers)
            {
                if(traveler==null || !traveler.IsAlive || !streets.FindActor(traveler.Id))continue;
                NpcServiceContract contract;
                if(NpcPaidCompanionTravel.TryReadActiveTravel(store,store.ActiveNpcAction(traveler.Id),now,out contract)) return traveler.Id;
            }
            return null;
        }

        private static int CountResidentBodies(string residentId)
        {
            int count=0;
            foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if(candidate && candidate.gameObject.activeInHierarchy && candidate.Resident!=null && candidate.Resident.Id==residentId)count++;
            return count;
        }

        private static string InventorySignature(NpcInventoryState value)
        {
            if(value==null)return "<none>";
            var items=new List<string>();
            foreach(var item in value.Items)items.Add(item.Uid+":"+item.NativePayload);
            items.Sort(StringComparer.Ordinal);
            return value.WalletGold+"|"+string.Join("|",items.ToArray());
        }

        private static string VitalsSignature(NpcVitalsView value)
        {
            return value==null ? "<none>" : value.IsAlive+"|"+value.MaximumHealth+"|"+value.CurrentHealth+"|"+
                value.MaximumMagicka+"|"+value.CurrentMagicka;
        }

        private static string ContractSignature(NpcServiceContract value)
        {
            return value==null ? "<none>" : value.ContractId+"|"+value.NpcId+"|"+value.PayerId+"|"+value.Coverage+"|"+
                value.Status+"|"+value.BlockStartAt+"|"+value.BlockEndAt+"|"+value.FundedGold+"|"+value.ReleasedGold+"|"+value.RefundedGold+"|"+value.Revision;
        }

        private static double PaidTravelProbeNow()
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            return (double)clock.ToSeconds()+clock.Second-Math.Floor(clock.Second);
        }

        private static void PaidTravelProbeRequire(bool condition,string message)
        { if(!condition)throw new InvalidOperationException(message); }
    }
}
