using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Classifies cover from contiguous Wall cells in the adjacent cardinal column.
    /// Floor coordinates identify standing elevation, so inspection begins at destination.y.
    /// </summary>
    public static class DestinationCoverClassifier
    {
        private static readonly Vector3Int[] HorizontalDirections =
        {
            Vector3Int.left,
            new Vector3Int(0, 0, -1),
            new Vector3Int(0, 0, 1),
            Vector3Int.right
        };

        public static void CollectCover(
            IGridPathMap map,
            Vector3Int destinationCoordinate,
            List<DestinationCover> results)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            for (int directionIndex = 0; directionIndex < HorizontalDirections.Length; directionIndex++)
            {
                Vector3Int direction = HorizontalDirections[directionIndex];
                Vector3Int wallCoordinate = destinationCoordinate + direction;
                int wallCount = 0;
                while (map.TryGetTile(
                           wallCoordinate + (Vector3Int.up * wallCount),
                           out TileManager.TileType tileType) &&
                       tileType == TileManager.TileType.Wall)
                {
                    wallCount++;
                }

                if (wallCount == 1)
                {
                    results.Add(new DestinationCover(direction, DestinationCoverType.Half));
                }
                else if (wallCount >= 2)
                {
                    results.Add(new DestinationCover(direction, DestinationCoverType.Full));
                }
            }
        }
    }
}
