using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PCOM.Tests
{
    public class MovementVisualizationSurfaceTests
    {
        private readonly List<GameObject> createdObjects = new List<GameObject>();

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
        public void SharedSlopeVerticesChooseHighFlatAndLowSlopeAtTheSameLogicalLevel()
        {
            TileManager tileManager = CreateGameObject("Tile Manager").AddComponent<TileManager>();
            UnitMovement unitMovement = CreateGameObject("Unit Movement").AddComponent<UnitMovement>();
            MovementVisualizationController controller =
                CreateGameObject("Movement Visualization").AddComponent<MovementVisualizationController>();
            SetPrivateField(unitMovement, "tileManager", tileManager);
            SetPrivateField(controller, "tileManager", tileManager);
            SetPrivateField(controller, "selectedUnit", unitMovement);

            SetTile(tileManager, new Vector3Int(-1, 1, 0), TileManager.TileType.Floor);
            SetTile(tileManager, new Vector3Int(0, 1, 0), TileManager.TileType.Slope);
            SetTile(tileManager, Vector3Int.zero, TileManager.TileType.Slope);
            SetTile(tileManager, Vector3Int.right, TileManager.TileType.Slope);
            SetTile(tileManager, new Vector3Int(2, 0, 0), TileManager.TileType.Floor);
            SetTile(tileManager, new Vector3Int(1, 3, 0), TileManager.TileType.Floor);

            Vector3 highSeam = ResolveVisualSurface(
                controller,
                new Vector3Int(-1, 1, 0),
                new Vector3(-0.5f, 0f, 0.5f));
            Vector3 lowSeam = ResolveVisualSurface(
                controller,
                new Vector3Int(2, 0, 0),
                new Vector3(1.5f, 0f, 0.5f));
            Vector3 highSeamFromSlope = ResolveVisualSurface(
                controller,
                new Vector3Int(0, 1, 0),
                new Vector3(-0.5f, 0f, 0.5f));
            Vector3 lowSeamFromSlope = ResolveVisualSurface(
                controller,
                Vector3Int.right,
                new Vector3(1.5f, 0f, 0.5f));

            Assert.That(highSeam.y, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(lowSeam.y, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(highSeamFromSlope.y, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(lowSeamFromSlope.y, Is.EqualTo(0.25f).Within(0.0001f));

            ExecutedMovementSegment highTransition = CreateSlopeSegment(
                new Vector3Int(-1, 1, 0),
                new Vector3Int(0, 1, 0));
            ExecutedMovementSegment lowTransition = CreateSlopeSegment(
                Vector3Int.right,
                new Vector3Int(2, 0, 0));

            Assert.That(
                TryGetSlopeRouteSurfaceHeights(controller, highTransition, out float highStart, out float highEnd),
                Is.True);
            Assert.That(highStart, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(highEnd, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                TryGetSlopeRouteSurfaceHeights(controller, lowTransition, out float lowStart, out float lowEnd),
                Is.True);
            Assert.That(lowStart, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(lowEnd, Is.Zero);

            SetPrivateField(unitMovement, "standingOffset", new Vector3(0f, 0.2f, 0f));
            Assert.That(
                TryGetSlopeRouteSurfaceHeights(controller, lowTransition, out lowStart, out lowEnd),
                Is.True);
            Assert.That(lowStart, Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(lowEnd, Is.EqualTo(0.2f).Within(0.0001f));

            SetPrivateField(controller, "slopeVisualizationHeightOffsetInCells", -0.5f);
            Vector3 negativeOffsetLowSeam = ResolveVisualSurface(
                controller,
                new Vector3Int(2, 0, 0),
                new Vector3(1.5f, 0f, 0.5f));
            Assert.That(negativeOffsetLowSeam.y, Is.EqualTo(-0.25f).Within(0.0001f));
        }

        [Test]
        public void PartialSlopeRouteSectionsShareTheProjectedHeightAtTheirJoin()
        {
            TileManager tileManager = CreateGameObject("Tile Manager").AddComponent<TileManager>();
            UnitMovement unitMovement = CreateGameObject("Unit Movement").AddComponent<UnitMovement>();
            MovementVisualizationController controller =
                CreateGameObject("Movement Visualization").AddComponent<MovementVisualizationController>();
            SetPrivateField(unitMovement, "tileManager", tileManager);
            SetPrivateField(controller, "tileManager", tileManager);
            SetPrivateField(controller, "selectedUnit", unitMovement);
            SetPrivateField(controller, "routeSurfaceOffset", 0f);

            SetTile(tileManager, new Vector3Int(0, 1, 0), TileManager.TileType.Slope);
            SetTile(tileManager, Vector3Int.zero, TileManager.TileType.Slope);
            SetTile(tileManager, Vector3Int.right, TileManager.TileType.Slope);
            ExecutedMovementSegment slopeSegment = CreateSlopeSegment(
                new Vector3Int(0, 1, 0),
                Vector3Int.zero,
                Vector3Int.right);

            ClearRouteMesh(controller);
            AppendRouteSection(controller, slopeSegment, 0f, 0.5f);
            List<Vector3> vertices = GetRouteMeshVertices(controller);
            float firstSectionEndHeight = vertices[vertices.Count - 2].y;
            int firstSectionVertexCount = vertices.Count;
            AppendRouteSection(controller, slopeSegment, 0.5f, 1f);
            vertices = GetRouteMeshVertices(controller);
            float secondSectionStartHeight = vertices[firstSectionVertexCount].y;

            Assert.That(firstSectionEndHeight, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(secondSectionStartHeight, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [Test]
        public void NonSlopeRouteSectionsUseTheConfiguredVerticalOffset()
        {
            UnitMovement unitMovement = CreateGameObject("Unit Movement").AddComponent<UnitMovement>();
            MovementVisualizationController controller =
                CreateGameObject("Movement Visualization").AddComponent<MovementVisualizationController>();
            SetPrivateField(controller, "selectedUnit", unitMovement);
            SetPrivateField(controller, "routeSurfaceOffset", -0.25f);
            MovementPresentationSegmentType[] specialRouteTypes =
            {
                MovementPresentationSegmentType.Jump,
                MovementPresentationSegmentType.Drop
            };

            for (int typeIndex = 0; typeIndex < specialRouteTypes.Length; typeIndex++)
            {
                ExecutedMovementSegment segment = CreateSegment(
                    specialRouteTypes[typeIndex],
                    Vector3Int.zero,
                    new Vector3Int(1, 1, 0));
                ClearRouteMesh(controller);
                AppendRouteSection(controller, segment, 0f, 1f);

                List<Vector3> vertices = GetRouteMeshVertices(controller);
                float expectedStartHeight =
                    unitMovement.SampleExecutedSegmentPosition(segment, 0f).y - 0.25f;
                Assert.That(vertices[0].y, Is.EqualTo(expectedStartHeight).Within(0.0001f));
            }
        }

        private GameObject CreateGameObject(string objectName)
        {
            GameObject gameObject = new GameObject(objectName);
            createdObjects.Add(gameObject);
            return gameObject;
        }

        private static void SetTile(
            TileManager tileManager,
            Vector3Int coordinate,
            TileManager.TileType tileType)
        {
            FieldInfo tilesField = typeof(TileManager).GetField(
                "tiles",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Dictionary<Vector3Int, TileManager.TileType> tiles =
                (Dictionary<Vector3Int, TileManager.TileType>)tilesField.GetValue(tileManager);
            tiles[coordinate] = tileType;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}'.");
            field.SetValue(target, value);
        }

        private static Vector3 ResolveVisualSurface(
            MovementVisualizationController controller,
            Vector3Int referenceCoordinate,
            Vector3 samplePosition)
        {
            MethodInfo method = typeof(MovementVisualizationController).GetMethod(
                "ResolveVisualSurface",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (Vector3)method.Invoke(
                controller,
                new object[] { referenceCoordinate, samplePosition, samplePosition });
        }

        private static ExecutedMovementSegment CreateSlopeSegment(
            params Vector3Int[] coordinates)
        {
            return CreateSegment(MovementPresentationSegmentType.SlopeRun, coordinates);
        }

        private static ExecutedMovementSegment CreateSegment(
            MovementPresentationSegmentType type,
            params Vector3Int[] coordinates)
        {
            MovementPresentationSegment presentation = new MovementPresentationSegment(
                type,
                0,
                coordinates.Length - 1,
                coordinates);
            return new ExecutedMovementSegment(
                presentation,
                (Vector3)coordinates[0],
                (Vector3)coordinates[coordinates.Length - 1],
                coordinates);
        }

        private static void ClearRouteMesh(MovementVisualizationController controller)
        {
            MethodInfo method = typeof(MovementVisualizationController).GetMethod(
                "BeginMesh",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(controller, null);
        }

        private static void AppendRouteSection(
            MovementVisualizationController controller,
            ExecutedMovementSegment segment,
            float startProgress,
            float endProgress)
        {
            MethodInfo method = typeof(MovementVisualizationController).GetMethod(
                "AppendSampledRouteSection",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(controller, new object[] { segment, startProgress, endProgress, Color.white });
        }

        private static List<Vector3> GetRouteMeshVertices(MovementVisualizationController controller)
        {
            FieldInfo field = typeof(MovementVisualizationController).GetField(
                "meshVertices",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (List<Vector3>)field.GetValue(controller);
        }

        private static bool TryGetSlopeRouteSurfaceHeights(
            MovementVisualizationController controller,
            ExecutedMovementSegment segment,
            out float startSurfaceHeight,
            out float endSurfaceHeight)
        {
            MethodInfo method = typeof(MovementVisualizationController).GetMethod(
                "TryGetSlopeRouteSurfaceHeights",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { segment, 0f, 0f };
            bool result = (bool)method.Invoke(controller, arguments);
            startSurfaceHeight = (float)arguments[1];
            endSurfaceHeight = (float)arguments[2];
            return result;
        }
    }
}
