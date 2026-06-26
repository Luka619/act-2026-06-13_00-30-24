using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Serialization;

namespace ActToolkit
{
    public sealed class CombatActor : MonoBehaviour
    {
#if UNITY_EDITOR
        private const string EditorDefaultLocomotionAssetPath = "Assets/External/TestAssets/Animations/PreviewClips/Quaternius_UniversalAnimationLibrary_Standard/UAL1_Standard.fbx";
        private const string EditorDefaultJumpAssetPath = "Assets/External/TestAssets/Animations/PreviewClips/Quaternius_UniversalAnimationLibrary2_Standard/UAL2_Standard.fbx";
        private const string EditorDefaultIdleClipName = "Armature|Idle_Loop";
        private const string EditorDefaultWalkClipName = "Armature|Walk_Loop";
        private const string EditorDefaultMoveClipName = "Armature|Jog_Fwd_Loop";
        private const string EditorDefaultJumpStartClipName = "Armature|NinjaJump_Start";
        private const string EditorDefaultJumpLoopClipName = "Armature|NinjaJump_Idle_Loop";
        private const string EditorDefaultJumpLandClipName = "Armature|NinjaJump_Land";
#endif

        private enum JumpAnimationPhase
        {
            Grounded,
            Start,
            AirLoop,
            Land
        }

        [Header("References")]
        [SerializeField]
        private PlayerCombatGamepadInput input;

        [SerializeField]
        private CharacterActionProfile characterProfile;

        [SerializeField]
        private Animator animator;

        [SerializeField]
        private Avatar fallbackAvatar;

        [SerializeField]
        private CharacterController characterController;

        [SerializeField]
        private Camera gameplayCamera;

        [SerializeField]
        private CombatActionDatabase actionDatabase;

        [Header("Actions")]
        [SerializeField]
        private string firstAttackActionId = "action.light_1";

        [SerializeField, Range(0.05f, 0.75f)]
        private float attackInputBuffer = 0.35f;

        [SerializeField]
        private AnimationClip idleClip;

        [SerializeField]
        private AnimationClip walkClip;

        [SerializeField]
        private AnimationClip moveClip;

        [SerializeField]
        private AnimationClip jumpStartClip;

        [SerializeField]
        private AnimationClip jumpLoopClip;

        [SerializeField]
        private AnimationClip jumpLandClip;

        [Header("Movement")]
        [SerializeField]
        private float moveSpeed = 3.8f;

        [SerializeField]
        private float rotationSpeed = 720f;

        [SerializeField]
        private float gravity = -18f;

        [SerializeField, Min(0.1f)]
        private float jumpHeight = 1.45f;

        [SerializeField]
        private float groundedStickVelocity = -1f;

        [SerializeField, Range(0f, 0.5f)]
        private float minAirTimeForLandingAnimation = 0.12f;

        [SerializeField, Min(0.05f)]
        private float landingAnimationDuration = 0.3f;

        [FormerlySerializedAs("lockLandingMotion")]
        [SerializeField]
        [Tooltip("Consumes player movement, rotation, and combat input during the landing animation.")]
        private bool lockLandingInput;

        [SerializeField, Range(0f, 0.35f)]
        private float locomotionDeadZone = 0.12f;

        [SerializeField, Range(1f, 20f)]
        private float locomotionBlendSpeed = 8f;

        [SerializeField, Range(0.2f, 0.85f)]
        private float locomotionWalkBlendPoint = 0.24f;

        [SerializeField, Min(0.01f)]
        private float locomotionWalkReferenceSpeed = 0.925f;

        [SerializeField, Min(0.01f)]
        private float locomotionMoveReferenceSpeed = 5.309f;

        [SerializeField, Range(0.1f, 3f)]
        private float locomotionPlaybackSpeedMin = 0.35f;

        [SerializeField, Range(0.1f, 3f)]
        private float locomotionPlaybackSpeedMax = 3f;

        [SerializeField]
        [Tooltip("Keeps walk and run on one normalized gait phase so same-leg poses line up while blend weights change.")]
        private bool synchronizeLocomotionCyclePhase = true;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Offset the walk cycle so phase 0 is the chosen same-leg high-knee pose.")]
        private float locomotionWalkCycleOffset;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Offset the run/move cycle so phase 0 is the same leg and pose as the walk high-knee reference.")]
        private float locomotionMoveCycleOffset;

        [SerializeField, Range(0f, 0.35f)]
        private float actionToLocomotionBlendDuration = 0.14f;

        [Header("Hit Detection")]
        [SerializeField]
        private LayerMask hurtboxLayers = ~0;

        [SerializeField]
        private int defaultDamage = 10;

        [SerializeField]
        private bool drawHitboxDebug = true;

        [SerializeField, Range(0.02f, 1f)]
        private float hitboxDebugLinger = 0.18f;

        [SerializeField]
        private bool logCombatEvents = true;

        [SerializeField]
        private bool logAnimationDiagnostics = true;

        [SerializeField]
        private float diagnosticLogInterval = 0.25f;

        private readonly HashSet<HitRecord> hitRecords = new HashSet<HitRecord>();
        private readonly List<CombatAnimationMarker> activeHitboxes = new List<CombatAnimationMarker>();
        private readonly List<HitboxDebugSample> hitboxDebugSamples = new List<HitboxDebugSample>();

        private PlayableGraph graph;
        private AnimationPlayableOutput output;
        private AnimationClipPlayable currentPlayable;
        private AnimationMixerPlayable locomotionMixer;
        private AnimationClipPlayable idlePlayable;
        private AnimationClipPlayable walkPlayable;
        private AnimationClipPlayable movePlayable;
        private AnimationMixerPlayable returnTransitionMixer;
        private AnimationClip currentClip;
        private bool currentClipLoops;
        private float locomotionBlendWeight;
        private float returnTransitionTime;
        private float returnTransitionDuration;
        private double locomotionCyclePhase;
        private double idleLocomotionTime;
        private double walkLocomotionTime;
        private double moveLocomotionTime;
        private JumpAnimationPhase jumpPhase = JumpAnimationPhase.Grounded;
        private float jumpPhaseTime;
        private float airborneTime;
        private bool isGrounded = true;
        private bool wasGrounded = true;
        private float fallbackGroundY;

        private CombatAnimationDefinition currentAction;
        private float currentActionTime;
        private string queuedActionId;
        private string bufferedInputAction = string.Empty;
        private float bufferedAttackUntil = -1f;
        private float verticalVelocity;
        private bool graphReady;
        private float nextDiagnosticLogTime;

        public CombatAnimationDefinition CurrentAction => currentAction;
        public float CurrentActionTime => currentActionTime;
        public bool IsAttacking => currentAction != null;
        public bool IsMovementLocked => currentAction != null && HasActiveMarker(CombatAnimationEventKind.MovementLock);

