using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PCOM.Tests
{
    public class UnitMovementTests
    {
        private readonly List<GameObject> createdObjects = new List<GameObject>();
        private TileManager tileManager;
        private UnitMovement movement;

        [SetUp]
        public void SetUp()
        {
            tileManager = CreateGameObject("Tile Manager").AddComponent<TileManager>();
            movement = CreateGameObject("Moving Unit").AddComponent<UnitMovement>();
            SetMovementField("tileManager", tileManager);
            SetMovementField("initializeFromWorldPositionOnStart", false);
            SetMovementField("captureStandingOffsetDuringInitialization", false);
            SetMovementField("movementAmount", 1f);
            SetMovementField("movementSpeed", 1000f);
            SetMovementField("hazardTraversalPenalty", 2f);
            SetMovementField("temporaryStartingActionPoints", 3);
            SetMovementField("temporaryCurrentActionPoints", 3);
        }

        [TearDown]
        public void TearDown()
        {
            for (int objectIndex = createdObjects.Count - 1; objectIndex >= 0; objectIndex--)
            {
                Object.DestroyImmediate(createdObjects[objectIndex]);
            }

            createdObjects.Clear();
        }

        [Test]
        public void InitializationValidatesWalkabilityAndCapturesAuthoritativeCoordinate()
        {
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);

            Assert.That(movement.TryInitializeAtCoordinate(Vector3Int.zero, true), Is.True);
            Assert.That(movement.IsInitialized, Is.True);
            Assert.That(movement.CurrentCoordinate, Is.EqualTo(Vector3Int.zero));
            Assert.That(movement.TryInitializeAtCoordinate(Vector3Int.right, true), Is.False);
        }

        [Test]
        public void WorldInitializationRespectsGridOriginCellSizeAndStandingOffset()
        {
            tileManager.GridOrigin = new Vector3(10f, -3f, 6f);
            tileManager.CellSize = 2f;
            Vector3Int coordinate = new Vector3Int(2, 1, -1);
            SetTile(coordinate, TileManager.TileType.Floor);
            Vector3 expectedOffset = new Vector3(0.2f, 0.8f, -0.1f);
            movement.transform.position = tileManager.GridToWorldCenter(coordinate) + expectedOffset;
            SetMovementField("captureStandingOffsetDuringInitialization", true);

            Assert.That(movement.TryInitializeCurrentCoordinateFromWorldPosition(), Is.True);
            Assert.That(movement.CurrentCoordinate, Is.EqualTo(coordinate));
            Assert.That(movement.StandingOffset.x, Is.EqualTo(expectedOffset.x).Within(0.0001f));
            Assert.That(movement.StandingOffset.y, Is.EqualTo(expectedOffset.y).Within(0.0001f));
            Assert.That(movement.StandingOffset.z, Is.EqualTo(expectedOffset.z).Within(0.0001f));
        }

        [Test]
        public void PreviewDoesNotSpendActionPointsOrMutateTransformOrCoordinate()
        {
            AddStraightFloorPath(2);
            movement.TryInitializeAtCoordinate(Vector3Int.zero, true);
            Vector3 initialPosition = movement.transform.position;
            int initialActionPoints = movement.CurrentActionPoints;

            GridPathResult result = movement.CalculatePathPreview(Vector3Int.right * 2);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(movement.CurrentActionPoints, Is.EqualTo(initialActionPoints));
            Assert.That(movement.CurrentCoordinate, Is.EqualTo(Vector3Int.zero));
            Assert.That(movement.transform.position, Is.EqualTo(initialPosition));
            Assert.That(movement.IsMoving, Is.False);
        }

        [Test]
        public void PlayerAndExternalPreviewsUseTheSamePathfinder()
        {
            AddStraightFloorPath(2);
            movement.TryInitializeAtCoordinate(Vector3Int.zero, true);
            Vector3Int destination = Vector3Int.right * 2;
            MovementTileSelection selection = new MovementTileSelection(
                destination,
                TileManager.TileType.Floor,
                tileManager.GridToWorldCenter(destination),
                default,
                Vector3.up);

            GridPathResult externalResult = movement.CalculatePathPreview(destination);
            GridPathResult playerResult = movement.CalculatePlayerSelectionPreview(selection);

            Assert.That(playerResult.IsSuccessful, Is.True);
            Assert.That(playerResult.Coordinates, Is.EqualTo(externalResult.Coordinates));
            Assert.That(playerResult.WeightedCost, Is.EqualTo(externalResult.WeightedCost));
        }

        [Test]
        public void ExternallyControlledUnitRejectsPlayerSelectionButAcceptsCoordinatePreview()
        {
            AddStraightFloorPath(1);
            SetMovementField("requiresPlayerTileSelection", false);
            movement.TryInitializeAtCoordinate(Vector3Int.zero, true);
            MovementTileSelection selection = new MovementTileSelection(
                Vector3Int.right,
                TileManager.TileType.Floor,
                Vector3.right,
                default,
                Vector3.up);

            GridPathResult playerResult = movement.CalculatePlayerSelectionPreview(selection);
            GridPathResult externalResult = movement.CalculatePathPreview(Vector3Int.right);

            Assert.That(playerResult.FailureReason,
                Is.EqualTo(GridPathFailureReason.PlayerSelectionNotAccepted));
            Assert.That(externalResult.IsSuccessful, Is.True);
        }

        [Test]
        public void FailedPathDoesNotSpendActionPoints()
        {
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);
            movement.TryInitializeAtCoordinate(Vector3Int.zero, true);

            bool accepted = movement.TryRequestMovement(Vector3Int.right, out GridPathResult result);

            Assert.That(accepted, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(GridPathFailureReason.TargetMissing));
            Assert.That(movement.CurrentActionPoints, Is.EqualTo(3));
        }

        [Test]
        public void ZeroLengthMovementSpendsNoActionPoints()
        {
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);
            movement.TryInitializeAtCoordinate(Vector3Int.zero, true);

            bool accepted = movement.TryRequestMovement(Vector3Int.zero, out GridPathResult result);

            Assert.That(accepted, Is.True);
            Assert.That(result.RequiredActionPoints, Is.Zero);
            Assert.That(movement.CurrentActionPoints, Is.EqualTo(3));
            Assert.That(movement.IsMoving, Is.False);
        }

        [Test]
        public void CommitSpendsActionPointsAndPreventsConcurrentMovement()
        {
            AddStraightFloorPath(2);
            SetMovementField("movementSpeed", 0.01f);
            movement.TryInitializeAtCoordinate(Vector3Int.zero, true);

            bool accepted = movement.TryRequestMovement(Vector3Int.right * 2, out GridPathResult result);
            bool secondAccepted = movement.TryRequestMovement(Vector3Int.right, out GridPathResult secondResult);

            Assert.That(accepted, Is.True);
            Assert.That(result.RequiredActionPoints, Is.EqualTo(2));
            Assert.That(movement.CurrentActionPoints, Is.EqualTo(1));
            Assert.That(movement.IsMoving, Is.True);
            Assert.That(secondAccepted, Is.False);
            Assert.That(secondResult.FailureReason, Is.EqualTo(GridPathFailureReason.AlreadyMoving));
            movement.CancelMovement();
        }

        [Test]
        public void CompletedMovementUpdatesCoordinateAndWorldPosition()
        {
            AddStraightFloorPath(1);
            Vector3 standingOffset = new Vector3(0.1f, 0.75f, -0.2f);
            SetMovementField("standingOffset", standingOffset);
            movement.TryInitializeAtCoordinate(Vector3Int.zero, true);

            Assert.That(movement.TryRequestMovement(Vector3Int.right, out _), Is.True);
            InvokeMovementMethod("AdvanceCommittedMovement", 1f);

            Assert.That(movement.IsMoving, Is.False);
            Assert.That(movement.CurrentCoordinate, Is.EqualTo(Vector3Int.right));
            Assert.That(
                movement.transform.position,
                Is.EqualTo(tileManager.GridToWorldCenter(Vector3Int.right) + standingOffset));
        }

        [Test]
        public void DownhillMovementUsesContinuousHeightAtEachSlopeColumn()
        {
            Vector3Int highFloor = new Vector3Int(-1, 1, 0);
            Vector3Int highSlope = new Vector3Int(0, 1, 0);
            Vector3Int lowSlope = new Vector3Int(1, 0, 0);
            SetTile(highFloor, TileManager.TileType.Floor);
            SetTile(highSlope, TileManager.TileType.Slope);
            SetTile(Vector3Int.zero, TileManager.TileType.Slope);
            SetTile(lowSlope, TileManager.TileType.Slope);
            movement.TryInitializeAtCoordinate(highFloor, true);

            Assert.That(movement.TryRequestMovement(lowSlope, out _), Is.True);
            InvokeMovementMethod("AdvanceCommittedMovement", 0.001f);

            Assert.That(movement.transform.position.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(movement.transform.position.y, Is.EqualTo(0.5f).Within(0.001f));
            movement.CancelMovement();
        }

        [Test]
        public void SlopeTraversalHeightOffsetBlendsAtFloorTransitions()
        {
            Vector3Int highFloor = new Vector3Int(-1, 1, 0);
            Vector3Int highSlope = new Vector3Int(0, 1, 0);
            Vector3Int lowSlope = new Vector3Int(1, 0, 0);
            Vector3Int lowFloor = new Vector3Int(2, 0, 0);
            SetTile(highFloor, TileManager.TileType.Floor);
            SetTile(highSlope, TileManager.TileType.Slope);
            SetTile(Vector3Int.zero, TileManager.TileType.Slope);
            SetTile(lowSlope, TileManager.TileType.Slope);
            SetTile(lowFloor, TileManager.TileType.Floor);
            SetMovementField("slopeTraversalHeightOffset", 0.2f);
            movement.TryInitializeAtCoordinate(highFloor, true);

            Assert.That(movement.TryRequestMovement(lowFloor, out _), Is.True);
            InvokeMovementMethod("AdvanceCommittedMovement", 0.0005f);

            Assert.That(movement.transform.position.x, Is.EqualTo(-0.5f).Within(0.001f));
            Assert.That(movement.transform.position.y, Is.EqualTo(0.85f).Within(0.001f));

            InvokeMovementMethod("AdvanceCommittedMovement", 0.0005f);
            Assert.That(movement.transform.position.y, Is.EqualTo(0.7f).Within(0.001f));

            InvokeMovementMethod("AdvanceCommittedMovement", 0.002f);
            Assert.That(movement.IsMoving, Is.False);
            Assert.That(movement.transform.position, Is.EqualTo(Vector3.right * 2f));
        }

        [Test]
        public void SlopeTraversalHeightOffsetUsesSmoothOneCellTransition()
        {
            MethodInfo method = typeof(UnitMovement).GetMethod(
                "GetSlopeOffsetWeight",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            float entryQuarter = (float)method.Invoke(null, new object[] { false, true, 0.25f });
            float entryHalf = (float)method.Invoke(null, new object[] { false, true, 0.5f });
            float exitQuarter = (float)method.Invoke(null, new object[] { true, false, 0.25f });

            Assert.That(entryQuarter, Is.EqualTo(0.15625f).Within(0.0001f));
            Assert.That(entryHalf, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(exitQuarter, Is.EqualTo(0.84375f).Within(0.0001f));
        }

        [Test]
        public void ExecutedSlopePathKeepsDiagonalAcrossVoxelizedSurfaceColumns()
        {
            for (int z = 0; z <= 2; z++)
            {
                SetTile(new Vector3Int(0, 2, z), TileManager.TileType.Slope);
                SetTile(new Vector3Int(0, 1, z), TileManager.TileType.Slope);
                SetTile(new Vector3Int(1, 1, z), TileManager.TileType.Slope);
                SetTile(new Vector3Int(1, 0, z), TileManager.TileType.Slope);
                SetTile(new Vector3Int(2, 0, z), TileManager.TileType.Slope);
            }

            List<Vector3Int> sourcePath = new List<Vector3Int>
            {
                new Vector3Int(0, 2, 0),
                new Vector3Int(0, 2, 1),
                new Vector3Int(1, 1, 1),
                new Vector3Int(1, 1, 2),
                new Vector3Int(2, 0, 2)
            };
            Assert.That(movement.TryInitializeAtCoordinate(sourcePath[0], true), Is.True);

            bool prepared = (bool)InvokeMovementMethodWithResult(
                "TryPrepareExecutedPath",
                sourcePath);

            Assert.That(prepared, Is.True);
            ExecutedMovementPath executedPath = movement.ActiveExecutedPath;
            Assert.That(executedPath, Is.Not.Null);
            Assert.That(executedPath.Segments, Has.Count.EqualTo(1));
            Assert.That(
                executedPath.Segments[0].Type,
                Is.EqualTo(MovementPresentationSegmentType.SlopeRun));
            Assert.That(executedPath.Segments[0].StartWorldPosition.x, Is.EqualTo(0f));
            Assert.That(executedPath.Segments[0].EndWorldPosition.x, Is.EqualTo(2f));
            Assert.That(executedPath.Segments[0].EndWorldPosition.z, Is.EqualTo(2f));

            TileGridSnapshot snapshot = new TileGridSnapshot(tileManager.Tiles);
            OneCellGridTraversalPolicy traversalPolicy =
                new OneCellGridTraversalPolicy(snapshot);
            for (int coordinateIndex = 0;
                 coordinateIndex < executedPath.Coordinates.Count;
                 coordinateIndex++)
            {
                Vector3Int coordinate = executedPath.Coordinates[coordinateIndex];
                Assert.That(snapshot.TryGetTile(coordinate, out TileManager.TileType tileType),
                    Is.True);
                Assert.That(tileType, Is.EqualTo(TileManager.TileType.Slope));
                Assert.That(traversalPolicy.CanEnter(coordinate), Is.True);
            }
        }

        [Test]
        public void SignificantTurnThresholdIncludesFortyFiveDegreesButNotFortyFour()
        {
            Vector3 forward = Vector3.forward;
            Vector3 fortyFourDegrees = Quaternion.Euler(0f, 44f, 0f) * forward;
            Vector3 fortyFiveDegrees = Quaternion.Euler(0f, 45f, 0f) * forward;

            Assert.That(
                movement.IsSignificantPresentationTurn(forward, fortyFourDegrees),
                Is.False);
            Assert.That(
                movement.IsSignificantPresentationTurn(forward, fortyFiveDegrees),
                Is.True);
            Assert.That(
                movement.IsSignificantPresentationTurn(forward, Vector3.back),
                Is.True);
        }

        [Test]
        public void TurnAngleIgnoresHeight()
        {
            float angle = UnitMovement.CalculateHorizontalTurnAngle(
                new Vector3(1f, 50f, 0f),
                new Vector3(0f, -20f, 1f));

            Assert.That(angle, Is.EqualTo(90f).Within(0.0001f));
        }

        [Test]
        public void DropLandingDelayAddsTimeOnlyBeyondFirstCell()
        {
            Assert.That(movement.CalculateDropLandingDelay(1), Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(movement.CalculateDropLandingDelay(2), Is.EqualTo(0.18f).Within(0.0001f));
            Assert.That(movement.CalculateDropLandingDelay(4), Is.EqualTo(0.34f).Within(0.0001f));
        }

        [Test]
        public void LadderBobIsZeroAtBothEndpoints()
        {
            Assert.That(movement.CalculateLadderBob(0f), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(movement.CalculateLadderBob(1f), Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void ElevationChangesTouchingSlopeRemainJumpOrDropPresentation()
        {
            Vector3Int highSlope = new Vector3Int(0, 1, 0);
            Vector3Int middleSlope = Vector3Int.zero;
            Vector3Int lowSlope = new Vector3Int(1, 0, 0);
            Vector3Int dropSource = new Vector3Int(-1, 3, 0);
            Vector3Int jumpDestination = new Vector3Int(1, 2, 0);
            SetTile(highSlope, TileManager.TileType.Slope);
            SetTile(middleSlope, TileManager.TileType.Slope);
            SetTile(lowSlope, TileManager.TileType.Slope);
            SetTile(dropSource, TileManager.TileType.Floor);
            SetTile(jumpDestination, TileManager.TileType.Floor);
            TileGridSnapshot snapshot = new TileGridSnapshot(tileManager.Tiles);
            MovementPresentationPathBuilder builder = new MovementPresentationPathBuilder();

            Assert.That(
                movement.TryGetContinuousSlopeRegion(dropSource, highSlope, out _),
                Is.False);
            Assert.That(
                builder.ClassifyTransition(snapshot, dropSource, highSlope, movement),
                Is.EqualTo(MovementPresentationSegmentType.Drop));
            Assert.That(
                movement.TryGetContinuousSlopeRegion(middleSlope, jumpDestination, out _),
                Is.False);
            Assert.That(
                builder.ClassifyTransition(snapshot, middleSlope, jumpDestination, movement),
                Is.EqualTo(MovementPresentationSegmentType.Jump));
            Assert.That(
                movement.TryGetContinuousSlopeRegion(highSlope, lowSlope, out _),
                Is.True);
        }

        [Test]
        public void ChasmDetourPinkCellsMatchFinalSegmentSupercoversAndTileEntries()
        {
            Vector3Int chasmCoordinate = new Vector3Int(2, 0, 2);
            for (int x = 0; x <= 4; x++)
            {
                for (int z = 0; z <= 4; z++)
                {
                    SetTile(new Vector3Int(x, 0, z), TileManager.TileType.Floor);
                }
            }

            SetTile(chasmCoordinate, TileManager.TileType.Chasm);
            SetMovementField("movementAmount", 20f);
            movement.TryInitializeAtCoordinate(Vector3Int.zero, true);
            Vector3Int destination = new Vector3Int(4, 0, 4);
            GridPathResult originalPath = movement.CalculatePathPreview(destination);
            Assert.That(originalPath.IsSuccessful, Is.True);
            List<Vector3Int> enteredCoordinates = new List<Vector3Int>();
            movement.ExecutedTileEntered += (_, coordinate) =>
                enteredCoordinates.Add(coordinate);

            InvokeTileManagerMethod("CreateTileVisuals");
            InvokeMovementMethod(
                "HandlePlayerTileSelected",
                new MovementTileSelection(
                    destination,
                    TileManager.TileType.Floor,
                    tileManager.GridToWorldCenter(destination),
                    default,
                    Vector3.up));
            ExecutedMovementPath executedPath = movement.ActiveExecutedPath;
            Assert.That(executedPath, Is.Not.Null);
            Assert.That(executedPath.Segments.Count, Is.GreaterThan(1));

            List<Vector2Int> supercover = new List<Vector2Int>();
            for (int segmentIndex = 0;
                 segmentIndex < executedPath.Segments.Count;
                 segmentIndex++)
            {
                ExecutedMovementSegment segment = executedPath.Segments[segmentIndex];
                GridSupercover.CollectHorizontalCells(
                    segment.PresentationSegment.StartCoordinate.x,
                    segment.PresentationSegment.StartCoordinate.z,
                    segment.PresentationSegment.EndCoordinate.x,
                    segment.PresentationSegment.EndCoordinate.z,
                    supercover);
                Assert.That(segment.CrossedCoordinates.Count, Is.EqualTo(supercover.Count));
                for (int cellIndex = 0; cellIndex < supercover.Count; cellIndex++)
                {
                    Vector3Int expectedCoordinate = new Vector3Int(
                        supercover[cellIndex].x,
                        0,
                        supercover[cellIndex].y);
                    Assert.That(
                        segment.CrossedCoordinates[cellIndex],
                        Is.EqualTo(expectedCoordinate));
                    Assert.That(expectedCoordinate, Is.Not.EqualTo(chasmCoordinate));
                    Assert.That(
                        tileManager.TryGetTile(
                            expectedCoordinate,
                            out TileManager.TileType tileType),
                        Is.True);
                    Assert.That(tileType, Is.EqualTo(TileManager.TileType.Floor));
                }
            }

            HashSet<Vector3Int> displayedCoordinates = GetDisplayedPlayerPath();
            HashSet<Vector3Int> expectedPinkCoordinates =
                new HashSet<Vector3Int>(executedPath.Coordinates);
            Assert.That(displayedCoordinates, Is.EquivalentTo(expectedPinkCoordinates));
            Assert.That(
                displayedCoordinates,
                Is.Not.EquivalentTo(new HashSet<Vector3Int>(originalPath.Coordinates)),
                "The fixture must exercise a presentation supercover that differs from A*. ");

            InvokeMovementMethod("AdvanceCommittedMovement", 1f);
            List<Vector3Int> expectedEntries = new List<Vector3Int>();
            for (int coordinateIndex = 1;
                 coordinateIndex < executedPath.Coordinates.Count;
                 coordinateIndex++)
            {
                expectedEntries.Add(executedPath.Coordinates[coordinateIndex]);
            }

            Assert.That(enteredCoordinates, Is.EqualTo(expectedEntries));
            Assert.That(movement.CurrentCoordinate, Is.EqualTo(destination));
        }

        private void AddStraightFloorPath(int finalX)
        {
            for (int x = 0; x <= finalX; x++)
            {
                SetTile(new Vector3Int(x, 0, 0), TileManager.TileType.Floor);
            }
        }

        private GameObject CreateGameObject(string objectName)
        {
            GameObject createdObject = new GameObject(objectName);
            createdObjects.Add(createdObject);
            return createdObject;
        }

        private void SetMovementField(string fieldName, object value)
        {
            FieldInfo field = typeof(UnitMovement).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}'.");
            field.SetValue(movement, value);
        }

        private void InvokeMovementMethod(string methodName, params object[] arguments)
        {
            InvokeMovementMethodWithResult(methodName, arguments);
        }

        private object InvokeMovementMethodWithResult(
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = typeof(UnitMovement).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Expected method '{methodName}'.");
            return method.Invoke(movement, arguments);
        }

        private void InvokeTileManagerMethod(string methodName)
        {
            MethodInfo method = typeof(TileManager).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Expected method '{methodName}'.");
            method.Invoke(tileManager, null);
        }

        private HashSet<Vector3Int> GetDisplayedPlayerPath()
        {
            FieldInfo field = typeof(TileManager).GetField(
                "displayedPlayerPath",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (HashSet<Vector3Int>)field.GetValue(tileManager);
        }

        private void SetTile(Vector3Int coordinate, TileManager.TileType tileType)
        {
            FieldInfo tilesField = typeof(TileManager).GetField(
                "tiles",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(tilesField, Is.Not.Null);
            Dictionary<Vector3Int, TileManager.TileType> tiles =
                (Dictionary<Vector3Int, TileManager.TileType>)tilesField.GetValue(tileManager);
            tiles[coordinate] = tileType;
        }
    }
}
