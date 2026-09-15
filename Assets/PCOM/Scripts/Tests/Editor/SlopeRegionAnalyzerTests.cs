using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PCOM.Tests
{
    public class SlopeRegionAnalyzerTests
    {
        private readonly SlopeRegionAnalyzer analyzer = new SlopeRegionAnalyzer();

        [Test]
        public void ConnectedComponentDiscoveryUsesAllSixOrthogonalDirections()
        {
            TileGridSnapshot map = CreateSlopeMap(
                Vector3Int.zero,
                Vector3Int.left,
                Vector3Int.right,
                Vector3Int.up,
                Vector3Int.down,
                new Vector3Int(0, 0, -1),
                new Vector3Int(0, 0, 1));

            SlopeRegionAnalysis result = analyzer.Analyze(map, Vector3Int.zero, Vector3.zero, 1f);

            Assert.That(result.DiscoveredCoordinates.Count, Is.EqualTo(7));
        }

        [Test]
        public void DiagonallyTouchingSlopeIsNotConnected()
        {
            TileGridSnapshot map = CreateSlopeMap(
                Vector3Int.zero,
                new Vector3Int(1, 1, 0));

            SlopeRegionAnalysis result = analyzer.Analyze(map, Vector3Int.zero, Vector3.zero, 1f);

            Assert.That(result.DiscoveredCoordinates, Is.EqualTo(new[] { Vector3Int.zero }));
        }

        [Test]
        public void EndpointsAndAxisAreSelectedDeterministically()
        {
            TileGridSnapshot map = CreateStandardRampMap();

            SlopeRegionAnalysis first = analyzer.Analyze(map, Vector3Int.zero, Vector3.zero, 1f);
            SlopeRegionAnalysis second = analyzer.Analyze(map, Vector3Int.zero, Vector3.zero, 1f);

            Assert.That(first.IsValid, Is.True);
            Assert.That(first.Region.Axis, Is.EqualTo(SlopeAxis.X));
            Assert.That(first.Region.LowEndpoint, Is.EqualTo(Vector3Int.zero));
            Assert.That(first.Region.HighEndpoint, Is.EqualTo(new Vector3Int(2, 2, 0)));
            Assert.That(second.Region.LowEndpoint, Is.EqualTo(first.Region.LowEndpoint));
            Assert.That(second.Region.HighEndpoint, Is.EqualTo(first.Region.HighEndpoint));
        }

        [Test]
        public void AngleUsesRiseOverRunAndIsIndependentOfWorldOrigin()
        {
            Vector3 gridOrigin = new Vector3(50f, -20f, 75f);

            SlopeRegionAnalysis result = analyzer.Analyze(
                CreateStandardRampMap(),
                Vector3Int.zero,
                gridOrigin,
                2f);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Region.Rise, Is.EqualTo(4f));
            Assert.That(result.Region.Run, Is.EqualTo(4f));
            Assert.That(result.Region.AngleDegrees, Is.EqualTo(45f).Within(0.001f));
        }

        [Test]
        public void ContinuousHeightUsesAdjacentHighFloorAsBlendEndpoint()
        {
            SlopeRegion region = analyzer.Analyze(
                CreateStandardRampMap(),
                Vector3Int.zero,
                Vector3.zero,
                2f).Region;

            Assert.That(region.EvaluateWorldHeight(new Vector3(0f, 0f, 0f)), Is.EqualTo(0f));
            Assert.That(
                region.EvaluateWorldHeight(new Vector3(2f, 0f, 0f)),
                Is.EqualTo(4f / 3f).Within(0.001f));
            Assert.That(
                region.EvaluateWorldHeight(new Vector3(4f, 0f, 0f)),
                Is.EqualTo(8f / 3f).Within(0.001f));
            Assert.That(region.EvaluateWorldHeight(new Vector3(6f, 0f, 0f)), Is.EqualTo(4f));
        }

        [Test]
        public void ThreeCellLPlacesHighestSlopeCenterHalfwayDownRamp()
        {
            TileGridSnapshot map = CreateSlopeMap(
                new Vector3Int(0, 1, 0),
                Vector3Int.zero,
                Vector3Int.right);

            SlopeRegion region = analyzer.Analyze(
                map,
                Vector3Int.zero,
                Vector3.zero,
                1f).Region;

            Assert.That(region.EvaluateWorldHeight(Vector3.zero), Is.EqualTo(0.5f));
            Assert.That(region.EvaluateWorldHeight(Vector3.right), Is.EqualTo(0f));
        }

        [Test]
        public void FiveColumnRampPlacesHighestSlopeCenterTwentyPercentDown()
        {
            TileGridSnapshot map = CreateSlopeMap(
                new Vector3Int(0, 4, 0),
                new Vector3Int(0, 3, 0),
                new Vector3Int(1, 3, 0),
                new Vector3Int(1, 2, 0),
                new Vector3Int(2, 2, 0),
                new Vector3Int(2, 1, 0),
                new Vector3Int(3, 1, 0),
                new Vector3Int(3, 0, 0),
                new Vector3Int(4, 0, 0));

            SlopeRegion region = analyzer.Analyze(
                map,
                new Vector3Int(0, 4, 0),
                Vector3.zero,
                1f).Region;

            Assert.That(region.EvaluateWorldHeight(new Vector3(0f, 0f, 0f)),
                Is.EqualTo(3.2f).Within(0.001f));
            Assert.That(region.EvaluateWorldHeight(new Vector3(4f, 0f, 0f)), Is.EqualTo(0f));
        }

        [Test]
        public void ContinuousHeightClampsToConnectedFloorElevations()
        {
            SlopeRegion region = analyzer.Analyze(
                CreateStandardRampMap(),
                Vector3Int.zero,
                Vector3.zero,
                1f).Region;

            Assert.That(region.EvaluateWorldHeight(new Vector3(-10f, 0f, 0f)), Is.EqualTo(0f));
            Assert.That(region.EvaluateWorldHeight(new Vector3(10f, 0f, 0f)), Is.EqualTo(2f));
        }

        [Test]
        public void UphillAndDownhillQueriesEvaluateTheSamePlane()
        {
            SlopeRegion region = analyzer.Analyze(
                CreateStandardRampMap(),
                Vector3Int.zero,
                new Vector3(10f, 3f, -4f),
                1f).Region;
            float[] positions = { 10f, 10.5f, 11f, 11.5f, 12f, 12.5f, 13f };

            for (int positionIndex = 0; positionIndex < positions.Length; positionIndex++)
            {
                float uphillHeight = region.EvaluateWorldHeight(
                    new Vector3(positions[positionIndex], 0f, -4f));
                float downhillHeight = region.EvaluateWorldHeight(
                    new Vector3(positions[positions.Length - 1 - positionIndex], 0f, -4f));
                Assert.That(
                    uphillHeight,
                    Is.EqualTo(8f - downhillHeight).Within(0.001f));
            }
        }

        [Test]
        public void MinorVoxelVariationAcrossWidthDoesNotOverrideDominantSlopeAxis()
        {
            TileGridSnapshot map = CreateSlopeMap(
                new Vector3Int(0, 0, 0),
                new Vector3Int(0, 0, 1),
                new Vector3Int(0, 1, 1),
                new Vector3Int(0, 1, 2),
                new Vector3Int(0, 2, 2),
                new Vector3Int(0, 2, 3),
                new Vector3Int(0, 3, 3),
                new Vector3Int(1, 1, 1),
                new Vector3Int(1, 2, 1));

            SlopeRegionAnalysis result = analyzer.Analyze(
                map,
                Vector3Int.zero,
                Vector3.zero,
                1f);

            Assert.That(result.IsValid, Is.True, result.FailureReason);
            Assert.That(result.Region.Axis, Is.EqualTo(SlopeAxis.Z));
            Assert.That(result.Region.LowEndpoint, Is.EqualTo(Vector3Int.zero));
            Assert.That(result.Region.HighEndpoint, Is.EqualTo(new Vector3Int(0, 3, 3)));
        }

        [Test]
        public void AmbiguousTwoAxisSlopeFailsSafely()
        {
            TileGridSnapshot map = CreateSlopeMap(
                Vector3Int.zero,
                Vector3Int.right,
                new Vector3Int(1, 1, 0),
                new Vector3Int(0, 0, 1),
                new Vector3Int(0, 1, 1));

            SlopeRegionAnalysis result = analyzer.Analyze(map, Vector3Int.zero, Vector3.zero, 1f);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.FailureReason, Does.Contain("both X and Z"));
        }

        private static TileGridSnapshot CreateStandardRampMap()
        {
            return CreateSlopeMap(
                Vector3Int.zero,
                Vector3Int.right,
                new Vector3Int(1, 1, 0),
                new Vector3Int(2, 1, 0),
                new Vector3Int(2, 2, 0));
        }

        private static TileGridSnapshot CreateSlopeMap(params Vector3Int[] coordinates)
        {
            Dictionary<Vector3Int, TileManager.TileType> tiles =
                new Dictionary<Vector3Int, TileManager.TileType>();
            for (int coordinateIndex = 0; coordinateIndex < coordinates.Length; coordinateIndex++)
            {
                tiles.Add(coordinates[coordinateIndex], TileManager.TileType.Slope);
            }

            return new TileGridSnapshot(tiles);
        }
    }
}
