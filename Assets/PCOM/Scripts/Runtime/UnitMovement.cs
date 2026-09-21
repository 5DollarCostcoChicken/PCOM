using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Reusable target-coordinate movement for player-controlled and externally controlled units.
    /// </summary>
    public class UnitMovement : MonoBehaviour, IActionPointProvider,
        IMovementPresentationSlopeResolver
    {
        private const int DefaultTemporaryActionPoints = 3;
        private const float DefaultMovementAmount = 4f;
        private const float DefaultMovementSpeed = 4f;
        private const float DefaultRotationSpeed = 540f;
        private const float DefaultHazardPenalty = 2f;
        private const float MinimumPositiveValue = 0.0001f;

        [Header("Control")]
        [SerializeField] private bool requiresPlayerTileSelection = true;
        [SerializeField] private PlayerTileSelector playerTileSelector;

        [Header("Grid")]
        [SerializeField] private TileManager tileManager;
        [SerializeField] private bool initializeFromWorldPositionOnStart = true;
        [SerializeField] private bool captureStandingOffsetDuringInitialization = true;
        [SerializeField] private Vector3 standingOffset;

        [Header("Movement Rules")]
        [SerializeField, Min(MinimumPositiveValue)]
        private float movementAmount = DefaultMovementAmount;
        [SerializeField, Min(0)] private int maximumUpwardJumpHeight = 1;
        [SerializeField, Min(0)] private int maximumSafeDownwardFallHeight = 2;
        [SerializeField, Min(MinimumPositiveValue)]
        private float hazardTraversalPenalty = DefaultHazardPenalty;

        [Header("Physical Movement")]
        [SerializeField, Min(MinimumPositiveValue)]
        private float movementSpeed = DefaultMovementSpeed;
        [SerializeField, Min(1), Tooltip("Routes with fewer logical transitions use slow movement.")]
        private int slowMovementTransitionThreshold = 5;
        [SerializeField, Range(0.01f, 1f)] private float slowMovementSpeedMultiplier = 0.65f;
        [SerializeField] private bool faceMovementDirection = true;
        [SerializeField, Tooltip("Additional vertical standing offset applied while traversing slopes.")]
        private float slopeTraversalHeightOffset;

        [Header("Movement Presentation")]
        [SerializeField, Min(MinimumPositiveValue)] private float rotationSpeed =
            DefaultRotationSpeed;
        [SerializeField, Range(0f, 180f)] private float significantTurnThreshold = 45f;
        [SerializeField, Range(0f, 1f)] private float significantTurnSpeedMultiplier = 0.1f;
        [SerializeField, Min(0f)] private float positionArrivalTolerance = 0.01f;
        [SerializeField, Range(0f, 180f)] private float rotationArrivalTolerance = 2f;
        [SerializeField, Tooltip("Optional model child used only for ladder bob. Never assign the authoritative movement root.")]
        private Transform visualRoot;

        [Header("Presentation Clearance")]
        [SerializeField, Tooltip("Solid obstacle layers checked before combining logical floor steps into a long run. Leave empty to rely on grid supercover validation only.")]
        private LayerMask presentationObstructionMask;
        [SerializeField, Tooltip("Optional body shape used by the presentation-only corridor cast.")]
        private CapsuleCollider movementBodyCollider;

        [Header("Jump Presentation")]
        [SerializeField, Min(0f)] private float preJumpDelay = 0.2f;
        [SerializeField, Min(0f)] private float postJumpLandingDelay = 0.15f;
        [SerializeField, Min(MinimumPositiveValue)] private float jumpDuration = 0.55f;
        [SerializeField, Min(0f)] private float jumpArcHeight = 1f;
        [SerializeField] private AnimationCurve jumpArcProfile;

        [Header("Clamber Presentation")]
        [SerializeField, Min(0f)] private float preClamberDelay = 0.08f;
        [SerializeField, Min(0f)] private float postClamberDelay = 0.08f;
        [SerializeField, Min(MinimumPositiveValue)] private float clamberDuration = 0.65f;
        [SerializeField] private AnimationCurve clamberMotionCurve;

        [Header("Drop Presentation")]
        [SerializeField, Min(0f)] private float preDropDelay = 0.08f;
        [SerializeField, Min(0f)] private float baseDropLandingDelay = 0.1f;
        [SerializeField, Min(0f)] private float additionalDropLandingDelayPerCellBeyondFirst =
            0.08f;
        [SerializeField, Min(0f)] private float minimumDropLandingDelay;
        [SerializeField, Min(0f)] private float maximumDropLandingDelay = 1f;
        [SerializeField, Min(MinimumPositiveValue)] private float dropDuration = 0.45f;
        [SerializeField] private AnimationCurve dropMotionCurve;

        [Header("Ladder Presentation")]
        [SerializeField, Min(0f)] private float preLadderDelay = 0.08f;
        [SerializeField, Min(0f)] private float postLadderDelay = 0.08f;
        [SerializeField, Min(MinimumPositiveValue)] private float ladderClimbingSpeed = 2f;
        [SerializeField, Min(0f), Tooltip("Distance below the destination, in grid cells, where ladder climbing hands off to the top clamber.")]
        private float ladderTopClamberHeightInCells = 1f;
        [SerializeField, Min(0f)] private float ladderBobAmplitude = 0.05f;
        [SerializeField, Min(0f)] private float ladderBobCycleCount = 2f;
        [SerializeField] private AnimationCurve ladderMotionCurve;

        [Header("Temporary Action Points")]
        [SerializeField, Min(0)]
        private int temporaryStartingActionPoints = DefaultTemporaryActionPoints;
        [SerializeField, Min(0)] private int temporaryCurrentActionPoints = DefaultTemporaryActionPoints;

        [Header("Runtime State")]
        [SerializeField] private Vector3Int currentCoordinate;
        [SerializeField] private bool isInitialized;
        [SerializeField] private bool isMoving;
        [SerializeField] private bool isUsingSlowMovement;

        private readonly GridPathfinder pathfinder = new GridPathfinder();
        private readonly MovementPresentationPathBuilder presentationPathBuilder =
            new MovementPresentationPathBuilder();
        private readonly List<MovementPresentationSegment> preparedPresentationSegments =
            new List<MovementPresentationSegment>();
        private readonly List<ExecutedMovementSegment> preparedExecutedSegments =
            new List<ExecutedMovementSegment>();
        private readonly List<Vector2Int> executedSupercoverCells = new List<Vector2Int>();
        private readonly List<Vector3Int> executedCrossedCoordinates =
            new List<Vector3Int>();
        private readonly RaycastHit[] presentationClearanceHits = new RaycastHit[16];
        private readonly List<Vector3Int> initializationColumn = new List<Vector3Int>();
        private readonly HashSet<Vector3Int> loggedMalformedSlopeCoordinates =
            new HashSet<Vector3Int>();
        private IActionPointProvider externalActionPointProvider;
        private IGridTraversalPolicy externalTraversalPolicy;
        private SlopeRegionCache slopeRegionCache;
        private Coroutine movementCoroutine;
        private GridPathResult activePath;
        private ExecutedMovementPath activeExecutedPath;
        private int nextExecutedCellIndexInSegment;
        private int deterministicExecutedSegmentIndex;
        private float deterministicExecutedSegmentProgress;
        private bool hasVisualRootBaseline;
        private Vector3 visualRootBaselineLocalPosition;

        public event Action<UnitMovement, GridPathResult> MovementStarted;
        public event Action<UnitMovement, ExecutedMovementPath> ExecutedMovementStarted;
        public event Action<UnitMovement, Vector3Int> MovementCompleted;
        public event Action<UnitMovement, Vector3Int> MovementInterrupted;
        public event Action<UnitMovement, MovementPresentationSegment> RunBegan;
        public event Action<UnitMovement, MovementPresentationSegment> RunEnded;
        public event Action<UnitMovement, MovementPresentationSegment> TurnBegan;
        public event Action<UnitMovement, MovementPresentationSegment> TurnEnded;
        public event Action<UnitMovement, MovementPresentationSegment> JumpAnticipationBegan;
        public event Action<UnitMovement, MovementPresentationSegment> JumpTakeoff;
        public event Action<UnitMovement, MovementPresentationSegment> JumpLanding;
        public event Action<UnitMovement, MovementPresentationSegment> ClamberBegan;
        public event Action<UnitMovement, MovementPresentationSegment> ClamberCompleted;
        public event Action<UnitMovement, MovementPresentationSegment> DropPreparationBegan;
        public event Action<UnitMovement, MovementPresentationSegment> DropBegan;
        public event Action<UnitMovement, MovementPresentationSegment> DropLanded;
        public event Action<UnitMovement, MovementPresentationSegment> LadderPreparationBegan;
        public event Action<UnitMovement, MovementPresentationSegment> LadderClimbBegan;
        public event Action<UnitMovement, MovementPresentationSegment> LadderClimbCompleted;
        public event Action<UnitMovement, Vector3Int> LogicalTileEntered;
        public event Action<UnitMovement, Vector3Int> ExecutedTileEntered;

        public Vector3Int CurrentCoordinate => currentCoordinate;
        public float MovementAmount => movementAmount;
        public int MaximumUpwardJumpHeight => maximumUpwardJumpHeight;
        public int MaximumSafeDownwardFallHeight => maximumSafeDownwardFallHeight;
        public float MovementSpeed => movementSpeed;
        public float CurrentMovementSpeed => isUsingSlowMovement
            ? movementSpeed * slowMovementSpeedMultiplier
            : movementSpeed;
        public bool IsUsingSlowMovement => isUsingSlowMovement;
        /// <summary>
        /// Presentation segments for the currently executed path. Empty while idle.
        /// </summary>
        public IReadOnlyList<MovementPresentationSegment> ActivePresentationSegments =>
            activeExecutedPath?.PresentationSegments ??
            Array.Empty<MovementPresentationSegment>();
        /// <summary>
        /// Final world segments and crossed tactical cells used by movement and interactions.
        /// Null while idle.
        /// </summary>
        public ExecutedMovementPath ActiveExecutedPath => activeExecutedPath;
        public float SlopeTraversalHeightOffset => slopeTraversalHeightOffset;
        public bool IsMoving => isMoving;
        public bool RequiresPlayerTileSelection => requiresPlayerTileSelection;
        public bool IsInitialized => isInitialized;
        public Vector3 StandingOffset => standingOffset;
        public int CurrentActionPoints => EffectiveActionPointProvider.CurrentActionPoints;

        private IActionPointProvider EffectiveActionPointProvider =>
            externalActionPointProvider ?? (IActionPointProvider)this;

        int IActionPointProvider.CurrentActionPoints => temporaryCurrentActionPoints;

        private void Awake()
        {
            EnsurePresentationCurves();
            temporaryStartingActionPoints = Mathf.Max(0, temporaryStartingActionPoints);
            temporaryCurrentActionPoints = Mathf.Clamp(
                temporaryCurrentActionPoints,
                0,
                temporaryStartingActionPoints);
        }

        private void OnEnable()
        {
            SubscribeToDependencies();
        }

        private void Start()
        {
            EnsureSlopeCache();
            if (initializeFromWorldPositionOnStart && !isInitialized)
            {
                TryInitializeCurrentCoordinateFromWorldPosition();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromDependencies();
            if (isMoving)
            {
                CancelMovement();
            }
        }

        private void OnDestroy()
        {
            slopeRegionCache?.Dispose();
            slopeRegionCache = null;
        }

        /// <summary>
        /// Resolves the closest walkable tile in the transform's X/Z column and captures
        /// the transform-to-tile offset when configured to do so.
        /// </summary>
        public bool TryInitializeCurrentCoordinateFromWorldPosition()
        {
            if (tileManager == null)
            {
                return false;
            }

            Vector3 gridProbePosition = captureStandingOffsetDuringInitialization
                ? transform.position
                : transform.position - standingOffset;
            Vector3Int probeCoordinate = tileManager.WorldToGridCoordinate(gridProbePosition);
            TileGridSnapshot snapshot = new TileGridSnapshot(tileManager.Tiles);
            snapshot.CollectWalkableCoordinatesInColumn(
                probeCoordinate.x,
                probeCoordinate.z,
                initializationColumn);
            if (initializationColumn.Count == 0)
            {
                return false;
            }

            Vector3Int bestCoordinate = initializationColumn[0];
            float bestVerticalDistance = Mathf.Abs(
                tileManager.GridToWorldCenter(bestCoordinate).y - gridProbePosition.y);
            for (int coordinateIndex = 1;
                 coordinateIndex < initializationColumn.Count;
                 coordinateIndex++)
            {
                Vector3Int candidate = initializationColumn[coordinateIndex];
                float candidateDistance = Mathf.Abs(
                    tileManager.GridToWorldCenter(candidate).y - gridProbePosition.y);
                if (candidateDistance < bestVerticalDistance)
                {
                    bestCoordinate = candidate;
                    bestVerticalDistance = candidateDistance;
                }
            }

            return TryInitializeAtCoordinate(bestCoordinate, false);
        }

        /// <summary>
        /// Deliberately initializes the authoritative coordinate after validating it.
        /// </summary>
        public bool TryInitializeAtCoordinate(Vector3Int coordinate, bool snapTransformToGrid)
        {
            if (tileManager == null ||
                !tileManager.TryGetTile(coordinate, out TileManager.TileType tileType) ||
                !TileManager.IsMovementSelectableTileType(tileType))
            {
                return false;
            }

            if (captureStandingOffsetDuringInitialization)
            {
                standingOffset = transform.position - tileManager.GridToWorldCenter(coordinate);
            }

            currentCoordinate = coordinate;
            isInitialized = true;
            EnsureSlopeCache();
            if (snapTransformToGrid)
            {
                transform.position = GetStandingWorldPosition(coordinate);
            }

            return true;
        }

        /// <summary>
        /// Calculates a budgeted path without moving, spending AP, or changing unit state.
        /// </summary>
        public GridPathResult CalculatePathPreview(Vector3Int targetCoordinate)
        {
            if (isMoving)
            {
                return GridPathResult.Failure(GridPathFailureReason.AlreadyMoving);
            }

            if (tileManager == null)
            {
                return GridPathResult.Failure(GridPathFailureReason.DependenciesUnavailable);
            }

            if (!isInitialized)
            {
                return GridPathResult.Failure(GridPathFailureReason.UnitNotInitialized);
            }

            TileGridSnapshot snapshot = new TileGridSnapshot(tileManager.Tiles);
            IGridTraversalPolicy traversalPolicy = externalTraversalPolicy ??
                                                   new OneCellGridTraversalPolicy(snapshot);
            return pathfinder.FindPath(
                snapshot,
                currentCoordinate,
                targetCoordinate,
                maximumUpwardJumpHeight,
                maximumSafeDownwardFallHeight,
                hazardTraversalPenalty,
                movementAmount,
                CurrentActionPoints,
                traversalPolicy);
        }

        /// <summary>
        /// Previews a selector result only for units configured for player tile selection.
        /// </summary>
        public GridPathResult CalculatePlayerSelectionPreview(MovementTileSelection selection)
        {
            return requiresPlayerTileSelection
                ? CalculatePathPreview(selection.Coordinate)
                : GridPathResult.Failure(GridPathFailureReason.PlayerSelectionNotAccepted);
        }

        /// <summary>
        /// Revalidates a target, spends AP, and starts following the accepted path.
        /// Player and AI targets enter through this same method.
        /// </summary>
        public bool TryRequestMovement(Vector3Int targetCoordinate, out GridPathResult result)
        {
            result = CalculatePathPreview(targetCoordinate);
            if (!result.IsSuccessful)
            {
                return false;
            }

            if (result.Coordinates.Count > 1 && (!isActiveAndEnabled || !gameObject.activeInHierarchy))
            {
                result = GridPathResult.Failure(GridPathFailureReason.DependenciesUnavailable);
                return false;
            }

            if (result.Coordinates.Count <= 1)
            {
                if (!EffectiveActionPointProvider.TrySpendActionPoints(result.RequiredActionPoints))
                {
                    result = GridPathResult.Failure(GridPathFailureReason.InsufficientActionPoints);
                    return false;
                }

                isUsingSlowMovement = false;
                MovementCompleted?.Invoke(this, currentCoordinate);
                return true;
            }

            if (!TryPrepareExecutedPath(result.Coordinates))
            {
                result = GridPathResult.Failure(GridPathFailureReason.StalePath);
                return false;
            }

            if (!EffectiveActionPointProvider.TrySpendActionPoints(result.RequiredActionPoints))
            {
                activeExecutedPath = null;
                result = GridPathResult.Failure(GridPathFailureReason.InsufficientActionPoints);
                return false;
            }

            isMoving = true;
            activePath = result;
            isUsingSlowMovement =
                result.Coordinates.Count - 1 < slowMovementTransitionThreshold;
            nextExecutedCellIndexInSegment = 1;
            deterministicExecutedSegmentIndex = 0;
            deterministicExecutedSegmentProgress = 0f;
            CacheVisualRootBaseline();
            ExecutedMovementPath committedExecutedPath = activeExecutedPath;
            MovementStarted?.Invoke(this, result);
            if (isMoving)
            {
                ExecutedMovementStarted?.Invoke(this, committedExecutedPath);
            }
            if (isMoving)
            {
                movementCoroutine = StartCoroutine(FollowCommittedPath());
            }
            return true;
        }

        /// <summary>
        /// Submits a validated selector result through the same coordinate request used by AI callers.
        /// </summary>
        public bool TryRequestPlayerSelection(
            MovementTileSelection selection,
            out GridPathResult result)
        {
            if (!requiresPlayerTileSelection)
            {
                result = GridPathResult.Failure(GridPathFailureReason.PlayerSelectionNotAccepted);
                return false;
            }

            return TryRequestMovement(selection.Coordinate, out result);
        }

        /// <summary>
        /// Commits a previously previewed destination after recalculating against current state.
        /// </summary>
        public bool TryCommitPath(GridPathResult preview, out GridPathResult committedResult)
        {
            if (preview == null || !preview.IsSuccessful || preview.Coordinates.Count == 0)
            {
                committedResult = GridPathResult.Failure(GridPathFailureReason.StalePath);
                return false;
            }

            return TryRequestMovement(preview.Destination, out committedResult);
        }

        public void CancelMovement()
        {
            if (!isMoving)
            {
                return;
            }

            if (movementCoroutine != null)
            {
                StopCoroutine(movementCoroutine);
                movementCoroutine = null;
            }

            isMoving = false;
            activePath = null;
            activeExecutedPath = null;
            preparedPresentationSegments.Clear();
            preparedExecutedSegments.Clear();
            nextExecutedCellIndexInSegment = 0;
            RestoreVisualRootBaseline();
            hasVisualRootBaseline = false;
            transform.position = GetStandingWorldPosition(currentCoordinate);
            isUsingSlowMovement = false;
            MovementInterrupted?.Invoke(this, currentCoordinate);
        }

        public void SetActionPointProvider(IActionPointProvider actionPointProvider)
        {
            if (ReferenceEquals(actionPointProvider, this))
            {
                throw new ArgumentException("UnitMovement cannot be its own external AP provider.");
            }

            externalActionPointProvider = actionPointProvider;
        }

        public void SetTraversalPolicy(IGridTraversalPolicy traversalPolicy)
        {
            externalTraversalPolicy = traversalPolicy;
        }

        public void ResetTemporaryActionPoints()
        {
            temporaryCurrentActionPoints = temporaryStartingActionPoints;
        }

        bool IActionPointProvider.TrySpendActionPoints(int amount)
        {
            if (amount < 0 || amount > temporaryCurrentActionPoints)
            {
                return false;
            }

            temporaryCurrentActionPoints -= amount;
            return true;
        }

        private bool TryPrepareExecutedPath(IReadOnlyList<Vector3Int> logicalCoordinates)
        {
            activeExecutedPath = null;
            preparedPresentationSegments.Clear();
            preparedExecutedSegments.Clear();
            TileGridSnapshot snapshot = new TileGridSnapshot(tileManager.Tiles);
            IGridTraversalPolicy traversalPolicy = externalTraversalPolicy ??
                                                   new OneCellGridTraversalPolicy(snapshot);
            IReadOnlyList<MovementPresentationSegment> gridValidatedSegments =
                presentationPathBuilder.Build(
                    snapshot,
                    logicalCoordinates,
                    traversalPolicy,
                    this);
            for (int segmentIndex = 0;
                 segmentIndex < gridValidatedSegments.Count;
                 segmentIndex++)
            {
                AddPhysicallyValidatedSegments(
                    gridValidatedSegments[segmentIndex],
                    logicalCoordinates,
                    snapshot,
                    traversalPolicy);
            }

            for (int segmentIndex = 0;
                 segmentIndex < preparedPresentationSegments.Count;
                 segmentIndex++)
            {
                MovementPresentationSegment presentationSegment =
                    preparedPresentationSegments[segmentIndex];
                if (!TryCollectSafeCrossedCoordinates(
                        presentationSegment,
                        snapshot,
                        traversalPolicy,
                        executedCrossedCoordinates))
                {
                    preparedExecutedSegments.Clear();
                    return false;
                }

                preparedExecutedSegments.Add(new ExecutedMovementSegment(
                    presentationSegment,
                    GetStandingWorldPosition(presentationSegment.StartCoordinate),
                    GetStandingWorldPosition(presentationSegment.EndCoordinate),
                    executedCrossedCoordinates));
            }

            if (preparedExecutedSegments.Count == 0)
            {
                return false;
            }

            activeExecutedPath = new ExecutedMovementPath(preparedExecutedSegments);
            return true;
        }

        private void AddPhysicallyValidatedSegments(
            MovementPresentationSegment segment,
            IReadOnlyList<Vector3Int> logicalCoordinates,
            TileGridSnapshot snapshot,
            IGridTraversalPolicy traversalPolicy)
        {
            if ((segment.Type != MovementPresentationSegmentType.FlatRun &&
                 segment.Type != MovementPresentationSegmentType.SlopeRun) ||
                segment.LogicalTransitionCount <= 1)
            {
                preparedPresentationSegments.Add(segment);
                return;
            }

            int subsectionStart = segment.LogicalStartIndex;
            while (subsectionStart < segment.LogicalEndIndex)
            {
                int farthestSafeEnd = subsectionStart + 1;
                for (int candidateEnd = subsectionStart + 2;
                     candidateEnd <= segment.LogicalEndIndex;
                     candidateEnd++)
                {
                    MovementPresentationSegment candidateSegment =
                        new MovementPresentationSegment(
                            segment.Type,
                            subsectionStart,
                            candidateEnd,
                            logicalCoordinates);
                    if (!TryCollectSafeCrossedCoordinates(
                            candidateSegment,
                            snapshot,
                            traversalPolicy,
                            executedCrossedCoordinates) ||
                        !IsPresentationCapsuleCorridorClear(
                            logicalCoordinates[subsectionStart],
                            logicalCoordinates[candidateEnd]))
                    {
                        break;
                    }

                    farthestSafeEnd = candidateEnd;
                }

                preparedPresentationSegments.Add(new MovementPresentationSegment(
                    segment.Type,
                    subsectionStart,
                    farthestSafeEnd,
                    logicalCoordinates));
                subsectionStart = farthestSafeEnd;
            }
        }

        private bool TryCollectSafeCrossedCoordinates(
            MovementPresentationSegment segment,
            TileGridSnapshot snapshot,
            IGridTraversalPolicy traversalPolicy,
            List<Vector3Int> crossedCoordinates)
        {
            crossedCoordinates.Clear();
            if (segment.LogicalTransitionCount == 1 &&
                traversalPolicy != null &&
                !traversalPolicy.CanTraverse(
                    segment.StartCoordinate,
                    segment.EndCoordinate))
            {
                return false;
            }

            if (segment.Type != MovementPresentationSegmentType.FlatRun &&
                segment.Type != MovementPresentationSegmentType.SlopeRun)
            {
                crossedCoordinates.Add(segment.StartCoordinate);
                crossedCoordinates.Add(segment.EndCoordinate);
                return IsValidExecutedEndpoint(segment.StartCoordinate, snapshot, null) &&
                       IsValidExecutedEndpoint(
                           segment.EndCoordinate,
                           snapshot,
                           traversalPolicy);
            }

            GridSupercover.CollectHorizontalCells(
                segment.StartCoordinate.x,
                segment.StartCoordinate.z,
                segment.EndCoordinate.x,
                segment.EndCoordinate.z,
                executedSupercoverCells);
            if (segment.Type == MovementPresentationSegmentType.FlatRun)
            {
                if (segment.StartCoordinate.y != segment.EndCoordinate.y)
                {
                    return false;
                }

                for (int cellIndex = 0;
                     cellIndex < executedSupercoverCells.Count;
                     cellIndex++)
                {
                    Vector2Int cell = executedSupercoverCells[cellIndex];
                    Vector3Int coordinate = new Vector3Int(
                        cell.x,
                        segment.StartCoordinate.y,
                        cell.y);
                    bool requiresOrdinaryFloor = segment.LogicalTransitionCount > 1 ||
                                                 executedSupercoverCells.Count > 2;
                    if (!IsValidExecutedEndpoint(
                            coordinate,
                            snapshot,
                            cellIndex == 0 ? null : traversalPolicy) ||
                        (requiresOrdinaryFloor &&
                         (!snapshot.TryGetTile(
                              coordinate,
                              out TileManager.TileType tileType) ||
                          tileType != TileManager.TileType.Floor)))
                    {
                        crossedCoordinates.Clear();
                        return false;
                    }

                    crossedCoordinates.Add(coordinate);
                }

                return crossedCoordinates.Count >= 2;
            }

            if (!TryGetContinuousSlopeRegion(
                    segment.StartCoordinate,
                    segment.EndCoordinate,
                    out SlopeRegion slopeRegion))
            {
                return false;
            }

            for (int cellIndex = 0;
                 cellIndex < executedSupercoverCells.Count;
                 cellIndex++)
            {
                Vector3Int coordinate;
                if (cellIndex == 0)
                {
                    coordinate = segment.StartCoordinate;
                }
                else if (cellIndex == executedSupercoverCells.Count - 1)
                {
                    coordinate = segment.EndCoordinate;
                }
                else
                {
                    Vector2Int cell = executedSupercoverCells[cellIndex];
                    Vector3 horizontalProbe = tileManager.GridToWorldCenter(
                        new Vector3Int(cell.x, 0, cell.y));
                    float worldElevation = slopeRegion.EvaluateWorldHeight(horizontalProbe);
                    float gridElevation =
                        (worldElevation - tileManager.GridOrigin.y) / tileManager.CellSize;
                    if (!TryResolveWalkableSlopeCoordinate(
                            slopeRegion,
                            cell.x,
                            cell.y,
                            gridElevation,
                            snapshot,
                            traversalPolicy,
                            out coordinate))
                    {
                        crossedCoordinates.Clear();
                        return false;
                    }
                }

                if (!IsValidExecutedEndpoint(
                        coordinate,
                        snapshot,
                        cellIndex == 0 ? null : traversalPolicy) ||
                    !snapshot.TryGetTile(coordinate, out TileManager.TileType tileType) ||
                    (tileType != TileManager.TileType.Slope &&
                     coordinate != segment.StartCoordinate &&
                     coordinate != segment.EndCoordinate))
                {
                    crossedCoordinates.Clear();
                    return false;
                }

                crossedCoordinates.Add(coordinate);
            }

            return crossedCoordinates.Count >= 2;
        }

        private static bool TryResolveWalkableSlopeCoordinate(
            SlopeRegion slopeRegion,
            int x,
            int z,
            float gridElevation,
            TileGridSnapshot snapshot,
            IGridTraversalPolicy traversalPolicy,
            out Vector3Int coordinate)
        {
            coordinate = default;
            bool found = false;
            float bestDistance = float.PositiveInfinity;
            IReadOnlyList<Vector3Int> members = slopeRegion.Members;
            for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                Vector3Int candidate = members[memberIndex];
                if (candidate.x != x ||
                    candidate.z != z ||
                    !snapshot.TryGetTile(candidate, out TileManager.TileType tileType) ||
                    tileType != TileManager.TileType.Slope ||
                    (traversalPolicy != null && !traversalPolicy.CanEnter(candidate)))
                {
                    continue;
                }

                float distance = Mathf.Abs(candidate.y - gridElevation);
                // Thin voxelized slopes commonly occupy both cells around the plane.
                // Only the upper one has clear headroom; prefer it on an exact tie.
                if (!found ||
                    distance < bestDistance - 0.0001f ||
                    (Mathf.Abs(distance - bestDistance) <= 0.0001f &&
                     candidate.y > coordinate.y))
                {
                    coordinate = candidate;
                    bestDistance = distance;
                    found = true;
                }
            }

            return found;
        }

        private static bool IsValidExecutedEndpoint(
            Vector3Int coordinate,
            TileGridSnapshot snapshot,
            IGridTraversalPolicy traversalPolicy)
        {
            return snapshot.TryGetTile(coordinate, out TileManager.TileType tileType) &&
                   TileManager.IsMovementSelectableTileType(tileType) &&
                   (traversalPolicy == null || traversalPolicy.CanEnter(coordinate));
        }

        private bool IsPresentationCapsuleCorridorClear(Vector3Int start, Vector3Int end)
        {
            if (movementBodyCollider == null || presentationObstructionMask.value == 0)
            {
                return true;
            }

            Vector3 startPosition = GetStandingWorldPosition(start);
            Vector3 endPosition = GetStandingWorldPosition(end);
            Vector3 castDelta = endPosition - startPosition;
            float castDistance = castDelta.magnitude;
            if (castDistance <= positionArrivalTolerance)
            {
                return true;
            }

            Transform colliderTransform = movementBodyCollider.transform;
            Vector3 lossyScale = colliderTransform.lossyScale;
            Vector3 localAxis;
            float axisScale;
            float perpendicularScale;
            switch (movementBodyCollider.direction)
            {
                case 0:
                    localAxis = Vector3.right;
                    axisScale = Mathf.Abs(lossyScale.x);
                    perpendicularScale = Mathf.Max(
                        Mathf.Abs(lossyScale.y),
                        Mathf.Abs(lossyScale.z));
                    break;
                case 2:
                    localAxis = Vector3.forward;
                    axisScale = Mathf.Abs(lossyScale.z);
                    perpendicularScale = Mathf.Max(
                        Mathf.Abs(lossyScale.x),
                        Mathf.Abs(lossyScale.y));
                    break;
                default:
                    localAxis = Vector3.up;
                    axisScale = Mathf.Abs(lossyScale.y);
                    perpendicularScale = Mathf.Max(
                        Mathf.Abs(lossyScale.x),
                        Mathf.Abs(lossyScale.z));
                    break;
            }

            float radius = movementBodyCollider.radius * perpendicularScale;
            float height = Mathf.Max(movementBodyCollider.height * axisScale, radius * 2f);
            Vector3 axis = colliderTransform.TransformDirection(localAxis).normalized;
            Vector3 colliderCenterOffset =
                colliderTransform.TransformPoint(movementBodyCollider.center) -
                transform.position;
            Vector3 colliderCenterAtStart = startPosition + colliderCenterOffset;
            float endpointOffset = Mathf.Max(0f, (height * 0.5f) - radius);
            Vector3 pointA = colliderCenterAtStart + (axis * endpointOffset);
            Vector3 pointB = colliderCenterAtStart - (axis * endpointOffset);
            int hitCount = Physics.CapsuleCastNonAlloc(
                pointA,
                pointB,
                radius,
                castDelta / castDistance,
                presentationClearanceHits,
                castDistance,
                presentationObstructionMask.value,
                QueryTriggerInteraction.Ignore);
            if (hitCount >= presentationClearanceHits.Length)
            {
                return false;
            }

            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                Collider hitCollider = presentationClearanceHits[hitIndex].collider;
                if (hitCollider != null &&
                    !hitCollider.transform.IsChildOf(transform) &&
                    !transform.IsChildOf(hitCollider.transform))
                {
                    return false;
                }
            }

            return true;
        }

        private void CacheVisualRootBaseline()
        {
            hasVisualRootBaseline = visualRoot != null && visualRoot != transform;
            if (hasVisualRootBaseline)
            {
                visualRootBaselineLocalPosition = visualRoot.localPosition;
            }
        }

        private void RestoreVisualRootBaseline()
        {
            if (hasVisualRootBaseline && visualRoot != null)
            {
                visualRoot.localPosition = visualRootBaselineLocalPosition;
            }
        }

        private IEnumerator FollowCommittedPath()
        {
            // Defer segment callbacks until StartCoroutine has returned and the handle
            // is stored, so an event listener can safely cancel the active phase.
            yield return null;
            if (!isMoving)
            {
                yield break;
            }

            Vector3 previousHorizontalDirection = Vector3.zero;
            for (int segmentIndex = 0;
                 segmentIndex < activeExecutedPath.Segments.Count && isMoving;
                 segmentIndex++)
            {
                ExecutedMovementSegment segment = activeExecutedPath.Segments[segmentIndex];
                nextExecutedCellIndexInSegment = 1;
                Vector3 horizontalDirection = GetHorizontalDirection(segment);
                yield return ExecuteExecutedSegment(
                    segment,
                    previousHorizontalDirection,
                    horizontalDirection);
                if (horizontalDirection.sqrMagnitude > Mathf.Epsilon)
                {
                    previousHorizontalDirection = horizontalDirection;
                }
            }

            if (isMoving)
            {
                CompleteMovement();
            }
        }

        private IEnumerator ExecuteExecutedSegment(
            ExecutedMovementSegment segment,
            Vector3 previousHorizontalDirection,
            Vector3 horizontalDirection)
        {
            switch (segment.Type)
            {
                case MovementPresentationSegmentType.FlatRun:
                case MovementPresentationSegmentType.SlopeRun:
                    yield return ExecuteRunSegment(
                        segment,
                        previousHorizontalDirection,
                        horizontalDirection);
                    break;
                case MovementPresentationSegmentType.Clamber:
                    yield return ExecuteClamberSegment(
                        segment,
                        previousHorizontalDirection,
                        horizontalDirection);
                    break;
                case MovementPresentationSegmentType.Jump:
                    yield return ExecuteJumpSegment(
                        segment,
                        previousHorizontalDirection,
                        horizontalDirection);
                    break;
                case MovementPresentationSegmentType.Drop:
                    yield return ExecuteDropSegment(
                        segment,
                        previousHorizontalDirection,
                        horizontalDirection);
                    break;
                case MovementPresentationSegmentType.LadderAscent:
                    yield return ExecuteLadderSegment(
                        segment,
                        previousHorizontalDirection,
                        horizontalDirection);
                    break;
            }
        }

        private IEnumerator ExecuteRunSegment(
            ExecutedMovementSegment segment,
            Vector3 previousHorizontalDirection,
            Vector3 horizontalDirection)
        {
            MovementPresentationSegment presentation = segment.PresentationSegment;
            RunBegan?.Invoke(this, presentation);
            Vector3 startPosition = segment.StartWorldPosition;
            Vector3 endPosition = segment.EndWorldPosition;
            float travelDistance = Vector3.Distance(startPosition, endPosition);
            float progress = 0f;
            bool significantTurn = IsSignificantPresentationTurn(
                previousHorizontalDirection,
                horizontalDirection);
            bool turnEnded = !significantTurn;
            if (significantTurn)
            {
                TurnBegan?.Invoke(this, presentation);
            }

            while (progress < 1f && isMoving)
            {
                bool isStillTurning = RotateTowards(horizontalDirection, Time.deltaTime);
                if (significantTurn && !isStillTurning && !turnEnded)
                {
                    turnEnded = true;
                    TurnEnded?.Invoke(this, presentation);
                }

                float effectiveSpeed = significantTurn && !turnEnded
                    ? CurrentMovementSpeed * significantTurnSpeedMultiplier
                    : CurrentMovementSpeed;
                progress = travelDistance <= positionArrivalTolerance
                    ? 1f
                    : Mathf.Min(1f, progress + (effectiveSpeed * Time.deltaTime / travelDistance));
                transform.position = EvaluateRunPosition(
                    presentation,
                    startPosition,
                    endPosition,
                    progress);
                AdvanceExecutedProgress(segment, progress);
                yield return null;
            }

            if (significantTurn && !turnEnded)
            {
                TurnEnded?.Invoke(this, presentation);
            }

            if (isMoving)
            {
                transform.position = endPosition;
                AdvanceExecutedProgress(segment, 1f);
                RunEnded?.Invoke(this, presentation);
            }
        }

        private IEnumerator ExecuteJumpSegment(
            ExecutedMovementSegment segment,
            Vector3 previousHorizontalDirection,
            Vector3 horizontalDirection)
        {
            yield return OrientForSpecialSegment(
                segment,
                previousHorizontalDirection,
                horizontalDirection);
            JumpAnticipationBegan?.Invoke(this, segment.PresentationSegment);
            yield return WaitForScaledSeconds(preJumpDelay);
            if (!isMoving)
            {
                yield break;
            }

            JumpTakeoff?.Invoke(this, segment.PresentationSegment);
            Vector3 startPosition = segment.StartWorldPosition;
            Vector3 endPosition = segment.EndWorldPosition;
            yield return MoveSpecialSegment(
                segment,
                startPosition,
                endPosition,
                jumpDuration,
                null,
                true,
                false);
            if (!isMoving)
            {
                yield break;
            }

            JumpLanding?.Invoke(this, segment.PresentationSegment);
            yield return WaitForScaledSeconds(postJumpLandingDelay);
        }

        private IEnumerator ExecuteClamberSegment(
            ExecutedMovementSegment segment,
            Vector3 previousHorizontalDirection,
            Vector3 horizontalDirection)
        {
            yield return OrientForSpecialSegment(
                segment,
                previousHorizontalDirection,
                horizontalDirection);
            ClamberBegan?.Invoke(this, segment.PresentationSegment);
            yield return WaitForScaledSeconds(preClamberDelay);
            if (!isMoving)
            {
                yield break;
            }

            yield return MoveSpecialSegment(
                segment,
                segment.StartWorldPosition,
                segment.EndWorldPosition,
                clamberDuration,
                clamberMotionCurve,
                false,
                false);
            if (!isMoving)
            {
                yield break;
            }

            ClamberCompleted?.Invoke(this, segment.PresentationSegment);
            yield return WaitForScaledSeconds(postClamberDelay);
        }

        private IEnumerator ExecuteDropSegment(
            ExecutedMovementSegment segment,
            Vector3 previousHorizontalDirection,
            Vector3 horizontalDirection)
        {
            yield return OrientForSpecialSegment(
                segment,
                previousHorizontalDirection,
                horizontalDirection);
            DropPreparationBegan?.Invoke(this, segment.PresentationSegment);
            yield return WaitForScaledSeconds(preDropDelay);
            if (!isMoving)
            {
                yield break;
            }

            DropBegan?.Invoke(this, segment.PresentationSegment);
            yield return MoveSpecialSegment(
                segment,
                segment.StartWorldPosition,
                segment.EndWorldPosition,
                dropDuration,
                dropMotionCurve,
                false,
                false);
            if (!isMoving)
            {
                yield break;
            }

            DropLanded?.Invoke(this, segment.PresentationSegment);
            int dropHeight = Mathf.Abs(
                segment.PresentationSegment.EndCoordinate.y -
                segment.PresentationSegment.StartCoordinate.y);
            yield return WaitForScaledSeconds(CalculateDropLandingDelay(dropHeight));
        }

        private IEnumerator ExecuteLadderSegment(
            ExecutedMovementSegment segment,
            Vector3 previousHorizontalDirection,
            Vector3 horizontalDirection)
        {
            yield return OrientForSpecialSegment(
                segment,
                previousHorizontalDirection,
                horizontalDirection);
            LadderPreparationBegan?.Invoke(this, segment.PresentationSegment);
            yield return WaitForScaledSeconds(preLadderDelay);
            if (!isMoving)
            {
                yield break;
            }

            LadderClimbBegan?.Invoke(this, segment.PresentationSegment);
            Vector3 startPosition = segment.StartWorldPosition;
            Vector3 endPosition = segment.EndWorldPosition;
            Vector3 ladderClimbEndPosition = CalculateLadderClimbEndPosition(
                startPosition,
                endPosition);
            float duration = Vector3.Distance(startPosition, ladderClimbEndPosition) /
                             ladderClimbingSpeed;
            yield return MoveSpecialSegment(
                segment,
                startPosition,
                ladderClimbEndPosition,
                duration,
                ladderMotionCurve,
                false,
                true,
                false);
            RestoreVisualRootBaseline();
            if (!isMoving)
            {
                yield break;
            }

            LadderClimbCompleted?.Invoke(this, segment.PresentationSegment);
            ClamberBegan?.Invoke(this, segment.PresentationSegment);
            yield return WaitForScaledSeconds(preClamberDelay);
            if (!isMoving)
            {
                yield break;
            }

            yield return MoveSpecialSegment(
                segment,
                ladderClimbEndPosition,
                endPosition,
                clamberDuration,
                clamberMotionCurve,
                false,
                false);
            if (!isMoving)
            {
                yield break;
            }

            ClamberCompleted?.Invoke(this, segment.PresentationSegment);
            yield return WaitForScaledSeconds(postClamberDelay);
            yield return WaitForScaledSeconds(postLadderDelay);
        }

        private Vector3 CalculateLadderClimbEndPosition(
            Vector3 startPosition,
            Vector3 endPosition)
        {
            float totalRise = Mathf.Max(0f, endPosition.y - startPosition.y);
            float configuredClamberRise =
                tileManager.CellSize * ladderTopClamberHeightInCells;
            float clamberRise = Mathf.Min(configuredClamberRise, totalRise);
            return endPosition - (Vector3.up * clamberRise);
        }

        private IEnumerator OrientForSpecialSegment(
            ExecutedMovementSegment segment,
            Vector3 previousHorizontalDirection,
            Vector3 horizontalDirection)
        {
            if (!faceMovementDirection || horizontalDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                yield break;
            }

            bool significantTurn = IsSignificantPresentationTurn(
                previousHorizontalDirection,
                horizontalDirection);
            if (significantTurn)
            {
                TurnBegan?.Invoke(this, segment.PresentationSegment);
            }

            while (RotateTowards(horizontalDirection, Time.deltaTime) && isMoving)
            {
                yield return null;
            }

            if (significantTurn && isMoving)
            {
                TurnEnded?.Invoke(this, segment.PresentationSegment);
            }
        }

        private IEnumerator MoveSpecialSegment(
            ExecutedMovementSegment segment,
            Vector3 startPosition,
            Vector3 endPosition,
            float duration,
            AnimationCurve motionCurve,
            bool applyJumpArc,
            bool applyLadderBob,
            bool advanceExecutedProgress = true)
        {
            float elapsed = 0f;
            float previousMotionProgress = 0f;
            duration = Mathf.Max(MinimumPositiveValue, duration);
            while (elapsed < duration && isMoving)
            {
                elapsed = Mathf.Min(duration, elapsed + Time.deltaTime);
                float normalizedTime = elapsed / duration;
                float motionProgress = motionCurve == null
                    ? normalizedTime
                    : Mathf.Clamp01(motionCurve.Evaluate(normalizedTime));
                if (applyLadderBob)
                {
                    // Authored ladder curves cannot reverse the authoritative root ascent.
                    motionProgress = Mathf.Max(previousMotionProgress, motionProgress);
                }

                previousMotionProgress = motionProgress;
                Vector3 position = Vector3.LerpUnclamped(
                    startPosition,
                    endPosition,
                    motionProgress);
                if (applyJumpArc)
                {
                    position.y += jumpArcHeight * jumpArcProfile.Evaluate(normalizedTime);
                }

                transform.position = position;
                RotateTowards(GetHorizontalDirection(segment), Time.deltaTime);
                if (applyLadderBob && hasVisualRootBaseline)
                {
                    visualRoot.localPosition = visualRootBaselineLocalPosition +
                                                (Vector3.up * CalculateLadderBob(normalizedTime));
                }

                if (advanceExecutedProgress)
                {
                    AdvanceExecutedProgress(segment, normalizedTime);
                }
                yield return null;
            }

            if (isMoving)
            {
                transform.position = endPosition;
                if (advanceExecutedProgress)
                {
                    AdvanceExecutedProgress(segment, 1f);
                }
            }
        }

        /// <summary>
        /// Delays use scaled game time so movement presentation observes Unity's normal pause state.
        /// </summary>
        private IEnumerator WaitForScaledSeconds(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration && isMoving)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private Vector3 EvaluateRunPosition(
            MovementPresentationSegment segment,
            Vector3 startPosition,
            Vector3 endPosition,
            float progress)
        {
            if (segment.Type != MovementPresentationSegmentType.SlopeRun ||
                !TryGetSlopeRegionForSegment(
                    segment.StartCoordinate,
                    segment.EndCoordinate,
                    out SlopeRegion slopeRegion))
            {
                return Vector3.Lerp(startPosition, endPosition, progress);
            }

            Vector3 startCenter = tileManager.GridToWorldCenter(segment.StartCoordinate);
            Vector3 endCenter = tileManager.GridToWorldCenter(segment.EndCoordinate);
            Vector3 baseHorizontalPosition = Vector3.Lerp(startCenter, endCenter, progress);
            bool startsOnSlope = IsSlopeCoordinate(segment.StartCoordinate);
            bool endsOnSlope = IsSlopeCoordinate(segment.EndCoordinate);
            return new Vector3(
                baseHorizontalPosition.x + standingOffset.x,
                slopeRegion.EvaluateWorldHeight(baseHorizontalPosition) +
                standingOffset.y +
                (slopeTraversalHeightOffset * GetSlopeOffsetWeight(
                    startsOnSlope,
                    endsOnSlope,
                    progress)),
                baseHorizontalPosition.z + standingOffset.z);
        }

        private void AdvanceExecutedProgress(ExecutedMovementSegment segment, float progress)
        {
            int transitionCount = segment.CrossedCoordinates.Count - 1;
            int completedTransitionCount = Mathf.Min(
                transitionCount,
                Mathf.FloorToInt((Mathf.Clamp01(progress) * transitionCount) +
                                 0.00001f));
            while (nextExecutedCellIndexInSegment <= completedTransitionCount &&
                   nextExecutedCellIndexInSegment < segment.CrossedCoordinates.Count)
            {
                currentCoordinate =
                    segment.CrossedCoordinates[nextExecutedCellIndexInSegment];
                nextExecutedCellIndexInSegment++;
                ExecutedTileEntered?.Invoke(this, currentCoordinate);
                LogicalTileEntered?.Invoke(this, currentCoordinate);
            }
        }

        private static Vector3 GetHorizontalDirection(ExecutedMovementSegment segment)
        {
            Vector3 direction = segment.EndWorldPosition - segment.StartWorldPosition;
            direction.y = 0f;
            return direction.sqrMagnitude > Mathf.Epsilon ? direction.normalized : Vector3.zero;
        }

        private bool RotateTowards(Vector3 horizontalDirection, float deltaTime)
        {
            if (!faceMovementDirection || horizontalDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                return false;
            }

            Quaternion targetRotation = Quaternion.LookRotation(horizontalDirection, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Mathf.Max(0f, deltaTime));
            return Quaternion.Angle(transform.rotation, targetRotation) > rotationArrivalTolerance;
        }

        public bool IsSignificantPresentationTurn(Vector3 firstDirection, Vector3 secondDirection)
        {
            return CalculateHorizontalTurnAngle(firstDirection, secondDirection) + 0.0001f >=
                   significantTurnThreshold;
        }

        public static float CalculateHorizontalTurnAngle(
            Vector3 firstDirection,
            Vector3 secondDirection)
        {
            firstDirection.y = 0f;
            secondDirection.y = 0f;
            return firstDirection.sqrMagnitude <= Mathf.Epsilon ||
                   secondDirection.sqrMagnitude <= Mathf.Epsilon
                ? 0f
                : Vector3.Angle(firstDirection, secondDirection);
        }

        /// <summary>
        /// Uses the base delay for the first dropped cell and adds the configured amount
        /// only for cells beyond the first, then applies the configured clamps.
        /// </summary>
        public float CalculateDropLandingDelay(int dropHeightInCells)
        {
            int additionalCells = Mathf.Max(0, dropHeightInCells - 1);
            float delay = baseDropLandingDelay +
                          (additionalDropLandingDelayPerCellBeyondFirst * additionalCells);
            return Mathf.Clamp(
                delay,
                minimumDropLandingDelay,
                Mathf.Max(minimumDropLandingDelay, maximumDropLandingDelay));
        }

        public float CalculateLadderBob(float normalizedProgress)
        {
            float progress = Mathf.Clamp01(normalizedProgress);
            return ladderBobAmplitude *
                   Mathf.Sin(2f * Mathf.PI * ladderBobCycleCount * progress) *
                   Mathf.Sin(Mathf.PI * progress);
        }

        /// <summary>
        /// Advances the executed path by a deterministic time slice for frame-independent tests.
        /// </summary>
        private void AdvanceCommittedMovement(float deltaTime)
        {
            if (!isMoving || activeExecutedPath == null || deltaTime <= 0f)
            {
                return;
            }

            float remainingMovementDistance = CurrentMovementSpeed * deltaTime;
            while (remainingMovementDistance > 0f && isMoving)
            {
                ExecutedMovementSegment segment = activeExecutedPath.Segments[
                    deterministicExecutedSegmentIndex];
                Vector3 delta = segment.EndWorldPosition - segment.StartWorldPosition;
                float segmentDistance = segment.Type == MovementPresentationSegmentType.FlatRun ||
                                        segment.Type == MovementPresentationSegmentType.SlopeRun
                    ? new Vector2(delta.x, delta.z).magnitude
                    : delta.magnitude;
                segmentDistance = Mathf.Max(positionArrivalTolerance, segmentDistance);
                float distanceRemainingInSegment =
                    (1f - deterministicExecutedSegmentProgress) * segmentDistance;
                float movementDistance = Mathf.Min(
                    remainingMovementDistance,
                    distanceRemainingInSegment);
                deterministicExecutedSegmentProgress = Mathf.Min(
                    1f,
                    deterministicExecutedSegmentProgress +
                    (movementDistance / segmentDistance));
                MovementPresentationSegment presentation = segment.PresentationSegment;
                transform.position = presentation.Type ==
                                     MovementPresentationSegmentType.FlatRun ||
                                     presentation.Type ==
                                     MovementPresentationSegmentType.SlopeRun
                    ? EvaluateRunPosition(
                        presentation,
                        segment.StartWorldPosition,
                        segment.EndWorldPosition,
                        deterministicExecutedSegmentProgress)
                    : Vector3.Lerp(
                        segment.StartWorldPosition,
                        segment.EndWorldPosition,
                        deterministicExecutedSegmentProgress);
                Vector3 facingDirection = GetHorizontalDirection(segment);
                if (facingDirection.sqrMagnitude > Mathf.Epsilon)
                {
                    UpdateFacing(
                        Vector2.zero,
                        new Vector2(facingDirection.x, facingDirection.z));
                }

                AdvanceExecutedProgress(segment, deterministicExecutedSegmentProgress);
                remainingMovementDistance -= movementDistance;
                if (deterministicExecutedSegmentProgress < 1f)
                {
                    return;
                }

                transform.position = segment.EndWorldPosition;
                deterministicExecutedSegmentIndex++;
                deterministicExecutedSegmentProgress = 0f;
                nextExecutedCellIndexInSegment = 1;
                if (deterministicExecutedSegmentIndex >= activeExecutedPath.Segments.Count)
                {
                    CompleteMovement();
                }
            }
        }

        private void CompleteMovement()
        {
            GridPathResult completedPath = activePath;
            activePath = null;
            activeExecutedPath = null;
            preparedPresentationSegments.Clear();
            preparedExecutedSegments.Clear();
            nextExecutedCellIndexInSegment = 0;
            movementCoroutine = null;
            isMoving = false;
            RestoreVisualRootBaseline();
            hasVisualRootBaseline = false;
            if (completedPath == null)
            {
                return;
            }

            isUsingSlowMovement = false;
            MovementCompleted?.Invoke(this, currentCoordinate);
        }

        private bool TryGetSlopeRegionForSegment(
            Vector3Int fromCoordinate,
            Vector3Int toCoordinate,
            out SlopeRegion slopeRegion)
        {
            slopeRegion = null;
            EnsureSlopeCache();
            if (slopeRegionCache == null)
            {
                return false;
            }

            Vector3Int slopeCoordinate;
            if (tileManager.TryGetTile(toCoordinate, out TileManager.TileType toType) &&
                toType == TileManager.TileType.Slope)
            {
                slopeCoordinate = toCoordinate;
            }
            else if (tileManager.TryGetTile(fromCoordinate, out TileManager.TileType fromType) &&
                     fromType == TileManager.TileType.Slope)
            {
                slopeCoordinate = fromCoordinate;
            }
            else
            {
                return false;
            }

            SlopeRegionAnalysis analysis = slopeRegionCache.GetAnalysis(slopeCoordinate);
            if (analysis.IsValid)
            {
                slopeRegion = analysis.Region;
                return true;
            }

            Vector3Int diagnosticCoordinate = analysis.DiscoveredCoordinates.Count > 0
                ? analysis.DiscoveredCoordinates[0]
                : slopeCoordinate;
            if (loggedMalformedSlopeCoordinates.Add(diagnosticCoordinate))
            {
                Debug.LogWarning(
                    $"Malformed slope region near {diagnosticCoordinate}: {analysis.FailureReason} " +
                    "Canonical grid-center interpolation will be used.",
                    this);
            }

            return false;
        }

        /// <summary>
        /// A continuous slope transition must follow one valid region. Transitions between
        /// a slope and ordinary terrain are treated as ramp blending only at equal logical
        /// elevation; actual elevation changes retain jump, clamber, or drop presentation.
        /// </summary>
        public bool TryGetContinuousSlopeRegion(
            Vector3Int source,
            Vector3Int destination,
            out SlopeRegion slopeRegion)
        {
            slopeRegion = null;
            bool sourceIsSlope = IsSlopeCoordinate(source);
            bool destinationIsSlope = IsSlopeCoordinate(destination);
            if (!sourceIsSlope && !destinationIsSlope)
            {
                return false;
            }

            if (!TryGetSlopeRegionForSegment(source, destination, out SlopeRegion candidateRegion))
            {
                return false;
            }

            if (sourceIsSlope && destinationIsSlope)
            {
                if (!candidateRegion.Contains(source) || !candidateRegion.Contains(destination))
                {
                    return false;
                }
            }
            else if (source.y != destination.y)
            {
                return false;
            }

            slopeRegion = candidateRegion;
            return true;
        }

        private void UpdateFacing(Vector2 fromHorizontal, Vector2 toHorizontal)
        {
            if (!faceMovementDirection)
            {
                return;
            }

            Vector2 horizontalDirection = toHorizontal - fromHorizontal;
            if (horizontalDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(
                new Vector3(horizontalDirection.x, 0f, horizontalDirection.y),
                Vector3.up);
        }

        private Vector3 GetStandingWorldPosition(Vector3Int coordinate)
        {
            Vector3 worldCenter = tileManager.GridToWorldCenter(coordinate);
            if (tileManager.TryGetTile(coordinate, out TileManager.TileType tileType) &&
                tileType == TileManager.TileType.Slope &&
                TryGetSlopeRegionForSegment(coordinate, coordinate, out SlopeRegion slopeRegion))
            {
                return GetSlopeStandingWorldPosition(worldCenter, slopeRegion);
            }

            return worldCenter + standingOffset;
        }

        private Vector3 GetSlopeStandingWorldPosition(
            Vector3 worldCenter,
            SlopeRegion slopeRegion,
            float slopeOffsetWeight = 1f)
        {
            return new Vector3(
                worldCenter.x + standingOffset.x,
                slopeRegion.EvaluateWorldHeight(worldCenter) + standingOffset.y +
                (slopeTraversalHeightOffset * Mathf.Clamp01(slopeOffsetWeight)),
                worldCenter.z + standingOffset.z);
        }

        private bool IsSlopeCoordinate(Vector3Int coordinate)
        {
            return tileManager.TryGetTile(coordinate, out TileManager.TileType tileType) &&
                   tileType == TileManager.TileType.Slope;
        }

        private static float GetSlopeOffsetWeight(
            bool startsOnSlope,
            bool endsOnSlope,
            float segmentProgress)
        {
            if (startsOnSlope && endsOnSlope)
            {
                return 1f;
            }

            // Floor/slope transitions span one path segment (one cell). SmoothStep
            // keeps the position continuous while also removing the abrupt vertical
            // velocity change that a linear offset introduces at either endpoint.
            float transitionWeight = Mathf.SmoothStep(0f, 1f, segmentProgress);
            if (endsOnSlope)
            {
                return transitionWeight;
            }

            return startsOnSlope ? 1f - transitionWeight : 0f;
        }

        private void HandlePlayerTileSelected(MovementTileSelection selection)
        {
            tileManager?.ClearPlayerDebugPath();
            if (TryRequestPlayerSelection(selection, out GridPathResult result))
            {
                IReadOnlyList<Vector3Int> displayedCoordinates =
                    activeExecutedPath?.Coordinates ?? result.Coordinates;
                tileManager?.ShowPlayerDebugPath(displayedCoordinates);
            }
        }

        private void HandlePlayerTileSelectionFailed()
        {
            tileManager?.ClearPlayerDebugPath();
        }

        private void HandleGridRebuilt()
        {
            loggedMalformedSlopeCoordinates.Clear();
            if (isMoving)
            {
                CancelMovement();
            }

            if (initializeFromWorldPositionOnStart && !isInitialized)
            {
                TryInitializeCurrentCoordinateFromWorldPosition();
            }
        }

        private void SubscribeToDependencies()
        {
            if (requiresPlayerTileSelection && playerTileSelector != null)
            {
                playerTileSelector.MovementTileSelected += HandlePlayerTileSelected;
                playerTileSelector.MovementTileSelectionFailed +=
                    HandlePlayerTileSelectionFailed;
            }

            if (tileManager != null)
            {
                tileManager.GridRebuilt += HandleGridRebuilt;
            }
        }

        private void UnsubscribeFromDependencies()
        {
            if (playerTileSelector != null)
            {
                playerTileSelector.MovementTileSelected -= HandlePlayerTileSelected;
                playerTileSelector.MovementTileSelectionFailed -=
                    HandlePlayerTileSelectionFailed;
            }

            if (tileManager != null)
            {
                tileManager.GridRebuilt -= HandleGridRebuilt;
            }
        }

        private void EnsureSlopeCache()
        {
            if (slopeRegionCache == null && tileManager != null)
            {
                slopeRegionCache = new SlopeRegionCache(tileManager);
            }
        }

        private void OnValidate()
        {
            movementAmount = ValidatePositive(movementAmount, DefaultMovementAmount);
            movementSpeed = ValidatePositive(movementSpeed, DefaultMovementSpeed);
            slowMovementTransitionThreshold = Mathf.Max(1, slowMovementTransitionThreshold);
            slowMovementSpeedMultiplier = Mathf.Clamp(
                slowMovementSpeedMultiplier,
                0.01f,
                1f);
            rotationSpeed = ValidatePositive(rotationSpeed, DefaultRotationSpeed);
            hazardTraversalPenalty = ValidatePositive(
                hazardTraversalPenalty,
                DefaultHazardPenalty);
            significantTurnThreshold = Mathf.Clamp(significantTurnThreshold, 0f, 180f);
            significantTurnSpeedMultiplier = Mathf.Clamp01(significantTurnSpeedMultiplier);
            positionArrivalTolerance = ValidateNonnegative(positionArrivalTolerance);
            rotationArrivalTolerance = Mathf.Clamp(rotationArrivalTolerance, 0f, 180f);
            preJumpDelay = ValidateNonnegative(preJumpDelay);
            postJumpLandingDelay = ValidateNonnegative(postJumpLandingDelay);
            jumpDuration = ValidatePositive(jumpDuration, 0.55f);
            jumpArcHeight = ValidateNonnegative(jumpArcHeight);
            preClamberDelay = ValidateNonnegative(preClamberDelay);
            postClamberDelay = ValidateNonnegative(postClamberDelay);
            clamberDuration = ValidatePositive(clamberDuration, 0.65f);
            preDropDelay = ValidateNonnegative(preDropDelay);
            baseDropLandingDelay = ValidateNonnegative(baseDropLandingDelay);
            additionalDropLandingDelayPerCellBeyondFirst = ValidateNonnegative(
                additionalDropLandingDelayPerCellBeyondFirst);
            minimumDropLandingDelay = ValidateNonnegative(minimumDropLandingDelay);
            maximumDropLandingDelay = Mathf.Max(
                minimumDropLandingDelay,
                ValidateNonnegative(maximumDropLandingDelay));
            dropDuration = ValidatePositive(dropDuration, 0.45f);
            preLadderDelay = ValidateNonnegative(preLadderDelay);
            postLadderDelay = ValidateNonnegative(postLadderDelay);
            ladderClimbingSpeed = ValidatePositive(ladderClimbingSpeed, 2f);
            ladderTopClamberHeightInCells = ValidateNonnegative(
                ladderTopClamberHeightInCells);
            ladderBobAmplitude = ValidateNonnegative(ladderBobAmplitude);
            ladderBobCycleCount = ValidateNonnegative(ladderBobCycleCount);
            EnsurePresentationCurves();
            maximumUpwardJumpHeight = Mathf.Max(0, maximumUpwardJumpHeight);
            maximumSafeDownwardFallHeight = Mathf.Max(0, maximumSafeDownwardFallHeight);
            temporaryStartingActionPoints = Mathf.Max(0, temporaryStartingActionPoints);
            temporaryCurrentActionPoints = Mathf.Clamp(
                temporaryCurrentActionPoints,
                0,
                temporaryStartingActionPoints);
            if (float.IsNaN(slopeTraversalHeightOffset) ||
                float.IsInfinity(slopeTraversalHeightOffset))
            {
                slopeTraversalHeightOffset = 0f;
            }
        }

        private static float ValidatePositive(float value, float fallback)
        {
            return value >= MinimumPositiveValue &&
                   !float.IsNaN(value) &&
                   !float.IsInfinity(value)
                ? value
                : fallback;
        }

        private static float ValidateNonnegative(float value)
        {
            return value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value)
                ? value
                : 0f;
        }

        private void EnsurePresentationCurves()
        {
            jumpArcProfile ??= new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.5f, 1f),
                new Keyframe(1f, 0f));
            clamberMotionCurve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            dropMotionCurve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            ladderMotionCurve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }
    }
}
