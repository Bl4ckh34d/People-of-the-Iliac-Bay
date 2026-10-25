using System;

namespace PeopleOfTheIliacBay.Core
{
    // A physical detour, not another task planner. DFU owns the treatment itself.
    [Serializable] public sealed class NpcTreatmentVisit
    {
        public int ProviderKey,ReturnKey,RecipeKey=-1;
        public string EffectKey;
        public NpcActionTarget Position;
        public bool Returning;
        public double ServiceAt;
    }

    public sealed partial class WorldStateStore
    {
        public bool HasCurrentNpcServiceDuty(string id,double now)
        {
            foreach(var contract in state.NpcServiceContracts.Contracts)
                if(contract.NpcId==id && contract.Status==NpcServiceContractStatus.Active &&
                    contract.BlockStartAt<=now && now<contract.BlockEndAt)return true;
            return false;
        }

        public NpcTreatmentVisit ReadNpcTreatmentVisit(string id)
        {
            var progress=IndexedNpcProgress(id);
            return progress==null?null:CopyTreatmentVisit(progress.TreatmentVisit);
        }
        public bool HasNpcTreatmentVisit(string id)
        {
            var progress=IndexedNpcProgress(id);return progress!=null && progress.TreatmentVisit!=null;
        }

        public bool SaveNpcTreatmentVisit(object epoch,string id,NpcTreatmentVisit visit)
        {
            if(!IsNpcVitalsWriteEpochCurrent(epoch))return false;
            var current=IndexedNpcProgress(id);
            bool alive;
            if(current==null || !KnownNpcIdentity(state,id,out alive) || !alive)return false;
            ValidateTreatmentVisit(visit);
            var copy=CopyNpcProgress(current);copy.TreatmentVisit=CopyTreatmentVisit(visit);
            state.NpcActors[state.NpcActors.IndexOf(current)]=copy;npcActorIndex[id]=copy;return true;
        }

        private static void ValidateTreatmentVisit(NpcTreatmentVisit visit)
        {
            if(visit==null)return;
            var p=visit.Position;
            Require(visit.ProviderKey>=0 && visit.ReturnKey>=0 && visit.RecipeKey>=-1 &&
                !string.IsNullOrEmpty(visit.EffectKey) && visit.EffectKey.Length<=80 &&
                Finite(visit.ServiceAt) && visit.ServiceAt>=0 && p!=null &&
                (p.Kind==NpcActionTargetKind.WorldPoint || p.Kind==NpcActionTargetKind.IndoorPoint) &&
                Finite(p.X) && Finite(p.Y) && Finite(p.Z) && p.PersonId==null && p.ItemId==null &&
                p.ContainerId==null,"Invalid NPC treatment visit.");
        }

        private static NpcTreatmentVisit CopyTreatmentVisit(NpcTreatmentVisit visit)
        {
            return visit==null?null:new NpcTreatmentVisit {ProviderKey=visit.ProviderKey,ReturnKey=visit.ReturnKey,
                RecipeKey=visit.RecipeKey,EffectKey=visit.EffectKey,Position=NpcActionExecutor.CopyTarget(visit.Position),
                Returning=visit.Returning,ServiceAt=visit.ServiceAt};
        }
    }
}
