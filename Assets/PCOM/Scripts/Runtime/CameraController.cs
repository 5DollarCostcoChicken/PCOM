using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace PCOM
{
    public class CameraController : MonoBehaviour
    {
        private const float MinimumSpeed = 0f;
        private const float MinimumFocalDistance = 0.01f;
        private const float MinimumSmoothingTime = 0.01f;
        private const float MotionStopThreshold = 0.0001f;

        [Header("Movement")]
        [SerializeField, Min(MinimumSpeed)] private float movementSpeed = 10f;
        [SerializeField, Min(MinimumSmoothingTime)] private float movementAccelerationTime = 0.12f;
        [SerializeField, Min(MinimumSmoothingTime)] private float movementDecelerationTime = 0.18f;
        [SerializeField, Min(MinimumSpeed)] private float elevationSpeed = 2f;
        [SerializeField, Min(MinimumSmoothingTime)] private float elevationSmoothingTime = 0.15f;

        [Header("Rotation")]
        [SerializeField, Min(MinimumSpeed)] private float rotationSpeed = 90f;
        [SerializeField, Min(MinimumFocalDistance)] private float focalDistance = 10f;

        private Vector3 focalPoint;
        private Vector3 horizontalVelocity;
        private Vector3 horizontalVelocitySmoothing;
        private float currentElevationOffset;
        private float targetElevationOffset;
        private float elevationSmoothingVelocity;

        private void Awake()
        {
            focalPoint = transform.position + (transform.forward * focalDistance);
        }

        private void Update()
        {
            HandleHorizontalMovement();
            HandleRotation();
            HandleElevation();
        }

        private void HandleHorizontalMovement()
        {
            Keyboard keyboard = Keyboard.current;
            Vector2 movementInput = Vector2.zero;
            if (keyboard != null)
            {
                movementInput.x = GetKeyAxis(keyboard.aKey, keyboard.dKey);
                movementInput.y = GetKeyAxis(keyboard.sKey, keyboard.wKey);
                movementInput = Vector2.ClampMagnitude(movementInput, 1f);
            }

            Vector3 cameraForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 cameraRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            Vector3 targetVelocity = ((cameraRight * movementInput.x) + (cameraForward * movementInput.y))
                                     * movementSpeed;
            float smoothingTime = movementInput.sqrMagnitude > 0f
                ? movementAccelerationTime
                : movementDecelerationTime;

            horizontalVelocity = Vector3.SmoothDamp(
                horizontalVelocity,
                targetVelocity,
                ref horizontalVelocitySmoothing,
                smoothingTime,
                Mathf.Infinity,
                Time.deltaTime);

            if (targetVelocity == Vector3.zero &&
                horizontalVelocity.sqrMagnitude < MotionStopThreshold * MotionStopThreshold)
            {
                horizontalVelocity = Vector3.zero;
                horizontalVelocitySmoothing = Vector3.zero;
            }

            TranslateCameraAndFocalPoint(horizontalVelocity * Time.deltaTime);
        }

        private void HandleRotation()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            float rotationInput = GetKeyAxis(keyboard.eKey, keyboard.qKey);
            float rotationDegrees = rotationInput * rotationSpeed * Time.deltaTime;
            transform.RotateAround(focalPoint, Vector3.up, rotationDegrees);
        }

        private void HandleElevation()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                float scrollInput = mouse.scroll.ReadValue().y;
                if (!Mathf.Approximately(scrollInput, 0f))
                {
                    targetElevationOffset -= Mathf.Sign(scrollInput) * elevationSpeed;
                }
            }

            float previousElevationOffset = currentElevationOffset;
            currentElevationOffset = Mathf.SmoothDamp(
                currentElevationOffset,
                targetElevationOffset,
                ref elevationSmoothingVelocity,
                elevationSmoothingTime,
                Mathf.Infinity,
                Time.deltaTime);

            if (Mathf.Abs(targetElevationOffset - currentElevationOffset) < MotionStopThreshold &&
                Mathf.Abs(elevationSmoothingVelocity) < MotionStopThreshold)
            {
                currentElevationOffset = targetElevationOffset;
                elevationSmoothingVelocity = 0f;
            }

            float elevationChange = currentElevationOffset - previousElevationOffset;
            Vector3 translation = Vector3.up * elevationChange;
            TranslateCameraAndFocalPoint(translation);
        }

        private void TranslateCameraAndFocalPoint(Vector3 translation)
        {
            transform.position += translation;
            focalPoint += translation;
        }

        private static float GetKeyAxis(KeyControl negativeKey, KeyControl positiveKey)
        {
            return (positiveKey.isPressed ? 1f : 0f) - (negativeKey.isPressed ? 1f : 0f);
        }

        private void OnValidate()
        {
            movementSpeed = Mathf.Max(MinimumSpeed, movementSpeed);
            movementAccelerationTime = Mathf.Max(MinimumSmoothingTime, movementAccelerationTime);
            movementDecelerationTime = Mathf.Max(MinimumSmoothingTime, movementDecelerationTime);
            elevationSpeed = Mathf.Max(MinimumSpeed, elevationSpeed);
            elevationSmoothingTime = Mathf.Max(MinimumSmoothingTime, elevationSmoothingTime);
            rotationSpeed = Mathf.Max(MinimumSpeed, rotationSpeed);
            focalDistance = Mathf.Max(MinimumFocalDistance, focalDistance);
        }
    }
}
