using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PCOM.Tests
{
    public class UnitMovementAnimatorTests
    {
        private GameObject testObject;
        private AnimationClip idleClip;
        private AnimationClip runClip;
        private AnimationClip slowRunClip;
        private AnimationClip slowStopClip;
        private AnimationClip shortDropLandingClip;

        [TearDown]
        public void TearDown()
        {
            if (testObject != null)
            {
                Object.DestroyImmediate(testObject);
            }

            if (idleClip != null)
            {
                Object.DestroyImmediate(idleClip);
            }

            if (runClip != null)
            {
                Object.DestroyImmediate(runClip);
            }

            if (slowRunClip != null)
            {
                Object.DestroyImmediate(slowRunClip);
            }

            if (slowStopClip != null)
            {
                Object.DestroyImmediate(slowStopClip);
            }

            if (shortDropLandingClip != null)
            {
                Object.DestroyImmediate(shortDropLandingClip);
            }
        }

        [Test]
        public void MandatoryClipsInitializeWhileEveryOptionalClipIsEmpty()
        {
            testObject = new GameObject("Movement Animator Test");
            testObject.SetActive(false);
            Animator animator = testObject.AddComponent<Animator>();
            UnitMovement movement = testObject.AddComponent<UnitMovement>();
            UnitMovementAnimator movementAnimator =
                testObject.AddComponent<UnitMovementAnimator>();
            idleClip = new AnimationClip { name = "Idle" };
            runClip = new AnimationClip { name = "Run" };
            SetField(movementAnimator, "unitMovement", movement);
            SetField(movementAnimator, "animator", animator);
            SetField(movementAnimator, "idleClip", idleClip);
            SetField(movementAnimator, "runClip", runClip);

            testObject.SetActive(true);

            Assert.That(movementAnimator.IsReady, Is.True);
            Assert.That(movementAnimator.CurrentClip, Is.SameAs(idleClip));

            MovementPresentationSegment segment = new MovementPresentationSegment(
                MovementPresentationSegmentType.FlatRun,
                0,
                1,
                new[] { Vector3Int.zero, Vector3Int.right });
            InvokePrivate(movementAnimator, "HandleRunBegan", movement, segment);

            Assert.That(movementAnimator.CurrentClip, Is.SameAs(runClip));
            Assert.DoesNotThrow(() =>
                InvokePrivate(movementAnimator, "HandleTurnBegan", movement, segment));
            Assert.That(movementAnimator.CurrentClip, Is.SameAs(runClip));
        }

        [Test]
        public void SlowRouteAndOneCellDropUseTheirNuanceClips()
        {
            testObject = new GameObject("Movement Animator Nuance Test");
            testObject.SetActive(false);
            Animator animator = testObject.AddComponent<Animator>();
            UnitMovement movement = testObject.AddComponent<UnitMovement>();
            UnitMovementAnimator movementAnimator =
                testObject.AddComponent<UnitMovementAnimator>();
            idleClip = new AnimationClip { name = "Idle" };
            runClip = new AnimationClip { name = "Run" };
            slowRunClip = new AnimationClip { name = "Slow Run" };
            slowStopClip = new AnimationClip { name = "Slow Stop" };
            shortDropLandingClip = new AnimationClip { name = "Short Drop Landing" };
            SetField(movementAnimator, "unitMovement", movement);
            SetField(movementAnimator, "animator", animator);
            SetField(movementAnimator, "idleClip", idleClip);
            SetField(movementAnimator, "runClip", runClip);
            SetField(movementAnimator, "slowRunClip", slowRunClip);
            SetField(movementAnimator, "slowMovementStopClip", slowStopClip);
            SetField(movementAnimator, "shortDropLandingClip", shortDropLandingClip);
            SetMovementField(movement, "isUsingSlowMovement", true);
            testObject.SetActive(true);

            GridPathResult shortPath = GridPathResult.Success(
                new[] { Vector3Int.zero, Vector3Int.right },
                1f,
                1);
            InvokePrivate(movementAnimator, "HandleMovementStarted", movement, shortPath);
            MovementPresentationSegment runSegment = new MovementPresentationSegment(
                MovementPresentationSegmentType.FlatRun,
                0,
                1,
                shortPath.Coordinates);
            InvokePrivate(movementAnimator, "HandleRunBegan", movement, runSegment);
            Assert.That(movementAnimator.CurrentClip, Is.SameAs(slowRunClip));

            InvokePrivate(
                movementAnimator,
                "HandleMovementCompleted",
                movement,
                Vector3Int.right);
            Assert.That(movementAnimator.CurrentClip, Is.SameAs(slowStopClip));

            MovementPresentationSegment shortDrop = new MovementPresentationSegment(
                MovementPresentationSegmentType.Drop,
                0,
                1,
                new[] { new Vector3Int(0, 1, 0), Vector3Int.right });
            InvokePrivate(movementAnimator, "HandleDropLanded", movement, shortDrop);
            Assert.That(movementAnimator.CurrentClip, Is.SameAs(shortDropLandingClip));
            InvokePrivate(
                movementAnimator,
                "HandleMovementCompleted",
                movement,
                Vector3Int.right);
            Assert.That(movementAnimator.CurrentClip, Is.SameAs(shortDropLandingClip));

            SetField(movementAnimator, "shortDropLandingMaximumHeight", 2);
            MovementPresentationSegment configuredTwoCellDrop =
                new MovementPresentationSegment(
                    MovementPresentationSegmentType.Drop,
                    0,
                    1,
                    new[] { new Vector3Int(0, 2, 0), Vector3Int.right });
            InvokePrivate(
                movementAnimator,
                "HandleDropLanded",
                movement,
                configuredTwoCellDrop);
            Assert.That(movementAnimator.CurrentClip, Is.SameAs(shortDropLandingClip));

            SetField(movementAnimator, "clamberClip", slowRunClip);
            InvokePrivate(
                movementAnimator,
                "HandleLadderClimbCompleted",
                movement,
                configuredTwoCellDrop);
            InvokePrivate(
                movementAnimator,
                "HandleClamberBegan",
                movement,
                configuredTwoCellDrop);
            Assert.That(movementAnimator.CurrentClip, Is.SameAs(slowRunClip));
        }

        private static void SetField(
            UnitMovementAnimator movementAnimator,
            string fieldName,
            object value)
        {
            FieldInfo field = typeof(UnitMovementAnimator).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}'.");
            field.SetValue(movementAnimator, value);
        }

        private static void InvokePrivate(
            UnitMovementAnimator movementAnimator,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = typeof(UnitMovementAnimator).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Expected method '{methodName}'.");
            method.Invoke(movementAnimator, arguments);
        }

        private static void SetMovementField(
            UnitMovement movement,
            string fieldName,
            object value)
        {
            FieldInfo field = typeof(UnitMovement).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}'.");
            field.SetValue(movement, value);
        }
    }
}
