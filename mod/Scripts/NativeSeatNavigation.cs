using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Reuse seat identity, reservations and the final furniture-aware approach.
    // A nearby point on another floor is not evidence that a seat is reachable.
    internal static class NativeSeatNavigation
    {
        public static bool TryApproach(FurnitureSeatCatalog catalog,FurnitureSeatSlot seat,Vector3 from,out NavMeshPath path,System.Action<string> trace=null)
        {
            path=null;
            NavMeshHit start;
            if(!NavMesh.SamplePosition(from,out start,0.5f,NavMesh.AllAreas))return false;
            Vector3 forward=seat.Facing;forward.y=0;
            if(forward.sqrMagnitude<0.001f)forward=Vector3.forward;
            forward.Normalize();
            int sampled=0,connected=0;
            string rejection="";
            for(int ring=0;ring<4;ring++)for(int side=0;side<16;side++)
            {
                var candidate=seat.Ground+Quaternion.AngleAxis(side*22.5f,Vector3.up)*forward*(0.55f+ring*0.3f);
                NavMeshHit hit;
                if(!NavMesh.SamplePosition(candidate,out hit,0.3f,NavMesh.AllAreas) || Mathf.Abs(hit.position.y-seat.Ground.y)>0.3f)continue;
                sampled++;
                if(FurnitureSeatCatalog.Blocks(hit.position,hit.position,CrowdMotion.BodyRadius))continue;
                var route=new NavMeshPath();
                if(!NavMesh.CalculatePath(start.position,hit.position,NavMesh.AllAreas,route) || route.status!=NavMeshPathStatus.PathComplete)continue;
                connected++;
                if(!catalog.SetApproach(seat,hit.position,reason=>{if(rejection.Length==0)rejection=reason;}))continue;
                path=route;return true;
            }
            if(trace!=null)trace("seat="+seat.Id+"; ground="+seat.Ground+"; start="+start.position+"; sampled="+sampled+"; connected="+connected+"; rejection="+rejection);
            return false;
        }
    }
}
