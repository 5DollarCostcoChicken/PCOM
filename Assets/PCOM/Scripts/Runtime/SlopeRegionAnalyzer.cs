using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Discovers six-neighbor slope components and derives an axis-aligned plane from them.
    /// </summary>
    public sealed class SlopeRegionAnalyzer
    {
        private const int VoxelSurfaceElevationTolerance = 1;

        private static readonly Vector3Int[] OrthogonalDirections =
        {
            Vector3Int.left,
            Vector3Int.right,
            Vector3Int.down,
            Vector3Int.up,
            new Vector3Int(0, 0, -1),
            new Vector3Int(0, 0, 1)
        };

        public SlopeRegionAnalysis Analyze(
            IGridPathMap map,
            Vector3Int startingCoordinate,
            Vector3 gridOrigin,
            float cellSize)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (cellSize <= 0f || float.IsNaN(cellSize) || float.IsInfinity(cellSize))
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            }

            if (!map.TryGetTile(startingCoordinate, out TileManager.TileType startingType) ||
                startingType != TileManager.TileType.Slope)
            {
                return Failure("The starting coordinate is not a slope tile.", Array.Empty<Vector3Int>());
            }

            Vector3Int[] members = DiscoverComponent(map, startingCoordinate);
            EndpointPair bestXPair = default;
            EndpointPair bestZPair = default;
            bool hasXPair = false;
            bool hasZPair = false;

            for (int leftIndex = 0; leftIndex < members.Length; leftIndex++)
            {
                for (int rightIndex = leftIndex + 1; rightIndex < members.Length; rightIndex++)
                {
                    Vector3Int left = members[leftIndex];
                    Vector3Int right = members[rightIndex];
                    if (left.y == right.y)
                    {
                        continue;
                    }

                    if (left.z == right.z && left.x != right.x)
                    {
                        EndpointPair pair = new EndpointPair(left, right, SlopeAxis.X);
                        if (!hasXPair || IsBetterPair(pair, bestXPair))
                        {
                            bestXPair = pair;
                            hasXPair = true;
                        }
                    }

                    if (left.x == right.x && left.z != right.z)
                    {
                        EndpointPair pair = new EndpointPair(left, right, SlopeAxis.Z);
                        if (!hasZPair || IsBetterPair(pair, bestZPair))
                        {
                            bestZPair = pair;
                            hasZPair = true;
                        }
                    }
                }
            }

            if (!hasXPair && !hasZPair)
            {
                return Failure(
                    "The connected slope component has no aligned endpoint pair with nonzero rise and run.",
                    members);
            }

            EndpointPair selectedPair;
            if (hasXPair && hasZPair)
            {
                int xRise = bestXPair.High.y - bestXPair.Low.y;
                int zRise = bestZPair.High.y - bestZPair.Low.y;
                if (xRise == zRise)
                {
                    return Failure(
                        "The connected slope component contains equally strong elevation " +
                        "changes along both X and Z.",
                        members);
                }

                // Voxelizing a wide, thin slope can touch an extra elevation cell across
                // its width. The dominant rise identifies the actual incline axis while
                // retaining equal-rise rejection for genuinely diagonal/ambiguous ramps.
                selectedPair = xRise > zRise ? bestXPair : bestZPair;
            }
            else
            {
                selectedPair = hasXPair ? bestXPair : bestZPair;
            }

            if (!HasConsistentInclineDirection(members, selectedPair))
            {
                return Failure(
                    "The connected slope component branches, curves, or reverses its incline direction.",
                    members);
            }


            if (!ConformsToSinglePlane(members, selectedPair))
            {
                return Failure(
                    "The connected slope component does not conform to one flat axis-aligned plane.",
                    members);
            }

            Vector3 lowWorldCenter = GridToWorldCenter(gridOrigin, cellSize, selectedPair.Low);
            Vector3 highWorldCenter = GridToWorldCenter(gridOrigin, cellSize, selectedPair.High);
            float run = selectedPair.Axis == SlopeAxis.X
                ? Mathf.Abs(highWorldCenter.x - lowWorldCenter.x)
                : Mathf.Abs(highWorldCenter.z - lowWorldCenter.z);
            if (run <= Mathf.Epsilon)
            {
                return Failure("The connected slope component has a zero-length horizontal run.", members);
            }

            return new SlopeRegionAnalysis(
                new SlopeRegion(
                    members,
                    selectedPair.Low,
                    selectedPair.High,
                    selectedPair.Axis,
                    lowWorldCenter,
                    highWorldCenter,
                    cellSize),
                null,
                members);
        }

        private static Vector3Int[] DiscoverComponent(IGridPathMap map, Vector3Int startingCoordinate)
        {
            HashSet<Vector3Int> discovered = new HashSet<Vector3Int> { startingCoordinate };
            Queue<Vector3Int> pending = new Queue<Vector3Int>();
            pending.Enqueue(startingCoordinate);

            while (pending.Count > 0)
            {
                Vector3Int current = pending.Dequeue();
                for (int directionIndex = 0; directionIndex < OrthogonalDirections.Length; directionIndex++)
                {
                    Vector3Int candidate = current + OrthogonalDirections[directionIndex];
                    if (discovered.Contains(candidate) ||
                        !map.TryGetTile(candidate, out TileManager.TileType candidateType) ||
                        candidateType != TileManager.TileType.Slope)
                    {
                        continue;
                    }

                    discovered.Add(candidate);
                    pending.Enqueue(candidate);
                }
            }

            Vector3Int[] members = new Vector3Int[discovered.Count];
            discovered.CopyTo(members);
            Array.Sort(members, CompareCoordinates);
            return members;
        }

        private static bool HasConsistentInclineDirection(
            IReadOnlyList<Vector3Int> members,
            EndpointPair selectedPair)
        {
            int lowAxis = GetAxisCoordinate(selectedPair.Low, selectedPair.Axis);
            int highAxis = GetAxisCoordinate(selectedPair.High, selectedPair.Axis);
            int expectedDirection = Math.Sign(highAxis - lowAxis);

            for (int leftIndex = 0; leftIndex < members.Count; leftIndex++)
            {
                for (int rightIndex = leftIndex + 1; rightIndex < members.Count; rightIndex++)
                {
                    int axisDifference = GetAxisCoordinate(members[rightIndex], selectedPair.Axis) -
                                         GetAxisCoordinate(members[leftIndex], selectedPair.Axis);
                    int elevationDifference = members[rightIndex].y - members[leftIndex].y;
                    if (axisDifference == 0 || elevationDifference == 0)
                    {
                        continue;
                    }

                    if (Math.Sign(axisDifference * elevationDifference) != expectedDirection &&
                        Math.Abs(elevationDifference) > VoxelSurfaceElevationTolerance)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsBetterPair(EndpointPair candidate, EndpointPair current)
        {
            int candidateRise = candidate.High.y - candidate.Low.y;
            int currentRise = current.High.y - current.Low.y;
            if (candidateRise != currentRise)
            {
                return candidateRise > currentRise;
            }

            int lowComparison = CompareCoordinates(candidate.Low, current.Low);
            return lowComparison != 0
                ? lowComparison < 0
                : CompareCoordinates(candidate.High, current.High) < 0;
        }

        private static bool ConformsToSinglePlane(
            IReadOnlyList<Vector3Int> members,
            EndpointPair selectedPair)
        {
            int lowAxis = GetAxisCoordinate(selectedPair.Low, selectedPair.Axis);
            int highAxis = GetAxisCoordinate(selectedPair.High, selectedPair.Axis);
            int axisRun = highAxis - lowAxis;
            if (axisRun == 0)
            {
                return false;
            }

            for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                Vector3Int member = members[memberIndex];
                float progress = (GetAxisCoordinate(member, selectedPair.Axis) - lowAxis) /
                                 (float)axisRun;
                float expectedElevation = Mathf.LerpUnclamped(
                    selectedPair.Low.y,
                    selectedPair.High.y,
                    progress);
                // A voxelized thin plane may touch the cells immediately above or below
                // its mathematical surface. Larger deviations describe another plane.
                if (Mathf.Abs(member.y - expectedElevation) >
                    VoxelSurfaceElevationTolerance + 0.0001f)
                {
                    return false;
                }
            }

            return true;
        }

        private static int CompareCoordinates(Vector3Int left, Vector3Int right)
        {
            int xComparison = left.x.CompareTo(right.x);
            if (xComparison != 0)
            {
                return xComparison;
            }

            int zComparison = left.z.CompareTo(right.z);
            return zComparison != 0 ? zComparison : left.y.CompareTo(right.y);
        }

        private static int GetAxisCoordinate(Vector3Int coordinate, SlopeAxis axis)
        {
            return axis == SlopeAxis.X ? coordinate.x : coordinate.z;
        }

        private static Vector3 GridToWorldCenter(
            Vector3 gridOrigin,
            float cellSize,
            Vector3Int coordinate)
        {
            return gridOrigin + ((Vector3)coordinate * cellSize);
        }

        private static SlopeRegionAnalysis Failure(string reason, Vector3Int[] discoveredCoordinates)
        {
            return new SlopeRegionAnalysis(null, reason, discoveredCoordinates);
        }

        private readonly struct EndpointPair
        {
            public EndpointPair(Vector3Int first, Vector3Int second, SlopeAxis axis)
            {
                if (first.y < second.y || (first.y == second.y && CompareCoordinates(first, second) <= 0))
                {
                    Low = first;
                    High = second;
                }
                else
                {
                    Low = second;
                    High = first;
                }

                Axis = axis;
            }

            public Vector3Int Low { get; }
            public Vector3Int High { get; }
            public SlopeAxis Axis { get; }
        }
    }
}
