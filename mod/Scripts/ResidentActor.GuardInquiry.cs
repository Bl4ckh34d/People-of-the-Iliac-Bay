using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class ResidentActor
    {
        private GuardActor inquiryGuard;
        internal void BeginGuardInquiry(GuardActor guard){inquiryGuard=guard;}
        internal void EndGuardInquiry(GuardActor guard){if(inquiryGuard==guard)inquiryGuard=null;}
        internal void ReleaseFlightPause()
        {inquiryGuard=null;SetConversationFacing(false);ResumeFromSocialPause();}
        internal bool HoldGuardInquiryPosition()
        {
            if(!inquiryGuard || !inquiryGuard.HoldsInquiry(this) || IsConversing || IsSleeping ||
                Resident==null || !Resident.IsAlive || !gameObject.activeInHierarchy)
            {inquiryGuard=null;return false;}
            var navigation=GetComponent<NativeResidentNavigation>();if(navigation)navigation.Pause();
            HoldPhysicalPosition();
            var facing=inquiryGuard.transform.position-transform.position;facing.y=0;
            // Mobile root heading selects existing directional frames; its
            // child billboard keeps facing the camera. A static flat shares
            // its root with that camera-facing quad and has no directional set.
            if(!IsStationary && facing.sqrMagnitude>0.0001f)transform.forward=facing.normalized;
            return true;
        }
    }
}
