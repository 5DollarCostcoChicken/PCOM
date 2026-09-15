using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Tile-derived description of one supported, axis-aligned planar slope component.
    /// </summary>
    public sealed class SlopeRegion
    {
        private readonly HashSet<Vector3Int> memberLookup;
        private readonly HashSet<Vector2Int> horizontalMemberLookup;

        internal SlopeRegion(
            Vector3Int[] members,
            Vector3Int lowEndpoint,
            Vector3Int highEndpoint,
            SlopeAxis axis,
            Vector3 lowWorldCenter,
            Vector3 highWorldCenter,
            float cellSize)
        {
            Members = Array.AsReadOnly(members);
            memberLookup = new HashSet<Vector3Int>(members);
            horizontalMemberLookup = new HashSet<Vector2Int>();
            for (int memberIndex = 0; memberIndex < members.Length; memberIndex++)
            {
                horizontalMemberLookup.Add(
                    new Vector2Int(members[memberIndex].x, members[memberIndex].z));
            }
            LowEndpoint = lowEndpoint;
            HighEndpoint = highEndpoint;
            Axis = axis;
            LowWorldCenter = lowWorldCenter;
            HighWorldCenter = highWorldCenter;
            GetBlendEndpoints(
                lowWorldCenter,
                highWorldCenter,
                axis,
                cellSize,
                out Vector3 lowBlendWorldPosition,
                out Vector3 highBlendWorldPosition);
            LowBlendWorldPosition = lowBlendWorldPosition;
            HighBlendWorldPosition = highBlendWorldPosition;
            Rise = highWorldCenter.y - lowWorldCenter.y;
            Run = axis == SlopeAxis.X
                ? Mathf.Abs(highWorldCenter.x - lowWorldCenter.x)
                : Mathf.Abs(highWorldCenter.z - lowWorldCenter.z);
            Gradient = Run > 0f ? Rise / Run : 0f;
            AngleDegrees = Mathf.Atan2(Rise, Run) * Mathf.Rad2Deg;
        }

        public IReadOnlyList<Vector3Int> Members { get; }
        public Vector3Int LowEndpoint { get; }
        public Vector3Int HighEndpoint { get; }
        public SlopeAxis Axis { get; }
        public float Rise { get; }
        public float Run { get; }
        public float Gradient { get; }
        public float AngleDegrees { get; }
        public Vector3 LowWorldCenter { get; }
        public Vector3 HighWorldCenter { get; }

        /// <summary>
        /// Low slope-cell center and inferred adjoining high-floor center used to blend
        /// each slope column through a nonzero fraction of the full elevation change.
        /// </summary>
        public Vector3 LowBlendWorldPosition { get; }
        public Vector3 HighBlendWorldPosition { get; }

        public bool Contains(Vector3Int coordinate)
        {
            return memberLookup.Contains(coordinate);
        }

        /// <summary>
        /// Returns whether the voxelized slope owns any surface cell in an X/Z column.
        /// </summary>
        public bool ContainsHorizontalCell(int x, int z)
        {
            return horizontalMemberLookup.Contains(new Vector2Int(x, z));
        }

        /// <summary>
        /// Evaluates the ramp's continuous standing height at a world position.
        /// Progress is clamped to the authored endpoint span.
        /// </summary>
        public float EvaluateWorldHeight(Vector3 worldPosition)
        {
            float lowAxisPosition = Axis == SlopeAxis.X
                ? LowBlendWorldPosition.x
                : LowBlendWorldPosition.z;
            float highAxisPosition = Axis == SlopeAxis.X
                ? HighBlendWorldPosition.x
                : HighBlendWorldPosition.z;
            float evaluatedAxisPosition = Axis == SlopeAxis.X ? worldPosition.x : worldPosition.z;
            float denominator = highAxisPosition - lowAxisPosition;
            if (Mathf.Approximately(denominator, 0f))
            {
                return LowWorldCenter.y;
            }

            float progress = Mathf.Clamp01((evaluatedAxisPosition - lowAxisPosition) / denominator);
            return Mathf.Lerp(LowWorldCenter.y, HighWorldCenter.y, progress);
        }

        private static void GetBlendEndpoints(
            Vector3 lowWorldCenter,
            Vector3 highWorldCenter,
            SlopeAxis axis,
            float cellSize,
            out Vector3 lowBlendWorldPosition,
            out Vector3 highBlendWorldPosition)
        {
            float lowAxisPosition = axis == SlopeAxis.X ? lowWorldCenter.x : lowWorldCenter.z;
            float highAxisPosition = axis == SlopeAxis.X ? highWorldCenter.x : highWorldCenter.z;
            float highFloorOffset = Mathf.Sign(highAxisPosition - lowAxisPosition) * cellSize;
            lowBlendWorldPosition = lowWorldCenter;
            highBlendWorldPosition = highWorldCenter;
            if (axis == SlopeAxis.X)
            {
                highBlendWorldPosition.x += highFloorOffset;
            }
            else
            {
                highBlendWorldPosition.z += highFloorOffset;
            }
        }
    }
}
