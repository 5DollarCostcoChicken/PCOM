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
    }
}
