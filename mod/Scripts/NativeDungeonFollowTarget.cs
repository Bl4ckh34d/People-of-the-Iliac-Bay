using UnityEngine;
using PeopleOfTheIliacBay.Core;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeDungeonFollowTarget
    {
        internal static Vector3 Resolve(ResidentActor actor, Vector3 player, out bool standing,bool requireDoorArrival=false)
        {
            Vector3 actual = actor.transform.position - Vector3.up;
            standing = false;
            var airborne = actor.GetComponent<NativeDungeonJumpTraversal>();
            if (airborne && airborne.Active) return player;
            var surface=actor.GetComponent<NativeDungeonSurfaceTraversal>();
            if(surface && surface.Active){surface.Advance();return actor.transform.position-Vector3.up;}
            var path = new NavMeshPath();
            bool walking = NavMesh.CalculatePath(actual, player, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            float length = PathLength(path);
            standing = !requireDoorArrival && NpcFollowArrival.CanHold(Vector3.Distance(actual, player), walking, length);
            if (standing) return actual;
            if(!walking && NativeDungeonSurfaceTraversal.TryMove(actor,player))return actor.transform.position-Vector3.up;
            if(walking)return player;
            if(path.status==NavMeshPathStatus.PathPartial && path.corners.Length>1)
            {
                var edge=path.corners[path.corners.Length-1];
                if(Vector3.Distance(actual,edge)>0.4f)return edge;
            }
            if (!airborne) airborne = actor.gameObject.AddComponent<NativeDungeonJumpTraversal>();
            // The player's occupied point cannot be a landing. Only a measured
            // floor in that same short, walk-connected destination area may be.
            var towardActor = actual - player; towardActor.y = 0;
            var side = Vector3.Cross(towardActor.normalized, Vector3.up);
            foreach (var candidate in new[] { player, player + towardActor.normalized * 0.8f,
                player + side * 0.8f, player - side * 0.8f,
                actual-towardActor.normalized*4f,actual-towardActor.normalized*3f,actual-towardActor.normalized*2f })
            {
                NavMeshHit sampled;
                if (!NavMesh.SamplePosition(candidate, out sampled, 0.15f, NavMesh.AllAreas) ||
                    Mathf.Abs(sampled.position.y - candidate.y) > 0.18f) continue;
                var approach = new NavMeshPath();
                if (!NavMesh.CalculatePath(sampled.position, player, NavMesh.AllAreas, approach) ||
                    approach.status != NavMeshPathStatus.PathComplete) continue;
                Vector3 landing; NpcJumpTrajectory trajectory; string refusal;
                if (airborne.TryPrepare(sampled.position, actor.WalkingSpeed, null, out landing, out trajectory, out refusal)) return landing;
                if(NativeDungeonSurfaceTraversal.TryMove(actor,sampled.position))return actor.transform.position-Vector3.up;
            }
            return player;
        }

        private static float PathLength(NavMeshPath path)
        {
            float length = 0;
            for (int index = 1; index < path.corners.Length; index++) length += Vector3.Distance(path.corners[index - 1], path.corners[index]);
            return length;
        }
    }
}
