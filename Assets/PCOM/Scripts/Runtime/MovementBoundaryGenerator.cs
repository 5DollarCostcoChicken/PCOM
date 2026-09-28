using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Produces only the meaningful perimeter edges of AP-band reachability regions.
    /// An edge between two bands is owned by the lower-numbered (cheaper) band.
    /// </summary>
    public static class MovementBoundaryGenerator
    {
        private static readonly Vector3Int[] HorizontalDirections =
        {
            Vector3Int.left,
            new Vector3Int(0, 0, -1),
            new Vector3Int(0, 0, 1),
            Vector3Int.right
        };

        public static void CollectEdges(
            MovementReachabilityResult reachability,
            List<MovementBoundaryEdge> results,
            IMovementPresentationSlopeResolver slopeResolver = null)
        {
            if (reachability == null)
            {
                throw new ArgumentNullException(nameof(reachability));
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            Dictionary<SlopeSurfaceBoundaryKey, int> slopeBoundaryIndices =
                slopeResolver == null
                    ? null
                    : new Dictionary<SlopeSurfaceBoundaryKey, int>();
            foreach (KeyValuePair<Vector3Int, float> entry in reachability.Costs)
            {
                if (!reachability.TryGetActionPointBand(entry.Key, out int band))
                {
                    continue;
                }

                // Thin slopes can voxelize into stacked cells. The highest member
                // in a slope column is the only selectable surface (and the one the
                // selector targets), so lower representatives must not contribute
                // competing boundary edges.
                if (TryGetTopSlopeSurfaceMember(
                        entry.Key,
                        slopeResolver,
                        out Vector3Int topSlopeMember) &&
                    topSlopeMember != entry.Key)
                {
                    continue;
                }

                for (int directionIndex = 0; directionIndex < HorizontalDirections.Length; directionIndex++)
                {
                    Vector3Int direction = HorizontalDirections[directionIndex];
                    if (TryGetTopSlopeSurfaceNeighbor(
                            entry.Key,
                            direction,
                            slopeResolver,
                            out Vector3Int slopeNeighbor))
                    {
                        // The adjoining X/Z column is one continuous slope surface.
                        // Its top representative is authoritative even when an
                        // overlapping lower voxel has a different reachability band.
                        if (!reachability.TryGetActionPointBand(
                                slopeNeighbor,
                                out int slopeNeighborBand))
                        {
                            // The origin and out-of-range tiles have no visible AP
                            // band, so this is a real range cutoff.
                            AddEdge(
                                new MovementBoundaryEdge(entry.Key, direction, band),
                                results,
                                slopeBoundaryIndices,
                                slopeResolver);
                            continue;
                        }

                        if (slopeNeighborBand == band)
                        {
                            continue;
                        }

                        if (band < slopeNeighborBand)
                        {
                            AddEdge(
                                new MovementBoundaryEdge(entry.Key, direction, band),
                                results,
                                slopeBoundaryIndices,
                                slopeResolver);
                        }

                        continue;
                    }

                    Vector3Int neighbor = entry.Key + direction;
                    if (!reachability.TryGetActionPointBand(neighbor, out int neighborBand))
                    {
                        AddEdge(
                            new MovementBoundaryEdge(entry.Key, direction, band),
                            results,
                            slopeBoundaryIndices,
                            slopeResolver);
                        continue;
                    }

                    if (neighborBand == band)
                    {
                        continue;
                    }

                    if (band < neighborBand)
                    {
                        AddEdge(
                            new MovementBoundaryEdge(entry.Key, direction, band),
                            results,
                            slopeBoundaryIndices,
                            slopeResolver);
                    }
                }
            }
        }

        private static bool TryGetTopSlopeSurfaceNeighbor(
            Vector3Int source,
            Vector3Int horizontalDirection,
            IMovementPresentationSlopeResolver slopeResolver,
            out Vector3Int topNeighbor)
        {
            topNeighbor = default;
            if (slopeResolver == null)
            {
                return false;
            }

            if (!slopeResolver.TryGetContinuousSlopeRegion(
                    source,
                    source,
                    out SlopeRegion slopeRegion) ||
                slopeRegion == null ||
                !slopeRegion.Contains(source))
            {
                return false;
            }

            return TryGetTopSlopeMemberInColumn(
                slopeRegion,
                source.x + horizontalDirection.x,
                source.z + horizontalDirection.z,
                out topNeighbor);
        }

        private static bool TryGetTopSlopeSurfaceMember(
            Vector3Int coordinate,
            IMovementPresentationSlopeResolver slopeResolver,
            out Vector3Int topSlopeMember)
        {
            topSlopeMember = default;
            if (slopeResolver == null ||
                !slopeResolver.TryGetContinuousSlopeRegion(
                    coordinate,
                    coordinate,
                    out SlopeRegion slopeRegion) ||
                slopeRegion == null ||
                !slopeRegion.Contains(coordinate))
            {
                return false;
            }

            return TryGetTopSlopeMemberInColumn(
                slopeRegion,
                coordinate.x,
                coordinate.z,
                out topSlopeMember);
        }

        private static bool TryGetTopSlopeMemberInColumn(
            SlopeRegion slopeRegion,
            int x,
            int z,
            out Vector3Int topSlopeMember)
        {
            topSlopeMember = default;
            bool found = false;
            IReadOnlyList<Vector3Int> members = slopeRegion.Members;
            for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                Vector3Int member = members[memberIndex];
                if (member.x != x ||
                    member.z != z ||
                    (found && member.y <= topSlopeMember.y))
                {
                    continue;
                }

                topSlopeMember = member;
                found = true;
            }

            return found;
        }

        private static void AddEdge(
            MovementBoundaryEdge candidate,
            List<MovementBoundaryEdge> results,
            Dictionary<SlopeSurfaceBoundaryKey, int> slopeBoundaryIndices,
            IMovementPresentationSlopeResolver slopeResolver)
        {
            if (slopeBoundaryIndices == null ||
                slopeResolver == null ||
                !slopeResolver.TryGetContinuousSlopeRegion(
                    candidate.TileCoordinate,
                    candidate.TileCoordinate,
                    out SlopeRegion slopeRegion) ||
                slopeRegion == null ||
                !slopeRegion.Contains(candidate.TileCoordinate))
            {
                results.Add(candidate);
                return;
            }

            SlopeSurfaceBoundaryKey key = new SlopeSurfaceBoundaryKey(candidate, slopeRegion);
            if (!slopeBoundaryIndices.TryGetValue(key, out int existingIndex))
            {
                slopeBoundaryIndices.Add(key, results.Count);
                results.Add(candidate);
                return;
            }

            MovementBoundaryEdge existing = results[existingIndex];
            if (IsPreferredSurfaceEdge(candidate, existing))
            {
                results[existingIndex] = candidate;
            }
        }

        private static bool IsPreferredSurfaceEdge(
            MovementBoundaryEdge candidate,
            MovementBoundaryEdge existing)
        {
            if (candidate.ActionPointBand != existing.ActionPointBand)
            {
                return candidate.ActionPointBand < existing.ActionPointBand;
            }

            int coordinateComparison = CompareCoordinates(
                candidate.TileCoordinate,
                existing.TileCoordinate);
            if (coordinateComparison != 0)
            {
                return coordinateComparison < 0;
            }

            return CompareCoordinates(candidate.OutwardDirection, existing.OutwardDirection) < 0;
        }

        private static int CompareCoordinates(Vector3Int left, Vector3Int right)
        {
            int xComparison = left.x.CompareTo(right.x);
            if (xComparison != 0)
            {
                return xComparison;
            }

            int yComparison = left.y.CompareTo(right.y);
            return yComparison != 0 ? yComparison : left.z.CompareTo(right.z);
        }

        private readonly struct SlopeSurfaceBoundaryKey : IEquatable<SlopeSurfaceBoundaryKey>
        {
            private readonly SlopeRegion slopeRegion;
            private readonly bool isXAxisEdge;
            private readonly int edgeX;
            private readonly int edgeZ;

            public SlopeSurfaceBoundaryKey(
                MovementBoundaryEdge edge,
                SlopeRegion slopeRegion)
            {
                this.slopeRegion = slopeRegion;
                isXAxisEdge = edge.OutwardDirection.x != 0;
                edgeX = edge.TileCoordinate.x + (edge.OutwardDirection.x > 0 ? 1 : 0);
                edgeZ = edge.TileCoordinate.z + (edge.OutwardDirection.z > 0 ? 1 : 0);
            }

            public bool Equals(SlopeSurfaceBoundaryKey other)
            {
                return ReferenceEquals(slopeRegion, other.slopeRegion) &&
                       isXAxisEdge == other.isXAxisEdge &&
                       edgeX == other.edgeX &&
                       edgeZ == other.edgeZ;
            }

            public override bool Equals(object obj)
            {
                return obj is SlopeSurfaceBoundaryKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = slopeRegion != null ? slopeRegion.GetHashCode() : 0;
                    hash = (hash * 397) ^ isXAxisEdge.GetHashCode();
                    hash = (hash * 397) ^ edgeX;
                    return (hash * 397) ^ edgeZ;
                }
            }
        }
    }
}
