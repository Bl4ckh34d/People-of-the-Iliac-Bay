using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class GoalSequenceProbe
    {
        // Acceptance only: reuse an actual saved normal hire, never create consent.
        internal static IEnumerator VerifyNativeCompanionFastTravel(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            RequireFastTravel(ResumeHiredFixture,"Fast-travel acceptance requires the genuine hired checkpoint and explicit ResumeHired.");
            if(MultipleFastTravel && Environment.GetEnvironmentVariable("POTIB_FAST_TRAVEL_PARTY_RESUME")=="1")
                yield return ResumeActualTravelParty(host,streets);
            else yield return HireActualSequenceCompanion(host,streets);
            var game=GameManager.Instance;var roads=host.GetComponent<RoadTravelActors>();
            var store=(WorldStateStore)Field(host.GetType(),"store").GetValue(host);
            var party=roads.CaptureFastTravelQuote();
            int expectedMembers=MultipleFastTravel?2:1;
            RequireFastTravel(party.Members.Count==expectedMembers,"Native quote did not contain the expected actually hired party: "+party.Members.Count);
            party.Members.Sort((left,right)=>left.Actor.Resident.Id==SequenceCompanion?-1:right.Actor.Resident.Id==SequenceCompanion?1:0);
            RequireFastTravel(party.Members[0].Actor.Resident.Id==SequenceCompanion,"Original actual Allan is absent from the party.");
            AdditionalTravelEvidence additional=null;
            if(MultipleFastTravel)
            {
                RequireFastTravel(party.Members[1].Actor==secondTravelCompanion,"Second native hire was replaced before departure.");
                additional=new AdditionalTravelEvidence(store,party.Members[1]);
            }
            var actor=party.Members[0].Actor;var job=store.ReadNpcAction(party.Members[0].ActionId);
            var inventoryOwner=actor.GetComponent<NativeNpcInventoryOwner>();
            RequireFastTravel(inventoryOwner && inventoryOwner.Binding!=null,"Actual hired body has no native inventory binding.");
            actor.CaptureState();string inventory=FastTravelInventory(store,actor.Resident.Id);
            var items=new Dictionary<ulong,DaggerfallUnityItem>();var equipment=new Dictionary<ulong,EquipSlots>();
            for(int index=0;index<inventoryOwner.Binding.Items.Count;index++)
            {var item=inventoryOwner.Binding.Items.GetItem(index);items.Add(item.UID,item);equipment.Add(item.UID,item.EquipSlot);}
            string relation=FastTravelRelationship(store,actor.Resident.Id);
            int body=actor.GetInstanceID(),person=actor.Person.GetInstanceID(),ownerId=inventoryOwner.GetInstanceID();
            double accepted=job.AcceptedAt,finish=job.FinishAt,deadline=job.DeadlineAt;
            var contract=store.ReadNpcServiceContracts().Read(NpcPaidCompanionAction.ContractId(job));
            RequireFastTravel(contract!=null,"Actual paid contract is missing.");
            string contractId=contract.ContractId;double blockStart=contract.BlockStartAt,blockEnd=contract.BlockEndAt;int funded=contract.FundedGold;
            RequireFastTravel(game.PlayerEffectManager.DiseaseCount==0 && game.PlayerEffectManager.PoisonCount==0,
                "This finite travel acceptance requires its actual disease-free checkpoint; no disease state is altered.");
            DFLocation destination;
            bool continueFollow=MultipleFastTravel || Environment.GetEnvironmentVariable("POTIB_FAST_TRAVEL_CONTINUE")=="1";
            double earliest=Math.Min(finish,deadline);
            if(additional!=null)earliest=Math.Min(earliest,Math.Min(additional.Finish,additional.Deadline));
            if(continueFollow)destination=SelectNativeContinuationDestination(earliest);
            else RequireFastTravel(DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall","Gothway Garden",out destination),
                "Existing native Gothway Garden destination is unavailable.");
            var target=MapsFile.LongitudeLatitudeToMapPixel(destination.MapTableData.Longitude,destination.MapTableData.Latitude);
            int targetMap=destination.MapTableData.MapId,sourceMap=game.PlayerGPS.CurrentLocation.MapTableData.MapId;
            var map=DaggerfallUI.Instance.DfTravelMapWindow;
            var manager=DaggerfallUI.UIManager;
            manager.PushWindow(map);yield return null;
            var popup=(NativeCompanionTravelPopUp)UIWindowFactory.GetInstanceWithArgs(UIWindowType.TravelPopUp,new object[]{manager,map,map});
            typeof(DaggerfallTravelPopUp).GetProperty("EndPos").SetValue(popup,target,null);
            popup.SpeedCautious=false;popup.TravelShip=false;popup.SleepModeInn=true;
            manager.PushWindow(popup);yield return null;
            int total,pieces,minutes;
            ReadNativeFastTravelPrice(popup,expectedMembers,out total,out pieces,out minutes);
            RequireFastTravel(total>0 && pieces>0,"Actual native route must have a nonzero fare and coin requirement for this funds check.");
            int purse=game.PlayerEntity.GetGoldAmount(),coins=game.PlayerEntity.GoldPieces;
            RequireFastTravel(purse>=total && coins>=pieces,"Actual checkpoint lacks native fare; this observer does not grant travel money.");
            double before=FastTravelNow();var position=game.PlayerMotor.transform.position;
            popup.ExitButtonOnClickHandler(null,Vector2.zero);yield return null;
            RequireNoNativeFastTravel(game,store,job.Id,sourceMap,position,before,purse,coins,finish,deadline);
            RequireFastTravel(!store.ReadNpcAction(job.Id).FastTravelFollowOwner,"Cancel acquired fast-travel ownership before confirmation.");
            if(additional!=null){additional.Check(store);RequireFastTravel(!store.ReadNpcAction(additional.Action).FastTravelFollowOwner,"Cancel acquired second member ownership.");}
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_CANCEL_PASS: person="+actor.Resident.Id+"; nativeExit=true; walletTimeMapActionUnchanged=true");

            manager.PushWindow(popup);yield return null;
            // Transparent, reversible purse condition only; the companion and booking are untouched.
            try
            {
                game.PlayerEntity.GoldPieces=0;before=FastTravelNow();int poorPurse=game.PlayerEntity.GetGoldAmount();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_FUNDS_FIXTURE: controlledPlayerCoins=0; originalCoins="+coins+"; originalWealth="+purse+"; nativeGoldCheck=true");
                popup.BeginButtonOnClickHandler(null,Vector2.zero);yield return null;
                RequireNoNativeFastTravel(game,store,job.Id,sourceMap,position,before,poorPurse,0,finish,deadline);
                if(additional!=null){additional.Check(store);RequireFastTravel(!store.ReadNpcAction(additional.Action).FastTravelFollowOwner,"Insufficient funds acquired second member ownership.");}
                RequireFastTravel(!(bool)Field(typeof(DaggerfallTravelPopUp),"doFastTravel").GetValue(popup) &&
                    manager.TopWindow is DaggerfallMessageBox && manager.TopWindow!=popup,
                    "Native insufficient-funds popup did not reject travel.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_FUNDS_PASS: nativePopup=true; walletTimeMapActionUnchanged=true; noTransport=true");
            }
            finally {game.PlayerEntity.GoldPieces=coins;}
            while(manager.TopWindow!=popup)manager.PopWindow();
            Method(typeof(DaggerfallTravelPopUp),"Refresh",Members,0).Invoke(popup,null);
            ReadNativeFastTravelPrice(popup,expectedMembers,out total,out pieces,out minutes);
            int departures=0,arrivals=0;Action<DaggerfallTravelPopUp> pre=window=>departures++;
            DaggerfallTravelPopUp.OnOnPostFastTravelEventHandler post=()=>arrivals++;
            DaggerfallTravelPopUp.OnPreFastTravel+=pre;DaggerfallTravelPopUp.OnPostFastTravel+=post;
            before=FastTravelNow();
            try
            {
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_BEGIN: displayedTotal="+total+"; displayedCoins="+pieces+"; nativeMinutes="+minutes+"; normalNativeBegin=true; newHire=false; newWage=false");
                popup.BeginButtonOnClickHandler(null,Vector2.zero);
                float ready=Time.realtimeSinceStartup+30;
                while(Time.realtimeSinceStartup<ready && (arrivals==0 || game.StreamingWorld.IsInit || !game.StreamingWorld.IsReady ||
                    game.StreamingWorld.IsRepositioningPlayer || !actor.gameObject.activeInHierarchy || !game.PlayerGPS.HasCurrentLocation ||
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId!=targetMap || additional!=null && !additional.Actor.gameObject.activeInHierarchy))yield return null;
                RequireFastTravel(departures==1 && arrivals==1 && game.PlayerGPS.HasCurrentLocation &&
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId==targetMap && actor.gameObject.activeInHierarchy,
                    "Actual native journey/companion arrival did not complete within 30 seconds; pre="+departures+"; post="+arrivals+"; bodyActive="+actor.gameObject.activeInHierarchy);
                RequireFastTravel(game.PlayerEntity.GetGoldAmount()==purse-total,"Native journey did not debit exactly one displayed party fare.");
                RequireFastTravel(FastTravelNow()>=before+minutes*60 && FastTravelNow()<before+minutes*60+120,
                    "Reckless native travel did not preserve its quoted native duration.");
                var current=store.ReadNpcAction(job.Id);var paid=store.ReadNpcServiceContracts().Read(contractId);
                RequireFastTravel(current!=null && current.ResidentId==actor.Resident.Id && current.AcceptedAt==accepted &&
                    current.FinishAt==finish && current.DeadlineAt==deadline && paid!=null && paid.BlockStartAt==blockStart &&
                    paid.BlockEndAt==blockEnd && paid.FundedGold==funded,"Arrival altered original consent, contract period or wages.");
                RequireFastTravel(actor.GetInstanceID()==body && actor.Person.GetInstanceID()==person &&
                    actor.GetComponent<NativeNpcInventoryOwner>().GetInstanceID()==ownerId,"Fast travel replaced actual body/person/inventory owner.");
                actor.CaptureState();RequireFastTravel(FastTravelInventory(store,actor.Resident.Id)==inventory &&
                    FastTravelRelationship(store,actor.Resident.Id)==relation,"Travel changed the actual inventory payload/wallet or relationship.");
                RequireFastTravel(inventoryOwner.Binding.Items.Count==items.Count,"Travel changed native item collection size.");
                for(int index=0;index<inventoryOwner.Binding.Items.Count;index++)
                {var item=inventoryOwner.Binding.Items.GetItem(index);DaggerfallUnityItem expected;
                    RequireFastTravel(items.TryGetValue(item.UID,out expected) && ReferenceEquals(item,expected) && item.EquipSlot==equipment[item.UID],
                        "Travel replaced a native item UID/object or changed equipment.");}
                int visible=0;foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if(candidate && candidate.gameObject.activeInHierarchy && candidate.Resident!=null && candidate.Resident.Id==actor.Resident.Id)visible++;
                RequireFastTravel(visible==1,"Destination has duplicate visible companion bodies.");
                bool expired=FastTravelNow()>=Math.Min(finish,deadline);
                RequireFastTravel(expired?NpcActionExecutor.IsTerminal(current):NpcCompanionFastTravel.CanFollow(store,current,FastTravelNow()),
                    "Arrival failed to apply original consent expiry without renewal.");
                if(continueFollow)
                {
                    RequireFastTravel(!expired,"Continuation acceptance chose a route beyond the actual existing contract.");
                    yield return VerifyContinuedFastTravelFollow(store,actor,job.Id,body,finish,deadline,inventory);
                    if(additional!=null)
                    {
                        additional.Check(store);
                        RequireFastTravel(Vector3.Distance(actor.transform.position,additional.Actor.transform.position)>=CrowdMotion.BodyRadius*2,
                            "Actual party destination bodies overlap.");
                        yield return VerifyContinuedFastTravelFollow(store,additional.Actor,additional.Action,additional.Actor.GetInstanceID(),
                            additional.Finish,additional.Deadline,additional.Inventory);
                        additional.Check(store);
                        RequireFastTravel(actor && actor.gameObject.activeInHierarchy && NpcCompanionFastTravel.CanFollow(store,store.ReadNpcAction(job.Id),FastTravelNow()),
                            "First companion lost Follow while observing the second.");
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_PARTY_PASS: companions=2; actualNativeHires=true; cancelAndFunds=true; displayedFare="+total+
                            "; actualDebit="+(purse-game.PlayerEntity.GetGoldAmount())+"; originalBodiesItemsEquipmentContracts=true; bothPhysicalFollow=true; consentInjected=false");
                    }
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_NATIVE_PASS: companions="+expectedMembers+"; destination="+targetMap+"; displayedFare="+total+
                    "; actualDebit="+(purse-game.PlayerEntity.GetGoldAmount())+"; nativePre=1; nativePost=1; sameBodyPersonItemsEquipment=true; sameRelationship=true; originalConsentDates=true; expired="+expired+"; contractExtended=false; controlledFundsOnly=true; multipleCompanionsTested="+MultipleFastTravel);
            }
            finally {DaggerfallTravelPopUp.OnPreFastTravel-=pre;DaggerfallTravelPopUp.OnPostFastTravel-=post;}
            if(continueFollow && Environment.GetEnvironmentVariable("POTIB_FAST_TRAVEL_RELOAD")=="1")
                yield return VerifyFastTravelCheckpointReload(host,streets,job.Id);
        }

        private static void ReadNativeFastTravelPrice(NativeCompanionTravelPopUp popup,int companions,out int total,out int pieces,out int minutes)
        {
            var calculator=(TravelTimeCalculator)Field(typeof(DaggerfallTravelPopUp),"travelTimeCalculator").GetValue(popup);
            minutes=(int)Field(typeof(DaggerfallTravelPopUp),"travelTimeTotalMins").GetValue(popup);
            // The independent native calculator recomputes the same real path to retain ocean/discount inputs.
            var solo=new TravelTimeCalculator();
            int soloMinutes=solo.CalculateTravelTime(popup.EndPos,popup.SpeedCautious,popup.SleepModeInn,popup.TravelShip,
                (bool)Field(typeof(DaggerfallTravelPopUp),"hasHorse").GetValue(popup),(bool)Field(typeof(DaggerfallTravelPopUp),"hasCart").GetValue(popup));
            soloMinutes=GameManager.Instance.GuildManager.FastTravel(soloMinutes);
            solo.CalculateTripCost(soloMinutes,popup.SleepModeInn,(bool)Field(typeof(DaggerfallTravelPopUp),"hasShip").GetValue(popup),popup.TravelShip);
            total=checked(solo.TotalCost*(1+companions));pieces=checked(solo.PiecesCost*(1+companions));
            string displayed=((TextLabel)Field(typeof(DaggerfallTravelPopUp),"tripCostLabel").GetValue(popup)).Text;
            RequireFastTravel(minutes==soloMinutes && calculator.TotalCost==total && calculator.PiecesCost==pieces &&
                displayed==total.ToString(CultureInfo.CurrentCulture),"Actual displayed native popup does not equal solo fare times actual party size.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_VISIBLE_PRICE_PASS: companions="+companions+"; soloTotal="+solo.TotalCost+
                "; soloCoins="+solo.PiecesCost+"; actualLabel="+displayed+"; actualCoins="+calculator.PiecesCost+"; beforeBegin=true");
        }
        private static void RequireNoNativeFastTravel(GameManager game,WorldStateStore store,string action,int map,Vector3 position,double time,
            int purse,int coins,double finish,double deadline)
        {
            var job=store.ReadNpcAction(action);
            RequireFastTravel(game.PlayerEntity.GetGoldAmount()==purse && game.PlayerEntity.GoldPieces==coins && FastTravelNow()==time &&
                game.PlayerGPS.CurrentLocation.MapTableData.MapId==map && Vector3.Distance(game.PlayerMotor.transform.position,position)<0.01f &&
                job!=null && !NpcActionExecutor.IsTerminal(job) && job.FinishAt==finish && job.DeadlineAt==deadline,
                "Rejected/cancelled native journey changed purse, clock, position or accepted action.");
        }
        private static string FastTravelInventory(WorldStateStore store,string id)
        {return (string)Method(typeof(NativeDungeonFollowProbe),"InventorySignature",StaticMembers,1).Invoke(null,new object[]{store.ReadNpcInventory(id)});}
        private static string FastTravelRelationship(WorldStateStore store,string id)
        {
            var relation=store.ReadNpcRelationship(id,"player");if(relation==null)return "<unknown>";
            var values=new List<string>();foreach(var property in relation.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance))
                if(property.GetIndexParameters().Length==0)values.Add(property.Name+"="+Convert.ToString(property.GetValue(relation,null),CultureInfo.InvariantCulture));
            values.Sort(StringComparer.Ordinal);return string.Join("|",values.ToArray());
        }
        private static double FastTravelNow(){var time=DaggerfallUnity.Instance.WorldTime.Now;return time.ToSeconds()+(time.Second-Math.Floor(time.Second));}
        private static void RequireFastTravel(bool valid,string reason){if(!valid)throw new InvalidOperationException(reason);}
    }
}
