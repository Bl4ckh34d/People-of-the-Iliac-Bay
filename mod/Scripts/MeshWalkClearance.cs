using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Classic model surfaces only. No Unity objects, colliders or physics-scene
    // changes. Terrain and replacement-prefab collision need separate adapters.
    public sealed class MeshWalkClearance
    {
        private sealed class Surface
        {
            public Vector3[] Vertices;
            public int[] Indices;
            public Bounds Bounds;
        }
        private const float BucketSize=16;
        private readonly Dictionary<long,List<Surface>> buckets=new Dictionary<long,List<Surface>>();
        private readonly HashSet<Surface> seen=new HashSet<Surface>();
        private static int Bucket(float value) {return Mathf.FloorToInt(value/BucketSize);}
        private static long Key(int x,int z) {return ((long)x<<32) | (uint)z;}
        public void Add(ModelData model,Matrix4x4 matrix)
        {
            if(model.Vertices==null || model.Vertices.Length==0 || model.Indices==null || model.Indices.Length==0) return;
            var vertices=new Vector3[model.Vertices.Length];
            for(int i=0;i<vertices.Length;i++) vertices[i]=matrix.MultiplyPoint3x4(model.Vertices[i]);
            var bounds=new Bounds(vertices[0],Vector3.zero);
            foreach(var point in vertices) bounds.Encapsulate(point);
            var surface=new Surface {Vertices=vertices,Indices=model.Indices,Bounds=bounds};
            for(int z=Bucket(bounds.min.z);z<=Bucket(bounds.max.z);z++)
                for(int x=Bucket(bounds.min.x);x<=Bucket(bounds.max.x);x++)
                {
                    long key=Key(x,z); List<Surface> list;
                    if(!buckets.TryGetValue(key,out list)) {list=new List<Surface>(); buckets.Add(key,list);}
                    list.Add(surface);
                }
        }
        public bool Clear(Vector3 from,Vector3 to,float radius)
        {
            if(float.IsNaN(radius) || float.IsInfinity(radius) || radius<=0) throw new ArgumentOutOfRangeException(nameof(radius));
            seen.Clear();
            var bounds=new Bounds(from,Vector3.zero); bounds.Encapsulate(to); bounds.Expand(radius*2);
            Vector3 delta=to-from; float length=delta.magnitude;
            for(int z=Bucket(bounds.min.z);z<=Bucket(bounds.max.z);z++)
                for(int x=Bucket(bounds.min.x);x<=Bucket(bounds.max.x);x++)
                {
                    List<Surface> list;
                    if(!buckets.TryGetValue(Key(x,z),out list)) continue;
                    foreach(var surface in list)
                    {
                        if(!seen.Add(surface)) continue;
                        var expanded=surface.Bounds; expanded.Expand(radius*2);
                        float distance;
                        if(!expanded.Contains(from) && (length<0.000001f ||
                            !expanded.IntersectRay(new Ray(from,delta/length),out distance) || distance>length)) continue;
                        var vertices=surface.Vertices; var indices=surface.Indices;
                        for(int i=0;i+2<indices.Length;i+=3)
                            if(SegmentTriangle(from,to,vertices[indices[i]],vertices[indices[i+1]],vertices[indices[i+2]])<=radius*radius)
                                return false;
                    }
                }
            return true;
        }
        private static float SegmentTriangle(Vector3 from,Vector3 to,Vector3 a,Vector3 b,Vector3 c)
        {
            var normal=Vector3.Cross(b-a,c-a);
            if(normal.sqrMagnitude>0.00000001f)
            {
                float denominator=Vector3.Dot(normal,to-from);
                if(Mathf.Abs(denominator)>0.00000001f)
                {
                    float t=Vector3.Dot(normal,a-from)/denominator;
                    if(t>=0 && t<=1 && PointTriangle(from+(to-from)*t,a,b,c)<0.00000001f) return 0;
                }
                float points=Mathf.Min(PointTriangle(from,a,b,c),PointTriangle(to,a,b,c));
                return Mathf.Min(points,Mathf.Min(Segments(from,to,a,b),Mathf.Min(Segments(from,to,b,c),Segments(from,to,c,a))));
            }
            return Mathf.Min(Segments(from,to,a,b),Mathf.Min(Segments(from,to,b,c),Segments(from,to,c,a)));
        }
        private static float PointTriangle(Vector3 point,Vector3 a,Vector3 b,Vector3 c)
        {
            var ab=b-a; var ac=c-a; var ap=point-a;
            float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
            if(d1<=0 && d2<=0) return ap.sqrMagnitude;
            var bp=point-b;
            float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
            if(d3>=0 && d4<=d3) return bp.sqrMagnitude;
            float vc=d1*d4-d3*d2;
            if(vc<=0 && d1>=0 && d3<=0) return (point-(a+ab*(d1/(d1-d3)))).sqrMagnitude;
            var cp=point-c;
            float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
            if(d6>=0 && d5<=d6) return cp.sqrMagnitude;
            float vb=d5*d2-d1*d6;
            if(vb<=0 && d2>=0 && d6<=0) return (point-(a+ac*(d2/(d2-d6)))).sqrMagnitude;
            float va=d3*d6-d5*d4;
            if(va<=0 && d4-d3>=0 && d5-d6>=0)
                return (point-(b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6))))).sqrMagnitude;
            float inverse=1/(va+vb+vc);
            return (point-(a+ab*(vb*inverse)+ac*(vc*inverse))).sqrMagnitude;
        }
        private static float Segments(Vector3 p,Vector3 q,Vector3 a,Vector3 b)
        {
            var d1=q-p; var d2=b-a; var r=p-a;
            float aa=Vector3.Dot(d1,d1),ee=Vector3.Dot(d2,d2),ff=Vector3.Dot(d2,r),s,t;
            if(aa<=0.00000001f && ee<=0.00000001f) return r.sqrMagnitude;
            if(aa<=0.00000001f) {s=0; t=Mathf.Clamp01(ff/ee);}
            else
            {
                float cc=Vector3.Dot(d1,r);
                if(ee<=0.00000001f) {t=0; s=Mathf.Clamp01(-cc/aa);}
                else
                {
                    float bb=Vector3.Dot(d1,d2),denominator=aa*ee-bb*bb;
                    s=denominator!=0 ? Mathf.Clamp01((bb*ff-cc*ee)/denominator) : 0;
                    t=(bb*s+ff)/ee;
                    if(t<0) {t=0; s=Mathf.Clamp01(-cc/aa);}
                    else if(t>1) {t=1; s=Mathf.Clamp01((bb-cc)/aa);}
                }
            }
            return (p+d1*s-a-d2*t).sqrMagnitude;
        }
    }
}
