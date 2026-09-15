using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PCOM
{
    /// <summary>
    /// Resolves pointer rays against authored floor-selection geometry, then asks the
    /// tile grid whether the corresponding coordinate is movement-selectable.
    /// </summary>
    public class PlayerTileSelector : MonoBehaviour
    {
        public event Action<MovementTileSelection> MovementTileSelected;
        public event Action MovementTileSelectionFailed;

        private const float MinimumSelectionDistance = 0.01f;
        private const float DefaultSelectionDistance = 250f;
        private const float MinimumSurfaceBoundaryToleranceFraction = 0.0001f;
        private const float DefaultSurfaceBoundaryToleranceFraction = 0.01f;
        private const float MaximumSurfaceBoundaryToleranceFraction = 0.1f;

        [Header("Movement Selection")]
        [SerializeField] private bool movementSelectionModeActive;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private TileManager tileManager;
        [Tooltip("Only colliders on dedicated floor-selection layers should be included.")]
        [SerializeField] private LayerMask floorSelectionLayerMask;
        [SerializeField, Min(MinimumSelectionDistance)]
        private float maximumSelectionDistance = DefaultSelectionDistance;
        [Tooltip("Fraction of TileManager.CellSize used to move a surface hit just inside its cell before resolving boundary ambiguity.")]
        [SerializeField, Range(
            MinimumSurfaceBoundaryToleranceFraction,
            MaximumSurfaceBoundaryToleranceFraction)]
        private float surfaceBoundaryToleranceFraction = DefaultSurfaceBoundaryToleranceFraction;

        private bool setupErrorLogged;
        private bool hasLoggedHoverSelection;
        private Vector3Int lastLoggedHoverCoordinate;
        private TileManager.TileType lastLoggedHoverTileType;

        public bool IsMovementSelectionModeActive => movementSelectionModeActive;

        /// <summary>
        /// Enables or disables pointer processing for movement tile selection.
        /// </summary>
        public void SetMovementSelectionMode(bool isActive)
        {
            movementSelectionModeActive = isActive;
            if (!isActive)
            {
                hasLoggedHoverSelection = false;
            }
        }

        private void Update()
        {
            if (!movementSelectionModeActive)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                hasLoggedHoverSelection = false;
                return;
            }

            Vector2 screenPosition = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (TrySelectMovementTile(screenPosition, out MovementTileSelection clickSelection))
                {
                    MovementTileSelected?.Invoke(clickSelection);
                    LogSelection(clickSelection, "click");
                }
                else
                {
                    MovementTileSelectionFailed?.Invoke();
                }

                return;
            }

            if (!TryGetHoveredMovementTile(screenPosition, out MovementTileSelection hoverSelection))
            {
                hasLoggedHoverSelection = false;
                return;
            }

            // Hover is evaluated every relevant frame, but a repeated result is logged
            // only when it changes so an idle pointer cannot flood the Console.
            if (!hasLoggedHoverSelection ||
                hoverSelection.Coordinate != lastLoggedHoverCoordinate ||
                hoverSelection.TileType != lastLoggedHoverTileType)
            {
                LogSelection(hoverSelection, "hover");
                lastLoggedHoverCoordinate = hoverSelection.Coordinate;
                lastLoggedHoverTileType = hoverSelection.TileType;
                hasLoggedHoverSelection = true;
            }
        }

        /// <summary>
        /// Attempts to resolve the movement tile currently under the system pointer.
        /// Intended for hover previews and does not commit a selection.
        /// </summary>
        public bool TryGetHoveredMovementTile(out MovementTileSelection selection)
        {
            selection = default;
            return TryGetCurrentPointerPosition(out Vector2 screenPosition) &&
                   TryGetHoveredMovementTile(screenPosition, out selection);
        }

        /// <summary>
        /// Attempts to resolve a hover tile from a supplied screen-space pointer position.
        /// </summary>
        public bool TryGetHoveredMovementTile(
            Vector2 screenPosition,
            out MovementTileSelection selection)
        {
            return TryResolveScreenPosition(screenPosition, out selection);
        }

        /// <summary>
        /// Attempts to resolve a hover tile from a ray supplied by an input/controller layer.
        /// </summary>
        public bool TryGetHoveredMovementTile(Ray pointerRay, out MovementTileSelection selection)
        {
            return TryResolveRay(pointerRay, out selection);
        }

        /// <summary>
        /// Attempts to produce the selection result that a future player movement system can consume.
        /// </summary>
        public bool TrySelectMovementTile(out MovementTileSelection selection)
        {
            selection = default;
            return TryGetCurrentPointerPosition(out Vector2 screenPosition) &&
                   TrySelectMovementTile(screenPosition, out selection);
        }

        /// <summary>
        /// Attempts to produce a committed selection result from a screen-space position.
        /// </summary>
        public bool TrySelectMovementTile(
            Vector2 screenPosition,
            out MovementTileSelection selection)
        {
            return TryResolveScreenPosition(screenPosition, out selection);
        }

        /// <summary>
        /// Attempts to produce a committed selection result from a preconstructed ray.
        /// </summary>
        public bool TrySelectMovementTile(Ray pointerRay, out MovementTileSelection selection)
        {
            return TryResolveRay(pointerRay, out selection);
        }

        private bool TryResolveScreenPosition(
            Vector2 screenPosition,
            out MovementTileSelection selection)
        {
            selection = default;
            if (!movementSelectionModeActive ||
                !ValidateSetup(true) ||
                !IsFinite(screenPosition))
            {
                return false;
            }

            Ray pointerRay = gameplayCamera.ScreenPointToRay(screenPosition);
            return TryResolveRay(pointerRay, out selection);
        }

        private bool TryResolveRay(Ray pointerRay, out MovementTileSelection selection)
        {
            selection = default;
            if (!movementSelectionModeActive ||
                !ValidateSetup(false) ||
                !IsFinite(pointerRay.origin) ||
                !IsFinite(pointerRay.direction) ||
                pointerRay.direction.sqrMagnitude <= Mathf.Epsilon)
            {
                return false;
            }

            if (!Physics.Raycast(
                    pointerRay,
                    out RaycastHit physicsHit,
                    maximumSelectionDistance,
                    floorSelectionLayerMask.value,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (!tileManager.TryResolveMovementSelectableTile(
                    physicsHit.point,
                    physicsHit.normal,
                    surfaceBoundaryToleranceFraction,
                    out Vector3Int coordinate,
                    out TileManager.TileType tileType))
            {
                return false;
            }

            selection = new MovementTileSelection(
                coordinate,
                tileType,
                tileManager.GridToWorldCenter(coordinate),
                physicsHit.point,
                physicsHit.normal);
            return true;
        }

        private bool ValidateSetup(bool requiresCamera)
        {
            bool isValid = tileManager != null &&
                           (!requiresCamera || gameplayCamera != null) &&
                           floorSelectionLayerMask.value != 0 &&
                           maximumSelectionDistance >= MinimumSelectionDistance &&
                           !float.IsNaN(maximumSelectionDistance) &&
                           !float.IsInfinity(maximumSelectionDistance);
            if (!isValid && !setupErrorLogged)
            {
                Debug.LogError(
                    "PlayerTileSelector requires a TileManager, a gameplay Camera for screen-position selection, " +
                    "a non-empty floor-selection LayerMask, and a finite positive maximum distance.",
                    this);
                setupErrorLogged = true;
            }

            return isValid;
        }

        private static bool TryGetCurrentPointerPosition(out Vector2 screenPosition)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                screenPosition = default;
                return false;
            }

            screenPosition = mouse.position.ReadValue();
            return true;
        }

        private static void LogSelection(MovementTileSelection selection, string interactionName)
        {
            Debug.Log(
                $"{selection.TileType} {selection.Coordinate} at {selection.WorldCenter} ({interactionName})");
        }

        private static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private void OnValidate()
        {
            if (maximumSelectionDistance < MinimumSelectionDistance ||
                float.IsNaN(maximumSelectionDistance) ||
                float.IsInfinity(maximumSelectionDistance))
            {
                maximumSelectionDistance = DefaultSelectionDistance;
            }

            surfaceBoundaryToleranceFraction = Mathf.Clamp(
                surfaceBoundaryToleranceFraction,
                MinimumSurfaceBoundaryToleranceFraction,
                MaximumSurfaceBoundaryToleranceFraction);
            setupErrorLogged = false;
        }
    }
}
