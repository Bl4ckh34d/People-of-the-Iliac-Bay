using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        // Prepare optional recollections before the wallet commit. Only the successful
        // service publisher may release these facts; a cursor replay is not a new event.
        private List<NpcSceneMemory> PrepareInnPurchaseMemories(NpcInnConsumptionCursor cursor)
        {
            var result=new List<NpcSceneMemory>();
            if(cursor==null || cursor.Receipt.Outcome!=NpcInnConsumptionOutcome.Purchased)return result;
            var receipt=cursor.Receipt;
            string payer=receipt.PayerId??receipt.BuyerId;
            string buyerName=InnParticipantName(receipt.BuyerId),keeperName=InnParticipantName(receipt.KeeperId);
            string payerName=InnParticipantName(payer);
            string product=CleanMemory(receipt.ProductName,80);
            string price=receipt.ChargedGold.ToString(CultureInfo.InvariantCulture)+" gold";
            string payment=payer==receipt.BuyerId?"I paid "+price:payerName+" paid "+price+" for me";
            AddInnPurchaseMemory(result,cursor,receipt.BuyerId,receipt.KeeperId,
                "I received "+product+" from "+keeperName+" at the Inn; "+payment+".");
            AddInnPurchaseMemory(result,cursor,receipt.KeeperId,payer,
                "I served "+product+" to "+buyerName+" at the Inn; "+payerName+" paid "+price+".");
            // A funded partner knows their own payment. Marriage or shared plans alone
            // do not make anyone a witness to the other person's separate purchase.
            if(payer!=receipt.BuyerId && payer!=receipt.KeeperId)
                AddInnPurchaseMemory(result,cursor,payer,receipt.KeeperId,
                    "I paid "+price+" to "+keeperName+" at the Inn for "+product+" for "+buyerName+".");
            return result;
        }

        private string InnParticipantName(string personId)
        {
            var resident=ResolveResident(personId);
            var native=IndexedNativePerson(personId);
            string name=CleanMemory(resident!=null?resident.FullName:native?.Name,100);
            return string.IsNullOrWhiteSpace(name)?"the other person":name;
        }

        private static void AddInnPurchaseMemory(List<NpcSceneMemory> memories,NpcInnConsumptionCursor cursor,
            string observer,string responsible,string observation)
        {
            try
            {
                memories.Add(NpcSceneMemory.Copy(new NpcSceneMemory {
                    ObserverId=observer,EventId="inn-purchase:"+cursor.Receipt.VisitId,Kind=NpcSceneEventKind.Shopping,
                    At=Math.Floor(cursor.Receipt.CompletedAt),SettlementId=cursor.SettlementId,BuildingKey=cursor.BuildingKey,
                    Observation=observation,Valence=NpcEventValence.Default(NpcSceneEventKind.Shopping),
                    SubjectId=cursor.Receipt.BuyerId,ResponsibleId=responsible}));
            }
            catch(ArgumentException)
            {
                // Receipt identities allow longer IDs than scene memory. An optional
                // projection must not prevent a valid native payment or truncate an ID.
            }
        }

        private void PublishInnPurchaseMemories(List<NpcSceneMemory> memories)
        {
            foreach(var memory in memories)
                RememberSceneEvent(memory.ObserverId,memory.EventId,memory.Kind,memory.At,memory.SettlementId,
                    memory.BuildingKey,memory.Observation,memory.Valence,memory.SubjectId,memory.ResponsibleId);
        }
    }
}
