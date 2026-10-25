using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    // Runtime resolves this recipient from one currently visible native actor.
    // Core never turns display text into a person identity.
    public static class NpcNamedAcquiredHandover
    {
        public static NpcActionOption TryBind(int sourceIndex,NpcActionOption source,NpcActionTarget verifiedRecipient,string recipientName)
        {
            if(sourceIndex<0 || sourceIndex>=NpcTaskContract.MaximumSteps-1 || source==null || source.Target==null ||
                verifiedRecipient==null || verifiedRecipient.Kind!=NpcActionTargetKind.Person ||
                string.IsNullOrWhiteSpace(verifiedRecipient.PersonId) || verifiedRecipient.PersonId.Length>240 ||
                verifiedRecipient.PersonId=="player" || string.IsNullOrWhiteSpace(recipientName) || recipientName.Length>160)return null;

            var sourceTarget=source.Target;
            bool purchase=source.Kind==NpcActionKind.Purchase && !string.IsNullOrEmpty(sourceTarget.ContainerId);
            bool homeRetrieval=source.Kind==NpcActionKind.Transfer && sourceTarget.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc &&
                sourceTarget.Kind==NpcActionTargetKind.IndoorPoint && !string.IsNullOrEmpty(sourceTarget.ContainerId);
            ulong uid;
            if((!purchase && !homeRetrieval) || !ulong.TryParse(sourceTarget.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid) || uid==0 ||
                sourceTarget.Quantity<1 || sourceTarget.Quantity>1000000)return null;

            string origin=purchase?"preceding purchase":"preceding home-container retrieval";
            return new NpcActionOption {Id=1,Kind=NpcActionKind.Transfer,ItemSourceStep=sourceIndex+1,
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId=verifiedRecipient.PersonId,
                    ItemId=sourceTarget.ItemId,Quantity=sourceTarget.Quantity,
                    TransferDirection=NpcActionTransferDirection.NpcToCounterparty},
                Label="hand over the exact "+sourceTarget.Quantity+" item(s) acquired by the "+origin+" to "+recipientName+", only after that step succeeds"};
        }
    }
}
