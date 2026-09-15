using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Immutable authority for the route a unit will physically execute and the ordered
    /// tactical cells that route processes after A* approval.
    /// </summary>
    public sealed class ExecutedMovementPath
    {
        public ExecutedMovementPath(IReadOnlyList<ExecutedMovementSegment> segments)
        {
            if (segments == null || segments.Count == 0)
            {
                throw new ArgumentException(
                    "An executed movement path requires at least one segment.",
                    nameof(segments));
            }

            ExecutedMovementSegment[] segmentCopy =
                new ExecutedMovementSegment[segments.Count];
            List<Vector3Int> flattenedCoordinates = new List<Vector3Int>();
            List<MovementPresentationSegment> presentationSegments =
                new List<MovementPresentationSegment>(segments.Count);
            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                ExecutedMovementSegment segment = segments[segmentIndex] ??
                                                  throw new ArgumentException(
                                                      "Executed segments cannot be null.",
                                                      nameof(segments));
                segmentCopy[segmentIndex] = segment;
                presentationSegments.Add(segment.PresentationSegment);
                IReadOnlyList<Vector3Int> crossedCoordinates = segment.CrossedCoordinates;
                int firstCoordinateIndex = segmentIndex == 0 ? 0 : 1;
                if (segmentIndex > 0 &&
                    flattenedCoordinates[flattenedCoordinates.Count - 1] != crossedCoordinates[0])
                {
                    throw new ArgumentException(
                        "Executed segments must form one continuous cell sequence.",
                        nameof(segments));
                }

                for (int coordinateIndex = firstCoordinateIndex;
                     coordinateIndex < crossedCoordinates.Count;
                     coordinateIndex++)
                {
                    flattenedCoordinates.Add(crossedCoordinates[coordinateIndex]);
                }
            }

            Segments = Array.AsReadOnly(segmentCopy);
            Coordinates = flattenedCoordinates.AsReadOnly();
            PresentationSegments = presentationSegments.AsReadOnly();
        }

        public IReadOnlyList<ExecutedMovementSegment> Segments { get; }
        public IReadOnlyList<Vector3Int> Coordinates { get; }
        public IReadOnlyList<MovementPresentationSegment> PresentationSegments { get; }
    }
}
