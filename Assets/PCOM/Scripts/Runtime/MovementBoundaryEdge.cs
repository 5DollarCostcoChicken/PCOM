using System;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// One horizontal grid edge owned by the reachable tile on its inward side.
    /// Shared edges always belong to the lower AP band, making their color deterministic.
    /// </summary>
    public readonly struct MovementBoundaryEdge : IEquatable<MovementBoundaryEdge>
    {
        public MovementBoundaryEdge(Vector3Int tileCoordinate, Vector3Int outwardDirection, int actionPointBand)
        {
            TileCoordinate = tileCoordinate;
            OutwardDirection = outwardDirection;
            ActionPointBand = actionPointBand;
        }

        public Vector3Int TileCoordinate { get; }
        public Vector3Int OutwardDirection { get; }
        public int ActionPointBand { get; }

        public bool Equals(MovementBoundaryEdge other)
        {
            return TileCoordinate == other.TileCoordinate &&
                   OutwardDirection == other.OutwardDirection &&
                   ActionPointBand == other.ActionPointBand;
        }

        public override bool Equals(object obj) => obj is MovementBoundaryEdge other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = TileCoordinate.GetHashCode();
                hash = (hash * 397) ^ OutwardDirection.GetHashCode();
                return (hash * 397) ^ ActionPointBand;
            }
        }
    }
}
