using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Immutable presentation subsection referencing the source A* route used to build it.
    /// Both source indices are inclusive; executed crossed cells are stored separately.
    /// </summary>
    public sealed class MovementPresentationSegment
    {
        public MovementPresentationSegment(
            MovementPresentationSegmentType type,
            int logicalStartIndex,
            int logicalEndIndex,
            IReadOnlyList<Vector3Int> logicalCoordinates)
        {
            if (logicalCoordinates == null)
            {
                throw new ArgumentNullException(nameof(logicalCoordinates));
            }

            if (logicalStartIndex < 0 ||
                logicalEndIndex <= logicalStartIndex ||
                logicalEndIndex >= logicalCoordinates.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalEndIndex));
            }

            Type = type;
            LogicalStartIndex = logicalStartIndex;
            LogicalEndIndex = logicalEndIndex;
            StartCoordinate = logicalCoordinates[logicalStartIndex];
            EndCoordinate = logicalCoordinates[logicalEndIndex];
        }

        public MovementPresentationSegmentType Type { get; }
        public int LogicalStartIndex { get; }
        public int LogicalEndIndex { get; }
        public int LogicalTransitionCount => LogicalEndIndex - LogicalStartIndex;
        public Vector3Int StartCoordinate { get; }
        public Vector3Int EndCoordinate { get; }
    }
}
