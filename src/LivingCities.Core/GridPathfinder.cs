using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public struct GridCell : IEquatable<GridCell>
    {
        public readonly int X;
        public readonly int Z;
        public GridCell(int x, int z) { X = x; Z = z; }
        public bool Equals(GridCell other) { return X == other.X && Z == other.Z; }
        public override bool Equals(object obj) { return obj is GridCell && Equals((GridCell)obj); }
        public override int GetHashCode() { unchecked { return X * 397 ^ Z; } }
        public override string ToString() { return X + "," + Z; }
    }

    public interface IWalkGrid
    {
        int Width { get; }
        int Height { get; }
        int Cost(int x, int z);
    }
    public interface IWalkEdgeGrid : IWalkGrid
    {
        bool CanStep(GridCell from,GridCell to);
    }

    public static class GridPathfinder
    {
        private sealed class Entry : IComparable<Entry>
        {
            internal GridCell Cell;
            internal long Cost, Score;
            public int CompareTo(Entry other)
            {
                int order = Score.CompareTo(other.Score);
                if (order == 0) order = Cell.Z.CompareTo(other.Cell.Z);
                if (order == 0) order = Cell.X.CompareTo(other.Cell.X);
                if (order == 0) order = Cost.CompareTo(other.Cost);
                return order;
            }
        }

        public static List<GridCell> Find(IWalkGrid grid, GridCell start, GridCell end, int maxVisited)
        {
            var search=Begin(grid,start,end,maxVisited);
            while(!search.IsComplete) search.Step(int.MaxValue);
            return new List<GridCell>(search.Path);
        }

        public static Search Begin(IWalkGrid grid,GridCell start,GridCell end,int maxVisited)
        { return new Search(grid,start,end,maxVisited); }

        // Caller owns an immutable grid for the search lifetime. No partial route is
        // published; both stale frontier entries and parent tracing consume budget.
        public sealed class Search
        {
            private readonly IWalkGrid grid;
            private readonly GridCell start,end;
            private readonly int maxVisited;
            private readonly SortedSet<Entry> frontier=new SortedSet<Entry>();
            private readonly Dictionary<GridCell,long> costs=new Dictionary<GridCell,long>();
            private readonly Dictionary<GridCell,GridCell> parents=new Dictionary<GridCell,GridCell>();
            private readonly List<GridCell> result=new List<GridCell>();
            private static readonly IReadOnlyList<GridCell> empty=Array.Empty<GridCell>();
            private IReadOnlyList<GridCell> published=empty;
            private int visited;
            private bool tracing;
            private GridCell trace;
            public bool IsComplete {get;private set;}
            public int WorkDone {get;private set;}
            public IReadOnlyList<GridCell> Path {get {return published;}}
            internal Search(IWalkGrid grid,GridCell start,GridCell end,int maxVisited)
            {
                if(grid==null) throw new ArgumentNullException(nameof(grid));
                if(maxVisited<1) throw new ArgumentOutOfRangeException(nameof(maxVisited));
                this.grid=grid; this.start=start; this.end=end; this.maxVisited=maxVisited;
                if(!Inside(grid,start) || !Inside(grid,end) || grid.Cost(start.X,start.Z)<=0 || grid.Cost(end.X,end.Z)<=0)
                {IsComplete=true; return;}
                costs[start]=0;
                frontier.Add(new Entry {Cell=start,Score=Distance(start,end)});
            }
            public void Step(int budget)
            {
                if(budget<1) throw new ArgumentOutOfRangeException(nameof(budget));
                for(int work=0;work<budget && !IsComplete;work++)
                {
                    if(tracing)
                    {
                        WorkDone++;
                        result.Add(trace);
                        if(trace.Equals(start)) {result.Reverse(); published=result.AsReadOnly(); IsComplete=true;}
                        else trace=parents[trace];
                        continue;
                    }
                    if(frontier.Count==0 || visited>=maxVisited) {IsComplete=true; break;}
                    WorkDone++;
                    Entry next=frontier.Min; frontier.Remove(next);
                    if(costs[next.Cell]!=next.Cost) continue;
                    visited++;
                    if(next.Cell.Equals(end)) {tracing=true; trace=end; continue;}
                    Visit(next,new GridCell(next.Cell.X,next.Cell.Z-1));
                    Visit(next,new GridCell(next.Cell.X-1,next.Cell.Z));
                    Visit(next,new GridCell(next.Cell.X+1,next.Cell.Z));
                    Visit(next,new GridCell(next.Cell.X,next.Cell.Z+1));
                }
            }
            private void Visit(Entry next,GridCell neighbor)
            {
                if(!Inside(grid,neighbor)) return;
                int step=grid.Cost(neighbor.X,neighbor.Z);
                if(step<=0) return;
                var edges=grid as IWalkEdgeGrid;
                if(edges!=null && !edges.CanStep(next.Cell,neighbor)) return;
                long cost=next.Cost+step,previous;
                if(costs.TryGetValue(neighbor,out previous) && previous<=cost) return;
                costs[neighbor]=cost; parents[neighbor]=next.Cell;
                frontier.Add(new Entry {Cell=neighbor,Cost=cost,Score=cost+Distance(neighbor,end)});
            }
        }

        private static bool Inside(IWalkGrid grid, GridCell cell) { return cell.X >= 0 && cell.Z >= 0 && cell.X < grid.Width && cell.Z < grid.Height; }
        private static long Distance(GridCell a, GridCell b) { return Math.Abs((long)a.X - b.X) + Math.Abs((long)a.Z - b.Z); }
    }
}
