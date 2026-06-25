using System.Collections.Generic;
using ActToolkit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
        private const string Ual1Path = "Assets/External/TestAssets/Animations/PreviewClips/Quaternius_UniversalAnimationLibrary_Standard/UAL1_Standard.fbx";
        private const string Ual2Path = "Assets/External/TestAssets/Animations/PreviewClips/Quaternius_UniversalAnimationLibrary2_Standard/UAL2_Standard.fbx";
        private const string DefaultIdleClipName = "Armature|Idle_Loop";
        private const string DefaultWalkClipName = "Armature|Walk_Loop";
        private const string DefaultMoveClipName = "Armature|Jog_Fwd_Loop";
        private const float MannequinVisualYawCorrection = 180f;
        private const float LocomotionDeadZone = 0.12f;
        private const float LocomotionBlendSpeed = 8f;
        private const float LocomotionWalkBlendPoint = 0.24f;
        private const float LocomotionWalkReferenceSpeed = 0.925f;
        private const float LocomotionMoveReferenceSpeed = 5.309f;
        private const float LocomotionPlaybackSpeedMin = 0.35f;
        private const float LocomotionPlaybackSpeedMax = 3f;
        private const float ActionToLocomotionBlendDuration = 0.14f;
        private const float KeyLightIntensity = 0.45f;
        private const float KeyLightShadowStrength = 0.45f;
        private const float AmbientIntensity = 0.55f;
        private const float ReflectionIntensity = 0.1f;
        private static readonly Color GroundColor = new Color(0.30f, 0.33f, 0.36f, 1f);
        private static readonly Color DummyColor = new Color(0.55f, 0.40f, 0.26f, 1f);
        private static readonly Color CameraBackgroundColor = new Color(0.18f, 0.20f, 0.22f, 1f);
        private static readonly Color AmbientColor = new Color(0.35f, 0.37f, 0.39f, 1f);
        private static readonly Color KeyLightColor = new Color(1f, 0.96f, 0.9f, 1f);
        private static readonly Color InstructionTextColor = new Color(0.70f, 0.88f, 1f, 1f);

        [MenuItem(ActToolkitMenu.PlaytestRoot + "/Open Combat Dummy MVP Scene", false, 100)]
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

        [MenuItem(ActToolkitMenu.PlaytestRoot + "/Create Combat Dummy MVP Scene", false, 101)]
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

            ConfigureNeutralTestLighting();

            Material groundMaterial = CreateMaterial(MvpFolder + "/M_Mvp_Ground.mat", GroundColor);
            Material dummyMaterial = CreateMaterial(MvpFolder + "/M_Mvp_Dummy.mat", DummyColor);

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
            List<AnimationClip> ual1Clips = LoadAnimationClips(Ual1Path, MannequinPath);
            List<AnimationClip> ual2Clips = LoadAnimationClips(Ual2Path, MannequinPath);
            AnimationClip idleClip = FindExactClip(ual1Clips, DefaultIdleClipName) ?? FindClip(ual1Clips, "idle", "stand");
            AnimationClip runClip = FindExactClip(ual1Clips, DefaultMoveClipName) ?? FindClip(ual1Clips, "jog", "run", "walk", "move");
            List<AnimationClip> attackClips = FindAttackClips(ual2Clips);
            AnimationClip light1Clip = FindExactClip(ual2Clips, "Armature|Sword_Regular_A") ?? (attackClips.Count > 0 ? attackClips[0] : idleClip);
            AnimationClip light2Clip = FindExactClip(ual2Clips, "Armature|Sword_Regular_B") ?? (attackClips.Count > 1 ? attackClips[1] : light1Clip);
            AnimationClip light3Clip = FindExactClip(ual2Clips, "Armature|Sword_Regular_C") ?? (attackClips.Count > 2 ? attackClips[2] : attackClips.Count > 0 ? attackClips[attackClips.Count - 1] : idleClip);

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
                    light1Clip,
                    10,
                    5,
                    18,
                    12,
                    10);

                CombatAnimationDefinition light2 = CreateOrUpdateAction(
                    ActionFolder + "/CA_Light_2.asset",
                    "action.light_2",
                    "Light 2",
                    light2Clip,
                    12,
                    5,
                    20,
                    12,
                    12);

                CombatAnimationDefinition light3 = CreateOrUpdateAction(
                    ActionFolder + "/CA_Light_3.asset",
                    "action.light_3",
                    "Light 3",
                    light3Clip,
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

            List<CombatAnimationDefinition> databaseActions = LoadDatabaseActions(lightActions);
            database.actions = databaseActions;
            SetEntryAction(database, CombatInputActionNames.LightAttack, lightActions.Count > 0 ? lightActions[0] : null);
            SetEntryAction(database, CombatInputActionNames.HeavyAttack, FindActionById(databaseActions, "action.heavy_1"));
            SetEntryAction(database, CombatInputActionNames.Jump, FindActionById(databaseActions, "action.jump"));
            SetEntryAction(database, CombatInputActionNames.Dodge, FindActionById(databaseActions, "action.roll"));
            SetEntryAction(database, CombatInputActionNames.Guard, FindActionById(databaseActions, "action.block"));
            SetEntryAction(database, CombatInputActionNames.ButtonR1, FindActionById(databaseActions, "action.throw"));
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
            profile.idleClip = LoadDefaultIdleClip();
            profile.walkClip = LoadDefaultWalkClip();
            profile.moveClip = LoadDefaultMoveClip();
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
                if (definition == null
                    || !IsLightActionDefinition(definition)
                    || !ActToolkitSkeletonCompatibility.IsDefinitionCompatibleWithModel(MannequinPath, definition))
                {
                    continue;
                }

                definitions.Add(definition);
            }

            definitions.Sort(CompareDefinitionsByLightOrder);
            return definitions;
        }

        private static List<CombatAnimationDefinition> LoadDatabaseActions(List<CombatAnimationDefinition> lightActions)
        {
            List<CombatAnimationDefinition> actions = new List<CombatAnimationDefinition>();
            HashSet<CombatAnimationDefinition> added = new HashSet<CombatAnimationDefinition>();

            AddActions(actions, added, lightActions);

            List<CombatAnimationDefinition> supplemental = new List<CombatAnimationDefinition>();
            string[] guids = AssetDatabase.FindAssets("t:CombatAnimationDefinition", new[] { ActionFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CombatAnimationDefinition definition = AssetDatabase.LoadAssetAtPath<CombatAnimationDefinition>(path);
                if (definition == null
                    || added.Contains(definition)
                    || !ActToolkitSkeletonCompatibility.IsDefinitionCompatibleWithModel(MannequinPath, definition))
                {
                    continue;
                }

                supplemental.Add(definition);
            }

            supplemental.Sort(CompareDefinitionsByDatabaseOrder);
            AddActions(actions, added, supplemental);
            return actions;
        }

        private static void AddActions(List<CombatAnimationDefinition> target, HashSet<CombatAnimationDefinition> added, List<CombatAnimationDefinition> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (CombatAnimationDefinition action in source)
            {
                if (action == null || added.Contains(action))
                {
                    continue;
                }

                target.Add(action);
                added.Add(action);
            }
        }

        private static CombatAnimationDefinition FindActionById(List<CombatAnimationDefinition> actions, string actionId)
        {
            if (actions == null || string.IsNullOrWhiteSpace(actionId))
            {
                return null;
            }

            foreach (CombatAnimationDefinition action in actions)
            {
                if (action != null && string.Equals(action.EnsureInternalActionId(), actionId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return action;
                }
            }

            return null;
        }

        private static bool IsLightActionDefinition(CombatAnimationDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            string actionId = definition.EnsureInternalActionId();
            if (actionId.StartsWith("action.light_", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string label = (definition.stateName + " " + definition.name).ToLowerInvariant();
            return label.Contains("light") || label.Contains("轻击") || label.Contains("轻攻击");
        }

        private static int CompareDefinitionsByLightOrder(CombatAnimationDefinition left, CombatAnimationDefinition right)
        {
            int leftOrder = LightActionOrder(left);
            int rightOrder = LightActionOrder(right);
            if (leftOrder != rightOrder)
            {
                return leftOrder.CompareTo(rightOrder);
            }

            return CompareDefinitionsByDisplayName(left, right);
        }

        private static int CompareDefinitionsByDatabaseOrder(CombatAnimationDefinition left, CombatAnimationDefinition right)
        {
            int leftOrder = DatabaseActionOrder(left);
            int rightOrder = DatabaseActionOrder(right);
            if (leftOrder != rightOrder)
            {
                return leftOrder.CompareTo(rightOrder);
            }

            return CompareDefinitionsByDisplayName(left, right);
        }

        private static int CompareDefinitionsByDisplayName(CombatAnimationDefinition left, CombatAnimationDefinition right)
        {
            string leftName = left == null ? string.Empty : left.DisplayName;
            string rightName = right == null ? string.Empty : right.DisplayName;
            return string.Compare(leftName, rightName, System.StringComparison.OrdinalIgnoreCase);
        }

        private static int LightActionOrder(CombatAnimationDefinition definition)
        {
            if (definition == null)
            {
                return int.MaxValue;
            }

            if (TryReadLastNumber(definition.actionId, out int actionIdIndex))
            {
                return actionIdIndex;
            }

            if (TryReadLastNumber(definition.stateName, out int stateIndex))
            {
                return stateIndex;
            }

            return TryReadLastNumber(definition.name, out int nameIndex) ? nameIndex : int.MaxValue;
        }

        private static int DatabaseActionOrder(CombatAnimationDefinition definition)
        {
            if (definition == null)
            {
                return int.MaxValue;
            }

            string actionId = definition.EnsureInternalActionId();
            if (actionId.StartsWith("action.light_", System.StringComparison.OrdinalIgnoreCase))
            {
                return LightActionOrder(definition);
            }

            if (string.Equals(actionId, "action.heavy_1", System.StringComparison.OrdinalIgnoreCase)) return 100;
            if (string.Equals(actionId, "action.jump", System.StringComparison.OrdinalIgnoreCase)) return 200;
            if (string.Equals(actionId, "action.jump_attack", System.StringComparison.OrdinalIgnoreCase)) return 210;
            if (string.Equals(actionId, "action.roll", System.StringComparison.OrdinalIgnoreCase)) return 300;
            if (string.Equals(actionId, "action.block", System.StringComparison.OrdinalIgnoreCase)) return 400;
            if (string.Equals(actionId, "action.throw", System.StringComparison.OrdinalIgnoreCase)) return 500;
            return 1000;
        }

        private static bool TryReadLastNumber(string value, out int number)
        {
            number = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            int end = -1;
            for (int i = value.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(value[i]))
                {
                    end = i;
                    break;
                }
            }

            if (end < 0)
            {
                return false;
            }

            int start = end;
            while (start > 0 && char.IsDigit(value[start - 1]))
            {
                start--;
            }

            return int.TryParse(value.Substring(start, end - start + 1), out number);
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
            int clampedStartFrame = Mathf.Clamp(startFrame, 0, frameCount);
            int clampedDurationFrames = Mathf.Clamp(durationFrames, 0, Mathf.Max(0, frameCount - clampedStartFrame));
            CombatAnimationMarker marker = new CombatAnimationMarker
            {
                kind = kind,
                normalizedTime = Mathf.Clamp01((float)clampedStartFrame / frameCount),
                duration = clampedDurationFrames / (float)frameRate,
                gameplayTag = tag,
                payload = payload,
                serverAuthoritative = kind != CombatAnimationEventKind.Vfx && kind != CombatAnimationEventKind.Sfx
            };

            switch (kind)
            {
                case CombatAnimationEventKind.Hitbox:
                    marker.localOffset = new Vector3(0f, 0.95f, 0.95f);
                    marker.size = new Vector3(1.1f, 0.9f, 1.15f);
                    marker.color = new Color(1f, 0.55f, 0.18f, 0.75f);
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
            int frameRate = Mathf.Max(1, source.authoringFrameRate);
            int frameCount = source.clip == null ? 0 : Mathf.Max(1, Mathf.RoundToInt(source.clip.length * frameRate));
            if (frameCount > 0)
            {
                startFrame = Mathf.Clamp(startFrame, 0, frameCount);
                endFrame = Mathf.Clamp(endFrame, startFrame, frameCount);
            }

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
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = CameraBackgroundColor;
            camera.allowHDR = false;
            ConfigureTestCameraRendering(camera);
            return camera;
        }

        private static void ConfigureTestCameraRendering(Camera camera)
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

        private static void CreateLight()
        {
            GameObject lightObject = new GameObject("Key Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = KeyLightColor;
            light.intensity = KeyLightIntensity;
            light.shadowStrength = KeyLightShadowStrength;
        }

        private static void ConfigureNeutralTestLighting()
        {
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientSkyColor = AmbientColor;
            RenderSettings.ambientEquatorColor = AmbientColor;
            RenderSettings.ambientGroundColor = AmbientColor;
            RenderSettings.ambientLight = AmbientColor;
            RenderSettings.ambientIntensity = AmbientIntensity;
            RenderSettings.reflectionIntensity = ReflectionIntensity;
            RenderSettings.flareStrength = 0f;
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
                : LoadDefaultIdleClip();
            AnimationClip walkClip = characterProfile != null && characterProfile.walkClip != null
                ? characterProfile.walkClip
                : LoadDefaultWalkClip();
            AnimationClip runClip = characterProfile != null && characterProfile.moveClip != null
                ? characterProfile.moveClip
                : LoadDefaultMoveClip();

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
            actorObject.FindProperty("walkClip").objectReferenceValue = walkClip;
            actorObject.FindProperty("moveClip").objectReferenceValue = runClip;
            actorObject.FindProperty("locomotionDeadZone").floatValue = LocomotionDeadZone;
            actorObject.FindProperty("locomotionBlendSpeed").floatValue = LocomotionBlendSpeed;
            actorObject.FindProperty("locomotionWalkBlendPoint").floatValue = LocomotionWalkBlendPoint;
            actorObject.FindProperty("locomotionWalkReferenceSpeed").floatValue = LocomotionWalkReferenceSpeed;
            actorObject.FindProperty("locomotionMoveReferenceSpeed").floatValue = LocomotionMoveReferenceSpeed;
            actorObject.FindProperty("locomotionPlaybackSpeedMin").floatValue = LocomotionPlaybackSpeedMin;
            actorObject.FindProperty("locomotionPlaybackSpeedMax").floatValue = LocomotionPlaybackSpeedMax;
            actorObject.FindProperty("actionToLocomotionBlendDuration").floatValue = ActionToLocomotionBlendDuration;
            actorObject.FindProperty("drawHitboxDebug").boolValue = true;
            actorObject.FindProperty("hitboxDebugLinger").floatValue = 0.18f;
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
            text.color = InstructionTextColor;
        }

        private static Material CreateMaterial(string path, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                ConfigureMaterial(material, color);
                EditorUtility.SetDirty(material);
                return material;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader);
            material.name = System.IO.Path.GetFileNameWithoutExtension(path);
            ConfigureMaterial(material, color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ConfigureMaterial(Material material, Color color)
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader != null && material.shader != unlitShader)
            {
                material.shader = unlitShader;
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

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.2f);
            }

            if (material.HasProperty("_SpecularHighlights"))
            {
                material.SetFloat("_SpecularHighlights", 0f);
            }

            if (material.HasProperty("_EnvironmentReflections"))
            {
                material.SetFloat("_EnvironmentReflections", 0f);
            }

            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", Color.black);
            }
        }

        private static void ApplyMaterial(GameObject target, Material material)
        {
            Renderer renderer = target.GetComponentInChildren<Renderer>();
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static List<AnimationClip> LoadAnimationClips(string path, string modelPath = null)
        {
            List<AnimationClip> clips = new List<AnimationClip>();
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object asset in assets)
            {
                if (asset is not AnimationClip clip)
                {
                    continue;
                }

                if (!clip.empty
                    && !clip.name.StartsWith("__", System.StringComparison.Ordinal)
                    && (string.IsNullOrWhiteSpace(modelPath) || ActToolkitSkeletonCompatibility.IsClipCompatibleWithModel(modelPath, clip)))
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

        private static AnimationClip FindExactClip(List<AnimationClip> clips, params string[] names)
        {
            foreach (string name in names)
            {
                foreach (AnimationClip clip in clips)
                {
                    if (string.Equals(clip.name, name, System.StringComparison.Ordinal))
                    {
                        return clip;
                    }
                }
            }

            return null;
        }

        private static AnimationClip LoadDefaultIdleClip()
        {
            return LoadClipByName(Ual1Path, DefaultIdleClipName);
        }

        private static AnimationClip LoadDefaultWalkClip()
        {
            return LoadClipByName(Ual1Path, DefaultWalkClipName);
        }

        private static AnimationClip LoadDefaultMoveClip()
        {
            return LoadClipByName(Ual1Path, DefaultMoveClipName);
        }

        private static AnimationClip LoadClipByName(string path, string clipName)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(clipName))
            {
                return null;
            }

            foreach (AnimationClip clip in LoadAnimationClips(path, MannequinPath))
            {
                if (string.Equals(clip.name, clipName, System.StringComparison.Ordinal))
                {
                    return clip;
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
