using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// One finalized world-space movement segment and the tactical cells its trajectory
    /// actually crosses. The crossed cells include both endpoints.
    /// </summary>
    public sealed class ExecutedMovementSegment
    {
        public ExecutedMovementSegment(
            MovementPresentationSegment presentationSegment,
            Vector3 startWorldPosition,
            Vector3 endWorldPosition,
            IReadOnlyList<Vector3Int> crossedCoordinates)
        {
            PresentationSegment = presentationSegment ??
                                  throw new ArgumentNullException(nameof(presentationSegment));
            if (crossedCoordinates == null || crossedCoordinates.Count < 2)
            {
                throw new ArgumentException(
                    "An executed movement segment requires at least two crossed cells.",
                    nameof(crossedCoordinates));
            }

            Vector3Int[] coordinateCopy = new Vector3Int[crossedCoordinates.Count];
            for (int coordinateIndex = 0;
                 coordinateIndex < crossedCoordinates.Count;
                 coordinateIndex++)
            {
                coordinateCopy[coordinateIndex] = crossedCoordinates[coordinateIndex];
            }

            if (coordinateCopy[0] != presentationSegment.StartCoordinate ||
                coordinateCopy[coordinateCopy.Length - 1] != presentationSegment.EndCoordinate)
            {
                throw new ArgumentException(
                    "Crossed cells must begin and end at the presentation endpoints.",
                    nameof(crossedCoordinates));
            }

            StartWorldPosition = startWorldPosition;
            EndWorldPosition = endWorldPosition;
            CrossedCoordinates = Array.AsReadOnly(coordinateCopy);
        }

        public MovementPresentationSegment PresentationSegment { get; }
        public MovementPresentationSegmentType Type => PresentationSegment.Type;
        public Vector3 StartWorldPosition { get; }
        public Vector3 EndWorldPosition { get; }
        public IReadOnlyList<Vector3Int> CrossedCoordinates { get; }
    }
}
