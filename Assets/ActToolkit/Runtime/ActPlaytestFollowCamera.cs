using UnityEngine;

namespace ActToolkit
{
    public sealed class ActPlaytestFollowCamera : MonoBehaviour
    {
        [SerializeField]
        private Transform target;

        [SerializeField]
        private PlayerCombatGamepadInput input;

        [Header("Framing")]
        [SerializeField]
        private Vector3 offset = new Vector3(0f, 4.2f, -6.2f);

        [SerializeField]
        private Vector3 lookAtOffset = new Vector3(0f, 1.1f, 0f);

        [Header("Look")]
        [SerializeField, Range(0f, 0.9f)]
        private float lookDeadzone = 0.12f;

        [SerializeField, Min(1f)]
        private float yawSpeed = 160f;

        [SerializeField, Min(1f)]
        private float pitchSpeed = 90f;

        [SerializeField, Range(-30f, 80f)]
        private float minPitch = 12f;

        [SerializeField, Range(-30f, 80f)]
        private float maxPitch = 58f;

        [SerializeField]
        private bool invertVerticalLook;

        [Header("Smoothing")]
        [SerializeField, Range(0f, 20f)]
        private float followSharpness = 10f;

        [SerializeField, Range(0f, 20f)]
        private float rotationSharpness = 12f;

        private float yaw;
        private float pitch;
        private float distance;
        private bool orbitInitialized;

        public void Configure(Transform followTarget)
        {
            target = followTarget;
            input = target == null ? null : target.GetComponent<PlayerCombatGamepadInput>();
            orbitInitialized = false;
            SnapToTarget();
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            EnsureOrbitInitialized();
            ResolveInputFromTarget();
            UpdateOrbitAngles(Time.deltaTime);

            Vector3 lookTarget = target.position + lookAtOffset;
            Vector3 desiredPosition = lookTarget + OrbitOffset();
            float positionT = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            float rotationT = 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime);

            transform.position = Vector3.Lerp(transform.position, desiredPosition, positionT);

            Vector3 lookDirection = lookTarget - transform.position;
            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationT);
            }
        }

        private void SnapToTarget()
        {
            if (target == null)
            {
                return;
            }

            EnsureOrbitInitialized();

            Vector3 lookTarget = target.position + lookAtOffset;
            transform.position = lookTarget + OrbitOffset();
            transform.rotation = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);
        }

        private void ResolveInputFromTarget()
        {
            if (input != null || target == null)
            {
                return;
            }

            input = target.GetComponent<PlayerCombatGamepadInput>();
        }

        private void EnsureOrbitInitialized()
        {
            if (orbitInitialized)
            {
                return;
            }

            distance = Mathf.Max(0.1f, offset.magnitude);

            Vector3 planarOffset = new Vector3(offset.x, 0f, offset.z);
            yaw = planarOffset.sqrMagnitude <= 0.0001f
                ? transform.eulerAngles.y
                : Vector3.SignedAngle(Vector3.back, planarOffset.normalized, Vector3.up);

            pitch = Mathf.Asin(Mathf.Clamp(offset.y / distance, -1f, 1f)) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            orbitInitialized = true;
        }

        private void UpdateOrbitAngles(float deltaTime)
        {
            Vector2 look = input == null ? Vector2.zero : input.Look;
            if (look.magnitude <= lookDeadzone)
            {
                return;
            }

            float effectiveMagnitude = Mathf.InverseLerp(lookDeadzone, 1f, Mathf.Clamp01(look.magnitude));
            Vector2 effectiveLook = look.normalized * effectiveMagnitude;
            float verticalSign = invertVerticalLook ? 1f : -1f;

            yaw += effectiveLook.x * yawSpeed * deltaTime;
            pitch = Mathf.Clamp(pitch + effectiveLook.y * verticalSign * pitchSpeed * deltaTime, minPitch, maxPitch);

            if (yaw > 360f || yaw < -360f)
            {
                yaw = Mathf.Repeat(yaw, 360f);
            }
        }

        private Vector3 OrbitOffset()
        {
            return Quaternion.Euler(pitch, yaw, 0f) * (Vector3.back * distance);
        }

        private void OnValidate()
        {
            maxPitch = Mathf.Max(minPitch, maxPitch);
            if (offset.sqrMagnitude < 0.01f)
            {
                offset = new Vector3(0f, 4.2f, -6.2f);
            }

            orbitInitialized = false;
        }
    }
}
