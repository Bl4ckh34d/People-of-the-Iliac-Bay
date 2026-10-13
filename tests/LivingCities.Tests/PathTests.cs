using System;
using System.Collections.Generic;
using System.Linq;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal sealed class TestGrid : IWalkGrid
    {
        private readonly string[] rows;
        public TestGrid(params string[] rows) { this.rows = rows; }
        public int Width => rows[0].Length;
        public int Height => rows.Length;
        public int Cost(int x, int z) => rows[z][x] - '0';
    }

    internal static class PathTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Routes go around solid obstacles", () =>
            {
                var grid = new TestGrid("111", "001", "111");
                var path = GridPathfinder.Find(grid, new GridCell(0, 0), new GridCell(0, 2), 100);
                Check.Equal(7, path.Count);
                Check.Equal(new GridCell(0, 0), path[0]);
                Check.Equal(new GridCell(0, 2), path[path.Count - 1]);
                Check.True(path.All(c => grid.Cost(c.X, c.Z) > 0));
                for (int i = 1; i < path.Count; i++)
                    Check.Equal(1, Math.Abs(path[i].X - path[i - 1].X) + Math.Abs(path[i].Z - path[i - 1].Z));
            }));
            cases.Add(("Blocked destinations do not produce teleport routes", () =>
                Check.Equal(0, GridPathfinder.Find(new TestGrid("111", "000", "111"), new GridCell(0, 0), new GridCell(0, 2), 100).Count)));
            cases.Add(("Routes favor cheaper roads over costly shortcuts", () =>
            {
                var grid = new TestGrid("11111", "19991", "11111");
                var path = GridPathfinder.Find(grid, new GridCell(0, 1), new GridCell(4, 1), 100);
                Check.Equal(7, path.Count);
                Check.True(path.All(c => grid.Cost(c.X, c.Z) == 1));
            }));
            cases.Add(("Already at destination returns a one-cell route", () =>
                Check.Equal(1, GridPathfinder.Find(new TestGrid("1"), new GridCell(0, 0), new GridCell(0, 0), 1).Count)));
            cases.Add(("Search budget cannot yield an incomplete route", () =>
                Check.Equal(0, GridPathfinder.Find(new TestGrid("111"), new GridCell(0, 0), new GridCell(2, 0), 1).Count)));
            cases.Add(("Routes cannot cut diagonally through corners", () =>
                Check.Equal(0, GridPathfinder.Find(new TestGrid("10", "01"), new GridCell(0, 0), new GridCell(1, 1), 10).Count)));
            cases.Add(("Out-of-bounds endpoints return no route", () =>
            {
                var grid = new TestGrid("11");
                Check.Equal(0, GridPathfinder.Find(grid, new GridCell(-1, 0), new GridCell(1, 0), 10).Count);
                Check.Equal(0, GridPathfinder.Find(grid, new GridCell(0, 0), new GridCell(2, 0), 10).Count);
            }));
            cases.Add(("Blocked starting cells return no route", () =>
                Check.Equal(0, GridPathfinder.Find(new TestGrid("01"), new GridCell(0, 0), new GridCell(1, 0), 10).Count)));
            cases.Add(("Equal-cost routes are reproducible", () =>
            {
                var grid = new TestGrid("111", "111", "111");
                var a = GridPathfinder.Find(grid, new GridCell(0, 0), new GridCell(2, 2), 100);
                var b = GridPathfinder.Find(grid, new GridCell(0, 0), new GridCell(2, 2), 100);
                Check.Equal(5, a.Count);
                Check.Equal(string.Join("|", a), string.Join("|", b));
            }));
            cases.Add(("Invalid search budgets are rejected", () =>
                Check.Throws<ArgumentOutOfRangeException>(() => GridPathfinder.Find(new TestGrid("1"), new GridCell(0, 0), new GridCell(0, 0), 0))));
        }
    }
}
