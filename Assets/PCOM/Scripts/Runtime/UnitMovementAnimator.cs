using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PCOM
{
    /// <summary>
    /// Presents UnitMovement events on an Animator without altering authoritative movement.
    /// Clips are played directly through a two-input PlayableGraph, so no Animator Controller
    /// state names or trigger parameters are required.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitMovementAnimator : MonoBehaviour
    {
        private const float MinimumPositiveSpeed = 0.0001f;

        [Header("Required References")]
        [SerializeField] private UnitMovement unitMovement;
        [SerializeField] private Animator animator;

        [Header("Mandatory Animation Clips")]
        [SerializeField, Tooltip("Loop used whenever the unit is stationary.")]
        private AnimationClip idleClip;
        [SerializeField, Tooltip("Fallback loop for every ordinary and sloped run.")]
        private AnimationClip runClip;

        [Header("Optional Locomotion Nuance")]
        [SerializeField] private AnimationClip runStartClip;
        [SerializeField] private AnimationClip runStopClip;
        [SerializeField] private AnimationClip uphillRunClip;
        [SerializeField] private AnimationClip downhillRunClip;
        [SerializeField] private AnimationClip turnLeftClip;
        [SerializeField] private AnimationClip turnRightClip;
        [SerializeField] private AnimationClip aboutFaceClip;

        [Header("Optional Jump and Clamber Clips")]
        [SerializeField] private AnimationClip jumpAnticipationClip;
        [SerializeField] private AnimationClip jumpAirborneClip;
        [SerializeField] private AnimationClip jumpLandingClip;
        [SerializeField] private AnimationClip clamberClip;

        [Header("Optional Drop Clips")]
        [SerializeField] private AnimationClip dropPreparationClip;
        [SerializeField] private AnimationClip fallLoopClip;
        [SerializeField] private AnimationClip dropLandingClip;

        [Header("Optional Ladder Clips")]
        [SerializeField] private AnimationClip ladderPreparationClip;
        [SerializeField] private AnimationClip ladderClimbClip;
        [SerializeField] private AnimationClip ladderDismountClip;

        [Header("Crossfades")]
        [SerializeField, Min(0f)] private float defaultCrossFadeDuration = 0.12f;
        [SerializeField, Min(0f)] private float turnCrossFadeDuration = 0.08f;
        [SerializeField, Min(0f)] private float actionCrossFadeDuration = 0.08f;
        [SerializeField, Range(90f, 180f)] private float aboutFaceThreshold = 135f;

        [Header("Playback")]
        [SerializeField, Min(MinimumPositiveSpeed)] private float locomotionPlaybackSpeed = 1f;
        [SerializeField, Min(MinimumPositiveSpeed)] private float actionPlaybackSpeed = 1f;
        [SerializeField] private bool applyFootIk = true;

        private readonly AnimationClipPlayable[] clipPlayables =
            new AnimationClipPlayable[2];
        private PlayableGraph playableGraph;
        private AnimationMixerPlayable mixer;
        private int activeInputIndex;
        private int fadingFromInputIndex = -1;
        private float fadeElapsed;
        private float fadeDuration;
        private float currentClipElapsed;
        private float currentPlaybackSpeed = 1f;
        private AnimationClip currentClip;
        private AnimationClip automaticFallbackClip;
        private bool currentClipLoops;
        private bool automaticFallbackLoops;
        private bool graphReady;
        private bool subscribed;
        private bool setupErrorLogged;
        private bool wasApplyingRootMotion;
        private bool rootMotionCaptured;
        private bool locomotionActive;
        private AnimationClip activeLocomotionClip;

        public bool IsReady => graphReady;
        public AnimationClip CurrentClip => currentClip;

        private void OnEnable()
        {
            Subscribe();
            TryInitializeAnimation();
        }

        private void Update()
        {
            if (!graphReady)
            {
                return;
            }

            float deltaTime = Mathf.Max(0f, Time.deltaTime);
            UpdateCrossFade(deltaTime);
            UpdateClipTime(deltaTime);
        }

        private void OnDisable()
        {
            Unsubscribe();
            DestroyGraph();
        }

        /// <summary>
        /// Creates the clip graph when the two mandatory clips and references are assigned.
        /// Missing optional clips are deliberately accepted and use mandatory fallbacks.
        /// </summary>
        public bool TryInitializeAnimation()
        {
            Subscribe();
            if (graphReady)
            {
                return true;
            }

            if (unitMovement == null || animator == null || idleClip == null || runClip == null)
            {
                LogSetupErrorOnce();
                return false;
            }

            wasApplyingRootMotion = animator.applyRootMotion;
            rootMotionCaptured = true;
            animator.applyRootMotion = false;
            playableGraph = PlayableGraph.Create($"{name} Movement Animation");
            playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(playableGraph, 2);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(
                playableGraph,
                "Movement Animation",
                animator);
            output.SetSourcePlayable(mixer);
            playableGraph.Play();
            graphReady = true;
            PlayClip(idleClip, true, 0f, null, false, actionPlaybackSpeed);
            return true;
        }

        private void HandleRunBegan(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            activeLocomotionClip = ResolveLocomotionClip(segment);
            bool beginsMovement = !locomotionActive;
            locomotionActive = true;
            if (beginsMovement && runStartClip != null)
            {
                PlayClip(
                    runStartClip,
                    false,
                    defaultCrossFadeDuration,
                    activeLocomotionClip,
                    true,
                    locomotionPlaybackSpeed);
                return;
            }

            PlayLocomotion(defaultCrossFadeDuration);
        }

        private void HandleTurnBegan(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            AnimationClip turnClip = ResolveTurnClip(source, segment);
            if (turnClip != null)
            {
                PlayClip(
                    turnClip,
                    false,
                    turnCrossFadeDuration,
                    null,
                    false,
                    actionPlaybackSpeed);
            }
        }

        private void HandleTurnEnded(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            if (locomotionActive)
            {
                PlayLocomotion(turnCrossFadeDuration);
            }
            else
            {
                PlayIdle(turnCrossFadeDuration);
            }
        }

        private void HandleJumpAnticipation(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            locomotionActive = false;
            PlayOptionalAction(jumpAnticipationClip, false, false);
        }

        private void HandleJumpTakeoff(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            PlayOptionalAction(jumpAirborneClip, true, false);
        }

        private void HandleJumpLanding(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            PlayOptionalAction(jumpLandingClip, false, true);
        }

        private void HandleClamberBegan(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            locomotionActive = false;
            PlayOptionalAction(clamberClip, false, false);
        }

        private void HandleClamberCompleted(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            PlayIdle(actionCrossFadeDuration);
        }

        private void HandleDropPreparation(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            locomotionActive = false;
            PlayOptionalAction(dropPreparationClip, false, false);
        }

        private void HandleDropBegan(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            PlayOptionalAction(fallLoopClip, true, false);
        }

        private void HandleDropLanded(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            AnimationClip landingClip = dropLandingClip != null
                ? dropLandingClip
                : jumpLandingClip;
            PlayOptionalAction(landingClip, false, true);
        }

        private void HandleLadderPreparation(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            locomotionActive = false;
            PlayOptionalAction(ladderPreparationClip, false, false);
        }

        private void HandleLadderClimbBegan(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            PlayOptionalAction(ladderClimbClip, true, false);
        }

        private void HandleLadderClimbCompleted(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            PlayOptionalAction(ladderDismountClip, false, true);
        }

        private void HandleMovementCompleted(UnitMovement source, Vector3Int coordinate)
        {
            StopLocomotionWithNuance();
        }

        private void HandleMovementInterrupted(UnitMovement source, Vector3Int coordinate)
        {
            StopLocomotionWithNuance();
        }

        private void StopLocomotionWithNuance()
        {
            locomotionActive = false;
            activeLocomotionClip = null;
            if (runStopClip != null)
            {
                PlayClip(
                    runStopClip,
                    false,
                    defaultCrossFadeDuration,
                    idleClip,
                    true,
                    locomotionPlaybackSpeed);
            }
            else
            {
                PlayIdle(defaultCrossFadeDuration);
            }
        }

        private AnimationClip ResolveLocomotionClip(MovementPresentationSegment segment)
        {
            if (segment.Type == MovementPresentationSegmentType.SlopeRun)
            {
                int elevationDifference = segment.EndCoordinate.y - segment.StartCoordinate.y;
                if (elevationDifference > 0 && uphillRunClip != null)
                {
                    return uphillRunClip;
                }

                if (elevationDifference < 0 && downhillRunClip != null)
                {
                    return downhillRunClip;
                }
            }

            return runClip;
        }

        private AnimationClip ResolveTurnClip(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            Vector3 desiredDirection = ResolveWorldDirection(source, segment);
            if (desiredDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                return null;
            }

            float signedAngle = Vector3.SignedAngle(
                source.transform.forward,
                desiredDirection,
                Vector3.up);
            if (Mathf.Abs(signedAngle) >= aboutFaceThreshold && aboutFaceClip != null)
            {
                return aboutFaceClip;
            }

            return signedAngle < 0f ? turnLeftClip : turnRightClip;
        }

        private static Vector3 ResolveWorldDirection(
            UnitMovement source,
            MovementPresentationSegment segment)
        {
            ExecutedMovementPath executedPath = source.ActiveExecutedPath;
            if (executedPath != null)
            {
                for (int segmentIndex = 0;
                     segmentIndex < executedPath.Segments.Count;
                     segmentIndex++)
                {
                    ExecutedMovementSegment executedSegment =
                        executedPath.Segments[segmentIndex];
                    if (!ReferenceEquals(executedSegment.PresentationSegment, segment))
                    {
                        continue;
                    }

                    Vector3 worldDirection =
                        executedSegment.EndWorldPosition - executedSegment.StartWorldPosition;
                    worldDirection.y = 0f;
                    return worldDirection.normalized;
                }
            }

            Vector3 gridDirection = segment.EndCoordinate - segment.StartCoordinate;
            gridDirection.y = 0f;
            return gridDirection.normalized;
        }

        private void PlayLocomotion(float crossFadeDuration)
        {
            AnimationClip locomotionClip = activeLocomotionClip != null
                ? activeLocomotionClip
                : runClip;
            PlayClip(
                locomotionClip,
                true,
                crossFadeDuration,
                null,
                false,
                locomotionPlaybackSpeed);
        }

        private void PlayIdle(float crossFadeDuration)
        {
            PlayClip(
                idleClip,
                true,
                crossFadeDuration,
                null,
                false,
                actionPlaybackSpeed);
        }

        private void PlayOptionalAction(
            AnimationClip optionalClip,
            bool loop,
            bool returnToIdleAutomatically)
        {
            if (optionalClip == null)
            {
                PlayIdle(actionCrossFadeDuration);
                return;
            }

            PlayClip(
                optionalClip,
                loop,
                actionCrossFadeDuration,
                returnToIdleAutomatically ? idleClip : null,
                returnToIdleAutomatically,
                actionPlaybackSpeed);
        }

        private void PlayClip(
            AnimationClip clip,
            bool loop,
            float requestedFadeDuration,
            AnimationClip fallbackClip,
            bool fallbackLoops,
            float playbackSpeed)
        {
            if (!graphReady || clip == null)
            {
                return;
            }

            playbackSpeed = Mathf.Max(MinimumPositiveSpeed, playbackSpeed);
            if (currentClip == clip && currentClipLoops == loop)
            {
                automaticFallbackClip = fallbackClip;
                automaticFallbackLoops = fallbackLoops;
                currentPlaybackSpeed = playbackSpeed;
                if (clipPlayables[activeInputIndex].IsValid())
                {
                    clipPlayables[activeInputIndex].SetSpeed(playbackSpeed);
                }
                return;
            }

            int targetInputIndex = currentClip == null ? activeInputIndex : 1 - activeInputIndex;
            DestroyInput(targetInputIndex);
            AnimationClipPlayable playable = AnimationClipPlayable.Create(playableGraph, clip);
            playable.SetApplyFootIK(applyFootIk);
            playable.SetSpeed(playbackSpeed);
            playable.SetTime(0d);
            playableGraph.Connect(playable, 0, mixer, targetInputIndex);
            clipPlayables[targetInputIndex] = playable;

            float clampedFadeDuration = Mathf.Max(0f, requestedFadeDuration);
            if (currentClip == null || clampedFadeDuration <= 0f)
            {
                DestroyInput(activeInputIndex == targetInputIndex ? 1 - targetInputIndex : activeInputIndex);
                activeInputIndex = targetInputIndex;
                fadingFromInputIndex = -1;
                mixer.SetInputWeight(activeInputIndex, 1f);
            }
            else
            {
                fadingFromInputIndex = activeInputIndex;
                activeInputIndex = targetInputIndex;
                fadeElapsed = 0f;
                fadeDuration = clampedFadeDuration;
                mixer.SetInputWeight(fadingFromInputIndex, 1f);
                mixer.SetInputWeight(activeInputIndex, 0f);
            }

            currentClip = clip;
            currentClipLoops = loop;
            currentClipElapsed = 0f;
            currentPlaybackSpeed = playbackSpeed;
            automaticFallbackClip = fallbackClip;
            automaticFallbackLoops = fallbackLoops;
        }

        private void UpdateCrossFade(float deltaTime)
        {
            if (fadingFromInputIndex < 0)
            {
                return;
            }

            fadeElapsed += deltaTime;
            float weight = fadeDuration <= 0f
                ? 1f
                : Mathf.Clamp01(fadeElapsed / fadeDuration);
            mixer.SetInputWeight(fadingFromInputIndex, 1f - weight);
            mixer.SetInputWeight(activeInputIndex, weight);
            if (weight < 1f)
            {
                return;
            }

            DestroyInput(fadingFromInputIndex);
            fadingFromInputIndex = -1;
        }

        private void UpdateClipTime(float deltaTime)
        {
            if (currentClip == null || !clipPlayables[activeInputIndex].IsValid())
            {
                return;
            }

            currentClipElapsed += deltaTime * currentPlaybackSpeed;
            float clipLength = currentClip.length;
            if (clipLength <= 0f)
            {
                return;
            }

            if (currentClipLoops)
            {
                if (currentClipElapsed >= clipLength)
                {
                    currentClipElapsed %= clipLength;
                    clipPlayables[activeInputIndex].SetTime(currentClipElapsed);
                }
                return;
            }

            if (currentClipElapsed < clipLength || automaticFallbackClip == null)
            {
                return;
            }

            AnimationClip fallback = automaticFallbackClip;
            bool fallbackLoops = automaticFallbackLoops;
            automaticFallbackClip = null;
            PlayClip(
                fallback,
                fallbackLoops,
                defaultCrossFadeDuration,
                null,
                false,
                fallback == runClip || fallback == uphillRunClip || fallback == downhillRunClip
                    ? locomotionPlaybackSpeed
                    : actionPlaybackSpeed);
        }

        private void DestroyInput(int inputIndex)
        {
            if (!graphReady || inputIndex < 0 || inputIndex >= clipPlayables.Length)
            {
                return;
            }

            if (mixer.IsValid())
            {
                mixer.DisconnectInput(inputIndex);
            }

            if (clipPlayables[inputIndex].IsValid())
            {
                playableGraph.DestroyPlayable(clipPlayables[inputIndex]);
                clipPlayables[inputIndex] = default;
            }
        }

        private void DestroyGraph()
        {
            if (graphReady && playableGraph.IsValid())
            {
                playableGraph.Destroy();
            }

            if (rootMotionCaptured && animator != null)
            {
                animator.applyRootMotion = wasApplyingRootMotion;
            }

            clipPlayables[0] = default;
            clipPlayables[1] = default;
            currentClip = null;
            automaticFallbackClip = null;
            activeLocomotionClip = null;
            activeInputIndex = 0;
            fadingFromInputIndex = -1;
            graphReady = false;
            locomotionActive = false;
            rootMotionCaptured = false;
        }

        private void Subscribe()
        {
            if (subscribed || unitMovement == null)
            {
                return;
            }

            unitMovement.RunBegan += HandleRunBegan;
            unitMovement.TurnBegan += HandleTurnBegan;
            unitMovement.TurnEnded += HandleTurnEnded;
            unitMovement.JumpAnticipationBegan += HandleJumpAnticipation;
            unitMovement.JumpTakeoff += HandleJumpTakeoff;
            unitMovement.JumpLanding += HandleJumpLanding;
            unitMovement.ClamberBegan += HandleClamberBegan;
            unitMovement.ClamberCompleted += HandleClamberCompleted;
            unitMovement.DropPreparationBegan += HandleDropPreparation;
            unitMovement.DropBegan += HandleDropBegan;
            unitMovement.DropLanded += HandleDropLanded;
            unitMovement.LadderPreparationBegan += HandleLadderPreparation;
            unitMovement.LadderClimbBegan += HandleLadderClimbBegan;
            unitMovement.LadderClimbCompleted += HandleLadderClimbCompleted;
            unitMovement.MovementCompleted += HandleMovementCompleted;
            unitMovement.MovementInterrupted += HandleMovementInterrupted;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || unitMovement == null)
            {
                return;
            }

            unitMovement.RunBegan -= HandleRunBegan;
            unitMovement.TurnBegan -= HandleTurnBegan;
            unitMovement.TurnEnded -= HandleTurnEnded;
            unitMovement.JumpAnticipationBegan -= HandleJumpAnticipation;
            unitMovement.JumpTakeoff -= HandleJumpTakeoff;
            unitMovement.JumpLanding -= HandleJumpLanding;
            unitMovement.ClamberBegan -= HandleClamberBegan;
            unitMovement.ClamberCompleted -= HandleClamberCompleted;
            unitMovement.DropPreparationBegan -= HandleDropPreparation;
            unitMovement.DropBegan -= HandleDropBegan;
            unitMovement.DropLanded -= HandleDropLanded;
            unitMovement.LadderPreparationBegan -= HandleLadderPreparation;
            unitMovement.LadderClimbBegan -= HandleLadderClimbBegan;
            unitMovement.LadderClimbCompleted -= HandleLadderClimbCompleted;
            unitMovement.MovementCompleted -= HandleMovementCompleted;
            unitMovement.MovementInterrupted -= HandleMovementInterrupted;
            subscribed = false;
        }

        private void LogSetupErrorOnce()
        {
            if (setupErrorLogged)
            {
                return;
            }

            setupErrorLogged = true;
            Debug.LogError(
                $"{nameof(UnitMovementAnimator)} on '{name}' requires UnitMovement, Animator, " +
                "Idle Clip, and Run Clip assignments. Optional animation clips may be empty.",
                this);
        }

        private void OnValidate()
        {
            defaultCrossFadeDuration = Mathf.Max(0f, defaultCrossFadeDuration);
            turnCrossFadeDuration = Mathf.Max(0f, turnCrossFadeDuration);
            actionCrossFadeDuration = Mathf.Max(0f, actionCrossFadeDuration);
            aboutFaceThreshold = Mathf.Clamp(aboutFaceThreshold, 90f, 180f);
            locomotionPlaybackSpeed = ValidatePositiveSpeed(locomotionPlaybackSpeed);
            actionPlaybackSpeed = ValidatePositiveSpeed(actionPlaybackSpeed);
        }

        private static float ValidatePositiveSpeed(float value)
        {
            return value >= MinimumPositiveSpeed &&
                   !float.IsNaN(value) &&
                   !float.IsInfinity(value)
                ? value
                : 1f;
        }
    }
}
