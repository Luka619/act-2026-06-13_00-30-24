using UnityEngine;

namespace ActToolkit
{
    public sealed class TrainingDummy : MonoBehaviour
    {
        [SerializeField]
        private int maxHealth = 100;

        [SerializeField]
        private Transform floatingTextAnchor;

        [SerializeField]
        private TextMesh healthText;

        [SerializeField]
        private Renderer[] flashRenderers;

        [SerializeField]
        private Color normalColor = new Color(0.72f, 0.52f, 0.32f, 1f);

        [SerializeField]
        private Color hitColor = new Color(1f, 0.25f, 0.12f, 1f);

        [SerializeField]
        private float flashDuration = 0.12f;

        [SerializeField]
        private float autoResetDelay = 0.8f;

        private int health;
        private float flashTimer;
        private float resetTimer = -1f;
        private MaterialPropertyBlock propertyBlock;

        public void ConfigureForPlaytest(Transform textAnchor, TextMesh targetHealthText, Renderer[] targetRenderers)
        {
            floatingTextAnchor = textAnchor;
            healthText = targetHealthText;
            flashRenderers = targetRenderers;

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            if (Application.isPlaying)
            {
                health = maxHealth;
                UpdateHealthText();
                ApplyColor(normalColor);
            }
        }

        private void Awake()
        {
            health = maxHealth;
            propertyBlock = new MaterialPropertyBlock();

            if (floatingTextAnchor == null)
            {
                floatingTextAnchor = transform;
            }

            if (flashRenderers == null || flashRenderers.Length == 0)
            {
                flashRenderers = GetComponentsInChildren<Renderer>();
            }

            UpdateHealthText();
            ApplyColor(normalColor);
        }

        private void Update()
        {
            if (flashTimer > 0f)
            {
                flashTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(flashTimer / Mathf.Max(0.01f, flashDuration));
                ApplyColor(Color.Lerp(normalColor, hitColor, t));
            }

            if (resetTimer >= 0f)
            {
                resetTimer -= Time.deltaTime;
                if (resetTimer <= 0f)
                {
                    ResetDummy();
                }
            }
        }

        public void ApplyHit(CombatActor source, CombatAnimationDefinition action, CombatAnimationMarker marker, int damage, Vector3 hitPoint)
        {
            health = Mathf.Max(0, health - Mathf.Max(1, damage));
            flashTimer = flashDuration;
            resetTimer = health <= 0 ? autoResetDelay : -1f;

            string actionId = action == null ? "unknown" : action.actionId;
            Debug.Log("[TrainingDummy] Hit by " + actionId + " for " + damage + ". HP " + health + "/" + maxHealth + ".", this);

            Vector3 textPosition = floatingTextAnchor == null ? hitPoint + Vector3.up : floatingTextAnchor.position;
            FloatingCombatText.Spawn("-" + damage, textPosition, Color.yellow);
            UpdateHealthText();
        }

        public void ResetDummy()
        {
            health = maxHealth;
            resetTimer = -1f;
            flashTimer = 0f;
            ApplyColor(normalColor);
            UpdateHealthText();
            FloatingCombatText.Spawn("Reset", transform.position + Vector3.up * 2.2f, Color.cyan);
            Debug.Log("[TrainingDummy] Reset.", this);
        }

        private void UpdateHealthText()
        {
            if (healthText != null)
            {
                healthText.text = "Dummy HP\n" + health + " / " + maxHealth;
            }
        }

        private void ApplyColor(Color color)
        {
            if (flashRenderers == null)
            {
                return;
            }

            foreach (Renderer targetRenderer in flashRenderers)
            {
                if (targetRenderer == null)
                {
                    continue;
                }

                targetRenderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor("_BaseColor", color);
                propertyBlock.SetColor("_Color", color);
                targetRenderer.SetPropertyBlock(propertyBlock);
            }
        }
    }
}
