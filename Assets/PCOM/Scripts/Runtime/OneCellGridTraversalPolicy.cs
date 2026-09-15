using System;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Temporary one-cell-tall grid clearance policy. Missing, Air, and Chasm headroom are clear.
    /// </summary>
    public sealed class OneCellGridTraversalPolicy : IGridTraversalPolicy
    {
        private readonly IGridPathMap map;

        public OneCellGridTraversalPolicy(IGridPathMap map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
        }

        public bool CanEnter(Vector3Int coordinate)
        {
            Vector3Int headCoordinate = coordinate + Vector3Int.up;
            if (!map.TryGetTile(headCoordinate, out TileManager.TileType headTileType))
            {
                return true;
            }

            return headTileType == TileManager.TileType.Air ||
                   headTileType == TileManager.TileType.Chasm;
        }

        public bool CanTraverse(Vector3Int fromCoordinate, Vector3Int toCoordinate)
        {
            int horizontalDistance = Mathf.Abs(toCoordinate.x - fromCoordinate.x) +
                                     Mathf.Abs(toCoordinate.z - fromCoordinate.z);
            return horizontalDistance == 1 && CanEnter(fromCoordinate) && CanEnter(toCoordinate);
        }
    }
}
