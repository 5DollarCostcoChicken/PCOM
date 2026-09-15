using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PCOM.Tests
{
    public class MovementPresentationPathBuilderTests
    {
        private MovementPresentationPathBuilder builder;
        private TestGridMap map;

        [SetUp]
        public void SetUp()
        {
            builder = new MovementPresentationPathBuilder();
            map = new TestGridMap();
        }

        [Test]
        public void OpenSquareStaircaseBecomesOneDiagonalWithoutChangingLogicalNodes()
        {
            FillFloorRectangle(0, 2, 0, 2);
            List<Vector3Int> logicalPath = new List<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(1, 0, 1),
                new Vector3Int(2, 0, 1),
                new Vector3Int(2, 0, 2)
            };
            Vector3Int[] originalNodes = logicalPath.ToArray();

            IReadOnlyList<MovementPresentationSegment> segments = builder.Build(map, logicalPath);

            Assert.That(segments, Has.Count.EqualTo(1));
            Assert.That(segments[0].Type, Is.EqualTo(MovementPresentationSegmentType.FlatRun));
            Assert.That(segments[0].LogicalStartIndex, Is.Zero);
            Assert.That(segments[0].LogicalEndIndex, Is.EqualTo(4));
            Assert.That(logicalPath, Is.EqualTo(originalNodes));
        }

        [Test]
        public void RectangularStaircaseUsesItsActualEndpointDirection()
        {
            FillFloorRectangle(0, 3, 0, 2);
            List<Vector3Int> logicalPath = new List<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(1, 0, 1),
                new Vector3Int(2, 0, 1),
                new Vector3Int(3, 0, 1),
                new Vector3Int(3, 0, 2)
            };

            MovementPresentationSegment segment = builder.Build(map, logicalPath)[0];
            Vector3 displacement = segment.EndCoordinate - segment.StartCoordinate;
            float angle = Mathf.Atan2(displacement.z, displacement.x) * Mathf.Rad2Deg;

            Assert.That(segment.LogicalEndIndex, Is.EqualTo(logicalPath.Count - 1));
            Assert.That(angle, Is.EqualTo(Mathf.Atan2(2f, 3f) * Mathf.Rad2Deg).Within(0.0001f));
        }

        [TestCase(TileManager.TileType.Wall)]
        [TestCase(TileManager.TileType.Chasm)]
        public void ImpassableSupercoverCellShortensDiagonal(TileManager.TileType blockedType)
        {
            FillFloorRectangle(0, 2, 0, 2);
            map.Set(new Vector3Int(0, 0, 1), blockedType);
            List<Vector3Int> logicalPath = CreateSquareStaircase();

            IReadOnlyList<MovementPresentationSegment> segments = builder.Build(map, logicalPath);

            Assert.That(segments, Has.Count.GreaterThan(1));
            Assert.That(segments[0].LogicalEndIndex, Is.LessThan(logicalPath.Count - 1));
        }

        [Test]
        public void MissingSupercoverCellShortensDiagonal()
        {
            FillFloorRectangle(0, 2, 0, 2);
            map.Remove(new Vector3Int(0, 0, 1));

            IReadOnlyList<MovementPresentationSegment> segments =
                builder.Build(map, CreateSquareStaircase());

            Assert.That(segments, Has.Count.GreaterThan(1));
        }

        [Test]
        public void AvoidedHazardInSupercoverShortensDiagonal()
        {
            FillFloorRectangle(0, 2, 0, 2);
            map.Set(new Vector3Int(0, 0, 1), TileManager.TileType.Hazard);

            IReadOnlyList<MovementPresentationSegment> segments =
                builder.Build(map, CreateSquareStaircase());

            Assert.That(segments, Has.Count.GreaterThan(1));
        }

        [Test]
        public void LogicalHazardCreatesExplicitEntryAndExitBoundaries()
        {
            Vector3Int start = Vector3Int.zero;
            Vector3Int hazard = Vector3Int.right;
            Vector3Int end = Vector3Int.right * 2;
            map.Set(start, TileManager.TileType.Floor);
            map.Set(hazard, TileManager.TileType.Hazard);
            map.Set(end, TileManager.TileType.Floor);

            IReadOnlyList<MovementPresentationSegment> segments = builder.Build(
                map,
                new[] { start, hazard, end });

            Assert.That(segments, Has.Count.EqualTo(2));
            Assert.That(segments[0].EndCoordinate, Is.EqualTo(hazard));
            Assert.That(segments[1].StartCoordinate, Is.EqualTo(hazard));
        }

        [TestCase(1, MovementPresentationSegmentType.Clamber)]
        [TestCase(2, MovementPresentationSegmentType.Jump)]
        [TestCase(-1, MovementPresentationSegmentType.Drop)]
        public void ElevationTransitionsHaveExplicitTypes(
            int elevationDifference,
            MovementPresentationSegmentType expectedType)
        {
            Vector3Int start = Vector3Int.zero;
            Vector3Int end = new Vector3Int(1, elevationDifference, 0);
            map.Set(start, TileManager.TileType.Floor);
            map.Set(end, TileManager.TileType.Floor);

            Assert.That(
                builder.ClassifyTransition(map, start, end),
                Is.EqualTo(expectedType));
        }

        [Test]
        public void LadderAscentAndSlopeTransitionsAreNotSmoothedAsFlatRuns()
        {
            Vector3Int ladder = Vector3Int.zero;
            Vector3Int ladderTop = new Vector3Int(1, 4, 0);
            map.Set(ladder, TileManager.TileType.Ladder);
            map.Set(ladderTop, TileManager.TileType.Floor);
            Assert.That(
                builder.ClassifyTransition(map, ladder, ladderTop),
                Is.EqualTo(MovementPresentationSegmentType.LadderAscent));

            Vector3Int slope = new Vector3Int(2, 0, 0);
            Vector3Int slopeEnd = new Vector3Int(3, 1, 0);
            map.Set(slope, TileManager.TileType.Slope);
            map.Set(slopeEnd, TileManager.TileType.Slope);
            Assert.That(
                builder.ClassifyTransition(map, slope, slopeEnd),
                Is.EqualTo(MovementPresentationSegmentType.SlopeRun));
        }

        [Test]
        public void StaircaseAcrossOneWideSlopeBecomesOneDiagonalSlopeRun()
        {
            for (int z = 0; z <= 2; z++)
            {
                map.Set(new Vector3Int(0, 2, z), TileManager.TileType.Slope);
                map.Set(new Vector3Int(0, 1, z), TileManager.TileType.Slope);
                map.Set(new Vector3Int(1, 1, z), TileManager.TileType.Slope);
                map.Set(new Vector3Int(1, 0, z), TileManager.TileType.Slope);
                map.Set(new Vector3Int(2, 0, z), TileManager.TileType.Slope);
            }

            SlopeRegionAnalysis analysis = new SlopeRegionAnalyzer().Analyze(
                map,
                new Vector3Int(0, 2, 0),
                Vector3.zero,
                1f);
            Assert.That(analysis.IsValid, Is.True, analysis.FailureReason);
            List<Vector3Int> logicalPath = new List<Vector3Int>
            {
                new Vector3Int(0, 2, 0),
                new Vector3Int(0, 2, 1),
                new Vector3Int(1, 1, 1),
                new Vector3Int(1, 1, 2),
                new Vector3Int(2, 0, 2)
            };

            IReadOnlyList<MovementPresentationSegment> segments = builder.Build(
                map,
                logicalPath,
                null,
                new TestSlopeResolver(analysis.Region));

            Assert.That(segments, Has.Count.EqualTo(1));
            Assert.That(segments[0].Type, Is.EqualTo(MovementPresentationSegmentType.SlopeRun));
            Assert.That(segments[0].LogicalStartIndex, Is.Zero);
            Assert.That(segments[0].LogicalEndIndex, Is.EqualTo(logicalPath.Count - 1));
        }

        private List<Vector3Int> CreateSquareStaircase()
        {
            return new List<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(1, 0, 1),
                new Vector3Int(2, 0, 1),
                new Vector3Int(2, 0, 2)
            };
        }

        private void FillFloorRectangle(int minimumX, int maximumX, int minimumZ, int maximumZ)
        {
            for (int x = minimumX; x <= maximumX; x++)
            {
                for (int z = minimumZ; z <= maximumZ; z++)
                {
                    map.Set(new Vector3Int(x, 0, z), TileManager.TileType.Floor);
                }
            }
        }

        private sealed class TestGridMap : IGridPathMap
        {
            private readonly Dictionary<Vector3Int, TileManager.TileType> tiles =
                new Dictionary<Vector3Int, TileManager.TileType>();

            public void Set(Vector3Int coordinate, TileManager.TileType tileType)
            {
                tiles[coordinate] = tileType;
            }

            public void Remove(Vector3Int coordinate)
            {
                tiles.Remove(coordinate);
            }

            public bool TryGetTile(Vector3Int coordinate, out TileManager.TileType tileType)
            {
                return tiles.TryGetValue(coordinate, out tileType);
            }

            public void CollectWalkableCoordinatesInColumn(
                int x,
                int z,
                List<Vector3Int> results)
            {
                results.Clear();
                foreach (KeyValuePair<Vector3Int, TileManager.TileType> tile in tiles)
                {
                    if (tile.Key.x == x &&
                        tile.Key.z == z &&
                        TileManager.IsMovementSelectableTileType(tile.Value))
                    {
                        results.Add(tile.Key);
                    }
                }
            }
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
                out SlopeRegion resolvedRegion)
            {
                bool isContinuous = slopeRegion.Contains(source) &&
                                    slopeRegion.Contains(destination);
                resolvedRegion = isContinuous ? slopeRegion : null;
                return isContinuous;
            }
        }
    }
}
