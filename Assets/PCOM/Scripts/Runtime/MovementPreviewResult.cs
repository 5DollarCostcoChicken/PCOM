using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Immutable movement-preview authority. The executed path is built by the same
    /// UnitMovement code that builds a committed physical route.
    /// </summary>
    public sealed class MovementPreviewResult
    {
        public MovementPreviewResult(
            GridPathResult pathResult,
            ExecutedMovementPath executedPath,
            IReadOnlyList<MovementRouteSection> routeSections)
        {
            PathResult = pathResult ?? throw new ArgumentNullException(nameof(pathResult));
            ExecutedPath = executedPath;
            if (routeSections == null || routeSections.Count == 0)
            {
                RouteSections = Array.Empty<MovementRouteSection>();
            }
            else
            {
                MovementRouteSection[] copiedSections =
                    new MovementRouteSection[routeSections.Count];
                for (int sectionIndex = 0; sectionIndex < routeSections.Count; sectionIndex++)
                {
                    copiedSections[sectionIndex] = routeSections[sectionIndex];
                }

                RouteSections = Array.AsReadOnly(copiedSections);
            }
        }

        public GridPathResult PathResult { get; }
        public bool IsReachable => PathResult.IsSuccessful &&
                                   (PathResult.Coordinates.Count <= 1 || ExecutedPath != null);
        public float WeightedCost => PathResult.WeightedCost;
        public int RequiredActionPoints => PathResult.RequiredActionPoints;
        public IReadOnlyList<Vector3Int> LogicalPath => PathResult.Coordinates;
        public ExecutedMovementPath ExecutedPath { get; }
        public IReadOnlyList<Vector3Int> CrossedTiles => ExecutedPath?.Coordinates ?? Array.Empty<Vector3Int>();
        public IReadOnlyList<MovementRouteSection> RouteSections { get; }

        public bool HasHazardSections
        {
            get
            {
                for (int sectionIndex = 0; sectionIndex < RouteSections.Count; sectionIndex++)
                {
                    if (RouteSections[sectionIndex].IsHazardous)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
