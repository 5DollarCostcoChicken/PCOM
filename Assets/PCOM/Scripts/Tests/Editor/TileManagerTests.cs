using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PCOM.Tests
{
    public class TileManagerTests
    {
        private readonly List<GameObject> createdObjects = new List<GameObject>();
        private GameObject managerObject;
        private TileManager manager;

        [SetUp]
        public void SetUp()
        {
            managerObject = CreateGameObject("Tile Manager Test");
            manager = managerObject.AddComponent<TileManager>();
        }

        [TearDown]
        public void TearDown()
        {
            for (int objectIndex = createdObjects.Count - 1; objectIndex >= 0; objectIndex--)
            {
                if (createdObjects[objectIndex] != null)
                {
                    Object.DestroyImmediate(createdObjects[objectIndex]);
                }
            }

            createdObjects.Clear();
        }

        [Test]
        public void UnrotatedBoxMapsExpectedRectangularCells()
        {
            BoxCollider box = CreateCollider<BoxCollider>("Box");
            box.size = Vector3.one * 1.8f;

            MapCollider(box, TileManager.TileType.Floor);

            Assert.That(manager.Tiles.Count, Is.EqualTo(27));
            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(-1, -1, -1)), Is.True);
            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(1, 1, 1)), Is.True);
        }

        [Test]
        public void RotatedBoxDoesNotFillWorldBoundsCorners()
        {
            BoxCollider box = CreateCollider<BoxCollider>("Rotated Box");
            box.size = new Vector3(4f, 0.8f, 0.8f);
            box.transform.rotation = Quaternion.Euler(0f, 45f, 0f);

            MapCollider(box, TileManager.TileType.Floor);

            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(1, 0, -1)), Is.True);
            Assert.That(box.bounds.Intersects(manager.GetCellWorldBounds(new Vector3Int(1, 0, 1))), Is.True);
            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(1, 0, 1)), Is.False);
        }

        [Test]
        public void RotatedThinBoxMapsDiagonalWithoutFillingCorners()
        {
            BoxCollider box = CreateCollider<BoxCollider>("Thin Rotated Box");
            box.size = new Vector3(4f, 0.05f, 0.05f);
            box.transform.rotation = Quaternion.Euler(0f, 45f, 0f);

            MapCollider(box, TileManager.TileType.Floor);

            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(1, 0, -1)), Is.True);
            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(1, 0, 1)), Is.False);
        }

        [Test]
        public void SphereMapsVoxelizedSphereInsteadOfBoundsCube()
        {
            SphereCollider sphere = CreateCollider<SphereCollider>("Sphere");
            sphere.radius = 0.6f;

            MapCollider(sphere, TileManager.TileType.Floor);

            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(1, 0, 0)), Is.True);
            Assert.That(sphere.bounds.Intersects(manager.GetCellWorldBounds(new Vector3Int(1, 1, 1))), Is.True);
            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(1, 1, 1)), Is.False);
        }

        [Test]
        public void CapsuleMapsRoundedVolumeInsteadOfBoundsPrism()
        {
            CapsuleCollider capsule = CreateCollider<CapsuleCollider>("Capsule");
            capsule.direction = 1;
            capsule.height = 3f;
            capsule.radius = 0.6f;

            MapCollider(capsule, TileManager.TileType.Floor);

            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(1, 0, 0)), Is.True);
            Assert.That(capsule.bounds.Intersects(manager.GetCellWorldBounds(new Vector3Int(1, 1, 1))), Is.True);
            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(1, 1, 1)), Is.False);
        }

        [Test]
        public void NonConvexCylinderMeshDoesNotFillBoundsCorners()
        {
            GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            createdObjects.Add(source);
            Mesh cylinderMesh = source.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(source);

            MeshCollider cylinder = CreateCollider<MeshCollider>("Cylinder Mesh");
            cylinder.sharedMesh = cylinderMesh;
            cylinder.convex = false;
            cylinder.transform.localScale = new Vector3(1.2f, 0.9f, 1.2f);

            MapCollider(cylinder, TileManager.TileType.Floor);

            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(1, 0, 0)), Is.True);
            Assert.That(cylinder.bounds.Intersects(manager.GetCellWorldBounds(new Vector3Int(1, 0, 1))), Is.True);
            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(1, 0, 1)), Is.False);
        }

        [Test]
        public void ConvexIrregularMeshExcludesEmptyAreaInsideItsBounds()
        {
            Mesh tetrahedron = CreateTetrahedronMesh();
            MeshCollider meshCollider = CreateCollider<MeshCollider>("Irregular Mesh");
            meshCollider.sharedMesh = tetrahedron;
            meshCollider.convex = true;

            MapCollider(meshCollider, TileManager.TileType.Floor);

            Assert.That(meshCollider.bounds.Intersects(manager.GetCellWorldBounds(new Vector3Int(2, 2, 2))), Is.True);
            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(2, 2, 2)), Is.False);
            Assert.That(manager.Tiles.ContainsKey(Vector3Int.zero), Is.True);

            Object.DestroyImmediate(tetrahedron);
        }

        [Test]
        public void ColliderTransformsCenterAndChildTransformAreRespected()
        {
            GameObject bounder = CreateGameObject("Transformed Bounder");
            bounder.transform.SetPositionAndRotation(new Vector3(3f, 2f, -4f), Quaternion.Euler(10f, 35f, 5f));
            bounder.transform.localScale = new Vector3(2f, 0.5f, 1.5f);

            GameObject child = CreateGameObject("Collider Child");
            child.transform.SetParent(bounder.transform, false);
            child.transform.localPosition = new Vector3(1f, 1f, -0.5f);
            child.transform.localRotation = Quaternion.Euler(15f, 20f, 0f);
            BoxCollider box = child.AddComponent<BoxCollider>();
            box.center = new Vector3(0.4f, -0.2f, 0.3f);
            box.size = Vector3.one * 0.4f;

            Vector3 worldCenter = child.transform.TransformPoint(box.center);
            Vector3Int expectedCoordinate = manager.WorldToGridCoordinate(worldCenter);
            MapCollider(box, TileManager.TileType.Floor);

            Assert.That(manager.Tiles.ContainsKey(expectedCoordinate), Is.True);
        }

        [Test]
        public void MultipleChildCollidersCombineAndLaterTypeOverwrites()
        {
            GameObject bounder = CreateGameObject("Compound Bounder");
            BoxCollider first = AddChildBox(bounder.transform, Vector3.zero);
            BoxCollider second = AddChildBox(bounder.transform, new Vector3(5f, 0f, 0f));
            SphereCollider wall = CreateCollider<SphereCollider>("Wall Override");
            wall.radius = 0.2f;

            Physics.SyncTransforms();
            InvokePrivate("MapBounder", bounder, TileManager.TileType.Floor);
            MapCollider(wall, TileManager.TileType.Wall);

            Assert.That(manager.Tiles.ContainsKey(Vector3Int.zero), Is.True);
            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(5, 0, 0)), Is.True);
            Assert.That(manager.Tiles[Vector3Int.zero], Is.EqualTo(TileManager.TileType.Wall));
            Assert.That(first.enabled && second.enabled && wall.enabled, Is.True);
        }

        [Test]
        public void GridOriginAndCellSizeControlConversionsAndVoxelSelection()
        {
            manager.GridOrigin = new Vector3(10f, -2f, 3f);
            manager.CellSize = 2f;
            Vector3Int coordinate = new Vector3Int(2, 1, -3);
            Vector3 expectedCenter = new Vector3(14f, 0f, -3f);
            BoxCollider box = CreateCollider<BoxCollider>("Configured Grid Box");
            box.transform.position = expectedCenter;
            box.size = Vector3.one * 0.5f;

            MapCollider(box, TileManager.TileType.Floor);

            Assert.That(manager.GridToWorldCenter(coordinate), Is.EqualTo(expectedCenter));
            Assert.That(manager.WorldToGridCoordinate(expectedCenter), Is.EqualTo(coordinate));
            Assert.That(manager.GetCellWorldBounds(coordinate).size, Is.EqualTo(Vector3.one * 2f));
            Assert.That(manager.Tiles.Count, Is.EqualTo(1));
            Assert.That(manager.Tiles.ContainsKey(coordinate), Is.True);
        }

        [Test]
        public void MovementSelectableTileTypeIncludesOnlyFloorFamily()
        {
            Assert.That(TileManager.IsMovementSelectableTileType(TileManager.TileType.Floor), Is.True);
            Assert.That(TileManager.IsMovementSelectableTileType(TileManager.TileType.Slope), Is.True);
            Assert.That(TileManager.IsMovementSelectableTileType(TileManager.TileType.Hazard), Is.True);
            Assert.That(TileManager.IsMovementSelectableTileType(TileManager.TileType.Ladder), Is.True);
            Assert.That(TileManager.IsMovementSelectableTileType(TileManager.TileType.Air), Is.False);
            Assert.That(TileManager.IsMovementSelectableTileType(TileManager.TileType.Wall), Is.False);
            Assert.That(TileManager.IsMovementSelectableTileType(TileManager.TileType.Chasm), Is.False);
        }

        [Test]
        public void TryGetTileDoesNotExposeMissingCoordinatesAsTiles()
        {
            SetTile(Vector3Int.zero, TileManager.TileType.Hazard);

            Assert.That(manager.TryGetTile(Vector3Int.zero, out TileManager.TileType tileType), Is.True);
            Assert.That(tileType, Is.EqualTo(TileManager.TileType.Hazard));
            Assert.That(manager.TryGetTile(Vector3Int.right, out _), Is.False);
        }

        [TestCase(TileManager.TileType.Floor)]
        [TestCase(TileManager.TileType.Slope)]
        [TestCase(TileManager.TileType.Hazard)]
        [TestCase(TileManager.TileType.Ladder)]
        public void SurfaceHitResolvesSelectableFloorFamilyType(TileManager.TileType expectedType)
        {
            Vector3Int expectedCoordinate = new Vector3Int(2, 3, -1);
            manager.GridOrigin = new Vector3(10f, -4f, 7f);
            manager.CellSize = 2f;
            SetTile(expectedCoordinate, expectedType);
            Vector3 cellCenter = manager.GridToWorldCenter(expectedCoordinate);
            Vector3 topSurface = cellCenter + (Vector3.up * manager.CellSize * 0.5f);

            bool resolved = manager.TryResolveMovementSelectableTile(
                topSurface,
                Vector3.up,
                0.01f,
                out Vector3Int coordinate,
                out TileManager.TileType tileType);

            Assert.That(resolved, Is.True);
            Assert.That(coordinate, Is.EqualTo(expectedCoordinate));
            Assert.That(tileType, Is.EqualTo(expectedType));
        }

        [TestCase(TileManager.TileType.Air)]
        [TestCase(TileManager.TileType.Wall)]
        [TestCase(TileManager.TileType.Chasm)]
        public void SurfaceHitRejectsNonSelectableTileTypes(TileManager.TileType rejectedType)
        {
            SetTile(Vector3Int.zero, rejectedType);

            Assert.That(
                manager.TryResolveMovementSelectableTile(
                    Vector3.up * 0.5f,
                    Vector3.up,
                    0.01f,
                    out _,
                    out _),
                Is.False);
        }

        [Test]
        public void SurfaceHitRejectsMissingCoordinate()
        {
            Assert.That(
                manager.TryResolveMovementSelectableTile(
                    Vector3.up * 0.5f,
                    Vector3.up,
                    0.01f,
                    out _,
                    out _),
                Is.False);
        }

        [Test]
        public void SlopedSurfaceHitPromotesAStackedLowerSlopeToTheUpperTile()
        {
            SetTile(Vector3Int.zero, TileManager.TileType.Slope);
            SetTile(Vector3Int.up, TileManager.TileType.Slope);

            bool resolved = manager.TryResolveMovementSelectableTile(
                Vector3.up * 0.35f,
                new Vector3(0f, 1f, 1f).normalized,
                0.01f,
                out Vector3Int coordinate,
                out TileManager.TileType tileType);

            Assert.That(resolved, Is.True);
            Assert.That(coordinate, Is.EqualTo(Vector3Int.up));
            Assert.That(tileType, Is.EqualTo(TileManager.TileType.Slope));
        }

        [Test]
        public void BounderWithoutColliderLogsWarningAndIsSkipped()
        {
            GameObject bounder = CreateGameObject("Missing Collider Bounder");
            LogAssert.Expect(LogType.Warning, new Regex("cannot be accurately voxelized.*was skipped"));

            InvokePrivate("MapBounder", bounder, TileManager.TileType.Floor);

            Assert.That(manager.Tiles, Is.Empty);
        }

        [Test]
        public void RecreatingVisualsReusesRootAndRemovesStaleChildren()
        {
            BoxCollider box = CreateCollider<BoxCollider>("Visualized Box");
            box.size = Vector3.one * 0.4f;
            MapCollider(box, TileManager.TileType.Floor);

            InvokePrivate("CreateTileVisuals");
            InvokePrivate("CreateTileVisuals");

            int matchingRootCount = 0;
            Transform visualizationRoot = null;
            for (int childIndex = 0; childIndex < manager.transform.childCount; childIndex++)
            {
                Transform child = manager.transform.GetChild(childIndex);
                if (child.name == "Generated Tile Visuals")
                {
                    matchingRootCount++;
                    visualizationRoot = child;
                }
            }

            Assert.That(matchingRootCount, Is.EqualTo(1));
            Assert.That(visualizationRoot, Is.Not.Null);
            Assert.That(visualizationRoot.childCount, Is.EqualTo(manager.Tiles.Count));
        }

        [Test]
        public void RebuildGridClearsStaleTileEntries()
        {
            BoxCollider box = CreateCollider<BoxCollider>("Temporary Far Box");
            Vector3Int staleCoordinate = new Vector3Int(10000, 10000, 10000);
            box.transform.position = staleCoordinate;
            box.size = Vector3.one * 0.4f;
            MapCollider(box, TileManager.TileType.Floor);
            Assert.That(manager.Tiles.ContainsKey(staleCoordinate), Is.True);

            Object.DestroyImmediate(box.gameObject);
            SetPrivateField("visualizeGrid", false);
            manager.RebuildGrid();

            Assert.That(manager.Tiles.ContainsKey(staleCoordinate), Is.False);
        }

        [Test]
        public void FloorBoundaryWallsAreAddedOnlyWithoutFloorOrChasmSupport()
        {
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);
            SetTile(new Vector3Int(1, -2, 0), TileManager.TileType.Floor);
            SetTile(new Vector3Int(-1, -3, 0), TileManager.TileType.Chasm);
            SetTile(new Vector3Int(0, -1, 1), TileManager.TileType.Wall);
            SetTile(new Vector3Int(0, 0, -1), TileManager.TileType.Air);

            InvokePrivate("CreateFloorBoundaryWalls");

            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(1, 0, 0)), Is.False);
            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(-1, 0, 0)), Is.False);
            Assert.That(manager.Tiles[new Vector3Int(0, 0, 1)], Is.EqualTo(TileManager.TileType.Wall));
            Assert.That(manager.Tiles[new Vector3Int(0, 0, -1)], Is.EqualTo(TileManager.TileType.Wall));
        }

        [Test]
        public void VerticalColumnsApplyLimitsPropagationAndAirFilling()
        {
            SetTile(new Vector3Int(0, 0, 0), TileManager.TileType.Floor);

            SetTile(new Vector3Int(1, -1, 0), TileManager.TileType.Wall);
            SetTile(new Vector3Int(1, 2, 0), TileManager.TileType.Air);

            SetTile(new Vector3Int(2, 2, 0), TileManager.TileType.Chasm);

            SetTile(new Vector3Int(3, 3, 0), TileManager.TileType.Floor);
            SetTile(new Vector3Int(3, 0, 0), TileManager.TileType.Air);

            SetTile(new Vector3Int(4, 20, 0), TileManager.TileType.Wall);
            SetTile(new Vector3Int(5, -10, 0), TileManager.TileType.Wall);

            InvokePrivate("FillVerticalColumns");

            Assert.That(manager.Tiles[new Vector3Int(0, -2, 0)], Is.EqualTo(TileManager.TileType.Wall));
            Assert.That(manager.Tiles[new Vector3Int(0, 10, 0)], Is.EqualTo(TileManager.TileType.Air));

            Assert.That(manager.Tiles[new Vector3Int(1, -2, 0)], Is.EqualTo(TileManager.TileType.Air));
            Assert.That(manager.Tiles[new Vector3Int(1, 2, 0)], Is.EqualTo(TileManager.TileType.Air));
            Assert.That(manager.Tiles[new Vector3Int(1, 3, 0)], Is.EqualTo(TileManager.TileType.Wall));

            Assert.That(manager.Tiles[new Vector3Int(2, -2, 0)], Is.EqualTo(TileManager.TileType.Chasm));
            Assert.That(manager.Tiles[new Vector3Int(2, 13, 0)], Is.EqualTo(TileManager.TileType.Chasm));

            Assert.That(manager.Tiles[new Vector3Int(3, -2, 0)], Is.EqualTo(TileManager.TileType.Air));
            Assert.That(manager.Tiles[new Vector3Int(3, 0, 0)], Is.EqualTo(TileManager.TileType.Air));
            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(4, 20, 0)), Is.False);
            Assert.That(manager.Tiles[new Vector3Int(4, 0, 0)], Is.EqualTo(TileManager.TileType.Air));
            Assert.That(manager.Tiles.ContainsKey(new Vector3Int(5, -10, 0)), Is.False);
            Assert.That(manager.Tiles[new Vector3Int(5, -2, 0)], Is.EqualTo(TileManager.TileType.Wall));

            for (int columnX = 0; columnX <= 5; columnX++)
            {
                for (int y = -2; y <= 13; y++)
                {
                    Assert.That(manager.Tiles.ContainsKey(new Vector3Int(columnX, y, 0)), Is.True);
                }
            }
        }

        [Test]
        public void ChasmStopsPropagatingUpwardAtFloor()
        {
            SetTile(new Vector3Int(0, -2, 0), TileManager.TileType.Chasm);
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);

            InvokePrivate("FillVerticalColumns");

            Assert.That(manager.Tiles[new Vector3Int(0, -1, 0)], Is.EqualTo(TileManager.TileType.Chasm));
            Assert.That(manager.Tiles[Vector3Int.zero], Is.EqualTo(TileManager.TileType.Floor));
            Assert.That(manager.Tiles[new Vector3Int(0, 1, 0)], Is.EqualTo(TileManager.TileType.Air));
            Assert.That(manager.Tiles[new Vector3Int(0, 10, 0)], Is.EqualTo(TileManager.TileType.Air));
        }

        [Test]
        public void WallStopsPropagatingUpwardAtFloorFamilyTiles()
        {
            TileManager.TileType[] floorTypes =
            {
                TileManager.TileType.Floor,
                TileManager.TileType.Slope,
                TileManager.TileType.Hazard,
                TileManager.TileType.Ladder
            };

            for (int columnX = 0; columnX < floorTypes.Length; columnX++)
            {
                SetTile(new Vector3Int(columnX, -3, 0), TileManager.TileType.Wall);
                SetTile(new Vector3Int(columnX, 0, 0), floorTypes[columnX]);
            }

            InvokePrivate("FillVerticalColumns");

            for (int columnX = 0; columnX < floorTypes.Length; columnX++)
            {
                Assert.That(
                    manager.Tiles[new Vector3Int(columnX, -1, 0)],
                    Is.EqualTo(TileManager.TileType.Wall));
                Assert.That(
                    manager.Tiles[new Vector3Int(columnX, 0, 0)],
                    Is.EqualTo(floorTypes[columnX]));
                Assert.That(
                    manager.Tiles[new Vector3Int(columnX, 1, 0)],
                    Is.EqualTo(TileManager.TileType.Air));
                Assert.That(
                    manager.Tiles[new Vector3Int(columnX, 10, 0)],
                    Is.EqualTo(TileManager.TileType.Air));
            }
        }

        [Test]
        public void SlopeUsesFloorBoundaryAndVerticalRules()
        {
            SetTile(Vector3Int.zero, TileManager.TileType.Slope);
            SetTile(new Vector3Int(2, -2, 0), TileManager.TileType.Chasm);
            SetTile(new Vector3Int(2, 0, 0), TileManager.TileType.Slope);

            InvokePrivate("CreateFloorBoundaryWalls");
            InvokePrivate("FillVerticalColumns");

            Assert.That(manager.Tiles[new Vector3Int(1, 0, 0)], Is.EqualTo(TileManager.TileType.Wall));
            Assert.That(manager.Tiles[new Vector3Int(0, -2, 0)], Is.EqualTo(TileManager.TileType.Wall));
            Assert.That(manager.Tiles[new Vector3Int(0, 1, 0)], Is.EqualTo(TileManager.TileType.Air));
            Assert.That(manager.Tiles[new Vector3Int(2, -1, 0)], Is.EqualTo(TileManager.TileType.Chasm));
            Assert.That(manager.Tiles[new Vector3Int(2, 1, 0)], Is.EqualTo(TileManager.TileType.Air));
        }

        [Test]
        public void HazardAndLadderProjectOntoAndOnlyOverrideFloorTypes()
        {
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);
            SetTile(new Vector3Int(1, 0, 0), TileManager.TileType.Slope);
            SetTile(new Vector3Int(2, 0, 0), TileManager.TileType.Wall);

            BoxCollider floorOverride = CreateCollider<BoxCollider>("Floor Override");
            floorOverride.transform.position = new Vector3(0f, 2f, 0f);
            floorOverride.size = Vector3.one * 0.4f;
            MapFloorOverrideBounder(floorOverride.gameObject, TileManager.TileType.Hazard);
            Assert.That(manager.Tiles[Vector3Int.zero], Is.EqualTo(TileManager.TileType.Hazard));

            floorOverride.transform.position = new Vector3(1f, 2f, 0f);
            MapFloorOverrideBounder(floorOverride.gameObject, TileManager.TileType.Hazard);
            Assert.That(manager.Tiles[new Vector3Int(1, 0, 0)], Is.EqualTo(TileManager.TileType.Hazard));

            floorOverride.transform.position = new Vector3(2f, 2f, 0f);
            MapFloorOverrideBounder(floorOverride.gameObject, TileManager.TileType.Ladder);
            Assert.That(manager.Tiles[new Vector3Int(2, 0, 0)], Is.EqualTo(TileManager.TileType.Wall));

            floorOverride.transform.position = new Vector3(0f, 2f, 0f);
            MapFloorOverrideBounder(floorOverride.gameObject, TileManager.TileType.Ladder);
            Assert.That(manager.Tiles[Vector3Int.zero], Is.EqualTo(TileManager.TileType.Ladder));
        }

        [Test]
        public void BounderMappingDisablesMeshRenderersWithoutDisablingColliders()
        {
            GameObject volumeBounder = CreateGameObject("Volume Bounder");
            MeshRenderer volumeRenderer = volumeBounder.AddComponent<MeshRenderer>();
            BoxCollider volumeCollider = volumeBounder.AddComponent<BoxCollider>();
            GameObject visualChild = CreateGameObject("Bounder Visual Child");
            visualChild.transform.SetParent(volumeBounder.transform, false);
            MeshRenderer childRenderer = visualChild.AddComponent<MeshRenderer>();
            Physics.SyncTransforms();

            InvokePrivate("MapBounder", volumeBounder, TileManager.TileType.Floor);

            Assert.That(volumeRenderer.enabled, Is.False);
            Assert.That(childRenderer.enabled, Is.False);
            Assert.That(volumeCollider.enabled, Is.True);

            SetTile(new Vector3Int(5, 0, 0), TileManager.TileType.Floor);
            GameObject overrideBounder = CreateGameObject("Override Bounder");
            overrideBounder.transform.position = new Vector3(5f, 1f, 0f);
            MeshRenderer overrideRenderer = overrideBounder.AddComponent<MeshRenderer>();
            BoxCollider overrideCollider = overrideBounder.AddComponent<BoxCollider>();
            overrideCollider.size = Vector3.one * 0.4f;

            MapFloorOverrideBounder(overrideBounder, TileManager.TileType.Hazard);

            Assert.That(overrideRenderer.enabled, Is.False);
            Assert.That(overrideCollider.enabled, Is.True);
        }

        [Test]
        public void BounderRenderersRemainEnabledWhenHidingIsDisabled()
        {
            SetPrivateField("hideBounderMeshRenderers", false);
            GameObject bounder = CreateGameObject("Visible Bounder");
            MeshRenderer renderer = bounder.AddComponent<MeshRenderer>();
            BoxCollider collider = bounder.AddComponent<BoxCollider>();
            Physics.SyncTransforms();

            InvokePrivate("MapBounder", bounder, TileManager.TileType.Floor);

            Assert.That(renderer.enabled, Is.True);
            Assert.That(collider.enabled, Is.True);
        }

        [Test]
        public void FloorSubtypesUseTheirDedicatedVisualizationColors()
        {
            Color expectedSlopeColor = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            Color expectedHazardColor = new Color(0.2f, 0.3f, 0.4f, 0.5f);
            Color expectedLadderColor = new Color(0.3f, 0.4f, 0.5f, 0.6f);
            SetPrivateField("slopeColor", expectedSlopeColor);
            SetPrivateField("hazardColor", expectedHazardColor);
            SetPrivateField("ladderColor", expectedLadderColor);

            Assert.That(
                (Color)InvokePrivate("GetTileColor", TileManager.TileType.Slope),
                Is.EqualTo(expectedSlopeColor));
            Assert.That(
                (Color)InvokePrivate("GetTileColor", TileManager.TileType.Hazard),
                Is.EqualTo(expectedHazardColor));
            Assert.That(
                (Color)InvokePrivate("GetTileColor", TileManager.TileType.Ladder),
                Is.EqualTo(expectedLadderColor));
        }

        [Test]
        public void PartFourPointFiveRetainsOnlyFloorFamilyTilesAndTheirMappedNeighbors()
        {
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);
            SetTile(Vector3Int.right, TileManager.TileType.Air);
            SetTile(Vector3Int.right * 2, TileManager.TileType.Air);

            Vector3Int slopeCoordinate = new Vector3Int(10, 0, 0);
            SetTile(slopeCoordinate, TileManager.TileType.Slope);
            SetTile(slopeCoordinate + Vector3Int.up, TileManager.TileType.Chasm);

            Vector3Int hazardCoordinate = new Vector3Int(20, 0, 0);
            SetTile(hazardCoordinate, TileManager.TileType.Hazard);
            SetTile(hazardCoordinate + Vector3Int.left, TileManager.TileType.Wall);

            Vector3Int ladderCoordinate = new Vector3Int(30, 0, 0);
            SetTile(ladderCoordinate, TileManager.TileType.Ladder);
            SetTile(ladderCoordinate + Vector3Int.down, TileManager.TileType.Air);

            Vector3Int distantWallCoordinate = new Vector3Int(100, 0, 0);
            Vector3Int distantAirCoordinate = new Vector3Int(200, 0, 0);
            SetTile(distantWallCoordinate, TileManager.TileType.Wall);
            SetTile(distantAirCoordinate, TileManager.TileType.Air);

            InvokePrivate("CullTilesOutsideFloorNeighborhood");

            Assert.That(manager.Tiles.ContainsKey(Vector3Int.zero), Is.True);
            Assert.That(manager.Tiles.ContainsKey(Vector3Int.right), Is.True);
            Assert.That(manager.Tiles.ContainsKey(slopeCoordinate), Is.True);
            Assert.That(manager.Tiles.ContainsKey(slopeCoordinate + Vector3Int.up), Is.True);
            Assert.That(manager.Tiles.ContainsKey(hazardCoordinate), Is.True);
            Assert.That(manager.Tiles.ContainsKey(hazardCoordinate + Vector3Int.left), Is.True);
            Assert.That(manager.Tiles.ContainsKey(ladderCoordinate), Is.True);
            Assert.That(manager.Tiles.ContainsKey(ladderCoordinate + Vector3Int.down), Is.True);
            Assert.That(manager.Tiles.ContainsKey(Vector3Int.right * 2), Is.False);
            Assert.That(manager.Tiles.ContainsKey(distantWallCoordinate), Is.False);
            Assert.That(manager.Tiles.ContainsKey(distantAirCoordinate), Is.False);
            Assert.That(manager.Tiles.Count, Is.EqualTo(8));
        }

        [Test]
        public void PartFourPointFiveRetainsDiagonalNeighbors()
        {
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);

            Vector3Int horizontalDiagonal = new Vector3Int(1, 0, 1);
            Vector3Int threeAxisDiagonal = new Vector3Int(-1, 1, -1);
            Vector3Int distantCoordinate = new Vector3Int(2, 2, 2);
            SetTile(horizontalDiagonal, TileManager.TileType.Air);
            SetTile(threeAxisDiagonal, TileManager.TileType.Wall);
            SetTile(distantCoordinate, TileManager.TileType.Air);

            InvokePrivate("CullTilesOutsideFloorNeighborhood");

            Assert.That(manager.Tiles.ContainsKey(horizontalDiagonal), Is.True);
            Assert.That(manager.Tiles.ContainsKey(threeAxisDiagonal), Is.True);
            Assert.That(manager.Tiles.ContainsKey(distantCoordinate), Is.False);
        }

        [Test]
        public void PartFourPointFiveRetainsTwoCellsAboveHorizontalWallAndChasmNeighbors()
        {
            SetTile(Vector3Int.zero, TileManager.TileType.Slope);

            Vector3Int neighboringWall = Vector3Int.right;
            Vector3Int neighboringChasm = new Vector3Int(0, 0, 1);
            Vector3Int neighboringFloor = Vector3Int.left;
            SetTile(neighboringWall, TileManager.TileType.Wall);
            SetTile(neighboringChasm, TileManager.TileType.Chasm);
            SetTile(neighboringFloor, TileManager.TileType.Floor);

            SetTile(neighboringWall + Vector3Int.up, TileManager.TileType.Air);
            SetTile(neighboringWall + (Vector3Int.up * 2), TileManager.TileType.Air);
            SetTile(neighboringWall + (Vector3Int.up * 3), TileManager.TileType.Air);
            SetTile(neighboringChasm + Vector3Int.up, TileManager.TileType.Chasm);
            SetTile(neighboringChasm + (Vector3Int.up * 2), TileManager.TileType.Chasm);
            SetTile(neighboringChasm + (Vector3Int.up * 3), TileManager.TileType.Chasm);
            SetTile(neighboringFloor + (Vector3Int.up * 2), TileManager.TileType.Air);

            InvokePrivate("CullTilesOutsideFloorNeighborhood");

            Assert.That(manager.Tiles.ContainsKey(neighboringWall + Vector3Int.up), Is.True);
            Assert.That(manager.Tiles.ContainsKey(neighboringWall + (Vector3Int.up * 2)), Is.True);
            Assert.That(manager.Tiles.ContainsKey(neighboringWall + (Vector3Int.up * 3)), Is.False);
            Assert.That(manager.Tiles.ContainsKey(neighboringChasm + Vector3Int.up), Is.True);
            Assert.That(manager.Tiles.ContainsKey(neighboringChasm + (Vector3Int.up * 2)), Is.True);
            Assert.That(manager.Tiles.ContainsKey(neighboringChasm + (Vector3Int.up * 3)), Is.False);
            Assert.That(manager.Tiles.ContainsKey(neighboringFloor + (Vector3Int.up * 2)), Is.False);
        }

        [Test]
        public void FloorNeighborsOnlyVisualizesFloorNeighborhoodAndAllWalls()
        {
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);
            SetTile(Vector3Int.right, TileManager.TileType.Air);
            SetTile(Vector3Int.right * 2, TileManager.TileType.Air);
            SetTile(Vector3Int.up, TileManager.TileType.Chasm);

            Vector3Int ladderCoordinate = new Vector3Int(10, 0, 0);
            SetTile(ladderCoordinate, TileManager.TileType.Ladder);
            SetTile(ladderCoordinate + Vector3Int.down, TileManager.TileType.Air);

            Vector3Int wallCoordinate = new Vector3Int(100, 0, 0);
            Vector3Int distantAirCoordinate = new Vector3Int(200, 0, 0);
            SetTile(wallCoordinate, TileManager.TileType.Wall);
            SetTile(distantAirCoordinate, TileManager.TileType.Air);
            SetPrivateField("floorNeighborsOnly", true);

            InvokePrivate("CreateTileVisuals");

            Transform visualizationRoot = manager.transform.Find("Generated Tile Visuals");
            Assert.That(visualizationRoot, Is.Not.Null);
            HashSet<Vector3Int> visualizedCoordinates = new HashSet<Vector3Int>();
            for (int childIndex = 0; childIndex < visualizationRoot.childCount; childIndex++)
            {
                visualizedCoordinates.Add(
                    manager.WorldToGridCoordinate(visualizationRoot.GetChild(childIndex).position));
            }

            Assert.That(visualizedCoordinates.Contains(Vector3Int.zero), Is.True);
            Assert.That(visualizedCoordinates.Contains(Vector3Int.right), Is.True);
            Assert.That(visualizedCoordinates.Contains(Vector3Int.up), Is.True);
            Assert.That(visualizedCoordinates.Contains(ladderCoordinate), Is.True);
            Assert.That(visualizedCoordinates.Contains(ladderCoordinate + Vector3Int.down), Is.True);
            Assert.That(visualizedCoordinates.Contains(wallCoordinate), Is.True);
            Assert.That(visualizedCoordinates.Contains(Vector3Int.right * 2), Is.False);
            Assert.That(visualizedCoordinates.Contains(distantAirCoordinate), Is.False);
            Assert.That(visualizedCoordinates.Count, Is.EqualTo(6));
        }

        [Test]
        public void PlayerPathUsesPropertyBlockAndRestoresTileTypeColor()
        {
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);
            InvokePrivate("CreateTileVisuals");
            Renderer renderer = manager.transform
                .Find("Generated Tile Visuals")
                .GetChild(0)
                .GetComponent<Renderer>();
            Material sharedMaterial = renderer.sharedMaterial;
            MaterialPropertyBlock properties = new MaterialPropertyBlock();

            manager.ShowPlayerDebugPath(new[] { Vector3Int.zero });
            renderer.GetPropertyBlock(properties);
            Assert.That(
                properties.GetColor(Shader.PropertyToID("_BaseColor")),
                Is.EqualTo(new Color(1f, 0.1f, 0.65f, 1f)));
            Assert.That(renderer.sharedMaterial, Is.SameAs(sharedMaterial));

            manager.ClearPlayerDebugPath();
            renderer.GetPropertyBlock(properties);
            Assert.That(
                properties.GetColor(Shader.PropertyToID("_BaseColor")),
                Is.EqualTo(new Color(0.7f, 0.7f, 0.7f, 0.75f)));
            Assert.That(renderer.sharedMaterial, Is.SameAs(sharedMaterial));
        }

        [Test]
        public void PlayerPathVisualizationIsOptionalWhenGridVisualsDoNotExist()
        {
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);

            Assert.DoesNotThrow(() => manager.ShowPlayerDebugPath(new[] { Vector3Int.zero }));
            Assert.DoesNotThrow(manager.ClearPlayerDebugPath);
        }

        private T CreateCollider<T>(string objectName) where T : Collider
        {
            GameObject colliderObject = CreateGameObject(objectName);
            return colliderObject.AddComponent<T>();
        }

        private BoxCollider AddChildBox(Transform parent, Vector3 localPosition)
        {
            GameObject child = CreateGameObject("Child Box");
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            BoxCollider box = child.AddComponent<BoxCollider>();
            box.size = Vector3.one * 0.4f;
            return box;
        }

        private GameObject CreateGameObject(string objectName)
        {
            GameObject createdObject = new GameObject(objectName);
            createdObjects.Add(createdObject);
            return createdObject;
        }

        private void MapCollider(Collider collider, TileManager.TileType tileType)
        {
            Physics.SyncTransforms();
            InvokePrivate("MapCollider", collider, tileType);
        }

        private void MapFloorOverrideBounder(GameObject bounder, TileManager.TileType floorSubtype)
        {
            Physics.SyncTransforms();
            Dictionary<Vector2Int, List<Vector3Int>> columns =
                (Dictionary<Vector2Int, List<Vector3Int>>)InvokePrivate("BuildColumnIndex");
            InvokePrivate("MapFloorOverrideBounder", bounder, floorSubtype, columns);
        }

        private object InvokePrivate(string methodName, params object[] arguments)
        {
            MethodInfo method = typeof(TileManager).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Expected private method '{methodName}' to exist.");
            return method.Invoke(manager, arguments);
        }

        private void SetPrivateField(string fieldName, object value)
        {
            FieldInfo field = typeof(TileManager).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected private field '{fieldName}' to exist.");
            field.SetValue(manager, value);
        }

        private void SetTile(Vector3Int coordinate, TileManager.TileType tileType)
        {
            FieldInfo tilesField = typeof(TileManager).GetField("tiles", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(tilesField, Is.Not.Null, "Expected private tile dictionary to exist.");
            Dictionary<Vector3Int, TileManager.TileType> mutableTiles =
                (Dictionary<Vector3Int, TileManager.TileType>)tilesField.GetValue(manager);
            mutableTiles[coordinate] = tileType;
        }

        private static Mesh CreateTetrahedronMesh()
        {
            Mesh mesh = new Mesh { name = "Test Tetrahedron" };
            mesh.vertices = new[]
            {
                Vector3.zero,
                new Vector3(3f, 0f, 0f),
                new Vector3(0f, 3f, 0f),
                new Vector3(0f, 0f, 3f)
            };
            mesh.triangles = new[]
            {
                0, 2, 1,
                0, 1, 3,
                0, 3, 2,
                1, 2, 3
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
