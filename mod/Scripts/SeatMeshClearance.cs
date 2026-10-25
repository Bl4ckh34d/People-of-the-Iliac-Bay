using System.Collections.Generic;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Combined native meshes cannot be ignored as one collider. Cache only local
    // triangles, removing triangles whose three vertices belong to this furniture.
    internal sealed class SeatMeshClearance
    {
        // Reject oversized combined meshes before copying their CPU-side data.
        // Clearance is deliberately fail-closed when a mesh exceeds either cap.
        internal const int MaxSourceTriangles=65536;
        internal const int MaxObstacleTriangles=1024;
        private struct Triangle {internal Vector3 A,B,C;}
        private readonly List<Triangle> obstacles=new List<Triangle>();
        private readonly HashSet<int> seatTriangles=new HashSet<int>();
        internal bool IsSeatTriangle(int index) {return seatTriangles.Contains(index);}
        internal bool HasSeatTriangles {get;private set;}
        internal bool Supported {get;private set;}
        internal SeatMeshClearance(MeshCollider collider,SeatFurniture furniture)
        {
            var source=collider.sharedMesh;
            if(!source||!source.isReadable)return;
            long indexCount=0;
            for(int submesh=0;submesh<source.subMeshCount;submesh++)
            {
                indexCount+=source.GetIndexCount(submesh);
                if(indexCount>MaxSourceTriangles*3L)return;
            }
            if(indexCount%3!=0||indexCount/3>MaxSourceTriangles)return;
            var vertices=source.vertices;var indices=source.triangles;
            var world=new Vector3[vertices.Length];for(int i=0;i<world.Length;i++)world[i]=collider.transform.TransformPoint(vertices[i]);
            var area=new Bounds(furniture.Matrix.MultiplyPoint3x4(furniture.Bounds.center),Vector3.zero);
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                area.Encapsulate(furniture.Matrix.MultiplyPoint3x4(furniture.Bounds.center+Vector3.Scale(furniture.Bounds.extents,new Vector3(x,y,z))));
            area.Expand(3.6f);
            for(int i=0;i+2<indices.Length;i+=3)
            {
                var a=world[indices[i]];var b=world[indices[i+1]];var c=world[indices[i+2]];
                if(furniture.Vertices.Contains(SeatFurniture.Key(a))&&furniture.Vertices.Contains(SeatFurniture.Key(b))&&furniture.Vertices.Contains(SeatFurniture.Key(c)))
                {HasSeatTriangles=true;seatTriangles.Add(i/3);continue;}
                var box=new Bounds(a,Vector3.zero);box.Encapsulate(b);box.Encapsulate(c);
                if(box.Intersects(area))
                {
                    if(obstacles.Count>=MaxObstacleTriangles)
                    {obstacles.Clear();seatTriangles.Clear();HasSeatTriangles=false;return;}
                    obstacles.Add(new Triangle {A=a,B=b,C=c});
                }
            }
            Supported=true;
        }
        internal bool Clear(Vector3 from,Vector3 to,float radius)
        {
            if(!Supported||!HasSeatTriangles)return false;
            // Conservative sphere samples cover the capsule and its entire short
            // sweep. Inflation covers the largest gap between horizontal and
            // vertical sample axes.
            int travel=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to)/0.08f));
            float bottom=0.51f+radius,top=1.65f;
            int vertical=Mathf.Max(1,Mathf.CeilToInt((top-bottom)/0.08f));float inflated=radius+0.057f;
            for(int step=0;step<=travel;step++)for(int height=0;height<=vertical;height++)
            {
                var p=Vector3.Lerp(from,to,(float)step/travel)+Vector3.up*Mathf.Lerp(bottom,top,(float)height/vertical);
                foreach(var triangle in obstacles)if((Closest(p,triangle.A,triangle.B,triangle.C)-p).sqrMagnitude<inflated*inflated)return false;
            }
            return true;
        }
        internal bool ClearSeatTransition(Vector3 from,Vector3 to)
        {
            if(!Supported||!HasSeatTriangles)return false;
            // During the short reserved-seat blend the standing capsule is off.
            // Retain a chest-height wall check, without treating the adjacent
            // tabletop as a standing-body collision inside the seated pose.
            int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to)/0.08f));
            for(int i=0;i<=steps;i++)
            {
                var point=Vector3.Lerp(from,to,(float)i/steps)+Vector3.up*1.35f;
                foreach(var triangle in obstacles)
                    if((Closest(point,triangle.A,triangle.B,triangle.C)-point).sqrMagnitude<0.01f)return false;
            }
            return true;
        }
        private static Vector3 Closest(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            var ab=b-a;var ac=c-a;var ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
            if(d1<=0&&d2<=0)return a;
            var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);if(d3>=0&&d4<=d3)return b;
            float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
            var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);if(d6>=0&&d5<=d6)return c;
            float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
            float va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6)));
            float denominator=va+vb+vc;if(Mathf.Abs(denominator)<0.0000001f)return a;
            return a+ab*(vb/denominator)+ac*(vc/denominator);
        }
    }
}
