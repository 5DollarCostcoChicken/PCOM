using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    public sealed class SlopeRegionAnalysis
    {
        internal SlopeRegionAnalysis(
            SlopeRegion region,
            string failureReason,
            Vector3Int[] discoveredCoordinates)
        {
            Region = region;
            FailureReason = failureReason;
            DiscoveredCoordinates = Array.AsReadOnly(discoveredCoordinates);
        }

        public bool IsValid => Region != null;
        public SlopeRegion Region { get; }
        public string FailureReason { get; }
        public IReadOnlyList<Vector3Int> DiscoveredCoordinates { get; }
    }
}
