using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Controlled disposable setup; movement, corpse removal and inquiry run on production controllers.
    internal static class CorpseResponseNativeProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        internal enum Case { NpcKiller,DeadNpcKiller,StaticWitness,MobileWitness,Unwitnessed }
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            yield return RunCase(host,streets,character,Case.NpcKiller);
            yield return RunCase(host,streets,character,Case.DeadNpcKiller);
            yield return RunCase(host,streets,character,Case.StaticWitness);
            yield return RunCase(host,streets,character,Case.MobileWitness);
            yield return RunCase(host,streets,character,Case.Unwitnessed);
        }
        internal static IEnumerator RunCase(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character,Case mode)
        {
            bool npcKiller=mode==Case.NpcKiller || mode==Case.DeadNpcKiller;
            bool naturalWitness=mode==Case.MobileWitness || npcKiller,staticWitness=mode==Case.StaticWitness;
            var game=GameManager.Instance;var guards=host.GetComponent<GuardPopulation>();
            typeof(PeopleOfTheIliacBayMod).GetField("exteriorGuardsEnabled",Private).SetValue(host,true);
            streets.ReactionsEnabled=true;game.PlayerEntity.GodMode=true;
            if(game.PlayerEnterExit.IsPlayerInside)game.PlayerEnterExit.TransitionExterior(true);
            float deadline=Time.realtimeSinceStartup+120;
            while((game.SaveLoadManager.LoadInProgress || game.PlayerEnterExit.IsPlayerInside || !streets.Ready) && Time.realtimeSinceStartup<deadline)yield return null;
            double now=Now();double noon=Math.Floor(now/86400)*86400+12*3600;
            if(noon<now)noon+=86400;
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(noon-now));
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var population=Field<SettlementPopulation>(streets,"population");
            var store=Field<WorldStateStore>(streets,"worldStore");
            guards.SetTown(directory,game.StreamingWorld.GetCurrentCityNavigation(),population,store);
            deadline=Time.realtimeSinceStartup+120;
            while((!guards.OwnsExterior || guards.Roster==null || guards.Roster.BuildingApproaches.Count==0 || !streets.EntranceSurveyReady) && Time.realtimeSinceStartup<deadline)yield return null;
            Require(guards.OwnsExterior && guards.Roster!=null && guards.Roster.BuildingApproaches.Count>0,"No owned native guard approach ready.");
            var geometry=Field<TownGeometry>(guards,"geometry");
            SavedRoutePoint site=null;List<SavedRoutePoint> siteRoute=null;double shortest=double.MaxValue;
            foreach(var approach in guards.Roster.BuildingApproaches)
            foreach(var route in approach.Routes)
            foreach(var point in route.Points)
            {
                double nearest=double.MaxValue;
                foreach(var guard in guards.Roster.Guards)
                {
                    if(!guard.IsAlive || guard.Duty!=GuardDuty.Posted)continue;
                    var post=guards.Roster.Position(guard.Id,Now());
                    double x=point.X-post.X,z=point.Z-post.Z;nearest=Math.Min(nearest,Math.Sqrt(x*x+z*z));
                }
                if(nearest>25 && nearest<shortest) {site=point;siteRoute=route.Points;shortest=nearest;}
            }
            Require(site!=null && shortest<100,"No surveyed corpse site outside immediate guard sight.");
            DaggerfallStaticDoors innOwner=null;StaticDoor innDoor=default(StaticDoor);
            var inns=directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern);
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            foreach(var door in owner.Doors)
                if(door.doorType==DoorTypes.Building && inns.Exists(b=>b.buildingKey==door.buildingKey))
                {innOwner=owner;innDoor=door;break;}
            Require(innOwner,"No native Inn doorway.");
            Resident victim=null,witness=null;
            var planned=new HashSet<string>();
            foreach(var plan in Field<IEnumerable>(streets,"plans"))
                planned.Add(((Resident)plan.GetType().GetField("Resident",Private|BindingFlags.Public).GetValue(plan)).Id);
            foreach(var home in population.Households)
            foreach(var person in home.Residents)
                if(person.IsAlive && !person.IsQuestProtected && person.AgeGroup==ResidentAgeGroup.Adult && person.NativeIdentity==null && planned.Contains(person.Id))
                {if(victim==null)victim=person;else if(person.HomeKey!=victim.HomeKey) {witness=person;break;}}
            Require(victim!=null && witness!=null,"No two ordinary residents for corpse fixture.");
            var actors=Field<Dictionary<string,ResidentActor>>(streets,"actors");
            bool moving=streets.enabled;streets.enabled=false;
            foreach(var actor in actors.Values)if(actor)actor.gameObject.SetActive(false);
            var ground=geometry.ScenePoint(new RoutePoint(site.X,site.Z));
            var target=StageResident(streets,store,victim,directory,ground);
            target.GetComponent<DaggerfallEntityBehaviour>().Entity.SetHealth(1);target.GetComponent<NativeNpcVitalsBinding>().Capture();
            game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
            CrimeSceneProbe.SetPlayerInvisible(game.PlayerEntity,false);
            game.PlayerMotor.transform.position=ground+Vector3.right*2+Vector3.up;
            ResidentActor observer=null;
            StaticNPC fixedObserver=null;string witnessId=witness.Id;
            if(staticWitness)
            {
                // Use a reachable, visible position far enough away to require
                // physical inquiry. Sparse NavMesh corners need not be nearby.
                foreach(var approach in guards.Roster.BuildingApproaches)
                foreach(var route in approach.Routes)
                foreach(var point in route.Points)
                {
                    if(fixedObserver)break;
                    var destination=geometry.ScenePoint(new RoutePoint(point.X,point.Z));
                    var direction=destination-ground;if(direction.magnitude<6)continue;
                    var spot=ground+direction.normalized*Mathf.Min(8,direction.magnitude);
                    var local=directory.transform.InverseTransformPoint(spot);
                    var delta=spot-ground;game.PlayerMotor.transform.position=ground+delta.normalized*1.5f+Vector3.up;
                    if(!CivilianResponses.Sees(spot+Vector3.up,target.transform.position,20) ||
                        !CivilianResponses.Sees(spot+Vector3.up,game.PlayerMotor.transform.position,20) ||
                        geometry.RouteBetweenPoints(site,new SavedRoutePoint{X=local.x,Z=local.z}).Count<2)continue;
                    // A stationary witness need not already see a guard: the
                    // investigator must find and approach this person later.
                    var go=GameObjectHelper.CreateDaggerfallBillboardGameObject(182,0,directory.transform);
                    go.name="Controlled native static murder witness";go.transform.position=spot+Vector3.up;
                    fixedObserver=go.AddComponent<StaticNPC>();
                    typeof(StaticNPC).GetField("npcData",Private).SetValue(fixedObserver,new StaticNPC.NPCData {
                        hash=923451,nameSeed=923451,mapID=directory.MapID,buildingKey=-1,context=StaticNPC.Context.Custom,
                        race=Races.Breton,gender=Genders.Male,nameBank=NameHelper.BankTypes.Breton,
                        billboardArchiveIndex=182,billboardRecordIndex=0});
                }
                Require(fixedObserver,"No reachable visible static witness position at inquiry distance.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIC_WITNESS_STAGE: body="+ground+"; witness="+fixedObserver.transform.position);
                // This test adds a new native person after the ordinary scene
                // survey. Let its existing 30-second refresh discover that body.
                yield return new WaitForSecondsRealtime(31);
            }
            if(naturalWitness)
            {
                int node=siteRoute.FindIndex(p=>Math.Abs(p.X-site.X)+Math.Abs(p.Z-site.Z)<0.001);
                int candidates=0,visible=0;string lengths="";
                for(int offset=1;offset<=8 && !observer;offset++)
                foreach(int neighbor in new[] {node-offset,node+offset})
                {
                    if(neighbor<0 || neighbor>=siteRoute.Count)continue;
                    var other=geometry.ScenePoint(new RoutePoint(siteRoute[neighbor].X,siteRoute[neighbor].Z));
                    var delta=other-ground;float length=delta.magnitude;lengths+=length.ToString("F2")+",";
                    if(length<2)continue;
                    // NavMesh corners can be tens of metres apart. Place the
                    // witness along the real segment, not only at its corners.
                    var spot=ground+delta.normalized*Mathf.Min(4,length);
                    var local=directory.transform.InverseTransformPoint(spot);
                    if(geometry.RouteBetweenPoints(site,new SavedRoutePoint{X=local.x,Z=local.z}).Count<2)continue;
                    candidates++;
                    game.PlayerMotor.transform.position=ground+delta.normalized*1.5f+Vector3.up;
                    if(!CivilianResponses.Sees(spot+Vector3.up,target.transform.position,20) ||
                        !CivilianResponses.Sees(spot+Vector3.up,game.PlayerMotor.transform.position,20))continue;
                    visible++;
                    string receiver;Vector3 contact;
                    if(!guards.FindBodyReportGuard(spot+Vector3.up,false,out receiver,out contact))continue;
                    observer=StageResident(streets,store,witness,directory,spot);break;
                }
                if(!observer)
                {
                    int available=0;string routes="";
                    foreach(var guard in guards.Roster.Guards)
                    {
                        Vector3 point;if(!guards.BodyReportGuardPosition(guard.Id,out point))continue;
                        available++;var post=guards.Roster.Position(guard.Id,Now());
                        routes+=guard.Id+":"+geometry.RouteBetweenPoints(site,post).Count+";";
                    }
                    throw new InvalidOperationException("No surveyed witness position both sees the attack and has a reachable guard; owns="+guards.OwnsExterior+
                        "; available="+available+"; site="+site.X+","+site.Z+"; candidates="+candidates+"; visible="+visible+
                        "; lengths="+lengths+"; routes="+routes);
                }
            }
            DaggerfallEntityBehaviour killer=null;
            // Actual eyewitnesses must face a visible attack. Concealment breaking
            // is a separate native-combat check, not evidence of prior sight.
            if(observer)observer.transform.rotation=Quaternion.LookRotation(target.transform.position-observer.transform.position);
            if(fixedObserver)fixedObserver.transform.rotation=Quaternion.LookRotation(target.transform.position-fixedObserver.transform.position);
            Physics.SyncTransforms();
            if(npcKiller)
            {
                killer=NpcMurderNativeProbe.Spawn(directory,game.PlayerMotor.transform.position);
                yield return null;NpcMurderNativeProbe.Attack(killer,target,mode==Case.DeadNpcKiller,false);
                NpcMurderNativeProbe.Conceal(killer,true);
            }
            else for(int hit=0;hit<20 && victim.IsAlive;hit++)
                game.WeaponManager.WeaponDamage(null,false,false,target.transform,target.transform.position,Vector3.forward);
            Require(!victim.IsAlive,"Native weapon did not kill the controlled injured resident.");
            var alarm=store.OutdoorCorpses(population.SettlementId).Find(a=>a.VictimId==victim.Id);
            Require(alarm!=null && alarm.Corpse!=null && alarm.PlayerAttributed!=npcKiller &&
                (!npcKiller || alarm.KillerThreatId=="native:"+killer.GetComponent<DaggerfallEnemy>().LoadID),
                "Native death did not preserve actual killer attribution.");
            if(staticWitness)
            {
                var observed=alarm.People.Find(p=>p.StaticWitness && p.DirectWitness);
                Require(observed!=null,"Native weapon observation did not identify the static eyewitness.");witnessId=observed.ResidentId;
                Require(!alarm.AccusationIssued && game.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None,
                    "Static eyewitness observation accused player before physical contact.");
            }
            else if(naturalWitness)
            {
                var observed=alarm.People.Find(p=>p.ResidentId==witness.Id);
                Require(observed!=null && observed.DirectWitness,"Native weapon observation did not identify the actual eyewitness.");
                Require(!alarm.AccusationIssued && game.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None,
                    "Civilian observation accused the player before the witness reached a guard.");
                observed.Reaction=CivilianReaction.Flee;
            }
            else Require(!alarm.People.Exists(p=>p.DirectWitness),"Controlled unwitnessed fixture has a direct witness.");
            if(mode==Case.DeadNpcKiller)
            {
                // A retained, inactive defense report must not hide this murder's
                // actual investigator when resolving the already-dead suspect.
                guards.Roster.Reports.Insert(0,new GuardReport {Id="defense:retained:"+alarm.KillerThreatId,
                    ThreatId=alarm.KillerThreatId});
                killer.DamageHealthFromSource((DaggerfallEntityBehaviour)null,killer.Entity.CurrentHealth,false,killer.transform.position);
            }
            streets.enabled=moving;
            CrimeSceneProbe.SetPlayerInvisible(game.PlayerEntity,true);
            yield return new WaitForSecondsRealtime(0.6f);
            var view=FindCorpse(alarm.Id);Require(view,"Actual corpse view missing before departure.");
            if(!naturalWitness)
            {
                // A stationary witness outside every guard's sight cannot
                // remotely dispatch one. Use the existing real departure
                // discovery path, then require physical cleanup and inquiry.
                TownSmokeHarness.EnterInterior(innOwner.transform,innDoor);
                yield return new WaitForSecondsRealtime(1);DaggerfallUI.Instance.PopToHUD();
                Require(game.PlayerEnterExit.IsPlayerInside,"Native Inn entry failed.");
                game.PlayerEnterExit.TransitionExterior(true);
                yield return new WaitForSecondsRealtime(1);
            }
            game.PlayerMotor.transform.position=ground+Vector3.back*6+Vector3.up;
            Require(!alarm.Corpse.Cleared,"Brief Inn visit removed the corpse without physical arrival.");
            string reportId="outdoor:"+alarm.Id;GuardReport report=null;
            bool walked=false,arrived=false;Vector3 previous=Vector3.zero;string followed=null;float largestStep=0;
            Vector3 observerStart=observer?observer.transform.position:Vector3.zero;bool witnessWalked=false;
            float talkStarted=-1;bool talkVerified=false;Vector3 talkGuardStart=Vector3.zero,talkWitnessStart=Vector3.zero;
            float reportDiagnosticAt=Time.realtimeSinceStartup+5;
            deadline=Time.realtimeSinceStartup+(naturalWitness?90:180);
            while(!alarm.Corpse.Cleared && Time.realtimeSinceStartup<deadline)
            {
                report=guards.Roster.Reports.Find(r=>r.Id==reportId);
                if(naturalWitness && observer && observer.gameObject.activeInHierarchy)
                {
                    witnessWalked|=Vector3.Distance(observerStart,observer.transform.position)>2;
                    if(report==null)game.PlayerMotor.transform.position=observer.transform.position+Vector3.back*6+Vector3.up;
                    var motions=Field<IDictionary>(streets,"bodyReportMotion");var motion=motions.Contains(witness.Id)?motions[witness.Id]:null;
                    var talking=motion==null?null:motion.GetType().GetField("TalkingGuard").GetValue(motion) as GuardActor;
                    if(talking && Time.time<Field<float>(talking,"reportConversationUntil"))
                    {
                        if(talkStarted<0){talkStarted=Time.time;talkGuardStart=talking.transform.position;talkWitnessStart=observer.transform.position;}
                        var facing=observer.transform.position-talking.transform.position;facing.y=0;
                        Require(Vector3.Distance(talkGuardStart,talking.transform.position)<0.2f &&
                            Vector3.Distance(talkWitnessStart,observer.transform.position)<0.2f,"Report partners moved during their conversation.");
                        Require(Vector3.Dot(talking.transform.forward,facing.normalized)>0.9f &&
                            Vector3.Dot(observer.transform.forward,-facing.normalized)>0.9f,"Report partners did not face each other.");
                    }
                    else if(talkStarted>=0 && !talkVerified)
                    {
                        float seconds=Time.time-talkStarted;
                        Require(seconds>=1.8f && seconds<=5.3f,"Report conversation did not last 2-5 real seconds: "+seconds);
                        talkVerified=true;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REPORT_CONVERSATION_NATIVE_PASS: seconds="+seconds+"; bothStill=true; mutualFacing=true; departureAfterTalk=true");
                    }
                }
                if(naturalWitness && Time.realtimeSinceStartup>=reportDiagnosticAt)
                {
                    reportDiagnosticAt=Time.realtimeSinceStartup+10;
                    var person=alarm.People.Find(p=>p.ResidentId==witness.Id);var source=person==null?null:person.OutdoorReport;
                    var live=streets.FindActor(witness.Id);var motions=Field<IDictionary>(streets,"bodyReportMotion");
                    var motion=motions.Contains(witness.Id)?motions[witness.Id]:null;
                    var path=motion==null?null:(ICollection)motion.GetType().GetField("Route").GetValue(motion);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WITNESS_ROUTE_STAGE: witness="+witness.Id+"; body="+(live?live.transform.position.ToString():"none")+
                        "; active="+(live&&live.gameObject.activeInHierarchy)+"; sameActor="+(live==observer)+
                        "; blocked="+Field<HashSet<string>>(streets,"pendingHouseholdInitialization").Contains(witness.Id)+
                        "; report="+(source==null?"none":source.GuardId+"/deferred="+source.Deferred+"/delivered="+source.Delivered+"/complete="+source.Complete)+
                        "; route="+(path==null?-1:path.Count)+"; invisible="+game.PlayerEntity.IsInvisible);
                }
                if(report!=null)
                    foreach(var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                    {
                        if(!report.GuardIds.Contains(actor.GuardId) || !actor.gameObject.activeInHierarchy)continue;
                        if(followed==actor.GuardId) {float step=Vector3.Distance(previous,actor.transform.position);largestStep=Math.Max(largestStep,step);walked|=step>0.001f;}
                        followed=actor.GuardId;previous=actor.transform.position;
                        arrived|=Vector3.Distance(actor.transform.position,ground+Vector3.up)<3.5f;
                        game.PlayerMotor.transform.position=actor.transform.position+Vector3.back*6+Vector3.up;
                        break;
                    }
                yield return null;
            }
            Require(alarm.Corpse.Cleared && report!=null && walked && arrived,"Physical guard arrival/cleanup missing: natural="+naturalWitness+
                "; cleared="+alarm.Corpse.Cleared+"; moved="+walked+"; arrived="+arrived+"; witnessWalked="+witnessWalked+
                "; reports="+string.Join(",",guards.Roster.Reports.ConvertAll(r=>r.Id).ToArray()));
            if(naturalWitness)Require(talkVerified,"Witness report did not include the visible conversation pause.");
            yield return new WaitForSecondsRealtime(1);
            Require(!FindCorpse(alarm.Id),"Cleared corpse still rendered after guard arrival.");
            Require(report.HasCorpseInquiryWindow && report.InvestigationUntil<=alarm.Corpse.ClearedAt+1800.01,"Cleanup did not start a bounded inquiry.");
            Require(game.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None,"Unwitnessed corpse accused invisible player.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CORPSE_PHYSICAL_CLEANUP_PASS: nativeWeaponDeath=true; realInnTransition="+(!naturalWitness)+
                "; quickReturnPreserved="+(!naturalWitness)+"; guardWalked=true; bodyRemoved=true; maxStep="+largestStep);

            // Isolate physical inquiry from unrelated routine movement. Supply one explicit direct-account fixture.
            streets.enabled=false;
            GuardActor investigator=null;
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                if(report.GuardIds.Contains(actor.GuardId) && actor.gameObject.activeInHierarchy) {investigator=actor;break;}
            Require(investigator,"Physical investigator missing.");
            if(!naturalWitness && !staticWitness)
                yield return CorpseSearchRecruitmentNativeProbe.Run(guards,store,directory,geometry,report,investigator);
            if(!naturalWitness && !staticWitness)
            {
                var account=store.Witness(alarm.Id,witness.Id,Now());Require(account!=null && account.DirectWitness,"Controlled direct account rejected.");
                StageResident(streets,store,witness,directory,ground);
            }
            Vector3 inquiryStart=investigator.transform.position;bool inquiryWalked=false;
            deadline=Time.realtimeSinceStartup+(staticWitness?90:30);
            while(report.CorpseSuspect==null && Time.realtimeSinceStartup<deadline)
            {
                inquiryWalked|=Vector3.Distance(inquiryStart,investigator.transform.position)>4;
                yield return null;
            }
            var testimony=alarm.People.Find(p=>p.ResidentId==witnessId);
            Require(report.CorpseSuspect!=null && (naturalWitness ? witnessWalked && testimony!=null && testimony.StatementDelivered &&
                testimony.OutdoorReport!=null && testimony.OutdoorReport.Delivered :
                alarm.Corpse.Inquiries.Exists(r=>r.PersonId==witnessId && r.StaticPerson==staticWitness && r.ReceivedWitnessStatement)),"Guard did not physically receive the direct account; natural="+naturalWitness+
                "; known="+(report.CorpseSuspect!=null)+"; inquiries="+alarm.Corpse.Inquiries.Count+"; guard="+investigator.transform.position+
                "; witness="+(streets.FindActor(witness.Id)?streets.FindActor(witness.Id).transform.position.ToString():"none"));
            Require(report.SuspectSighting==null && game.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None,"Identity knowledge invented suspect sight.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CORPSE_INQUIRY_NATIVE_PASS: physicalContact=true; directAccount=true; hiddenSuspectNotLocated=true; guards="+report.GuardIds.Count);
            if(staticWitness)
            {
                foreach(var candidate in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                    if(candidate.GuardId==report.CorpseSuspect.LearnedByGuardId && candidate.gameObject.activeInHierarchy)
                    {investigator=candidate;break;}
                Require(fixedObserver && fixedObserver.gameObject.activeInHierarchy && inquiryWalked &&
                    Vector3.Distance(investigator.transform.position,fixedObserver.transform.position)<=4.1f,
                    "Guard did not physically approach the distant static eyewitness.");
                float started=Time.time,until=Field<float>(investigator,"reportConversationUntil");
                var guardPoint=investigator.transform.position;var witnessPoint=fixedObserver.transform.position;
                Require(until-started>=1.8f && until-started<=5.1f,"Physical inquiry has no two-to-five-second conversation.");
                game.PauseGame(true);float pausedAt=Time.time;
                yield return new WaitForSecondsRealtime(0.75f);
                Require(Mathf.Abs(Time.time-pausedAt)<0.01f,"Game pause consumed inquiry conversation time.");
                game.PauseGame(false);
                while(Time.time<until)
                {
                    var toward=witnessPoint-guardPoint;toward.y=0;
                    Require(Vector3.Distance(investigator.transform.position,guardPoint)<0.2f &&
                        Vector3.Distance(fixedObserver.transform.position,witnessPoint)<0.2f,
                        "Inquiry partners moved during their physical conversation.");
                    Require(Vector3.Dot(investigator.transform.forward,toward.normalized)>0.9f &&
                        Field<Transform>(investigator,"conversationPartner")==fixedObserver.transform &&
                        Field<bool>(investigator,"inquiryConversation"),
                        "Inquiry lost guard facing or exact stationary witness focus.");
                    yield return null;
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIC_INQUIRY_CONVERSATION_PASS: unpausedSeconds="+(Time.time-started)+
                    "; realPauseExcluded=true; bothStill=true; guardFacing=true; stationaryFocus=true; "+
                    "cameraFacingPreserved=true; staticDirectionalArt=false; directAccount=true");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIC_CORPSE_INQUIRY_PASS: actualWitness=true; cleanupFirst=true; walkedToWitness=true; physicalContact=true; sharedCase=true; hiddenSuspectNotLocated=true");
            }
            if(npcKiller)
            {
                streets.enabled=moving;
                yield return NpcMurderNativeProbe.Finish(guards,store,geometry,alarm,report,killer,investigator,mode==Case.DeadNpcKiller);
                yield return ConversationMoodProbe.LoadSave(host,streets,character,"People - Gothway Garden",false);
                yield break;
            }
            CrimeSceneProbe.SetPlayerInvisible(game.PlayerEntity,false);
            bool visiblePlacement=false;
            foreach(var direction in new[] {Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
            {
                var point=investigator.transform.position+direction*3;
                if(!CivilianResponses.Sees(investigator.transform.position,point,20))continue;
                game.PlayerMotor.transform.position=point;visiblePlacement=true;break;
            }
            Require(visiblePlacement,"No clear observer position near investigator.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CORPSE_SIGHT_STAGE: invisible="+game.PlayerEntity.IsInvisible+
                "; sees="+CivilianResponses.Sees(investigator.transform.position,game.PlayerEntityBehaviour.transform.position,20)+
                "; paused="+GameManager.IsGamePaused+"; until="+report.InvestigationUntil+"; now="+Now());
            deadline=Time.realtimeSinceStartup+15;
            while(report.SuspectSighting==null && Time.realtimeSinceStartup<deadline)yield return null;
            Require(report.SuspectSighting!=null && alarm.AccusationIssued && game.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.Murder,
                "Actual suspect sight did not trigger native murder pursuit: sight="+(report.SuspectSighting!=null)+
                "; accused="+alarm.AccusationIssued+"; crime="+game.PlayerEntity.CrimeCommitted+
                "; invisible="+game.PlayerEntity.IsInvisible+"; sees="+CivilianResponses.Sees(investigator.transform.position,game.PlayerEntityBehaviour.transform.position,20)+
                "; paused="+GameManager.IsGamePaused);
            Require(report.GuardIds.Count<=GuardRoster.MaximumCorpseSearchGuards,"Native corpse search exceeded six guards.");
            var wire=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot(),false));
            var restored=new WorldStateStore(0);restored.Restore(wire);
            Require(restored.OutdoorCorpses(population.SettlementId).Find(a=>a.Id==alarm.Id).Corpse.Cleared,"Native save restored removed corpse.");
            CrimeSceneProbe.SetPlayerInvisible(game.PlayerEntity,true);
            double end=report.InvestigationUntil;DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)Math.Max(0,end-Now()+3601));
            streets.enabled=moving;yield return new WaitForSecondsRealtime(2);
            Require(!FindCorpse(alarm.Id) && !guards.Roster.Guards.Exists(g=>g.IncidentId==reportId && (g.Duty==GuardDuty.Searching || g.Duty==GuardDuty.Engaged)),"Expired corpse search or corpse remained active.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_SCENE_PASS: physicalCleanup=true; physicalInquiry=true; sightBeforePursuit=true; sixGuardCap=true; nativeSerializer=true; searchExpired=true");
            if(naturalWitness)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATURAL_MURDER_REPORT_PASS: actualWitness=true; reporterWalked=true; physicalDelivery=true; cleanup=true; identityBeforeSight=true; nativePursuit=true");
            yield return ConversationMoodProbe.LoadSave(host,streets,character,"People - Gothway Garden",false);
        }
        static ResidentActor StageResident(StreetPopulation streets,WorldStateStore store,Resident resident,BuildingDirectory directory,Vector3 ground)
        {
            var actors=Field<Dictionary<string,ResidentActor>>(streets,"actors");ResidentActor old;
            if(actors.TryGetValue(resident.Id,out old) && old) {old.gameObject.SetActive(false);UnityEngine.Object.Destroy(old.gameObject);}
            var go=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,directory.transform);
            var actor=go.AddComponent<ResidentActor>();actor.Initialize(resident,resident.AppearanceSeed,GameManager.Instance.StreamingWorld.GetCurrentCityNavigation(),inventoryStore:store);
            actor.Place(ground,Vector3.forward,true);actors[resident.Id]=actor;go.SetActive(true);return actor;
        }
        static CorpseInteraction FindCorpse(string id) {foreach(var body in UnityEngine.Object.FindObjectsOfType<CorpseInteraction>())if(body.IncidentId==id)return body;return null;}
        static T Field<T>(object value,string name) {return (T)value.GetType().GetField(name,Private).GetValue(value);}
        static double Now() {return DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();}
        static void Require(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    }
}
