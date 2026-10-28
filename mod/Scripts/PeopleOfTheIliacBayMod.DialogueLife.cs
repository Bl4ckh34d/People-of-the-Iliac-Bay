using System;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private string ManagedConversationPlan(WorldStateStore activeStore,Resident resident,double now,int settlement,ActivitySnapshot? observed)
        {
            string travelerPlan=ManagedTravelerPlanContext(activeStore,resident.Id,now,settlement);
            if(!string.IsNullOrEmpty(travelerPlan))return travelerPlan;
            string plan;
                ResidentTrip trip = activeStore.GetResidentDialogueTrip(resident.Id);
                string travel = trip != null ? ConversationLifeContext.TripStatus(trip, now,
                    activeStore.ResidentTripAt(resident.Id, now), ConversationSettlementName,
                    key => trip.DestinationSettlement == settlement ? ConversationBuildingName(key) : string.Empty,observed)
                    : null;
                if (travel != null) plan = travel;
                else
                {
                    int homeSettlement = trip != null ? trip.HomeSettlement : settlement;
                    ResidentRoutine routine = activeStore.GetResidentDialogueRoutine(homeSettlement, resident.Id);
                    if (homeSettlement == settlement && streets && streets.Ready)
                    {
                        double shift = 0;
                        if (observed.HasValue)
                        {
                            int observedTown, observedBuilding;
                            double observedAt, endsAt, plannedEndAt, resumeUntil;
                            if (activeStore.TryObservedVisitMetadata(resident.Id, out observedTown, out observedBuilding,
                                out observedAt, out endsAt, out plannedEndAt, out resumeUntil) &&
                                observedTown == settlement && now >= observedAt && now < resumeUntil + endsAt - plannedEndAt)
                                shift = endsAt - plannedEndAt;
                            plan = streets.AppointmentConversationPlan(resident.Id,now,shift,ConversationBuildingName,observed) ??
                                ConversationLifeContext.RoutineStatus(routine, now,
                                    observed.Value, shift, ConversationBuildingName);
                        }
                        else
                            // AppointmentConversationPlan falls back to StreetPopulation.GetState
                            // when observation is null. Do not turn that stored state into an
                            // observed-current-location claim while the NPC scene is unknown.
                            plan = ConversationLifeContext.Routine(routine,
                                key => homeSettlement == settlement ? ConversationBuildingName(key) : string.Empty);
                    }
                    else plan = ConversationLifeContext.Routine(routine,
                        key => homeSettlement == settlement ? ConversationBuildingName(key) : string.Empty);
                }
            return plan;
        }

        private string CurrentConversationLifeContext(string npcName, int disposition)
        {
            var game = GameManager.Instance;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            int day = (int)Math.Floor(now / 86400);
            int settlement = game.PlayerGPS.HasCurrentLocation ? game.PlayerGPS.CurrentLocation.MapTableData.MapId : -1;
            bool staticSpeaker = game.TalkManager.CurrentNPCType == TalkManager.NPCType.Static && game.TalkManager.StaticNPC;
            var mobile = game.TalkManager.CurrentNPCType==TalkManager.NPCType.Mobile ? game.TalkManager.MobileNPC : null;
            bool currentManagedSpeaker=ConversationVitalsContext.MatchesMobileSpeaker(
                game.TalkManager.CurrentNPCType==TalkManager.NPCType.Mobile,conversationActor?conversationActor.Person:null,mobile);
            Resident resident = currentManagedSpeaker && conversationActor && conversationActor.Resident != null && conversationActor.Resident.IsAlive
                ? conversationActor.Resident : null;
            NativePerson native = resident == null && store != null && settlement >= 0
                ? FindConversationNative(settlement) : null;
            int contextTown,contextBuilding;BuildingSummary contextSummary;
            bool keeper=native!=null && TryConversationBuilding(out contextTown,out contextBuilding,out contextSummary) &&
                native.WorkKey==contextBuilding && NativeBusinessServices.OffersKeeperService(native.FactionId,contextSummary.BuildingType);
            string fallbackIdentity = !string.IsNullOrEmpty(conversationPersonId) ? conversationPersonId :
                npcName + (mobile ? ":" + mobile.GetInstanceID() : string.Empty);
            uint seed = resident != null ? resident.AppearanceSeed : native != null
                ? native.NativeLayoutKey!=null?ConversationNameSeed(native.Id,native.SettlementId):
                    StableSeed.Combine(0, (uint)native.SettlementId, (uint)native.WorkKey, unchecked((uint)native.NativeHash))
                : ConversationNameSeed(fallbackIdentity, settlement);
            bool grieving = resident != null && store != null && store.BereavementFor(resident, now) != null;
            bool alarmed = resident != null && streets && streets.IsAlarmed(resident.Id);
            bool outdoors;
            string condition;
            bool npcLocationKnown = TryReadCurrentNpcOutdoorWeather(out outdoors, out condition);
            outdoors = npcLocationKnown && outdoors;
            ActivitySnapshot? observed=null;
            if(resident!=null && streets && streets.Ready && npcLocationKnown && !game.PlayerEnterExit.IsPlayerInsideDungeon)
                observed=ConversationLifeContext.ObservedActivity(streets.GetState(resident.Id,now),outdoors,
                    game.PlayerEnterExit.IsPlayerInsideBuilding ? game.PlayerEnterExit.BuildingDiscoveryData.buildingKey : -1);
            string activity = resident != null && streets
                ? (observed.HasValue ? observed.Value.Activity.ToString().ToLowerInvariant() : string.Empty)
                : keeper && game.PlayerEnterExit.IsPlayerInsideBuilding &&
                    game.PlayerEnterExit.BuildingDiscoveryData.buildingKey == native.WorkKey ? "work" : string.Empty;
            string plan = "No tracked daily itinerary is available.";
            if (resident != null && store != null)
            {
                plan = ManagedConversationPlan(store,resident,now,settlement,observed);
            }
            else if (native != null && keeper)
                plan = ConversationLifeContext.NativeDuty(ConversationBuildingName(native.WorkKey),
                    ConversationBuildingName(native.HomeKey),
                    game.PlayerEnterExit.IsPlayerInsideBuilding &&
                    game.PlayerEnterExit.BuildingDiscoveryData.buildingKey == native.WorkKey);
            else if(native!=null && staticSpeaker)
            {
                var verifiedRole=CaptureVerifiedStaticRole(game.TalkManager.StaticNPC);
                plan=ConversationLifeContext.NativeStaticDuty(verifiedRole.HasValue?
                    StaticNpcRoleProjection.RoleText(verifiedRole.Value):null);
            }
            else if(staticSpeaker)
            {
                var verifiedRole = CaptureVerifiedStaticRole(game.TalkManager.StaticNPC);
                string roleText = verifiedRole.HasValue ? StaticNpcRoleProjection.RoleText(verifiedRole.Value) : null;
                plan = ConversationLifeContext.NativeStaticDuty(roleText);
            }
            string outdoorDuty=ExteriorStaticNpcNightContext(staticSpeaker?game.TalkManager.StaticNPC:null,now);
            if(!string.IsNullOrEmpty(outdoorDuty))plan+=" "+outdoorDuty;
            var care=resident==null || store==null?null:store.ReadNpcHealthCare(resident.Id);
            if(care!=null && !alarmed && care.Phase!=NpcHealthCarePhase.Recovered && now>=care.StartedAt)
            {
                activity="recovery";
                plan="Ordinary work and visits are interrupted for recovery from observed injury. "+
                    "Only confirmed treatment restores health; the cause of injury is not identified by this record.";
                if(!string.IsNullOrEmpty(care.LastFailure))plan+=" Last treatment constraint: "+care.LastFailure;
            }
            var medical=resident==null || store==null?null:store.ReadNpcTreatmentVisit(resident.Id);
            if(medical!=null && !alarmed)
            {
                activity="recovery";
                plan=medical.Returning?
                    "Your medical visit has ended and you are rejoining your ordinary routine. Your current physical condition remains authoritative; a visit alone does not prove a cure.":
                    "You are seeking treatment at "+ConversationBuildingName(medical.ProviderKey)+
                    " for "+(medical.EffectKey==DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects.CurePoison.EffectKey?"poisoning":"disease")+
                    ". Your ordinary visits are interrupted. You have not yet completed treatment.";
            }
            string refuge;
            if(resident!=null && streets && streets.TryGuardRefugeContext(resident.Id,ConversationBuildingName,out refuge))
            {activity="sheltering";plan=refuge;}
            string actionAwareness=resident!=null && streets && streets.Ready?
                streets.ActionAwarenessContext(resident.Id,now,ConversationBuildingName):string.Empty;
            string sharedActivityContext=CurrentConversationSharedActivityContext(now);
            string vitalsContext=string.Empty;
            if(resident!=null && currentManagedSpeaker && conversationActor && conversationActor.Resident==resident)
                vitalsContext=NativeConversationBodyContext(conversationActor);
            var mood = new ConversationMoodInputs { Disposition = disposition, Hour = (int)(now % 86400 / 3600),
                Grieving = grieving, Alarmed = alarmed, Outdoors = outdoors, Weather = condition,
                Familiar = conversationPersonId != null && store != null && store.ConversationEncounters(conversationPersonId) > 1,
                Activity = activity };
            string haunting=settlement==1291010263 && !game.PlayerEntity.GlobalVars.GetGlobalVar(0) ?
                ConversationLifeContext.Haunting((int)(now%86400/60)) : string.Empty;
            return " " + ConversationLifeContext.Personality(seed) + " " +
                ConversationLifeContext.Mood(seed, day, mood) + " " + plan + " " +
                actionAwareness + " " + sharedActivityContext + " " + vitalsContext + " " +
                "Know the player's name only if they state it in this conversation or it appears in your recorded conversation history; repeated encounters alone do not reveal it. " +
                (outdoors ? "You are physically outside. Planned destinations are not places you have already entered. Say you are heading there, not already inside. " : "")+
                "Do not claim you are leaving now or soon unless your verified current phase or visit ending time supports it. A goodbye does not change your physical schedule. " +
                haunting+ConversationAppointmentContext()+" "+ConversationActionContext()+" "+
                ConversationHomeContext(CurrentDialogueDisposition());
        }

        private static string NativeConversationBodyContext(ResidentActor actor)
        {
            if(!actor || !actor.gameObject.activeInHierarchy)return string.Empty;
            string context=string.Empty;
            var binding=actor.GetComponent<NativeNpcVitalsBinding>();
            int health,maxHealth,magicka,maxMagicka;
            if(binding && binding.TryReadCurrent(out health,out maxHealth,out magicka,out maxMagicka))
                context=ConversationVitalsContext.Format(true,true,health,maxHealth,magicka,maxMagicka);
            var effects=actor.GetComponent<DaggerfallWorkshop.Game.MagicAndEffects.EntityEffectManager>();
            if(effects && effects.PoisonCount==0 && effects.DiseaseCount==0)
                context+=" Current native status: no active poisoning or disease. Neither condition is making you physically ill now. Past mood influences are memories, not ongoing symptoms.";
            if(effects && effects.PoisonCount>0)
                context+=" You are currently poisoned and need an antidote or a cure-poison spell. The responsible person is unknown unless independently witnessed.";
            if(effects && effects.DiseaseCount>0)
                context+=" You currently have a disease and need appropriate medicine, a cure-disease spell or temple treatment.";
            return context;
        }

        private string CurrentConversationSharedActivityContext(double now)
        {
            if(store==null || conversationActor==null || conversationActor.Resident==null)return string.Empty;
            var manager=GameManager.Instance.TalkManager;
            var mobile=manager.CurrentNPCType==TalkManager.NPCType.Mobile?manager.MobileNPC:null;
            var person=conversationActor.Person;
            if(!ConversationVitalsContext.MatchesMobileSpeaker(manager.CurrentNPCType==TalkManager.NPCType.Mobile,person,mobile))
                return string.Empty;
            var resident=conversationActor.Resident;
            if(!resident.IsAlive || resident.IsQuestProtected || resident.Id!=conversationPersonId)return string.Empty;
            return SharedActivityConversationContext.Build(store.ActiveNpcAction(resident.Id),resident.Id,now,
                ConversationPlaceContext());
        }

        private static uint ConversationNameSeed(string name, int settlement)
        {
            uint value = 2166136261u;
            foreach (char letter in name ?? string.Empty)
                unchecked { value = (value ^ letter) * 16777619u; }
            return StableSeed.Combine(value, unchecked((uint)settlement), 0x6469616cu, 1);
        }

        private static string ConversationSettlementName(int mapId)
        {
            if (mapId < 0) return string.Empty;
            int pixel = mapId & 0x000fffff;
            var reader = DaggerfallUnity.Instance.ContentReader;
            DaggerfallWorkshop.Utility.ContentReader.MapSummary summary;
            if (!reader.HasLocation(pixel % 1000, pixel / 1000, out summary) || summary.MapID != mapId)
                return string.Empty;
            DFLocation location;
            return reader.GetLocation(summary.RegionIndex, summary.MapIndex, out location) ? location.Name : string.Empty;
        }

        private static string ConversationBuildingName(int key)
        {
            if (key < 0) return string.Empty;
            var game = GameManager.Instance;
            BuildingSummary summary;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            if (!directory || !directory.GetBuildingSummary(key, out summary)) return string.Empty;
            try { return game.TalkManager.GetBuildingNameForBuildingKey(key); }
            catch (Exception) { return string.Empty; }
        }
    }
}
