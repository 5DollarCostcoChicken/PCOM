using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PCOM.Tests
{
    public class MovementVisualizationLogicTests
    {
        [Test]
        public void ReachabilityAssignsTilesToCeilingActionPointBands()
        {
            TileGridSnapshot map = CreateMap(
                Tile(Vector3Int.zero),
                Tile(Vector3Int.right),
                Tile(Vector3Int.right * 2),
                Tile(Vector3Int.right * 3));

            MovementReachabilityResult result = new GridPathfinder().CalculateReachability(
                map, Vector3Int.zero, 0, 0, 1f, 1f, 3);

            Assert.That(result.TryGetActionPointBand(Vector3Int.right, out int oneAp), Is.True);
            Assert.That(oneAp, Is.EqualTo(1));
            Assert.That(result.TryGetActionPointBand(Vector3Int.right * 2, out int twoAp), Is.True);
            Assert.That(twoAp, Is.EqualTo(2));
            Assert.That(result.TryGetActionPointBand(Vector3Int.right * 3, out int threeAp), Is.True);
            Assert.That(threeAp, Is.EqualTo(3));
        }

        [Test]
        public void ReachabilityTreatsHazardsAsOnePhysicalMovementStep()
        {
            TileGridSnapshot map = CreateMap(
                Tile(Vector3Int.zero),
                Tile(Vector3Int.right, TileManager.TileType.Hazard),
                Tile(Vector3Int.right * 2));

            MovementReachabilityResult result = new GridPathfinder().CalculateReachability(
                map, Vector3Int.zero, 0, 0, 2f, 3f, 2);

            Assert.That(result.TryGetCost(Vector3Int.right * 2, out float cost), Is.True);
            Assert.That(cost, Is.EqualTo(2f));
            Assert.That(result.TryGetActionPointBand(Vector3Int.right * 2, out int band), Is.True);
            Assert.That(band, Is.EqualTo(1));
        }

        [Test]
        public void SlopeHeightCorrectionUsesContinuousHorizontalSlopeMembership()
        {
            TileGridSnapshot map = CreateMap(
                Tile(new Vector3Int(0, 1, 0), TileManager.TileType.Slope),
                Tile(Vector3Int.zero, TileManager.TileType.Slope),
                Tile(Vector3Int.right, TileManager.TileType.Slope));
            SlopeRegion region = new SlopeRegionAnalyzer().Analyze(
                map,
                Vector3Int.zero,
                Vector3.zero,
                1f).Region;

            Vector3Int slopeRow = Vector3Int.zero;
            float[] samplePositions = { -0.5f, 0f, 0.5f, 1f, 1.5f, 2f };
            float[] expectedHeights = { 1f, 1f, 0.75f, 0.5f, 0.25f, 0f };

            for (int sampleIndex = 0; sampleIndex < samplePositions.Length; sampleIndex++)
            {
                Vector3 samplePosition = new Vector3(samplePositions[sampleIndex], 0f, 0f);
                float correctedHeight = region.EvaluateWorldHeight(samplePosition) +
                                        SlopeVisualizationHeightResolver.GetSlopeHeightOffset(
                                            region,
                                            slopeRow,
                                            samplePosition,
                                            Vector3.zero,
                                            1f,
                                            0.5f);
                Assert.That(
                    correctedHeight,
                    Is.EqualTo(expectedHeights[sampleIndex]).Within(0.0001f));
            }

            float highRowOffset = SlopeVisualizationHeightResolver.GetSlopeHeightOffset(
                region,
                new Vector3Int(0, 1, 0),
                new Vector3(1.5f, 0f, 0f),
                Vector3.zero,
                1f,
                0.5f);
            float lowRowOffset = SlopeVisualizationHeightResolver.GetSlopeHeightOffset(
                region,
                new Vector3Int(1, 0, 0),
                new Vector3(1.5f, 0f, 0f),
                Vector3.zero,
                1f,
                0.5f);

            Assert.That(highRowOffset, Is.EqualTo(lowRowOffset).Within(0.0001f));
        }

        [Test]
        public void SlopeHeightCorrectionUsesGridOriginAndCellSizeForMembership()
        {
            Vector3 gridOrigin = new Vector3(10f, 3f, -4f);
            TileGridSnapshot map = CreateMap(
                Tile(new Vector3Int(0, 1, 0), TileManager.TileType.Slope),
                Tile(Vector3Int.zero, TileManager.TileType.Slope),
                Tile(Vector3Int.right, TileManager.TileType.Slope));
            SlopeRegion region = new SlopeRegionAnalyzer().Analyze(
                map,
                Vector3Int.zero,
                gridOrigin,
                2f).Region;

            Assert.That(
                SlopeVisualizationHeightResolver.GetSlopeMembershipWeight(
                    region,
                    Vector3Int.zero,
                    new Vector3(9f, 0f, -4f),
                    gridOrigin,
                    2f),
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                SlopeVisualizationHeightResolver.GetSlopeMembershipWeight(
                    region,
                    Vector3Int.zero,
                    new Vector3(10f, 0f, -4f),
                    gridOrigin,
                    2f),
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                SlopeVisualizationHeightResolver.GetSlopeMembershipWeight(
                    region,
                    Vector3Int.zero,
                    new Vector3(13f, 0f, -4f),
                    gridOrigin,
                    2f),
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                SlopeVisualizationHeightResolver.GetSlopeMembershipWeight(
                    region,
                    Vector3Int.zero,
                    new Vector3(14f, 0f, -4f),
                    gridOrigin,
                    2f),
                Is.Zero);
        }

        [Test]
        public void SlopeHeightCorrectionUsesTheSameMembershipEnvelopeAlongZ()
        {
            TileGridSnapshot map = CreateMap(
                Tile(new Vector3Int(0, 1, 0), TileManager.TileType.Slope),
                Tile(Vector3Int.zero, TileManager.TileType.Slope),
                Tile(new Vector3Int(0, 0, 1), TileManager.TileType.Slope));
            SlopeRegion region = new SlopeRegionAnalyzer().Analyze(
                map,
                Vector3Int.zero,
                Vector3.zero,
                1f).Region;

            Assert.That(region.Axis, Is.EqualTo(SlopeAxis.Z));
            float[] samplePositions = { -0.5f, 0f, 0.5f, 1f, 1.5f, 2f };
            float[] expectedHeights = { 1f, 1f, 0.75f, 0.5f, 0.25f, 0f };
            for (int sampleIndex = 0; sampleIndex < samplePositions.Length; sampleIndex++)
            {
                Vector3 samplePosition = new Vector3(0f, 0f, samplePositions[sampleIndex]);
                float correctedHeight = region.EvaluateWorldHeight(samplePosition) +
                                        SlopeVisualizationHeightResolver.GetSlopeHeightOffset(
                                            region,
                                            Vector3Int.zero,
                                            samplePosition,
                                            Vector3.zero,
                                            1f,
                                            0.5f);
                Assert.That(
                    correctedHeight,
                    Is.EqualTo(expectedHeights[sampleIndex]).Within(0.0001f));
            }
        }

        [Test]
        public void BoundaryGeneratorOmitsSameBandInteriorAndOwnsSharedBandEdgeByCheaperBand()
        {
            MovementReachabilityResult result = new MovementReachabilityResult(
                new Dictionary<Vector3Int, float>
                {
                    { Vector3Int.zero, 0f },
                    { Vector3Int.right, 1f },
                    { new Vector3Int(1, 0, 1), 1f },
                    { Vector3Int.right * 2, 2f }
                },
                1f,
                3);
            List<MovementBoundaryEdge> edges = new List<MovementBoundaryEdge>();

            MovementBoundaryGenerator.CollectEdges(result, edges);

            Assert.That(edges, Has.None.Matches<MovementBoundaryEdge>(edge =>
                edge.TileCoordinate == Vector3Int.right && edge.OutwardDirection == new Vector3Int(0, 0, 1)));
            Assert.That(edges.FindAll(edge =>
                edge.TileCoordinate == Vector3Int.right && edge.OutwardDirection == Vector3Int.right &&
                edge.ActionPointBand == 1), Has.Count.EqualTo(1));
            Assert.That(edges.FindAll(edge =>
                edge.TileCoordinate == Vector3Int.right * 2 && edge.OutwardDirection == Vector3Int.right),
                Has.Count.EqualTo(1));
        }

        [Test]
        public void BoundaryGeneratorDrawsOneInterbandBoundaryForOneContinuousSlopeSurface()
        {
            Vector3Int upperSlope = new Vector3Int(0, 1, 0);
            Vector3Int middleSlope = Vector3Int.zero;
            Vector3Int lowerSlope = Vector3Int.right;
            TileGridSnapshot map = CreateMap(
                Tile(upperSlope, TileManager.TileType.Slope),
                Tile(middleSlope, TileManager.TileType.Slope),
                Tile(lowerSlope, TileManager.TileType.Slope));
            SlopeRegion slopeRegion = new SlopeRegionAnalyzer().Analyze(
                map,
                middleSlope,
                Vector3.zero,
                1f).Region;
            MovementReachabilityResult result = new MovementReachabilityResult(
                new Dictionary<Vector3Int, float>
                {
                    { upperSlope, 1f },
                    { middleSlope, 1f },
                    { lowerSlope, 2f }
                },
                1f,
                3);
            List<MovementBoundaryEdge> edges = new List<MovementBoundaryEdge>();

            MovementBoundaryGenerator.CollectEdges(
                result,
                edges,
                new TestSlopeResolver(slopeRegion));

            List<MovementBoundaryEdge> sharedSurfaceEdges = edges.FindAll(edge =>
                edge.OutwardDirection.x != 0 &&
                edge.TileCoordinate.x + (edge.OutwardDirection.x > 0 ? 1 : 0) == 1 &&
                edge.TileCoordinate.z == 0);
            Assert.That(sharedSurfaceEdges, Has.Count.EqualTo(1));
            Assert.That(sharedSurfaceEdges[0].ActionPointBand, Is.EqualTo(1));
        }

        [Test]
        public void BoundaryGeneratorOmitsSameBandEdgesBetweenVerticallyOffsetSlopeVoxels()
        {
            Vector3Int upperSlope = new Vector3Int(0, 1, 0);
            Vector3Int middleSlope = Vector3Int.zero;
            Vector3Int lowerSlope = Vector3Int.right;
            TileGridSnapshot map = CreateMap(
                Tile(upperSlope, TileManager.TileType.Slope),
                Tile(middleSlope, TileManager.TileType.Slope),
                Tile(lowerSlope, TileManager.TileType.Slope));
            SlopeRegion slopeRegion = new SlopeRegionAnalyzer().Analyze(
                map,
                middleSlope,
                Vector3.zero,
                1f).Region;
            MovementReachabilityResult result = new MovementReachabilityResult(
                new Dictionary<Vector3Int, float>
                {
                    { upperSlope, 1f },
                    { middleSlope, 2f },
                    { lowerSlope, 1f }
                },
                1f,
                3);
            List<MovementBoundaryEdge> edges = new List<MovementBoundaryEdge>();

            MovementBoundaryGenerator.CollectEdges(
                result,
                edges,
                new TestSlopeResolver(slopeRegion));

            Assert.That(edges, Has.None.Matches<MovementBoundaryEdge>(edge =>
                edge.OutwardDirection.x != 0 &&
                edge.TileCoordinate.x + (edge.OutwardDirection.x > 0 ? 1 : 0) == 1 &&
                edge.TileCoordinate.z == 0));
            Assert.That(edges, Has.Exactly(1).Matches<MovementBoundaryEdge>(edge =>
                edge.TileCoordinate == upperSlope && edge.OutwardDirection == Vector3Int.left));
            Assert.That(edges, Has.Exactly(1).Matches<MovementBoundaryEdge>(edge =>
                edge.TileCoordinate == lowerSlope && edge.OutwardDirection == Vector3Int.right));
        }

        [Test]
        public void BoundaryGeneratorUsesTheTopSlopeMemberForAdjacentBandComparison()
        {
            Vector3Int upperSource = new Vector3Int(0, 1, 0);
            Vector3Int lowerSource = Vector3Int.zero;
            Vector3Int upperTarget = new Vector3Int(1, 1, 0);
            Vector3Int lowerTarget = Vector3Int.right;
            TileGridSnapshot map = CreateMap(
                Tile(upperSource, TileManager.TileType.Slope),
                Tile(lowerSource, TileManager.TileType.Slope),
                Tile(upperTarget, TileManager.TileType.Slope),
                Tile(lowerTarget, TileManager.TileType.Slope));
            SlopeRegion slopeRegion = new SlopeRegionAnalyzer().Analyze(
                map,
                upperSource,
                Vector3.zero,
                1f).Region;
            MovementReachabilityResult result = new MovementReachabilityResult(
                new Dictionary<Vector3Int, float>
                {
                    { upperSource, 1f },
                    { lowerSource, 1f },
                    { upperTarget, 1f },
                    { lowerTarget, 2f }
                },
                1f,
                3);
            List<MovementBoundaryEdge> edges = new List<MovementBoundaryEdge>();

            MovementBoundaryGenerator.CollectEdges(
                result,
                edges,
                new TestSlopeResolver(slopeRegion));

            Assert.That(edges, Has.None.Matches<MovementBoundaryEdge>(edge =>
                edge.OutwardDirection.x != 0 &&
                edge.TileCoordinate.x + (edge.OutwardDirection.x > 0 ? 1 : 0) == 1 &&
                edge.TileCoordinate.z == 0));
        }

        [Test]
        public void BoundaryGeneratorRetainsSlopeBoundaryAtAHiddenOriginColumn()
        {
            Vector3Int upperSlope = new Vector3Int(0, 1, 0);
            Vector3Int middleSlope = Vector3Int.zero;
            Vector3Int lowerSlope = Vector3Int.right;
            TileGridSnapshot map = CreateMap(
                Tile(upperSlope, TileManager.TileType.Slope),
                Tile(middleSlope, TileManager.TileType.Slope),
                Tile(lowerSlope, TileManager.TileType.Slope));
            SlopeRegion slopeRegion = new SlopeRegionAnalyzer().Analyze(
                map,
                middleSlope,
                Vector3.zero,
                1f).Region;
            MovementReachabilityResult result = new MovementReachabilityResult(
                new Dictionary<Vector3Int, float>
                {
                    { upperSlope, 1f },
                    { lowerSlope, 0f }
                },
                1f,
                3);
            List<MovementBoundaryEdge> edges = new List<MovementBoundaryEdge>();

            MovementBoundaryGenerator.CollectEdges(
                result,
                edges,
                new TestSlopeResolver(slopeRegion));

            Assert.That(edges, Has.Exactly(1).Matches<MovementBoundaryEdge>(edge =>
                edge.TileCoordinate == upperSlope &&
                edge.OutwardDirection == Vector3Int.right &&
                edge.ActionPointBand == 1));
        }

        [Test]
        public void CoverClassifierRequiresContiguousWallCellsAtStandingElevation()
        {
            Vector3Int destination = Vector3Int.zero;
            TileGridSnapshot map = CreateMap(
                Tile(destination),
                Tile(destination + Vector3Int.right, TileManager.TileType.Wall),
                Tile(destination + Vector3Int.right + Vector3Int.up, TileManager.TileType.Wall),
                Tile(destination + Vector3Int.left, TileManager.TileType.Wall),
                Tile(destination + Vector3Int.left + (Vector3Int.up * 2), TileManager.TileType.Wall));
            List<DestinationCover> cover = new List<DestinationCover>();

            DestinationCoverClassifier.CollectCover(map, destination, cover);

            Assert.That(cover, Has.Exactly(1).Matches<DestinationCover>(result =>
                result.Direction == Vector3Int.right && result.Type == DestinationCoverType.Full));
            Assert.That(cover, Has.Exactly(1).Matches<DestinationCover>(result =>
                result.Direction == Vector3Int.left && result.Type == DestinationCoverType.Half));
        }

        private static TileGridSnapshot CreateMap(
            params KeyValuePair<Vector3Int, TileManager.TileType>[] tiles)
        {
            Dictionary<Vector3Int, TileManager.TileType> dictionary =
                new Dictionary<Vector3Int, TileManager.TileType>();
            for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
            {
                dictionary.Add(tiles[tileIndex].Key, tiles[tileIndex].Value);
            }

            return new TileGridSnapshot(dictionary);
        }

        private static KeyValuePair<Vector3Int, TileManager.TileType> Tile(
            Vector3Int coordinate,
            TileManager.TileType tileType = TileManager.TileType.Floor)
        {
            return new KeyValuePair<Vector3Int, TileManager.TileType>(coordinate, tileType);
        }

        private sealed class TestSlopeResolver : IMovementPresentationSlopeResolver
        {
            private readonly SlopeRegion slopeRegion;

            public TestSlopeResolver(SlopeRegion slopeRegion)
            {
                this.slopeRegion = slopeRegion;
            }

            public bool TryGetContinuousSlopeRegion(
                Vector3Int source,
                Vector3Int destination,
                out SlopeRegion resolvedSlopeRegion)
            {
                resolvedSlopeRegion = slopeRegion;
                return slopeRegion != null &&
                       slopeRegion.Contains(source) &&
                       slopeRegion.Contains(destination);
            }
        }
    }
}
