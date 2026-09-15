using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Per-grid cache of slope analyses. TileManager rebuilds invalidate every cached component.
    /// </summary>
    public sealed class SlopeRegionCache : IDisposable
    {
        private readonly TileManager tileManager;
        private readonly SlopeRegionAnalyzer analyzer = new SlopeRegionAnalyzer();
        private readonly Dictionary<Vector3Int, SlopeRegionAnalysis> analyses =
            new Dictionary<Vector3Int, SlopeRegionAnalysis>();
        private TileGridSnapshot snapshot;

        public SlopeRegionCache(TileManager tileManager)
        {
            this.tileManager = tileManager != null
                ? tileManager
                : throw new ArgumentNullException(nameof(tileManager));
            tileManager.GridRebuilt += Invalidate;
        }

        public SlopeRegionAnalysis GetAnalysis(Vector3Int slopeCoordinate)
        {
            if (analyses.TryGetValue(slopeCoordinate, out SlopeRegionAnalysis cachedAnalysis))
            {
                return cachedAnalysis;
            }

            snapshot ??= new TileGridSnapshot(tileManager.Tiles);
            SlopeRegionAnalysis analysis = analyzer.Analyze(
                snapshot,
                slopeCoordinate,
                tileManager.GridOrigin,
                tileManager.CellSize);
            for (int coordinateIndex = 0;
                 coordinateIndex < analysis.DiscoveredCoordinates.Count;
                 coordinateIndex++)
            {
                analyses[analysis.DiscoveredCoordinates[coordinateIndex]] = analysis;
            }

            if (analysis.DiscoveredCoordinates.Count == 0)
            {
                analyses[slopeCoordinate] = analysis;
            }

            return analysis;
        }

        public void Invalidate()
        {
            analyses.Clear();
            snapshot = null;
        }

        public void Dispose()
        {
            tileManager.GridRebuilt -= Invalidate;
            Invalidate();
        }
    }
}
