using UnityEngine;

namespace ActToolkit
{
    public sealed class TrainingDummyHurtbox : MonoBehaviour
    {
        [SerializeField]
        private TrainingDummy dummy;

        public TrainingDummy Dummy
        {
            get
            {
                if (dummy == null)
                {
                    dummy = GetComponentInParent<TrainingDummy>();
                }

                return dummy;
            }
        }

        private void Reset()
        {
            dummy = GetComponentInParent<TrainingDummy>();
        }
    }
}
