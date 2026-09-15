using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Deterministically returns every horizontal grid cell crossed or corner-touched
    /// by a line between two cell centers.
    /// </summary>
    public static class GridSupercover
    {
        public static void CollectHorizontalCells(
            int startX,
            int startZ,
            int endX,
            int endZ,
            List<Vector2Int> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            int deltaX = endX - startX;
            int deltaZ = endZ - startZ;
            int countX = Mathf.Abs(deltaX);
            int countZ = Mathf.Abs(deltaZ);
            int stepX = Math.Sign(deltaX);
            int stepZ = Math.Sign(deltaZ);
            int x = startX;
            int z = startZ;
            int progressedX = 0;
            int progressedZ = 0;
            AddUnique(results, new Vector2Int(x, z));

            while (progressedX < countX || progressedZ < countZ)
            {
                long xDecision = (1L + (2L * progressedX)) * countZ;
                long zDecision = (1L + (2L * progressedZ)) * countX;
                if (xDecision == zDecision)
                {
                    AddUnique(results, new Vector2Int(x + stepX, z));
                    AddUnique(results, new Vector2Int(x, z + stepZ));
                    x += stepX;
                    z += stepZ;
                    progressedX++;
                    progressedZ++;
                }
                else if (xDecision < zDecision)
                {
                    x += stepX;
                    progressedX++;
                }
                else
                {
                    z += stepZ;
                    progressedZ++;
                }

                AddUnique(results, new Vector2Int(x, z));
            }
        }

        private static void AddUnique(List<Vector2Int> results, Vector2Int coordinate)
        {
            if (!results.Contains(coordinate))
            {
                results.Add(coordinate);
            }
        }
    }
}
