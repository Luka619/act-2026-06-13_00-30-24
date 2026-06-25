using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ActToolkit
{
    public sealed class ActPlaytestBootstrap : MonoBehaviour
    {
        private const float DefaultModelFacingYaw = 180f;

        [SerializeField]
        private CharacterActionProfile playerProfile;

        [SerializeField]
        private bool spawnDummyAtEnemySpawns = true;

        [SerializeField]
        private bool hideGameplayMarkersInPlay = true;

        [SerializeField]
        private float modelFacingYaw = DefaultModelFacingYaw;

        [SerializeField]
        private Vector3 fallbackPlayerPosition = Vector3.zero;

        private static readonly Dictionary<BlockoutElementKind, Material> RuntimeBlockoutMaterials = new Dictionary<BlockoutElementKind, Material>();

        private GameObject runtimeRoot;

        public CharacterActionProfile PlayerProfile => playerProfile;

        public void Configure(CharacterActionProfile profile)
        {
            playerProfile = profile;
        }

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            BuildPlaytestRuntime();
        }

        private void BuildPlaytestRuntime()
        {
            if (runtimeRoot != null)
            {
                Destroy(runtimeRoot);
            }

            runtimeRoot = new GameObject("__ActPlaytestRuntime");
            runtimeRoot.transform.SetParent(transform, false);

            List<BlockoutElement> elements = CollectBlockoutElements();
            int assignedBlockoutMaterials = ApplyRuntimeBlockoutVisuals(elements);
            BlockoutElement playerSpawn = FindFirst(elements, BlockoutElementKind.SpawnPoint);
            Vector3 playerPosition = playerSpawn == null ? fallbackPlayerPosition : PlayerRootPosition(playerSpawn);
            Quaternion playerRotation = playerSpawn == null ? Quaternion.identity : YawOnly(playerSpawn.transform.rotation);

            Camera camera = EnsurePlaytestCamera();
            GameObject player = CreatePlayer(playerPosition, playerRotation, camera);

            ActPlaytestFollowCamera followCamera = camera.GetComponent<ActPlaytestFollowCamera>();
            if (followCamera == null)
            {
                followCamera = camera.gameObject.AddComponent<ActPlaytestFollowCamera>();
            }

            followCamera.Configure(player.transform);
            CreateDummies(elements, player.transform);

            if (hideGameplayMarkersInPlay)
            {
                HideGameplayMarkers(elements);
            }

            Debug.Log("[ActPlaytestBootstrap] Playtest runtime spawned. profile="
                + (playerProfile == null ? "None" : playerProfile.displayName)
                + ", playerSpawn="
                + (playerSpawn == null ? "fallback" : playerSpawn.name)
                + ", blockoutMaterials="
                + assignedBlockoutMaterials,
                this);
            LogPlaytestRenderDiagnostic(camera, elements);
        }

        private GameObject CreatePlayer(Vector3 position, Quaternion rotation, Camera camera)
        {
            GameObject player = new GameObject("Player");
            player.SetActive(false);
            player.transform.SetParent(runtimeRoot.transform, false);
            player.transform.position = position;
            player.transform.rotation = rotation;

            GameObject visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(player.transform, false);

            GameObject facingCorrection = new GameObject("ModelFacingCorrection_Yaw" + Mathf.RoundToInt(modelFacingYaw));
            facingCorrection.transform.SetParent(visualRoot.transform, false);
            facingCorrection.transform.localRotation = Quaternion.Euler(0f, modelFacingYaw, 0f);

            GameObject visualModel = CreateVisualModel(facingCorrection.transform);
            Animator animator = visualModel == null ? null : visualModel.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                animator = visualModel == null ? visualRoot.AddComponent<Animator>() : visualModel.AddComponent<Animator>();
            }

            Avatar avatar = playerProfile == null ? null : playerProfile.avatar;
            if (animator != null)
            {
                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (animator.avatar == null && avatar != null)
                {
                    animator.avatar = avatar;
                }
            }

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.height = 1.8f;
            controller.radius = 0.35f;

            PlayerCombatGamepadInput input = player.AddComponent<PlayerCombatGamepadInput>();
            CombatActor actor = player.AddComponent<CombatActor>();
            actor.ConfigureForPlaytest(playerProfile, input, controller, camera, animator, avatar);

            player.SetActive(true);
            return player;
        }

        private GameObject CreateVisualModel(Transform parent)
        {
            GameObject prefab = playerProfile == null ? null : playerProfile.modelPrefab;
            GameObject visualModel = prefab == null
                ? GameObject.CreatePrimitive(PrimitiveType.Capsule)
                : Instantiate(prefab, parent);

            visualModel.name = prefab == null ? "Visual_Placeholder" : prefab.name;
            visualModel.transform.SetParent(parent, false);
            visualModel.transform.localPosition = Vector3.zero;
            visualModel.transform.localRotation = Quaternion.identity;
            visualModel.transform.localScale = Vector3.one;

            if (prefab == null)
            {
                Collider visualCollider = visualModel.GetComponent<Collider>();
                if (visualCollider != null)
                {
                    Destroy(visualCollider);
                }
            }

            return visualModel;
        }

        private Camera EnsurePlaytestCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
                camera = cameras.Length > 0 ? cameras[0] : null;
            }

            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                camera = cameraObject.AddComponent<Camera>();
            }

            if (!camera.CompareTag("MainCamera"))
            {
                camera.tag = "MainCamera";
            }

            if (camera.GetComponent<AudioListener>() == null
                && FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude).Length == 0)
            {
                camera.gameObject.AddComponent<AudioListener>();
            }

            camera.transform.SetParent(runtimeRoot.transform, true);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.18f, 0.20f, 0.22f, 1f);
            camera.allowHDR = false;
            ConfigurePlaytestCameraRendering(camera);
            DisableOtherSceneCameras(camera);
            return camera;
        }

        private static void ConfigurePlaytestCameraRendering(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;
            cameraData.allowHDROutput = false;
            cameraData.stopNaN = true;
            cameraData.dithering = false;
        }

        private static void DisableOtherSceneCameras(Camera activeCamera)
        {
            if (activeCamera == null)
            {
                return;
            }

            Scene scene = activeCamera.gameObject.scene;
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include);
            foreach (Camera camera in cameras)
            {
                if (camera == null || camera == activeCamera || camera.gameObject.scene != scene)
                {
                    continue;
                }

                camera.enabled = false;
            }

            activeCamera.enabled = true;
        }

        private static int ApplyRuntimeBlockoutVisuals(List<BlockoutElement> elements)
        {
            int assigned = 0;
            foreach (BlockoutElement element in elements)
            {
                if (element == null)
                {
                    continue;
                }

                Renderer[] renderers = element.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer targetRenderer in renderers)
                {
                    if (targetRenderer == null)
                    {
                        continue;
                    }

                    targetRenderer.sharedMaterial = RuntimeBlockoutMaterial(element.kind);
                    assigned++;
                }
            }

            assigned += ApplyFallbackBlockoutRootMaterials();
            return assigned;
        }

        private static int ApplyFallbackBlockoutRootMaterials()
        {
            GameObject root = GameObject.Find("Blockout_Root");
            if (root == null)
            {
                return 0;
            }

            int assigned = 0;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer targetRenderer in renderers)
            {
                if (targetRenderer == null || targetRenderer.GetComponentInParent<BlockoutElement>() != null)
                {
                    continue;
                }

                BlockoutElementKind kind = GuessBlockoutKind(targetRenderer.name);
                targetRenderer.sharedMaterial = RuntimeBlockoutMaterial(kind);
                assigned++;
            }

            return assigned;
        }

        private static Material RuntimeBlockoutMaterial(BlockoutElementKind kind)
        {
            if (RuntimeBlockoutMaterials.TryGetValue(kind, out Material cached) && cached != null)
            {
                return cached;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader);
            material.name = "M_Playtest_Blockout_" + kind;
            ApplyMaterialColor(material, ColorFor(kind));
            RuntimeBlockoutMaterials[kind] = material;
            return material;
        }

        private static void ApplyMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", Color.black);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0f);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0f);
            }
        }

        private void CreateDummies(List<BlockoutElement> elements, Transform player)
        {
            bool createdAny = false;
            foreach (BlockoutElement element in elements)
            {
                if (element == null)
                {
                    continue;
                }

                if (element.kind == BlockoutElementKind.DummySpawn
                    || (spawnDummyAtEnemySpawns && element.kind == BlockoutElementKind.EnemySpawn))
                {
                    CreateTrainingDummy(element.transform.position, element.transform.rotation);
                    createdAny = true;
                }
            }

            if (!createdAny && player != null)
            {
                CreateTrainingDummy(player.position + player.forward * 2.6f, Quaternion.LookRotation(-player.forward, Vector3.up));
            }
        }

        private GameObject CreateTrainingDummy(Vector3 position, Quaternion rotation)
        {
            GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dummy.name = "TrainingDummy";
            dummy.transform.SetParent(runtimeRoot.transform, false);
            if (position.y < 0.2f)
            {
                position.y = 0.9f;
            }

            dummy.transform.position = position;
            dummy.transform.rotation = YawOnly(rotation);
            dummy.transform.localScale = new Vector3(0.55f, 0.9f, 0.55f);
            ApplyMaterial(dummy, DummyMaterial());

            TrainingDummy trainingDummy = dummy.AddComponent<TrainingDummy>();

            GameObject hurtbox = new GameObject("Hurtbox");
            hurtbox.transform.SetParent(dummy.transform, false);
            hurtbox.transform.localPosition = Vector3.zero;
            BoxCollider hurtboxCollider = hurtbox.AddComponent<BoxCollider>();
            hurtboxCollider.isTrigger = true;
            hurtboxCollider.center = Vector3.zero;
            hurtboxCollider.size = new Vector3(1.2f, 2f, 1.2f);
            hurtbox.AddComponent<TrainingDummyHurtbox>();

            GameObject anchor = new GameObject("FloatingTextAnchor");
            anchor.transform.SetParent(dummy.transform, false);
            anchor.transform.localPosition = new Vector3(0f, 1.45f, 0f);

            GameObject healthTextObject = new GameObject("HealthText");
            healthTextObject.transform.SetParent(dummy.transform, false);
            healthTextObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            TextMesh healthText = healthTextObject.AddComponent<TextMesh>();
            healthText.anchor = TextAnchor.MiddleCenter;
            healthText.alignment = TextAlignment.Center;
            healthText.characterSize = 0.06f;
            healthText.fontSize = 42;
            healthText.color = Color.white;

            trainingDummy.ConfigureForPlaytest(anchor.transform, healthText, dummy.GetComponentsInChildren<Renderer>());
            return dummy;
        }

        private static Material DummyMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader);
            material.name = "M_Playtest_Dummy_Runtime";
            ApplyMaterialColor(material, new Color(0.55f, 0.40f, 0.26f, 1f));
            return material;
        }

        private static void ApplyMaterial(GameObject target, Material material)
        {
            Renderer renderer = target.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static List<BlockoutElement> CollectBlockoutElements()
        {
            BlockoutElement[] allElements = FindObjectsByType<BlockoutElement>(FindObjectsInactive.Exclude);
            List<BlockoutElement> elements = new List<BlockoutElement>(allElements.Length);
            foreach (BlockoutElement element in allElements)
            {
                if (element != null)
                {
                    elements.Add(element);
                }
            }

            return elements;
        }

        private static BlockoutElement FindFirst(List<BlockoutElement> elements, BlockoutElementKind kind)
        {
            foreach (BlockoutElement element in elements)
            {
                if (element != null && element.kind == kind)
                {
                    return element;
                }
            }

            return null;
        }

        private static Vector3 PlayerRootPosition(BlockoutElement spawn)
        {
            Vector3 position = spawn.transform.position;
            position.y -= Mathf.Max(0f, spawn.logicalSize.y) * 0.5f;
            if (position.y < 0f)
            {
                position.y = 0f;
            }

            return position;
        }

        private static void HideGameplayMarkers(List<BlockoutElement> elements)
        {
            foreach (BlockoutElement element in elements)
            {
                if (element == null || !IsGameplayMarker(element.kind))
                {
                    continue;
                }

                Renderer[] renderers = element.GetComponentsInChildren<Renderer>();
                foreach (Renderer targetRenderer in renderers)
                {
                    targetRenderer.enabled = false;
                }
            }
        }

        private static bool IsGameplayMarker(BlockoutElementKind kind)
        {
            return kind == BlockoutElementKind.SpawnPoint
                || kind == BlockoutElementKind.EnemySpawn
                || kind == BlockoutElementKind.DummySpawn
                || kind == BlockoutElementKind.Objective
                || kind == BlockoutElementKind.TriggerVolume
                || kind == BlockoutElementKind.KillZone
                || kind == BlockoutElementKind.NavMarker
                || kind == BlockoutElementKind.CombatZone;
        }

        private static BlockoutElementKind GuessBlockoutKind(string objectName)
        {
            string name = objectName == null ? string.Empty : objectName.ToLowerInvariant();
            if (name.Contains("floor"))
            {
                return BlockoutElementKind.Floor;
            }

            if (name.Contains("wall"))
            {
                return BlockoutElementKind.Wall;
            }

            if (name.Contains("ramp"))
            {
                return BlockoutElementKind.Ramp;
            }

            if (name.Contains("platform"))
            {
                return BlockoutElementKind.Platform;
            }

            if (name.Contains("cover"))
            {
                return BlockoutElementKind.Cover;
            }

            return BlockoutElementKind.Block;
        }

        private static Color ColorFor(BlockoutElementKind kind)
        {
            switch (kind)
            {
                case BlockoutElementKind.Floor:
                    return new Color(0.22f, 0.25f, 0.28f, 1f);
                case BlockoutElementKind.Wall:
                    return new Color(0.30f, 0.34f, 0.38f, 1f);
                case BlockoutElementKind.Ramp:
                    return new Color(0.42f, 0.47f, 0.36f, 1f);
                case BlockoutElementKind.Platform:
                    return new Color(0.22f, 0.40f, 0.52f, 1f);
                case BlockoutElementKind.Cover:
                    return new Color(0.48f, 0.36f, 0.25f, 1f);
                case BlockoutElementKind.SpawnPoint:
                    return new Color(0.14f, 0.55f, 0.32f, 1f);
                case BlockoutElementKind.EnemySpawn:
                    return new Color(0.62f, 0.34f, 0.16f, 1f);
                case BlockoutElementKind.DummySpawn:
                    return new Color(0.58f, 0.48f, 0.20f, 1f);
                case BlockoutElementKind.CombatZone:
                    return new Color(0.26f, 0.48f, 0.25f, 1f);
                default:
                    return new Color(0.34f, 0.40f, 0.46f, 1f);
            }
        }

        private static void LogPlaytestRenderDiagnostic(Camera camera, List<BlockoutElement> elements)
        {
            int enabledCameraCount = 0;
            string enabledCameraNames = string.Empty;
            if (camera != null)
            {
                Scene scene = camera.gameObject.scene;
                Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include);
                foreach (Camera candidate in cameras)
                {
                    if (candidate == null || !candidate.enabled || candidate.gameObject.scene != scene)
                    {
                        continue;
                    }

                    enabledCameraCount++;
                    if (enabledCameraNames.Length > 0)
                    {
                        enabledCameraNames += ", ";
                    }

                    enabledCameraNames += candidate.name;
                }
            }

            bool urpPost = false;
            bool urpHdr = false;
            if (camera != null)
            {
                UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
                urpPost = cameraData.renderPostProcessing;
                urpHdr = cameraData.allowHDROutput;
            }

            int rendererCount = 0;
            string firstMaterial = "None";
            Color firstColor = Color.black;
            foreach (BlockoutElement element in elements)
            {
                if (element == null)
                {
                    continue;
                }

                Renderer renderer = element.GetComponentInChildren<Renderer>(true);
                if (renderer == null)
                {
                    continue;
                }

                rendererCount++;
                if (rendererCount == 1 && renderer.sharedMaterial != null)
                {
                    firstMaterial = renderer.sharedMaterial.name;
                    firstColor = renderer.sharedMaterial.color;
                }
            }

            Debug.Log("[ActPlaytestBootstrap/RenderDiag] camera="
                + (camera == null ? "None" : camera.name)
                + ", cameraHDR="
                + (camera != null && camera.allowHDR)
                + ", urpPost="
                + urpPost
                + ", urpHDROutput="
                + urpHdr
                + ", enabledCameras="
                + enabledCameraCount
                + " ["
                + enabledCameraNames
                + "], blockoutRenderers="
                + rendererCount
                + ", firstMaterial="
                + firstMaterial
                + ", firstColor="
                + firstColor,
                camera);
        }

        private static Quaternion YawOnly(Quaternion rotation)
        {
            Vector3 euler = rotation.eulerAngles;
            return Quaternion.Euler(0f, euler.y, 0f);
        }
    }
}
