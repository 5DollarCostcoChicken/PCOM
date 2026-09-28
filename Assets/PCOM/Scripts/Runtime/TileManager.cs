using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PCOM
{
    public class TileManager : MonoBehaviour
    {
        /// <summary>
        /// Raised after the runtime tile grid has been cleared and rebuilt.
        /// </summary>
        public event Action GridRebuilt;

        public enum TileType
        {
            Chasm = 0,
            Air = 1,
            Floor = 2,
            Wall = 3,
            Slope = 4,
            Hazard = 5,
            Ladder = 6
        }

        private const float DefaultCellSize = 1f;
        private const float MinimumCellSize = 0.0001f;
        private const float BroadPhaseWorldTolerance = 0.0001f;
        private const int InitialOverlapBufferSize = 16;
        private const int TilesAboveHighestFloor = 10;
        private const int TilesBelowLowestFloor = 2;
        private const string DebugRootName = "Generated Tile Visuals";

        private static readonly Vector3Int[] HorizontalNeighborOffsets =
        {
            Vector3Int.left,
            Vector3Int.right,
            new Vector3Int(0, 0, -1),
            new Vector3Int(0, 0, 1)
        };

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");

        [Header("Grid")]
        [SerializeField] private Vector3 gridOrigin = Vector3.zero;
        [SerializeField, Min(MinimumCellSize)] private float cellSize = DefaultCellSize;

        [Header("Map Bounders")]
        [SerializeField] private bool hideBounderMeshRenderers = true;

        [Header("Grid Visualization")]
        [SerializeField] private bool visualizeGrid = true;
        [Tooltip("Show only floor-family tiles, their complete adjacent neighborhood, preserved headroom, and all wall tiles.")]
        [SerializeField] private bool floorNeighborsOnly = false;
        [SerializeField, Range(0.05f, 1f)] private float visualizationScale = 0.3f;
        [SerializeField] private Color playerPathDebugColor = new Color(1f, 0.1f, 0.65f, 1f);
        private Color chasmColor = new Color(0.28f, 0.1f, 0.1f, 0.65f);
        private Color airColor = new Color(1f, 1f, 1f, 0.25f);
        private Color floorColor = new Color(0.7f, 0.7f, 0.7f, 0.75f);
        private Color wallColor = new Color(0.4f, 0.4f, 0.4f, 0.85f);
        private Color hazardColor = new Color(0.9f, 0.2f, 0.1f, 0.85f);
        private Color slopeColor = new Color(0.05f, 0.65f, 0.8f, 0.8f);
        private Color ladderColor = new Color(0.1f, 0.8f, 0.3f, 0.85f);

        private readonly Dictionary<Vector3Int, TileType> tiles = new Dictionary<Vector3Int, TileType>();
        private readonly Dictionary<Vector3Int, Renderer> tileVisualRenderers =
            new Dictionary<Vector3Int, Renderer>();
        private readonly HashSet<Vector3Int> displayedPlayerPath = new HashSet<Vector3Int>();
        private MaterialPropertyBlock visualizationColorProperties;
        private Collider[] overlapResults = new Collider[InitialOverlapBufferSize];

        /// <summary>
        /// The preliminary grid, keyed by integer world-space tile centers.
        /// </summary>
        public IReadOnlyDictionary<Vector3Int, TileType> Tiles => tiles;

        /// <summary>
        /// World-space center of grid coordinate (0, 0, 0).
        /// </summary>
        public Vector3 GridOrigin
        {
            get => gridOrigin;
            set => gridOrigin = value;
        }

        /// <summary>
        /// Uniform world-space width, height, and depth of every grid cell.
        /// </summary>
        public float CellSize
        {
            get => cellSize;
            set
            {
                if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Cell size must be finite and positive.");
                }

                cellSize = value;
            }
        }

        private void Start()
        {
            TileInitialization();
        }

        /// <summary>
        /// Clears and rebuilds the grid from the currently active bounders.
        /// </summary>
        public void RebuildGrid()
        {
            TileInitialization();
        }

        private void OnValidate()
        {
            if (cellSize < MinimumCellSize || float.IsNaN(cellSize) || float.IsInfinity(cellSize))
            {
                cellSize = DefaultCellSize;
            }
        }

        private void TileInitialization()
        {
            tiles.Clear();

            if (cellSize <= 0f || float.IsNaN(cellSize) || float.IsInfinity(cellSize))
            {
                Debug.LogError("Tile grid initialization requires a finite, positive cell size.", this);
                ClearTileVisuals();
                GridRebuilt?.Invoke();
                return;
            }

            // Bounders can be moved by scene setup immediately before initialization.
            // Synchronize once so the following physics-scene overlap queries see the
            // authored transforms even when automatic transform syncing is disabled.
            Physics.SyncTransforms();

            // Process from lowest to highest priority. A later bounder replaces an
            // existing tile, so overlapping volumes never create duplicate cells.
            MapTaggedBounders("ChasmBounder", TileType.Chasm);
            MapTaggedBounders("AirBounder", TileType.Air);
            MapTaggedBounders("FloorBounder", TileType.Floor);
            MapTaggedBounders("SlopeBounder", TileType.Slope);
            MapTaggedBounders("WallBounder", TileType.Wall);

            CreateFloorBoundaryWalls();
            FillVerticalColumns();

            // Hazard and ladder geometry contributes a horizontal footprint. Each
            // footprint column overrides only the first floor-family tile below it.
            MapTaggedFloorOverrides("HazardBounder", TileType.Hazard);
            MapTaggedFloorOverrides("LadderBounder", TileType.Ladder);

            // Part 4.5: retain floor-family tiles, their complete adjacent
            // neighborhood, and required headroom above neighboring walls and
            // chasms. This trims the generated volume before visualization and
            // before the grid is exposed to later gameplay systems.
            CullTilesOutsideFloorNeighborhood();

            if (visualizeGrid)
            {
                CreateTileVisuals();
            }
            else
            {
                ClearTileVisuals();
            }

            GridRebuilt?.Invoke();
        }

        private void MapTaggedBounders(string tagName, TileType tileType)
        {
            GameObject[] bounders = GameObject.FindGameObjectsWithTag(tagName);
            for (int bounderIndex = 0; bounderIndex < bounders.Length; bounderIndex++)
            {
                MapBounder(bounders[bounderIndex], tileType);
            }
        }

        private void MapBounder(GameObject bounder, TileType tileType)
        {
            HideBounderMeshRenderers(bounder);
            Collider[] colliders = bounder.GetComponentsInChildren<Collider>();
            int enabledColliderCount = 0;
            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            {
                Collider bounderCollider = colliders[colliderIndex];
                if (bounderCollider.enabled && bounderCollider.gameObject.activeInHierarchy)
                {
                    enabledColliderCount++;
                    MapCollider(bounderCollider, tileType);
                }
            }

            if (enabledColliderCount == 0)
            {
                Debug.LogWarning(
                    $"Bounder '{bounder.name}' has no enabled Collider in its active hierarchy and cannot be accurately voxelized. " +
                    "Add an enabled Collider (a thin BoxCollider is recommended for plane-like bounders). The bounder was skipped.",
                    bounder);
            }
        }

        private void MapTaggedFloorOverrides(string tagName, TileType floorSubtype)
        {
            GameObject[] bounders = GameObject.FindGameObjectsWithTag(tagName);
            Dictionary<Vector2Int, List<Vector3Int>> columns = BuildColumnIndex();
            for (int bounderIndex = 0; bounderIndex < bounders.Length; bounderIndex++)
            {
                MapFloorOverrideBounder(bounders[bounderIndex], floorSubtype, columns);
            }
        }

        private void MapFloorOverrideBounder(
            GameObject bounder,
            TileType floorSubtype,
            Dictionary<Vector2Int, List<Vector3Int>> columns)
        {
            HideBounderMeshRenderers(bounder);
            Collider[] colliders = bounder.GetComponentsInChildren<Collider>();
            Dictionary<Vector2Int, int> footprintMinimumY = new Dictionary<Vector2Int, int>();
            int enabledColliderCount = 0;

            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            {
                Collider bounderCollider = colliders[colliderIndex];
                if (!bounderCollider.enabled || !bounderCollider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                enabledColliderCount++;
                CollectColliderFootprint(bounderCollider, footprintMinimumY);
            }

            if (enabledColliderCount == 0)
            {
                Debug.LogWarning(
                    $"Bounder '{bounder.name}' has no enabled Collider in its active hierarchy and cannot be accurately voxelized. " +
                    "Add an enabled Collider. The bounder was skipped.",
                    bounder);
                return;
            }

            foreach (KeyValuePair<Vector2Int, int> footprintColumn in footprintMinimumY)
            {
                if (TryFindHighestFloorAtOrBelow(
                        footprintColumn.Key,
                        footprintColumn.Value,
                        columns,
                        out Vector3Int floorCoordinate))
                {
                    AssignTile(floorCoordinate, floorSubtype);
                }
            }
        }

        private void HideBounderMeshRenderers(GameObject bounder)
        {
            if (!hideBounderMeshRenderers)
            {
                return;
            }

            MeshRenderer[] meshRenderers = bounder.GetComponentsInChildren<MeshRenderer>(true);
            for (int rendererIndex = 0;
                 rendererIndex < meshRenderers.Length;
                 rendererIndex++)
            {
                meshRenderers[rendererIndex].enabled = false;
            }
        }

        private void CollectColliderFootprint(
            Collider bounderCollider,
            Dictionary<Vector2Int, int> footprintMinimumY)
        {
            GetCandidateCoordinateRange(bounderCollider.bounds, out Vector3Int minimum, out Vector3Int maximum);
            PhysicsScene physicsScene = bounderCollider.gameObject.scene.GetPhysicsScene();

            for (int x = minimum.x; x <= maximum.x; x++)
            {
                for (int y = minimum.y; y <= maximum.y; y++)
                {
                    for (int z = minimum.z; z <= maximum.z; z++)
                    {
                        Vector3Int coordinate = new Vector3Int(x, y, z);
                        if (!DoesColliderOverlapCell(physicsScene, bounderCollider, coordinate))
                        {
                            continue;
                        }

                        Vector2Int column = new Vector2Int(x, z);
                        if (!footprintMinimumY.TryGetValue(column, out int currentMinimumY) || y < currentMinimumY)
                        {
                            footprintMinimumY[column] = y;
                        }
                    }
                }
            }
        }

        private bool TryFindHighestFloorAtOrBelow(
            Vector2Int column,
            int floorSearchMaximumY,
            Dictionary<Vector2Int, List<Vector3Int>> columns,
            out Vector3Int floorCoordinate)
        {
            floorCoordinate = default;
            if (!columns.TryGetValue(column, out List<Vector3Int> columnTiles))
            {
                return false;
            }

            int highestFloorY = int.MinValue;
            for (int coordinateIndex = 0; coordinateIndex < columnTiles.Count; coordinateIndex++)
            {
                Vector3Int candidate = columnTiles[coordinateIndex];
                if (candidate.y <= floorSearchMaximumY &&
                    candidate.y > highestFloorY &&
                    IsMovementSelectableTileType(tiles[candidate]))
                {
                    floorCoordinate = candidate;
                    highestFloorY = candidate.y;
                }
            }

            return highestFloorY != int.MinValue;
        }

        private void MapCollider(Collider bounderCollider, TileType tileType)
        {
            GetCandidateCoordinateRange(bounderCollider.bounds, out Vector3Int minimum, out Vector3Int maximum);
            PhysicsScene physicsScene = bounderCollider.gameObject.scene.GetPhysicsScene();

            for (int x = minimum.x; x <= maximum.x; x++)
            {
                for (int y = minimum.y; y <= maximum.y; y++)
                {
                    for (int z = minimum.z; z <= maximum.z; z++)
                    {
                        Vector3Int coordinate = new Vector3Int(x, y, z);
                        if (DoesColliderOverlapCell(physicsScene, bounderCollider, coordinate))
                        {
                            AssignTile(coordinate, tileType);
                        }
                    }
                }
            }
        }

        private void GetCandidateCoordinateRange(Bounds colliderBounds, out Vector3Int minimum, out Vector3Int maximum)
        {
            float queryHalfExtent = GetQueryHalfExtent();
            minimum = new Vector3Int(
                GetMinimumCandidateCoordinate(colliderBounds.min.x, gridOrigin.x, queryHalfExtent),
                GetMinimumCandidateCoordinate(colliderBounds.min.y, gridOrigin.y, queryHalfExtent),
                GetMinimumCandidateCoordinate(colliderBounds.min.z, gridOrigin.z, queryHalfExtent));
            maximum = new Vector3Int(
                GetMaximumCandidateCoordinate(colliderBounds.max.x, gridOrigin.x, queryHalfExtent),
                GetMaximumCandidateCoordinate(colliderBounds.max.y, gridOrigin.y, queryHalfExtent),
                GetMaximumCandidateCoordinate(colliderBounds.max.z, gridOrigin.z, queryHalfExtent));
        }

        private int GetMinimumCandidateCoordinate(float boundsMinimum, float originAxis, float queryHalfExtent)
        {
            // Expanding only the broad phase prevents floating-point rounding from
            // omitting a boundary candidate. The exact overlap query remains full-size.
            return Mathf.CeilToInt(
                (boundsMinimum - originAxis - queryHalfExtent - BroadPhaseWorldTolerance) / cellSize);
        }

        private int GetMaximumCandidateCoordinate(float boundsMaximum, float originAxis, float queryHalfExtent)
        {
            return Mathf.FloorToInt(
                (boundsMaximum - originAxis + queryHalfExtent + BroadPhaseWorldTolerance) / cellSize);
        }

        private bool DoesColliderOverlapCell(
            PhysicsScene physicsScene,
            Collider bounderCollider,
            Vector3Int coordinate)
        {
            Vector3 center = GridToWorldCenter(coordinate);
            Vector3 halfExtents = Vector3.one * GetQueryHalfExtent();

            while (true)
            {
                int overlapCount = physicsScene.OverlapBox(
                    center,
                    halfExtents,
                    overlapResults,
                    Quaternion.identity,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Collide);

                for (int resultIndex = 0; resultIndex < overlapCount; resultIndex++)
                {
                    if (overlapResults[resultIndex] == bounderCollider)
                    {
                        return true;
                    }
                }

                if (overlapCount < overlapResults.Length)
                {
                    return false;
                }

                Array.Resize(ref overlapResults, overlapResults.Length * 2);
            }
        }

        private float GetQueryHalfExtent()
        {
            return cellSize * 0.5f;
        }

        private void AssignTile(Vector3Int coordinate, TileType tileType)
        {
            tiles[coordinate] = tileType;
        }

        private void CreateFloorBoundaryWalls()
        {
            List<Vector3Int> floorCoordinates = new List<Vector3Int>();
            Dictionary<Vector2Int, List<Vector3Int>> columns = BuildColumnIndex();

            foreach (KeyValuePair<Vector3Int, TileType> tile in tiles)
            {
                if (IsMovementSelectableTileType(tile.Value))
                {
                    floorCoordinates.Add(tile.Key);
                }
            }

            List<Vector3Int> boundaryWalls = new List<Vector3Int>();
            HashSet<Vector3Int> uniqueBoundaryWalls = new HashSet<Vector3Int>();
            for (int floorIndex = 0; floorIndex < floorCoordinates.Count; floorIndex++)
            {
                Vector3Int floorCoordinate = floorCoordinates[floorIndex];
                for (int neighborIndex = 0; neighborIndex < HorizontalNeighborOffsets.Length; neighborIndex++)
                {
                    Vector3Int neighborCoordinate = floorCoordinate + HorizontalNeighborOffsets[neighborIndex];
                    if (tiles.TryGetValue(neighborCoordinate, out TileType neighborType) && neighborType != TileType.Air)
                    {
                        continue;
                    }

                    if (HasFloorOrChasmSupportBelow(neighborCoordinate, columns))
                    {
                        continue;
                    }

                    if (uniqueBoundaryWalls.Add(neighborCoordinate))
                    {
                        boundaryWalls.Add(neighborCoordinate);
                    }
                }
            }

            for (int wallIndex = 0; wallIndex < boundaryWalls.Count; wallIndex++)
            {
                AssignTile(boundaryWalls[wallIndex], TileType.Wall);
            }
        }

        private bool HasFloorOrChasmSupportBelow(
            Vector3Int coordinate,
            Dictionary<Vector2Int, List<Vector3Int>> columns)
        {
            Vector2Int column = new Vector2Int(coordinate.x, coordinate.z);
            if (!columns.TryGetValue(column, out List<Vector3Int> columnTiles))
            {
                return false;
            }

            int nearestTileY = int.MinValue;
            TileType nearestTileType = TileType.Air;
            for (int tileIndex = 0; tileIndex < columnTiles.Count; tileIndex++)
            {
                Vector3Int candidate = columnTiles[tileIndex];
                if (candidate.y >= coordinate.y || candidate.y <= nearestTileY)
                {
                    continue;
                }

                TileType candidateType = tiles[candidate];
                if (candidateType == TileType.Air)
                {
                    continue;
                }

                nearestTileY = candidate.y;
                nearestTileType = candidateType;
            }

            return nearestTileY != int.MinValue &&
                   (IsMovementSelectableTileType(nearestTileType) || nearestTileType == TileType.Chasm);
        }

        private void FillVerticalColumns()
        {
            if (!TryGetFloorLimits(out int lowestFloorY, out int highestFloorY))
            {
                Debug.LogWarning("Vertical grid filling requires at least one floor tile and was skipped.", this);
                return;
            }

            int minimumY = lowestFloorY - TilesBelowLowestFloor;
            int maximumY = highestFloorY + TilesAboveHighestFloor;
            Dictionary<Vector2Int, List<Vector3Int>> columns = BuildColumnIndex();
            foreach (KeyValuePair<Vector2Int, List<Vector3Int>> column in columns)
            {
                FillVerticalColumn(column.Key, column.Value, minimumY, maximumY);
            }

            // Sources outside the playable height range still participate in
            // propagation before their authored cells are clipped from the result.
            RemoveTilesOutsideVerticalLimits(minimumY, maximumY);
        }

        private bool TryGetFloorLimits(out int lowestFloorY, out int highestFloorY)
        {
            lowestFloorY = int.MaxValue;
            highestFloorY = int.MinValue;

            foreach (KeyValuePair<Vector3Int, TileType> tile in tiles)
            {
                if (!IsMovementSelectableTileType(tile.Value))
                {
                    continue;
                }

                lowestFloorY = Mathf.Min(lowestFloorY, tile.Key.y);
                highestFloorY = Mathf.Max(highestFloorY, tile.Key.y);
            }

            return lowestFloorY != int.MaxValue;
        }

        private void RemoveTilesOutsideVerticalLimits(int minimumY, int maximumY)
        {
            List<Vector3Int> coordinatesToRemove = new List<Vector3Int>();
            foreach (KeyValuePair<Vector3Int, TileType> tile in tiles)
            {
                if (tile.Key.y < minimumY || tile.Key.y > maximumY)
                {
                    coordinatesToRemove.Add(tile.Key);
                }
            }

            for (int coordinateIndex = 0; coordinateIndex < coordinatesToRemove.Count; coordinateIndex++)
            {
                tiles.Remove(coordinatesToRemove[coordinateIndex]);
            }
        }

        private void FillVerticalColumn(
            Vector2Int column,
            List<Vector3Int> existingCoordinates,
            int minimumY,
            int maximumY)
        {
            int downwardWallCeiling = int.MinValue;

            for (int coordinateIndex = 0; coordinateIndex < existingCoordinates.Count; coordinateIndex++)
            {
                Vector3Int coordinate = existingCoordinates[coordinateIndex];
                TileType tileType = tiles[coordinate];
                if (IsMovementSelectableTileType(tileType) &&
                    !HasNonWallTileBelow(coordinate.y, existingCoordinates))
                {
                    downwardWallCeiling = Mathf.Max(downwardWallCeiling, coordinate.y);
                }
            }

            for (int y = minimumY; y <= maximumY; y++)
            {
                Vector3Int coordinate = new Vector3Int(column.x, y, column.y);
                if (tiles.ContainsKey(coordinate))
                {
                    continue;
                }

                if (CanWallReachCoordinate(y, existingCoordinates))
                {
                    AssignTile(coordinate, TileType.Wall);
                }
                else if (CanChasmReachCoordinate(y, existingCoordinates))
                {
                    AssignTile(coordinate, TileType.Chasm);
                }
                else if (downwardWallCeiling != int.MinValue && y < downwardWallCeiling)
                {
                    AssignTile(coordinate, TileType.Wall);
                }
                else
                {
                    AssignTile(coordinate, TileType.Air);
                }
            }
        }

        private bool CanWallReachCoordinate(int targetY, List<Vector3Int> existingCoordinates)
        {
            for (int sourceIndex = 0; sourceIndex < existingCoordinates.Count; sourceIndex++)
            {
                Vector3Int wallSource = existingCoordinates[sourceIndex];
                if (tiles[wallSource] != TileType.Wall || targetY <= wallSource.y)
                {
                    continue;
                }

                if (!HasFloorBetween(wallSource.y, targetY, existingCoordinates))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasFloorBetween(
            int lowerExclusiveY,
            int upperExclusiveY,
            List<Vector3Int> existingCoordinates)
        {
            for (int coordinateIndex = 0; coordinateIndex < existingCoordinates.Count; coordinateIndex++)
            {
                Vector3Int coordinate = existingCoordinates[coordinateIndex];
                if (coordinate.y > lowerExclusiveY &&
                    coordinate.y < upperExclusiveY &&
                    IsMovementSelectableTileType(tiles[coordinate]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanChasmReachCoordinate(int targetY, List<Vector3Int> existingCoordinates)
        {
            for (int sourceIndex = 0; sourceIndex < existingCoordinates.Count; sourceIndex++)
            {
                Vector3Int chasmSource = existingCoordinates[sourceIndex];
                if (tiles[chasmSource] != TileType.Chasm)
                {
                    continue;
                }

                // Downward chasm propagation remains unbounded within the grid limits.
                if (targetY < chasmSource.y)
                {
                    return true;
                }

                if (targetY > chasmSource.y &&
                    !HasFloorOrWallBetween(chasmSource.y, targetY, existingCoordinates))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasFloorOrWallBetween(
            int lowerExclusiveY,
            int upperExclusiveY,
            List<Vector3Int> existingCoordinates)
        {
            for (int coordinateIndex = 0; coordinateIndex < existingCoordinates.Count; coordinateIndex++)
            {
                Vector3Int coordinate = existingCoordinates[coordinateIndex];
                if (coordinate.y <= lowerExclusiveY || coordinate.y >= upperExclusiveY)
                {
                    continue;
                }

                TileType tileType = tiles[coordinate];
                if (IsMovementSelectableTileType(tileType) || tileType == TileType.Wall)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasNonWallTileBelow(int y, List<Vector3Int> existingCoordinates)
        {
            for (int coordinateIndex = 0; coordinateIndex < existingCoordinates.Count; coordinateIndex++)
            {
                Vector3Int coordinate = existingCoordinates[coordinateIndex];
                if (coordinate.y < y && tiles[coordinate] != TileType.Wall)
                {
                    return true;
                }
            }

            return false;
        }

        private Dictionary<Vector2Int, List<Vector3Int>> BuildColumnIndex()
        {
            Dictionary<Vector2Int, List<Vector3Int>> columns =
                new Dictionary<Vector2Int, List<Vector3Int>>();

            foreach (KeyValuePair<Vector3Int, TileType> tile in tiles)
            {
                Vector2Int column = new Vector2Int(tile.Key.x, tile.Key.z);
                if (!columns.TryGetValue(column, out List<Vector3Int> columnTiles))
                {
                    columnTiles = new List<Vector3Int>();
                    columns.Add(column, columnTiles);
                }

                columnTiles.Add(tile.Key);
            }

            return columns;
        }

        /// <summary>
        /// Returns whether a tile type represents a surface that can be selected for movement.
        /// </summary>
        public static bool IsMovementSelectableTileType(TileType tileType)
        {
            return tileType == TileType.Floor ||
                   tileType == TileType.Slope ||
                   tileType == TileType.Hazard ||
                   tileType == TileType.Ladder;
        }

        private void CullTilesOutsideFloorNeighborhood()
        {
            HashSet<Vector3Int> retainedCoordinates = BuildFloorNeighborhood();
            List<Vector3Int> coordinatesToRemove = new List<Vector3Int>();

            foreach (KeyValuePair<Vector3Int, TileType> tile in tiles)
            {
                if (!retainedCoordinates.Contains(tile.Key))
                {
                    coordinatesToRemove.Add(tile.Key);
                }
            }

            for (int coordinateIndex = 0; coordinateIndex < coordinatesToRemove.Count; coordinateIndex++)
            {
                tiles.Remove(coordinatesToRemove[coordinateIndex]);
            }
        }

        /// <summary>
        /// Converts a grid coordinate to the center of its world-axis-aligned cell.
        /// </summary>
        public Vector3 GridToWorldCenter(Vector3Int coordinate)
        {
            return gridOrigin + ((Vector3)coordinate * cellSize);
        }

        /// <summary>
        /// Returns the grid coordinate whose center is nearest to a world position.
        /// </summary>
        public Vector3Int WorldToGridCoordinate(Vector3 worldPosition)
        {
            Vector3 relativePosition = (worldPosition - gridOrigin) / cellSize;
            return new Vector3Int(
                Mathf.RoundToInt(relativePosition.x),
                Mathf.RoundToInt(relativePosition.y),
                Mathf.RoundToInt(relativePosition.z));
        }

        /// <summary>
        /// Attempts to retrieve the authored runtime tile at a grid coordinate.
        /// </summary>
        public bool TryGetTile(Vector3Int coordinate, out TileType tileType)
        {
            return tiles.TryGetValue(coordinate, out tileType);
        }

        /// <summary>
        /// Resolves a surface hit to a nearby movement-selectable tile. The tolerance is
        /// expressed as a fraction of cell size so grid scaling does not change behavior.
        /// </summary>
        public bool TryResolveMovementSelectableTile(
            Vector3 surfaceHitPoint,
            Vector3 surfaceNormal,
            float surfaceBoundaryToleranceFraction,
            out Vector3Int coordinate,
            out TileType tileType)
        {
            coordinate = default;
            tileType = default;

            if (!IsFinite(surfaceHitPoint) ||
                !IsFinite(surfaceNormal) ||
                surfaceNormal.sqrMagnitude <= Mathf.Epsilon ||
                surfaceBoundaryToleranceFraction < 0f ||
                float.IsNaN(surfaceBoundaryToleranceFraction) ||
                float.IsInfinity(surfaceBoundaryToleranceFraction))
            {
                return false;
            }

            float worldTolerance = cellSize * surfaceBoundaryToleranceFraction;
            // The tolerance only resolves horizontal grid-edge ambiguity. Pushing a
            // sloped hit along its complete normal also changes its elevation, which
            // can incorrectly resolve an overlapping lower slope voxel. Preserve the
            // collider's hit height and move only horizontally into the touched cell.
            Vector3 horizontalNormal = new Vector3(surfaceNormal.x, 0f, surfaceNormal.z);
            Vector3 inwardSample = surfaceHitPoint;
            if (horizontalNormal.sqrMagnitude > Mathf.Epsilon)
            {
                inwardSample -= horizontalNormal.normalized * worldTolerance;
            }
            Vector3Int initialCoordinate = WorldToGridCoordinate(inwardSample);
            float maximumBoundsDistanceSquared = worldTolerance * worldTolerance;
            float bestBoundsDistanceSquared = float.PositiveInfinity;
            float bestCenterDistanceSquared = float.PositiveInfinity;
            bool foundCandidate = false;

            for (int xOffset = -1; xOffset <= 1; xOffset++)
            {
                for (int yOffset = -1; yOffset <= 1; yOffset++)
                {
                    for (int zOffset = -1; zOffset <= 1; zOffset++)
                    {
                        Vector3Int candidateCoordinate = initialCoordinate +
                            new Vector3Int(xOffset, yOffset, zOffset);
                        if (!tiles.TryGetValue(candidateCoordinate, out TileType candidateType) ||
                            !IsMovementSelectableTileType(candidateType))
                        {
                            continue;
                        }

                        Bounds candidateBounds = GetCellWorldBounds(candidateCoordinate);
                        float boundsDistanceSquared = candidateBounds.SqrDistance(inwardSample);
                        if (boundsDistanceSquared > maximumBoundsDistanceSquared)
                        {
                            continue;
                        }

                        float centerDistanceSquared =
                            (candidateBounds.center - inwardSample).sqrMagnitude;
                        if (foundCandidate &&
                            !IsBetterResolutionCandidate(
                                boundsDistanceSquared,
                                centerDistanceSquared,
                                candidateCoordinate,
                                bestBoundsDistanceSquared,
                                bestCenterDistanceSquared,
                                coordinate))
                        {
                            continue;
                        }

                        coordinate = candidateCoordinate;
                        tileType = candidateType;
                        bestBoundsDistanceSquared = boundsDistanceSquared;
                        bestCenterDistanceSquared = centerDistanceSquared;
                        foundCandidate = true;
                    }
                }
            }

            if (foundCandidate && tileType == TileType.Slope)
            {
                PromoteStackedSlopeSelection(ref coordinate, ref tileType);
            }

            return foundCandidate;
        }

        private void PromoteStackedSlopeSelection(
            ref Vector3Int coordinate,
            ref TileType tileType)
        {
            // A lower voxel in a stacked slope column has no usable headroom. The
            // visible and traversable surface is represented by the slope tile above
            // it, so selection deliberately promotes through the full stack.
            while (tiles.TryGetValue(coordinate + Vector3Int.up, out TileType upperType) &&
                   upperType == TileType.Slope)
            {
                coordinate += Vector3Int.up;
                tileType = upperType;
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsBetterResolutionCandidate(
            float boundsDistanceSquared,
            float centerDistanceSquared,
            Vector3Int candidateCoordinate,
            float bestBoundsDistanceSquared,
            float bestCenterDistanceSquared,
            Vector3Int bestCoordinate)
        {
            if (!Mathf.Approximately(boundsDistanceSquared, bestBoundsDistanceSquared))
            {
                return boundsDistanceSquared < bestBoundsDistanceSquared;
            }

            if (!Mathf.Approximately(centerDistanceSquared, bestCenterDistanceSquared))
            {
                return centerDistanceSquared < bestCenterDistanceSquared;
            }

            if (candidateCoordinate.y != bestCoordinate.y)
            {
                // When a thin slope straddles a voxel boundary, both cells have the
                // same hit distance. The upper representative has the available
                // headroom and is the stable selection for the visible surface.
                return candidateCoordinate.y > bestCoordinate.y;
            }

            if (candidateCoordinate.x != bestCoordinate.x)
            {
                return candidateCoordinate.x < bestCoordinate.x;
            }

            return candidateCoordinate.z < bestCoordinate.z;
        }

        /// <summary>
        /// Returns the full world-space bounds of a grid cell.
        /// </summary>
        public Bounds GetCellWorldBounds(Vector3Int coordinate)
        {
            return new Bounds(GridToWorldCenter(coordinate), Vector3.one * cellSize);
        }

        /// <summary>
        /// Recolors available debug-grid renderers for the authoritative executed path.
        /// Movement remains independent of whether those renderers exist.
        /// </summary>
        public void ShowPlayerDebugPath(IReadOnlyList<Vector3Int> coordinates)
        {
            ClearPlayerDebugPath();
            if (coordinates == null)
            {
                return;
            }

            for (int coordinateIndex = 0; coordinateIndex < coordinates.Count; coordinateIndex++)
            {
                Vector3Int coordinate = coordinates[coordinateIndex];
                if (tileVisualRenderers.TryGetValue(coordinate, out Renderer tileRenderer) &&
                    tileRenderer != null)
                {
                    ApplyVisualizationColor(tileRenderer, playerPathDebugColor);
                    displayedPlayerPath.Add(coordinate);
                }
            }
        }

        public void ClearPlayerDebugPath()
        {
            foreach (Vector3Int coordinate in displayedPlayerPath)
            {
                if (tileVisualRenderers.TryGetValue(coordinate, out Renderer tileRenderer) &&
                    tileRenderer != null &&
                    tiles.TryGetValue(coordinate, out TileType tileType))
                {
                    ApplyVisualizationColor(tileRenderer, GetTileColor(tileType));
                }
            }

            displayedPlayerPath.Clear();
        }

        private void CreateTileVisuals()
        {
            Transform debugRoot = GetOrCreateVisualizationRoot();
            ClearVisualizationChildren(debugRoot);
            HashSet<Vector3Int> floorNeighborhood = floorNeighborsOnly ? BuildFloorNeighborhood() : null;

            foreach (KeyValuePair<Vector3Int, TileType> tile in tiles)
            {
                if (floorNeighborsOnly &&
                    tile.Value != TileType.Wall &&
                    !floorNeighborhood.Contains(tile.Key))
                {
                    continue;
                }

                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = $"{tile.Value} {tile.Key}";
                visual.transform.SetParent(debugRoot.transform, true);
                visual.transform.position = GridToWorldCenter(tile.Key);
                visual.transform.localScale = Vector3.one * (cellSize * visualizationScale);

                Collider visualCollider = visual.GetComponent<Collider>();
                if (visualCollider != null)
                {
                    visualCollider.enabled = false;
                    if (Application.isPlaying)
                    {
                        Destroy(visualCollider);
                    }
                    else
                    {
                        DestroyImmediate(visualCollider);
                    }
                }

                Color tileColor = GetTileColor(tile.Value);
                Renderer tileRenderer = visual.GetComponent<Renderer>();
                ApplyVisualizationColor(tileRenderer, tileColor);
                tileVisualRenderers[tile.Key] = tileRenderer;
            }
        }

        private HashSet<Vector3Int> BuildFloorNeighborhood()
        {
            HashSet<Vector3Int> floorNeighborhood = new HashSet<Vector3Int>();
            foreach (KeyValuePair<Vector3Int, TileType> tile in tiles)
            {
                if (!IsMovementSelectableTileType(tile.Value))
                {
                    continue;
                }

                for (int xOffset = -1; xOffset <= 1; xOffset++)
                {
                    for (int yOffset = -1; yOffset <= 1; yOffset++)
                    {
                        for (int zOffset = -1; zOffset <= 1; zOffset++)
                        {
                            floorNeighborhood.Add(
                                tile.Key + new Vector3Int(xOffset, yOffset, zOffset));
                        }
                    }
                }

                for (int neighborIndex = 0; neighborIndex < HorizontalNeighborOffsets.Length; neighborIndex++)
                {
                    Vector3Int horizontalNeighbor = tile.Key + HorizontalNeighborOffsets[neighborIndex];
                    if (!tiles.TryGetValue(horizontalNeighbor, out TileType neighborType) ||
                        (neighborType != TileType.Wall && neighborType != TileType.Chasm))
                    {
                        continue;
                    }

                    floorNeighborhood.Add(horizontalNeighbor + Vector3Int.up);
                    floorNeighborhood.Add(horizontalNeighbor + (Vector3Int.up * 2));
                }
            }

            return floorNeighborhood;
        }

        private Transform GetOrCreateVisualizationRoot()
        {
            Transform debugRoot = transform.Find(DebugRootName);
            if (debugRoot != null)
            {
                debugRoot.gameObject.SetActive(true);
                return debugRoot;
            }

            GameObject debugRootObject = new GameObject(DebugRootName);
            debugRootObject.transform.SetParent(transform, true);
            return debugRootObject.transform;
        }

        private void ClearTileVisuals()
        {
            displayedPlayerPath.Clear();
            tileVisualRenderers.Clear();
            Transform debugRoot = transform.Find(DebugRootName);
            if (debugRoot == null)
            {
                return;
            }

            ClearVisualizationChildren(debugRoot);
            debugRoot.gameObject.SetActive(false);
        }

        private void ClearVisualizationChildren(Transform debugRoot)
        {
            displayedPlayerPath.Clear();
            tileVisualRenderers.Clear();
            for (int childIndex = debugRoot.childCount - 1; childIndex >= 0; childIndex--)
            {
                GameObject child = debugRoot.GetChild(childIndex).gameObject;
                child.SetActive(false);
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        private void ApplyVisualizationColor(Renderer tileRenderer, Color color)
        {
            if (tileRenderer == null)
            {
                return;
            }

            visualizationColorProperties ??= new MaterialPropertyBlock();
            visualizationColorProperties.Clear();
            visualizationColorProperties.SetColor(ColorProperty, color);
            visualizationColorProperties.SetColor(BaseColorProperty, color);
            tileRenderer.SetPropertyBlock(visualizationColorProperties);
        }

        private Color GetTileColor(TileType tileType)
        {
            switch (tileType)
            {
                case TileType.Chasm:
                    return chasmColor;
                case TileType.Air:
                    return airColor;
                case TileType.Floor:
                    return floorColor;
                case TileType.Wall:
                    return wallColor;
                case TileType.Slope:
                    return slopeColor;
                case TileType.Hazard:
                    return hazardColor;
                case TileType.Ladder:
                    return ladderColor;
                default:
                    return Color.magenta;
            }
        }
    }
}
