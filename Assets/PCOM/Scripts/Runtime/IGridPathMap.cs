using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Read-only sparse grid contract consumed by pathfinding and slope analysis.
    /// </summary>
    public interface IGridPathMap
    {
        bool TryGetTile(Vector3Int coordinate, out TileManager.TileType tileType);

        void CollectWalkableCoordinatesInColumn(int x, int z, List<Vector3Int> results);
    }
}
