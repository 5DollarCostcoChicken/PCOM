using System;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Calculates presentation-only height corrections for a voxelized slope region.
    /// </summary>
    public static class SlopeVisualizationHeightResolver
    {
        /// <summary>
        /// Returns the signed world-space slope lift at a sample point. The lift is
        /// linearly blended from the authored horizontal slope-cell membership, so it
        /// transitions cleanly between a high flat endpoint and a low sloped endpoint.
        /// </summary>
        public static float GetSlopeHeightOffset(
            SlopeRegion slopeRegion,
            Vector3Int slopeCoordinate,
            Vector3 worldPosition,
            Vector3 gridOrigin,
            float cellSize,
            float heightOffsetInCells)
        {
            if (slopeRegion == null)
            {
                throw new ArgumentNullException(nameof(slopeRegion));
            }

            if (cellSize <= 0f || float.IsNaN(cellSize) || float.IsInfinity(cellSize))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSize),
                    cellSize,
                    "Cell size must be finite and positive.");
            }

            if (float.IsNaN(heightOffsetInCells) || float.IsInfinity(heightOffsetInCells))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(heightOffsetInCells),
                    heightOffsetInCells,
                    "Height offset must be finite.");
            }

            return cellSize * heightOffsetInCells * GetSlopeMembershipWeightUnchecked(
                slopeRegion,
                slopeCoordinate,
                worldPosition,
                gridOrigin,
                cellSize);
        }

        /// <summary>
        /// Returns the interpolated membership of the horizontal slope cells beneath
        /// a visual sample. This intentionally uses a linear blend rather than a
        /// stepped tile-type test, preserving a continuous visual surface at both
        /// slope ends.
        /// </summary>
        public static float GetSlopeMembershipWeight(
            SlopeRegion slopeRegion,
            Vector3Int slopeCoordinate,
            Vector3 worldPosition,
            Vector3 gridOrigin,
            float cellSize)
        {
            if (slopeRegion == null)
            {
                throw new ArgumentNullException(nameof(slopeRegion));
            }

            if (cellSize <= 0f || float.IsNaN(cellSize) || float.IsInfinity(cellSize))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSize),
                    cellSize,
                    "Cell size must be finite and positive.");
            }

            return GetSlopeMembershipWeightUnchecked(
                slopeRegion,
                slopeCoordinate,
                worldPosition,
                gridOrigin,
                cellSize);
        }

        private static float GetSlopeMembershipWeightUnchecked(
            SlopeRegion slopeRegion,
            Vector3Int slopeCoordinate,
            Vector3 worldPosition,
            Vector3 gridOrigin,
            float cellSize)
        {
            float worldAxisPosition = GetAxisPosition(worldPosition, slopeRegion.Axis);
            float originAxisPosition = GetAxisPosition(gridOrigin, slopeRegion.Axis);
            float gridAxisPosition = (worldAxisPosition - originAxisPosition) / cellSize;
            int lowerAxisCoordinate = Mathf.FloorToInt(gridAxisPosition);
            float fractionToNextCoordinate = gridAxisPosition - lowerAxisCoordinate;
            int perpendicularCoordinate = slopeRegion.Axis == SlopeAxis.X
                ? slopeCoordinate.z
                : slopeCoordinate.x;
            float lowerMembership = ContainsHorizontalCell(
                slopeRegion,
                lowerAxisCoordinate,
                perpendicularCoordinate)
                ? 1f
                : 0f;
            float upperMembership = ContainsHorizontalCell(
                slopeRegion,
                lowerAxisCoordinate + 1,
                perpendicularCoordinate)
                ? 1f
                : 0f;
            return Mathf.Lerp(lowerMembership, upperMembership, fractionToNextCoordinate);
        }

        private static bool ContainsHorizontalCell(
            SlopeRegion slopeRegion,
            int axisCoordinate,
            int perpendicularCoordinate)
        {
            return slopeRegion.Axis == SlopeAxis.X
                ? slopeRegion.ContainsHorizontalCell(axisCoordinate, perpendicularCoordinate)
                : slopeRegion.ContainsHorizontalCell(perpendicularCoordinate, axisCoordinate);
        }

        private static float GetAxisPosition(Vector3 worldPosition, SlopeAxis axis)
        {
            return axis == SlopeAxis.X ? worldPosition.x : worldPosition.z;
        }
    }
}
