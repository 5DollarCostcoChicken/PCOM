using UnityEngine;

namespace PCOM
{
    public enum DestinationCoverType
    {
        None = 0,
        Half = 1,
        Full = 2
    }

    /// <summary>Geometric cover supplied by a wall column immediately beside a destination.</summary>
    public readonly struct DestinationCover
    {
        public DestinationCover(Vector3Int direction, DestinationCoverType type)
        {
            Direction = direction;
            Type = type;
        }

        public Vector3Int Direction { get; }
        public DestinationCoverType Type { get; }
    }
}
