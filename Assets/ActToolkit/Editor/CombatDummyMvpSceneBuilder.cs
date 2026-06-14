using System.Collections.Generic;
using ActToolkit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ActToolkit.EditorTools
{
    public static class CombatDummyMvpSceneBuilder
    {
        private const string SceneFolder = ActToolkitEditorUtilities.GeneratedFolder + "/Scenes";
        private const string MvpFolder = ActToolkitEditorUtilities.GeneratedFolder + "/CombatMvp";
        private const string ActionFolder = ActToolkitEditorUtilities.DefaultCombatDefinitionFolder;
        private const string ScenePath = SceneFolder + "/CombatDummyMvp.unity";
        private const string CharacterProfilePath = MvpFolder + "/MVP_CharacterActionProfile.asset";
        private const string DatabasePath = MvpFolder + "/MVP_CombatActionDatabase.asset";
        private const string MannequinPath = "Assets/External/TestAssets/Characters/Quaternius_UniversalAnimationLibrary2_Standard/Mannequin_F.fbx";
        private const string Ual2Path = "Assets/External/TestAssets/Animations/PreviewClips/Quaternius_UniversalAnimationLibrary2_Standard/UAL2_Standard.fbx";
        private const float MannequinVisualYawCorrection = 180f;

        [MenuItem("Tools/Act Toolkit/Open Combat Dummy MVP Scene")]
        public static void OpenScene()
        {
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset == null)
            {
                CreateScene();
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Tools/Act Toolkit/Create Combat Dummy MVP Scene")]
        public static void CreateScene()
        {
            ActToolkitEditorUtilities.EnsureGeneratedFolders();
            ActToolkitEditorUtilities.EnsureFolder(ActToolkitEditorUtilities.GeneratedFolder, "Scenes");
            ActToolkitEditorUtilities.EnsureFolder(ActToolkitEditorUtilities.GeneratedFolder, "CombatMvp");
            ActToolkitEditorUtilities.EnsureFolder(MvpFolder, "Actions");

            CombatActionDatabase database = CreateOrUpdateActionDatabase();
            CharacterActionProfile characterProfile = CreateOrUpdateCharacterProfile(database);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "CombatDummyMvp";

            Material groundMaterial = CreateMaterial(MvpFolder + "/M_Mvp_Ground.mat", new Color(0.22f, 0.26f, 0.28f, 1f));
            Material dummyMaterial = CreateMaterial(MvpFolder + "/M_Mvp_Dummy.mat", new Color(0.72f, 0.52f, 0.32f, 1f));

            CreateGround(groundMaterial);
            Camera camera = CreateCamera();
            CreateLight();
            GameObject player = CreatePlayer(characterProfile, camera);
            CreateTrainingDummy(dummyMaterial);
            CreateWorldInstructionText();

            Selection.activeGameObject = player;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[ActToolkit] Created Combat Dummy MVP scene: " + ScenePath);
        }

        private static CombatActionDatabase CreateOrUpdateActionDatabase()
        {
            List<AnimationClip> ual2Clips = LoadAnimationClips(Ual2Path);
            AnimationClip idleClip = FindClip(ual2Clips, "idle", "stand");
            AnimationClip runClip = FindClip(ual2Clips, "run", "jog", "walk", "move");
            List<AnimationClip> attackClips = FindAttackClips(ual2Clips);

            List<CombatAnimationDefinition> lightActions = LoadAuthoredLightActions();
            if (lightActions.Count >= 3)
            {
                lightActions.RemoveRange(3, lightActions.Count - 3);
                ConfigureAuthoredLightChain(lightActions);
            }
            else
            {
                CombatAnimationDefinition light1 = CreateOrUpdateAction(
                    ActionFolder + "/CA_Light_1.asset",
                    "action.light_1",
                    "Light 1",
                    attackClips.Count > 0 ? attackClips[0] : idleClip,
                    10,
                    5,
                    18,
                    12,
                    10);

                CombatAnimationDefinition light2 = CreateOrUpdateAction(
                    ActionFolder + "/CA_Light_2.asset",
                    "action.light_2",
                    "Light 2",
                    attackClips.Count > 1 ? attackClips[1] : attackClips.Count > 0 ? attackClips[0] : idleClip,
                    12,
                    5,
                    20,
                    12,
                    12);

                CombatAnimationDefinition light3 = CreateOrUpdateAction(
                    ActionFolder + "/CA_Light_3.asset",
                    "action.light_3",
                    "Light 3",
                    attackClips.Count > 2 ? attackClips[2] : attackClips.Count > 0 ? attackClips[attackClips.Count - 1] : idleClip,
                    16,
                    7,
                    -1,
                    0,
                    16);

                SetAttackLink(light1, light2, 18, 30);
                SetAttackLink(light2, light3, 20, 34);
                ClearAttackLinks(light3);
                lightActions = new List<CombatAnimationDefinition> { light1, light2, light3 };
            }

            CombatActionDatabase database = AssetDatabase.LoadAssetAtPath<CombatActionDatabase>(DatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<CombatActionDatabase>();
                AssetDatabase.CreateAsset(database, DatabasePath);
            }

            database.actions = lightActions;
            SetEntryAction(database, CombatInputActionNames.LightAttack, lightActions.Count > 0 ? lightActions[0] : null);
            database.RebuildLookup();
            EditorUtility.SetDirty(database);

            // Store locomotion clips on a hidden helper asset through EditorPrefs would be overkill; the scene builder passes them directly.
            EditorPrefs.SetString("ActToolkit.Mvp.IdleClipPath", idleClip == null ? string.Empty : AssetDatabase.GetAssetPath(idleClip));
            EditorPrefs.SetString("ActToolkit.Mvp.IdleClipName", idleClip == null ? string.Empty : idleClip.name);
            EditorPrefs.SetString("ActToolkit.Mvp.RunClipPath", runClip == null ? string.Empty : AssetDatabase.GetAssetPath(runClip));
            EditorPrefs.SetString("ActToolkit.Mvp.RunClipName", runClip == null ? string.Empty : runClip.name);

            return database;
        }

        private static CharacterActionProfile CreateOrUpdateCharacterProfile(CombatActionDatabase database)
        {
            CharacterActionProfile profile = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(CharacterProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<CharacterActionProfile>();
                AssetDatabase.CreateAsset(profile, CharacterProfilePath);
            }

            profile.characterId = "character.mvp_mannequin_f";
            profile.displayName = "MVP Mannequin F";
            profile.modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MannequinPath);
            profile.avatar = LoadAvatarFromModel(MannequinPath);
            profile.idleClip = LoadClipFromEditorPrefs("ActToolkit.Mvp.IdleClipPath", "ActToolkit.Mvp.IdleClipName");
            profile.moveClip = LoadClipFromEditorPrefs("ActToolkit.Mvp.RunClipPath", "ActToolkit.Mvp.RunClipName");
            profile.comboTable = database;
            profile.EnsureDefaults();

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static List<CombatAnimationDefinition> LoadAuthoredLightActions()
        {
            List<CombatAnimationDefinition> definitions = new List<CombatAnimationDefinition>();
            string[] guids = AssetDatabase.FindAssets("t:CombatAnimationDefinition", new[] { ActionFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CombatAnimationDefinition definition = AssetDatabase.LoadAssetAtPath<CombatAnimationDefinition>(path);
                if (definition == null || definition.name.StartsWith("CA_Light_", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                definitions.Add(definition);
            }

            definitions.Sort(CompareDefinitionsByNaturalName);
            return definitions;
        }

        private static int CompareDefinitionsByNaturalName(CombatAnimationDefinition left, CombatAnimationDefinition right)
        {
            SplitTrailingNumber(left == null ? string.Empty : left.name, out string leftBase, out int leftIndex);
            SplitTrailingNumber(right == null ? string.Empty : right.name, out string rightBase, out int rightIndex);

            int baseCompare = string.Compare(leftBase, rightBase, System.StringComparison.OrdinalIgnoreCase);
            if (baseCompare != 0)
            {
                return baseCompare;
            }

            return leftIndex.CompareTo(rightIndex);
        }

        private static void SplitTrailingNumber(string value, out string baseName, out int index)
        {
            baseName = value == null ? string.Empty : value.Trim();
            index = 0;

            int spaceIndex = baseName.LastIndexOf(' ');
            if (spaceIndex < 0 || spaceIndex >= baseName.Length - 1)
            {
                return;
            }

            string suffix = baseName.Substring(spaceIndex + 1);
            if (!int.TryParse(suffix, out int parsedIndex))
            {
                return;
            }

            baseName = baseName.Substring(0, spaceIndex);
            index = parsedIndex;
        }

        private static void ConfigureAuthoredLightChain(List<CombatAnimationDefinition> lightActions)
        {
            for (int i = 0; i < lightActions.Count; i++)
            {
                CombatAnimationDefinition action = lightActions[i];
                if (action == null)
                {
                    continue;
                }

                action.actionId = "action.light_" + (i + 1);
                action.requiresNetworkSync = true;
                action.EnsureMarkers();
                action.EnsureActionLinks();

                if (string.IsNullOrWhiteSpace(action.stateName) || action.stateName == "NewAction")
                {
                    action.stateName = "Light " + (i + 1);
                }

                EditorUtility.SetDirty(action);
            }

            SetAttackLinkFromComboMarker(lightActions[0], lightActions[1], 18, 30);
            SetAttackLinkFromComboMarker(lightActions[1], lightActions[2], 20, 34);
            ClearAttackLinks(lightActions[2]);
        }

        private static CombatAnimationDefinition CreateOrUpdateAction(string path, string actionId, string stateName, AnimationClip clip, int hitStartFrame, int hitDurationFrames, int comboStartFrame, int comboDurationFrames, int damage)
        {
            CombatAnimationDefinition definition = AssetDatabase.LoadAssetAtPath<CombatAnimationDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<CombatAnimationDefinition>();
                definition.actionId = actionId;
                definition.stateName = stateName;
                definition.authoringFrameRate = 60;
                definition.requiresNetworkSync = true;
                definition.clip = clip;
                AssetDatabase.CreateAsset(definition, path);
            }
            else if (definition.clip == null)
            {
                definition.clip = clip;
            }

            definition.EnsureMarkers();
            if (definition.markers.Count == 0)
            {
                definition.markers.Add(CreateMarker(CombatAnimationEventKind.NetworkSync, definition, 0, 0, "net.sync.action", string.Empty));
                definition.markers.Add(CreateMarker(CombatAnimationEventKind.MovementLock, definition, 0, Mathf.Max(24, hitStartFrame + hitDurationFrames + 8), "movement.lock", string.Empty));
                definition.markers.Add(CreateMarker(CombatAnimationEventKind.Hitbox, definition, hitStartFrame, hitDurationFrames, "combat.hitbox.light", "damage=" + damage));

                if (comboStartFrame >= 0 && comboDurationFrames > 0)
                {
                    definition.markers.Add(CreateMarker(CombatAnimationEventKind.ComboBranch, definition, comboStartFrame, comboDurationFrames, "combat.combo.branch", string.Empty));
                }

                definition.SortMarkers();
            }

            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static CombatAnimationMarker CreateMarker(CombatAnimationEventKind kind, CombatAnimationDefinition definition, int startFrame, int durationFrames, string tag, string payload)
        {
            int frameRate = Mathf.Max(1, definition.authoringFrameRate);
            int frameCount = definition.clip == null ? 60 : Mathf.Max(1, Mathf.RoundToInt(definition.clip.length * frameRate));
            CombatAnimationMarker marker = new CombatAnimationMarker
            {
                kind = kind,
                normalizedTime = Mathf.Clamp01((float)Mathf.Clamp(startFrame, 0, frameCount) / frameCount),
                duration = Mathf.Max(0, durationFrames) / (float)frameRate,
                gameplayTag = tag,
                payload = payload,
                serverAuthoritative = kind != CombatAnimationEventKind.Vfx && kind != CombatAnimationEventKind.Sfx
            };

            switch (kind)
            {
                case CombatAnimationEventKind.Hitbox:
                    marker.localOffset = new Vector3(0f, 1.05f, 1.05f);
                    marker.size = new Vector3(1.15f, 1.0f, 1.2f);
                    marker.color = new Color(1f, 0.42f, 0.12f, 0.75f);
                    break;
                case CombatAnimationEventKind.MovementLock:
                    marker.color = new Color(0.8f, 0.55f, 0.35f, 0.65f);
                    break;
                case CombatAnimationEventKind.ComboBranch:
                    marker.color = new Color(0.25f, 0.9f, 0.45f, 0.7f);
                    break;
                case CombatAnimationEventKind.NetworkSync:
                    marker.color = new Color(1f, 0.95f, 0.25f, 0.95f);
                    break;
            }

            return marker;
        }

        private static void SetAttackLinkFromComboMarker(CombatAnimationDefinition source, CombatAnimationDefinition target, int fallbackStartFrame, int fallbackEndFrame)
        {
            int startFrame = fallbackStartFrame;
            int endFrame = fallbackEndFrame;

            if (TryGetComboMarkerWindow(source, out int markerStartFrame, out int markerEndFrame))
            {
                startFrame = markerStartFrame;
                endFrame = markerEndFrame;
            }

            SetAttackLink(source, target, startFrame, endFrame);
        }

        private static bool TryGetComboMarkerWindow(CombatAnimationDefinition source, out int startFrame, out int endFrame)
        {
            startFrame = 0;
            endFrame = 0;
            if (source == null || source.markers == null)
            {
                return false;
            }

            int frameRate = Mathf.Max(1, source.authoringFrameRate);
            foreach (CombatAnimationMarker marker in source.markers)
            {
                if (marker == null || marker.kind != CombatAnimationEventKind.ComboBranch)
                {
                    continue;
                }

                startFrame = marker.StartFrame(source.clip, frameRate);
                endFrame = marker.EndFrame(source.clip, frameRate);
                return endFrame > startFrame;
            }

            return false;
        }

        private static void SetAttackLink(CombatAnimationDefinition source, CombatAnimationDefinition target, int startFrame, int endFrame)
        {
            if (source == null || target == null)
            {
                return;
            }

            ClearAttackLinks(source);
            source.EnsureActionLinks();
            source.actionLinks.Add(new CombatActionLink
            {
                inputAction = CombatInputActionNames.LightAttack,
                triggerTag = "combat.combo.branch",
                targetDefinition = target,
                targetActionId = target.actionId,
                startFrame = startFrame,
                endFrame = endFrame,
                serverAuthoritative = true
            });

            EditorUtility.SetDirty(source);
        }

        private static void ClearAttackLinks(CombatAnimationDefinition source)
        {
            if (source == null)
            {
                return;
            }

            source.EnsureActionLinks();
            for (int i = source.actionLinks.Count - 1; i >= 0; i--)
            {
                CombatActionLink link = source.actionLinks[i];
                if (link == null || CombatInputActionNames.Matches(link.inputAction, CombatInputActionNames.LightAttack))
                {
                    source.actionLinks.RemoveAt(i);
                }
            }

            EditorUtility.SetDirty(source);
        }

        private static void SetEntryAction(CombatActionDatabase database, string inputAction, CombatAnimationDefinition target)
        {
            if (database == null || target == null)
            {
                return;
            }

            database.EnsureEntryActions();
            for (int i = database.entryActions.Count - 1; i >= 0; i--)
            {
                CombatActionEntry entry = database.entryActions[i];
                if (entry == null || CombatInputActionNames.Matches(entry.inputAction, inputAction))
                {
                    database.entryActions.RemoveAt(i);
                }
            }

            database.entryActions.Add(new CombatActionEntry
            {
                inputAction = CombatInputActionNames.Normalize(inputAction),
                targetDefinition = target,
                targetActionId = target.actionId,
                serverAuthoritative = true
            });
        }

        private static GameObject CreateGround(Material material)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Arena_Ground";
            ground.transform.position = new Vector3(0f, -0.05f, 1.5f);
            ground.transform.localScale = new Vector3(10f, 0.1f, 10f);
            ApplyMaterial(ground, material);
            return ground;
        }

        private static Camera CreateCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 3.6f, -5.8f);
            cameraObject.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 52f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            return camera;
        }

        private static void CreateLight()
        {
            GameObject lightObject = new GameObject("Key Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
        }

        private static GameObject CreatePlayer(CharacterActionProfile characterProfile, Camera camera)
        {
            GameObject modelAsset = characterProfile == null || characterProfile.modelPrefab == null
                ? AssetDatabase.LoadAssetAtPath<GameObject>(MannequinPath)
                : characterProfile.modelPrefab;
            GameObject player = new GameObject("Player");
            player.name = "Player";
            player.transform.position = new Vector3(0f, 0f, -1.2f);
            player.transform.rotation = Quaternion.identity;

            GameObject visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(player.transform, false);
            visualRoot.transform.localPosition = Vector3.zero;
            visualRoot.transform.localRotation = Quaternion.identity;
            visualRoot.transform.localScale = Vector3.one;

            GameObject facingCorrection = new GameObject("ModelFacingCorrection_Yaw" + Mathf.RoundToInt(MannequinVisualYawCorrection));
            facingCorrection.transform.SetParent(visualRoot.transform, false);
            facingCorrection.transform.localPosition = Vector3.zero;
            facingCorrection.transform.localRotation = Quaternion.Euler(0f, MannequinVisualYawCorrection, 0f);
            facingCorrection.transform.localScale = Vector3.one;

            GameObject visualModel = modelAsset == null
                ? GameObject.CreatePrimitive(PrimitiveType.Capsule)
                : PrefabUtility.InstantiatePrefab(modelAsset, facingCorrection.transform) as GameObject;

            if (visualModel != null)
            {
                visualModel.name = modelAsset == null ? "Visual_Placeholder" : "Mannequin_F";
                visualModel.transform.SetParent(facingCorrection.transform, false);
                visualModel.transform.localPosition = Vector3.zero;
                visualModel.transform.localRotation = Quaternion.identity;
                visualModel.transform.localScale = Vector3.one;

                if (modelAsset == null)
                {
                    Collider visualCollider = visualModel.GetComponent<Collider>();
                    if (visualCollider != null)
                    {
                        Object.DestroyImmediate(visualCollider);
                    }
                }
            }

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller == null)
            {
                controller = player.AddComponent<CharacterController>();
            }

            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.height = 1.8f;
            controller.radius = 0.35f;

            PlayerCombatGamepadInput input = player.GetComponent<PlayerCombatGamepadInput>();
            if (input == null)
            {
                input = player.AddComponent<PlayerCombatGamepadInput>();
            }

            CombatActor actor = player.GetComponent<CombatActor>();
            if (actor == null)
            {
                actor = player.AddComponent<CombatActor>();
            }

            Avatar avatar = characterProfile != null && characterProfile.avatar != null ? characterProfile.avatar : LoadAvatarFromModel(MannequinPath);
            Animator animator = visualModel == null ? null : visualModel.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                animator = visualModel == null ? visualRoot.AddComponent<Animator>() : visualModel.AddComponent<Animator>();
            }

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

            AnimationClip idleClip = characterProfile != null && characterProfile.idleClip != null
                ? characterProfile.idleClip
                : LoadClipFromEditorPrefs("ActToolkit.Mvp.IdleClipPath", "ActToolkit.Mvp.IdleClipName");
            AnimationClip runClip = characterProfile != null && characterProfile.moveClip != null
                ? characterProfile.moveClip
                : LoadClipFromEditorPrefs("ActToolkit.Mvp.RunClipPath", "ActToolkit.Mvp.RunClipName");

            SerializedObject actorObject = new SerializedObject(actor);
            actorObject.FindProperty("input").objectReferenceValue = input;
            actorObject.FindProperty("characterProfile").objectReferenceValue = characterProfile;
            actorObject.FindProperty("animator").objectReferenceValue = animator;
            actorObject.FindProperty("fallbackAvatar").objectReferenceValue = avatar;
            actorObject.FindProperty("characterController").objectReferenceValue = controller;
            actorObject.FindProperty("gameplayCamera").objectReferenceValue = camera;
            actorObject.FindProperty("actionDatabase").objectReferenceValue = characterProfile == null ? null : characterProfile.comboTable;
            actorObject.FindProperty("firstAttackActionId").stringValue = "action.light_1";
            actorObject.FindProperty("attackInputBuffer").floatValue = 0.35f;
            actorObject.FindProperty("idleClip").objectReferenceValue = idleClip;
            actorObject.FindProperty("moveClip").objectReferenceValue = runClip;
            actorObject.ApplyModifiedPropertiesWithoutUndo();

            return player;
        }

        private static Avatar LoadAvatarFromModel(string path)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object asset in assets)
            {
                if (asset is Avatar avatar)
                {
                    return avatar;
                }
            }

            return null;
        }

        private static GameObject CreateTrainingDummy(Material material)
        {
            GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dummy.name = "TrainingDummy";
            dummy.transform.position = new Vector3(0f, 0.9f, 1.45f);
            dummy.transform.localScale = new Vector3(0.55f, 0.9f, 0.55f);
            ApplyMaterial(dummy, material);

            TrainingDummy trainingDummy = dummy.AddComponent<TrainingDummy>();

            GameObject hurtbox = new GameObject("Hurtbox");
            hurtbox.transform.SetParent(dummy.transform, false);
            hurtbox.transform.localPosition = Vector3.zero;
            BoxCollider hurtboxCollider = hurtbox.AddComponent<BoxCollider>();
            hurtboxCollider.isTrigger = true;
            hurtboxCollider.center = Vector3.zero;
            hurtboxCollider.size = new Vector3(1.2f, 2.0f, 1.2f);
            hurtbox.AddComponent<TrainingDummyHurtbox>();

            GameObject anchor = new GameObject("FloatingTextAnchor");
            anchor.transform.SetParent(dummy.transform, false);
            anchor.transform.localPosition = new Vector3(0f, 1.45f, 0f);

            GameObject healthTextObject = new GameObject("HealthText");
            healthTextObject.transform.SetParent(dummy.transform, false);
            healthTextObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            healthTextObject.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            TextMesh healthText = healthTextObject.AddComponent<TextMesh>();
            healthText.anchor = TextAnchor.MiddleCenter;
            healthText.alignment = TextAlignment.Center;
            healthText.characterSize = 0.06f;
            healthText.fontSize = 42;
            healthText.color = Color.white;

            SerializedObject dummyObject = new SerializedObject(trainingDummy);
            dummyObject.FindProperty("floatingTextAnchor").objectReferenceValue = anchor.transform;
            dummyObject.FindProperty("healthText").objectReferenceValue = healthText;
            dummyObject.ApplyModifiedPropertiesWithoutUndo();

            return dummy;
        }

        private static void CreateWorldInstructionText()
        {
            GameObject textObject = new GameObject("World_Controls_Label");
            textObject.transform.position = new Vector3(-2.8f, 1.2f, -0.4f);
            textObject.transform.rotation = Quaternion.Euler(0f, 25f, 0f);

            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = "PS5 Controller\nLeft Stick: Move\nSquare: Light Combo";
            text.anchor = TextAnchor.MiddleLeft;
            text.alignment = TextAlignment.Left;
            text.characterSize = 0.075f;
            text.fontSize = 38;
            text.color = new Color(0.78f, 0.92f, 1f, 1f);
        }

        private static Material CreateMaterial(string path, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader);
            material.name = System.IO.Path.GetFileNameWithoutExtension(path);
            material.color = color;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ApplyMaterial(GameObject target, Material material)
        {
            Renderer renderer = target.GetComponentInChildren<Renderer>();
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static List<AnimationClip> LoadAnimationClips(string path)
        {
            List<AnimationClip> clips = new List<AnimationClip>();
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object asset in assets)
            {
                if (asset is AnimationClip clip && !clip.empty && !clip.name.StartsWith("__", System.StringComparison.Ordinal))
                {
                    clips.Add(clip);
                }
            }

            return clips;
        }

        private static AnimationClip FindClip(List<AnimationClip> clips, params string[] keywords)
        {
            foreach (string keyword in keywords)
            {
                foreach (AnimationClip clip in clips)
                {
                    if (clip.name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return clip;
                    }
                }
            }

            return null;
        }

        private static List<AnimationClip> FindAttackClips(List<AnimationClip> clips)
        {
            List<AnimationClip> scored = new List<AnimationClip>();
            foreach (AnimationClip clip in clips)
            {
                string name = clip.name.ToLowerInvariant();
                if (name.Contains("attack")
                    || name.Contains("punch")
                    || name.Contains("kick")
                    || name.Contains("slash")
                    || name.Contains("stab")
                    || name.Contains("throw")
                    || name.Contains("overhand"))
                {
                    scored.Add(clip);
                }
            }

            if (scored.Count == 0)
            {
                foreach (AnimationClip clip in clips)
                {
                    string name = clip.name.ToLowerInvariant();
                    if (!name.Contains("idle") && !name.Contains("run") && !name.Contains("walk"))
                    {
                        scored.Add(clip);
                    }
                }
            }

            scored.Sort((left, right) => AttackScore(right).CompareTo(AttackScore(left)));
            return scored;
        }

        private static int AttackScore(AnimationClip clip)
        {
            string name = clip.name.ToLowerInvariant();
            int score = 0;
            if (name.Contains("punch")) score += 30;
            if (name.Contains("attack")) score += 25;
            if (name.Contains("slash")) score += 20;
            if (name.Contains("kick")) score += 18;
            if (name.Contains("throw")) score += 12;
            if (name.Contains("overhand")) score += 10;
            return score;
        }

        private static AnimationClip LoadClipFromEditorPrefs(string pathKey, string nameKey)
        {
            string path = EditorPrefs.GetString(pathKey, string.Empty);
            string clipName = EditorPrefs.GetString(nameKey, string.Empty);
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(clipName))
            {
                return null;
            }

            foreach (AnimationClip clip in LoadAnimationClips(path))
            {
                if (clip.name == clipName)
                {
                    return clip;
                }
            }

            return null;
        }
    }
}
