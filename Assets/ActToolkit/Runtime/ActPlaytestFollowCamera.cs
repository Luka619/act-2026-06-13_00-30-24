using UnityEngine;

namespace ActToolkit
{
    public sealed class ActPlaytestFollowCamera : MonoBehaviour
    {
        [SerializeField]
        private Transform target;

        [SerializeField]
        private Vector3 offset = new Vector3(0f, 4.2f, -6.2f);

        [SerializeField]
        private Vector3 lookAtOffset = new Vector3(0f, 1.1f, 0f);

        [SerializeField, Range(0f, 20f)]
        private float followSharpness = 10f;

        [SerializeField, Range(0f, 20f)]
        private float rotationSharpness = 12f;

        public void Configure(Transform followTarget)
        {
            target = followTarget;
            SnapToTarget();
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 desiredPosition = target.position + offset;
            Vector3 lookTarget = target.position + lookAtOffset;
            float positionT = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            float rotationT = 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime);

            transform.position = Vector3.Lerp(transform.position, desiredPosition, positionT);
            Quaternion desiredRotation = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationT);
        }

        private void SnapToTarget()
        {
            if (target == null)
            {
                return;
            }

            transform.position = target.position + offset;
            Vector3 lookTarget = target.position + lookAtOffset;
            transform.rotation = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);
        }
    }
}
