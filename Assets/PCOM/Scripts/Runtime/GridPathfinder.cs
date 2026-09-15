using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Deterministic A* over movement-selectable nodes in a sparse tile grid.
    /// </summary>
    public sealed class GridPathfinder
    {
        private const float NormalStepCost = 1f;
        private const float CostComparisonTolerance = 0.00001f;

        private static readonly Vector3Int[] HorizontalDirections =
        {
            Vector3Int.left,
            new Vector3Int(0, 0, -1),
            new Vector3Int(0, 0, 1),
            Vector3Int.right
        };

        private readonly List<Vector3Int> columnCoordinates = new List<Vector3Int>();
        private readonly List<NeighborCandidate> neighbors = new List<NeighborCandidate>();
        private readonly List<NeighborCandidate> ladderAscentCandidates = new List<NeighborCandidate>();

        public GridPathResult FindPath(
            IGridPathMap map,
            Vector3Int startCoordinate,
            Vector3Int targetCoordinate,
            int maximumJumpHeight,
            int maximumFallHeight,
            float hazardPenalty,
            float movementAmount,
            int currentActionPoints,
            IGridTraversalPolicy traversalPolicy = null)
        {
            if (map == null)
            {
                return GridPathResult.Failure(GridPathFailureReason.DependenciesUnavailable);
            }

            ValidateSettings(maximumJumpHeight, maximumFallHeight, hazardPenalty, movementAmount);
            if (!map.TryGetTile(startCoordinate, out TileManager.TileType startType) ||
                !TileManager.IsMovementSelectableTileType(startType))
            {
                return GridPathResult.Failure(GridPathFailureReason.StartMissingOrNotWalkable);
            }

            if (!map.TryGetTile(targetCoordinate, out TileManager.TileType targetType))
            {
                return GridPathResult.Failure(GridPathFailureReason.TargetMissing);
            }

            if (!TileManager.IsMovementSelectableTileType(targetType))
            {
                return GridPathResult.Failure(GridPathFailureReason.TargetNotWalkable);
            }

            if (traversalPolicy != null && !traversalPolicy.CanEnter(targetCoordinate))
            {
                return GridPathResult.Failure(GridPathFailureReason.TargetBlocked);
            }

            if (startCoordinate == targetCoordinate)
            {
                return GridPathResult.Success(new[] { startCoordinate }, 0f, 0);
            }

            Dictionary<Vector3Int, NodeRecord> records = new Dictionary<Vector3Int, NodeRecord>();
            HashSet<Vector3Int> closedCoordinates = new HashSet<Vector3Int>();
            BinaryMinHeap openCoordinates = new BinaryMinHeap();
            float startHeuristic = CalculateHeuristic(startCoordinate, targetCoordinate);
            records.Add(startCoordinate, new NodeRecord(
                0f,
                default,
                false,
                startCoordinate));
            openCoordinates.Push(new OpenEntry(
                startCoordinate,
                0f,
                startHeuristic,
                CalculateHorizontalLineDeviation(
                    startCoordinate,
                    startCoordinate,
                    targetCoordinate),
                0));
            long insertionOrder = 1;

            while (openCoordinates.Count > 0)
            {
                OpenEntry currentEntry = openCoordinates.Pop();
                if (closedCoordinates.Contains(currentEntry.Coordinate) ||
                    !records.TryGetValue(currentEntry.Coordinate, out NodeRecord currentRecord) ||
                    currentEntry.CostFromStart > currentRecord.CostFromStart + CostComparisonTolerance)
                {
                    continue;
                }

                if (currentEntry.Coordinate == targetCoordinate)
                {
                    return BuildSuccessfulResult(
                        records,
                        startCoordinate,
                        targetCoordinate,
                        currentRecord.CostFromStart,
                        movementAmount,
                        currentActionPoints);
                }

                closedCoordinates.Add(currentEntry.Coordinate);
                CollectNeighbors(
                    map,
                    currentEntry.Coordinate,
                    maximumJumpHeight,
                    maximumFallHeight,
                    traversalPolicy,
                    neighbors);
                bool beginsOnLadder = map.TryGetTile(
                    currentEntry.Coordinate,
                    out TileManager.TileType currentTileType) &&
                    currentTileType == TileManager.TileType.Ladder;

                for (int neighborIndex = 0; neighborIndex < neighbors.Count; neighborIndex++)
                {
                    NeighborCandidate neighbor = neighbors[neighborIndex];
                    if (closedCoordinates.Contains(neighbor.Coordinate))
                    {
                        continue;
                    }

                    float traversalCost = NormalStepCost +
                        (neighbor.TileType == TileManager.TileType.Hazard ? hazardPenalty : 0f);
                    float tentativeCost = currentRecord.CostFromStart + traversalCost;
                    if (records.TryGetValue(neighbor.Coordinate, out NodeRecord existingRecord) &&
                        tentativeCost >= existingRecord.CostFromStart - CostComparisonTolerance)
                    {
                        continue;
                    }

                    Vector3Int lineStartCoordinate = beginsOnLadder &&
                                                     neighbor.Coordinate.y >
                                                     currentEntry.Coordinate.y
                        ? neighbor.Coordinate
                        : currentRecord.LineStartCoordinate;
                    records[neighbor.Coordinate] = new NodeRecord(
                        tentativeCost,
                        currentEntry.Coordinate,
                        true,
                        lineStartCoordinate);
                    float heuristic = CalculateHeuristic(neighbor.Coordinate, targetCoordinate);
                    openCoordinates.Push(
                        new OpenEntry(
                            neighbor.Coordinate,
                            tentativeCost,
                            heuristic,
                            CalculateHorizontalLineDeviation(
                                neighbor.Coordinate,
                                lineStartCoordinate,
                                targetCoordinate),
                            insertionOrder));
                    insertionOrder++;
                }
            }

            return GridPathResult.Failure(GridPathFailureReason.NoLegalPath);
        }

        private void CollectNeighbors(
            IGridPathMap map,
            Vector3Int currentCoordinate,
            int maximumJumpHeight,
            int maximumFallHeight,
            IGridTraversalPolicy traversalPolicy,
            List<NeighborCandidate> results)
        {
            results.Clear();
            ladderAscentCandidates.Clear();
            bool beginsOnLadder = map.TryGetTile(currentCoordinate, out TileManager.TileType currentType) &&
                                  currentType == TileManager.TileType.Ladder;

            for (int directionIndex = 0; directionIndex < HorizontalDirections.Length; directionIndex++)
            {
                Vector3Int adjacentColumn = currentCoordinate + HorizontalDirections[directionIndex];
                map.CollectWalkableCoordinatesInColumn(
                    adjacentColumn.x,
                    adjacentColumn.z,
                    columnCoordinates);

                for (int coordinateIndex = 0; coordinateIndex < columnCoordinates.Count; coordinateIndex++)
                {
                    Vector3Int candidateCoordinate = columnCoordinates[coordinateIndex];
                    if (traversalPolicy != null &&
                        (!traversalPolicy.CanEnter(candidateCoordinate) ||
                         !traversalPolicy.CanTraverse(currentCoordinate, candidateCoordinate)))
                    {
                        continue;
                    }

                    int elevationDifference = candidateCoordinate.y - currentCoordinate.y;
                    map.TryGetTile(candidateCoordinate, out TileManager.TileType candidateType);
                    NeighborCandidate candidate = new NeighborCandidate(
                        candidateCoordinate,
                        candidateType,
                        HasWallSupport(map, candidateCoordinate));

                    if (elevationDifference > 0 && beginsOnLadder)
                    {
                        ladderAscentCandidates.Add(candidate);
                    }
                    else if (elevationDifference <= maximumJumpHeight &&
                             -elevationDifference <= maximumFallHeight)
                    {
                        results.Add(candidate);
                    }
                }
            }

            AddPreferredLadderAscents(currentCoordinate, results);
            results.Sort(CompareNeighborCandidates);
        }

        private void AddPreferredLadderAscents(
            Vector3Int ladderCoordinate,
            List<NeighborCandidate> results)
        {
            if (ladderAscentCandidates.Count == 0)
            {
                return;
            }

            bool hasSupportedCandidate = false;
            for (int candidateIndex = 0; candidateIndex < ladderAscentCandidates.Count; candidateIndex++)
            {
                if (ladderAscentCandidates[candidateIndex].HasWallSupport)
                {
                    hasSupportedCandidate = true;
                    break;
                }
            }

            int smallestElevationDifference = int.MaxValue;
            for (int candidateIndex = 0; candidateIndex < ladderAscentCandidates.Count; candidateIndex++)
            {
                NeighborCandidate candidate = ladderAscentCandidates[candidateIndex];
                if (hasSupportedCandidate && !candidate.HasWallSupport)
                {
                    continue;
                }

                int elevationDifference = candidate.Coordinate.y - ladderCoordinate.y;
                if (elevationDifference < smallestElevationDifference)
                {
                    smallestElevationDifference = elevationDifference;
                }
            }

            for (int candidateIndex = 0; candidateIndex < ladderAscentCandidates.Count; candidateIndex++)
            {
                NeighborCandidate candidate = ladderAscentCandidates[candidateIndex];
                if ((!hasSupportedCandidate || candidate.HasWallSupport) &&
                    candidate.Coordinate.y - ladderCoordinate.y == smallestElevationDifference)
                {
                    results.Add(candidate);
                }
            }
        }

        private static bool HasWallSupport(IGridPathMap map, Vector3Int coordinate)
        {
            return map.TryGetTile(coordinate + Vector3Int.down, out TileManager.TileType supportType) &&
                   supportType == TileManager.TileType.Wall;
        }

        private static GridPathResult BuildSuccessfulResult(
            Dictionary<Vector3Int, NodeRecord> records,
            Vector3Int startCoordinate,
            Vector3Int targetCoordinate,
            float weightedCost,
            float movementAmount,
            int currentActionPoints)
        {
            int requiredActionPoints = MovementBudget.CalculateRequiredActionPoints(
                weightedCost,
                movementAmount);
            List<Vector3Int> reversedPath = new List<Vector3Int> { targetCoordinate };
            Vector3Int coordinate = targetCoordinate;
            while (coordinate != startCoordinate)
            {
                NodeRecord record = records[coordinate];
                if (!record.HasParent)
                {
                    return GridPathResult.Failure(GridPathFailureReason.NoLegalPath);
                }

                coordinate = record.ParentCoordinate;
                reversedPath.Add(coordinate);
            }

            reversedPath.Reverse();
            if (weightedCost > MovementBudget.CalculateMaximumCost(movementAmount, currentActionPoints) +
                CostComparisonTolerance)
            {
                return GridPathResult.RejectedPath(
                    reversedPath,
                    weightedCost,
                    requiredActionPoints,
                    GridPathFailureReason.ExceedsMovementBudget);
            }

            return GridPathResult.Success(reversedPath, weightedCost, requiredActionPoints);
        }

        private static float CalculateHeuristic(Vector3Int coordinate, Vector3Int targetCoordinate)
        {
            // Elevation is deliberately omitted: one horizontal step can legally change
            // several elevation cells, so horizontal Manhattan distance is admissible.
            return Mathf.Abs(targetCoordinate.x - coordinate.x) +
                   Mathf.Abs(targetCoordinate.z - coordinate.z);
        }

        /// <summary>
        /// Returns an exact, unnormalized X/Z cross product. Decimal is used as a
        /// wide integer-valued type so every possible Vector3Int difference and product
        /// is representable without division or floating-point approximation.
        /// </summary>
        private static decimal CalculateHorizontalLineDeviation(
            Vector3Int coordinate,
            Vector3Int startCoordinate,
            Vector3Int targetCoordinate)
        {
            decimal deltaX = (decimal)targetCoordinate.x - startCoordinate.x;
            decimal deltaZ = (decimal)targetCoordinate.z - startCoordinate.z;
            decimal progressX = (decimal)coordinate.x - startCoordinate.x;
            decimal progressZ = (decimal)coordinate.z - startCoordinate.z;
            return Math.Abs((progressX * deltaZ) - (progressZ * deltaX));
        }

        private static void ValidateSettings(
            int maximumJumpHeight,
            int maximumFallHeight,
            float hazardPenalty,
            float movementAmount)
        {
            if (maximumJumpHeight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumJumpHeight));
            }

            if (maximumFallHeight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumFallHeight));
            }

            if (hazardPenalty <= 0f || float.IsNaN(hazardPenalty) || float.IsInfinity(hazardPenalty))
            {
                throw new ArgumentOutOfRangeException(nameof(hazardPenalty));
            }

            MovementBudget.CalculateMaximumCost(movementAmount, 1);
        }

        private static int CompareNeighborCandidates(NeighborCandidate left, NeighborCandidate right)
        {
            int xComparison = left.Coordinate.x.CompareTo(right.Coordinate.x);
            if (xComparison != 0)
            {
                return xComparison;
            }

            int zComparison = left.Coordinate.z.CompareTo(right.Coordinate.z);
            return zComparison != 0
                ? zComparison
                : left.Coordinate.y.CompareTo(right.Coordinate.y);
        }

        private readonly struct NeighborCandidate
        {
            public NeighborCandidate(
                Vector3Int coordinate,
                TileManager.TileType tileType,
                bool hasWallSupport)
            {
                Coordinate = coordinate;
                TileType = tileType;
                HasWallSupport = hasWallSupport;
            }

            public Vector3Int Coordinate { get; }
            public TileManager.TileType TileType { get; }
            public bool HasWallSupport { get; }
        }

        private readonly struct NodeRecord
        {
            public NodeRecord(
                float costFromStart,
                Vector3Int parentCoordinate,
                bool hasParent,
                Vector3Int lineStartCoordinate)
            {
                CostFromStart = costFromStart;
                ParentCoordinate = parentCoordinate;
                HasParent = hasParent;
                LineStartCoordinate = lineStartCoordinate;
            }

            public float CostFromStart { get; }
            public Vector3Int ParentCoordinate { get; }
            public bool HasParent { get; }
            public Vector3Int LineStartCoordinate { get; }
        }

        private readonly struct OpenEntry
        {
            public OpenEntry(
                Vector3Int coordinate,
                float costFromStart,
                float heuristic,
                decimal lineDeviation,
                long insertionOrder)
            {
                Coordinate = coordinate;
                CostFromStart = costFromStart;
                Heuristic = heuristic;
                EstimatedTotalCost = costFromStart + heuristic;
                LineDeviation = lineDeviation;
                InsertionOrder = insertionOrder;
            }

            public Vector3Int Coordinate { get; }
            public float CostFromStart { get; }
            public float Heuristic { get; }
            public float EstimatedTotalCost { get; }
            public decimal LineDeviation { get; }
            public long InsertionOrder { get; }
        }

        private sealed class BinaryMinHeap
        {
            private readonly List<OpenEntry> entries = new List<OpenEntry>();

            public int Count => entries.Count;

            public void Push(OpenEntry entry)
            {
                entries.Add(entry);
                int childIndex = entries.Count - 1;
                while (childIndex > 0)
                {
                    int parentIndex = (childIndex - 1) / 2;
                    if (Compare(entries[parentIndex], entries[childIndex]) <= 0)
                    {
                        break;
                    }

                    Swap(parentIndex, childIndex);
                    childIndex = parentIndex;
                }
            }

            public OpenEntry Pop()
            {
                OpenEntry minimum = entries[0];
                int lastIndex = entries.Count - 1;
                entries[0] = entries[lastIndex];
                entries.RemoveAt(lastIndex);
                int parentIndex = 0;

                while (true)
                {
                    int leftChildIndex = (parentIndex * 2) + 1;
                    if (leftChildIndex >= entries.Count)
                    {
                        break;
                    }

                    int rightChildIndex = leftChildIndex + 1;
                    int smallerChildIndex = rightChildIndex < entries.Count &&
                                            Compare(entries[rightChildIndex], entries[leftChildIndex]) < 0
                        ? rightChildIndex
                        : leftChildIndex;
                    if (Compare(entries[parentIndex], entries[smallerChildIndex]) <= 0)
                    {
                        break;
                    }

                    Swap(parentIndex, smallerChildIndex);
                    parentIndex = smallerChildIndex;
                }

                return minimum;
            }

            private void Swap(int leftIndex, int rightIndex)
            {
                OpenEntry temporary = entries[leftIndex];
                entries[leftIndex] = entries[rightIndex];
                entries[rightIndex] = temporary;
            }

            private static int Compare(OpenEntry left, OpenEntry right)
            {
                int totalCostComparison = left.EstimatedTotalCost.CompareTo(right.EstimatedTotalCost);
                if (totalCostComparison != 0)
                {
                    return totalCostComparison;
                }

                int lineDeviationComparison = left.LineDeviation.CompareTo(right.LineDeviation);
                if (lineDeviationComparison != 0)
                {
                    return lineDeviationComparison;
                }

                int heuristicComparison = left.Heuristic.CompareTo(right.Heuristic);
                if (heuristicComparison != 0)
                {
                    return heuristicComparison;
                }

                int xComparison = left.Coordinate.x.CompareTo(right.Coordinate.x);
                if (xComparison != 0)
                {
                    return xComparison;
                }

                int zComparison = left.Coordinate.z.CompareTo(right.Coordinate.z);
                if (zComparison != 0)
                {
                    return zComparison;
                }

                int yComparison = left.Coordinate.y.CompareTo(right.Coordinate.y);
                return yComparison != 0
                    ? yComparison
                    : left.InsertionOrder.CompareTo(right.InsertionOrder);
            }
        }
    }
}
