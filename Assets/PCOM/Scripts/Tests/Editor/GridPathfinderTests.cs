using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PCOM.Tests
{
    public class GridPathfinderTests
    {
        private const float LargeMovementAmount = 100f;
        private const int AvailableActionPoints = 3;
        private readonly GridPathfinder pathfinder = new GridPathfinder();

        [Test]
        public void SameHeightFloorMovementFindsOrderedPath()
        {
            TileGridSnapshot map = CreateMap(
                Tile(Vector3Int.zero),
                Tile(Vector3Int.right),
                Tile(Vector3Int.right * 2));

            GridPathResult result = Find(map, Vector3Int.zero, Vector3Int.right * 2);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.Coordinates, Is.EqualTo(new[]
            {
                Vector3Int.zero,
                Vector3Int.right,
                Vector3Int.right * 2
            }));
            Assert.That(result.WeightedCost, Is.EqualTo(2f));
        }

        [Test]
        public void PathNeverMovesVerticallyWithinSameColumn()
        {
            TileGridSnapshot map = CreateMap(
                Tile(Vector3Int.zero),
                Tile(Vector3Int.up));

            GridPathResult result = Find(map, Vector3Int.zero, Vector3Int.up, jumpHeight: 5);

            Assert.That(result.PathFound, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(GridPathFailureReason.NoLegalPath));
        }

        [Test]
        public void AscendingWithinJumpHeightSucceeds()
        {
            Vector3Int target = new Vector3Int(1, 2, 0);
            GridPathResult result = Find(
                CreateMap(Tile(Vector3Int.zero), Tile(target)),
                Vector3Int.zero,
                target,
                jumpHeight: 2);

            Assert.That(result.IsSuccessful, Is.True);
        }

        [Test]
        public void AscendingBeyondJumpHeightFails()
        {
            Vector3Int target = new Vector3Int(1, 2, 0);
            GridPathResult result = Find(
                CreateMap(Tile(Vector3Int.zero), Tile(target)),
                Vector3Int.zero,
                target,
                jumpHeight: 1);

            Assert.That(result.FailureReason, Is.EqualTo(GridPathFailureReason.NoLegalPath));
        }

        [Test]
        public void DescendingWithinFallHeightSucceeds()
        {
            Vector3Int start = new Vector3Int(0, 2, 0);
            Vector3Int target = Vector3Int.right;
            GridPathResult result = Find(
                CreateMap(Tile(start), Tile(target)),
                start,
                target,
                fallHeight: 2);

            Assert.That(result.IsSuccessful, Is.True);
        }

        [Test]
        public void DescendingBeyondFallHeightFails()
        {
            Vector3Int start = new Vector3Int(0, 3, 0);
            Vector3Int target = Vector3Int.right;
            GridPathResult result = Find(
                CreateMap(Tile(start), Tile(target)),
                start,
                target,
                fallHeight: 2);

            Assert.That(result.FailureReason, Is.EqualTo(GridPathFailureReason.NoLegalPath));
        }

        [TestCase(TileManager.TileType.Air)]
        [TestCase(TileManager.TileType.Wall)]
        [TestCase(TileManager.TileType.Chasm)]
        public void NonWalkableTargetsAreRejected(TileManager.TileType tileType)
        {
            GridPathResult result = Find(
                CreateMap(Tile(Vector3Int.zero), Tile(Vector3Int.right, tileType)),
                Vector3Int.zero,
                Vector3Int.right);

            Assert.That(result.FailureReason, Is.EqualTo(GridPathFailureReason.TargetNotWalkable));
        }

        [Test]
        public void MissingTargetIsRejected()
        {
            GridPathResult result = Find(
                CreateMap(Tile(Vector3Int.zero)),
                Vector3Int.zero,
                Vector3Int.right);

            Assert.That(result.FailureReason, Is.EqualTo(GridPathFailureReason.TargetMissing));
        }

        [TestCase(TileManager.TileType.Air)]
        [TestCase(TileManager.TileType.Wall)]
        [TestCase(TileManager.TileType.Chasm)]
        public void NonWalkableCoordinatesAreNeverTraversed(TileManager.TileType blockedType)
        {
            TileGridSnapshot map = CreateMap(
                Tile(Vector3Int.zero),
                Tile(Vector3Int.right, blockedType),
                Tile(Vector3Int.right * 2));

            GridPathResult result = Find(map, Vector3Int.zero, Vector3Int.right * 2);

            Assert.That(result.FailureReason, Is.EqualTo(GridPathFailureReason.NoLegalPath));
        }

        [Test]
        public void OneCellClearanceRejectsBlockedHeadroom()
        {
            TileGridSnapshot map = CreateMap(
                Tile(Vector3Int.zero),
                Tile(Vector3Int.right),
                Tile(Vector3Int.right + Vector3Int.up, TileManager.TileType.Wall));

            GridPathResult result = pathfinder.FindPath(
                map,
                Vector3Int.zero,
                Vector3Int.right,
                0,
                0,
                1f,
                LargeMovementAmount,
                AvailableActionPoints,
                new OneCellGridTraversalPolicy(map));

            Assert.That(result.FailureReason, Is.EqualTo(GridPathFailureReason.TargetBlocked));
        }

        [TestCase(TileManager.TileType.Floor)]
        [TestCase(TileManager.TileType.Slope)]
        [TestCase(TileManager.TileType.Hazard)]
        [TestCase(TileManager.TileType.Ladder)]
        public void EveryFloorFamilyTypeIsTraversable(TileManager.TileType tileType)
        {
            GridPathResult result = Find(
                CreateMap(Tile(Vector3Int.zero), Tile(Vector3Int.right, tileType)),
                Vector3Int.zero,
                Vector3Int.right);

            Assert.That(result.IsSuccessful, Is.True);
        }

        [Test]
        public void LowerCostSafeRouteIsPreferredOverHazard()
        {
            TileGridSnapshot map = CreateHazardChoiceMap();

            GridPathResult result = Find(
                map,
                Vector3Int.zero,
                Vector3Int.right * 2,
                hazardPenalty: 3f);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.WeightedCost, Is.EqualTo(4f));
            CollectionAssert.DoesNotContain(result.Coordinates, Vector3Int.right);
        }

        [Test]
        public void HazardRouteRemainsUsableWhenItIsOnlyRoute()
        {
            TileGridSnapshot map = CreateMap(
                Tile(Vector3Int.zero),
                Tile(Vector3Int.right, TileManager.TileType.Hazard),
                Tile(Vector3Int.right * 2));

            GridPathResult result = Find(
                map,
                Vector3Int.zero,
                Vector3Int.right * 2,
                hazardPenalty: 2f);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.WeightedCost, Is.EqualTo(2f));
        }

        [Test]
        public void HazardPreferenceDoesNotIncreaseBudgetOrActionPointCost()
        {
            TileGridSnapshot map = CreateMap(
                Tile(Vector3Int.zero),
                Tile(Vector3Int.right, TileManager.TileType.Hazard),
                Tile(Vector3Int.right * 2));

            GridPathResult result = pathfinder.FindPath(
                map,
                Vector3Int.zero,
                Vector3Int.right * 2,
                0,
                0,
                2f,
                3f,
                2);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.WeightedCost, Is.EqualTo(2f));
            Assert.That(result.RequiredActionPoints, Is.EqualTo(1));
        }

        [Test]
        public void HazardRouteRemainsAvailableWhenSafeAlternativeExceedsBudget()
        {
            GridPathResult result = pathfinder.FindPath(
                CreateHazardChoiceMap(),
                Vector3Int.zero,
                Vector3Int.right * 2,
                0,
                0,
                3f,
                1f,
                2);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.WeightedCost, Is.EqualTo(2f));
            CollectionAssert.Contains(result.Coordinates, Vector3Int.right);
        }

        [Test]
        public void CheaperHazardRouteBeatsMuchLongerSafeRoute()
        {
            GridPathResult result = Find(
                CreateHazardChoiceMap(),
                Vector3Int.zero,
                Vector3Int.right * 2,
                hazardPenalty: 0.5f);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.WeightedCost, Is.EqualTo(2f));
            CollectionAssert.Contains(result.Coordinates, Vector3Int.right);
        }

        [Test]
        public void MovementOverBudgetReturnsPathCostAndRequiredActionPoints()
        {
            TileGridSnapshot map = CreateMap(
                Tile(Vector3Int.zero),
                Tile(Vector3Int.right),
                Tile(Vector3Int.right * 2),
                Tile(Vector3Int.right * 3));

            GridPathResult result = pathfinder.FindPath(
                map,
                Vector3Int.zero,
                Vector3Int.right * 3,
                0,
                0,
                1f,
                1f,
                2);

            Assert.That(result.PathFound, Is.True);
            Assert.That(result.IsSuccessful, Is.False);
            Assert.That(result.WeightedCost, Is.EqualTo(3f));
            Assert.That(result.RequiredActionPoints, Is.EqualTo(3));
            Assert.That(result.FailureReason, Is.EqualTo(GridPathFailureReason.ExceedsMovementBudget));
        }

        [Test]
        public void ExactMovementBudgetSucceeds()
        {
            TileGridSnapshot map = CreateMap(
                Tile(Vector3Int.zero),
                Tile(Vector3Int.right),
                Tile(Vector3Int.right * 2));

            GridPathResult result = pathfinder.FindPath(
                map,
                Vector3Int.zero,
                Vector3Int.right * 2,
                0,
                0,
                1f,
                1f,
                2);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.RequiredActionPoints, Is.EqualTo(2));
        }

        [TestCase(0f, 0)]
        [TestCase(0.1f, 1)]
        [TestCase(4f, 1)]
        [TestCase(4.1f, 2)]
        [TestCase(12f, 3)]
        public void ActionPointCalculationRoundsUp(float cost, int expectedActionPoints)
        {
            Assert.That(
                MovementBudget.CalculateRequiredActionPoints(cost, 4f),
                Is.EqualTo(expectedActionPoints));
        }

        [Test]
        public void LadderPermitsOtherwiseTooHighAscent()
        {
            Vector3Int target = new Vector3Int(1, 5, 0);
            GridPathResult result = Find(
                CreateMap(
                    Tile(Vector3Int.zero, TileManager.TileType.Ladder),
                    Tile(target)),
                Vector3Int.zero,
                target,
                jumpHeight: 0);

            Assert.That(result.IsSuccessful, Is.True);
        }

        [Test]
        public void UnlimitedLadderAscentAppliesOnlyToOutgoingLadderStep()
        {
            Vector3Int ladderExit = new Vector3Int(1, 3, 0);
            Vector3Int target = new Vector3Int(2, 6, 0);
            GridPathResult result = Find(
                CreateMap(
                    Tile(Vector3Int.zero, TileManager.TileType.Ladder),
                    Tile(ladderExit),
                    Tile(target)),
                Vector3Int.zero,
                target,
                jumpHeight: 0);

            Assert.That(result.FailureReason, Is.EqualTo(GridPathFailureReason.NoLegalPath));
        }

        [Test]
        public void LadderExitPrefersWallSupportedCandidate()
        {
            Vector3Int supportedExit = new Vector3Int(1, 3, 0);
            Vector3Int unsupportedExit = new Vector3Int(-1, 1, 0);
            Vector3Int target = new Vector3Int(2, 3, 0);
            GridPathResult result = Find(
                CreateMap(
                    Tile(Vector3Int.zero, TileManager.TileType.Ladder),
                    Tile(supportedExit),
                    Tile(supportedExit + Vector3Int.down, TileManager.TileType.Wall),
                    Tile(unsupportedExit),
                    Tile(target)),
                Vector3Int.zero,
                target,
                jumpHeight: 0);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.Coordinates[1], Is.EqualTo(supportedExit));
        }

        [Test]
        public void LadderExitFallsBackToClosestUnsupportedCandidate()
        {
            Vector3Int closestExit = new Vector3Int(-1, 1, 0);
            Vector3Int fartherExit = new Vector3Int(1, 3, 0);
            Vector3Int target = new Vector3Int(-2, 1, 0);
            GridPathResult result = Find(
                CreateMap(
                    Tile(Vector3Int.zero, TileManager.TileType.Ladder),
                    Tile(closestExit),
                    Tile(fartherExit),
                    Tile(target)),
                Vector3Int.zero,
                target,
                jumpHeight: 0);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.Coordinates[1], Is.EqualTo(closestExit));
        }

        [Test]
        public void RouteDistributionRestartsFromElevatedLadderExit()
        {
            Vector3Int start = Vector3Int.zero;
            Vector3Int ladder = Vector3Int.right;
            Vector3Int ladderExit = new Vector3Int(2, 3, 0);
            Vector3Int target = new Vector3Int(6, 3, 4);
            List<KeyValuePair<Vector3Int, TileManager.TileType>> tiles =
                new List<KeyValuePair<Vector3Int, TileManager.TileType>>
                {
                    Tile(start),
                    Tile(ladder, TileManager.TileType.Ladder)
                };
            for (int x = ladderExit.x; x <= target.x; x++)
            {
                for (int z = ladderExit.z; z <= target.z; z++)
                {
                    tiles.Add(Tile(new Vector3Int(x, ladderExit.y, z)));
                }
            }

            GridPathResult result = Find(
                CreateMap(tiles.ToArray()),
                start,
                target,
                jumpHeight: 0);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.Coordinates, Is.EqualTo(new[]
            {
                start,
                ladder,
                ladderExit,
                new Vector3Int(2, 3, 1),
                new Vector3Int(3, 3, 1),
                new Vector3Int(3, 3, 2),
                new Vector3Int(4, 3, 2),
                new Vector3Int(4, 3, 3),
                new Vector3Int(5, 3, 3),
                new Vector3Int(5, 3, 4),
                target
            }));
            Assert.That(result.WeightedCost, Is.EqualTo(10f));
            AssertCardinalEdges(result.Coordinates);
        }

        [Test]
        public void EqualAxisDisplacementProducesCardinalStaircase()
        {
            GridPathResult result = Find(
                CreateFloorRectangle(0, 3, 0, 3),
                Vector3Int.zero,
                new Vector3Int(3, 0, 3));

            Assert.That(result.Coordinates, Is.EqualTo(new[]
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(0, 0, 1),
                new Vector3Int(1, 0, 1),
                new Vector3Int(1, 0, 2),
                new Vector3Int(2, 0, 2),
                new Vector3Int(2, 0, 3),
                new Vector3Int(3, 0, 3)
            }));
            Assert.That(result.WeightedCost, Is.EqualTo(6f));
            AssertCardinalEdges(result.Coordinates);
        }

        [Test]
        public void ReversedEqualAxisRouteProducesReversedStaircase()
        {
            TileGridSnapshot map = CreateFloorRectangle(0, 3, 0, 3);
            GridPathResult forward = Find(map, Vector3Int.zero, new Vector3Int(3, 0, 3));
            GridPathResult reverse = Find(map, new Vector3Int(3, 0, 3), Vector3Int.zero);
            List<Vector3Int> reversedForward = new List<Vector3Int>(forward.Coordinates);
            reversedForward.Reverse();

            Assert.That(reverse.Coordinates, Is.EqualTo(reversedForward));
            AssertCardinalEdges(reverse.Coordinates);
        }

        [Test]
        public void FourByOneDisplacementPlacesMinorityStepNearMiddle()
        {
            GridPathResult result = Find(
                CreateFloorRectangle(0, 4, 0, 1),
                Vector3Int.zero,
                new Vector3Int(4, 0, 1));

            Assert.That(result.Coordinates, Is.EqualTo(new[]
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(2, 0, 0),
                new Vector3Int(2, 0, 1),
                new Vector3Int(3, 0, 1),
                new Vector3Int(4, 0, 1)
            }));
            Assert.That(result.WeightedCost, Is.EqualTo(5f));
            AssertCardinalEdges(result.Coordinates);
        }

        [Test]
        public void UnequalDisplacementDistributesMinorityAxisSteps()
        {
            GridPathResult result = Find(
                CreateFloorRectangle(0, 5, 0, 2),
                Vector3Int.zero,
                new Vector3Int(5, 0, 2));

            Assert.That(result.Coordinates, Is.EqualTo(new[]
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(1, 0, 1),
                new Vector3Int(2, 0, 1),
                new Vector3Int(3, 0, 1),
                new Vector3Int(4, 0, 1),
                new Vector3Int(4, 0, 2),
                new Vector3Int(5, 0, 2)
            }));
            AssertCardinalEdges(result.Coordinates);
        }

        [Test]
        public void PureZRouteRemainsStraight()
        {
            GridPathResult result = Find(
                CreateFloorRectangle(0, 0, 0, 4),
                Vector3Int.zero,
                new Vector3Int(0, 0, 4));

            Assert.That(result.Coordinates.Count, Is.EqualTo(5));
            for (int coordinateIndex = 0;
                 coordinateIndex < result.Coordinates.Count;
                 coordinateIndex++)
            {
                Assert.That(result.Coordinates[coordinateIndex],
                    Is.EqualTo(new Vector3Int(0, 0, coordinateIndex)));
            }
        }

        [Test]
        public void BlockedStaircaseUsesCheapestCardinalDetour()
        {
            Vector3Int blockedCoordinate = new Vector3Int(2, 0, 0);
            GridPathResult result = Find(
                CreateFloorRectangle(0, 4, 0, 2, blockedCoordinate),
                Vector3Int.zero,
                new Vector3Int(4, 0, 0));

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.WeightedCost, Is.EqualTo(6f));
            CollectionAssert.DoesNotContain(result.Coordinates, blockedCoordinate);
            AssertCardinalEdges(result.Coordinates);
        }

        [Test]
        public void NegativeDirectionsReceiveTheSameDistributedShape()
        {
            GridPathResult result = Find(
                CreateFloorRectangle(-4, 0, -1, 0),
                Vector3Int.zero,
                new Vector3Int(-4, 0, -1));

            Assert.That(result.Coordinates, Is.EqualTo(new[]
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(-1, 0, 0),
                new Vector3Int(-2, 0, 0),
                new Vector3Int(-2, 0, -1),
                new Vector3Int(-3, 0, -1),
                new Vector3Int(-4, 0, -1)
            }));
            AssertCardinalEdges(result.Coordinates);
        }

        [Test]
        public void LargeCoordinateLineDeviationUsesWideIntegerArithmetic()
        {
            MethodInfo method = typeof(GridPathfinder).GetMethod(
                "CalculateHorizontalLineDeviation",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            Vector3Int start = new Vector3Int(int.MinValue, 0, int.MinValue);
            Vector3Int target = new Vector3Int(int.MaxValue, 0, int.MaxValue);
            Vector3Int candidate = new Vector3Int(int.MaxValue, 0, int.MinValue);

            decimal deviation =
                (decimal)method.Invoke(null, new object[] { candidate, start, target });

            Assert.That(deviation, Is.EqualTo(18446744065119617025m));
        }

        [Test]
        public void MultipleElevationsAndRepeatedSearchesAreDeterministic()
        {
            Vector3Int lowerIntermediate = Vector3Int.right;
            Vector3Int upperIntermediate = new Vector3Int(1, 1, 0);
            Vector3Int target = new Vector3Int(2, 1, 0);
            TileGridSnapshot map = CreateMap(
                Tile(Vector3Int.zero),
                Tile(lowerIntermediate),
                Tile(upperIntermediate),
                Tile(target));

            GridPathResult first = Find(map, Vector3Int.zero, target, jumpHeight: 1);
            GridPathResult second = Find(map, Vector3Int.zero, target, jumpHeight: 1);

            Assert.That(first.Coordinates, Is.EqualTo(second.Coordinates));
            Assert.That(first.Coordinates[1], Is.EqualTo(lowerIntermediate));
        }

        private GridPathResult Find(
            IGridPathMap map,
            Vector3Int start,
            Vector3Int target,
            int jumpHeight = 0,
            int fallHeight = 0,
            float hazardPenalty = 2f)
        {
            return pathfinder.FindPath(
                map,
                start,
                target,
                jumpHeight,
                fallHeight,
                hazardPenalty,
                LargeMovementAmount,
                AvailableActionPoints);
        }

        private static TileGridSnapshot CreateHazardChoiceMap()
        {
            return CreateMap(
                Tile(Vector3Int.zero),
                Tile(Vector3Int.right, TileManager.TileType.Hazard),
                Tile(Vector3Int.right * 2),
                Tile(new Vector3Int(0, 0, 1)),
                Tile(new Vector3Int(1, 0, 1)),
                Tile(new Vector3Int(2, 0, 1)));
        }

        private static TileGridSnapshot CreateFloorRectangle(
            int minimumX,
            int maximumX,
            int minimumZ,
            int maximumZ,
            Vector3Int? omittedCoordinate = null)
        {
            List<KeyValuePair<Vector3Int, TileManager.TileType>> tiles =
                new List<KeyValuePair<Vector3Int, TileManager.TileType>>();
            for (int x = minimumX; x <= maximumX; x++)
            {
                for (int z = minimumZ; z <= maximumZ; z++)
                {
                    Vector3Int coordinate = new Vector3Int(x, 0, z);
                    if (!omittedCoordinate.HasValue || coordinate != omittedCoordinate.Value)
                    {
                        tiles.Add(Tile(coordinate));
                    }
                }
            }

            return CreateMap(tiles.ToArray());
        }

        private static void AssertCardinalEdges(IReadOnlyList<Vector3Int> coordinates)
        {
            for (int coordinateIndex = 1; coordinateIndex < coordinates.Count; coordinateIndex++)
            {
                Vector3Int difference = coordinates[coordinateIndex] -
                                        coordinates[coordinateIndex - 1];
                Assert.That(Mathf.Abs(difference.x) + Mathf.Abs(difference.z), Is.EqualTo(1));
                Assert.That(difference.x == 0 || difference.z == 0, Is.True);
            }
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
    }
}
