using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Immutable outcome of a grid path request. Successful paths include the start coordinate.
    /// </summary>
    public sealed class GridPathResult
    {
        private static readonly ReadOnlyCollection<Vector3Int> EmptyPath =
            Array.AsReadOnly(Array.Empty<Vector3Int>());

        private GridPathResult(
            bool pathFound,
            IReadOnlyList<Vector3Int> coordinates,
            float weightedCost,
            int requiredActionPoints,
            GridPathFailureReason failureReason)
        {
            PathFound = pathFound;
            Coordinates = coordinates;
            WeightedCost = weightedCost;
            RequiredActionPoints = requiredActionPoints;
            FailureReason = failureReason;
        }

        public bool PathFound { get; }

        public bool IsSuccessful => PathFound && FailureReason == GridPathFailureReason.None;

        public IReadOnlyList<Vector3Int> Coordinates { get; }

        public float WeightedCost { get; }

        public int RequiredActionPoints { get; }

        public GridPathFailureReason FailureReason { get; }

        public Vector3Int Destination => Coordinates.Count > 0
            ? Coordinates[Coordinates.Count - 1]
            : default;

        public static GridPathResult Success(
            IList<Vector3Int> coordinates,
            float weightedCost,
            int requiredActionPoints)
        {
            if (coordinates == null)
            {
                throw new ArgumentNullException(nameof(coordinates));
            }

            Vector3Int[] coordinateCopy = new Vector3Int[coordinates.Count];
            coordinates.CopyTo(coordinateCopy, 0);
            return new GridPathResult(
                true,
                Array.AsReadOnly(coordinateCopy),
                weightedCost,
                requiredActionPoints,
                GridPathFailureReason.None);
        }

        public static GridPathResult Failure(GridPathFailureReason failureReason)
        {
            if (failureReason == GridPathFailureReason.None)
            {
                throw new ArgumentException("A failed path requires a failure reason.", nameof(failureReason));
            }

            return new GridPathResult(false, EmptyPath, 0f, 0, failureReason);
        }

        public static GridPathResult RejectedPath(
            IList<Vector3Int> coordinates,
            float weightedCost,
            int requiredActionPoints,
            GridPathFailureReason failureReason)
        {
            if (failureReason == GridPathFailureReason.None)
            {
                throw new ArgumentException("A rejected path requires a failure reason.", nameof(failureReason));
            }

            if (coordinates == null)
            {
                throw new ArgumentNullException(nameof(coordinates));
            }

            Vector3Int[] coordinateCopy = new Vector3Int[coordinates.Count];
            coordinates.CopyTo(coordinateCopy, 0);
            return new GridPathResult(
                true,
                Array.AsReadOnly(coordinateCopy),
                weightedCost,
                requiredActionPoints,
                failureReason);
        }
    }
}
