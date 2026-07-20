using System.Collections.Generic;
using System.Numerics;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    public class GridPathfinderTests
    {
        // --- R2: straight, unobstructed path ---

        [Fact]
        public void FlatGround_UnobstructedPath_ReturnsDirectWaypointLine()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);

            var start = new Vector3(0, 0, 0);
            var goal = new Vector3(5, 0, 0);

            var result = pathfinder.FindPath(start, goal);

            Assert.True(result.Found);
            Assert.NotEmpty(result.Waypoints);
            Assert.Equal(new Vector3(0, 0, 0), result.Waypoints[0]);
            Assert.Equal(new Vector3(5, 0, 0), result.Waypoints[result.Waypoints.Count - 1]);

            // "Direct line": z never deviates, x strictly increases toward the goal.
            for (int i = 1; i < result.Waypoints.Count; i++)
            {
                Assert.Equal(0f, result.Waypoints[i].Z);
                Assert.True(result.Waypoints[i].X > result.Waypoints[i - 1].X);
            }
        }

        // --- R2: solid wall obstacle ---

        [Fact]
        public void WallOfSolidBlocks_DetoursAround_NeverRoutesThroughSolidColumn()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            // Wall at x=3 spanning z=-5..5; start/goal sit on either side, so
            // the only way through is around one end of the wall.
            for (int z = -5; z <= 5; z++)
                sampler.SetSolid(3, z);

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var start = new Vector3(0, 0, 0);
            var goal = new Vector3(6, 0, 0);

            var result = pathfinder.FindPath(start, goal);

            Assert.True(result.Found);
            Assert.All(result.Waypoints, wp => Assert.False(sampler.IsSolidAt((int)wp.X, (int)wp.Z)));
        }

        // --- R2: player-placed obstacle, distinct from natural solidity ---

        [Fact]
        public void PlayerPlacedObstacle_RoutedAroundSameAsSolidBlock()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            for (int z = -5; z <= 5; z++)
                sampler.SetObstacle(3, z);

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var start = new Vector3(0, 0, 0);
            var goal = new Vector3(6, 0, 0);

            var result = pathfinder.FindPath(start, goal);

            Assert.True(result.Found);
            Assert.All(result.Waypoints, wp => Assert.False(sampler.IsObstacleAt((int)wp.X, (int)wp.Z)));
        }

        // --- Endpoint obstacle exemption: the mover itself occupies the start column,
        // and the return-to-dock leg targets the column the dock WorldObject occupies.
        // Live-world queries report both as obstructed; the pathfinder must still path
        // out of the start and into the goal (solidity remains a hard failure). Without
        // this, every dispatch and every return-to-dock fails immediately with no-path.

        [Fact]
        public void ObstacleAtStartColumn_SelfDetection_StillFindsPath()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            sampler.SetObstacle(0, 0); // the drone itself, seen by the world query

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var result = pathfinder.FindPath(new Vector3(0, 0, 0), new Vector3(4, 0, 0));

            Assert.True(result.Found);
        }

        [Fact]
        public void ObstacleAtGoalColumn_DockOccupiesTarget_StillFindsPath_WhenCallerDeclaresIt()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            sampler.SetObstacle(4, 0); // the home dock occupying the return target

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var result = pathfinder.FindPath(new Vector3(0, 0, 0), new Vector3(4, 0, 0), exemptGoalObstacle: true);

            Assert.True(result.Found);
            Assert.Equal(4f, result.Waypoints[^1].X);
        }

        // The goal exemption is opt-in precisely so a roam hop cannot end inside another
        // player's object: the drone would park on top of it and, with its own column
        // then reporting obstructed, have no way to pick a route back out.
        [Fact]
        public void ObstacleAtGoalColumn_UndeclaredByCaller_ReturnsNoPath()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            sampler.SetObstacle(4, 0); // some other player's object at the roam target

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var result = pathfinder.FindPath(new Vector3(0, 0, 0), new Vector3(4, 0, 0));

            Assert.False(result.Found);
        }

        [Fact]
        public void ObstacleAtBothEndpoints_DeclaredGoal_StillFindsPath()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            sampler.SetObstacle(0, 0); // the drone itself
            sampler.SetObstacle(4, 0); // the dock it is returning to

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var result = pathfinder.FindPath(new Vector3(0, 0, 0), new Vector3(4, 0, 0), exemptGoalObstacle: true);

            Assert.True(result.Found);
        }

        [Fact]
        public void StartEqualsGoal_ObstacleOnThatColumn_StillArrives()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            sampler.SetObstacle(2, 0); // drone already standing on the dock column

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var result = pathfinder.FindPath(new Vector3(2, 0, 0), new Vector3(2, 0, 0));

            Assert.True(result.Found);
            Assert.Single(result.Waypoints);
        }

        [Fact]
        public void SolidAtStartOrGoalColumn_StillReturnsNoPath()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            sampler.SetSolid(0, 0);

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            Assert.False(pathfinder.FindPath(new Vector3(0, 0, 0), new Vector3(4, 0, 0)).Found);

            var sampler2 = new FakeWorldSampler(defaultHeight: 0f);
            sampler2.SetSolid(4, 0);

            var pathfinder2 = new GridPathfinder(sampler2, maxStepHeight: 1f);
            Assert.False(pathfinder2.FindPath(new Vector3(0, 0, 0), new Vector3(4, 0, 0)).Found);
        }

        // --- R2: step-height rules ---

        [Fact]
        public void StepWithinMaxStepHeight_PathProceedsThroughStep()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            // A one-unit step up starting at x=2; area beyond the step sits at height 1.
            for (int z = -3; z <= 3; z++)
            {
                for (int x = 2; x <= 6; x++)
                    sampler.SetHeight(x, z, 1f);
            }

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var start = new Vector3(0, 0, 0);
            var goal = new Vector3(5, 1, 0);

            var result = pathfinder.FindPath(start, goal);

            Assert.True(result.Found);
            // No detour was necessary - the path stays on the straight z=0 line.
            Assert.All(result.Waypoints, wp => Assert.Equal(0f, wp.Z));
        }

        [Fact]
        public void StepExceedingMaxStepHeight_IsTreatedAsImpassable()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            // A 5-unit cliff at x=3, spanning the whole bounded search area,
            // so with maxStepHeight=1 it behaves exactly like a solid wall -
            // fully blocking every route between start and goal.
            for (int z = -30; z <= 30; z++)
                sampler.SetHeight(3, z, 5f);

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var start = new Vector3(0, 0, 0);
            var goal = new Vector3(6, 0, 0);

            var result = pathfinder.FindPath(start, goal);

            Assert.False(result.Found);
        }

        // --- AE9: fully enclosed target reports no-path, not an exception ---

        [Fact]
        public void TargetFullyEnclosedByImpassableTerrain_ReturnsNoPath()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            // Box in the four orthogonal neighbors of the goal column so no
            // 4-connected route can ever reach it.
            sampler.SetSolid(11, 10);
            sampler.SetSolid(9, 10);
            sampler.SetSolid(10, 11);
            sampler.SetSolid(10, 9);

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var start = new Vector3(0, 0, 0);
            var goal = new Vector3(10, 0, 10);

            var result = pathfinder.FindPath(start, goal);

            Assert.False(result.Found);
            Assert.Empty(result.Waypoints);
        }

        // --- Arrival detection ---

        [Fact]
        public void ArrivalDetector_MidPath_ReportsNotArrived()
        {
            var waypoints = new List<Vector3>
            {
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(2, 0, 0),
            };
            var current = new Vector3(1, 0, 0);

            Assert.False(ArrivalDetector.HasArrived(current, waypoints));
        }

        [Fact]
        public void ArrivalDetector_AtFinalWaypoint_ReportsArrived()
        {
            var waypoints = new List<Vector3>
            {
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(2, 0, 0),
            };
            var current = new Vector3(2, 0, 0);

            Assert.True(ArrivalDetector.HasArrived(current, waypoints));
        }

        [Fact]
        public void ArrivalDetector_NearFinalWaypointWithinTolerance_ReportsArrived()
        {
            var waypoints = new List<Vector3>
            {
                new Vector3(0, 0, 0),
                new Vector3(2, 0, 0),
            };
            // 0.05 units off the exact final waypoint - inside the default tolerance (0.1).
            var current = new Vector3(2.05f, 0, 0);

            Assert.True(ArrivalDetector.HasArrived(current, waypoints));
        }

        /// <summary>
        /// Simple in-memory fake IWorldSampler for tests - a set-based
        /// override of a flat, all-walkable default plane. No real Eco data.
        /// </summary>
        private sealed class FakeWorldSampler : IWorldSampler
        {
            private readonly HashSet<(int X, int Z)> _solid = new HashSet<(int, int)>();
            private readonly HashSet<(int X, int Z)> _obstacles = new HashSet<(int, int)>();
            private readonly Dictionary<(int X, int Z), float> _heights = new Dictionary<(int, int), float>();
            private readonly float _defaultHeight;

            public FakeWorldSampler(float defaultHeight)
            {
                _defaultHeight = defaultHeight;
            }

            public void SetSolid(int x, int z) => _solid.Add((x, z));

            public void SetObstacle(int x, int z) => _obstacles.Add((x, z));

            public void SetHeight(int x, int z, float height) => _heights[(x, z)] = height;

            public bool IsSolidAt(int x, int z) => _solid.Contains((x, z));

            public bool IsObstacleAt(int x, int z) => _obstacles.Contains((x, z));

            public float GroundHeightAt(int x, int z) =>
                _heights.TryGetValue((x, z), out float height) ? height : _defaultHeight;
        }
    }
}
