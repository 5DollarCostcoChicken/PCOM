using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace PCOM
{
    /// <summary>
    /// Presentation-only XCOM-style movement visualization. It consumes UnitMovement's
    /// path and executed-route authority; it never decides whether movement is legal.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MovementVisualizationController : MonoBehaviour
    {
        private const int IgnoreRaycastLayer = 2;
        private const float SurfaceHeightComparisonTolerance = 0.0001f;
        private static readonly int BaseMapStProperty = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int MainTextureStProperty = Shader.PropertyToID("_MainTex_ST");

        [Header("Required References")]
        [Tooltip("Required. The authoritative grid used to build surface-aligned outlines, boundaries, and cover checks.")]
        [SerializeField] private TileManager tileManager;
        [Tooltip("Required. Supplies movement-mode state and the currently hovered selectable tile.")]
        [SerializeField] private PlayerTileSelector playerTileSelector;
        [Tooltip("Required. The currently selected player unit whose movement rules, AP, and executed path are previewed.")]
        [SerializeField] private UnitMovement selectedUnit;
        [Tooltip("Optional. Reserved for camera-dependent movement presentation. Cover icons face their associated wall rather than this camera.")]
        [SerializeField] private Camera gameplayCamera;
        [Tooltip("Optional. Parent for generated visual meshes and pooled cover icons. A child root is created automatically when left empty.")]
        [SerializeField] private Transform visualizationRoot;

        [Header("Materials and Icons")]
        [Tooltip("Optional. Shared material for AP-band boundaries. Falls back to the hover, route, or boundary material assigned elsewhere.")]
        [SerializeField] private Material boundaryMaterial;
        [Tooltip("Optional. Shared material for the hovered destination outline. Falls back to another assigned line material.")]
        [SerializeField] private Material hoveredTileOutlineMaterial;
        [Tooltip("Optional. Shared material for the executed route ribbon. Falls back to another assigned line material.")]
        [SerializeField] private Material routeMaterial;
        [Tooltip("Optional. Shared material for cover shield sprites. Unity's sprite default is used when empty.")]
        [SerializeField] private Material coverIconMaterial;
        [Tooltip("Optional. Sprite displayed beside one-cell-tall adjacent wall cover. Missing sprites hide only half-cover icons.")]
        [SerializeField] private Sprite halfCoverShieldSprite;
        [Tooltip("Optional. Sprite displayed beside adjacent wall columns two or more cells tall. Missing sprites hide only full-cover icons.")]
        [SerializeField] private Sprite fullCoverShieldSprite;
        [Tooltip("Optional. Pooled prefab for cover icons. It should contain a SpriteRenderer; one is added at runtime if absent.")]
        [SerializeField] private GameObject coverIconPrefab;

        [Header("Colors")]
        [Tooltip("Optional presentation setting. Vertex color used by routes and boundaries costing one action point.")]
        [SerializeField] private Color oneActionPointColor = new Color(0.1f, 0.8f, 1f, 1f);
        [Tooltip("Optional presentation setting. Vertex color used by routes and boundaries costing two action points.")]
        [SerializeField] private Color twoActionPointColor = new Color(1f, 0.78f, 0.12f, 1f);
        [Tooltip("Optional presentation setting. Vertex color used by routes and boundaries costing three action points.")]
        [SerializeField] private Color threeActionPointColor = new Color(1f, 0.4f, 0.08f, 1f);
        [Tooltip("Optional presentation setting. Vertex color used only on route sections whose authoritative crossed tiles include a Hazard.")]
        [SerializeField] private Color hazardRouteColor = Color.red;
        [Tooltip("Optional presentation setting. Outline color for a walkable destination that has no legal route.")]
        [SerializeField] private Color unreachableDestinationColor = new Color(0.45f, 0.45f, 0.45f, 1f);

        [Header("Movement Boundaries")]
        [Tooltip("Optional presentation setting. World-space width of the procedural AP-band boundary ribbons.")]
        [SerializeField, Min(0.001f)] private float boundaryLineWidth = 0.045f;
        [Tooltip("Optional presentation setting. Signed height relative to each tile surface. Positive values prevent AP-boundary z-fighting; negative values place boundaries below the surface.")]
        [SerializeField] private float boundarySurfaceOffset = 0.025f;
        [Tooltip("Optional presentation setting. Extends adjoining edge ribbons slightly to make boundary corners read as bevelled joins.")]
        [SerializeField] private bool bevelBoundaryCorners = true;
        [Tooltip("Optional presentation setting. Extra world-space overlap added at each boundary-edge endpoint when bevelled joins are enabled.")]
        [SerializeField, Min(0f)] private float boundaryEndpointOverlap = 0.02f;
        [Tooltip("Optional presentation setting. Maximum AP bands shown by this component. Clamped to one through three and the unit's usable AP.")]
        [SerializeField, Range(1, 3)] private int maximumDisplayedActionPointBands = 3;

        [Header("Hovered Tile Outline")]
        [Tooltip("Optional presentation setting. World-space width of the stronger outline around the currently hovered destination.")]
        [SerializeField, Min(0.001f)] private float outlineWidth = 0.085f;
        [Tooltip("Optional presentation setting. Signed height relative to the destination surface. Positive values prevent z-fighting; negative values place the hover outline below the surface.")]
        [SerializeField] private float outlineSurfaceOffset = 0.045f;
        [Tooltip("Optional presentation setting. World-space inset (negative) or expansion (positive) applied to the hovered tile outline.")]
        [SerializeField] private float outlineHorizontalExpansion = 0.025f;
        [Tooltip("Optional presentation setting. Pulse frequency in cycles per second. Set to zero to disable pulsing.")]
        [SerializeField, Min(0f)] private float outlinePulseSpeed;
        [Tooltip("Optional presentation setting. Additional vertical pulse amplitude in world units. Set to zero to disable pulsing.")]
        [SerializeField, Min(0f)] private float outlinePulseMagnitude;

        [Header("Route")]
        [Tooltip("Optional presentation setting. World-space width of the procedural executed-route ribbon.")]
        [SerializeField, Min(0.001f)] private float routeWidth = 0.08f;
        [Tooltip("Optional presentation setting. Signed height relative to the rendered movement path. Use a negative value to place every route type below the unit root, including jumps, drops, clambers, and ladders.")]
        [SerializeField] private float routeSurfaceOffset = 0.035f;
        [Tooltip("Optional presentation setting. Maximum world-space distance between route samples; lower values make curved jumps, slopes, and ladders smoother.")]
        [SerializeField, Min(0.02f)] private float routeSamplingDistance = 0.15f;
        [Tooltip("Optional presentation setting. Minimum samples per authoritative route section. This improves visual curve resolution without changing movement geometry.")]
        [SerializeField, Min(1)] private int routeCornerSmoothingResolution = 4;
        [Tooltip("Optional presentation setting. World-space distance represented by one route UV repeat. Requires a route material that reads mesh UVs.")]
        [SerializeField, Min(0.01f)] private float routeTextureTiling = 1f;
        [Tooltip("Optional presentation setting. Horizontal route-texture offset per second. Requires the route shader to expose _BaseMap_ST or _MainTex_ST.")]
        [SerializeField] private float routeTextureScrollSpeed;

        [Header("Slope Height Correction")]
        [Tooltip("Optional presentation setting. Signed height correction in TileManager.CellSize units for slope visualization. It blends continuously from authored slope membership, keeping upper joins flat and lower exits visibly sloped into adjoining terrain.")]
        [SerializeField] private float slopeVisualizationHeightOffsetInCells = 0.5f;

        [Header("Cover Icons")]
        [Tooltip("Optional presentation setting. Uniform world-space scale applied to pooled cover shield icons.")]
        [SerializeField, Min(0.01f)] private float coverIconWorldSize = 0.45f;
        [Tooltip("Optional presentation setting. World-space height above the destination surface where cover shield icons are placed.")]
        [SerializeField] private float coverIconHeight = 0.45f;
        [Tooltip("Optional presentation setting. Signed fraction of a cell relative to the covered edge. Positive values move inward, zero places the icon at the edge, and negative values move outward.")]
        [SerializeField, Range(-0.5f, 0.5f)] private float coverIconEdgeOffset = 0.1f;
        [Tooltip("Optional presentation setting. Rotates each cover shield to face its associated adjacent wall. Disable only for a custom prefab that supplies its own orientation.")]
        [FormerlySerializedAs("coverIconsFaceCamera")]
        [SerializeField] private bool coverIconsFaceCoverDirection = true;
        [Tooltip("Optional presentation setting. Fractional scale pulse amplitude for visible cover icons. Set to zero to disable pulsing.")]
        [SerializeField, Min(0f)] private float coverIconPulseMagnitude;
        [Tooltip("Optional presentation setting. Cover-icon pulse frequency in cycles per second. Set to zero to disable pulsing.")]
        [SerializeField, Min(0f)] private float coverIconPulseSpeed;
        [Tooltip("Optional presentation setting. Requests ZTest Always and high sorting order for cover icons. The assigned icon shader must expose _ZTest for depth override.")]
        [SerializeField] private bool coverIconsRemainVisibleWhenOccluded;

        private readonly List<MovementBoundaryEdge> boundaryEdges = new List<MovementBoundaryEdge>();
        private readonly List<DestinationCover> coverResults = new List<DestinationCover>();
        private readonly List<CoverIconInstance> coverIconPool = new List<CoverIconInstance>();
        private readonly List<Vector3> meshVertices = new List<Vector3>();
        private readonly List<Color> meshColors = new List<Color>();
        private readonly List<Vector2> meshUvs = new List<Vector2>();
        private readonly List<int> meshTriangles = new List<int>();
        private MeshDisplay boundaryDisplay;
        private MeshDisplay outlineDisplay;
        private MeshDisplay routeDisplay;
        private MovementReachabilityResult reachability;
        private Vector3Int observedOrigin;
        private int observedActionPoints = int.MinValue;
        private float observedMovementAmount = float.NaN;
        private bool reachabilityDirty = true;
        private bool hasHoveredCoordinate;
        private Vector3Int hoveredCoordinate;
        private MovementPreviewResult activePreview;
        private bool loggedSetupWarning;
        private MaterialPropertyBlock routeProperties;
        private MaterialPropertyBlock coverIconProperties;

        private void Awake()
        {
            EnsureDisplays();
        }

        private void OnEnable()
        {
            if (tileManager != null)
            {
                tileManager.GridRebuilt += HandleGridRebuilt;
            }

            if (selectedUnit != null)
            {
                selectedUnit.MovementStarted += HandleMovementStarted;
                selectedUnit.MovementCompleted += HandleMovementFinished;
                selectedUnit.MovementInterrupted += HandleMovementFinished;
            }

            reachabilityDirty = true;
        }

        private void OnDisable()
        {
            if (tileManager != null)
            {
                tileManager.GridRebuilt -= HandleGridRebuilt;
            }

            if (selectedUnit != null)
            {
                selectedUnit.MovementStarted -= HandleMovementStarted;
                selectedUnit.MovementCompleted -= HandleMovementFinished;
                selectedUnit.MovementInterrupted -= HandleMovementFinished;
            }

            ClearAll();
        }

        private void OnDestroy()
        {
            boundaryDisplay?.Dispose();
            outlineDisplay?.Dispose();
            routeDisplay?.Dispose();
        }

        private void Update()
        {
            if (!CanVisualizeMovement())
            {
                ClearAll();
                return;
            }

            EnsureDisplays();
            UpdateReachabilityIfNeeded();
            UpdateHoverPreview();
            UpdateAnimatedPresentation();
        }

        private bool CanVisualizeMovement()
        {
            bool isValid = tileManager != null && playerTileSelector != null && selectedUnit != null &&
                           playerTileSelector.IsMovementSelectionModeActive &&
                           selectedUnit.RequiresPlayerTileSelection && selectedUnit.IsInitialized &&
                           !selectedUnit.IsMoving && selectedUnit.CurrentActionPoints > 0;
            if (!isValid && !loggedSetupWarning &&
                (tileManager == null || playerTileSelector == null || selectedUnit == null))
            {
                Debug.LogWarning(
                    "MovementVisualizationController requires TileManager, PlayerTileSelector, and UnitMovement references. " +
                    "Missing visual assets only hide their matching display.", this);
                loggedSetupWarning = true;
            }

            return isValid;
        }

        private void UpdateReachabilityIfNeeded()
        {
            if (selectedUnit.CurrentCoordinate != observedOrigin ||
                selectedUnit.CurrentActionPoints != observedActionPoints ||
                !Mathf.Approximately(selectedUnit.MovementAmount, observedMovementAmount))
            {
                reachabilityDirty = true;
            }

            if (!reachabilityDirty)
            {
                return;
            }

            observedOrigin = selectedUnit.CurrentCoordinate;
            observedActionPoints = selectedUnit.CurrentActionPoints;
            observedMovementAmount = selectedUnit.MovementAmount;
            reachability = selectedUnit.CalculateReachabilityPreview(maximumDisplayedActionPointBands);
            MovementBoundaryGenerator.CollectEdges(reachability, boundaryEdges, selectedUnit);
            RebuildBoundaryMesh();
            reachabilityDirty = false;
            hasHoveredCoordinate = false;
            ClearHoverPresentation();
        }

        private void UpdateHoverPreview()
        {
            if (!playerTileSelector.TryGetHoveredMovementTile(out MovementTileSelection selection))
            {
                hasHoveredCoordinate = false;
                ClearHoverPresentation();
                return;
            }

            if (hasHoveredCoordinate && selection.Coordinate == hoveredCoordinate)
            {
                return;
            }

            hasHoveredCoordinate = true;
            hoveredCoordinate = selection.Coordinate;
            activePreview = selectedUnit.CalculateMovementPreview(selection.Coordinate);
            if (!activePreview.IsReachable)
            {
                BuildHoveredOutline(selection.Coordinate, unreachableDestinationColor);
                ClearRoute();
                ClearCoverIcons();
                return;
            }

            Color movementColor = GetActionPointColor(Mathf.Max(1, activePreview.RequiredActionPoints));
            Color outlineColor = activePreview.HasHazardSections
                ? hazardRouteColor
                : movementColor;
            BuildHoveredOutline(selection.Coordinate, outlineColor);
            BuildRoute(activePreview, movementColor);
            BuildCoverIcons(selection.Coordinate, outlineColor);
        }

        private void RebuildBoundaryMesh()
        {
            BeginMesh();
            float halfCell = tileManager.CellSize * 0.5f;
            for (int edgeIndex = 0; edgeIndex < boundaryEdges.Count; edgeIndex++)
            {
                MovementBoundaryEdge edge = boundaryEdges[edgeIndex];
                Vector3 direction = edge.OutwardDirection;
                Vector3 tangent = new Vector3(-direction.z, 0f, direction.x);
                Vector3 center = tileManager.GridToWorldCenter(edge.TileCoordinate) +
                                 (direction * halfCell);
                float endpointOverlap = bevelBoundaryCorners ? boundaryEndpointOverlap : 0f;
                Vector3 startAnchor = center - (tangent * halfCell);
                Vector3 endAnchor = center + (tangent * halfCell);
                Vector3 startSample = center - (tangent * (halfCell + endpointOverlap));
                Vector3 endSample = center + (tangent * (halfCell + endpointOverlap));
                Vector3 start = ResolveVisualSurface(
                    edge.TileCoordinate,
                    startAnchor,
                    startSample);
                Vector3 end = ResolveVisualSurface(
                    edge.TileCoordinate,
                    endAnchor,
                    endSample);
                start.y += boundarySurfaceOffset;
                end.y += boundarySurfaceOffset;
                AppendRibbon(start, end, boundaryLineWidth, GetActionPointColor(edge.ActionPointBand));
            }

            ApplyMesh(boundaryDisplay, boundaryMaterial, "boundary material");
        }

        private void BuildHoveredOutline(Vector3Int coordinate, Color color)
        {
            BeginMesh();
            float halfExtent = (tileManager.CellSize * 0.5f) + outlineHorizontalExpansion;
            Vector3 center = tileManager.GridToWorldCenter(coordinate);
            Vector3[] corners =
            {
                new Vector3(center.x - halfExtent, center.y, center.z - halfExtent),
                new Vector3(center.x - halfExtent, center.y, center.z + halfExtent),
                new Vector3(center.x + halfExtent, center.y, center.z + halfExtent),
                new Vector3(center.x + halfExtent, center.y, center.z - halfExtent)
            };
            float anchorHalfExtent = tileManager.CellSize * 0.5f;
            float pulse = outlinePulseMagnitude <= 0f ? 0f :
                Mathf.Sin(Time.time * outlinePulseSpeed) * outlinePulseMagnitude;
            for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
            {
                float anchorX = cornerIndex < 2 ? -anchorHalfExtent : anchorHalfExtent;
                float anchorZ = cornerIndex == 0 || cornerIndex == 3
                    ? -anchorHalfExtent
                    : anchorHalfExtent;
                Vector3 anchor = new Vector3(
                    center.x + anchorX,
                    center.y,
                    center.z + anchorZ);
                corners[cornerIndex] = ResolveVisualSurface(
                    coordinate,
                    anchor,
                    corners[cornerIndex]);
                corners[cornerIndex].y += outlineSurfaceOffset + pulse;
            }

            for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
            {
                AppendRibbon(corners[cornerIndex], corners[(cornerIndex + 1) % corners.Length], outlineWidth, color);
            }

            ApplyMesh(outlineDisplay, hoveredTileOutlineMaterial, "hovered-tile outline material");
        }

        private void BuildRoute(MovementPreviewResult preview, Color safeColor)
        {
            BeginMesh();
            if (preview.ExecutedPath != null)
            {
                for (int sectionIndex = 0; sectionIndex < preview.RouteSections.Count; sectionIndex++)
                {
                    MovementRouteSection section = preview.RouteSections[sectionIndex];
                    ExecutedMovementSegment segment = preview.ExecutedPath.Segments[section.SegmentIndex];
                    Color color = section.IsHazardous ? hazardRouteColor : safeColor;
                    AppendSampledRouteSection(segment, section.StartProgress, section.EndProgress, color);
                }
            }

            ApplyMesh(routeDisplay, routeMaterial, "route material");
        }

        private void AppendSampledRouteSection(
            ExecutedMovementSegment segment,
            float startProgress,
            float endProgress,
            Color color)
        {
            Vector3 start = selectedUnit.SampleExecutedSegmentPosition(segment, startProgress);
            Vector3 end = selectedUnit.SampleExecutedSegmentPosition(segment, endProgress);
            int sampleCount = Mathf.Max(
                1,
                Mathf.CeilToInt(Vector3.Distance(start, end) / routeSamplingDistance));
            sampleCount = Mathf.Max(sampleCount, routeCornerSmoothingResolution);
            bool usesSlopeSurface = TryGetSlopeRouteSurfaceHeights(
                segment,
                out float slopeStartSurfaceHeight,
                out float slopeEndSurfaceHeight);
            Vector3 previous = start;
            previous.y = usesSlopeSurface
                ? Mathf.Lerp(
                    slopeStartSurfaceHeight,
                    slopeEndSurfaceHeight,
                    startProgress) + routeSurfaceOffset
                : previous.y + routeSurfaceOffset;

            for (int sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
            {
                float progress = Mathf.Lerp(startProgress, endProgress, (float)sampleIndex / sampleCount);
                Vector3 next = selectedUnit.SampleExecutedSegmentPosition(segment, progress);
                next.y = usesSlopeSurface
                    ? Mathf.Lerp(
                        slopeStartSurfaceHeight,
                        slopeEndSurfaceHeight,
                        progress) + routeSurfaceOffset
                    : next.y + routeSurfaceOffset;

                AppendRibbon(previous, next, routeWidth, color);
                previous = next;
            }
        }

        private void BuildCoverIcons(Vector3Int coordinate, Color outlineColor)
        {
            DestinationCoverClassifier.CollectCover(
                new TileGridSnapshot(tileManager.Tiles),
                coordinate,
                coverResults);
            for (int coverIndex = 0; coverIndex < coverResults.Count; coverIndex++)
            {
                CoverIconInstance icon = GetCoverIcon(coverIndex);
                DestinationCover cover = coverResults[coverIndex];
                Sprite sprite = cover.Type == DestinationCoverType.Full
                    ? fullCoverShieldSprite
                    : halfCoverShieldSprite;
                if (sprite == null)
                {
                    icon.GameObject.SetActive(false);
                    continue;
                }

                Vector3 direction = cover.Direction;
                Vector3 position = tileManager.GridToWorldCenter(coordinate) +
                                   (direction * (tileManager.CellSize * (0.5f - coverIconEdgeOffset)));
                position = selectedUnit.EvaluateVisualSurfaceWorldPosition(coordinate, position);
                position.y += coverIconHeight;
                icon.SpriteRenderer.sprite = sprite;
                icon.SpriteRenderer.color = outlineColor;
                icon.GameObject.transform.position = position;
                if (coverIconsFaceCoverDirection)
                {
                    icon.GameObject.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                }

                icon.GameObject.transform.localScale = Vector3.one * coverIconWorldSize;
                icon.GameObject.SetActive(true);
            }

            for (int iconIndex = coverResults.Count; iconIndex < coverIconPool.Count; iconIndex++)
            {
                coverIconPool[iconIndex].GameObject.SetActive(false);
            }
        }

        private CoverIconInstance GetCoverIcon(int index)
        {
            while (coverIconPool.Count <= index)
            {
                GameObject iconObject = coverIconPrefab != null
                    ? Instantiate(coverIconPrefab, visualizationRoot)
                    : new GameObject("Movement Cover Icon");
                if (iconObject.transform.parent != visualizationRoot)
                {
                    iconObject.transform.SetParent(visualizationRoot, true);
                }

                SetLayerRecursively(iconObject, IgnoreRaycastLayer);
                Collider[] colliders = iconObject.GetComponentsInChildren<Collider>(true);
                for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
                {
                    colliders[colliderIndex].enabled = false;
                }

                SpriteRenderer spriteRenderer = iconObject.GetComponentInChildren<SpriteRenderer>(true);
                if (spriteRenderer == null)
                {
                    spriteRenderer = iconObject.AddComponent<SpriteRenderer>();
                }

                if (coverIconMaterial != null)
                {
                    spriteRenderer.sharedMaterial = coverIconMaterial;
                }

                spriteRenderer.sortingOrder = coverIconsRemainVisibleWhenOccluded ? 1000 : 0;
                coverIconProperties ??= new MaterialPropertyBlock();
                coverIconProperties.SetInt(
                    "_ZTest",
                    (int)(coverIconsRemainVisibleWhenOccluded
                        ? CompareFunction.Always
                        : CompareFunction.LessEqual));
                spriteRenderer.SetPropertyBlock(coverIconProperties);
                iconObject.SetActive(false);
                coverIconPool.Add(new CoverIconInstance(iconObject, spriteRenderer));
            }

            return coverIconPool[index];
        }

        private void UpdateAnimatedPresentation()
        {
            if (hasHoveredCoordinate && outlinePulseMagnitude > 0f)
            {
                Color color = activePreview == null || !activePreview.IsReachable
                    ? unreachableDestinationColor
                    : activePreview.HasHazardSections
                        ? hazardRouteColor
                        : GetActionPointColor(Mathf.Max(1, activePreview.RequiredActionPoints));
                BuildHoveredOutline(hoveredCoordinate, color);
            }

            if (routeDisplay != null && routeDisplay.GameObject.activeSelf &&
                !Mathf.Approximately(routeTextureScrollSpeed, 0f))
            {
                routeProperties ??= new MaterialPropertyBlock();
                float textureOffset = Time.time * routeTextureScrollSpeed;
                Vector4 textureTransform = new Vector4(1f, 1f, textureOffset, 0f);
                routeProperties.SetVector(BaseMapStProperty, textureTransform);
                routeProperties.SetVector(MainTextureStProperty, textureTransform);
                routeDisplay.Renderer.SetPropertyBlock(routeProperties);
            }

            float pulse = coverIconPulseMagnitude <= 0f ? 1f :
                1f + (Mathf.Sin(Time.time * coverIconPulseSpeed) * coverIconPulseMagnitude);
            for (int iconIndex = 0; iconIndex < coverIconPool.Count; iconIndex++)
            {
                CoverIconInstance icon = coverIconPool[iconIndex];
                if (!icon.GameObject.activeSelf)
                {
                    continue;
                }

                icon.GameObject.transform.localScale = Vector3.one * coverIconWorldSize * pulse;
            }
        }

        private void ClearHoverPresentation()
        {
            activePreview = null;
            if (outlineDisplay != null)
            {
                outlineDisplay.SetActive(false);
            }

            ClearRoute();
            ClearCoverIcons();
        }

        private void ClearRoute()
        {
            if (routeDisplay != null)
            {
                routeDisplay.SetActive(false);
            }
        }

        private void ClearCoverIcons()
        {
            for (int iconIndex = 0; iconIndex < coverIconPool.Count; iconIndex++)
            {
                coverIconPool[iconIndex].GameObject.SetActive(false);
            }
        }

        private void ClearAll()
        {
            hasHoveredCoordinate = false;
            activePreview = null;
            boundaryDisplay?.SetActive(false);
            ClearHoverPresentation();
        }

        private void BeginMesh()
        {
            meshVertices.Clear();
            meshColors.Clear();
            meshUvs.Clear();
            meshTriangles.Clear();
        }

        private void AppendRibbon(Vector3 start, Vector3 end, float width, Color color)
        {
            Vector3 direction = end - start;
            Vector3 horizontalDirection = new Vector3(direction.x, 0f, direction.z);
            if (horizontalDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            Vector3 sideways = Vector3.Cross(Vector3.up, horizontalDirection.normalized) * (width * 0.5f);
            int firstVertex = meshVertices.Count;
            meshVertices.Add(visualizationRoot.InverseTransformPoint(start - sideways));
            meshVertices.Add(visualizationRoot.InverseTransformPoint(start + sideways));
            meshVertices.Add(visualizationRoot.InverseTransformPoint(end - sideways));
            meshVertices.Add(visualizationRoot.InverseTransformPoint(end + sideways));
            meshColors.Add(color);
            meshColors.Add(color);
            meshColors.Add(color);
            meshColors.Add(color);
            float textureLength = direction.magnitude / routeTextureTiling;
            meshUvs.Add(new Vector2(0f, 0f));
            meshUvs.Add(new Vector2(0f, 1f));
            meshUvs.Add(new Vector2(textureLength, 0f));
            meshUvs.Add(new Vector2(textureLength, 1f));
            meshTriangles.Add(firstVertex);
            meshTriangles.Add(firstVertex + 2);
            meshTriangles.Add(firstVertex + 1);
            meshTriangles.Add(firstVertex + 1);
            meshTriangles.Add(firstVertex + 2);
            meshTriangles.Add(firstVertex + 3);
        }

        private void ApplyMesh(MeshDisplay display, Material material, string assetName)
        {
            Material resolvedMaterial = ResolveLineMaterial(material);
            if (display == null || resolvedMaterial == null || meshVertices.Count == 0)
            {
                display?.SetActive(false);
                if (resolvedMaterial == null && !loggedSetupWarning)
                {
                    Debug.LogWarning($"MovementVisualizationController has no {assetName}; that display is hidden.", this);
                    loggedSetupWarning = true;
                }

                return;
            }

            display.Renderer.sharedMaterial = resolvedMaterial;
            display.Mesh.Clear();
            display.Mesh.SetVertices(meshVertices);
            display.Mesh.SetColors(meshColors);
            display.Mesh.SetUVs(0, meshUvs);
            display.Mesh.SetTriangles(meshTriangles, 0, true);
            display.Mesh.RecalculateBounds();
            display.SetActive(true);
        }

        private Material ResolveLineMaterial(Material requestedMaterial)
        {
            return requestedMaterial ?? hoveredTileOutlineMaterial ?? routeMaterial ?? boundaryMaterial;
        }

        private void EnsureDisplays()
        {
            if (visualizationRoot == null)
            {
                GameObject rootObject = new GameObject("Movement Visualization");
                rootObject.transform.SetParent(transform, false);
                rootObject.layer = IgnoreRaycastLayer;
                visualizationRoot = rootObject.transform;
            }

            boundaryDisplay ??= CreateDisplay("Movement Boundaries");
            outlineDisplay ??= CreateDisplay("Movement Hover Outline");
            routeDisplay ??= CreateDisplay("Movement Route");
        }

        private MeshDisplay CreateDisplay(string displayName)
        {
            GameObject displayObject = new GameObject(displayName);
            displayObject.transform.SetParent(visualizationRoot, false);
            displayObject.layer = IgnoreRaycastLayer;
            MeshFilter meshFilter = displayObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = displayObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            Mesh mesh = new Mesh { name = displayName + " Mesh" };
            meshFilter.sharedMesh = mesh;
            displayObject.SetActive(false);
            return new MeshDisplay(displayObject, meshRenderer, mesh);
        }

        private Color GetActionPointColor(int actionPointBand)
        {
            switch (actionPointBand)
            {
                case 1:
                    return oneActionPointColor;
                case 2:
                    return twoActionPointColor;
                default:
                    return threeActionPointColor;
            }
        }

        /// <summary>
        /// Resolves a visual vertex from all same-level cells touching its unexpanded
        /// grid anchor. Selecting from raw surface heights first prevents the high
        /// slope cap from floating above its neighboring floor; a tied low floor and
        /// slope instead keeps the slope so its descent remains visible.
        /// </summary>
        private Vector3 ResolveVisualSurface(
            Vector3Int referenceCoordinate,
            Vector3 anchorWorldPosition,
            Vector3 sampleWorldPosition)
        {
            if (tileManager == null || selectedUnit == null)
            {
                return sampleWorldPosition;
            }

            GetAnchorCandidateRange(
                referenceCoordinate,
                anchorWorldPosition,
                out int minimumX,
                out int maximumX,
                out int minimumZ,
                out int maximumZ);
            bool hasFlatCandidate = false;
            bool hasSlopeCandidate = false;
            VisualSurfaceCandidate bestFlatCandidate = default;
            VisualSurfaceCandidate bestSlopeCandidate = default;

            for (int x = minimumX; x <= maximumX; x++)
            {
                for (int z = minimumZ; z <= maximumZ; z++)
                {
                    Vector3Int candidateCoordinate = new Vector3Int(
                        x,
                        referenceCoordinate.y,
                        z);
                    if (!TryCreateVisualSurfaceCandidate(
                            candidateCoordinate,
                            sampleWorldPosition,
                            out VisualSurfaceCandidate candidate))
                    {
                        continue;
                    }

                    if (candidate.IsSlope)
                    {
                        if (!hasSlopeCandidate ||
                            IsBetterRawSurfaceCandidate(candidate, bestSlopeCandidate))
                        {
                            bestSlopeCandidate = candidate;
                            hasSlopeCandidate = true;
                        }
                    }
                    else if (!hasFlatCandidate ||
                             IsBetterRawSurfaceCandidate(candidate, bestFlatCandidate))
                    {
                        bestFlatCandidate = candidate;
                        hasFlatCandidate = true;
                    }

                }
            }

            if (hasSlopeCandidate &&
                (!hasFlatCandidate ||
                 bestFlatCandidate.RawHeight <=
                 bestSlopeCandidate.RawHeight + SurfaceHeightComparisonTolerance))
            {
                sampleWorldPosition.y = bestSlopeCandidate.CorrectedHeight;
                return sampleWorldPosition;
            }

            if (hasFlatCandidate)
            {
                sampleWorldPosition.y = bestFlatCandidate.CorrectedHeight;
                return sampleWorldPosition;
            }

            Vector3 fallback = selectedUnit.EvaluateVisualSurfaceWorldPosition(
                referenceCoordinate,
                sampleWorldPosition);
            if (TryGetSlopeVisualizationRegion(referenceCoordinate, out SlopeRegion slopeRegion))
            {
                fallback.y += GetSlopeVisualizationHeightCorrection(
                    slopeRegion,
                    referenceCoordinate,
                    sampleWorldPosition);
            }

            return fallback;
        }

        private void GetAnchorCandidateRange(
            Vector3Int referenceCoordinate,
            Vector3 anchorWorldPosition,
            out int minimumX,
            out int maximumX,
            out int minimumZ,
            out int maximumZ)
        {
            Vector3 referenceCenter = tileManager.GridToWorldCenter(referenceCoordinate);
            float halfCell = tileManager.CellSize * 0.5f;
            float boundaryTolerance = tileManager.CellSize * 0.0001f;
            GetAnchorAxisCandidateRange(
                referenceCoordinate.x,
                anchorWorldPosition.x - referenceCenter.x,
                halfCell,
                boundaryTolerance,
                out minimumX,
                out maximumX);
            GetAnchorAxisCandidateRange(
                referenceCoordinate.z,
                anchorWorldPosition.z - referenceCenter.z,
                halfCell,
                boundaryTolerance,
                out minimumZ,
                out maximumZ);
        }

        private static void GetAnchorAxisCandidateRange(
            int referenceAxisCoordinate,
            float localAnchorPosition,
            float halfCell,
            float boundaryTolerance,
            out int minimumAxisCoordinate,
            out int maximumAxisCoordinate)
        {
            minimumAxisCoordinate = referenceAxisCoordinate;
            maximumAxisCoordinate = referenceAxisCoordinate;
            if (localAnchorPosition <= -halfCell + boundaryTolerance)
            {
                minimumAxisCoordinate--;
            }

            if (localAnchorPosition >= halfCell - boundaryTolerance)
            {
                maximumAxisCoordinate++;
            }
        }

        private bool TryCreateVisualSurfaceCandidate(
            Vector3Int candidateCoordinate,
            Vector3 sampleWorldPosition,
            out VisualSurfaceCandidate candidate)
        {
            candidate = default;
            if (!tileManager.TryGetTile(candidateCoordinate, out TileManager.TileType tileType) ||
                !TileManager.IsMovementSelectableTileType(tileType))
            {
                return false;
            }

            if (tileType == TileManager.TileType.Slope &&
                TryGetSlopeVisualizationRegion(candidateCoordinate, out SlopeRegion slopeRegion))
            {
                float rawHeight = slopeRegion.EvaluateWorldHeight(sampleWorldPosition);
                candidate = new VisualSurfaceCandidate(
                    candidateCoordinate,
                    rawHeight,
                    rawHeight + GetSlopeVisualizationHeightCorrection(
                        slopeRegion,
                        candidateCoordinate,
                        sampleWorldPosition),
                    true);
                return true;
            }

            float flatHeight = tileManager.GridToWorldCenter(candidateCoordinate).y;
            candidate = new VisualSurfaceCandidate(
                candidateCoordinate,
                flatHeight,
                flatHeight,
                false);
            return true;
        }

        private static bool IsBetterRawSurfaceCandidate(
            VisualSurfaceCandidate candidate,
            VisualSurfaceCandidate currentBest)
        {
            if (candidate.RawHeight > currentBest.RawHeight + SurfaceHeightComparisonTolerance)
            {
                return true;
            }

            if (candidate.RawHeight < currentBest.RawHeight - SurfaceHeightComparisonTolerance)
            {
                return false;
            }

            return IsBetterCorrectedSurfaceCandidate(candidate, currentBest);
        }

        private static bool IsBetterCorrectedSurfaceCandidate(
            VisualSurfaceCandidate candidate,
            VisualSurfaceCandidate currentBest)
        {
            if (candidate.CorrectedHeight >
                currentBest.CorrectedHeight + SurfaceHeightComparisonTolerance)
            {
                return true;
            }

            if (candidate.CorrectedHeight <
                currentBest.CorrectedHeight - SurfaceHeightComparisonTolerance)
            {
                return false;
            }

            if (candidate.IsSlope != currentBest.IsSlope)
            {
                return candidate.IsSlope;
            }

            return CompareCoordinates(candidate.Coordinate, currentBest.Coordinate) < 0;
        }

        private static int CompareCoordinates(Vector3Int left, Vector3Int right)
        {
            int xComparison = left.x.CompareTo(right.x);
            if (xComparison != 0)
            {
                return xComparison;
            }

            int yComparison = left.y.CompareTo(right.y);
            return yComparison != 0 ? yComparison : left.z.CompareTo(right.z);
        }

        private float GetSlopeVisualizationHeightCorrection(
            SlopeRegion slopeRegion,
            Vector3Int slopeCoordinate,
            Vector3 worldPosition)
        {
            return SlopeVisualizationHeightResolver.GetSlopeHeightOffset(
                slopeRegion,
                slopeCoordinate,
                worldPosition,
                tileManager.GridOrigin,
                tileManager.CellSize,
                slopeVisualizationHeightOffsetInCells);
        }

        private bool TryGetSlopeRouteSurfaceHeights(
            ExecutedMovementSegment segment,
            out float startSurfaceHeight,
            out float endSurfaceHeight)
        {
            startSurfaceHeight = 0f;
            endSurfaceHeight = 0f;
            if (segment.Type != MovementPresentationSegmentType.SlopeRun ||
                (!TryGetSlopeVisualizationRegion(segment.PresentationSegment.StartCoordinate, out _) &&
                 !TryGetSlopeVisualizationRegion(segment.PresentationSegment.EndCoordinate, out _)))
            {
                return false;
            }

            Vector3 startCenter = tileManager.GridToWorldCenter(segment.PresentationSegment.StartCoordinate);
            Vector3 endCenter = tileManager.GridToWorldCenter(segment.PresentationSegment.EndCoordinate);
            // A slope traversal may raise the unit root for presentation clearance.
            // The route ribbon is surface-aligned, but retains the unit's authored
            // standing offset so it still joins ordinary flat-route sections. Resolve
            // the full segment endpoints here rather than a colored subsection's
            // samples, because route sections can split one segment at hazard cells.
            startSurfaceHeight = ResolveVisualSurface(
                segment.PresentationSegment.StartCoordinate,
                startCenter,
                segment.StartWorldPosition).y + selectedUnit.StandingOffset.y;
            endSurfaceHeight = ResolveVisualSurface(
                segment.PresentationSegment.EndCoordinate,
                endCenter,
                segment.EndWorldPosition).y + selectedUnit.StandingOffset.y;
            return true;
        }

        private bool TryGetSlopeVisualizationRegion(
            Vector3Int coordinate,
            out SlopeRegion slopeRegion)
        {
            slopeRegion = null;
            return selectedUnit != null &&
                   selectedUnit.TryGetContinuousSlopeRegion(
                       coordinate,
                       coordinate,
                       out slopeRegion);
        }

        private void HandleGridRebuilt()
        {
            reachabilityDirty = true;
            ClearAll();
        }

        private void HandleMovementStarted(UnitMovement source, GridPathResult path)
        {
            ClearAll();
        }

        private void HandleMovementFinished(UnitMovement source, Vector3Int coordinate)
        {
            reachabilityDirty = true;
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;
            for (int childIndex = 0; childIndex < target.transform.childCount; childIndex++)
            {
                SetLayerRecursively(target.transform.GetChild(childIndex).gameObject, layer);
            }
        }

        private void OnValidate()
        {
            maximumDisplayedActionPointBands = Mathf.Clamp(maximumDisplayedActionPointBands, 1, 3);
            boundaryLineWidth = Mathf.Max(0.001f, boundaryLineWidth);
            outlineWidth = Mathf.Max(0.001f, outlineWidth);
            routeWidth = Mathf.Max(0.001f, routeWidth);
            routeSamplingDistance = Mathf.Max(0.02f, routeSamplingDistance);
            routeCornerSmoothingResolution = Mathf.Max(1, routeCornerSmoothingResolution);
            routeTextureTiling = Mathf.Max(0.01f, routeTextureTiling);
            coverIconWorldSize = Mathf.Max(0.01f, coverIconWorldSize);
            if (float.IsNaN(slopeVisualizationHeightOffsetInCells) ||
                float.IsInfinity(slopeVisualizationHeightOffsetInCells))
            {
                slopeVisualizationHeightOffsetInCells = 0.5f;
            }
            reachabilityDirty = true;
        }

        private sealed class MeshDisplay
        {
            public MeshDisplay(GameObject gameObject, MeshRenderer renderer, Mesh mesh)
            {
                GameObject = gameObject;
                Renderer = renderer;
                Mesh = mesh;
            }

            public GameObject GameObject { get; }
            public MeshRenderer Renderer { get; }
            public Mesh Mesh { get; }

            public void SetActive(bool active) => GameObject.SetActive(active);

            public void Dispose()
            {
                if (Mesh != null)
                {
                    UnityEngine.Object.Destroy(Mesh);
                }
            }
        }

        private readonly struct CoverIconInstance
        {
            public CoverIconInstance(GameObject gameObject, SpriteRenderer spriteRenderer)
            {
                GameObject = gameObject;
                SpriteRenderer = spriteRenderer;
            }

            public GameObject GameObject { get; }
            public SpriteRenderer SpriteRenderer { get; }
        }

        private readonly struct VisualSurfaceCandidate
        {
            public VisualSurfaceCandidate(
                Vector3Int coordinate,
                float rawHeight,
                float correctedHeight,
                bool isSlope)
            {
                Coordinate = coordinate;
                RawHeight = rawHeight;
                CorrectedHeight = correctedHeight;
                IsSlope = isSlope;
            }

            public Vector3Int Coordinate { get; }
            public float RawHeight { get; }
            public float CorrectedHeight { get; }
            public bool IsSlope { get; }
        }
    }
}
