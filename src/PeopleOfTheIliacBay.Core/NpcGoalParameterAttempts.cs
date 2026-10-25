using System;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Three bounded parameter attempts, anchored to the last accepted partial proposal.</summary>
    public sealed class NpcGoalParameterAttempts
    {
        public const int MaximumAttempts = 3;
        public NpcGoalDecomposition Decomposition { get; }
        public int Count { get; private set; }
        public bool Ready { get; private set; }
        public bool CanRetry => Count > 0 && Count < MaximumAttempts && !Ready;
        public NpcGoalExtractionResult Result { get; private set; }
        public string Reason { get; private set; } = "";
        public string RepairAnchor { get; private set; }
        private string trustedOutput;

        public NpcGoalParameterAttempts(NpcGoalDecomposition decomposition)
        {
            if (decomposition == null || decomposition.Intent != NpcGoalIntent.Request)
                throw new ArgumentException("Parameter attempts require a frozen action request.");
            Decomposition = decomposition;
        }

        public void Accept(string output)
        {
            if (Ready || Count >= MaximumAttempts)
                throw new InvalidOperationException("Parameter attempt budget is closed.");
            Count++;
            Result = trustedOutput == null
                ? NpcGoalTwoPassChat.ParseParameters(output, Decomposition)
                : NpcGoalTwoPassChat.ParseParameterRepair(output, Decomposition, trustedOutput);
            if (Result.Status == NpcGoalExtractionStatus.Valid)
            {
                trustedOutput = output;
                Reason = NpcGoalTwoPassChat.ParameterRepairIssue(Result.Proposal);
                Ready = string.IsNullOrWhiteSpace(Reason);
            }
            else Reason = Result.Reason;
            // An invalid correction never replaces the last accepted constraints.
            // With no accepted proposal, bounded malformed data can still be repaired.
            RepairAnchor = trustedOutput ?? (output != null && output.Length <= 32768 ? output : "");
        }
    }
}
