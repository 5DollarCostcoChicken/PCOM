using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Isolated hook for clearance, occupancy, reservations, and turn-state restrictions.
    /// </summary>
    public interface IGridTraversalPolicy
    {
        bool CanEnter(Vector3Int coordinate);

        bool CanTraverse(Vector3Int fromCoordinate, Vector3Int toCoordinate);
    }
}
