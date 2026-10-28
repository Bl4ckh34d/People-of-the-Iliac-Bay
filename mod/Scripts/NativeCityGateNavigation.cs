using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Exact city captures can contain closed gate leaves. Keep those wall meshes,
    // and connect only a physically open native aperture at runtime.
    internal sealed class NativeCityGateNavigation : IDisposable
    {
        internal struct Aperture
        {
            internal Vector3 Middle,Inward,Axis;
            internal float HalfDepth,HalfOpening;
            internal Vector3 Inner {get{return Middle+Inward*(HalfDepth+2);}}
            internal Vector3 Outer {get{return Middle-Inward*(HalfDepth+2);}}
        }
        private sealed class Passage
        {
            internal DaggerfallCityGate Gate;
            internal Aperture Geometry;
            internal NavMeshLinkInstance Link;
            internal Mesh Mesh;
            internal bool Open;
        }
        private readonly List<Passage> passages=new List<Passage>();
        private readonly Collider[] stepOverlaps=new Collider[16];
        private readonly RaycastHit[] stepHits=new RaycastHit[16];
        internal bool ClearPath(Vector3[] corners)
        {
            Refresh();
            foreach(var passage in passages)
            {
                if(passage.Open || !passage.Gate)continue;
                var geometry=passage.Geometry;
                var middle=geometry.Middle;middle.y=passage.Gate.transform.position.y+1;
                for(int i=1;i<corners.Length;i++)
                {
                    var from=corners[i-1]-middle;var to=corners[i]-middle;
                    float enter=0,exit=1;
                    if(Clip(Vector3.Dot(from,geometry.Inward),Vector3.Dot(to,geometry.Inward),geometry.HalfDepth+0.3f,ref enter,ref exit) &&
                        Clip(Vector3.Dot(from,geometry.Axis),Vector3.Dot(to,geometry.Axis),geometry.HalfOpening+0.3f,ref enter,ref exit) &&
                        Clip(from.y,to.y,1.3f,ref enter,ref exit))return false;
                }
            }
            return true;
        }
        private static bool Clip(float from,float to,float halfExtent,ref float enter,ref float exit)
        {
            float delta=to-from;
            if(Mathf.Abs(delta)<0.00001f)return Mathf.Abs(from)<=halfExtent;
            float first=(-halfExtent-from)/delta,last=(halfExtent-from)/delta;
            if(first>last){float swap=first;first=last;last=swap;}
            enter=Mathf.Max(enter,first);exit=Mathf.Min(exit,last);
            return enter<=exit;
        }
        internal bool ClearStep(Vector3 from,Vector3 to)
        {
            bool near=false;
            foreach(var passage in passages)
            {
                var offset=from-passage.Geometry.Middle;
                if(Mathf.Abs(Vector3.Dot(offset,passage.Geometry.Inward))<=passage.Geometry.HalfDepth+1 &&
                    Mathf.Abs(Vector3.Dot(offset,passage.Geometry.Axis))<=passage.Geometry.HalfOpening+1)
                {near=true;break;}
            }
            if(!near)return true;
            int count=Physics.OverlapCapsuleNonAlloc(to+Vector3.up*0.35f,to+Vector3.up*1.6f,0.3f,
                stepOverlaps,~0,QueryTriggerInteraction.Ignore);
            if(count==stepOverlaps.Length)return false;
            for(int i=0;i<count;i++)
                if(stepOverlaps[i] && stepOverlaps[i].GetComponentInParent<DaggerfallCityGate>())return false;
            var delta=to-from;
            count=Physics.CapsuleCastNonAlloc(from+Vector3.up*0.35f,from+Vector3.up*1.6f,0.3f,
                delta.normalized,stepHits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
            if(count==stepHits.Length)return false;
            for(int i=0;i<count;i++)
                if(stepHits[i].collider && stepHits[i].collider.GetComponentInParent<DaggerfallCityGate>())return false;
            return true;
        }
        internal NativeCityGateNavigation(Transform root,Vector3 cityCenter)
        {
            foreach(var gate in root.GetComponentsInChildren<DaggerfallCityGate>())
            {
                Aperture geometry;
                if(TryAperture(gate,cityCenter,out geometry))
                    passages.Add(new Passage {Gate=gate,Geometry=geometry});
            }
            Refresh();
        }
        internal static bool TryAperture(DaggerfallCityGate gate,Vector3 cityCenter,out Aperture result)
        {
            result=default(Aperture);var filter=gate?gate.GetComponent<MeshFilter>():null;
            if(!filter || !filter.sharedMesh)return false;
            var bounds=filter.sharedMesh.bounds;
            bool alongX=bounds.size.x<bounds.size.z;
            bool nativeFrame=Mathf.Abs(bounds.size.x-25.6f)<0.05f && Mathf.Abs(bounds.size.z-9.6f)<0.05f;
            var middle=bounds.center;
            if(nativeFrame)middle.x=-3.1875f;
            result.Middle=gate.transform.TransformPoint(middle);
            result.Inward=gate.transform.TransformDirection(alongX?Vector3.right:Vector3.forward);
            result.Inward.y=0;result.Inward.Normalize();
            if(Vector3.Dot(result.Inward,cityCenter-result.Middle)<0)result.Inward=-result.Inward;
            result.Axis=gate.transform.TransformDirection(alongX?Vector3.forward:Vector3.right);
            result.Axis.y=0;result.Axis.Normalize();
            result.HalfDepth=alongX?bounds.extents.x*Mathf.Abs(gate.transform.lossyScale.x):bounds.extents.z*Mathf.Abs(gate.transform.lossyScale.z);
            result.HalfOpening=nativeFrame?4.2625f*Mathf.Abs(gate.transform.lossyScale.x):
                alongX?bounds.extents.z*Mathf.Abs(gate.transform.lossyScale.z):bounds.extents.x*Mathf.Abs(gate.transform.lossyScale.x);
            return result.Inward.sqrMagnitude>0.9f && result.Axis.sqrMagnitude>0.9f;
        }
        internal void Refresh()
        {
            foreach(var passage in passages)
            {
                var filter=passage.Gate?passage.Gate.GetComponent<MeshFilter>():null;
                var mesh=filter?filter.sharedMesh:null;
                if(mesh==passage.Mesh)continue;
                passage.Mesh=mesh;
                passage.Open=false;
                if(passage.Link.valid)passage.Link.Remove();
                if(!mesh)continue;
                NavMeshHit inner,outer;
                var geometry=passage.Geometry;
                // The gate model's origin is its native ground level, not bounds.center.y.
                var a=geometry.Inner;var b=geometry.Outer;
                a.y=b.y=passage.Gate.transform.position.y;
                if(!NavMesh.SamplePosition(a,out inner,0.5f,NavMesh.AllAreas) ||
                    !NavMesh.SamplePosition(b,out outer,0.5f,NavMesh.AllAreas) ||
                    !Clear(inner.position,outer.position))continue;
                passage.Open=true;
                passage.Link=NavMesh.AddLink(new NavMeshLinkData {
                    startPosition=inner.position,endPosition=outer.position,width=0,bidirectional=true,
                    costModifier=-1,agentTypeID=NativeNavigationGeometry.Settings().agentTypeID,area=0});
            }
        }
        internal static bool Clear(Vector3 from,Vector3 to)
        {
            var delta=to-from;
            foreach(var endpoint in new[]{from,to})
                foreach(var collider in Physics.OverlapCapsule(endpoint+Vector3.up*0.35f,endpoint+Vector3.up*1.6f,
                    0.3f,~0,QueryTriggerInteraction.Ignore))
                    if(Blocks(collider))return false;
            foreach(var hit in Physics.CapsuleCastAll(from+Vector3.up*0.35f,from+Vector3.up*1.6f,0.3f,
                delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                if(Blocks(hit.collider))return false;
            return true;
        }
        private static bool Blocks(Collider collider)
        {
            return collider && !(collider is TerrainCollider) &&
                !collider.GetComponentInParent<DaggerfallEntityBehaviour>() &&
                !collider.GetComponentInParent<MobilePersonNPC>() && !collider.GetComponentInParent<CrowdMotion>() &&
                !collider.GetComponentInParent<StaticNPC>() && !collider.GetComponentInParent<PlayerMotor>();
        }
        public void Dispose()
        {foreach(var passage in passages)if(passage.Link.valid)passage.Link.Remove();passages.Clear();}
    }
}
