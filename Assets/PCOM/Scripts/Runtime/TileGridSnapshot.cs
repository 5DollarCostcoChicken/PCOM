using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Stable per-request snapshot and column index of TileManager's sparse grid.
    /// </summary>
    public sealed class TileGridSnapshot : IGridPathMap
    {
        private readonly Dictionary<Vector3Int, TileManager.TileType> tiles;
        private readonly Dictionary<Vector2Int, List<Vector3Int>> walkableColumns;

        public TileGridSnapshot(IReadOnlyDictionary<Vector3Int, TileManager.TileType> sourceTiles)
        {
            if (sourceTiles == null)
            {
                throw new ArgumentNullException(nameof(sourceTiles));
            }

            tiles = new Dictionary<Vector3Int, TileManager.TileType>(sourceTiles.Count);
            walkableColumns = new Dictionary<Vector2Int, List<Vector3Int>>();
            foreach (KeyValuePair<Vector3Int, TileManager.TileType> tile in sourceTiles)
            {
                tiles.Add(tile.Key, tile.Value);
                if (!TileManager.IsMovementSelectableTileType(tile.Value))
                {
                    continue;
                }

                Vector2Int column = new Vector2Int(tile.Key.x, tile.Key.z);
                if (!walkableColumns.TryGetValue(column, out List<Vector3Int> coordinates))
                {
                    coordinates = new List<Vector3Int>();
                    walkableColumns.Add(column, coordinates);
                }

                coordinates.Add(tile.Key);
            }

            foreach (List<Vector3Int> coordinates in walkableColumns.Values)
            {
                coordinates.Sort(CompareCoordinatesByElevation);
            }
        }

        public bool TryGetTile(Vector3Int coordinate, out TileManager.TileType tileType)
        {
            return tiles.TryGetValue(coordinate, out tileType);
        }

        public void CollectWalkableCoordinatesInColumn(int x, int z, List<Vector3Int> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            if (!walkableColumns.TryGetValue(new Vector2Int(x, z), out List<Vector3Int> coordinates))
            {
                return;
            }

            results.AddRange(coordinates);
        }

        private static int CompareCoordinatesByElevation(Vector3Int left, Vector3Int right)
        {
            int yComparison = left.y.CompareTo(right.y);
            if (yComparison != 0)
            {
                return yComparison;
            }

            int xComparison = left.x.CompareTo(right.x);
            return xComparison != 0 ? xComparison : left.z.CompareTo(right.z);
        }
    }
}
