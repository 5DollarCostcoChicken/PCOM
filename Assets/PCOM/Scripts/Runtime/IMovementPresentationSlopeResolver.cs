using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Identifies transitions that genuinely follow one continuous slope plane.
    /// Merely touching a slope tile is not sufficient.
    /// </summary>
    public interface IMovementPresentationSlopeResolver
    {
        bool TryGetContinuousSlopeRegion(
            Vector3Int source,
            Vector3Int destination,
            out SlopeRegion slopeRegion);
    }
}
