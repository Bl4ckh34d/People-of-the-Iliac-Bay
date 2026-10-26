using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcJumpTrajectoryTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Dungeon jump reaches a same-height floor on the descending arc", () => {
                NpcJumpTrajectory jump;
                Check.True(NpcJumpTrajectory.TryCreate(1, 0, 4.5, 20, 3, out jump));
                Check.Near(0.45, jump.Duration);
                Check.Near(1, jump.HorizontalSpeed * jump.Duration);
                Check.Near(0.50625, jump.HeightAt(0.225));
                Check.Near(0, jump.HeightAt(jump.Duration));
            }));
            cases.Add(("Dungeon jump uses the descending root for a raised landing", () => {
                NpcJumpTrajectory jump;
                Check.True(NpcJumpTrajectory.TryCreate(0.8, 0.3, 4.5, 20, 3, out jump));
                Check.True(jump.Duration > jump.LaunchSpeed / jump.Gravity);
                Check.Near(0.3, jump.HeightAt(jump.Duration));
            }));
            cases.Add(("Dungeon jump refuses an unreachable height and horizontal range", () => {
                NpcJumpTrajectory jump;
                Check.True(!NpcJumpTrajectory.TryCreate(0.5, 0.51, 4.5, 20, 3, out jump));
                Check.True(!NpcJumpTrajectory.TryCreate(1.36, 0, 4.5, 20, 3, out jump));
                Check.True(NpcJumpTrajectory.TryCreate(1.35, 0, 4.5, 20, 3, out jump));
                Check.True(!NpcJumpTrajectory.TryCreate(0, 0, 4.5, 20, 3, out jump));
            }));
            cases.Add(("Dungeon jump capability changes real reachable range", () => {
                NpcJumpTrajectory jump;
                Check.True(!NpcJumpTrajectory.TryCreate(1.8, 0, 4.5, 20, 3, out jump));
                Check.True(NpcJumpTrajectory.TryCreate(1.8, 0, 6.75, 20, 3, out jump));
                Check.Near(0.675, jump.Duration);
            }));
            cases.Add(("Dungeon jump lower landing extends flight without changing launch capability", () => {
                NpcJumpTrajectory jump;
                Check.True(NpcJumpTrajectory.TryCreate(1.5, -0.4, 4.5, 20, 3, out jump));
                Check.True(jump.Duration > 0.45);
                Check.Near(-0.4, jump.HeightAt(jump.Duration));
                Check.Near(4.5, jump.LaunchSpeed);
            }));
            cases.Add(("Dungeon jump rejects invalid capability and coordinates", () => {
                foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    NpcJumpTrajectory jump;
                    Check.True(!NpcJumpTrajectory.TryCreate(invalid, 0, 4.5, 20, 3, out jump));
                    Check.True(!NpcJumpTrajectory.TryCreate(1, invalid, 4.5, 20, 3, out jump));
                    Check.True(!NpcJumpTrajectory.TryCreate(1, 0, invalid, 20, 3, out jump));
                    Check.True(!NpcJumpTrajectory.TryCreate(1, 0, 4.5, invalid, 3, out jump));
                    Check.True(!NpcJumpTrajectory.TryCreate(1, 0, 4.5, 20, invalid, out jump));
                }
                foreach (double invalid in new[] { -1d, 0d })
                {
                    NpcJumpTrajectory jump;
                    Check.True(!NpcJumpTrajectory.TryCreate(1, 0, invalid, 20, 3, out jump));
                    Check.True(!NpcJumpTrajectory.TryCreate(1, 0, 4.5, invalid, 3, out jump));
                    Check.True(!NpcJumpTrajectory.TryCreate(1, 0, 4.5, 20, invalid, out jump));
                }
            }));
        }
    }
}
