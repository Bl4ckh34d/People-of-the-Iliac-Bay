using System;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // This component has no action authority. No production caller enables or
    // submits it until the separately verified shadow-routing slice exists.
    internal interface INpcDecisionAdapter
    {
        bool Request(LocalDialogueConnection connection, NpcDecisionRequest request,
            Func<LocalDecisionFreshness> captureCurrent, Action<NpcDecisionResult> complete);
        void Cancel(Guid requestId);
    }

    internal sealed class LocalDecisionClient : MonoBehaviour, INpcDecisionAdapter
    {
        private LocalDecisionSession session;

        internal string ExpectedModelId
        {
            get { return session == null ? null : session.ExpectedModelId; }
            set { EnsureSession(); session.ExpectedModelId = value; }
        }

        internal bool Enabled
        {
            get { return session != null && session.Enabled; }
            set
            {
                EnsureSession();
                session.Enabled = value;
            }
        }

        public bool Request(LocalDialogueConnection connection, NpcDecisionRequest request,
            Func<LocalDecisionFreshness> captureCurrent, Action<NpcDecisionResult> complete)
        {
            EnsureSession();
            return session.TryRequest(connection, request, captureCurrent, complete);
        }

        public void Cancel(Guid requestId)
        {
            if (session != null) session.Cancel(requestId);
        }

        private void EnsureSession()
        {
            if (session == null) session = new LocalDecisionSession();
        }

        private void Awake() { EnsureSession(); }
        private void Update() { if (session != null) session.Pump(); }
        private void OnDisable() { if (session != null) session.CancelAll(); }
        private void OnDestroy() { if (session != null) session.Dispose(); }
    }
}
