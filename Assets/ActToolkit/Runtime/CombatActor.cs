using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ActToolkit
{
    public sealed class CombatActor : MonoBehaviour
    {
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
        private AnimationClip moveClip;

        [Header("Movement")]
        [SerializeField]
        private float moveSpeed = 3.8f;

        [SerializeField]
        private float rotationSpeed = 720f;

        [SerializeField]
        private float gravity = -18f;

        [Header("Hit Detection")]
        [SerializeField]
        private LayerMask hurtboxLayers = ~0;

        [SerializeField]
        private int defaultDamage = 10;

        [SerializeField]
        private bool logCombatEvents = true;

        [SerializeField]
        private bool logAnimationDiagnostics = true;

        [SerializeField]
        private float diagnosticLogInterval = 0.25f;

        private readonly HashSet<HitRecord> hitRecords = new HashSet<HitRecord>();
        private readonly List<CombatAnimationMarker> activeHitboxes = new List<CombatAnimationMarker>();

        private PlayableGraph graph;
        private AnimationPlayableOutput output;
        private AnimationClipPlayable currentPlayable;
        private AnimationClip currentClip;
        private bool currentClipLoops;

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

            if (characterProfile.moveClip != null)
            {
                moveClip = characterProfile.moveClip;
            }
        }

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
            PlayLocomotionClip(true);
        }

        private void OnDisable()
        {
            if (graph.IsValid())
            {
                graph.Destroy();
            }

            graphReady = false;
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

            if (animator.avatar == null)
            {
                Debug.LogWarning("[CombatActor] Animator has no Avatar. Animation clips may not move the model.", this);
            }

            graph = PlayableGraph.Create(name + "_CombatGraph");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            graph.Play();
            graphReady = true;

            LogAnimationDiagnostic("GraphReady", BuildGraphDiagnostic());
        }

        private void HandleAttackInput()
        {
            if (input == null)
            {
                return;
            }

            if (input.AttackPressed)
            {
                HandlePressedCombatInput(CombatInputActionNames.WithMoveDirection(CombatInputActionNames.LightAttack, input.Move));
            }

            if (input.HeavyPressed)
            {
                HandlePressedCombatInput(CombatInputActionNames.WithMoveDirection(CombatInputActionNames.HeavyAttack, input.Move));
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
                    + currentAction.actionId
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

            Vector2 moveInput = IsMovementLocked ? Vector2.zero : input.Move;
            Vector3 move = CameraRelativeMove(moveInput);

            if (move.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(move, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * deltaTime);
            }

            if (characterController != null)
            {
                if (characterController.isGrounded && verticalVelocity < 0f)
                {
                    verticalVelocity = -1f;
                }

                verticalVelocity += gravity * deltaTime;
                Vector3 velocity = move * moveSpeed;
                velocity.y = verticalVelocity;
                characterController.Move(velocity * deltaTime);
            }
            else
            {
                transform.position += move * (moveSpeed * deltaTime);
            }
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

        private void UpdateAnimationAndCombat(float deltaTime)
        {
            activeHitboxes.Clear();

            if (currentAction == null)
            {
                PlayLocomotionClip(false);
                return;
            }

            currentActionTime += deltaTime;
            float clipLength = currentAction.clip == null ? 0f : currentAction.clip.length;
            ProcessActiveMarkers();
            TryQueueBufferedCombo("buffer");
            LogPlaybackTickDiagnostic(clipLength);

            if (clipLength <= 0f || currentActionTime >= clipLength)
            {
                FinishCurrentAction();
            }
        }

        private void PlayLocomotionClip(bool force)
        {
            AnimationClip targetClip = idleClip;
            if (input != null && input.Move.sqrMagnitude > 0.05f && moveClip != null)
            {
                targetClip = moveClip;
            }

            if (targetClip != null)
            {
                PlayClip(targetClip, true, force);
            }
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
                Debug.LogWarning("[CombatActor] Action has no clip: " + action.actionId, action);
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
                Debug.Log("[CombatActor] Start action " + action.actionId, this);
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

            PlayLocomotionClip(true);

            if (logCombatEvents && !string.IsNullOrWhiteSpace(finishedAction))
            {
                Debug.Log("[CombatActor] Finish action " + finishedAction, this);
            }
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

            bool sameClip = currentClip == clipToPlay && currentPlayable.IsValid();
            if (!sameClip)
            {
                if (currentPlayable.IsValid())
                {
                    graph.DestroyPlayable(currentPlayable);
                }

                currentPlayable = AnimationClipPlayable.Create(graph, clipToPlay);
                currentPlayable.SetApplyFootIK(true);
                currentPlayable.SetSpeed(1d);
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
                + (currentAction == null ? "None" : currentAction.actionId)
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
                + ", moveClip=" + ClipName(moveClip);
        }

        private string BuildActionDiagnostic(CombatAnimationDefinition action)
        {
            if (action == null)
            {
                return "action=None";
            }

            return "action=" + action.actionId
                + ", clip=" + ClipName(action.clip)
                + ", markers=" + (action.markers == null ? 0 : action.markers.Count)
                + ", links=" + (action.actionLinks == null ? 0 : action.actionLinks.Count)
                + ", " + BuildAnimatorDiagnostic()
                + ", " + BuildBoneDiagnostic();
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

        private void OnDrawGizmosSelected()
        {
            if (currentAction == null || currentAction.markers == null)
            {
                return;
            }

            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            foreach (CombatAnimationMarker marker in activeHitboxes)
            {
                if (marker == null)
                {
                    continue;
                }

                Gizmos.color = marker.color;
                Gizmos.DrawWireCube(marker.localOffset, marker.size);
            }

            Gizmos.matrix = Matrix4x4.identity;
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
