using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Deterministic A* over movement-selectable nodes in a sparse tile grid.
    /// Hazard preference is planning-only; every legal horizontal step costs one unit
    /// of actual movement budget regardless of its tile type.
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

        /// <summary>
        /// Calculates the cheapest legal physical movement cost to every tile within a
        /// bounded movement budget. Hazard preference is deliberately excluded because
        /// it only influences route selection, not AP spending or reachability.
        /// </summary>
        public MovementReachabilityResult CalculateReachability(
            IGridPathMap map,
            Vector3Int startCoordinate,
            int maximumJumpHeight,
            int maximumFallHeight,
            float hazardPenalty,
            float movementAmount,
            int maximumActionPointBands,
            IGridTraversalPolicy traversalPolicy = null)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            ValidateSettings(maximumJumpHeight, maximumFallHeight, hazardPenalty, movementAmount);
            int clampedBands = Mathf.Clamp(maximumActionPointBands, 0, 3);
            float maximumCost = MovementBudget.CalculateMaximumCost(movementAmount, clampedBands);
            Dictionary<Vector3Int, float> costs = new Dictionary<Vector3Int, float>();
            if (clampedBands == 0 ||
                !map.TryGetTile(startCoordinate, out TileManager.TileType startType) ||
                !TileManager.IsMovementSelectableTileType(startType) ||
                (traversalPolicy != null && !traversalPolicy.CanEnter(startCoordinate)))
            {
                return new MovementReachabilityResult(costs, movementAmount, clampedBands);
            }

            BinaryMinHeap openCoordinates = new BinaryMinHeap();
            costs.Add(startCoordinate, 0f);
            openCoordinates.Push(new OpenEntry(startCoordinate, 0f, 0f, 0m, 0));
            long insertionOrder = 1;
            while (openCoordinates.Count > 0)
            {
                OpenEntry currentEntry = openCoordinates.Pop();
                if (!costs.TryGetValue(currentEntry.Coordinate, out float currentCost) ||
                    currentEntry.CostFromStart > currentCost + CostComparisonTolerance)
                {
                    continue;
                }

                CollectNeighbors(
                    map,
                    currentEntry.Coordinate,
                    maximumJumpHeight,
                    maximumFallHeight,
                    traversalPolicy,
                    neighbors);
                for (int neighborIndex = 0; neighborIndex < neighbors.Count; neighborIndex++)
                {
                    NeighborCandidate neighbor = neighbors[neighborIndex];
                    float neighborCost = currentCost + NormalStepCost;
                    if (neighborCost > maximumCost + CostComparisonTolerance ||
                        (costs.TryGetValue(neighbor.Coordinate, out float knownCost) &&
                         neighborCost >= knownCost - CostComparisonTolerance))
                    {
                        continue;
                    }

                    costs[neighbor.Coordinate] = neighborCost;
                    openCoordinates.Push(new OpenEntry(
                        neighbor.Coordinate,
                        neighborCost,
                        0f,
                        0m,
                        insertionOrder));
                    insertionOrder++;
                }
            }

            return new MovementReachabilityResult(costs, movementAmount, clampedBands);
        }

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

            List<PathNode> nodes = new List<PathNode>();
            List<bool> activeNodes = new List<bool>();
            Dictionary<Vector3Int, List<int>> labelsByCoordinate =
                new Dictionary<Vector3Int, List<int>>();
            BinaryMinHeap openCoordinates = new BinaryMinHeap();
            float startHeuristic = CalculateHeuristic(startCoordinate, targetCoordinate);
            TryAddPathNode(
                nodes,
                activeNodes,
                labelsByCoordinate,
                new PathNode(startCoordinate, 0f, 0f, -1, startCoordinate),
                out int startNodeIndex);
            openCoordinates.Push(new OpenEntry(
                startCoordinate,
                0f,
                startHeuristic,
                CalculateHorizontalLineDeviation(
                    startCoordinate,
                    startCoordinate,
                    targetCoordinate),
                0,
                startNodeIndex));
            long insertionOrder = 1;
            float maximumMovementCost = MovementBudget.CalculateMaximumCost(
                movementAmount,
                currentActionPoints);
            int rejectedTargetNodeIndex = -1;

            while (openCoordinates.Count > 0)
            {
                OpenEntry currentEntry = openCoordinates.Pop();
                if (currentEntry.PathNodeIndex < 0 ||
                    currentEntry.PathNodeIndex >= nodes.Count ||
                    !activeNodes[currentEntry.PathNodeIndex])
                {
                    continue;
                }

                PathNode currentNode = nodes[currentEntry.PathNodeIndex];
                if (currentEntry.CostFromStart > currentNode.PlanningCost + CostComparisonTolerance)
                {
                    continue;
                }

                if (currentEntry.Coordinate == targetCoordinate)
                {
                    if (currentNode.PhysicalCost <= maximumMovementCost + CostComparisonTolerance)
                    {
                        return BuildPathResult(
                            nodes,
                            currentEntry.PathNodeIndex,
                            currentNode.PhysicalCost,
                            movementAmount,
                            currentActionPoints);
                    }

                    if (rejectedTargetNodeIndex < 0 ||
                        IsPreferredRejectedTarget(
                            currentNode,
                            nodes[rejectedTargetNodeIndex]))
                    {
                        rejectedTargetNodeIndex = currentEntry.PathNodeIndex;
                    }

                    continue;
                }

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
                    Vector3Int lineStartCoordinate = beginsOnLadder &&
                                                     neighbor.Coordinate.y >
                                                     currentEntry.Coordinate.y
                        ? neighbor.Coordinate
                        : currentNode.LineStartCoordinate;
                    float physicalCost = currentNode.PhysicalCost + NormalStepCost;
                    float planningCost = currentNode.PlanningCost + NormalStepCost +
                        (neighbor.TileType == TileManager.TileType.Hazard ? hazardPenalty : 0f);
                    PathNode candidateNode = new PathNode(
                        neighbor.Coordinate,
                        physicalCost,
                        planningCost,
                        currentEntry.PathNodeIndex,
                        lineStartCoordinate);
                    if (!TryAddPathNode(
                            nodes,
                            activeNodes,
                            labelsByCoordinate,
                            candidateNode,
                            out int candidateNodeIndex))
                    {
                        continue;
                    }

                    float heuristic = CalculateHeuristic(neighbor.Coordinate, targetCoordinate);
                    openCoordinates.Push(
                        new OpenEntry(
                            neighbor.Coordinate,
                            planningCost,
                            heuristic,
                            CalculateHorizontalLineDeviation(
                                neighbor.Coordinate,
                                lineStartCoordinate,
                                targetCoordinate),
                            insertionOrder,
                            candidateNodeIndex));
                    insertionOrder++;
                }
            }

            if (rejectedTargetNodeIndex >= 0)
            {
                PathNode rejectedTarget = nodes[rejectedTargetNodeIndex];
                return BuildPathResult(
                    nodes,
                    rejectedTargetNodeIndex,
                    rejectedTarget.PhysicalCost,
                    movementAmount,
                    currentActionPoints);
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

        private static GridPathResult BuildPathResult(
            IReadOnlyList<PathNode> nodes,
            int targetNodeIndex,
            float physicalCost,
            float movementAmount,
            int currentActionPoints)
        {
            int requiredActionPoints = MovementBudget.CalculateRequiredActionPoints(
                physicalCost,
                movementAmount);
            List<Vector3Int> reversedPath = new List<Vector3Int>();
            int nodeIndex = targetNodeIndex;
            while (nodeIndex >= 0)
            {
                PathNode node = nodes[nodeIndex];
                reversedPath.Add(node.Coordinate);
                nodeIndex = node.ParentNodeIndex;
            }

            reversedPath.Reverse();
            if (physicalCost > MovementBudget.CalculateMaximumCost(movementAmount, currentActionPoints) +
                CostComparisonTolerance)
            {
                return GridPathResult.RejectedPath(
                    reversedPath,
                    physicalCost,
                    requiredActionPoints,
                    GridPathFailureReason.ExceedsMovementBudget);
            }

            return GridPathResult.Success(reversedPath, physicalCost, requiredActionPoints);
        }

        private static bool TryAddPathNode(
            List<PathNode> nodes,
            List<bool> activeNodes,
            Dictionary<Vector3Int, List<int>> labelsByCoordinate,
            PathNode candidate,
            out int candidateNodeIndex)
        {
            candidateNodeIndex = -1;
            if (!labelsByCoordinate.TryGetValue(candidate.Coordinate, out List<int> labels))
            {
                labels = new List<int>();
                labelsByCoordinate.Add(candidate.Coordinate, labels);
            }

            for (int labelIndex = 0; labelIndex < labels.Count; labelIndex++)
            {
                int existingNodeIndex = labels[labelIndex];
                if (!activeNodes[existingNodeIndex])
                {
                    continue;
                }

                if (Dominates(nodes[existingNodeIndex], candidate))
                {
                    return false;
                }
            }

            for (int labelIndex = labels.Count - 1; labelIndex >= 0; labelIndex--)
            {
                int existingNodeIndex = labels[labelIndex];
                if (!activeNodes[existingNodeIndex] || Dominates(candidate, nodes[existingNodeIndex]))
                {
                    activeNodes[existingNodeIndex] = false;
                    labels.RemoveAt(labelIndex);
                }
            }

            candidateNodeIndex = nodes.Count;
            nodes.Add(candidate);
            activeNodes.Add(true);
            labels.Add(candidateNodeIndex);
            return true;
        }

        private static bool Dominates(PathNode left, PathNode right)
        {
            return left.PhysicalCost <= right.PhysicalCost + CostComparisonTolerance &&
                   left.PlanningCost <= right.PlanningCost + CostComparisonTolerance;
        }

        private static bool IsPreferredRejectedTarget(PathNode candidate, PathNode current)
        {
            if (!Mathf.Approximately(candidate.PlanningCost, current.PlanningCost))
            {
                return candidate.PlanningCost < current.PlanningCost;
            }

            return candidate.PhysicalCost < current.PhysicalCost;
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

        /// <summary>
        /// A nondominated planning label. Multiple labels may occupy one coordinate when
        /// a safer route costs more physical movement than a hazardous alternative.
        /// </summary>
        private readonly struct PathNode
        {
            public PathNode(
                Vector3Int coordinate,
                float physicalCost,
                float planningCost,
                int parentNodeIndex,
                Vector3Int lineStartCoordinate)
            {
                Coordinate = coordinate;
                PhysicalCost = physicalCost;
                PlanningCost = planningCost;
                ParentNodeIndex = parentNodeIndex;
                LineStartCoordinate = lineStartCoordinate;
            }

            public Vector3Int Coordinate { get; }
            public float PhysicalCost { get; }
            public float PlanningCost { get; }
            public int ParentNodeIndex { get; }
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
                : this(
                    coordinate,
                    costFromStart,
                    heuristic,
                    lineDeviation,
                    insertionOrder,
                    -1)
            {
            }

            public OpenEntry(
                Vector3Int coordinate,
                float costFromStart,
                float heuristic,
                decimal lineDeviation,
                long insertionOrder,
                int pathNodeIndex)
            {
                Coordinate = coordinate;
                CostFromStart = costFromStart;
                Heuristic = heuristic;
                EstimatedTotalCost = costFromStart + heuristic;
                LineDeviation = lineDeviation;
                InsertionOrder = insertionOrder;
                PathNodeIndex = pathNodeIndex;
            }

            public Vector3Int Coordinate { get; }
            public float CostFromStart { get; }
            public float Heuristic { get; }
            public float EstimatedTotalCost { get; }
            public decimal LineDeviation { get; }
            public long InsertionOrder { get; }
            public int PathNodeIndex { get; }
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
