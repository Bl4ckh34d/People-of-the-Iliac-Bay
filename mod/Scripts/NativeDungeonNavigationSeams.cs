using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Runs only after the world loader installs modular dungeon data. Returned
    // endpoints are world coordinates; the caller owns AddLink and removal.
    internal static class NativeDungeonNavigationSeams
    {
        private const float TransformTolerance=0.05f;
        private const float EdgeBand=1.2f;
        private const float CandidateSpacing=0.75f;
        private const float EndpointInset=0.60f;
        private const float SampleRange=0.45f;
        private const float FloorTolerance=0.25f;
        private const int MaxCandidatesPerSeam=2048;
        private const int MaxProbes=16384;

        private struct Seam
        {
            public Vector3 origin;
            public float side;
            public bool east;
            public float Edge {get{return (east?origin.x:origin.z)+side;}}
            public float Across(Vector3 point){return (east?point.x:point.z)-Edge;}
            public float Along(Vector3 point){return east?point.z-origin.z:point.x-origin.x;}
            public Vector3 Point(float along,float height)
            {return east?new Vector3(Edge,height,origin.z+along):new Vector3(origin.x+along,height,Edge);}
        }

        public static List<NavMeshLinkData> Collect(Transform root)
        {
            var links=new List<NavMeshLinkData>();
            if(!root || !root.GetComponent<DaggerfallDungeon>() ||
                (root.lossyScale-Vector3.one).sqrMagnitude>0.000001f ||
                Vector3.Dot(root.up,Vector3.up)<0.9999f)return links;
            var blocks=new List<Vector3>();
            foreach(var block in root.GetComponentsInChildren<DaggerfallRDBBlock>())
            {
                if(!block || !block.gameObject.activeInHierarchy || block.transform.parent!=root ||
                    Quaternion.Angle(block.transform.localRotation,Quaternion.identity)>0.01f ||
                    (block.transform.lossyScale-Vector3.one).sqrMagnitude>0.000001f)continue;
                blocks.Add(block.transform.localPosition);
            }
            if(blocks.Count<2)return links;
            var triangulation=NavMesh.CalculateTriangulation();
            var vertices=triangulation.vertices;
            for(int index=0;index<vertices.Length;index++)vertices[index]=root.InverseTransformPoint(vertices[index]);
            // Boundary extraction is shared by every seam. It avoids scanning
            // all floor triangles again for each pair of native RDB blocks.
            var edges=BoundaryEdges(vertices,triangulation.indices);
            var settings=NativeNavigationGeometry.Settings();
            var filter=new NavMeshQueryFilter {agentTypeID=settings.agentTypeID,areaMask=NavMesh.AllAreas};
            int probes=0;
            for(int a=0;a<blocks.Count;a++)for(int b=a+1;b<blocks.Count;b++)
            {
                Seam seam;
                if(!TrySeam(blocks[a],blocks[b],RDBLayout.RDBSide,out seam))continue;
                var candidates=CandidateEdges(seam,vertices,edges);
                if(candidates.Count==MaxCandidatesPerSeam)
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_RDB_SEAM_LIMIT: candidates="+MaxCandidatesPerSeam);
                var accepted=new List<Vector3>();
                foreach(var candidate in candidates)
                {
                    if(++probes>MaxProbes)
                    {
                        Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_RDB_SEAM_LIMIT: probes="+MaxProbes);
                        return links;
                    }
                    if(NearAccepted(accepted,candidate))continue;
                    var across=root.TransformDirection(seam.east?Vector3.right:Vector3.forward);
                    var middle=root.TransformPoint(candidate);
                    NavMeshHit from,to;
                    if(!NavMesh.SamplePosition(middle-across*EndpointInset,out from,SampleRange,filter) ||
                        !NavMesh.SamplePosition(middle+across*EndpointInset,out to,SampleRange,filter))continue;
                    var localFrom=root.InverseTransformPoint(from.position);
                    var localTo=root.InverseTransformPoint(to.position);
                    if(!ValidEndpoints(blocks[a],blocks[b],RDBLayout.RDBSide,candidate,localFrom,localTo) ||
                        !Supported(root,from.position,to.position,settings.agentSlope) ||
                        !Clear(from.position,to.position,settings.agentRadius,settings.agentHeight))continue;
                    var midpoint=(localFrom+localTo)*0.5f;
                    if(NearAccepted(accepted,midpoint))continue;
                    accepted.Add(midpoint);
                    links.Add(new NavMeshLinkData {startPosition=from.position,endPosition=to.position,
                        width=0,bidirectional=true,costModifier=-1,agentTypeID=settings.agentTypeID,area=0});
                }
            }
            return links;
        }

        private static bool TrySeam(Vector3 a,Vector3 b,float side,out Seam seam)
        {
            seam=default(Seam);
            if(!Finite(a) || !Finite(b) || float.IsNaN(side) || float.IsInfinity(side) || side<=0 ||
                Math.Abs(a.y-b.y)>TransformTolerance)return false;
            bool east=Math.Abs(Math.Abs(b.x-a.x)-side)<=TransformTolerance && Math.Abs(b.z-a.z)<=TransformTolerance;
            bool north=Math.Abs(Math.Abs(b.z-a.z)-side)<=TransformTolerance && Math.Abs(b.x-a.x)<=TransformTolerance;
            if(!east && !north)return false;
            if(east?b.x<a.x:b.z<a.z){var swap=a;a=b;b=swap;}
            seam=new Seam {origin=a,side=side,east=east};return true;
        }

        // Pure geometry entry points for focused CPU verification.
        internal static List<Vector3> Candidates(Vector3 a,Vector3 b,float side,Vector3[] vertices,int[] indices)
        {
            Seam seam;
            if(!TrySeam(a,b,side,out seam))return new List<Vector3>();
            return CandidateEdges(seam,vertices,BoundaryEdges(vertices,indices));
        }

        private static int[] BoundaryEdges(Vector3[] vertices,int[] indices)
        {
            if(vertices==null || indices==null)return new int[0];
            var counts=new Dictionary<long,int>();
            for(int index=0;index+2<indices.Length;index+=3)
            {
                int a=indices[index],b=indices[index+1],c=indices[index+2];
                if(a<0 || b<0 || c<0 || a>=vertices.Length || b>=vertices.Length || c>=vertices.Length ||
                    !Finite(vertices[a]) || !Finite(vertices[b]) || !Finite(vertices[c]))continue;
                CountEdge(counts,a,b);CountEdge(counts,b,c);CountEdge(counts,c,a);
            }
            var keys=new List<long>();
            foreach(var pair in counts)if(pair.Value==1)keys.Add(pair.Key);
            keys.Sort();var result=new int[keys.Count*2];
            for(int index=0;index<keys.Count;index++)
            {result[index*2]=(int)(keys[index]>>32);result[index*2+1]=(int)keys[index];}
            return result;
        }

        private static void CountEdge(Dictionary<long,int> counts,int a,int b)
        {
            if(a==b)return;
            long key=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);int count;
            counts.TryGetValue(key,out count);counts[key]=count+1;
        }

        private static List<Vector3> CandidateEdges(Seam seam,Vector3[] vertices,int[] edges)
        {
            var result=new List<Vector3>();var seen=new HashSet<long>();
            for(int index=0;index+1<edges.Length && result.Count<MaxCandidatesPerSeam;index+=2)
            {
                var a=vertices[edges[index]];var b=vertices[edges[index+1]];
                if(Math.Abs(seam.Across(a))>EdgeBand || Math.Abs(seam.Across(b))>EdgeBand)continue;
                float begin=seam.Along(a),end=seam.Along(b);
                if(Math.Max(begin,end)<0.3f || Math.Min(begin,end)>seam.side-0.3f)continue;
                float first=0,last=1,alongDelta=end-begin;
                if(Math.Abs(alongDelta)>0.0001f)
                {
                    float low=(0.3f-begin)/alongDelta,high=(seam.side-0.3f-begin)/alongDelta;
                    first=Math.Max(0,Math.Min(low,high));last=Math.Min(1,Math.Max(low,high));
                }
                if(first>last)continue;
                float heightA=a.y+(b.y-a.y)*first,heightB=a.y+(b.y-a.y)*last;
                end=begin+alongDelta*last;begin+=alongDelta*first;
                AddCandidate(result,seen,seam,(begin+end)*0.5f,(heightA+heightB)*0.5f);
                int steps=Math.Max(1,(int)Math.Ceiling(Math.Abs(end-begin)/CandidateSpacing));
                for(int step=0;step<=steps && result.Count<MaxCandidatesPerSeam;step++)
                {
                    float t=(float)step/steps;
                    AddCandidate(result,seen,seam,begin+(end-begin)*t,heightA+(heightB-heightA)*t);
                }
            }
            result.Sort((a,b)=>{int y=a.y.CompareTo(b.y);return y!=0?y:seam.Along(a).CompareTo(seam.Along(b));});
            return result;
        }

        private static void AddCandidate(List<Vector3> result,HashSet<long> seen,Seam seam,float along,float height)
        {
            if(float.IsNaN(along) || float.IsInfinity(along) || float.IsNaN(height) || float.IsInfinity(height) ||
                along<0.3f || along>seam.side-0.3f || result.Count>=MaxCandidatesPerSeam)return;
            int horizontal=(int)Math.Round(along/0.25f),vertical=(int)Math.Round(height/0.20f);
            long key=((long)vertical<<32)|(uint)horizontal;
            if(seen.Add(key))result.Add(seam.Point(along,height));
        }

        internal static bool ValidEndpoints(Vector3 a,Vector3 b,float side,Vector3 candidate,Vector3 from,Vector3 to)
        {
            Seam seam;
            if(!TrySeam(a,b,side,out seam) || !Finite(candidate) || !Finite(from) || !Finite(to))return false;
            float start=seam.Across(from),end=seam.Across(to),along=seam.Along(candidate);
            var axis=seam.east?Vector3.right:Vector3.forward;
            if(Math.Abs(seam.Across(candidate))>0.01f || along<0.3f || along>side-0.3f ||
                start>=-0.05f || end<=0.05f || start<-1.05f || end>1.05f ||
                Math.Abs(seam.Along(from)-along)>SampleRange || Math.Abs(seam.Along(to)-along)>SampleRange ||
                seam.Along(from)<0.3f || seam.Along(from)>side-0.3f ||
                seam.Along(to)<0.3f || seam.Along(to)>side-0.3f ||
                Math.Abs(from.y-candidate.y)>0.35f || Math.Abs(to.y-candidate.y)>0.35f ||
                Math.Abs(from.y-to.y)>0.5f || (to-from).sqrMagnitude>1.8f*1.8f ||
                (from-(candidate-axis*EndpointInset)).sqrMagnitude>SampleRange*SampleRange+0.00001f ||
                (to-(candidate+axis*EndpointInset)).sqrMagnitude>SampleRange*SampleRange+0.00001f)return false;
            return true;
        }

        private static bool NearAccepted(List<Vector3> accepted,Vector3 candidate)
        {
            foreach(var point in accepted)
            {
                var delta=point-candidate;
                if(Math.Abs(delta.y)<0.35f && delta.x*delta.x+delta.z*delta.z<1)return true;
            }
            return false;
        }

        private static bool Supported(Transform root,Vector3 from,Vector3 to,float slope)
        {
            int steps=Math.Max(4,(int)Math.Ceiling(Vector3.Distance(from,to)/0.2f));
            if((steps&1)!=0)steps++; // Always test the exact midpoint.
            float normal=(float)Math.Cos(slope*Math.PI/180);
            for(int step=0;step<=steps;step++)
            {
                var point=Vector3.Lerp(from,to,(float)step/steps);bool found=false;
                foreach(var hit in Physics.RaycastAll(point+Vector3.up*0.55f,Vector3.down,0.55f+FloorTolerance,
                    ~0,QueryTriggerInteraction.Ignore))
                {
                    if(!Static(hit.collider) || !hit.collider.transform.IsChildOf(root) ||
                        hit.normal.y<normal || Math.Abs(hit.point.y-point.y)>FloorTolerance)continue;
                    found=true;break;
                }
                if(!found)return false;
            }
            return true;
        }

        private static bool Clear(Vector3 from,Vector3 to,float agentRadius,float agentHeight)
        {
            float radius=agentRadius;
            var lower=Vector3.up*(agentRadius+0.12f);var upper=Vector3.up*(agentHeight-agentRadius);
            foreach(var collider in Physics.OverlapCapsule(from+lower,from+upper,radius,~0,QueryTriggerInteraction.Ignore))
                if(Static(collider))return false;
            foreach(var collider in Physics.OverlapCapsule(to+lower,to+upper,radius,~0,QueryTriggerInteraction.Ignore))
                if(Static(collider))return false;
            var delta=to-from;
            foreach(var hit in Physics.CapsuleCastAll(from+lower,from+upper,radius,delta.normalized,delta.magnitude,
                ~0,QueryTriggerInteraction.Ignore))if(Static(hit.collider))return false;
            return true;
        }

        private static bool Static(Collider collider)
        {return collider && collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger &&
            !NativeNavigationGeometry.IsDynamicNavigationBody(collider);}

        private static bool Finite(Vector3 point)
        {return !float.IsNaN(point.x) && !float.IsNaN(point.y) && !float.IsNaN(point.z) &&
            !float.IsInfinity(point.x) && !float.IsInfinity(point.y) && !float.IsInfinity(point.z);}
    }
}
