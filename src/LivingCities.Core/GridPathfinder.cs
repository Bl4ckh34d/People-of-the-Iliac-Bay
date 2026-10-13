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
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (maxVisited < 1) throw new ArgumentOutOfRangeException(nameof(maxVisited));
            var empty = new List<GridCell>();
            if (!Inside(grid, start) || !Inside(grid, end) || grid.Cost(start.X, start.Z) <= 0 || grid.Cost(end.X, end.Z) <= 0)
                return empty;
            var frontier = new SortedSet<Entry>();
            var costs = new Dictionary<GridCell, long> { [start] = 0 };
            var parents = new Dictionary<GridCell, GridCell>();
            frontier.Add(new Entry { Cell = start, Score = Distance(start, end) });
            int visited = 0;
            while (frontier.Count > 0 && visited < maxVisited)
            {
                Entry next = frontier.Min;
                frontier.Remove(next);
                if (costs[next.Cell] != next.Cost) continue;
                visited++;
                if (next.Cell.Equals(end))
                {
                    var path = new List<GridCell> { end };
                    GridCell cell = end;
                    while (!cell.Equals(start)) { cell = parents[cell]; path.Add(cell); }
                    path.Reverse();
                    return path;
                }
                var neighbors = new[] {
                    new GridCell(next.Cell.X, next.Cell.Z - 1), new GridCell(next.Cell.X - 1, next.Cell.Z),
                    new GridCell(next.Cell.X + 1, next.Cell.Z), new GridCell(next.Cell.X, next.Cell.Z + 1) };
                foreach (GridCell neighbor in neighbors)
                {
                    if (!Inside(grid, neighbor)) continue;
                    int step = grid.Cost(neighbor.X, neighbor.Z);
                    if (step <= 0) continue;
                    long cost = next.Cost + step;
                    long previous;
                    if (costs.TryGetValue(neighbor, out previous) && previous <= cost) continue;
                    costs[neighbor] = cost;
                    parents[neighbor] = next.Cell;
                    frontier.Add(new Entry { Cell = neighbor, Cost = cost, Score = cost + Distance(neighbor, end) });
                }
            }
            return empty;
        }

        private static bool Inside(IWalkGrid grid, GridCell cell) { return cell.X >= 0 && cell.Z >= 0 && cell.X < grid.Width && cell.Z < grid.Height; }
        private static long Distance(GridCell a, GridCell b) { return Math.Abs((long)a.X - b.X) + Math.Abs((long)a.Z - b.Z); }
    }
}
