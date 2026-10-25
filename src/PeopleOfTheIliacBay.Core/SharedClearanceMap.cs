using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // Positive cells certify the ENTIRE closed square for the caller's body size.
    // False means uncertain, not impassable: narrow passages use an exact proof.
    // Coordinates are town-local. No Unity state, NPC identity or save data here.
    public sealed class SharedClearanceMap
    {
        private readonly double size;
        private readonly int capacity;
        private readonly Func<int,int,bool> survey;
        private readonly Func<RoutePoint,RoutePoint,bool> exact;
        private sealed class Page {public byte[] Cells;public int Used;}
        private readonly int pageShift,pageSide,pageMask,maximumPages;
        private readonly Dictionary<GridCell,Page> cells=new Dictionary<GridCell,Page>();
        private readonly Queue<GridCell> cellOrder=new Queue<GridCell>();
        private readonly Dictionary<Segment,bool> segments=new Dictionary<Segment,bool>();
        private readonly Queue<Segment> segmentOrder=new Queue<Segment>();
        public long Surveys {get;private set;}
        public long Hits {get;private set;}
        public long ExactChecks {get;private set;}
        public int CellCount {get;private set;}
        public int PageCount {get{return cells.Count;}}
        public int SegmentCount {get{return segments.Count;}}
        public SharedClearanceMap(double cellSize,int capacity,Func<int,int,bool> survey,Func<RoutePoint,RoutePoint,bool> exact)
        {
            if(!Finite(cellSize)||cellSize<=0||capacity<1||survey==null||exact==null)throw new ArgumentException();
            size=cellSize;this.capacity=capacity;this.survey=survey;this.exact=exact;
            pageShift=capacity<64?0:3;pageSide=1<<pageShift;pageMask=pageSide-1;
            maximumPages=Math.Max(1,capacity/(pageSide*pageSide));
        }
        public bool Clear(RoutePoint a,RoutePoint b)
        {
            if(!Valid(a)||!Valid(b))return false;
            double distanceX=a.X-b.X,distanceZ=a.Z-b.Z;
            if(distanceX*distanceX+distanceZ*distanceZ>24.01*24.01)return false;
            int x=(int)Math.Floor(a.X/size),z=(int)Math.Floor(a.Z/size);
            int endX=(int)Math.Floor(b.X/size),endZ=(int)Math.Floor(b.Z/size);
            int sx=Math.Sign(b.X-a.X),sz=Math.Sign(b.Z-a.Z);
            double dx=b.X-a.X,dz=b.Z-a.Z;
            double tx=sx==0?double.PositiveInfinity:((x+(sx>0?1:0))*size-a.X)/dx;
            double tz=sz==0?double.PositiveInfinity:((z+(sz>0?1:0))*size-a.Z)/dz;
            double stepX=sx==0?double.PositiveInfinity:Math.Abs(size/dx);
            double stepZ=sz==0?double.PositiveInfinity:Math.Abs(size/dz);
            int budget=Math.Abs(endX-x)+Math.Abs(endZ-z)+2;
            if(budget>4096)return false;
            while(budget-->0)
            {
                if(!Cell(x,z))return Exact(a,b);
                if(x==endX&&z==endZ)return true;
                if(tx<tz){x+=sx;tx+=stepX;}
                else if(tz<tx){z+=sz;tz+=stepZ;}
                else {x+=sx;z+=sz;tx+=stepX;tz+=stepZ;}
            }
            return Exact(a,b);
        }
        private bool Cell(int x,int z)
        {
            var key=new GridCell(x>>pageShift,z>>pageShift);Page page;
            int index=(z&pageMask)*pageSide+(x&pageMask);
            if(cells.TryGetValue(key,out page)&&page.Cells[index]!=0){Hits++;return page.Cells[index]==1;}
            bool clear=survey(x,z);Surveys++;
            if(page==null)
            {
                while(cells.Count>=maximumPages)
                {var old=cellOrder.Dequeue();CellCount-=cells[old].Used;cells.Remove(old);}
                page=new Page{Cells=new byte[pageSide*pageSide]};cells.Add(key,page);cellOrder.Enqueue(key);
            }
            page.Cells[index]=(byte)(clear?1:2);page.Used++;CellCount++;return clear;
        }
        private bool Exact(RoutePoint a,RoutePoint b)
        {
            var key=new Segment(a,b);bool clear;
            if(segments.TryGetValue(key,out clear)){Hits++;return clear;}
            ExactChecks++;clear=exact(a,b);
            while(segments.Count>=Math.Min(capacity,32768))segments.Remove(segmentOrder.Dequeue());
            segments.Add(key,clear);segmentOrder.Enqueue(key);return clear;
        }
        public void Invalidate(double minX,double minZ,double maxX,double maxZ)
        {
            // Rebuild FIFO queues to avoid stale keys removing newer incarnations.
            var kept=new Queue<GridCell>();
            foreach(var key in cellOrder)
                if((key.X+1)*pageSide*size>=minX&&key.X*pageSide*size<=maxX&&
                    (key.Z+1)*pageSide*size>=minZ&&key.Z*pageSide*size<=maxZ)
                {
                    var page=cells[key];
                    for(int z=0;z<pageSide;z++)for(int x=0;x<pageSide;x++)
                    {
                        double cx=(key.X*pageSide+x)*size,cz=(key.Z*pageSide+z)*size;
                        int index=z*pageSide+x;
                        if(page.Cells[index]!=0&&cx+size>=minX&&cx<=maxX&&cz+size>=minZ&&cz<=maxZ)
                        {page.Cells[index]=0;page.Used--;CellCount--;}
                    }
                    if(page.Used==0)cells.Remove(key);else kept.Enqueue(key);
                }
                else kept.Enqueue(key);
            cellOrder.Clear();foreach(var key in kept)cellOrder.Enqueue(key);
            var routes=new Queue<Segment>();
            foreach(var key in segmentOrder)
                if(key.Intersects(minX,minZ,maxX,maxZ))segments.Remove(key);else routes.Enqueue(key);
            segmentOrder.Clear();foreach(var key in routes)segmentOrder.Enqueue(key);
        }
        public void Reset(){cells.Clear();cellOrder.Clear();segments.Clear();segmentOrder.Clear();CellCount=0;}
        private static bool Valid(RoutePoint p){return Finite(p.X)&&Finite(p.Z)&&Math.Abs(p.X)<1000000&&Math.Abs(p.Z)<1000000;}
        private static bool Finite(double x){return !double.IsNaN(x)&&!double.IsInfinity(x);}
        private struct Segment:IEquatable<Segment>
        {
            private readonly double ax,az,bx,bz;
            public Segment(RoutePoint a,RoutePoint b)
            {
                if(a.X>b.X||a.X==b.X&&a.Z>b.Z){var swap=a;a=b;b=swap;}
                ax=a.X;az=a.Z;bx=b.X;bz=b.Z;
            }
            public bool Equals(Segment s){return ax==s.ax&&az==s.az&&bx==s.bx&&bz==s.bz;}
            public override bool Equals(object value){return value is Segment&&Equals((Segment)value);}
            public override int GetHashCode(){unchecked{return ((ax.GetHashCode()*397^az.GetHashCode())*397^bx.GetHashCode())*397^bz.GetHashCode();}}
            public bool Intersects(double x0,double z0,double x1,double z1)
            {return Math.Max(ax,bx)>=x0&&Math.Min(ax,bx)<=x1&&Math.Max(az,bz)>=z0&&Math.Min(az,bz)<=z1;}
        }
    }
}
