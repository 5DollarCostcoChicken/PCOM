using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Uses an A* path to derive safe presentation candidates. The caller must create
    /// the final executed-path authority after validating each candidate's supercover.
    /// </summary>
    public sealed class MovementPresentationPathBuilder
    {
        private readonly List<Vector2Int> supercoverCells = new List<Vector2Int>();

        public IReadOnlyList<MovementPresentationSegment> Build(
            IGridPathMap map,
            IReadOnlyList<Vector3Int> logicalCoordinates,
            IGridTraversalPolicy traversalPolicy = null,
            IMovementPresentationSlopeResolver slopeResolver = null)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (logicalCoordinates == null)
            {
                throw new ArgumentNullException(nameof(logicalCoordinates));
            }

            List<MovementPresentationSegment> segments =
                new List<MovementPresentationSegment>();
            int startIndex = 0;
            while (startIndex < logicalCoordinates.Count - 1)
            {
                MovementPresentationSegmentType type = ClassifyTransition(
                    map,
                    logicalCoordinates[startIndex],
                    logicalCoordinates[startIndex + 1],
                    slopeResolver);
                int endIndex = startIndex + 1;
                if (type == MovementPresentationSegmentType.FlatRun &&
                    IsOrdinarySafeFloor(map, logicalCoordinates[startIndex]))
                {
                    int candidateEndIndex = endIndex + 1;
                    while (candidateEndIndex < logicalCoordinates.Count &&
                           ClassifyTransition(
                               map,
                               logicalCoordinates[candidateEndIndex - 1],
                               logicalCoordinates[candidateEndIndex],
                               slopeResolver) ==
                           MovementPresentationSegmentType.FlatRun &&
                           IsOrdinarySafeFloor(
                               map,
                               logicalCoordinates[candidateEndIndex]) &&
                           IsSafeSmoothedCorridor(
                               map,
                               logicalCoordinates[startIndex],
                               logicalCoordinates[candidateEndIndex],
                               traversalPolicy))
                    {
                        endIndex = candidateEndIndex;
                        candidateEndIndex++;
                    }
                }
                else if (type == MovementPresentationSegmentType.SlopeRun &&
                         slopeResolver != null &&
                         slopeResolver.TryGetContinuousSlopeRegion(
                             logicalCoordinates[startIndex],
                             logicalCoordinates[endIndex],
                             out SlopeRegion slopeRegion) &&
                         slopeRegion.Contains(logicalCoordinates[startIndex]) &&
                         slopeRegion.Contains(logicalCoordinates[endIndex]))
                {
                    int candidateEndIndex = endIndex + 1;
                    while (candidateEndIndex < logicalCoordinates.Count &&
                           ClassifyTransition(
                               map,
                               logicalCoordinates[candidateEndIndex - 1],
                               logicalCoordinates[candidateEndIndex],
                               slopeResolver) == MovementPresentationSegmentType.SlopeRun &&
                           slopeResolver.TryGetContinuousSlopeRegion(
                               logicalCoordinates[candidateEndIndex - 1],
                               logicalCoordinates[candidateEndIndex],
                               out SlopeRegion candidateRegion) &&
                           ReferenceEquals(candidateRegion, slopeRegion) &&
                           slopeRegion.Contains(logicalCoordinates[candidateEndIndex]) &&
                           IsSafeSmoothedSlopeCorridor(
                               slopeRegion,
                               logicalCoordinates[startIndex],
                               logicalCoordinates[candidateEndIndex]))
                    {
                        endIndex = candidateEndIndex;
                        candidateEndIndex++;
                    }
                }

                segments.Add(new MovementPresentationSegment(
                    type,
                    startIndex,
                    endIndex,
                    logicalCoordinates));
                startIndex = endIndex;
            }

            return segments.AsReadOnly();
        }

        public MovementPresentationSegmentType ClassifyTransition(
            IGridPathMap map,
            Vector3Int source,
            Vector3Int destination,
            IMovementPresentationSlopeResolver slopeResolver = null)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            map.TryGetTile(source, out TileManager.TileType sourceType);
            map.TryGetTile(destination, out TileManager.TileType destinationType);
            int elevationDifference = destination.y - source.y;
            if (sourceType == TileManager.TileType.Ladder && elevationDifference > 0)
            {
                return MovementPresentationSegmentType.LadderAscent;
            }

            bool touchesSlope = sourceType == TileManager.TileType.Slope ||
                                destinationType == TileManager.TileType.Slope;
            if (touchesSlope &&
                (slopeResolver == null ||
                 slopeResolver.TryGetContinuousSlopeRegion(source, destination, out _)))
            {
                return MovementPresentationSegmentType.SlopeRun;
            }

            if (elevationDifference == 1)
            {
                return MovementPresentationSegmentType.Clamber;
            }

            if (elevationDifference > 1)
            {
                return MovementPresentationSegmentType.Jump;
            }

            return elevationDifference < 0
                ? MovementPresentationSegmentType.Drop
                : MovementPresentationSegmentType.FlatRun;
        }

        private bool IsSafeSmoothedCorridor(
            IGridPathMap map,
            Vector3Int start,
            Vector3Int end,
            IGridTraversalPolicy traversalPolicy)
        {
            if (start.y != end.y)
            {
                return false;
            }

            GridSupercover.CollectHorizontalCells(
                start.x,
                start.z,
                end.x,
                end.z,
                supercoverCells);
            for (int cellIndex = 0; cellIndex < supercoverCells.Count; cellIndex++)
            {
                Vector2Int cell = supercoverCells[cellIndex];
                Vector3Int coordinate = new Vector3Int(cell.x, start.y, cell.y);
                if (!IsOrdinarySafeFloor(map, coordinate) ||
                    (traversalPolicy != null && !traversalPolicy.CanEnter(coordinate)))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsSafeSmoothedSlopeCorridor(
            SlopeRegion slopeRegion,
            Vector3Int start,
            Vector3Int end)
        {
            GridSupercover.CollectHorizontalCells(
                start.x,
                start.z,
                end.x,
                end.z,
                supercoverCells);
            for (int cellIndex = 0; cellIndex < supercoverCells.Count; cellIndex++)
            {
                Vector2Int cell = supercoverCells[cellIndex];
                if (!slopeRegion.ContainsHorizontalCell(cell.x, cell.y))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsOrdinarySafeFloor(IGridPathMap map, Vector3Int coordinate)
        {
            return map.TryGetTile(coordinate, out TileManager.TileType tileType) &&
                   tileType == TileManager.TileType.Floor;
        }

    }
}
