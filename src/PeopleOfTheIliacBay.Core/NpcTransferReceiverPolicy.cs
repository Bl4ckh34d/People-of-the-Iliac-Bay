namespace PeopleOfTheIliacBay.Core
{
    public static class NpcTransferReceiverPolicy
    {
        public static bool Allows(NpcActionJob active,NpcTaskPlan task,string expectedTransferId=null)
        {
            if(task!=null && !NpcTaskContract.IsTerminal(task) &&
                (string.IsNullOrEmpty(expectedTransferId) || NpcTaskContract.ActionId(task)!=expectedTransferId ||
                 task.Phase==NpcTaskPhase.Running && task.ActiveActionId!=expectedTransferId))return false;
            if(active==null || NpcActionExecutor.IsTerminal(active))return true;
            return !string.IsNullOrEmpty(expectedTransferId) && active.Id==expectedTransferId && active.Kind==NpcActionKind.Transfer &&
                active.Target!=null && active.Target.PersonId=="player" && active.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc;
        }
    }
}