        public void ConfigureForPlaytest(
            CharacterActionProfile profile,
            PlayerCombatGamepadInput inputComponent,
            CharacterController controller,
            Camera camera,
            Animator targetAnimator,
            Avatar avatarOverride)
        {
            characterProfile = profile;
            input = inputComponent;
            characterController = controller;
            gameplayCamera = camera;
            animator = targetAnimator;

            if (avatarOverride != null)
            {
                fallbackAvatar = avatarOverride;
            }

            ApplyCharacterProfile();

            if (animator != null)
            {
                if (animator.avatar == null && fallbackAvatar != null)
                {
                    animator.avatar = fallbackAvatar;
                }

                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            if (graphReady && graph.IsValid())
            {
                graph.Destroy();
                graphReady = false;
                currentPlayable = default;
                locomotionMixer = default;
                idlePlayable = default;
                walkPlayable = default;
                movePlayable = default;
                returnTransitionMixer = default;
                currentClip = null;
            }

            if (isActiveAndEnabled)
            {
                EnsureGraph();
                PlayMovementAnimation(true, 0f);
            }
        }

        private void Awake()
        {
            ApplyCharacterProfile();
            EnsureProfileModelInstance();

            if (input == null)
            {
                input = GetComponent<PlayerCombatGamepadInput>();
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (animator == null)
            {
                animator = gameObject.AddComponent<Animator>();
                Debug.LogWarning("[CombatActor] No Animator was found under the actor. Added one to the root GameObject.", this);
            }

            if (animator.avatar == null && fallbackAvatar != null)
            {
                animator.avatar = fallbackAvatar;
                Debug.Log("[CombatActor] Assigned fallback Avatar: " + fallbackAvatar.name, this);
            }

            if (animator != null)
            {
                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }

            if (gameplayCamera == null)
            {
                gameplayCamera = Camera.main;
            }

            fallbackGroundY = transform.position.y;
            isGrounded = characterController == null || characterController.isGrounded;
            wasGrounded = isGrounded;

            LogAnimationDiagnostic("Awake", BuildAnimatorDiagnostic());
        }

        private void ApplyCharacterProfile()
        {
            if (characterProfile == null)
            {
                return;
            }

            characterProfile.EnsureDefaults();

            if (characterProfile.comboTable != null)
            {
                actionDatabase = characterProfile.comboTable;
            }

            if (characterProfile.avatar != null)
            {
                fallbackAvatar = characterProfile.avatar;
            }

            if (characterProfile.idleClip != null)
            {
                idleClip = characterProfile.idleClip;
            }

            if (characterProfile.walkClip != null)
            {
                walkClip = characterProfile.walkClip;
            }

            if (characterProfile.moveClip != null)
            {
                moveClip = characterProfile.moveClip;
            }

            if (characterProfile.jumpStartClip != null)
            {
                jumpStartClip = characterProfile.jumpStartClip;
            }

            if (characterProfile.jumpLoopClip != null)
            {
                jumpLoopClip = characterProfile.jumpLoopClip;
            }

            if (characterProfile.jumpLandClip != null)
            {
                jumpLandClip = characterProfile.jumpLandClip;
            }

#if UNITY_EDITOR
            if (idleClip == null)
            {
                idleClip = LoadEditorDefaultClip(EditorDefaultLocomotionAssetPath, EditorDefaultIdleClipName);
            }

            if (walkClip == null)
            {
                walkClip = LoadEditorDefaultClip(EditorDefaultLocomotionAssetPath, EditorDefaultWalkClipName);
            }

            if (moveClip == null)
            {
                moveClip = LoadEditorDefaultClip(EditorDefaultLocomotionAssetPath, EditorDefaultMoveClipName);
            }

            if (jumpStartClip == null)
            {
                jumpStartClip = LoadEditorDefaultClip(EditorDefaultJumpAssetPath, EditorDefaultJumpStartClipName);
            }

            if (jumpLoopClip == null)
            {
                jumpLoopClip = LoadEditorDefaultClip(EditorDefaultJumpAssetPath, EditorDefaultJumpLoopClipName);
            }

            if (jumpLandClip == null)
            {
                jumpLandClip = LoadEditorDefaultClip(EditorDefaultJumpAssetPath, EditorDefaultJumpLandClipName);
            }
#endif
        }

#if UNITY_EDITOR
        private static AnimationClip LoadEditorDefaultClip(string assetPath, string clipName)
        {
            UnityEngine.Object[] assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is AnimationClip clip && clip.name == clipName)
                {
                    return clip;
                }
            }

            return null;
        }
#endif

        private void EnsureProfileModelInstance()
        {
            if (characterProfile == null || characterProfile.modelPrefab == null || GetComponentInChildren<Animator>() != null)
            {
                return;
            }

            GameObject model = Instantiate(characterProfile.modelPrefab, transform);
            model.name = characterProfile.modelPrefab.name;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
        }

        private void OnEnable()
        {
            EnsureGraph();
            fallbackGroundY = transform.position.y;
            isGrounded = characterController == null || characterController.isGrounded;
            wasGrounded = isGrounded;
            if (isGrounded)
            {
                ResetJumpAnimationState();
            }

            PlayMovementAnimation(true, 0f);
        }

        private void OnDisable()
        {
            if (graph.IsValid())
            {
                graph.Destroy();
            }

            graphReady = false;
            currentPlayable = default;
            locomotionMixer = default;
            idlePlayable = default;
            walkPlayable = default;
            movePlayable = default;
            returnTransitionMixer = default;
            currentClip = null;
            locomotionBlendWeight = 0f;
            returnTransitionTime = 0f;
            returnTransitionDuration = 0f;
            locomotionCyclePhase = 0d;
            idleLocomotionTime = 0d;
            walkLocomotionTime = 0d;
            moveLocomotionTime = 0d;
            jumpPhase = JumpAnimationPhase.Grounded;
            jumpPhaseTime = 0f;
            airborneTime = 0f;
            isGrounded = true;
            wasGrounded = true;
        }

        private void Update()
        {
            EnsureGraph();
            HandleAttackInput();
            MoveCharacter(Time.deltaTime);
            UpdateAnimationAndCombat(Time.deltaTime);
        }

        private void EnsureGraph()
        {
            if (graphReady || animator == null)
            {
                return;
            }

            if (animator.avatar == null && AnyAssignedClipRequiresAvatar())
            {
                Debug.LogWarning("[CombatActor] Animator has no Avatar, but at least one assigned clip uses humanoid motion. Humanoid clips may not move the model.", this);
            }

            graph = PlayableGraph.Create(name + "_CombatGraph");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            graph.Play();
            graphReady = true;

            LogAnimationDiagnostic("GraphReady", BuildGraphDiagnostic());
        }

