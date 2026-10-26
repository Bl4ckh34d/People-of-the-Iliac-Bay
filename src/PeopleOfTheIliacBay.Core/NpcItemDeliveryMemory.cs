using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        // The committed receipt establishes only the handoff. The receiver did not
        // necessarily witness an earlier purchase or hear the player's request.
        private bool TryRememberNpcItemDelivery(NpcActionJob job,string date,string place)
        {
            var receipt=job.ItemTransferReceipt;
            if(job.Phase!=NpcActionPhase.Completed || job.CompletedEffectId!=job.Id+":observed" || receipt==null ||
                receipt.CounterpartyId!=job.ResidentId || receipt.RecipientId!=job.Target.PersonId)return false;
            var giver=ResolveResident(receipt.CounterpartyId);
            var recipient=ResolveResident(receipt.RecipientId);
            if(giver==null || recipient==null ||
                HasActionSource(MemoryFor(giver.Id,false),job.Id) || HasActionSource(MemoryFor(recipient.Id,false),job.Id))return false;
            string amount=receipt.Quantity.ToString(CultureInfo.InvariantCulture);
            string item=amount+" x "+receipt.NativeItemName;
            var given=ItemDeliveryMemoryEntry(job,date,place,
                "I handed "+item+" to "+InteractionMemoryLabel(recipient)+". They received it directly from me.");
            var received=ItemDeliveryMemoryEntry(job,date,place,
                "I received "+item+" directly from "+InteractionMemoryLabel(giver)+".");
            List<NpcConversationMemory> pending;
            Dictionary<string,NpcConversationMemory> preparedIndex;
            try
            {
                NpcActionTransferReceipt.CopyValidated(receipt,job);
                ValidateMemoryEntry(given);ValidateMemoryEntry(received);
                pending=CopyConversationMemories(state);
                AppendPending(pending,giver.Id,given);AppendPending(pending,recipient.Id,received);
                preparedIndex=new Dictionary<string,NpcConversationMemory>(memoryIndex,StringComparer.Ordinal);
                AppendIndexed(preparedIndex,giver.Id,given);AppendIndexed(preparedIndex,recipient.Id,received);
            }
            catch(ArgumentException) {return false;}
            // Publish both journals together; no narrative from the caller grants
            // purchase knowledge, gift intent, debt, gratitude or player credit.
            state.ConversationMemories=pending;
            pendingConversationMemoryEpoch=new object();
            memoryIndex=preparedIndex;memoryIndexOwner=state;
            return true;
        }

        private static NpcMemoryEntry ItemDeliveryMemoryEntry(NpcActionJob job,string date,string place,string observation)
        {
            return new NpcMemoryEntry {At=job.ItemTransferReceipt.ObservedGameTime,
                Date=CleanMemory(date,120),Place=CleanMemory(place,300),PlayerText="",NpcText="",
                Observation=CleanMemory(observation,600),Source="observed",SourceActionId=job.Id};
        }
    }
}
