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
        private sealed class EdgeGrid : IWalkEdgeGrid
        {
            public int Width {get {return 3;}}
            public int Height {get {return 2;}}
            public int Cost(int x,int z) {return 1;}
            public bool CanStep(GridCell from,GridCell to)
            {return !(from.Z==0 && to.Z==0 && Math.Min(from.X,to.X)==0 && Math.Max(from.X,to.X)==1);}
        }
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Walkable endpoints cannot cross a blocked edge", () =>
            {
                var grid=new EdgeGrid();
                var path=GridPathfinder.Find(grid,new GridCell(0,0),new GridCell(2,0),100);
                Check.Equal(5,path.Count);
                for(int i=1;i<path.Count;i++) Check.True(grid.CanStep(path[i-1],path[i]));
                var search=GridPathfinder.Begin(grid,new GridCell(2,0),new GridCell(0,0),100);
                while(!search.IsComplete) search.Step(1);
                Check.Equal(5,search.Path.Count);
                for(int i=1;i<search.Path.Count;i++) Check.True(grid.CanStep(search.Path[i-1],search.Path[i]));
            }));
            cases.Add(("Background work starts stay separated after stalls", () =>
            {
                var gate=new WorkStartGate(0.1);
                Check.True(gate.TryStart(0));
                Check.True(!gate.TryStart(0));
                Check.True(!gate.TryStart(0.099));
                Check.True(gate.TryStart(0.1));
                Check.True(gate.TryStart(100));
                Check.True(!gate.TryStart(100));
                Check.True(!gate.TryStart(100.099));
                Check.True(gate.TryStart(100.1));
                Check.Throws<ArgumentOutOfRangeException>(()=>gate.TryStart(double.NaN));
                Check.Throws<ArgumentOutOfRangeException>(()=>new WorkStartGate(0));
            }));
            cases.Add(("Sliced grid search preserves route and bounds each advance", () =>
            {
                var grid=new TestGrid("111111", "190091", "111111");
                var expected=GridPathfinder.Find(grid,new GridCell(0,1),new GridCell(5,1),100);
                foreach(int budget in new[]{1,2,7})
                {
                    var search=GridPathfinder.Begin(grid,new GridCell(0,1),new GridCell(5,1),100);
                    int calls=0;
                    while(!search.IsComplete)
                    {
                        Check.Equal(0,search.Path.Count);
                        int before=search.WorkDone;
                        search.Step(budget);
                        Check.True(search.WorkDone-before<=budget);
                        Check.True(++calls<100);
                    }
                    Check.Equal(string.Join("|",expected),string.Join("|",search.Path));
                    int done=search.WorkDone; search.Step(1); Check.Equal(done,search.WorkDone);
                }
            }));
            cases.Add(("Sliced grid search rejects partial exhausted paths", () =>
            {
                var search=GridPathfinder.Begin(new TestGrid("11111"),new GridCell(0,0),new GridCell(4,0),2);
                while(!search.IsComplete) search.Step(1);
                Check.Equal(0,search.Path.Count);
                Check.Throws<ArgumentOutOfRangeException>(()=>search.Step(0));
            }));
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
