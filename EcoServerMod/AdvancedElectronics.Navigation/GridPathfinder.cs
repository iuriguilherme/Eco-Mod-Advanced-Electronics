using System;
using System.Collections.Generic;
using System.Numerics;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// Outcome of a <see cref="GridPathfinder.FindPath"/> query. "No path
    /// found" is an expected, normal outcome (e.g. a destination fully
    /// enclosed by impassable terrain) - callers check <see cref="Found"/>
    /// rather than catching an exception, and a not-found result never
    /// carries waypoints that could be mistaken for a real path.
    /// </summary>
    public readonly struct PathResult
    {
        public bool Found { get; }

        public IReadOnlyList<Vector3> Waypoints { get; }

        private PathResult(bool found, IReadOnlyList<Vector3> waypoints)
        {
            Found = found;
            Waypoints = waypoints;
        }

        public static PathResult NotFound { get; } = new PathResult(false, Array.Empty<Vector3>());

        public static PathResult Success(IReadOnlyList<Vector3> waypoints) => new PathResult(true, waypoints);
    }

    /// <summary>
    /// Grid-based A* pathfinder over walkable (x, z) columns, querying
    /// terrain/obstacle data through <see cref="IWorldSampler"/>. This type
    /// has no dependency on any Eco.* namespace or game-engine API - it is
    /// self-written pathfinding (R1) that a mod's tick loop can drive.
    /// </summary>
    /// <remarks>
    /// Approach: classic 4-directionally-connected grid A* with a Manhattan
    /// distance heuristic (admissible and consistent for unit-cost moves on
    /// a 4-connected grid, so the result is optimal). A* is preferred here
    /// over a plain BFS because step-height differences make edge "cost"
    /// meaningful even though this implementation currently uses uniform
    /// move cost - keeping the A* shape leaves room to weight diagonal or
    /// costly terrain later without a rewrite.
    ///
    /// A column is walkable when it is neither solid terrain nor a
    /// player-placed obstacle (R2), and a move between two adjacent walkable
    /// columns is only allowed when the absolute ground-height difference
    /// between them is within <c>maxStepHeight</c>; a bigger difference
    /// (a step up, or a drop) is treated as impassable, exactly like a wall.
    ///
    /// The search area is bounded to the start/goal bounding box plus a
    /// margin. Without this bound, a query against an unreachable goal (e.g.
    /// fully enclosed by impassable columns, per AE9) would expand across an
    /// effectively infinite open plane and never terminate; the margin keeps
    /// the search finite while still comfortably covering any detour a
    /// realistic obstacle would require.
    /// </remarks>
    public sealed class GridPathfinder
    {
        private const int DefaultSearchMargin = 25;

        private readonly IWorldSampler _sampler;
        private readonly float _maxStepHeight;
        private readonly int _searchMargin;

        public GridPathfinder(IWorldSampler sampler, float maxStepHeight, int searchMargin = DefaultSearchMargin)
        {
            if (maxStepHeight < 0f)
                throw new ArgumentOutOfRangeException(nameof(maxStepHeight), "maxStepHeight cannot be negative.");
            if (searchMargin < 0)
                throw new ArgumentOutOfRangeException(nameof(searchMargin), "searchMargin cannot be negative.");

            _sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
            _maxStepHeight = maxStepHeight;
            _searchMargin = searchMargin;
        }

        /// <summary>
        /// Finds a walkable path from <paramref name="start"/> to
        /// <paramref name="goal"/>. Only the X/Z components of the inputs
        /// are used to locate grid columns; returned waypoints carry the
        /// sampler's actual ground height for each column.
        /// </summary>
        /// <param name="exemptOccupiedColumns">
        /// World positions whose columns are exempt from the OBSTACLE predicate — the
        /// footprint of an object the caller legitimately intends to path into and out
        /// of. Only X/Z matter. Null or empty means no exemption. Solidity is never
        /// exempt.
        /// </param>
        public PathResult FindPath(Vector3 start, Vector3 goal, IReadOnlyCollection<Vector3> exemptOccupiedColumns = null)
        {
            GridColumn startColumn = ToColumn(start);
            GridColumn goalColumn = ToColumn(goal);

            // The START column is always exempt from the OBSTACLE predicate: the mover
            // itself is a world object standing there, so an obstacle query at its own
            // column always reports true — without the exemption every dispatch fails
            // immediately with no-path.
            //
            // Other columns are exempt only when the caller names them, because the
            // caller is the only party that knows a given occupied column belongs to an
            // object it may stand on. The return-to-dock leg targets the dock and must
            // path into it (that is what "docking" is); a roam hop must NOT, or the drone
            // parks inside another player's object and then cannot pick a way back out.
            //
            // The set, rather than the single goal column this used to take: a dock that
            // occupies a 4x4 pad blocks its own drone twice over. Every cell of the pad
            // reports Occupied, so exempting only the goal cell leaves it walled in by
            // its own footprint — unreachable on the way home, and un-leavable on the way
            // out, since the drone's first step off the pad crosses another pad cell.
            // Solidity remains a hard failure for both endpoints regardless.
            var exempt = new HashSet<GridColumn> { startColumn };
            if (exemptOccupiedColumns != null)
            {
                foreach (Vector3 column in exemptOccupiedColumns)
                    exempt.Add(ToColumn(column));
            }

            if (_sampler.IsSolidAt(startColumn.X, startColumn.Z) || _sampler.IsSolidAt(goalColumn.X, goalColumn.Z))
                return PathResult.NotFound;

            if (!exempt.Contains(goalColumn) && _sampler.IsObstacleAt(goalColumn.X, goalColumn.Z))
                return PathResult.NotFound;

            if (startColumn.Equals(goalColumn))
                return PathResult.Success(new List<Vector3> { ToWaypoint(startColumn) });

            int minX = Math.Min(startColumn.X, goalColumn.X) - _searchMargin;
            int maxX = Math.Max(startColumn.X, goalColumn.X) + _searchMargin;
            int minZ = Math.Min(startColumn.Z, goalColumn.Z) - _searchMargin;
            int maxZ = Math.Max(startColumn.Z, goalColumn.Z) + _searchMargin;

            var open = new PriorityQueue<GridColumn, float>();
            var cameFrom = new Dictionary<GridColumn, GridColumn>();
            var gScore = new Dictionary<GridColumn, float> { [startColumn] = 0f };
            var closed = new HashSet<GridColumn>();

            open.Enqueue(startColumn, Heuristic(startColumn, goalColumn));

            while (open.Count > 0)
            {
                GridColumn current = open.Dequeue();
                if (!closed.Add(current))
                    continue;

                if (current.Equals(goalColumn))
                    return PathResult.Success(BuildWaypoints(cameFrom, current));

                foreach (GridColumn neighbor in Neighbors(current))
                {
                    if (neighbor.X < minX || neighbor.X > maxX || neighbor.Z < minZ || neighbor.Z > maxZ)
                        continue;
                    if (closed.Contains(neighbor))
                        continue;
                    // An exempt column is obstacle-exempt but never solidity-exempt (see
                    // the endpoint exemption above). Every other column must be walkable.
                    if (exempt.Contains(neighbor))
                    {
                        if (_sampler.IsSolidAt(neighbor.X, neighbor.Z))
                            continue;
                    }
                    else if (!IsWalkable(neighbor))
                        continue;
                    if (!IsStepAllowed(current, neighbor))
                        continue;

                    float tentativeG = gScore[current] + 1f;
                    if (!gScore.TryGetValue(neighbor, out float existingG) || tentativeG < existingG)
                    {
                        gScore[neighbor] = tentativeG;
                        cameFrom[neighbor] = current;
                        open.Enqueue(neighbor, tentativeG + Heuristic(neighbor, goalColumn));
                    }
                }
            }

            return PathResult.NotFound;
        }

        private bool IsWalkable(GridColumn column) =>
            !_sampler.IsSolidAt(column.X, column.Z) && !_sampler.IsObstacleAt(column.X, column.Z);

        private bool IsStepAllowed(GridColumn from, GridColumn to)
        {
            float fromHeight = _sampler.GroundHeightAt(from.X, from.Z);
            float toHeight = _sampler.GroundHeightAt(to.X, to.Z);
            return MathF.Abs(toHeight - fromHeight) <= _maxStepHeight;
        }

        private static IEnumerable<GridColumn> Neighbors(GridColumn column)
        {
            yield return new GridColumn(column.X + 1, column.Z);
            yield return new GridColumn(column.X - 1, column.Z);
            yield return new GridColumn(column.X, column.Z + 1);
            yield return new GridColumn(column.X, column.Z - 1);
        }

        private static float Heuristic(GridColumn a, GridColumn b) =>
            Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z);

        private static GridColumn ToColumn(Vector3 position) =>
            new GridColumn((int)MathF.Round(position.X), (int)MathF.Round(position.Z));

        /// <summary>
        /// A waypoint sits in the cell the entity STANDS IN -- one above the ground
        /// surface -- not in the ground block itself. <see cref="IWorldSampler.GroundHeightAt"/>
        /// reports the top solid block's height, so a waypoint placed at exactly that
        /// height puts the body inside the floor (it rendered sunk by one block).
        /// This matches the engine's own convention that a walkable node is the first
        /// empty block above a solid one.
        ///
        /// Step-height comparisons deliberately keep using the raw ground height: both
        /// columns shift by the same +1, so the difference is unchanged.
        /// </summary>
        private Vector3 ToWaypoint(GridColumn column) =>
            new Vector3(column.X, _sampler.GroundHeightAt(column.X, column.Z) + StandingHeightOffset, column.Z);

        /// <summary>Vertical offset from the ground surface to the cell an entity occupies.</summary>
        private const float StandingHeightOffset = 1f;

        private List<Vector3> BuildWaypoints(Dictionary<GridColumn, GridColumn> cameFrom, GridColumn end)
        {
            var columns = new List<GridColumn> { end };
            GridColumn current = end;
            while (cameFrom.TryGetValue(current, out GridColumn previous))
            {
                columns.Add(previous);
                current = previous;
            }
            columns.Reverse();

            var waypoints = new List<Vector3>(columns.Count);
            foreach (GridColumn column in columns)
                waypoints.Add(ToWaypoint(column));
            return waypoints;
        }

        private readonly struct GridColumn : IEquatable<GridColumn>
        {
            public readonly int X;
            public readonly int Z;

            public GridColumn(int x, int z)
            {
                X = x;
                Z = z;
            }

            public bool Equals(GridColumn other) => X == other.X && Z == other.Z;

            public override bool Equals(object obj) => obj is GridColumn other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(X, Z);
        }
    }

    /// <summary>
    /// Pure helper for deciding whether a moving entity has "arrived" at the
    /// end of a waypoint path returned by <see cref="GridPathfinder"/>.
    /// Exact floating-point equality between a simulated position and the
    /// final waypoint is unreliable, so arrival is judged within a distance
    /// tolerance instead.
    /// </summary>
    public static class ArrivalDetector
    {
        /// <summary>Default arrival tolerance, in world units.</summary>
        public const float DefaultTolerance = 0.1f;

        /// <summary>
        /// True when <paramref name="currentPosition"/> is within
        /// <paramref name="tolerance"/> world units of the last waypoint.
        /// False for an empty/null waypoint list (nothing to arrive at).
        /// </summary>
        public static bool HasArrived(
            Vector3 currentPosition,
            IReadOnlyList<Vector3> waypoints,
            float tolerance = DefaultTolerance)
        {
            if (waypoints == null || waypoints.Count == 0)
                return false;

            Vector3 finalWaypoint = waypoints[waypoints.Count - 1];
            return Vector3.Distance(currentPosition, finalWaypoint) <= tolerance;
        }
    }
}
