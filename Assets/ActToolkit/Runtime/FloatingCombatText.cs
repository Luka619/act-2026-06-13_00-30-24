using UnityEngine;

namespace ActToolkit
{
    public sealed class FloatingCombatText : MonoBehaviour
    {
        [SerializeField]
        private float lifetime = 0.75f;

        [SerializeField]
        private Vector3 velocity = new Vector3(0f, 1.4f, 0f);

        private TextMesh textMesh;
        private Color startColor;
        private float age;

        public static FloatingCombatText Spawn(string text, Vector3 position, Color color)
        {
            GameObject instance = new GameObject("FloatingCombatText");
            instance.transform.position = position;

            TextMesh mesh = instance.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontSize = 48;
            mesh.characterSize = 0.045f;
            mesh.color = color;

            MeshRenderer renderer = instance.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sortingOrder = 20;
            }

            FloatingCombatText floatingText = instance.AddComponent<FloatingCombatText>();
            floatingText.textMesh = mesh;
            floatingText.startColor = color;
            return floatingText;
        }

        private void Awake()
        {
            if (textMesh == null)
            {
                textMesh = GetComponent<TextMesh>();
            }

            if (textMesh != null)
            {
                startColor = textMesh.color;
            }
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            transform.position += velocity * Time.deltaTime;

            Camera camera = Camera.main;
            if (camera != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position, Vector3.up);
            }

            if (textMesh != null)
            {
                Color color = startColor;
                color.a = Mathf.Clamp01(1f - age / Mathf.Max(0.01f, lifetime));
                textMesh.color = color;
            }

            if (age >= lifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}
