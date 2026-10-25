using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.MagicAndEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class HealthRestChoice
        {
            public string ResidentId,CareEpisodeId,BedId,RoomId;
            public int SettlementId,BuildingKey,BedRecordIndex,Nights,Price,RecoveryRate,ExpectedGain;
            public bool PaidRoom,PrepaidRoom,AssignedInnBed;
            public long InventoryRevision,VitalsRevision;
            public double StartsAt,DurationSeconds,ExpectedSeconds;
            public NpcActionTarget Target;
        }

        // Pure from the scorer's perspective: this verifies the loaded local bed
        // and quotes an NPC-funded stay, but never books a room or changes gold.
        private bool TryGetHealthRestChoice(ResidentActor actor,NpcTaskPlan task,double now,
            out int costGold,out int expectedGain,out double expectedSeconds)
        {
            costGold=expectedGain=0;expectedSeconds=0;
            if(worldStore==null || actor==null || actor.Resident==null)return false;
            NpcHealthCareRecord care=worldStore.ReadNpcHealthCare(actor.Resident.Id);
            HealthRestChoice choice;string reason;
            if(care==null || !TryDiscoverHealthRestChoice(actor,care,now,out choice,out reason))return false;
            if(task!=null && !TaskUsesHealthRestChoice(task,choice))return false;
            costGold=choice.Price;expectedGain=choice.ExpectedGain;expectedSeconds=choice.ExpectedSeconds;
            return true;
        }

        private bool TryCreateHealthRestTask(ResidentActor actor,NpcHealthCareRecord care,double now,
            out NpcTaskPlan task,out string reason)
        {
            task=null;reason="No safe, accessible bed is available for uninterrupted recovery here.";
            HealthRestChoice choice;
            if(!TryDiscoverHealthRestChoice(actor,care,now,out choice,out reason))return false;
            task=HealthTask(care,now);
            task.DeadlineAt=now+choice.ExpectedSeconds+600d;
            task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.RestToHeal,
                Target=NpcActionExecutor.CopyTarget(choice.Target),DurationSeconds=choice.DurationSeconds});
            reason=null;return true;
        }

        private bool TryConfirmHealthRestReservation(ResidentActor actor,NpcTaskPlan task,double now,out string reason)
        {
            // The care dispatcher shares this hook across rest and potion choices.
            if(task!=null && task.Steps!=null && task.Steps.Count>0 &&
                task.Steps[0]!=null && task.Steps[0].Kind!=NpcActionKind.RestToHeal)
            {reason=null;return true;}
            reason="The bed or quoted Inn stay changed before the accepted treatment could be reserved.";
            if(worldStore==null || actor==null || task==null || task.ResidentId!=actor.Resident.Id ||
                task.Phase!=NpcTaskPhase.Ready || task.Steps==null || task.Steps.Count!=1 ||
                task.Steps[0].Kind!=NpcActionKind.RestToHeal)return false;
            NpcHealthCareRecord care=worldStore.ReadNpcHealthCare(actor.Resident.Id);
            if(care==null || care.Phase!=NpcHealthCarePhase.Acting || care.TaskId!=task.Id)return false;
            HealthRestChoice choice;string discoverReason;
            if(!TryDiscoverHealthRestChoice(actor,care,now,out choice,out discoverReason) || !TaskUsesHealthRestChoice(task,choice))
            {reason=discoverReason??reason;return false;}
            if(choice.PrepaidRoom)
            {
                NpcHealthLodgingReservation saved=worldStore.NpcHealthRestLodging(actor.Resident.Id,care.Id,now);
                if(saved==null || saved.SettlementId!=choice.SettlementId || saved.BuildingKey!=choice.BuildingKey ||
                    saved.RoomId!=choice.RoomId || saved.BedId!=choice.BedId || saved.EndsAt<now+choice.ExpectedSeconds)return false;
                reason=null;return true;
            }
            if(!choice.PaidRoom){reason=null;return true;}

            NativeNpcInventoryOwner owner=actor.GetComponent<NativeNpcInventoryOwner>();
            if(!owner || owner.Binding==null || !NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason))return false;
            NpcInventoryState inventory=worldStore.ReadNpcInventory(actor.Resident.Id);
            if(inventory==null || inventory.Revision!=owner.Binding.Revision || inventory.WalletGold<choice.Price)
            {reason="The resident cannot afford the full quoted Inn stay.";return false;}
            var request=new NpcHealthLodgingReservationRequest {ResidentId=actor.Resident.Id,
                CareEpisodeId=care.Id,TaskId=task.Id,ExpectedCareRevision=care.Revision,
                ExpectedInventoryRevision=inventory.Revision,SettlementId=choice.SettlementId,
                BuildingKey=choice.BuildingKey,RoomId=choice.RoomId,BedId=choice.BedId,
                Nights=choice.Nights,Price=choice.Price,StartsAt=now};
            NpcHealthLodgingReservation reservation;
            if(!worldStore.TryReserveNpcHealthRestLodging(request,out reservation) || reservation==null)
            {reason="The exact rentable room became unavailable or the resident's wallet changed.";return false;}
            if(reservation.ResidentId!=actor.Resident.Id || reservation.CareEpisodeId!=care.Id ||
                reservation.SettlementId!=choice.SettlementId || reservation.BuildingKey!=choice.BuildingKey ||
                reservation.RoomId!=choice.RoomId || reservation.BedId!=choice.BedId || reservation.EndsAt<now+choice.ExpectedSeconds)
            {reason="The saved Inn reservation does not match the accepted bed-rest task.";return false;}
            reason=null;return true;
        }

        private bool PrepareHealthRestAction(ResidentActor actor,NpcActionJob job,out string reason)
        {
            reason="The exact accessible bed is no longer a safe treatment target.";
            if(worldStore==null || actor==null || job==null || job.Kind!=NpcActionKind.RestToHeal ||
                job.ResidentId!=actor.Resident.Id || job.Target==null || job.Target.Kind!=NpcActionTargetKind.IndoorPoint ||
                job.Target.ItemId!=null || job.Target.PersonId!=null)return false;
            NpcHealthCareRecord care=worldStore.ReadNpcHealthCare(job.ResidentId);
            if(care==null || care.Phase!=NpcHealthCarePhase.Acting || care.TaskId==null ||
                job.Id!=care.TaskId+":step:0")return false;
            HealthRestChoice choice;string failure;
            if(!TryDiscoverHealthRestChoice(actor,care,job.AcceptedAt,out choice,out failure) ||
                !TargetMatchesHealthRest(job.Target,choice.Target) || job.RestDurationSeconds!=choice.DurationSeconds)
            {reason=failure??reason;return false;}
            NativeNpcVitalsBinding binding=actor.GetComponent<NativeNpcVitalsBinding>();
            if(!binding)return false;
            binding.Capture();
            NpcVitalsView vitals=worldStore.ReadNpcVitals(job.ResidentId,HealthArchetype(worldStore,job.ResidentId));
            if(vitals==null || !vitals.IsPersisted || !vitals.IsAlive || vitals.CurrentHealth>=vitals.MaximumHealth ||
                vitals.Revision<1 || !NativeRestStatusSafe(actor))return false;
            job.ExpectedVitalsRevision=vitals.Revision;
            job.RestStartedAt=0;job.FinishAt=0;
            reason=null;return true;
        }

        private bool ExecuteHealthRestAction(NpcActionJob job,ResidentActor actor,double now,out string reason)
        {
            reason="Native uninterrupted bed rest could not be verified.";
            if(job==null || actor==null || !IsHealthRestPoseAtTarget(job,actor,now))return false;
            return NativeNpcRestTreatment.TryComplete(worldStore,job,actor.gameObject,now,
                ()=>IsHealthRestPoseAtTarget(job,actor,now),out reason);
        }

        private void UpdateHealthRestPose(NpcActionJob job,ResidentActor actor,double now)
        {
            if(job==null || actor==null || job.Phase!=NpcActionPhase.Executing)return;
            BedAnchor bed;
            if(!TryValidateHealthRestTarget(job,actor,now,out bed,out _))
            {
                EndAction(job,now,"The assigned bed, health status or room access changed during rest.",false);
                return;
            }
            if(IsHealthRestPoseAtTarget(job,actor,now))return;
            if(actor.IsSleeping)return;
            SleepingArt.Atlas atlas=sleepingArt==null?null:sleepingArt.ForResident(actor.Resident);
            if(atlas!=null && atlas.Texture!=null && atlas.Views!=null && atlas.Views.Length>0)
                actor.Sleep(interior.transform,bed,atlas);
            else
            {
                EndAction(job,now,"A verified sleeping pose became unavailable.",false);
            }
        }

        // Used by the shared arrival loop after the saved action timer has begun.
        // Sleeping alone is not evidence: the actor must occupy this bed's native pose.
        private bool IsHealthRestPoseAtTarget(NpcActionJob job,ResidentActor actor,double now)
        {
            BedAnchor bed;string ignored;
            if(!TryValidateHealthRestTarget(job,actor,now,out bed,out ignored) || !actor.IsSleeping)return false;
            Vector3 mattress=interior.transform.TransformPoint(bed.LocalCenter)+Vector3.up*0.18f;
            return Vector3.Distance(actor.transform.position,mattress)<=0.12f;
        }

        private bool TryDiscoverHealthRestChoice(ResidentActor actor,NpcHealthCareRecord care,double now,
            out HealthRestChoice choice,out string reason)
        {
            choice=null;reason="No safe, accessible bed is available for uninterrupted recovery in this loaded building.";
            if(worldStore==null || actor==null || !actor.gameObject.activeInHierarchy || actor.Resident==null ||
                actor.Resident.Id!=care.PersonId || !actor.Resident.IsAlive || actor.Resident.IsQuestProtected ||
                actor.Resident.AgeGroup!=ResidentAgeGroup.Adult || !interior || indoorGeometry==null || !directory ||
                population==null || population.SettlementId<0 || !actor.transform.IsChildOf(interior.transform) ||
                actor.IsConversing || IsAlarmed(actor.Resident.Id) || worldStore.IsResidentAway(actor.Resident.Id,now) ||
                !NativeRestStatusSafe(actor))return false;
            SleepingArt.Atlas sleepAtlas;
            try {sleepAtlas=sleepingArt==null?null:sleepingArt.ForResident(actor.Resident);}
            catch(Exception error){reason="Sleeping art could not be loaded for this resident: "+error.Message;return false;}
            if(sleepAtlas==null || sleepAtlas.Texture==null || sleepAtlas.Views==null || sleepAtlas.Views.Length==0)
            {reason="A verified sleeping pose is unavailable for this resident.";return false;}
            NpcVitalsView vitals=worldStore.ReadNpcVitals(actor.Resident.Id,HealthArchetype(worldStore,actor.Resident.Id));
            if(vitals==null || !vitals.IsAlive || !vitals.IsPersisted || vitals.CurrentHealth<=0 ||
                vitals.CurrentHealth>=vitals.MaximumHealth)return false;
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            var binding=actor.GetComponent<NativeNpcVitalsBinding>();
            if(!body || body.Entity==null || !binding)return false;
            int rate=NativeNpcRestTreatment.RecoveryRatePerGameHour(body.Entity);
            if(rate<1)return false;
            int missing=vitals.MaximumHealth-vitals.CurrentHealth;
            int nights=NpcHealthRestQuoteMath.NightsFor(missing,rate);
            if(nights<1 || nights>NpcHealthRestQuoteMath.MaximumNights)return false;
            double duration=NpcHealthRestQuoteMath.NextRestDurationSeconds(missing,rate);
            int gain=NpcHealthRestQuoteMath.ExpectedGain(missing,rate,duration);
            if(gain<1 || duration>43200 || now<0 || double.IsNaN(now) || double.IsInfinity(now))return false;

            int town=population.SettlementId,building=interior.EntryDoor.buildingKey;
            List<BedAnchor> beds=HealthRestBedAnchors();
            if(beds.Count==0)return false;
            BuildingSummary summary;bool isInn=directory.GetBuildingSummary(building,out summary) &&
                summary.BuildingType==DFLocation.BuildingTypes.Tavern;
            Dictionary<string,LodgingRoom> roomByBed=null;
            Dictionary<int,string> markerRooms=null;List<LodgingRoom> innRooms=null;
            if(isInn)
            {
                try
                {
                    Dictionary<int,string> occupants,bedroomOccupants;
                    innRooms=TavernRoomSurvey.Read(interior,out markerRooms,out occupants,out bedroomOccupants);
                    var rentable=new HashSet<string>(markerRooms.Values,StringComparer.Ordinal);
                    foreach(var room in innRooms)room.NonRentable=!rentable.Contains(room.Id);
                    worldStore.RegisterLodging(town,building,innRooms);
                    roomByBed=new Dictionary<string,LodgingRoom>(StringComparer.Ordinal);
                    foreach(var room in innRooms)
                        foreach(string bedId in room.Beds)
                            if(!roomByBed.ContainsKey(bedId))roomByBed.Add(bedId,room);
                }
                catch(Exception error)
                {reason="The loaded Inn room survey could not verify rentable beds: "+error.Message;return false;}
            }
            var homeAssignment=OwnHomeBed(actor.Resident,building);
            var assignedInnBed=isInn?OwnAssignedInnBed(actor.Resident,town,building,roomByBed):null;
            NpcHealthLodgingReservation reserved=worldStore.NpcHealthRestLodging(actor.Resident.Id,care.Id,now);
            string reservedBed=null,reservedRoom=null;
            if(reserved!=null && reserved.SettlementId==town && reserved.BuildingKey==building)
            {reservedBed=reserved.BedId;reservedRoom=reserved.RoomId;}

            // Keep the resident's exact assigned bed ahead of a rented room.
            foreach(var bed in beds)
            {
                string bedId="bed:"+bed.RecordIndex.ToString(CultureInfo.InvariantCulture);
                bool ownHome=homeAssignment!=null && homeAssignment.BedRecordIndex==bed.RecordIndex && homeAssignment.SharingCount==1;
                bool ownInn=assignedInnBed!=null && assignedInnBed==bedId;
                bool prepaid=reservedBed==bedId && roomByBed!=null && roomByBed.ContainsKey(bedId) &&
                    roomByBed[bedId].Id==reservedRoom;
                if(!ownHome && !ownInn && !prepaid)continue;
                if(ownInn && roomByBed!=null && (!roomByBed.ContainsKey(bedId) || roomByBed[bedId].NonRentable))continue;
                double routeSeconds;
                if(!TryHealthRestStanding(actor,bed,out Vector3 targetWorld,out routeSeconds))continue;
                double expectedSeconds=duration+routeSeconds;
                if(expectedSeconds>NpcHealthCareChoice.MaximumCandidateTimeSeconds-600d)continue;
                string roomId=prepaid?reservedRoom:ownInn?roomByBed[bedId].Id:null;
                if(prepaid && (reserved==null || reserved.EndsAt<now+expectedSeconds))continue;
                choice=CreateHealthRestChoice(actor,care,now,vitals,rate,missing,nights,duration,gain,
                    bed,bedId,roomId,town,building,prepaid,prepaid?reserved.Price:0,0,targetWorld,prepaid,routeSeconds);
                reason=null;return true;
            }

            if(!isInn || roomByBed==null)return false;
            if(reserved!=null)
            {
                // A valid prepaid room is reserved for this entire episode and is
                // reused for later rest actions without another debit.
                return false;
            }
            NativeNpcInventoryOwner owner=actor.GetComponent<NativeNpcInventoryOwner>();
            if(!owner || owner.Binding==null || !NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason))
            {reason="The resident's NPC wallet is unavailable for an Inn reservation.";return false;}
            NpcInventoryState inventory=worldStore.ReadNpcInventory(actor.Resident.Id);
            if(inventory==null || inventory.Revision!=owner.Binding.Revision)return false;
            if(GameManager.Instance==null || !GameManager.Instance.PlayerEnterExit ||
                !GameManager.Instance.PlayerEnterExit.IsPlayerInside ||
                GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.buildingKey!=building)return false;
            int nativeRoomCost=FormulaHelper.CalculateRoomCost(nights);
            if(nativeRoomCost<0)return false;
            int price=0;int quality=GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.quality;
            if(nativeRoomCost>0 && !NpcShopCommerce.TryPriceForBuyer(worldStore,actor.Resident.Id,
                nativeRoomCost,1,quality,false,out price))return false;
            if(inventory.WalletGold<price)
            {reason="The resident cannot afford the full quoted Inn stay.";return false;}
            double stayEnds=now+nights*86400d;
            if(double.IsNaN(stayEnds) || double.IsInfinity(stayEnds) || stayEnds<=now)return false;
            string availableRoom=worldStore.AvailableLodgingRoom(town,building,1,now,stayEnds);
            if(string.IsNullOrWhiteSpace(availableRoom))
            {reason="Every rentable room in this Inn is occupied or already reserved for the required nights.";return false;}
            LodgingRoom selectedRoom=innRooms.Find(room=>room.Id==availableRoom && !room.NonRentable);
            if(selectedRoom==null)return false;
            foreach(string bedId in selectedRoom.Beds)
            {
                if(!roomByBed.ContainsKey(bedId))continue;
                BedAnchor bed=beds.Find(value=>"bed:"+value.RecordIndex.ToString(CultureInfo.InvariantCulture)==bedId);
                Vector3 targetWorld;
                double routeSeconds;
                if(bed==null || !TryHealthRestStanding(actor,bed,out targetWorld,out routeSeconds))continue;
                double expectedSeconds=duration+routeSeconds;
                if(expectedSeconds>NpcHealthCareChoice.MaximumCandidateTimeSeconds-600d)continue;
                choice=CreateHealthRestChoice(actor,care,now,vitals,rate,missing,nights,duration,gain,
                    bed,bedId,selectedRoom.Id,town,building,true,price,inventory.Revision,targetWorld,false,routeSeconds);
                reason=null;return true;
            }
            reason="The exact available Inn bedroom has no reachable surveyed bed.";return false;
        }

        private HealthRestChoice CreateHealthRestChoice(ResidentActor actor,NpcHealthCareRecord care,double now,
            NpcVitalsView vitals,int rate,int missing,int nights,double duration,int gain,BedAnchor bed,string bedId,
            string roomId,int town,int building,bool paid,int price,long inventoryRevision,Vector3 targetWorld,bool prepaid,
            double routeSeconds)
        {
            NpcActionTarget target=ActionPosition(targetWorld);
            target.ContainerId=bedId;
            return new HealthRestChoice {ResidentId=actor.Resident.Id,CareEpisodeId=care.Id,BedId=bedId,RoomId=roomId,
                SettlementId=town,BuildingKey=building,BedRecordIndex=bed.RecordIndex,Nights=prepaid?0:nights,
                Price=prepaid?0:price,RecoveryRate=rate,ExpectedGain=gain,PaidRoom=paid,PrepaidRoom=prepaid,
                AssignedInnBed=roomId!=null&&!paid,InventoryRevision=inventoryRevision,
                VitalsRevision=vitals.Revision,StartsAt=now,DurationSeconds=duration,
                ExpectedSeconds=duration+routeSeconds,Target=target};
        }

        private List<BedAnchor> HealthRestBedAnchors()
        {
            if(indoorBeds.Count==0)
                foreach(BedAnchor bed in BedGeometry.Read(interior))indoorBeds[bed.RecordIndex]=bed;
            var result=new List<BedAnchor>(indoorBeds.Values);
            result.Sort((first,second)=>first.RecordIndex.CompareTo(second.RecordIndex));
            return result;
        }

        private BedAssignment OwnHomeBed(Resident resident,int building)
        {
            if(resident==null || resident.HomeKey!=building || bedClaims==null)return null;
            BedAssignment assignment;
            return bedClaims.TryGetValue(resident.Id,out assignment) && assignment.SharingCount==1 &&
                assignment.BedRecordIndex>=0 && indoorBeds.ContainsKey(assignment.BedRecordIndex)?assignment:null;
        }

        private string OwnAssignedInnBed(Resident resident,int town,int building,Dictionary<string,LodgingRoom> rooms)
        {
            if(resident==null || rooms==null)return null;
            NativePerson native=worldStore.NativePersonInSettlement(town,resident.Id);
            if(native==null || !native.IsAlive || native.WorkKey!=building)return null;
            string bedId=worldStore.NativeLodgingBed(resident.Id);
            if(string.IsNullOrWhiteSpace(bedId) || worldStore.NativeGuestBedroom(resident.Id)==null ||
                !rooms.ContainsKey(bedId) || rooms[bedId].NonRentable)return null;
            return bedId;
        }

        private bool TryHealthRestStanding(ResidentActor actor,BedAnchor bed,out Vector3 targetWorld,
            out double routeSeconds)
        {
            targetWorld=Vector3.zero;routeSeconds=0;
            if(actor.IsSleeping)
            {
                Vector3 mattress=interior.transform.TransformPoint(bed.LocalCenter)+Vector3.up*0.18f;
                if(Vector3.Distance(actor.transform.position,mattress)>0.12f)return false;
                targetWorld=actor.transform.position-Vector3.up;
                return true;
            }
            Vector3 standing;
            if(!indoorGeometry.StandBesideBed(bed,out standing,true))return false;
            Vector3 from=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            Vector3 to=interior.transform.InverseTransformPoint(standing);
            List<GuardPoint3> route=indoorGeometry.RouteBetween(from,to);
            if(route.Count<2)return false;
            double length=0;
            for(int index=1;index<route.Count;index++)
            {
                double dx=route[index].X-route[index-1].X;
                double dy=route[index].Y-route[index-1].Y;
                double dz=route[index].Z-route[index-1].Z;
                length+=Math.Sqrt(dx*dx+dy*dy+dz*dz);
            }
            double timeScale=DaggerfallUnity.Instance==null || DaggerfallUnity.Instance.WorldTime==null?
                double.NaN:DaggerfallUnity.Instance.WorldTime.TimeScale;
            if(actor.WalkingSpeed<=0 || timeScale<=0 || double.IsNaN(timeScale) || double.IsInfinity(timeScale))return false;
            routeSeconds=length*timeScale/actor.WalkingSpeed;
            if(double.IsNaN(routeSeconds) || double.IsInfinity(routeSeconds) || routeSeconds<0)return false;
            targetWorld=standing;return true;
        }

        private bool TaskUsesHealthRestChoice(NpcTaskPlan task,HealthRestChoice choice)
        {
            return task!=null && choice!=null && task.ResidentId==choice.ResidentId && task.Steps!=null &&
                task.Steps.Count==1 && task.Steps[0]!=null && task.Steps[0].Kind==NpcActionKind.RestToHeal &&
                task.Steps[0].DurationSeconds==choice.DurationSeconds && TargetMatchesHealthRest(task.Steps[0].Target,choice.Target);
        }

        private static bool TargetMatchesHealthRest(NpcActionTarget first,NpcActionTarget second)
        {
            return first!=null && second!=null && first.Kind==NpcActionTargetKind.IndoorPoint &&
                second.Kind==NpcActionTargetKind.IndoorPoint && first.SettlementId==second.SettlementId &&
                first.BuildingKey==second.BuildingKey && first.ContainerId==second.ContainerId &&
                first.X==second.X && first.Y==second.Y && first.Z==second.Z && first.ItemId==null &&
                first.PersonId==null && first.Quantity==1 && first.QuotedGold==0;
        }

        private bool TryValidateHealthRestTarget(NpcActionJob job,ResidentActor actor,double now,
            out BedAnchor bed,out string reason)
        {
            bed=null;reason="The exact bed access could not be reverified.";
            if(worldStore==null || !actor || !actor.gameObject.activeInHierarchy || actor.Resident==null ||
                !interior || indoorGeometry==null || job==null || job.Target==null || job.ResidentId!=actor.Resident.Id ||
                job.Target.Kind!=NpcActionTargetKind.IndoorPoint || !ActionSpaceMatches(job.Target) ||
                !actor.transform.IsChildOf(interior.transform) || actor.IsConversing || IsAlarmed(actor.Resident.Id) ||
                worldStore.IsResidentAway(actor.Resident.Id,now) || !NativeRestStatusSafe(actor))return false;
            int recordIndex;
            if(!TryBedRecord(job.Target.ContainerId,out recordIndex))return false;
            BedAnchor exact=HealthRestBedAnchors().Find(value=>value.RecordIndex==recordIndex);
            if(exact==null)return false;
            int town=population.SettlementId,building=interior.EntryDoor.buildingKey;
            bool ownedHome=OwnHomeBed(actor.Resident,building)!=null &&
                OwnHomeBed(actor.Resident,building).BedRecordIndex==recordIndex;
            BuildingSummary summary;
            bool isInn=directory.GetBuildingSummary(building,out summary) && summary.BuildingType==DFLocation.BuildingTypes.Tavern;
            bool assignedInn=false;
            if(isInn)
            {
                Dictionary<int,string> markerRooms,occupants,bedrooms;
                List<LodgingRoom> rooms=TavernRoomSurvey.Read(interior,out markerRooms,out occupants,out bedrooms);
                var map=new Dictionary<string,LodgingRoom>(StringComparer.Ordinal);
                var rentable=new HashSet<string>(markerRooms.Values,StringComparer.Ordinal);
                foreach(var room in rooms)
                {
                    room.NonRentable=!rentable.Contains(room.Id);
                    foreach(string bedId in room.Beds)map[bedId]=room;
                }
                worldStore.RegisterLodging(town,building,rooms);
                string innBed=OwnAssignedInnBed(actor.Resident,town,building,map);
                assignedInn=innBed==job.Target.ContainerId;
            }
            NpcHealthCareRecord care=worldStore.ReadNpcHealthCare(actor.Resident.Id);
            if(!ownedHome && !assignedInn)
            {
                var reservation=care==null?null:worldStore.NpcHealthRestLodging(actor.Resident.Id,care.Id,now);
                if(reservation==null || reservation.SettlementId!=town || reservation.BuildingKey!=building ||
                    reservation.BedId!=job.Target.ContainerId || reservation.EndsAt<job.FinishAt || !isInn)return false;
                var task=worldStore.ReadNpcTask(reservation.TaskId);
                // The original accepted task remains the durable authority for
                // a prepaid room used by later attempts in this care episode.
                if(task==null || task.ResidentId!=actor.Resident.Id || task.Steps.Count!=1 ||
                    task.Steps[0].Target.ContainerId!=reservation.BedId)return false;
            }
            Vector3 expected=ActionWorldPoint(job.Target);
            if(actor.IsSleeping)
            {
                Vector3 mattress=interior.transform.TransformPoint(exact.LocalCenter)+Vector3.up*0.18f;
                if(Vector3.Distance(actor.transform.position,mattress)>0.12f)return false;
            }
            else
            {
                Vector3 standing;
                if(!indoorGeometry.StandBesideBed(exact,out standing,true) ||
                    Vector3.Distance(expected,standing)>0.05f ||
                    Vector3.Distance(actor.transform.position-Vector3.up,expected)>0.65f)return false;
            }
            bed=exact;reason=null;return true;
        }

        private bool NativeRestStatusSafe(ResidentActor actor)
        {
            if(!actor || actor.Resident==null || !actor.Resident.IsAlive || actor.Resident.IsQuestProtected ||
                actor.Resident.AgeGroup!=ResidentAgeGroup.Adult)return false;
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            var effects=actor.GetComponent<EntityEffectManager>();
            return body && body.Entity!=null && body.Entity.CurrentHealth>0 && effects &&
                effects.PoisonCount==0 && effects.DiseaseCount==0;
        }

        private static bool TryBedRecord(string value,out int record)
        {
            record=-1;
            return value!=null && value.StartsWith("bed:",StringComparison.Ordinal) &&
                int.TryParse(value.Substring(4),NumberStyles.None,CultureInfo.InvariantCulture,out record) && record>=0;
        }
    }
}
