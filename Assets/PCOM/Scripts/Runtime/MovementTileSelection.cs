using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Describes a movement-selectable grid tile resolved from level geometry.
    /// </summary>
    public readonly struct MovementTileSelection
    {
        public MovementTileSelection(
            Vector3Int coordinate,
            TileManager.TileType tileType,
            Vector3 worldCenter,
            Vector3 physicsHitPoint,
            Vector3 physicsHitNormal)
        {
            Coordinate = coordinate;
            TileType = tileType;
            WorldCenter = worldCenter;
            PhysicsHitPoint = physicsHitPoint;
            PhysicsHitNormal = physicsHitNormal;
        }

        public Vector3Int Coordinate { get; }

        public TileManager.TileType TileType { get; }

        /// <summary>
        /// Gets the canonical grid-cell center supplied by <see cref="TileManager"/>.
        /// </summary>
        public Vector3 WorldCenter { get; }

        public Vector3 PhysicsHitPoint { get; }

        public Vector3 PhysicsHitNormal { get; }
    }
}
