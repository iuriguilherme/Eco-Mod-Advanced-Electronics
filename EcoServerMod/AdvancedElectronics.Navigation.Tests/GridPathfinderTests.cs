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

            // Waypoints sit one above the ground surface -- the cell the drone stands
            // IN, not the ground block itself. Asserting ground height here is what let
            // the drone render sunk one block into the floor.
            Assert.Equal(new Vector3(0, 1, 0), result.Waypoints[0]);
            Assert.Equal(new Vector3(5, 1, 0), result.Waypoints[result.Waypoints.Count - 1]);

            // "Direct line": z never deviates and x never backtracks. Deliberately NOT strictly
            // increasing any more -- the route now opens with a climb and closes with a descent,
            // each a waypoint sharing its neighbour's column, which is the point of flying rather
            // than walking. Progress toward the goal is still monotonic.
            for (int i = 1; i < result.Waypoints.Count; i++)
            {
                Assert.Equal(0f, result.Waypoints[i].Z);
                Assert.True(result.Waypoints[i].X >= result.Waypoints[i - 1].X);
            }

            Assert.True(result.Waypoints.Count > 2, "a flight profile has more than its two endpoints");
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
            var result = pathfinder.FindPath(new Vector3(0, 0, 0), new Vector3(4, 0, 0), new[] { new Vector3(4, 0, 0) });

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
            var result = pathfinder.FindPath(new Vector3(0, 0, 0), new Vector3(4, 0, 0), new[] { new Vector3(4, 0, 0) });

            Assert.True(result.Found);
        }

        // --- Multi-cell footprint exemption: a dock that occupies a 4x4 pad walls its
        // own drone in. Every pad cell reports Occupied, so exempting one column leaves
        // the interior unreachable on the way home and un-leavable on the way out. These
        // pin the whole footprint being exempt, in both directions.

        /// <summary>A 4x4 pad at x 4..7, z -1..2 -- the dock's registered occupancy.</summary>
        private static List<Vector3> Pad4x4(FakeWorldSampler sampler)
        {
            var columns = new List<Vector3>();
            for (int x = 4; x <= 7; x++)
            {
                for (int z = -1; z <= 2; z++)
                {
                    sampler.SetObstacle(x, z);
                    columns.Add(new Vector3(x, 0, z));
                }
            }
            return columns;
        }

        [Fact]
        public void GoalInsideMultiCellFootprint_WholeFootprintExempt_FindsPath()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            var pad = Pad4x4(sampler);

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var result = pathfinder.FindPath(new Vector3(0, 0, 0), new Vector3(5, 0, 0), pad);

            Assert.True(result.Found);
            Assert.Equal(5f, result.Waypoints[^1].X);
        }

        [Fact]
        public void StartInsideMultiCellFootprint_WholeFootprintExempt_FindsPathOut()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            var pad = Pad4x4(sampler);

            // Parked on the pad, dispatched to open ground. The first step off the pad
            // crosses another pad cell, which the goal-only exemption never covered.
            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var result = pathfinder.FindPath(new Vector3(5, 0, 0), new Vector3(0, 0, 0), pad);

            Assert.True(result.Found);
            Assert.Equal(0f, result.Waypoints[^1].X);
        }

        [Fact]
        public void BothEndsInsideSameFootprint_FindsPathBetweenInteriorCells()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            var pad = Pad4x4(sampler);

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var result = pathfinder.FindPath(new Vector3(4, 0, -1), new Vector3(7, 0, 2), pad);

            Assert.True(result.Found);
            Assert.Equal(new Vector3(7, 1, 2), result.Waypoints[^1]);
        }

        // The widening must not turn into a licence to walk through anything: a column
        // that belongs to no named footprint still blocks, footprint exemption or not.
        [Fact]
        public void OccupiedColumnOutsideExemptFootprint_StillBlocks()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            var pad = Pad4x4(sampler);
            // Another player's object walling off the pad from the start position. It
            // spans the whole bounded search area, so no detour exists -- the pad's
            // exemption is the only thing that could let the drone through, and must not.
            for (int z = -30; z <= 30; z++)
                sampler.SetObstacle(2, z);

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var result = pathfinder.FindPath(new Vector3(0, 0, 0), new Vector3(5, 0, 0), pad);

            Assert.False(result.Found);
        }

        // The invariant the exemption comments promise most loudly, tested where it is
        // newly reachable: a MID-PATH exempt cell. Exempting a column waives the obstacle
        // test, never the solidity test -- occupancy says "something of mine stands here",
        // solidity says "there is no room to stand", and only the first is the caller's to
        // waive. Without this, a dock buried in terrain would route a drone through rock.
        [Fact]
        public void SolidColumnInsideExemptFootprint_StillBlocks()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            var pad = Pad4x4(sampler);
            // Wall off the pad's interior with solid terrain across its whole width, so the
            // only route from the west edge to the east edge runs through solid columns.
            for (int z = -30; z <= 30; z++)
                sampler.SetSolid(5, z);

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var result = pathfinder.FindPath(new Vector3(4, 0, 0), new Vector3(7, 0, 0), pad);

            Assert.False(result.Found);
        }

        // Regression guard for every one-block object -- the assembly, and the dock as it
        // was. A single-cell exempt set must behave exactly like the old bool did.
        [Fact]
        public void SingleCellExemptFootprint_BehavesLikeTheOldGoalExemption()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            sampler.SetObstacle(4, 0);

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var exempt = new[] { new Vector3(4, 0, 0) };

            Assert.True(pathfinder.FindPath(new Vector3(0, 0, 0), new Vector3(4, 0, 0), exempt).Found);
            Assert.False(pathfinder.FindPath(new Vector3(0, 0, 0), new Vector3(4, 0, 0)).Found);
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

        // Regression: vegetated terrain must stay walkable. The live drone never moved
        // because the world sampler reported every grass-covered column as solid, so no
        // path existed anywhere. The engine's own pathfinding treats plants as walkable;
        // the IWorldSampler contract now says so explicitly. A sampler that regresses to
        // "any non-empty block is solid" makes this fail.
        [Fact]
        public void FullyVegetatedTerrain_AllColumnsWalkable_PathIsFound()
        {
            // A sampler where nothing is solid or obstructed models open, grassy ground.
            var sampler = new FakeWorldSampler(defaultHeight: 0f);

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var result = pathfinder.FindPath(new Vector3(0, 0, 0), new Vector3(20, 0, 0));

            Assert.True(result.Found);
            Assert.Equal(20f, result.Waypoints[^1].X);
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
        // --- The drone is 3.35 blocks wide, not a point ---

        [Fact]
        public void ARouteDoesNotGraze_APlacedObject()
        {
            // A single obstacle beside the direct line. Routing as a point put the drone's centre
            // one column clear while its hull passed through the object -- what looked like
            // partial clipping of stockpiles.
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            sampler.SetObstacle(3, 1);

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);
            var result = pathfinder.FindPath(new Vector3(0, 1, 0), new Vector3(6, 1, 0));

            Assert.True(result.Found);
            foreach (var waypoint in result.Waypoints)
                Assert.False(waypoint.X == 3f && waypoint.Z == 0f,
                    "routed within one column of the obstacle -- the hull would overlap it");
        }

        [Fact]
        public void SolidTerrainBeside_DoesNotBlock_SoAShaftIsStillEnterable()
        {
            // Clearance is obstacles only. A 5x5 shaft is a hole with solid walls; requiring a
            // clear block on every side would make the drone unable to enter what it digs.
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            for (var z = -1; z <= 1; z++) { sampler.SetSolid(2, z); sampler.SetSolid(4, z); }
            sampler.SetHeight(3, 0, 60f);

            var pathfinder = new GridPathfinder(sampler, ReturnEscalation.OrdinaryMaxStepHeight);

            Assert.True(pathfinder.FindPath(new Vector3(3, 65, 1), new Vector3(3, 61, 0)).Found);
        }

        [Fact]
        public void ExemptColumnsAreSkippedInTheSweep_SoTheDockDoesNotWallItsDroneIn()
        {
            // Every pad cell reports Occupied. Without exempting them in the clearance sweep too,
            // a drone beside its own dock finds its footprint overlapping the pad and cannot move.
            var sampler = new FakeWorldSampler(defaultHeight: 0f);
            var pad = new List<Vector3>();
            for (var x = 0; x <= 1; x++)
            for (var z = 0; z <= 1; z++)
            {
                sampler.SetObstacle(x, z);
                pad.Add(new Vector3(x, 0, z));
            }

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);

            Assert.True(pathfinder.FindPath(new Vector3(0, 1, 0), new Vector3(6, 1, 0), pad).Found);
        }

        // --- The drone flies: it crosses pits, it does not descend into them ---

        [Fact]
        public void APitOnTheRoute_IsFlownOver_NotDescendedInto()
        {
            // Flat ground with a shaft partway along. Every waypoint between the ends must stay
            // at or above the surrounding surface -- the drone used to trace the hole down and up.
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            sampler.SetHeight(3, 0, 49f);

            var pathfinder = new GridPathfinder(sampler, ReturnEscalation.OrdinaryMaxStepHeight);
            var result = pathfinder.FindPath(new Vector3(0, 65, 0), new Vector3(6, 65, 0));

            Assert.True(result.Found);
            foreach (var waypoint in result.Waypoints)
                Assert.True(waypoint.Y >= 64f, $"waypoint dipped to {waypoint.Y}, into the pit");
        }

        [Fact]
        public void TheClimbIsItsOwnLeg_SoTheDroneRisesBeforeItTravels()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            var pathfinder = new GridPathfinder(sampler, ReturnEscalation.OrdinaryMaxStepHeight);

            var result = pathfinder.FindPath(new Vector3(0, 65, 0), new Vector3(5, 65, 0));

            // Second waypoint shares the first's column and is higher: a vertical climb, not a
            // diagonal. The diagonal is what read as the drone clipping into rising ground.
            Assert.Equal(result.Waypoints[0].X, result.Waypoints[1].X);
            Assert.Equal(result.Waypoints[0].Z, result.Waypoints[1].Z);
            Assert.True(result.Waypoints[1].Y > result.Waypoints[0].Y);

            // ...and the mirror image at the far end: descend in place onto the goal.
            var last = result.Waypoints.Count - 1;
            Assert.Equal(result.Waypoints[last].X, result.Waypoints[last - 1].X);
            Assert.Equal(result.Waypoints[last].Z, result.Waypoints[last - 1].Z);
            Assert.True(result.Waypoints[last - 1].Y > result.Waypoints[last].Y);
        }

        [Fact]
        public void TheGoalKeepsItsGroundHeight_SoAShaftFloorIsStillLandedOn()
        {
            // Cruising must not stop the drone reaching the bottom of its own shaft.
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            sampler.SetHeight(5, 0, 49f);

            var pathfinder = new GridPathfinder(sampler, ReturnEscalation.OrdinaryMaxStepHeight);
            var result = pathfinder.FindPath(new Vector3(0, 65, 0), new Vector3(5, 50, 0));

            Assert.True(result.Found);
            Assert.Equal(50f, result.Waypoints[result.Waypoints.Count - 1].Y);
        }

        // --- The drone flies: a shaft it dug itself must stay reachable ---

        [Fact]
        public void ShaftDugToFullTierDepth_IsStillReachable_AtTheDronesOwnClimbHeight()
        {
            // The exact live failure, offline: flat ground, one column cut to the mining tier's
            // 15 layers, drone parked at its dock trying to get back in. At the old walking step
            // of 1f this returned no path, the lifecycle went Unreachable, and the drone hovered
            // over its dock forever.
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            sampler.SetHeight(5, 0, 64f - 15f);

            var pathfinder = new GridPathfinder(sampler, ReturnEscalation.OrdinaryMaxStepHeight);

            var result = pathfinder.FindPath(new Vector3(0, 65, 0), new Vector3(5, 50, 0));

            Assert.True(result.Found);
            Assert.Equal(50f, result.Waypoints[result.Waypoints.Count - 1].Y); // shaft floor + 1
        }

        [Fact]
        public void ShaftDugToFullTierDepth_IsUnreachable_AtAWalkingStep()
        {
            // The regression this guards: proves the old value is what refused the path, so the
            // constant is doing the work and a future "tidy-up" back to 1f fails here loudly
            // rather than on a live server two restarts later.
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            sampler.SetHeight(5, 0, 64f - 15f);

            var pathfinder = new GridPathfinder(sampler, maxStepHeight: 1f);

            Assert.False(pathfinder.FindPath(new Vector3(0, 65, 0), new Vector3(5, 50, 0)).Found);
        }

        [Fact]
        public void ADropOfSeveralBlocks_IsTraversable_SoASlopedAreaIsNotAWall()
        {
            // Same constant, the other reported symptom: a freshly drawn area on a slope read
            // "unreachable" to the survey drone. Any terrain step over one block was a wall.
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            for (var x = 1; x <= 4; x++) sampler.SetHeight(x, 0, 64f + x * 3f);

            var pathfinder = new GridPathfinder(sampler, ReturnEscalation.OrdinaryMaxStepHeight);

            Assert.True(pathfinder.FindPath(new Vector3(0, 65, 0), new Vector3(4, 77, 0)).Found);
        }

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