        private bool AnyAssignedClipRequiresAvatar()
        {
            if (ClipRequiresAvatar(idleClip)
                || ClipRequiresAvatar(walkClip)
                || ClipRequiresAvatar(moveClip)
                || ClipRequiresAvatar(jumpStartClip)
                || ClipRequiresAvatar(jumpLoopClip)
                || ClipRequiresAvatar(jumpLandClip))
            {
                return true;
            }

            if (actionDatabase == null || actionDatabase.actions == null)
            {
                return false;
            }

            foreach (CombatAnimationDefinition action in actionDatabase.actions)
            {
                if (action != null && ClipRequiresAvatar(action.clip))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ClipRequiresAvatar(AnimationClip clip)
        {
            return clip != null && clip.humanMotion;
        }

        private void HandleAttackInput()
        {
            if (input == null || IsLandingInputLocked())
            {
                return;
            }

            foreach (string pressedToken in input.PressedInputTokens)
            {
                if (CombatInputActionNames.Matches(pressedToken, CombatInputActionNames.Jump))
                {
                    continue;
                }

                HandlePressedCombatInput(CombatInputActionNames.ComposeInputAction(input.HeldInputTokens, input.Move, pressedToken));
            }
        }

        private void HandlePressedCombatInput(string inputAction)
        {
            if (currentAction == null)
            {
                StartEntryAction(inputAction);
                return;
            }

            bufferedInputAction = CombatInputActionNames.Normalize(inputAction);
            bufferedAttackUntil = Time.time + attackInputBuffer;
            if (TryQueueBufferedCombo("press"))
            {
                return;
            }

            if (logCombatEvents)
            {
                Debug.Log("[CombatActor] Attack buffered until combo window. action="
                    + ActionDisplayName(currentAction)
                    + ", input=" + bufferedInputAction
                    + ", frame=" + CurrentActionFrame()
                    + ", buffer=" + attackInputBuffer.ToString("0.00") + "s",
                    this);
            }
        }

        private void StartEntryAction(string inputAction)
        {
            if (actionDatabase != null && actionDatabase.TryGetEntryAction(inputAction, out string entryActionId))
            {
                StartAction(entryActionId);
                return;
            }

            if (CombatInputActionNames.Matches(inputAction, CombatInputActionNames.LightAttack))
            {
                StartAction(firstAttackActionId);
            }
        }

        private void MoveCharacter(float deltaTime)
        {
            if (input == null)
            {
                return;
            }

            bool landingInputLocked = IsLandingInputLocked();
            Vector2 moveInput = IsMovementLocked || landingInputLocked ? Vector2.zero : EffectiveMoveInput(input.Move);
            Vector3 move = CameraRelativeMove(moveInput);
            bool groundedBeforeMove = IsActorGrounded();

            if (!landingInputLocked && move.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(move, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * deltaTime);
            }

            if (groundedBeforeMove && verticalVelocity < 0f)
            {
                verticalVelocity = groundedStickVelocity;
            }

            if (CanStartJump(groundedBeforeMove))
            {
                verticalVelocity = Mathf.Sqrt(Mathf.Max(0f, -2f * gravity * jumpHeight));
                groundedBeforeMove = false;
                BeginJumpStartAnimation();
            }

            verticalVelocity += gravity * deltaTime;
            Vector3 velocity = move * moveSpeed;
            velocity.y = verticalVelocity;

            if (characterController != null)
            {
                characterController.Move(velocity * deltaTime);
                bool groundedAfterMove = characterController.isGrounded;
                if (groundedAfterMove && verticalVelocity < 0f)
                {
                    verticalVelocity = groundedStickVelocity;
                }

                UpdateAirborneState(groundedAfterMove, deltaTime);
            }
            else
            {
                Vector3 nextPosition = transform.position + velocity * deltaTime;
                bool groundedAfterMove = nextPosition.y <= fallbackGroundY && verticalVelocity <= 0f;
                if (groundedAfterMove)
                {
                    nextPosition.y = fallbackGroundY;
                    verticalVelocity = groundedStickVelocity;
                }

                transform.position = nextPosition;
                UpdateAirborneState(groundedAfterMove, deltaTime);
            }
        }

        private bool CanStartJump(bool groundedBeforeMove)
        {
            return input != null
                && input.JumpPressed
                && groundedBeforeMove
                && jumpPhase != JumpAnimationPhase.Land
                && currentAction == null;
        }

        private bool IsLandingInputLocked()
        {
            return lockLandingInput && jumpPhase == JumpAnimationPhase.Land;
        }

        private bool IsActorGrounded()
        {
            return characterController == null ? isGrounded : characterController.isGrounded;
        }

        private void UpdateAirborneState(bool groundedNow, float deltaTime)
        {
            wasGrounded = isGrounded;
            isGrounded = groundedNow;

            if (!isGrounded)
            {
                airborneTime += Mathf.Max(0f, deltaTime);
                if (jumpPhase == JumpAnimationPhase.Grounded || jumpPhase == JumpAnimationPhase.Land)
                {
                    BeginJumpLoopAnimation(false);
                }

                return;
            }

            if (!wasGrounded && airborneTime >= minAirTimeForLandingAnimation)
            {
                BeginJumpLandAnimation();
            }
            else if (jumpPhase != JumpAnimationPhase.Land)
            {
                ResetJumpAnimationState();
            }

            airborneTime = 0f;
        }

        private Vector3 CameraRelativeMove(Vector2 moveInput)
        {
            if (moveInput.sqrMagnitude <= 0.0001f)
            {
                return Vector3.zero;
            }

            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;
            if (gameplayCamera != null)
            {
                forward = gameplayCamera.transform.forward;
                right = gameplayCamera.transform.right;
                forward.y = 0f;
                right.y = 0f;
                forward.Normalize();
                right.Normalize();
            }

            Vector3 move = right * moveInput.x + forward * moveInput.y;
            return move.sqrMagnitude > 1f ? move.normalized : move;
        }

        private Vector2 EffectiveMoveInput(Vector2 rawInput)
        {
            float magnitude = Mathf.Clamp01(rawInput.magnitude);
            if (magnitude <= locomotionDeadZone)
            {
                return Vector2.zero;
            }

            float effectiveMagnitude = Mathf.InverseLerp(locomotionDeadZone, 1f, magnitude);
            return rawInput.normalized * effectiveMagnitude;
        }

        private void UpdateAnimationAndCombat(float deltaTime)
        {
            activeHitboxes.Clear();
            PruneHitboxDebugSamples();

            if (currentAction == null)
            {
                PlayMovementAnimation(false, deltaTime);
                return;
            }

            currentActionTime += deltaTime;
            float clipLength = currentAction.clip == null ? 0f : currentAction.clip.length;
            ProcessActiveMarkers();
            TryQueueBufferedCombo("buffer");
            LogPlaybackTickDiagnostic(clipLength);

            if (ShouldReturnToLocomotionFromAction(clipLength))
            {
                if (logCombatEvents)
                {
                    Debug.Log("[CombatActor] Return to locomotion from action "
                        + ActionDisplayName(currentAction)
                        + ", frame=" + CurrentActionFrame(),
                        this);
                }

                FinishCurrentAction();
                return;
            }

            if (clipLength <= 0f || currentActionTime >= clipLength)
            {
                FinishCurrentAction();
            }
        }

        private void PlayLocomotionClip(bool force, float deltaTime)
        {
            if (!graphReady || (idleClip == null && walkClip == null && moveClip == null))
            {
                return;
            }

            if (returnTransitionMixer.IsValid())
            {
                UpdateReturnToLocomotionTransition(deltaTime);
                return;
            }

            if (currentPlayable.IsValid())
            {
                graph.DestroyPlayable(currentPlayable);
                currentPlayable = default;
            }

            EnsureLocomotionMixer(force);
            UpdateLocomotionBlend(force, deltaTime);
        }

        private void PlayMovementAnimation(bool force, float deltaTime)
        {
            if (PlayJumpAnimation(force, deltaTime))
            {
                return;
            }

            PlayLocomotionClip(force, deltaTime);
        }

        private bool PlayJumpAnimation(bool force, float deltaTime)
        {
            if (!graphReady || jumpPhase == JumpAnimationPhase.Grounded)
            {
                return false;
            }

            jumpPhaseTime += Mathf.Max(0f, deltaTime);

            if (jumpPhase == JumpAnimationPhase.Start)
            {
                AnimationClip startClip = jumpStartClip != null ? jumpStartClip : jumpLoopClip;
                if (startClip == null)
                {
                    BeginJumpLoopAnimation(true);
                    return PlayJumpAnimation(true, 0f);
                }

                PlayClipIfNeeded(startClip, false, force);
                if (jumpPhaseTime >= Mathf.Max(0.01f, startClip.length))
                {
                    BeginJumpLoopAnimation(true);
                    return PlayJumpAnimation(true, 0f);
                }

                return true;
            }

            if (jumpPhase == JumpAnimationPhase.AirLoop)
            {
                if (jumpLoopClip == null)
                {
                    return false;
                }

                PlayClipIfNeeded(jumpLoopClip, true, force);
                if (currentPlayable.IsValid())
                {
                    currentPlayable.SetSpeed(1d);
                }

                return true;
            }

            if (jumpPhase == JumpAnimationPhase.Land)
            {
                if (jumpLandClip == null)
                {
                    ResetJumpAnimationState();
                    return false;
                }

                float targetDuration = Mathf.Max(0.05f, landingAnimationDuration);
                if (!returnTransitionMixer.IsValid())
                {
                    PlayClipIfNeeded(jumpLandClip, false, force);
                    if (currentPlayable.IsValid())
                    {
                        currentPlayable.SetSpeed(Mathf.Max(0.01f, jumpLandClip.length) / targetDuration);
                    }

                    BeginReturnToLocomotionTransition(targetDuration, false);
                }
                else if (currentPlayable.IsValid())
                {
                    currentPlayable.SetSpeed(Mathf.Max(0.01f, jumpLandClip.length) / targetDuration);
                }

                UpdateReturnToLocomotionTransition(deltaTime);

                if (jumpPhaseTime >= targetDuration)
                {
                    ResetJumpAnimationState();
                    if (returnTransitionMixer.IsValid())
                    {
                        CompleteReturnToLocomotionTransition();
                    }
                }

                return true;
            }

            return false;
        }

        private void PlayClipIfNeeded(AnimationClip clipToPlay, bool loop, bool forceRestart)
        {
            if (clipToPlay == null)
            {
                return;
            }

            if (forceRestart || currentClip != clipToPlay || !currentPlayable.IsValid())
            {
                PlayClip(clipToPlay, loop, forceRestart);
            }
        }

        private void BeginJumpStartAnimation()
        {
            jumpPhase = JumpAnimationPhase.Start;
            jumpPhaseTime = 0f;
            airborneTime = 0f;
            isGrounded = false;
        }

        private void BeginJumpLoopAnimation(bool forceRestart)
        {
            jumpPhase = JumpAnimationPhase.AirLoop;
            if (forceRestart)
            {
                jumpPhaseTime = 0f;
            }
        }

        private void BeginJumpLandAnimation()
        {
            if (jumpPhase == JumpAnimationPhase.Land)
            {
                return;
            }

            jumpPhase = JumpAnimationPhase.Land;
            jumpPhaseTime = 0f;
        }

        private void ResetJumpAnimationState()
        {
            jumpPhase = JumpAnimationPhase.Grounded;
            jumpPhaseTime = 0f;
        }

        private void UpdateLocomotionBlend(bool force, float deltaTime)
        {
            Vector2 effectiveMove = input == null ? Vector2.zero : EffectiveMoveInput(input.Move);
            float targetWeight = Mathf.Clamp01(effectiveMove.magnitude);

            locomotionBlendWeight = force
                ? targetWeight
                : Mathf.MoveTowards(locomotionBlendWeight, targetWeight, Mathf.Max(0.01f, locomotionBlendSpeed) * deltaTime);

            ComputeLocomotionWeights(locomotionBlendWeight, out float idleWeight, out float walkWeight, out float moveWeight);
            float playbackSpeed = ApplyLocomotionPlaybackSpeed(locomotionBlendWeight, walkWeight, moveWeight);

            if (idlePlayable.IsValid())
            {
                locomotionMixer.SetInputWeight(0, idleWeight);
            }

            if (walkPlayable.IsValid())
            {
                locomotionMixer.SetInputWeight(1, walkWeight);
            }

            if (movePlayable.IsValid())
            {
                locomotionMixer.SetInputWeight(2, moveWeight);
            }

            UpdateLocomotionPlayableTimes(deltaTime, playbackSpeed, walkWeight, moveWeight);

            currentClip = SelectDominantLocomotionClip(idleWeight, walkWeight, moveWeight);
            currentClipLoops = true;
        }

        private void ComputeLocomotionWeights(float blendValue, out float idleWeight, out float walkWeight, out float moveWeight)
        {
            float value = Mathf.Clamp01(blendValue);
            float walkPeak = Mathf.Clamp(locomotionWalkBlendPoint, 0.01f, 0.99f);

            if (walkClip == null)
            {
                idleWeight = idleClip == null ? 0f : 1f - value;
                walkWeight = 0f;
                moveWeight = moveClip == null ? 0f : value;
                NormalizeLocomotionWeights(ref idleWeight, ref walkWeight, ref moveWeight);
                return;
            }

            if (moveClip == null)
            {
                idleWeight = idleClip == null ? 0f : 1f - value;
                walkWeight = value;
                moveWeight = 0f;
                NormalizeLocomotionWeights(ref idleWeight, ref walkWeight, ref moveWeight);
                return;
            }

            if (value <= walkPeak)
            {
                float t = value / walkPeak;
                idleWeight = idleClip == null ? 0f : 1f - t;
                walkWeight = t;
                moveWeight = 0f;
            }
            else
            {
                float t = Mathf.InverseLerp(walkPeak, 1f, value);
                idleWeight = 0f;
                walkWeight = 1f - t;
                moveWeight = t;
            }

            NormalizeLocomotionWeights(ref idleWeight, ref walkWeight, ref moveWeight);
        }

        private float ApplyLocomotionPlaybackSpeed(float effectiveMoveMagnitude, float walkWeight, float moveWeight)
        {
            if (idlePlayable.IsValid())
            {
                idlePlayable.SetSpeed(0d);
            }

            float desiredSpeed = Mathf.Max(0f, moveSpeed) * Mathf.Clamp01(effectiveMoveMagnitude);
            float authoredSpeed = walkWeight * Mathf.Max(0.01f, locomotionWalkReferenceSpeed)
                + moveWeight * Mathf.Max(0.01f, locomotionMoveReferenceSpeed);
            float playbackSpeed = authoredSpeed <= 0.001f
                ? 1f
                : desiredSpeed / authoredSpeed;

            float minSpeed = Mathf.Min(locomotionPlaybackSpeedMin, locomotionPlaybackSpeedMax);
            float maxSpeed = Mathf.Max(locomotionPlaybackSpeedMin, locomotionPlaybackSpeedMax);
            playbackSpeed = Mathf.Clamp(playbackSpeed, minSpeed, maxSpeed);

            if (walkPlayable.IsValid())
            {
                walkPlayable.SetSpeed(0d);
            }

            if (movePlayable.IsValid())
            {
                movePlayable.SetSpeed(0d);
            }

            return playbackSpeed;
        }

        private static void NormalizeLocomotionWeights(ref float idleWeight, ref float walkWeight, ref float moveWeight)
        {
            float total = idleWeight + walkWeight + moveWeight;
            if (total > 0.0001f)
            {
                idleWeight /= total;
                walkWeight /= total;
                moveWeight /= total;
            }
        }

        private AnimationClip SelectDominantLocomotionClip(float idleWeight, float walkWeight, float moveWeight)
        {
            if (moveClip != null && moveWeight >= walkWeight && moveWeight >= idleWeight)
            {
                return moveClip;
            }

            if (walkClip != null && walkWeight >= idleWeight)
            {
                return walkClip;
            }

            return idleClip != null ? idleClip : walkClip != null ? walkClip : moveClip;
        }

        private void EnsureLocomotionMixer(bool forceRestart)
        {
            if (locomotionMixer.IsValid())
            {
                if (forceRestart)
                {
                    ResetLocomotionTimes();
                }

                return;
            }

            if (forceRestart)
            {
                ResetLocomotionTimes();
            }

            locomotionMixer = AnimationMixerPlayable.Create(graph, 3);

            if (idleClip != null)
            {
                idlePlayable = AnimationClipPlayable.Create(graph, idleClip);
                ConfigureClipPlayable(idlePlayable, true);
                if (forceRestart)
                {
                    idlePlayable.SetTime(idleLocomotionTime);
                    idlePlayable.SetDone(false);
                }

                graph.Connect(idlePlayable, 0, locomotionMixer, 0);
            }

            if (walkClip != null)
            {
                walkPlayable = AnimationClipPlayable.Create(graph, walkClip);
                ConfigureClipPlayable(walkPlayable, true);
                if (forceRestart)
                {
                    walkPlayable.SetTime(walkLocomotionTime);
                    walkPlayable.SetDone(false);
                }

                graph.Connect(walkPlayable, 0, locomotionMixer, 1);
            }

            if (moveClip != null)
            {
                movePlayable = AnimationClipPlayable.Create(graph, moveClip);
                ConfigureClipPlayable(movePlayable, true);
                if (forceRestart)
                {
                    movePlayable.SetTime(moveLocomotionTime);
                    movePlayable.SetDone(false);
                }

                graph.Connect(movePlayable, 0, locomotionMixer, 2);
            }

            output.SetSourcePlayable(locomotionMixer);
            LogAnimationDiagnostic("LocomotionMixer", "idleClip=" + ClipName(idleClip)
                + ", walkClip=" + ClipName(walkClip)
                + ", moveClip=" + ClipName(moveClip)
                + ", blend=" + locomotionBlendWeight.ToString("0.00"));
        }

        private void StartAction(string actionId)
        {
            if (actionDatabase == null)
            {
                Debug.LogWarning("[CombatActor] Missing CombatActionDatabase.", this);
                return;
            }

            if (string.IsNullOrWhiteSpace(actionId))
            {
                CombatAnimationDefinition firstAction = actionDatabase.FirstAction();
                actionId = firstAction == null ? string.Empty : firstAction.actionId;
            }

            if (!actionDatabase.TryGetAction(actionId, out CombatAnimationDefinition action) || action == null)
            {
                Debug.LogWarning("[CombatActor] Action not found: " + actionId, this);
                return;
            }

            if (action.clip == null)
            {
                Debug.LogWarning("[CombatActor] Action has no clip: " + ActionDisplayName(action), action);
                return;
            }

            action.EnsureMarkers();
            action.EnsureActionLinks();

            currentAction = action;
            currentActionTime = 0f;
            queuedActionId = string.Empty;
            bufferedInputAction = string.Empty;
            bufferedAttackUntil = -1f;
            hitRecords.Clear();
            PlayClip(action.clip, false, true);

            if (logCombatEvents)
            {
                Debug.Log("[CombatActor] Start action " + ActionDisplayName(action), this);
            }

            LogAnimationDiagnostic("StartAction", BuildActionDiagnostic(action));
        }

        private void FinishCurrentAction()
        {
            string finishedAction = currentAction == null ? string.Empty : currentAction.actionId;
            string nextActionId = queuedActionId;

            currentAction = null;
            currentActionTime = 0f;
            queuedActionId = string.Empty;
            bufferedInputAction = string.Empty;
            bufferedAttackUntil = -1f;
            hitRecords.Clear();

            if (!string.IsNullOrWhiteSpace(nextActionId))
            {
                StartAction(nextActionId);
                return;
            }

            BeginReturnToLocomotionTransition();

            if (logCombatEvents && !string.IsNullOrWhiteSpace(finishedAction))
            {
                Debug.Log("[CombatActor] Finish action " + finishedAction, this);
            }
        }

        private void BeginReturnToLocomotionTransition()
        {
            BeginReturnToLocomotionTransition(actionToLocomotionBlendDuration, true);
        }

        private void BeginReturnToLocomotionTransition(float duration, bool freezeSource)
        {
            if (jumpPhase != JumpAnimationPhase.Grounded && freezeSource)
            {
                PlayMovementAnimation(true, 0f);
                return;
            }

            float transitionDuration = Mathf.Max(0f, duration);
            if (!graphReady || !currentPlayable.IsValid() || transitionDuration <= 0f)
            {
                PlayLocomotionClip(true, 0f);
                return;
            }

            EnsureLocomotionMixer(true);
            UpdateLocomotionBlend(true, 0f);
            if (freezeSource)
            {
                currentPlayable.SetSpeed(0d);
            }

            currentPlayable.SetDone(false);

            returnTransitionMixer = AnimationMixerPlayable.Create(graph, 2);
            graph.Connect(currentPlayable, 0, returnTransitionMixer, 0);
            graph.Connect(locomotionMixer, 0, returnTransitionMixer, 1);
            returnTransitionMixer.SetInputWeight(0, 1f);
            returnTransitionMixer.SetInputWeight(1, 0f);
            returnTransitionTime = 0f;
            returnTransitionDuration = transitionDuration;
            output.SetSourcePlayable(returnTransitionMixer);

            LogAnimationDiagnostic("ReturnToLocomotion", "duration=" + returnTransitionDuration.ToString("0.00")
                + ", freezeSource=" + freezeSource
                + ", targetClip=" + ClipName(currentClip));
        }

        private void UpdateReturnToLocomotionTransition(float deltaTime)
        {
            if (!returnTransitionMixer.IsValid())
            {
                return;
            }

            EnsureLocomotionMixer(false);
            UpdateLocomotionBlend(false, deltaTime);

            returnTransitionTime += Mathf.Max(0f, deltaTime);
            float duration = returnTransitionDuration > 0f ? returnTransitionDuration : actionToLocomotionBlendDuration;
            float linear = duration <= 0f
                ? 1f
                : Mathf.Clamp01(returnTransitionTime / duration);
            float eased = Mathf.SmoothStep(0f, 1f, linear);
            returnTransitionMixer.SetInputWeight(0, 1f - eased);
            returnTransitionMixer.SetInputWeight(1, eased);

            if (linear >= 1f)
            {
                CompleteReturnToLocomotionTransition();
            }
        }

        private void CompleteReturnToLocomotionTransition()
        {
            DestroyReturnTransition(true);
            if (locomotionMixer.IsValid())
            {
                output.SetSourcePlayable(locomotionMixer);
            }
        }

        private bool ShouldReturnToLocomotionFromAction(float clipLength)
        {
            if (currentAction == null
                || clipLength <= 0f
                || !string.IsNullOrWhiteSpace(queuedActionId)
                || HasOutgoingActionLinks(currentAction)
                || input == null
                || EffectiveMoveInput(input.Move).sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            return !HasActiveMarker(CombatAnimationEventKind.MovementLock)
                && !HasActiveMarker(CombatAnimationEventKind.Hitbox)
                && !HasActiveMarker(CombatAnimationEventKind.ComboBranch)
                && !HasActiveMarker(CombatAnimationEventKind.Invulnerability)
                && !HasActiveMarker(CombatAnimationEventKind.SuperArmor);
        }

        private static bool HasOutgoingActionLinks(CombatAnimationDefinition action)
        {
            if (action == null || action.actionLinks == null)
            {
                return false;
            }

            foreach (CombatActionLink link in action.actionLinks)
            {
                if (link == null)
                {
                    continue;
                }

                if (link.targetDefinition != null || !string.IsNullOrWhiteSpace(link.targetActionId))
                {
                    return true;
                }
            }

            return false;
        }

        private void ProcessActiveMarkers()
        {
            if (currentAction == null || currentAction.markers == null)
            {
                return;
            }

            float clipLength = currentAction.clip == null ? 0f : currentAction.clip.length;
            foreach (CombatAnimationMarker marker in currentAction.markers)
            {
                if (marker == null || !IsMarkerActive(marker, currentActionTime, clipLength, currentAction.authoringFrameRate))
                {
                    continue;
                }

                if (marker.kind == CombatAnimationEventKind.Hitbox)
                {
                    activeHitboxes.Add(marker);
                    ProcessHitbox(marker);
                }
            }
        }

        private void ProcessHitbox(CombatAnimationMarker marker)
        {
            Vector3 center = transform.TransformPoint(marker.localOffset);
            Vector3 halfExtents = new Vector3(
                Mathf.Max(0.01f, marker.size.x * 0.5f),
                Mathf.Max(0.01f, marker.size.y * 0.5f),
                Mathf.Max(0.01f, marker.size.z * 0.5f));

            AddHitboxDebugSample(marker, center, halfExtents * 2f, transform.rotation);

            Collider[] hits = Physics.OverlapBox(center, halfExtents, transform.rotation, hurtboxLayers, QueryTriggerInteraction.Collide);
            foreach (Collider hit in hits)
            {
                TrainingDummyHurtbox hurtbox = hit.GetComponentInParent<TrainingDummyHurtbox>();
                if (hurtbox == null || hurtbox.Dummy == null)
                {
                    continue;
                }

                string markerId = string.IsNullOrWhiteSpace(marker.id) ? marker.GetHashCode().ToString() : marker.id;
                if (!hitRecords.Add(new HitRecord(markerId, hurtbox.Dummy)))
                {
                    continue;
                }

                hurtbox.Dummy.ApplyHit(this, currentAction, marker, DamageFor(marker), hit.ClosestPoint(center));
            }
        }

        private int DamageFor(CombatAnimationMarker marker)
        {
            if (marker != null && !string.IsNullOrWhiteSpace(marker.payload))
            {
                string payload = marker.payload.Trim();
                if (int.TryParse(payload, out int directDamage))
                {
                    return Mathf.Max(1, directDamage);
                }

                const string damagePrefix = "damage=";
                int damageIndex = payload.IndexOf(damagePrefix, System.StringComparison.OrdinalIgnoreCase);
                if (damageIndex >= 0)
                {
                    string damageText = payload.Substring(damageIndex + damagePrefix.Length).Trim();
                    int separator = damageText.IndexOfAny(new[] { ';', ',', ' ' });
                    if (separator >= 0)
                    {
                        damageText = damageText.Substring(0, separator);
                    }

                    if (int.TryParse(damageText, out int payloadDamage))
                    {
                        return Mathf.Max(1, payloadDamage);
                    }
                }
            }

            return defaultDamage;
        }

        private void AddHitboxDebugSample(CombatAnimationMarker marker, Vector3 center, Vector3 size, Quaternion rotation)
        {
            if (!drawHitboxDebug || marker == null)
            {
                return;
            }

            PruneHitboxDebugSamples();
            hitboxDebugSamples.Add(new HitboxDebugSample
            {
                center = center,
                size = size,
                rotation = rotation,
                color = marker.color,
                expiresAt = Time.time + Mathf.Max(0.02f, hitboxDebugLinger)
            });
        }

        private void PruneHitboxDebugSamples()
        {
            for (int i = hitboxDebugSamples.Count - 1; i >= 0; i--)
            {
                if (Time.time >= hitboxDebugSamples[i].expiresAt)
                {
                    hitboxDebugSamples.RemoveAt(i);
                }
            }
        }

        private bool TryGetComboTarget(string inputAction, out string targetActionId)
        {
            targetActionId = string.Empty;
            if (currentAction == null || currentAction.actionLinks == null)
            {
                return false;
            }

            int frameRate = Mathf.Max(1, currentAction.authoringFrameRate);
            int currentFrame = Mathf.RoundToInt(currentActionTime * frameRate);

            foreach (CombatActionLink link in currentAction.actionLinks)
            {
                if (link == null || !CombatInputActionNames.ExactMatches(link.inputAction, inputAction))
                {
                    continue;
                }

                if (currentFrame >= link.startFrame && currentFrame <= Mathf.Max(link.startFrame, link.endFrame))
                {
                    targetActionId = link.targetActionId;
                    return !string.IsNullOrWhiteSpace(targetActionId);
                }
            }

            foreach (CombatActionLink link in currentAction.actionLinks)
            {
                if (link == null || !CombatInputActionNames.Matches(link.inputAction, inputAction))
                {
                    continue;
                }

                if (currentFrame >= link.startFrame && currentFrame <= Mathf.Max(link.startFrame, link.endFrame))
                {
                    targetActionId = link.targetActionId;
                    return !string.IsNullOrWhiteSpace(targetActionId);
                }
            }

            return false;
        }

        private bool TryQueueBufferedCombo(string reason)
        {
            if (currentAction == null
                || !string.IsNullOrWhiteSpace(queuedActionId)
                || string.IsNullOrWhiteSpace(bufferedInputAction)
                || Time.time > bufferedAttackUntil)
            {
                return false;
            }

            if (!TryGetComboTarget(bufferedInputAction, out string targetActionId))
            {
                return false;
            }

            queuedActionId = targetActionId;
            string consumedInput = bufferedInputAction;
            bufferedInputAction = string.Empty;
            bufferedAttackUntil = -1f;
            if (logCombatEvents)
            {
                Debug.Log("[CombatActor] Buffered combo -> "
                    + queuedActionId
                    + " (" + reason + ", input=" + consumedInput + ", frame=" + CurrentActionFrame() + ")",
                    this);
            }

            return true;
        }

        private int CurrentActionFrame()
        {
            if (currentAction == null)
            {
                return 0;
            }

            return Mathf.RoundToInt(currentActionTime * Mathf.Max(1, currentAction.authoringFrameRate));
        }

        private bool HasActiveMarker(CombatAnimationEventKind kind)
        {
            if (currentAction == null || currentAction.markers == null)
            {
                return false;
            }

            float clipLength = currentAction.clip == null ? 0f : currentAction.clip.length;
            foreach (CombatAnimationMarker marker in currentAction.markers)
            {
                if (marker != null && marker.kind == kind && IsMarkerActive(marker, currentActionTime, clipLength, currentAction.authoringFrameRate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMarkerActive(CombatAnimationMarker marker, float currentTime, float clipLength, int frameRate)
        {
            if (marker == null || clipLength <= 0f)
            {
                return false;
            }

            float start = Mathf.Clamp01(marker.normalizedTime) * clipLength;
            float minDuration = frameRate <= 0 ? 0.016f : 1f / frameRate;
            float duration = Mathf.Max(marker.duration, minDuration);
            return currentTime >= start && currentTime <= start + duration;
        }

        private void PlayClip(AnimationClip clipToPlay, bool loop, bool forceRestart)
        {
            if (clipToPlay == null || !graphReady)
            {
                return;
            }

            DestroyReturnTransition(true);
            DestroyLocomotionMixer();

            bool sameClip = currentClip == clipToPlay && currentPlayable.IsValid();
            if (!sameClip)
            {
                if (currentPlayable.IsValid())
                {
                    graph.DestroyPlayable(currentPlayable);
                }

                currentPlayable = AnimationClipPlayable.Create(graph, clipToPlay);
                ConfigureClipPlayable(currentPlayable, loop);
                output.SetSourcePlayable(currentPlayable);
                currentClip = clipToPlay;
            }

            currentClipLoops = loop;
            if (forceRestart || !sameClip)
            {
                currentPlayable.SetTime(0d);
                currentPlayable.SetDone(false);
            }

            LogAnimationDiagnostic("PlayClip", BuildClipDiagnostic(clipToPlay, loop, forceRestart, sameClip));
        }

        private void DestroyReturnTransition(bool destroyActionPlayable)
        {
            bool hadTransition = returnTransitionMixer.IsValid();
            if (returnTransitionMixer.IsValid())
            {
                returnTransitionMixer.DisconnectInput(0);
                returnTransitionMixer.DisconnectInput(1);
                graph.DestroyPlayable(returnTransitionMixer);
                returnTransitionMixer = default;
            }

            if (hadTransition && destroyActionPlayable && currentPlayable.IsValid())
            {
                graph.DestroyPlayable(currentPlayable);
                currentPlayable = default;
            }

            returnTransitionTime = 0f;
            returnTransitionDuration = 0f;
        }

        private static void ConfigureClipPlayable(AnimationClipPlayable playable, bool loop)
        {
            playable.SetApplyFootIK(true);
            if (loop)
            {
                playable.SetDuration(double.PositiveInfinity);
                playable.SetSpeed(0d);
                return;
            }

            playable.SetSpeed(1d);
        }

        private void UpdateLocomotionPlayableTimes(float deltaTime, float playbackSpeed, float walkWeight, float moveWeight)
        {
            float safeDeltaTime = Mathf.Max(0f, deltaTime);
            float safePlaybackSpeed = Mathf.Max(0f, playbackSpeed);

            AdvanceLoopingPlayable(idlePlayable, idleClip, ref idleLocomotionTime, safeDeltaTime, 1f);
            if (!synchronizeLocomotionCyclePhase)
            {
                AdvanceLoopingPlayable(walkPlayable, walkClip, ref walkLocomotionTime, safeDeltaTime, safePlaybackSpeed);
                AdvanceLoopingPlayable(movePlayable, moveClip, ref moveLocomotionTime, safeDeltaTime, safePlaybackSpeed);
                return;
            }

            AdvanceSynchronizedLocomotionPhase(safeDeltaTime, safePlaybackSpeed, walkWeight, moveWeight);
            ApplySynchronizedLoopingPlayable(walkPlayable, walkClip, ref walkLocomotionTime, locomotionWalkCycleOffset);
            ApplySynchronizedLoopingPlayable(movePlayable, moveClip, ref moveLocomotionTime, locomotionMoveCycleOffset);
        }

        private void AdvanceSynchronizedLocomotionPhase(float deltaTime, float playbackSpeed, float walkWeight, float moveWeight)
        {
            if (deltaTime <= 0f || playbackSpeed <= 0f)
            {
                return;
            }

            float cycleRate = 0f;
            float totalWeight = 0f;
            AddCycleRate(walkClip, walkWeight, ref cycleRate, ref totalWeight);
            AddCycleRate(moveClip, moveWeight, ref cycleRate, ref totalWeight);
            if (totalWeight <= 0.0001f)
            {
                return;
            }

            cycleRate = playbackSpeed * cycleRate / totalWeight;
            locomotionCyclePhase = PositiveModulo(locomotionCyclePhase + deltaTime * cycleRate, 1d);
        }

        private static void AddCycleRate(AnimationClip clip, float weight, ref float cycleRate, ref float totalWeight)
        {
            if (clip == null || clip.length <= 0f || weight <= 0.0001f)
            {
                return;
            }

            cycleRate += weight / clip.length;
            totalWeight += weight;
        }

        private void ApplySynchronizedLoopingPlayable(
            AnimationClipPlayable playable,
            AnimationClip clip,
            ref double localTime,
            float phaseOffset)
        {
            if (!playable.IsValid() || clip == null || clip.length <= 0f)
            {
                return;
            }

            double phase = PositiveModulo(locomotionCyclePhase + Mathf.Repeat(phaseOffset, 1f), 1d);
            localTime = phase * clip.length;
            playable.SetTime(localTime);
            playable.SetDone(false);
        }

        private static void AdvanceLoopingPlayable(
            AnimationClipPlayable playable,
            AnimationClip clip,
            ref double localTime,
            float deltaTime,
            float playbackSpeed)
        {
            if (!playable.IsValid() || clip == null || clip.length <= 0f)
            {
                return;
            }

            double length = clip.length;
            if (double.IsNaN(localTime) || double.IsInfinity(localTime))
            {
                localTime = PositiveModulo(playable.GetTime(), length);
            }

            localTime = PositiveModulo(localTime + deltaTime * playbackSpeed, length);
            playable.SetTime(localTime);
            playable.SetDone(false);
        }

        private static double PositiveModulo(double value, double modulus)
        {
            double result = value % modulus;
            return result < 0d ? result + modulus : result;
        }

        private void DestroyLocomotionMixer()
        {
            if (locomotionMixer.IsValid())
            {
                locomotionMixer.DisconnectInput(0);
                locomotionMixer.DisconnectInput(1);
                locomotionMixer.DisconnectInput(2);
            }

            if (idlePlayable.IsValid())
            {
                graph.DestroyPlayable(idlePlayable);
                idlePlayable = default;
            }

            if (walkPlayable.IsValid())
            {
                graph.DestroyPlayable(walkPlayable);
                walkPlayable = default;
            }

            if (movePlayable.IsValid())
            {
                graph.DestroyPlayable(movePlayable);
                movePlayable = default;
            }

            if (locomotionMixer.IsValid())
            {
                graph.DestroyPlayable(locomotionMixer);
                locomotionMixer = default;
            }

            locomotionBlendWeight = 0f;
            ResetLocomotionTimes();
        }

        private void ResetLocomotionTimes()
        {
            locomotionCyclePhase = 0d;
            idleLocomotionTime = 0d;
            walkLocomotionTime = 0d;
            moveLocomotionTime = 0d;
        }

        private void LogPlaybackTickDiagnostic(float clipLength)
        {
            if (!logAnimationDiagnostics || Time.time < nextDiagnosticLogTime)
            {
                return;
            }

            nextDiagnosticLogTime = Time.time + Mathf.Max(0.05f, diagnosticLogInterval);

            string playableState = currentPlayable.IsValid()
                ? "playableTime=" + currentPlayable.GetTime().ToString("0.000") + ", playableDuration=" + currentPlayable.GetDuration().ToString("0.000")
                : "playable=invalid";

            string boneState = BuildBoneDiagnostic();
            Debug.Log("[CombatActor/AnimDiag] Tick action="
                + ActionDisplayName(currentAction)
                + ", actionTime=" + currentActionTime.ToString("0.000")
                + ", clipLength=" + clipLength.ToString("0.000")
                + ", currentClip=" + (currentClip == null ? "None" : currentClip.name)
                + ", graphValid=" + graph.IsValid()
                + ", graphReady=" + graphReady
                + ", " + playableState
                + ", " + boneState,
                this);
        }

        private void LogAnimationDiagnostic(string stage, string message)
        {
            if (!logAnimationDiagnostics)
            {
                return;
            }

            Debug.Log("[CombatActor/AnimDiag] " + stage + " " + message, this);
        }

        private string BuildAnimatorDiagnostic()
        {
            if (animator == null)
            {
                return "animator=None";
            }

            Avatar avatar = animator.avatar;
            return "animator=" + animator.name
                + ", enabled=" + animator.enabled
                + ", active=" + animator.gameObject.activeInHierarchy
                + ", avatar=" + (avatar == null ? "None" : avatar.name)
                + ", fallbackAvatar=" + (fallbackAvatar == null ? "None" : fallbackAvatar.name)
                + ", avatarValid=" + (avatar != null && avatar.isValid)
                + ", avatarHuman=" + (avatar != null && avatar.isHuman)
                + ", controller=" + (animator.runtimeAnimatorController == null ? "None" : animator.runtimeAnimatorController.name)
                + ", culling=" + animator.cullingMode
                + ", applyRootMotion=" + animator.applyRootMotion
                + ", transform=" + TransformPath(animator.transform, transform);
        }

        private string BuildGraphDiagnostic()
        {
            return "graphValid=" + graph.IsValid()
                + ", graphReady=" + graphReady
                + ", outputValid=" + output.IsOutputValid()
                + ", " + BuildAnimatorDiagnostic()
                + ", idleClip=" + ClipName(idleClip)
                + ", walkClip=" + ClipName(walkClip)
                + ", moveClip=" + ClipName(moveClip)
                + ", jumpStartClip=" + ClipName(jumpStartClip)
                + ", jumpLoopClip=" + ClipName(jumpLoopClip)
                + ", jumpLandClip=" + ClipName(jumpLandClip)
                + ", landingDuration=" + landingAnimationDuration.ToString("0.000")
                + ", lockLandingInput=" + lockLandingInput
                + ", syncLocomotionPhase=" + synchronizeLocomotionCyclePhase
                + ", locomotionPhase=" + locomotionCyclePhase.ToString("0.000")
                + ", walkPhaseOffset=" + locomotionWalkCycleOffset.ToString("0.000")
                + ", movePhaseOffset=" + locomotionMoveCycleOffset.ToString("0.000")
                + ", jumpPhase=" + jumpPhase
                + ", grounded=" + isGrounded;
        }

        private string BuildActionDiagnostic(CombatAnimationDefinition action)
        {
            if (action == null)
            {
                return "action=None";
            }

            return "action=" + ActionDisplayName(action)
                + ", clip=" + ClipName(action.clip)
                + ", markers=" + (action.markers == null ? 0 : action.markers.Count)
                + ", links=" + (action.actionLinks == null ? 0 : action.actionLinks.Count)
                + ", " + BuildAnimatorDiagnostic()
                + ", " + BuildBoneDiagnostic();
        }

        private static string ActionDisplayName(CombatAnimationDefinition action)
        {
            return action == null ? "None" : action.DisplayName;
        }

        private string BuildClipDiagnostic(AnimationClip clipToPlay, bool loop, bool forceRestart, bool sameClip)
        {
            string playableState = currentPlayable.IsValid()
                ? "playableValid=True, playableTime=" + currentPlayable.GetTime().ToString("0.000") + ", playableDuration=" + currentPlayable.GetDuration().ToString("0.000")
                : "playableValid=False";

            return "clip=" + ClipName(clipToPlay)
                + ", loop=" + loop
                + ", forceRestart=" + forceRestart
                + ", sameClip=" + sameClip
                + ", graphValid=" + graph.IsValid()
                + ", graphReady=" + graphReady
                + ", currentClip=" + ClipName(currentClip)
                + ", " + playableState;
        }

        private string BuildBoneDiagnostic()
        {
            if (animator == null)
            {
                return "bone=None";
            }

            Transform bone = null;
            if (animator.avatar != null && animator.avatar.isHuman)
            {
                bone = animator.GetBoneTransform(HumanBodyBones.Hips);
            }

            if (bone == null && animator.transform.childCount > 0)
            {
                bone = animator.transform.GetChild(0);
            }

            if (bone == null)
            {
                return "bone=None";
            }

            return "bone=" + TransformPath(bone, transform)
                + ", boneLocalPos=" + FormatVector(bone.localPosition)
                + ", boneLocalEuler=" + FormatVector(bone.localEulerAngles);
        }

        private static string ClipName(AnimationClip clipToName)
        {
            if (clipToName == null)
            {
                return "None";
            }

            return clipToName.name
                + "(len=" + clipToName.length.ToString("0.000")
                + ", frameRate=" + clipToName.frameRate.ToString("0.##")
                + ", empty=" + clipToName.empty
                + ", legacy=" + clipToName.legacy
                + ", humanMotion=" + clipToName.humanMotion
                + ")";
        }

        private static string TransformPath(Transform target, Transform root)
        {
            if (target == null)
            {
                return "None";
            }

            if (target == root)
            {
                return target.name;
            }

            List<string> parts = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            if (current == root)
            {
                parts.Add(root.name);
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string FormatVector(Vector3 value)
        {
            return "(" + value.x.ToString("0.000") + ", " + value.y.ToString("0.000") + ", " + value.z.ToString("0.000") + ")";
        }

        private void OnDrawGizmos()
        {
            DrawHitboxDebugGizmos();
        }

        private void DrawHitboxDebugGizmos()
        {
            if (!drawHitboxDebug || !Application.isPlaying || hitboxDebugSamples.Count == 0)
            {
                return;
            }

            PruneHitboxDebugSamples();
            for (int i = 0; i < hitboxDebugSamples.Count; i++)
            {
                HitboxDebugSample sample = hitboxDebugSamples[i];
                Gizmos.color = sample.color;
                Gizmos.matrix = Matrix4x4.TRS(sample.center, sample.rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, sample.size);
            }

            Gizmos.matrix = Matrix4x4.identity;
        }

        private struct HitboxDebugSample
        {
            public Vector3 center;
            public Vector3 size;
            public Quaternion rotation;
            public Color color;
            public float expiresAt;
        }

        private readonly struct HitRecord : System.IEquatable<HitRecord>
        {
            private readonly string markerId;
            private readonly TrainingDummy dummy;

            public HitRecord(string markerId, TrainingDummy dummy)
            {
                this.markerId = markerId;
                this.dummy = dummy;
            }

            public bool Equals(HitRecord other)
            {
                return string.Equals(markerId, other.markerId, System.StringComparison.Ordinal)
                    && ReferenceEquals(dummy, other.dummy);
            }

            public override bool Equals(object obj)
            {
                return obj is HitRecord other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((markerId == null ? 0 : markerId.GetHashCode()) * 397)
                        ^ (dummy == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(dummy));
                }
            }
        }
    }
}
