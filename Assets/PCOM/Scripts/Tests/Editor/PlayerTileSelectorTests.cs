using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PCOM.Tests
{
    public class PlayerTileSelectorTests
    {
        private const int FloorSelectionLayer = 8;

        private readonly List<GameObject> createdObjects = new List<GameObject>();
        private TileManager tileManager;
        private PlayerTileSelector selector;

        [SetUp]
        public void SetUp()
        {
            tileManager = CreateGameObject("Tile Manager").AddComponent<TileManager>();
            selector = CreateGameObject("Player Tile Selector").AddComponent<PlayerTileSelector>();
            SetSelectorField("tileManager", tileManager);
            SetSelectorField("floorSelectionLayerMask", (LayerMask)(1 << FloorSelectionLayer));
            SetSelectorField("maximumSelectionDistance", 100f);
            SetSelectorField("surfaceBoundaryToleranceFraction", 0.01f);
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
        public void MovementModeDisabledRejectsOtherwiseValidRay()
        {
            CreateSelectionCollider(Vector3.zero);
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);

            bool selected = selector.TryGetHoveredMovementTile(
                new Ray(Vector3.up * 5f, Vector3.down),
                out MovementTileSelection result);

            Assert.That(selected, Is.False);
            Assert.That(result, Is.EqualTo(default(MovementTileSelection)));
        }

        [TestCase(TileManager.TileType.Floor)]
        [TestCase(TileManager.TileType.Slope)]
        [TestCase(TileManager.TileType.Hazard)]
        [TestCase(TileManager.TileType.Ladder)]
        public void PermittedColliderReturnsCanonicalTileResult(TileManager.TileType expectedType)
        {
            CreateSelectionCollider(Vector3.zero);
            SetTile(Vector3Int.zero, expectedType);
            selector.SetMovementSelectionMode(true);
            Physics.SyncTransforms();

            bool selected = selector.TryGetHoveredMovementTile(
                new Ray(Vector3.up * 5f, Vector3.down),
                out MovementTileSelection result);

            Assert.That(selected, Is.True);
            Assert.That(result.Coordinate, Is.EqualTo(Vector3Int.zero));
            Assert.That(result.TileType, Is.EqualTo(expectedType));
            Assert.That(result.WorldCenter, Is.EqualTo(tileManager.GridToWorldCenter(Vector3Int.zero)));
            Assert.That(result.PhysicsHitPoint.y, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void UnrelatedColliderDoesNotInterceptLayerFilteredRay()
        {
            BoxCollider unrelatedCollider = CreateCollider("Unrelated Prop", Vector3.up * 2f);
            unrelatedCollider.gameObject.layer = 0;
            CreateSelectionCollider(Vector3.zero);
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);
            selector.SetMovementSelectionMode(true);
            Physics.SyncTransforms();

            bool selected = selector.TrySelectMovementTile(
                new Ray(Vector3.up * 5f, Vector3.down),
                out MovementTileSelection result);

            Assert.That(selected, Is.True);
            Assert.That(result.Coordinate, Is.EqualTo(Vector3Int.zero));
        }

        [Test]
        public void TriggerSelectionGeometryIsIgnoredDeliberately()
        {
            BoxCollider triggerCollider = CreateSelectionCollider(Vector3.zero);
            triggerCollider.isTrigger = true;
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);
            selector.SetMovementSelectionMode(true);
            Physics.SyncTransforms();

            Assert.That(
                selector.TryGetHoveredMovementTile(
                    new Ray(Vector3.up * 5f, Vector3.down),
                    out _),
                Is.False);
        }

        [Test]
        public void StackedFloorsResolveSurfaceActuallyHit()
        {
            CreateSelectionCollider(Vector3.zero);
            CreateSelectionCollider(Vector3.up * 3f);
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);
            SetTile(Vector3Int.up * 3, TileManager.TileType.Hazard);
            selector.SetMovementSelectionMode(true);
            Physics.SyncTransforms();

            bool selected = selector.TryGetHoveredMovementTile(
                new Ray(Vector3.up * 10f, Vector3.down),
                out MovementTileSelection result);

            Assert.That(selected, Is.True);
            Assert.That(result.Coordinate, Is.EqualTo(Vector3Int.up * 3));
            Assert.That(result.TileType, Is.EqualTo(TileManager.TileType.Hazard));
        }

        [Test]
        public void HitBeyondMaximumDistanceIsRejected()
        {
            CreateSelectionCollider(Vector3.zero);
            SetTile(Vector3Int.zero, TileManager.TileType.Floor);
            SetSelectorField("maximumSelectionDistance", 2f);
            selector.SetMovementSelectionMode(true);
            Physics.SyncTransforms();

            Assert.That(
                selector.TryGetHoveredMovementTile(
                    new Ray(Vector3.up * 5f, Vector3.down),
                    out _),
                Is.False);
        }

        [Test]
        public void RotatedSlopeResolvesVoxelAtHitElevation()
        {
            BoxCollider slopeCollider = CreateSelectionCollider(Vector3.zero);
            slopeCollider.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            SetTile(Vector3Int.up, TileManager.TileType.Slope);
            selector.SetMovementSelectionMode(true);
            Physics.SyncTransforms();

            bool selected = selector.TryGetHoveredMovementTile(
                new Ray(Vector3.up * 5f, Vector3.down),
                out MovementTileSelection result);

            Assert.That(selected, Is.True);
            Assert.That(result.Coordinate, Is.EqualTo(Vector3Int.up));
            Assert.That(result.TileType, Is.EqualTo(TileManager.TileType.Slope));
        }

        [Test]
        public void MissingRequiredSetupLogsOnlyOnce()
        {
            SetSelectorField("tileManager", null);
            selector.SetMovementSelectionMode(true);
            const string expectedMessage =
                "PlayerTileSelector requires a TileManager, a gameplay Camera for screen-position selection, " +
                "a non-empty floor-selection LayerMask, and a finite positive maximum distance.";
            LogAssert.Expect(LogType.Error, expectedMessage);

            Assert.That(
                selector.TryGetHoveredMovementTile(new Ray(Vector3.up, Vector3.down), out _),
                Is.False);
            Assert.That(
                selector.TryGetHoveredMovementTile(new Ray(Vector3.up, Vector3.down), out _),
                Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        private BoxCollider CreateSelectionCollider(Vector3 position)
        {
            BoxCollider collider = CreateCollider("Floor Selection Collider", position);
            collider.gameObject.layer = FloorSelectionLayer;
            return collider;
        }

        private BoxCollider CreateCollider(string objectName, Vector3 position)
        {
            GameObject colliderObject = CreateGameObject(objectName);
            colliderObject.transform.position = position;
            return colliderObject.AddComponent<BoxCollider>();
        }

        private GameObject CreateGameObject(string objectName)
        {
            GameObject createdObject = new GameObject(objectName);
            createdObjects.Add(createdObject);
            return createdObject;
        }

        private void SetSelectorField(string fieldName, object value)
        {
            FieldInfo field = typeof(PlayerTileSelector).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(selector, value);
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
