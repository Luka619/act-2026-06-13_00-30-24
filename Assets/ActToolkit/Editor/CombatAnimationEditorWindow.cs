using System;
using System.Collections.Generic;
using System.IO;
using ActToolkit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ActToolkit.EditorTools
{
    public sealed class CombatAnimationEditorWindow : EditorWindow
    {
        private const string CharacterProfilePrefsKey = "ActToolkit.CharacterActionEditor.Profile";
        private const string DefaultCharacterProfilePath = ActToolkitEditorUtilities.CombatMvpFolder + "/MVP_CharacterActionProfile.asset";
        private const string ModelFolderPrefsKey = "ActToolkit.CombatAnimationEditor.ModelFolder";
        private const string AnimationFolderPrefsKey = "ActToolkit.CombatAnimationEditor.AnimationFolder";
        private const string PreviewModelNamePrefix = "ActPreview_";
        private const string LegacyPreviewModelNamePrefix = "Preview_";
        private const string PreviewLightName = "Preview Key Light";
        private const string PreviewSampleRootName = "Preview Model";
        private const double EditorRepaintInterval = 1d / 15d;
        private const string PreviewSceneName = "ActAnimationPreview";

        private GameObject previewRoot;
        private Animator previewAnimator;
        private Transform previewSampleTransform;
        private Vector3 previewSampleInitialLocalPosition;
        private Quaternion previewSampleInitialLocalRotation = Quaternion.identity;
        private Vector3 previewSampleInitialLocalScale = Vector3.one;
        private CharacterActionProfile characterProfile;
        private AnimationClip clip;
        private CombatAnimationDefinition definition;
        private readonly List<ModelCandidate> modelCandidates = new List<ModelCandidate>();
        private readonly HashSet<string> selectedModelTransformPaths = new HashSet<string>();
        private string[] modelLabels = Array.Empty<string>();
        private string modelFolder = ActToolkitEditorUtilities.DefaultModelFolder;
        private int selectedModelIndex = -1;
        private readonly List<AnimationClipCandidate> animationCandidates = new List<AnimationClipCandidate>();
        private string[] animationLabels = Array.Empty<string>();
        private string animationFolder = ActToolkitEditorUtilities.DefaultPreviewClipFolder;
        private int selectedAnimationIndex = -1;
        private int hiddenIncompatibleAnimationCount;
        private string cachedSelectedModelAssetPath;
        private GameObject cachedSelectedModelAsset;
        private int selectedMarkerIndex = -1;
        private Vector2 mainScroll;
        private Vector2 markerScroll;
        private float normalizedTime;
        private bool isPlaying;
        private bool drawAllMarkers = true;
        private bool snapToFrames = true;
        private bool showValidation = true;
        private bool showCharacterActionGraph = true;
        private bool showAssetDrawer = true;
        private bool showRootMotionDrawer;
        private bool showActionLinksDrawer;
        private CombatComboGraphView characterActionGraphView;
        private CombatAnimationEventKind markerTemplate = CombatAnimationEventKind.Hitbox;
        private ActionRecipe actionRecipe = ActionRecipe.LightAttack;
        private readonly List<float> rootMotionDistances = new List<float>();
        private readonly List<float> rootMotionSpeeds = new List<float>();
        private float rootMotionTotalDistance;
        private float rootMotionPeakSpeed;
        private string rootMotionStatus = "Not analyzed.";
        private string cachedTransformPathModelPath;
        private double lastUpdateTime;
        private double nextEditorRepaintTime;
        private bool previewSceneRefreshScheduled;
        private bool scheduledPreviewSceneCreateNew;

        [MenuItem("Tools/Act Toolkit/Character Action Editor")]
        [MenuItem("Tools/Act Toolkit/Combat Animation Editor")]
        public static void Open()
        {
            CombatAnimationEditorWindow window = GetWindow<CombatAnimationEditorWindow>();
            window.titleContent = new GUIContent("Character Actions");
            window.minSize = new Vector2(860f, 680f);
            window.Show();
        }

        private void OnEnable()
        {
            modelFolder = EditorPrefs.GetString(ModelFolderPrefsKey, ActToolkitEditorUtilities.DefaultModelFolder);
            animationFolder = EditorPrefs.GetString(AnimationFolderPrefsKey, ActToolkitEditorUtilities.DefaultPreviewClipFolder);
            characterActionGraphView = new CombatComboGraphView(Repaint);
            characterActionGraphView.Initialize();
            LoadCharacterProfileFromPrefs();
            ClearSpawnedPreviewModels();
            RefreshModelLibrary(false);
            RefreshAnimationLibrary(false);
            ApplyCharacterProfileToEditor(false);
            EditorApplication.update += EditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChangedInEditMode;
            SceneView.duringSceneGui += DuringSceneGui;
            SchedulePreviewSceneRefresh(true);
        }

        private void OnDisable()
        {
            EditorApplication.delayCall -= RunScheduledPreviewSceneRefresh;
            EditorApplication.update -= EditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChangedInEditMode;
            SceneView.duringSceneGui -= DuringSceneGui;
            previewSceneRefreshScheduled = false;
            scheduledPreviewSceneCreateNew = false;
            StopPreview();
            ClearSpawnedPreviewModels();
            characterActionGraphView = null;
        }

        private void OnSceneSaving(Scene scene, string path)
        {
            StopPreview();
            ClearSpawnedPreviewModels();
        }

        private void OnActiveSceneChangedInEditMode(Scene previousScene, Scene nextScene)
        {
            StopPreview();
            ClearSpawnedPreviewModels();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode)
            {
                return;
            }

            StopPreview();
            ClearSpawnedPreviewModels();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6f);
            DrawCharacterProfileHeader();
            EditorGUILayout.Space(4f);
            DrawStatusHeader();
            EditorGUILayout.Space(4f);
            DrawQuickActions();

            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
            try
            {
                DrawWorkspace();
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawStatusHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Character Action Editor", EditorStyles.boldLabel, GUILayout.Width(190f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawStatusCell("Character", GetCharacterStatus());
            DrawStatusCell("Model", GetModelStatus());
            DrawStatusCell("Clip", GetClipStatus());
            DrawStatusCell("Action", GetDefinitionStatus());
            DrawStatusCell("Frame", GetFrameStatus());
            DrawStatusCell("Selected", GetSelectedMarkerStatus());
            EditorGUILayout.EndHorizontal();

            if (definition == null || clip == null)
            {
                EditorGUILayout.HelpBox("Choose or create a character profile, then bind its model, combo table, and actions. Each action owns its animation and hitbox data.", MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCharacterProfileHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Character", EditorStyles.boldLabel, GUILayout.Width(76f));

            EditorGUI.BeginChangeCheck();
            characterProfile = (CharacterActionProfile)EditorGUILayout.ObjectField(characterProfile, typeof(CharacterActionProfile), false, GUILayout.MinWidth(180f));
            if (EditorGUI.EndChangeCheck())
            {
                SaveCharacterProfilePrefs();
                ApplyCharacterProfileToEditor(true);
            }

            using (new EditorGUI.DisabledScope(characterProfile == null))
            {
                if (GUILayout.Button("Apply", GUILayout.Width(54f)))
                {
                    ApplyCharacterProfileToEditor(true);
                }
            }

            if (GUILayout.Button("Create", GUILayout.Width(58f)))
            {
                CreateCharacterProfileAsset();
            }

            GUILayout.FlexibleSpace();
            if (characterProfile != null)
            {
                characterProfile.EnsureDefaults();
                GUILayout.Label(characterProfile.displayName + "  |  Combo Table: " + (characterProfile.comboTable == null ? "None" : characterProfile.comboTable.name), EditorStyles.miniLabel);
            }
            else
            {
                GUILayout.Label("Pick or create a character profile first.", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();

            if (characterProfile != null)
            {
                EditorGUI.BeginChangeCheck();
                characterProfile.characterId = EditorGUILayout.TextField("Character Id", characterProfile.characterId);
                characterProfile.displayName = EditorGUILayout.TextField("Display Name", characterProfile.displayName);
                characterProfile.modelPrefab = (GameObject)EditorGUILayout.ObjectField("Model", characterProfile.modelPrefab, typeof(GameObject), false);
                characterProfile.avatar = (Avatar)EditorGUILayout.ObjectField("Avatar", characterProfile.avatar, typeof(Avatar), false);
                characterProfile.comboTable = (CombatActionDatabase)EditorGUILayout.ObjectField("Combo Table", characterProfile.comboTable, typeof(CombatActionDatabase), false);
                characterProfile.idleClip = (AnimationClip)EditorGUILayout.ObjectField("Idle Clip", characterProfile.idleClip, typeof(AnimationClip), false);
                characterProfile.moveClip = (AnimationClip)EditorGUILayout.ObjectField("Move Clip", characterProfile.moveClip, typeof(AnimationClip), false);
                if (EditorGUI.EndChangeCheck())
                {
                    characterProfile.EnsureDefaults();
                    EditorUtility.SetDirty(characterProfile);
                    ApplyCharacterProfileToEditor(true);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void LoadCharacterProfileFromPrefs()
        {
            string profilePath = EditorPrefs.GetString(CharacterProfilePrefsKey, DefaultCharacterProfilePath);
            characterProfile = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(profilePath);
            if (characterProfile == null)
            {
                characterProfile = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(DefaultCharacterProfilePath);
            }
        }

        private void SaveCharacterProfilePrefs()
        {
            string path = characterProfile == null ? string.Empty : AssetDatabase.GetAssetPath(characterProfile);
            EditorPrefs.SetString(CharacterProfilePrefsKey, path);
        }

        private void CreateCharacterProfileAsset()
        {
            ActToolkitEditorUtilities.EnsureGeneratedFolders();

            CharacterActionProfile profile = CreateInstance<CharacterActionProfile>();
            profile.characterId = "character.new";
            profile.displayName = "New Character";
            profile.modelPrefab = LoadSelectedModelAsset();
            profile.avatar = LoadAvatarFromModel(GetSelectedModelPath());
            profile.comboTable = characterActionGraphView == null ? LoadOrCreateDefaultComboTable() : characterActionGraphView.Database ?? LoadOrCreateDefaultComboTable();
            profile.idleClip = null;
            profile.moveClip = null;
            profile.EnsureDefaults();

            string path = AssetDatabase.GenerateUniqueAssetPath(ActToolkitEditorUtilities.CombatMvpFolder + "/CharacterActionProfile.asset");
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();

            characterProfile = profile;
            SaveCharacterProfilePrefs();
            ApplyCharacterProfileToEditor(true);
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        private CombatActionDatabase LoadOrCreateDefaultComboTable()
        {
            ActToolkitEditorUtilities.EnsureGeneratedFolders();

            string path = ActToolkitEditorUtilities.CombatMvpFolder + "/MVP_CombatActionDatabase.asset";
            CombatActionDatabase database = AssetDatabase.LoadAssetAtPath<CombatActionDatabase>(path);
            if (database != null)
            {
                return database;
            }

            database = CreateInstance<CombatActionDatabase>();
            AssetDatabase.CreateAsset(database, path);
            AssetDatabase.SaveAssets();
            return database;
        }

        private void ApplyCharacterProfileToEditor(bool refreshPreview)
        {
            if (characterProfile == null)
            {
                return;
            }

            characterProfile.EnsureDefaults();
            EditorUtility.SetDirty(characterProfile);

            if (characterActionGraphView != null)
            {
                characterActionGraphView.SetDatabase(characterProfile.comboTable, true);
            }

            SelectProfileModel();
            SelectFirstProfileAction();

            if (refreshPreview)
            {
                RefreshAnimationLibrary(false);
                SchedulePreviewSceneRefresh(true);
            }
        }

        private void SelectProfileModel()
        {
            if (characterProfile == null || characterProfile.modelPrefab == null)
            {
                return;
            }

            string modelPath = AssetDatabase.GetAssetPath(characterProfile.modelPrefab);
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return;
            }

            int index = FindModelCandidateIndex(modelPath);
            if (index < 0)
            {
                string folder = Path.GetDirectoryName(modelPath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    modelFolder = folder.Replace('\\', '/');
                    EditorPrefs.SetString(ModelFolderPrefsKey, modelFolder);
                    RefreshModelLibrary(false);
                    index = FindModelCandidateIndex(modelPath);
                }
            }

            if (index >= 0)
            {
                selectedModelIndex = index;
                cachedSelectedModelAssetPath = string.Empty;
                cachedSelectedModelAsset = null;
            }
        }

        private int FindModelCandidateIndex(string modelPath)
        {
            for (int i = 0; i < modelCandidates.Count; i++)
            {
                if (string.Equals(modelCandidates[i].path, modelPath, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private void SelectFirstProfileAction()
        {
            CombatAnimationDefinition firstAction = characterProfile == null || characterProfile.comboTable == null
                ? null
                : characterProfile.comboTable.FirstAction();
            if (firstAction == null)
            {
                return;
            }

            definition = firstAction;
            clip = definition.clip;
            normalizedTime = 0f;
        }

        private static void DrawStatusCell(string label, string value)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(96f), GUILayout.Height(42f));
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(new GUIContent(ShortenMiddle(value, 22), value), EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private static string ShortenMiddle(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            int left = Mathf.Max(1, maxLength / 2 - 1);
            int right = Mathf.Max(1, maxLength - left - 3);
            return value.Substring(0, left) + "..." + value.Substring(value.Length - right);
        }

        private void DrawQuickActions()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            using (new EditorGUI.DisabledScope(clip == null))
            {
                if (GUILayout.Button("Create Definition", EditorStyles.toolbarButton, GUILayout.Width(102f)))
                {
                    CreateDefinitionAsset();
                }
            }

            GUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(definition == null))
            {
                if (GUILayout.Button("Add Hitbox", EditorStyles.toolbarButton, GUILayout.Width(74f)))
                {
                    AddMarker(CombatAnimationEventKind.Hitbox);
                }

                if (GUILayout.Button("Add Combo", EditorStyles.toolbarButton, GUILayout.Width(78f)))
                {
                    AddMarker(CombatAnimationEventKind.ComboBranch);
                }

                if (GUILayout.Button("Net Sync", EditorStyles.toolbarButton, GUILayout.Width(68f)))
                {
                    AddMarker(CombatAnimationEventKind.NetworkSync);
                }

                if (GUILayout.Button("Export JSON", EditorStyles.toolbarButton, GUILayout.Width(82f)))
                {
                    ExportDefinitionJson();
                }
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Batch Check", EditorStyles.toolbarButton, GUILayout.Width(86f)))
            {
                CombatAnimationBatchValidatorWindow.Open();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawWorkspace()
        {
            DrawMainAuthoringWorkbench();
            EditorGUILayout.Space(8f);
            DrawCharacterActionGraph();
            EditorGUILayout.Space(8f);
            DrawUtilityDrawers();
        }

        private void DrawCharacterActionGraph()
        {
            showCharacterActionGraph = EditorGUILayout.Foldout(showCharacterActionGraph, "Character Action Graph", true);
            if (!showCharacterActionGraph)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (characterActionGraphView == null)
            {
                characterActionGraphView = new CombatComboGraphView(Repaint);
                characterActionGraphView.Initialize();
            }

            characterActionGraphView.Draw(true);
            EditorGUILayout.EndVertical();
        }

        private void DrawMainAuthoringWorkbench()
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.MinWidth(360f));
            using (new EditorGUI.DisabledScope(clip == null))
            {
                DrawPreviewControls();
            }

            EditorGUILayout.Space(8f);
            DrawMarkerOverviewList();
            EditorGUILayout.Space(8f);
            DrawActionSummary();
            EditorGUILayout.EndVertical();

            float inspectorWidth = Mathf.Clamp(position.width * 0.32f, 300f, 380f);
            EditorGUILayout.BeginVertical(GUILayout.Width(inspectorWidth));
            DrawInspectorPanel();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawUtilityDrawers()
        {
            showAssetDrawer = EditorGUILayout.Foldout(showAssetDrawer, "Asset Library and Binding", true);
            if (showAssetDrawer)
            {
                DrawObjectBindingControls();
                EditorGUILayout.Space(8f);
            }

            showRootMotionDrawer = EditorGUILayout.Foldout(showRootMotionDrawer, "Root Motion Analysis", true);
            if (showRootMotionDrawer)
            {
                DrawRootMotionPanel();
                EditorGUILayout.Space(8f);
            }

            showActionLinksDrawer = EditorGUILayout.Foldout(showActionLinksDrawer, "Action Links", true);
            if (showActionLinksDrawer)
            {
                DrawActionLinks();
                EditorGUILayout.Space(8f);
            }

            DrawValidationPanel();
            EditorGUILayout.Space(8f);
            DrawExportControls();
        }

        private void DrawExportControls()
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(definition == null))
            {
                if (GUILayout.Button("Export Current Definition JSON", GUILayout.Height(28f)))
                {
                    ExportDefinitionJson();
                }
            }

            if (GUILayout.Button("Open Batch Validator", GUILayout.Height(28f)))
            {
                CombatAnimationBatchValidatorWindow.Open();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMarkerOverviewList()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Markers", EditorStyles.boldLabel);

            if (definition == null)
            {
                EditorGUILayout.HelpBox("Create or assign a definition before adding gameplay markers.", MessageType.None);
                EditorGUILayout.EndVertical();
                return;
            }

            definition.EnsureMarkers();
            if (definition.markers.Count == 0)
            {
                EditorGUILayout.HelpBox("No markers yet. Add Hitbox, Combo, Net Sync, or a template at the current frame.", MessageType.None);
                EditorGUILayout.EndVertical();
                return;
            }

            int frameRate = GetAuthoringFrameRate();
            float markerListHeight = Mathf.Clamp(position.height - 500f, 120f, 220f);
            markerScroll = EditorGUILayout.BeginScrollView(markerScroll, GUILayout.Height(markerListHeight));
            try
            {
                for (int i = 0; i < definition.markers.Count; i++)
                {
                    CombatAnimationMarker marker = definition.markers[i];
                    if (marker == null)
                    {
                        continue;
                    }

                    bool selected = selectedMarkerIndex == i;
                    EditorGUILayout.BeginHorizontal(selected ? EditorStyles.helpBox : GUIStyle.none, GUILayout.Height(24f));

                    Rect swatchRect = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(14f), GUILayout.Height(20f));
                    if (Event.current.type == EventType.Repaint)
                    {
                        Rect colorRect = new Rect(swatchRect.x + 1f, swatchRect.y + 4f, 10f, 10f);
                        EditorGUI.DrawRect(colorRect, marker.color);
                    }

                    string frameText = marker.StartFrame(clip, frameRate) + "f";
                    if (marker.duration > 0f)
                    {
                        frameText += " +" + marker.DurationFrames(frameRate) + "f";
                    }

                    if (GUILayout.Toggle(selected, marker.kind + "  " + frameText, "Button", GUILayout.Width(170f)))
                    {
                        if (!selected)
                        {
                            selectedMarkerIndex = i;
                            SetNormalizedTime(marker.normalizedTime);
                        }
                    }

                    EditorGUILayout.LabelField(new GUIContent(ShortenMiddle(marker.gameplayTag, 26), marker.gameplayTag), EditorStyles.miniLabel);

                    if (GUILayout.Button("Jump", GUILayout.Width(48f)))
                    {
                        selectedMarkerIndex = i;
                        SetNormalizedTime(marker.normalizedTime);
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawInspectorPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);

            if (definition == null)
            {
                EditorGUILayout.HelpBox("No definition is selected. Create one from the current clip or assign an existing CombatAnimationDefinition.", MessageType.Warning);
                using (new EditorGUI.DisabledScope(clip == null))
                {
                    if (GUILayout.Button("Create Definition From Clip", GUILayout.Height(28f)))
                    {
                        CreateDefinitionAsset();
                    }
                }

                EditorGUILayout.EndVertical();
                return;
            }

            definition.EnsureMarkers();
            if (selectedMarkerIndex >= 0 && selectedMarkerIndex < definition.markers.Count)
            {
                DrawSelectedMarkerInspector(definition.markers[selectedMarkerIndex]);
            }
            else
            {
                DrawDefinitionInspector();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawDefinitionInspector()
        {
            EditorGUILayout.LabelField("Action Definition", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            definition.actionId = EditorGUILayout.TextField("Action Id", definition.actionId);
            definition.stateName = EditorGUILayout.TextField("State Name", definition.stateName);
            definition.authoringFrameRate = Mathf.Max(1, EditorGUILayout.IntField("Authoring FPS", definition.authoringFrameRate));
            definition.requiresNetworkSync = EditorGUILayout.Toggle("Require Net Sync", definition.requiresNetworkSync);
            definition.loopPreview = EditorGUILayout.Toggle("Loop Preview", definition.loopPreview);
            definition.rootMotionScale = EditorGUILayout.FloatField("Root Motion Scale", definition.rootMotionScale);
            definition.clip = clip;
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(definition);
            }

            EditorGUILayout.Space(6f);
            markerTemplate = (CombatAnimationEventKind)EditorGUILayout.EnumPopup("Marker Template", markerTemplate);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Template", GUILayout.Height(26f)))
            {
                AddMarker(markerTemplate);
            }

            if (GUILayout.Button("Sort", GUILayout.Height(26f), GUILayout.Width(56f)))
            {
                definition.SortMarkers();
                EditorUtility.SetDirty(definition);
            }
            EditorGUILayout.EndHorizontal();

            actionRecipe = (ActionRecipe)EditorGUILayout.EnumPopup("Action Recipe", actionRecipe);
            if (GUILayout.Button("Append Recipe Markers", GUILayout.Height(26f)))
            {
                AppendRecipeMarkers(actionRecipe);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox("Select a marker on the timeline or in the marker list to edit its timing, tag, size, and scene gizmo data here.", MessageType.None);
        }

        private void DrawSelectedMarkerInspector(CombatAnimationMarker marker)
        {
            if (marker == null)
            {
                EditorGUILayout.HelpBox("Selected marker is missing.", MessageType.Error);
                return;
            }

            int frameRate = GetAuthoringFrameRate();
            int frameCount = GetClipFrameCount(frameRate);
            int startFrame = marker.StartFrame(clip, frameRate);
            int durationFrames = marker.DurationFrames(frameRate);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Selected Marker", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", GUILayout.Width(54f)))
            {
                selectedMarkerIndex = -1;
                GUI.FocusControl(null);
                return;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            marker.kind = (CombatAnimationEventKind)EditorGUILayout.EnumPopup("Kind", marker.kind);
            int nextStartFrame = EditorGUILayout.IntSlider("Start Frame", startFrame, 0, Mathf.Max(0, frameCount));
            int nextDurationFrames = Mathf.Max(0, EditorGUILayout.IntField("Duration Frames", durationFrames));
            marker.gameplayTag = EditorGUILayout.TextField("Tag", marker.gameplayTag);
            marker.payload = EditorGUILayout.TextField("Payload", marker.payload);
            marker.localOffset = EditorGUILayout.Vector3Field("Local Offset", marker.localOffset);
            marker.size = EditorGUILayout.Vector3Field("Size", marker.size);
            marker.color = EditorGUILayout.ColorField("Color", marker.color);
            marker.serverAuthoritative = EditorGUILayout.Toggle("Server Auth", marker.serverAuthoritative);
            if (EditorGUI.EndChangeCheck())
            {
                marker.normalizedTime = FrameToNormalizedTime(nextStartFrame, frameCount);
                marker.duration = FramesToSeconds(nextDurationFrames, frameRate);
                EditorUtility.SetDirty(definition);
                SampleClip();
            }

            EditorGUILayout.LabelField("Clip Time", marker.TimeSeconds(clip).ToString("0.000") + "s");
            EditorGUILayout.LabelField("Duration", marker.duration.ToString("0.000") + "s");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Jump To Marker", GUILayout.Height(26f)))
            {
                SetNormalizedTime(marker.normalizedTime);
            }

            if (GUILayout.Button("Duplicate", GUILayout.Height(26f)))
            {
                definition.markers.Insert(selectedMarkerIndex + 1, CloneMarker(marker));
                selectedMarkerIndex++;
                EditorUtility.SetDirty(definition);
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Remove Marker", GUILayout.Height(26f)))
            {
                definition.markers.RemoveAt(selectedMarkerIndex);
                selectedMarkerIndex = -1;
                EditorUtility.SetDirty(definition);
            }
        }

        private void DrawObjectBindingControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Assets and Binding", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Preview Scene", SceneManager.GetActiveScene().name == PreviewSceneName ? PreviewSceneName : "Not active");
            definition = (CombatAnimationDefinition)EditorGUILayout.ObjectField("Definition", definition, typeof(CombatAnimationDefinition), false);

            if (definition != null && clip == null)
            {
                clip = definition.clip;
            }

            clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", clip, typeof(AnimationClip), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (definition != null)
                {
                    definition.clip = clip;
                    EditorUtility.SetDirty(definition);
                }

                SampleClip();
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(280f));
            DrawModelLibraryControls();
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(280f));
            DrawAnimationLibraryControls();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private string GetCharacterStatus()
        {
            if (characterProfile == null)
            {
                return "None";
            }

            characterProfile.EnsureDefaults();
            return string.IsNullOrWhiteSpace(characterProfile.displayName) ? characterProfile.name : characterProfile.displayName;
        }

        private string GetModelStatus()
        {
            if (selectedModelIndex >= 0 && selectedModelIndex < modelCandidates.Count)
            {
                return modelCandidates[selectedModelIndex].label;
            }

            return "None";
        }

        private string GetClipStatus()
        {
            if (clip != null)
            {
                return BuildAnimationDisplayName(clip.name);
            }

            if (selectedAnimationIndex >= 0 && selectedAnimationIndex < animationCandidates.Count)
            {
                return animationCandidates[selectedAnimationIndex].label;
            }

            return "None";
        }

        private string GetDefinitionStatus()
        {
            if (definition == null)
            {
                return "None";
            }

            return string.IsNullOrWhiteSpace(definition.actionId) ? definition.name : definition.actionId;
        }

        private string GetFrameStatus()
        {
            if (clip == null)
            {
                return "-";
            }

            int frameRate = GetAuthoringFrameRate();
            int frameCount = GetClipFrameCount(frameRate);
            int currentFrame = NormalizedTimeToFrame(normalizedTime, frameCount);
            return currentFrame + " / " + frameCount + "f";
        }

        private string GetSelectedMarkerStatus()
        {
            if (definition == null || definition.markers == null || selectedMarkerIndex < 0 || selectedMarkerIndex >= definition.markers.Count)
            {
                return "None";
            }

            CombatAnimationMarker marker = definition.markers[selectedMarkerIndex];
            return marker.kind + " @ " + marker.StartFrame(clip, GetAuthoringFrameRate()) + "f";
        }

        private void DrawModelLibraryControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Preview Model Library", EditorStyles.boldLabel);

            DefaultAsset currentFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(modelFolder);
            EditorGUI.BeginChangeCheck();
            DefaultAsset nextFolderAsset = (DefaultAsset)EditorGUILayout.ObjectField("Model Folder", currentFolderAsset, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck() && nextFolderAsset != null)
            {
                string nextPath = AssetDatabase.GetAssetPath(nextFolderAsset);
                if (AssetDatabase.IsValidFolder(nextPath))
                {
                    modelFolder = nextPath;
                    EditorPrefs.SetString(ModelFolderPrefsKey, modelFolder);
                    RefreshModelLibrary(false);
                    RefreshAnimationLibrary(false);
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Models"))
            {
                RefreshModelLibrary(true);
            }

            if (GUILayout.Button("Open Folder"))
            {
                ActToolkitEditorUtilities.EnsureExternalAssetFolders();
                EditorUtility.RevealInFinder(modelFolder);
            }
            EditorGUILayout.EndHorizontal();

            if (modelCandidates.Count == 0)
            {
                EditorGUILayout.HelpBox("No model assets found. Put FBX, GLB, glTF, or prefabs under the model folder. Files inside an Animations subfolder are ignored.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            selectedModelIndex = Mathf.Clamp(selectedModelIndex, 0, modelCandidates.Count - 1);
            EditorGUI.BeginChangeCheck();
            int nextModelIndex = EditorGUILayout.Popup("Preview Model", selectedModelIndex, modelLabels);
            if (EditorGUI.EndChangeCheck())
            {
                selectedModelIndex = nextModelIndex;
                RefreshAnimationLibrary(false);
                SchedulePreviewSceneRefresh(true);
            }
            else
            {
                selectedModelIndex = nextModelIndex;
                EnsureSelectedPreviewModelInstance();
            }

            EditorGUILayout.HelpBox("Selecting a model opens a dedicated preview Scene and keeps one temporary preview model there, so test scenes stay clean.", MessageType.None);

            EditorGUILayout.EndVertical();
        }

        private void DrawAnimationLibraryControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Animation Library", EditorStyles.boldLabel);

            DefaultAsset currentFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(animationFolder);
            EditorGUI.BeginChangeCheck();
            DefaultAsset nextFolderAsset = (DefaultAsset)EditorGUILayout.ObjectField("Animation Folder", currentFolderAsset, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck() && nextFolderAsset != null)
            {
                string nextPath = AssetDatabase.GetAssetPath(nextFolderAsset);
                if (AssetDatabase.IsValidFolder(nextPath))
                {
                    animationFolder = nextPath;
                    EditorPrefs.SetString(AnimationFolderPrefsKey, animationFolder);
                    RefreshAnimationLibrary(false);
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Animations"))
            {
                RefreshAnimationLibrary(true);
            }

            if (GUILayout.Button("Open Folder"))
            {
                ActToolkitEditorUtilities.EnsureExternalAssetFolders();
                EditorUtility.RevealInFinder(animationFolder);
            }
            EditorGUILayout.EndHorizontal();

            if (animationCandidates.Count == 0)
            {
                string message = hiddenIncompatibleAnimationCount > 0
                    ? "No compatible clips for the selected preview model. Hidden incompatible clips: " + hiddenIncompatibleAnimationCount + "."
                    : "No animation clips found. Put FBX or .anim files under the animation folder, then refresh Unity assets.";
                EditorGUILayout.HelpBox(message, MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            if (hiddenIncompatibleAnimationCount > 0)
            {
                EditorGUILayout.HelpBox("Hidden incompatible clips for the selected preview model: " + hiddenIncompatibleAnimationCount + ".", MessageType.None);
            }

            selectedAnimationIndex = Mathf.Clamp(selectedAnimationIndex, 0, animationCandidates.Count - 1);
            EditorGUI.BeginChangeCheck();
            int nextAnimationIndex = EditorGUILayout.Popup("Animation Clip", selectedAnimationIndex, animationLabels);
            if (EditorGUI.EndChangeCheck())
            {
                selectedAnimationIndex = nextAnimationIndex;
                UseSelectedAnimationClip();
            }
            else
            {
                selectedAnimationIndex = nextAnimationIndex;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPreviewControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            normalizedTime = EditorGUILayout.Slider("Time", normalizedTime, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                SampleClip();
            }

            float length = clip == null ? 0f : clip.length;
            EditorGUILayout.LabelField("Seconds", (normalizedTime * length).ToString("0.000") + " / " + length.ToString("0.000"));
            DrawFrameControls();
            DrawTimeline();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(isPlaying ? "Pause" : "Play", GUILayout.Height(26f)))
            {
                isPlaying = !isPlaying;
                lastUpdateTime = EditorApplication.timeSinceStartup;
                SampleClip();
            }

            if (GUILayout.Button("Stop", GUILayout.Height(26f)))
            {
                normalizedTime = 0f;
                isPlaying = false;
                SampleClip();
            }

            if (GUILayout.Button("Sample", GUILayout.Height(26f)))
            {
                SampleClip();
            }

            EditorGUILayout.EndHorizontal();

            drawAllMarkers = EditorGUILayout.Toggle("Draw All Markers", drawAllMarkers);
            snapToFrames = EditorGUILayout.Toggle("Snap To Frames", snapToFrames);

            EditorGUILayout.EndVertical();
        }

        private void DrawFrameControls()
        {
            int frameRate = GetAuthoringFrameRate();
            int frameCount = GetClipFrameCount(frameRate);
            int currentFrame = NormalizedTimeToFrame(normalizedTime, frameCount);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(currentFrame <= 0))
            {
                if (GUILayout.Button("<", GUILayout.Width(28f)))
                {
                    SetCurrentFrame(currentFrame - 1, frameCount);
                }
            }

            EditorGUI.BeginChangeCheck();
            int nextFrame = EditorGUILayout.IntSlider("Frame", currentFrame, 0, frameCount);
            if (EditorGUI.EndChangeCheck())
            {
                SetCurrentFrame(nextFrame, frameCount);
            }

            using (new EditorGUI.DisabledScope(currentFrame >= frameCount))
            {
                if (GUILayout.Button(">", GUILayout.Width(28f)))
                {
                    SetCurrentFrame(currentFrame + 1, frameCount);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTimeline()
        {
            if (clip == null)
            {
                return;
            }

            int frameRate = GetAuthoringFrameRate();
            int frameCount = GetClipFrameCount(frameRate);
            string[] laneLabels = { "Combat", "Hurtbox", "Defense", "Movement", "Combo", "Present" };
            const float headerHeight = 22f;
            const float laneHeight = 20f;
            const float laneGap = 3f;
            const float labelWidth = 86f;
            float timelineHeight = headerHeight + laneLabels.Length * (laneHeight + laneGap) + 10f;
            Rect rect = GUILayoutUtility.GetRect(1f, timelineHeight, GUILayout.ExpandWidth(true));
            Rect trackArea = new Rect(rect.x + labelWidth, rect.y + headerHeight, Mathf.Max(1f, rect.width - labelWidth), laneLabels.Length * (laneHeight + laneGap) - laneGap);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, new Color(0.12f, 0.13f, 0.14f, 1f));

                int minorStep = Mathf.Max(1, Mathf.RoundToInt(frameRate / 10f));
                int majorStep = Mathf.Max(1, Mathf.RoundToInt(frameRate / 2f));

                for (int lane = 0; lane < laneLabels.Length; lane++)
                {
                    Rect laneRect = GetTimelineLaneRect(trackArea, lane, laneHeight, laneGap);
                    EditorGUI.DrawRect(laneRect, lane % 2 == 0 ? new Color(0.17f, 0.18f, 0.19f, 1f) : new Color(0.14f, 0.15f, 0.16f, 1f));
                    GUI.Label(new Rect(rect.x + 6f, laneRect.y + 1f, labelWidth - 8f, laneHeight), laneLabels[lane], EditorStyles.miniLabel);
                }

                for (int frame = 0; frame <= frameCount; frame += minorStep)
                {
                    float x = FrameToX(trackArea, frame, frameCount);
                    bool major = frame % majorStep == 0;
                    Rect tick = new Rect(x, trackArea.y, 1f, major ? trackArea.height : trackArea.height * 0.45f);
                    EditorGUI.DrawRect(tick, major ? new Color(0.55f, 0.58f, 0.62f, 0.7f) : new Color(0.45f, 0.48f, 0.52f, 0.35f));
                }
            }

            Event current = Event.current;
            if (definition != null && definition.markers != null)
            {
                definition.EnsureMarkers();
                for (int i = 0; i < definition.markers.Count; i++)
                {
                    CombatAnimationMarker marker = definition.markers[i];
                    if (marker == null)
                    {
                        continue;
                    }

                    int lane = TimelineLaneFor(marker.kind);
                    Rect laneRect = GetTimelineLaneRect(trackArea, lane, laneHeight, laneGap);
                    int startFrame = marker.StartFrame(clip, frameRate);
                    int durationFrames = Mathf.Max(1, marker.DurationFrames(frameRate));
                    float x = FrameToX(trackArea, startFrame, frameCount);
                    float width = Mathf.Max(marker.duration <= 0f ? 5f : 3f, trackArea.width * durationFrames / Mathf.Max(1, frameCount));
                    Rect markerRect = new Rect(x, laneRect.y + 4f, width, laneHeight - 8f);

                    if (current.type == EventType.Repaint)
                    {
                        Color color = marker.color;
                        color.a = i == selectedMarkerIndex ? 1f : 0.82f;
                        EditorGUI.DrawRect(markerRect, color);
                        if (i == selectedMarkerIndex)
                        {
                            DrawRectOutline(markerRect, new Color(1f, 1f, 1f, 0.95f));
                        }
                    }

                    if (current.type == EventType.MouseDown && markerRect.Contains(current.mousePosition))
                    {
                        selectedMarkerIndex = i;
                        SetNormalizedTime(marker.normalizedTime);
                        current.Use();
                    }
                }
            }

            if (definition != null && definition.actionLinks != null)
            {
                definition.EnsureActionLinks();
                Rect comboLane = GetTimelineLaneRect(trackArea, TimelineLaneForLinks(), laneHeight, laneGap);
                foreach (CombatActionLink link in definition.actionLinks)
                {
                    int startFrame = Mathf.Clamp(link.startFrame, 0, frameCount);
                    int endFrame = Mathf.Clamp(Mathf.Max(link.endFrame, link.startFrame + 1), 0, frameCount);
                    float x = FrameToX(trackArea, startFrame, frameCount);
                    float width = Mathf.Max(5f, trackArea.width * Mathf.Max(1, endFrame - startFrame) / Mathf.Max(1, frameCount));
                    Rect linkRect = new Rect(x, comboLane.y + 1f, width, 3f);

                    if (current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(linkRect, new Color(0.35f, 1f, 0.55f, 0.9f));
                    }
                }
            }

            if (Event.current.type == EventType.Repaint)
            {
                int currentFrame = NormalizedTimeToFrame(normalizedTime, frameCount);
                float currentX = FrameToX(trackArea, currentFrame, frameCount);
                EditorGUI.DrawRect(new Rect(currentX - 1f, rect.y, 2f, rect.height), new Color(1f, 0.9f, 0.2f, 1f));
            }

            GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, 220f, 18f), "Timeline: " + frameRate + " fps / " + frameCount + " frames");

            if ((current.type == EventType.MouseDown || current.type == EventType.MouseDrag) && trackArea.Contains(current.mousePosition))
            {
                float ratio = Mathf.InverseLerp(trackArea.x, trackArea.xMax, current.mousePosition.x);
                SetNormalizedTime(ratio);
                current.Use();
            }
        }

        private void DrawDefinitionControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Definition Asset", EditorStyles.boldLabel);

            if (definition == null)
            {
                if (GUILayout.Button("Create Definition From Clip", GUILayout.Height(28f)))
                {
                    CreateDefinitionAsset();
                }

                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUI.BeginChangeCheck();
            definition.actionId = EditorGUILayout.TextField("Action Id", definition.actionId);
            definition.stateName = EditorGUILayout.TextField("State Name", definition.stateName);
            definition.authoringFrameRate = Mathf.Max(1, EditorGUILayout.IntField("Authoring FPS", definition.authoringFrameRate));
            definition.requiresNetworkSync = EditorGUILayout.Toggle("Require Net Sync", definition.requiresNetworkSync);
            definition.loopPreview = EditorGUILayout.Toggle("Loop Preview", definition.loopPreview);
            definition.rootMotionScale = EditorGUILayout.FloatField("Root Motion Scale", definition.rootMotionScale);
            definition.clip = clip;
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(definition);
            }

            markerTemplate = (CombatAnimationEventKind)EditorGUILayout.EnumPopup("Marker Template", markerTemplate);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Template At Time"))
            {
                AddMarker(markerTemplate);
            }

            if (GUILayout.Button("Add Net Sync"))
            {
                AddMarker(CombatAnimationEventKind.NetworkSync);
            }

            if (GUILayout.Button("Sort Markers"))
            {
                definition.SortMarkers();
                EditorUtility.SetDirty(definition);
            }

            if (GUILayout.Button("Export JSON"))
            {
                ExportDefinitionJson();
            }
            EditorGUILayout.EndHorizontal();

            actionRecipe = (ActionRecipe)EditorGUILayout.EnumPopup("Action Recipe", actionRecipe);
            if (GUILayout.Button("Append Recipe Markers", GUILayout.Height(26f)))
            {
                AppendRecipeMarkers(actionRecipe);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawActionSummary()
        {
            if (definition == null || clip == null)
            {
                return;
            }

            definition.EnsureMarkers();
            int frameRate = GetAuthoringFrameRate();
            int frameCount = GetClipFrameCount(frameRate);
            int firstHitbox = -1;
            int lastHitbox = -1;
            int firstCombo = -1;
            int lastCombo = -1;
            int invulnerabilityFrames = 0;
            int armorFrames = 0;

            foreach (CombatAnimationMarker marker in definition.markers)
            {
                int start = marker.StartFrame(clip, frameRate);
                int end = marker.EndFrame(clip, frameRate);
                if (marker.kind == CombatAnimationEventKind.Hitbox)
                {
                    firstHitbox = firstHitbox < 0 ? start : Mathf.Min(firstHitbox, start);
                    lastHitbox = Mathf.Max(lastHitbox, end);
                }
                else if (marker.kind == CombatAnimationEventKind.ComboBranch)
                {
                    firstCombo = firstCombo < 0 ? start : Mathf.Min(firstCombo, start);
                    lastCombo = Mathf.Max(lastCombo, end);
                }
                else if (marker.kind == CombatAnimationEventKind.Invulnerability)
                {
                    invulnerabilityFrames += marker.DurationFrames(frameRate);
                }
                else if (marker.kind == CombatAnimationEventKind.SuperArmor)
                {
                    armorFrames += marker.DurationFrames(frameRate);
                }
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Action Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Total Frames", frameCount + "f @ " + frameRate + " fps");
            EditorGUILayout.LabelField("Hitbox Window", firstHitbox < 0 ? "None" : firstHitbox + " - " + lastHitbox + " (" + Mathf.Max(0, lastHitbox - firstHitbox) + "f)");
            EditorGUILayout.LabelField("Combo Window", firstCombo < 0 ? "None" : firstCombo + " - " + lastCombo + " (" + Mathf.Max(0, lastCombo - firstCombo) + "f)");
            EditorGUILayout.LabelField("Invulnerability", invulnerabilityFrames + "f");
            EditorGUILayout.LabelField("Super Armor", armorFrames + "f");
            EditorGUILayout.EndVertical();
        }

        private void DrawRootMotionPanel()
        {
            if (clip == null)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Root Motion Analysis", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(LoadSelectedModelAsset() == null))
            {
                if (GUILayout.Button("Analyze Root Motion", GUILayout.Height(26f)))
                {
                    AnalyzeRootMotion();
                }
            }

            if (GUILayout.Button("Clear", GUILayout.Height(26f), GUILayout.Width(72f)))
            {
                rootMotionDistances.Clear();
                rootMotionSpeeds.Clear();
                rootMotionTotalDistance = 0f;
                rootMotionPeakSpeed = 0f;
                rootMotionStatus = "Not analyzed.";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Status", rootMotionStatus);
            EditorGUILayout.LabelField("Total Distance", rootMotionTotalDistance.ToString("0.000") + "m");
            EditorGUILayout.LabelField("Peak Speed", rootMotionPeakSpeed.ToString("0.000") + "m/s");

            DrawRootMotionGraph(rootMotionDistances, "Distance", new Color(0.35f, 0.75f, 1f, 0.95f));
            DrawRootMotionGraph(rootMotionSpeeds, "Speed", new Color(0.35f, 1f, 0.55f, 0.95f));
            EditorGUILayout.EndVertical();
        }

        private void DrawRootMotionGraph(List<float> values, string label, Color color)
        {
            if (values.Count < 2)
            {
                return;
            }

            Rect rect = GUILayoutUtility.GetRect(1f, 48f, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            EditorGUI.DrawRect(rect, new Color(0.12f, 0.13f, 0.14f, 1f));
            GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, 120f, 18f), label, EditorStyles.miniLabel);

            float max = 0.0001f;
            for (int i = 0; i < values.Count; i++)
            {
                max = Mathf.Max(max, Mathf.Abs(values[i]));
            }

            Vector3 previous = GraphPoint(rect, values[0], 0, values.Count, max);
            Handles.BeginGUI();
            Handles.color = color;
            for (int i = 1; i < values.Count; i++)
            {
                Vector3 next = GraphPoint(rect, values[i], i, values.Count, max);
                Handles.DrawLine(previous, next);
                previous = next;
            }
            Handles.color = Color.white;
            Handles.EndGUI();
        }

        private static Vector3 GraphPoint(Rect rect, float value, int index, int count, float max)
        {
            float x = Mathf.Lerp(rect.x + 6f, rect.xMax - 6f, count <= 1 ? 0f : (float)index / (count - 1));
            float y = Mathf.Lerp(rect.yMax - 6f, rect.y + 18f, Mathf.Clamp01(value / max));
            return new Vector3(x, y, 0f);
        }

        private void DrawValidationPanel()
        {
            if (definition == null)
            {
                return;
            }

            showValidation = EditorGUILayout.Foldout(showValidation, "Validation", true);
            if (!showValidation)
            {
                return;
            }

            List<CombatAnimationValidationIssue> issues = CombatAnimationValidation.ValidateDefinition(definition);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Ready: clip, frame data, marker data, and network sync checks pass.", MessageType.Info);
                return;
            }

            foreach (CombatAnimationValidationIssue issue in issues)
            {
                EditorGUILayout.HelpBox(issue.message, issue.type);
            }
        }

        private void DrawActionLinks()
        {
            if (definition == null)
            {
                return;
            }

            definition.EnsureActionLinks();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Action Links", EditorStyles.boldLabel);

            int frameRate = GetAuthoringFrameRate();
            int frameCount = GetClipFrameCount(frameRate);
            int removeIndex = -1;

            for (int i = 0; i < definition.actionLinks.Count; i++)
            {
                CombatActionLink link = definition.actionLinks[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Link " + (i + 1), EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Use Selected Marker", GUILayout.Width(140f)))
                {
                    ApplySelectedMarkerToActionLink(link);
                }

                if (GUILayout.Button("Remove", GUILayout.Width(74f)))
                {
                    removeIndex = i;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                link.inputAction = DrawInputActionPopup("Input Action", link.inputAction);
                link.triggerTag = EditorGUILayout.TextField("Trigger Tag", link.triggerTag);
                link.targetDefinition = (CombatAnimationDefinition)EditorGUILayout.ObjectField("Target Definition", link.targetDefinition, typeof(CombatAnimationDefinition), false);
                if (link.targetDefinition != null)
                {
                    link.targetActionId = link.targetDefinition.actionId;
                }

                link.targetActionId = EditorGUILayout.TextField("Target Action Id", link.targetActionId);
                link.startFrame = Mathf.Clamp(EditorGUILayout.IntField("Start Frame", link.startFrame), 0, Mathf.Max(0, frameCount));
                link.endFrame = Mathf.Clamp(EditorGUILayout.IntField("End Frame", link.endFrame), 0, Mathf.Max(0, frameCount));
                link.serverAuthoritative = EditorGUILayout.Toggle("Server Auth", link.serverAuthoritative);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(definition);
                }

                EditorGUILayout.LabelField("Normalized Window", link.StartNormalizedTime(clip, frameRate).ToString("0.000") + " - " + link.EndNormalizedTime(clip, frameRate).ToString("0.000"));
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Action Link", GUILayout.Height(26f)))
            {
                definition.actionLinks.Add(CreateActionLinkFromSelectedMarker());
                EditorUtility.SetDirty(definition);
            }

            if (GUILayout.Button("Sort Links", GUILayout.Height(26f)))
            {
                definition.actionLinks.Sort((left, right) => left.startFrame.CompareTo(right.startFrame));
                EditorUtility.SetDirty(definition);
            }
            EditorGUILayout.EndHorizontal();

            if (removeIndex >= 0)
            {
                definition.actionLinks.RemoveAt(removeIndex);
                EditorUtility.SetDirty(definition);
            }

            EditorGUILayout.EndVertical();
        }

        private static string DrawInputActionPopup(string label, string inputAction)
        {
            string normalized = CombatInputActionNames.Normalize(inputAction);
            int selectedIndex = Array.IndexOf(CombatInputActionNames.AuthoringNames, normalized);
            string[] labels = BuildInputActionPopupLabels(inputAction, selectedIndex);
            int nextIndex = EditorGUILayout.Popup(label, selectedIndex >= 0 ? selectedIndex : labels.Length - 1, labels);
            if (nextIndex >= 0 && nextIndex < CombatInputActionNames.AuthoringNames.Length)
            {
                return CombatInputActionNames.AuthoringNames[nextIndex];
            }

            return EditorGUILayout.TextField("Custom Input", string.IsNullOrWhiteSpace(inputAction) ? normalized : inputAction);
        }

        private static string[] BuildInputActionPopupLabels(string inputAction, int selectedIndex)
        {
            if (selectedIndex >= 0)
            {
                return CombatInputActionNames.AuthoringLabels;
            }

            string custom = string.IsNullOrWhiteSpace(inputAction) ? "Custom" : "Custom: " + inputAction;
            string[] labels = new string[CombatInputActionNames.AuthoringLabels.Length + 1];
            Array.Copy(CombatInputActionNames.AuthoringLabels, labels, CombatInputActionNames.AuthoringLabels.Length);
            labels[labels.Length - 1] = custom;
            return labels;
        }

        private void DrawMarkerList()
        {
            EditorGUILayout.LabelField("Gameplay Markers", EditorStyles.boldLabel);

            if (definition == null)
            {
                EditorGUILayout.HelpBox("Create or assign a Combat Animation Definition before editing gameplay markers.", MessageType.None);
                return;
            }

            definition.EnsureMarkers();
            int removeIndex = -1;
            int duplicateIndex = -1;

            float markerListHeight = Mathf.Clamp(position.height - 360f, 220f, 420f);
            markerScroll = EditorGUILayout.BeginScrollView(markerScroll, GUILayout.Height(markerListHeight));
            try
            {
                for (int i = 0; i < definition.markers.Count; i++)
                {
                    CombatAnimationMarker marker = definition.markers[i];
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    EditorGUILayout.BeginHorizontal();
                    marker.kind = (CombatAnimationEventKind)EditorGUILayout.EnumPopup(marker.kind);
                    GUILayout.FlexibleSpace();
                    bool selected = selectedMarkerIndex == i;
                    if (GUILayout.Button(selected ? "Selected" : "Select", GUILayout.Width(70f)))
                    {
                        selectedMarkerIndex = i;
                        SetNormalizedTime(marker.normalizedTime);
                    }

                    if (GUILayout.Button("Jump", GUILayout.Width(54f)))
                    {
                        SetNormalizedTime(marker.normalizedTime);
                    }

                    if (GUILayout.Button("Dup", GUILayout.Width(46f)))
                    {
                        duplicateIndex = i;
                    }

                    if (GUILayout.Button("Remove", GUILayout.Width(74f)))
                    {
                        removeIndex = i;
                    }
                    EditorGUILayout.EndHorizontal();

                    marker.normalizedTime = EditorGUILayout.Slider("Time", marker.normalizedTime, 0f, 1f);
                    marker.duration = Mathf.Max(0f, EditorGUILayout.FloatField("Duration", marker.duration));
                    marker.gameplayTag = EditorGUILayout.TextField("Tag", marker.gameplayTag);
                    marker.payload = EditorGUILayout.TextField("Payload", marker.payload);
                    marker.localOffset = EditorGUILayout.Vector3Field("Local Offset", marker.localOffset);
                    marker.size = EditorGUILayout.Vector3Field("Size", marker.size);
                    marker.color = EditorGUILayout.ColorField("Color", marker.color);
                    marker.serverAuthoritative = EditorGUILayout.Toggle("Server Auth", marker.serverAuthoritative);

                    EditorGUILayout.LabelField("Clip Time", marker.TimeSeconds(clip).ToString("0.000") + "s");
                    int frameRate = GetAuthoringFrameRate();
                    EditorGUILayout.LabelField("Frames", marker.StartFrame(clip, frameRate) + " - " + marker.EndFrame(clip, frameRate) + " (" + marker.DurationFrames(frameRate) + "f)");
                    EditorGUILayout.EndVertical();
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }

            if (removeIndex >= 0)
            {
                definition.markers.RemoveAt(removeIndex);
                if (selectedMarkerIndex == removeIndex)
                {
                    selectedMarkerIndex = -1;
                }
                else if (selectedMarkerIndex > removeIndex)
                {
                    selectedMarkerIndex--;
                }

                EditorUtility.SetDirty(definition);
            }
            else if (duplicateIndex >= 0)
            {
                CombatAnimationMarker source = definition.markers[duplicateIndex];
                definition.markers.Insert(duplicateIndex + 1, CloneMarker(source));
                selectedMarkerIndex = duplicateIndex + 1;
                EditorUtility.SetDirty(definition);
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(definition);
            }
        }

        private void CreateDefinitionAsset()
        {
            ActToolkitEditorUtilities.EnsureGeneratedFolders();

            string displayName = clip == null ? "New Action" : BuildAnimationDisplayName(clip.name);
            string defaultName = clip == null ? "CombatAnimationDefinition" : "CA_" + SanitizeAssetName(displayName);
            string path = AssetDatabase.GenerateUniqueAssetPath(ActToolkitEditorUtilities.DefaultCombatDefinitionFolder + "/" + defaultName + ".asset");

            definition = CreateInstance<CombatAnimationDefinition>();
            definition.clip = clip;
            definition.actionId = clip == null ? "action.new" : BuildActionIdFromClipName(clip.name);
            definition.stateName = clip == null ? "New Action" : displayName;
            definition.authoringFrameRate = 60;
            AssetDatabase.CreateAsset(definition, path);
            AddDefinitionToCurrentCharacter(definition);
            AssetDatabase.SaveAssets();
            Selection.activeObject = definition;
            EditorGUIUtility.PingObject(definition);
        }

        private void AddDefinitionToCurrentCharacter(CombatAnimationDefinition action)
        {
            if (characterProfile == null || action == null)
            {
                return;
            }

            if (characterProfile.comboTable == null)
            {
                characterProfile.comboTable = LoadOrCreateDefaultComboTable();
                EditorUtility.SetDirty(characterProfile);
            }

            if (characterProfile.comboTable.actions == null)
            {
                characterProfile.comboTable.actions = new List<CombatAnimationDefinition>();
            }

            if (!characterProfile.comboTable.actions.Contains(action))
            {
                characterProfile.comboTable.actions.Add(action);
                characterProfile.comboTable.RebuildLookup();
                EditorUtility.SetDirty(characterProfile.comboTable);
            }

            characterActionGraphView?.SetDatabase(characterProfile.comboTable, true);
            characterActionGraphView?.Refresh();
        }

        private void RefreshModelLibrary(bool forceAssetRefresh)
        {
            ActToolkitEditorUtilities.EnsureExternalAssetFolders();

            if (forceAssetRefresh)
            {
                AssetDatabase.Refresh();
            }

            modelCandidates.Clear();

            if (!AssetDatabase.IsValidFolder(modelFolder))
            {
                modelFolder = ActToolkitEditorUtilities.DefaultModelFolder;
                EditorPrefs.SetString(ModelFolderPrefsKey, modelFolder);
            }

            string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { modelFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsPreviewModelPath(path))
                {
                    continue;
                }

                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                {
                    continue;
                }

                modelCandidates.Add(new ModelCandidate(path, BuildModelLabel(path)));
            }

            modelCandidates.Sort((left, right) => string.Compare(left.label, right.label, StringComparison.OrdinalIgnoreCase));
            modelLabels = new string[modelCandidates.Count];
            for (int i = 0; i < modelCandidates.Count; i++)
            {
                modelLabels[i] = modelCandidates[i].label;
            }

            selectedModelIndex = modelCandidates.Count == 0 ? -1 : Mathf.Clamp(selectedModelIndex, 0, modelCandidates.Count - 1);
        }

        private static bool IsPreviewModelPath(string path)
        {
            string normalized = path.Replace('\\', '/');
            string extension = Path.GetExtension(normalized).ToLowerInvariant();
            if (extension != ".fbx" && extension != ".glb" && extension != ".gltf" && extension != ".prefab")
            {
                return false;
            }

            return !normalized.Contains("/Animations/", StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains("/Animation/", StringComparison.OrdinalIgnoreCase);
        }

        private string BuildModelLabel(string path)
        {
            return BuildModelDisplayName(path) + "  [" + BuildAssetSourceCode(path) + "]";
        }

        private void SchedulePreviewSceneRefresh(bool createNewScene)
        {
            scheduledPreviewSceneCreateNew |= createNewScene;
            if (previewSceneRefreshScheduled)
            {
                return;
            }

            previewSceneRefreshScheduled = true;
            EditorApplication.delayCall += RunScheduledPreviewSceneRefresh;
        }

        private void RunScheduledPreviewSceneRefresh()
        {
            EditorApplication.delayCall -= RunScheduledPreviewSceneRefresh;

            bool createNewScene = scheduledPreviewSceneCreateNew;
            previewSceneRefreshScheduled = false;
            scheduledPreviewSceneCreateNew = false;

            if (this == null)
            {
                return;
            }

            bool previewSceneReady = createNewScene ? EnsurePreviewScene(true) : EnsurePreviewScene(false);
            if (!previewSceneReady)
            {
                return;
            }

            SpawnSelectedPreviewModel(false);
            UseFirstCompatibleAnimationIfNeeded();
            Repaint();
        }

        private bool EnsurePreviewScene(bool createNew)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!createNew && activeScene.IsValid() && activeScene.name == PreviewSceneName)
            {
                return true;
            }

            if (!createNew)
            {
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            StopPreview();
            ClearSpawnedPreviewModels();
            Scene previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            previewScene.name = PreviewSceneName;
            CreatePreviewSceneEnvironment();
            return true;
        }

        private static void CreatePreviewSceneEnvironment()
        {
            GameObject lightObject = new GameObject(PreviewLightName);
            lightObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
        }

        private void SpawnSelectedPreviewModel(bool createPreviewScene)
        {
            if (!EnsurePreviewScene(createPreviewScene) || selectedModelIndex < 0 || selectedModelIndex >= modelCandidates.Count)
            {
                return;
            }

            GameObject asset = LoadSelectedModelAsset();
            if (asset == null)
            {
                RefreshModelLibrary(false);
                return;
            }

            StopPreview();
            ClearSpawnedPreviewModels();

            Scene activeScene = SceneManager.GetActiveScene();
            GameObject wrapper = new GameObject(PreviewModelNamePrefix + asset.name);
            EditorSceneManager.MoveGameObjectToScene(wrapper, activeScene);
            wrapper.transform.position = Vector3.zero;
            wrapper.transform.rotation = Quaternion.identity;
            wrapper.transform.localScale = Vector3.one;

            GameObject instance = PrefabUtility.InstantiatePrefab(asset, activeScene) as GameObject;
            if (instance == null)
            {
                instance = Instantiate(asset);
                EditorSceneManager.MoveGameObjectToScene(instance, activeScene);
            }

            instance.name = PreviewSampleRootName;
            instance.transform.SetParent(wrapper.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            ApplyPreviewHideFlags(wrapper);

            Animator animator = instance.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }

            Avatar avatar = LoadAvatarFromModel(GetSelectedModelPath());
            if (animator.avatar == null && avatar != null)
            {
                animator.avatar = avatar;
            }

            animator.enabled = true;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            previewRoot = wrapper;
            previewAnimator = animator;
            CachePreviewSampleTransform();
            Selection.activeGameObject = wrapper;

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                sceneView.FrameSelected();
            }

            SampleClip();
        }

        private void EnsureSelectedPreviewModelInstance()
        {
            if (previewRoot != null
                && previewAnimator != null
                && !EditorUtility.IsPersistent(previewRoot)
                && previewRoot.scene.IsValid()
                && previewRoot.scene.name == PreviewSceneName)
            {
                return;
            }

            GameObject existingRoot = FindSpawnedPreviewRoot();
            if (existingRoot != null)
            {
                previewRoot = existingRoot;
                previewAnimator = existingRoot.GetComponentInChildren<Animator>();
                CachePreviewSampleTransform();
                SampleClip();
                return;
            }

            SpawnSelectedPreviewModel(false);
        }

        private static GameObject FindSpawnedPreviewRoot()
        {
            GameObject[] sceneObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (GameObject sceneObject in sceneObjects)
            {
                if (sceneObject == null
                    || EditorUtility.IsPersistent(sceneObject)
                    || !sceneObject.scene.IsValid()
                    || sceneObject.transform.parent != null
                    || !IsSpawnedPreviewModel(sceneObject.name))
                {
                    continue;
                }

                return sceneObject;
            }

            return null;
        }

        private static void ClearSpawnedPreviewModels()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.name == PreviewSceneName)
            {
                GameObject[] roots = activeScene.GetRootGameObjects();
                foreach (GameObject root in roots)
                {
                    if (root == null || root.name == PreviewLightName)
                    {
                        continue;
                    }

                    DestroyImmediate(root);
                }

                return;
            }

            GameObject[] sceneObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (GameObject sceneObject in sceneObjects)
            {
                if (sceneObject == null
                    || EditorUtility.IsPersistent(sceneObject)
                    || !sceneObject.scene.IsValid()
                    || sceneObject.transform.parent != null
                    || !IsSpawnedPreviewModel(sceneObject.name))
                {
                    continue;
                }

                DestroyImmediate(sceneObject);
            }
        }

        private static void ApplyPreviewHideFlags(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in transforms)
            {
                child.gameObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            }
        }

        private static Avatar LoadAvatarFromModel(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset is Avatar avatar)
                {
                    return avatar;
                }
            }

            return null;
        }

        private static bool IsSpawnedPreviewModel(string objectName)
        {
            return objectName.StartsWith(PreviewModelNamePrefix, StringComparison.Ordinal)
                || objectName.StartsWith(LegacyPreviewModelNamePrefix, StringComparison.Ordinal);
        }

        private void CachePreviewSampleTransform()
        {
            previewSampleTransform = previewAnimator != null ? previewAnimator.transform : null;
            if (previewSampleTransform == null)
            {
                previewSampleInitialLocalPosition = Vector3.zero;
                previewSampleInitialLocalRotation = Quaternion.identity;
                previewSampleInitialLocalScale = Vector3.one;
                return;
            }

            previewSampleInitialLocalPosition = previewSampleTransform.localPosition;
            previewSampleInitialLocalRotation = previewSampleTransform.localRotation;
            previewSampleInitialLocalScale = previewSampleTransform.localScale;
        }

        private void NormalizePreviewTransformsAfterSample()
        {
            if (previewRoot != null)
            {
                previewRoot.transform.position = Vector3.zero;
                previewRoot.transform.rotation = Quaternion.identity;
                previewRoot.transform.localScale = Vector3.one;
            }

            if (previewSampleTransform == null)
            {
                return;
            }

            previewSampleTransform.localPosition = previewSampleInitialLocalPosition;
            previewSampleTransform.localRotation = previewSampleInitialLocalRotation;
            previewSampleTransform.localScale = previewSampleInitialLocalScale;
        }

        private void RefreshAnimationLibrary(bool forceAssetRefresh)
        {
            ActToolkitEditorUtilities.EnsureExternalAssetFolders();

            if (forceAssetRefresh)
            {
                AssetDatabase.Refresh();
            }

            animationCandidates.Clear();
            hiddenIncompatibleAnimationCount = 0;

            if (!AssetDatabase.IsValidFolder(animationFolder))
            {
                animationFolder = ActToolkitEditorUtilities.DefaultPreviewClipFolder;
                EditorPrefs.SetString(AnimationFolderPrefsKey, animationFolder);
            }

            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { animationFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AddAnimationClipsAtPath(path);
            }

            animationCandidates.Sort((left, right) => string.Compare(left.label, right.label, StringComparison.OrdinalIgnoreCase));
            animationLabels = new string[animationCandidates.Count];
            for (int i = 0; i < animationCandidates.Count; i++)
            {
                animationLabels[i] = animationCandidates[i].label;
            }

            int currentClipIndex = FindAnimationCandidateIndex(clip);
            selectedAnimationIndex = animationCandidates.Count == 0
                ? -1
                : currentClipIndex >= 0
                    ? currentClipIndex
                    : Mathf.Clamp(selectedAnimationIndex, 0, animationCandidates.Count - 1);
        }

        private void AddAnimationClipsAtPath(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".fbx" && extension != ".anim")
            {
                return;
            }

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset is not AnimationClip animationClip)
                {
                    continue;
                }

                if (IsEditorPreviewClip(animationClip))
                {
                    continue;
                }

                if (!IsAnimationCompatibleWithSelectedModel(path, animationClip))
                {
                    hiddenIncompatibleAnimationCount++;
                    continue;
                }

                animationCandidates.Add(new AnimationClipCandidate(path, animationClip.name, BuildAnimationLabel(path, animationClip.name)));
            }
        }

        private bool IsAnimationCompatibleWithSelectedModel(string animationPath, AnimationClip animationClip)
        {
            string modelPath = GetSelectedModelPath();
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return true;
            }

            ModelImporterAnimationType modelType = GetModelImporterAnimationType(modelPath);
            ModelImporterAnimationType animationType = GetModelImporterAnimationType(animationPath);
            if (modelType == ModelImporterAnimationType.Human && animationType == ModelImporterAnimationType.Human)
            {
                return true;
            }

            if (animationType == ModelImporterAnimationType.None)
            {
                return true;
            }

            if (AreTransformBindingsCompatibleWithSelectedModel(animationClip))
            {
                return true;
            }

            string modelSource = BuildAssetSourceCode(modelPath);
            string animationSource = BuildAssetSourceCode(animationPath);
            if (string.Equals(modelSource, "Local", StringComparison.OrdinalIgnoreCase)
                || string.Equals(animationSource, "Local", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(modelSource, animationSource, StringComparison.OrdinalIgnoreCase);
        }

        private bool AreTransformBindingsCompatibleWithSelectedModel(AnimationClip animationClip)
        {
            if (animationClip == null)
            {
                return false;
            }

            HashSet<string> transformPaths = GetSelectedModelTransformPaths();
            if (transformPaths.Count == 0)
            {
                return false;
            }

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(animationClip);
            int transformBindingCount = 0;
            foreach (EditorCurveBinding binding in bindings)
            {
                if (binding.type != typeof(Transform))
                {
                    continue;
                }

                transformBindingCount++;
                if (!transformPaths.Contains(binding.path))
                {
                    return false;
                }
            }

            return transformBindingCount > 0;
        }

        private HashSet<string> GetSelectedModelTransformPaths()
        {
            string modelPath = GetSelectedModelPath();
            if (string.Equals(cachedTransformPathModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                return selectedModelTransformPaths;
            }

            selectedModelTransformPaths.Clear();
            cachedTransformPathModelPath = modelPath;

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset != null)
            {
                CollectTransformPaths(modelAsset.transform, string.Empty, selectedModelTransformPaths);
            }

            return selectedModelTransformPaths;
        }

        private static void CollectTransformPaths(Transform transform, string path, HashSet<string> paths)
        {
            paths.Add(path);
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                string childPath = string.IsNullOrWhiteSpace(path) ? child.name : path + "/" + child.name;
                CollectTransformPaths(child, childPath, paths);
            }
        }

        private string GetSelectedModelPath()
        {
            if (selectedModelIndex < 0 || selectedModelIndex >= modelCandidates.Count)
            {
                return string.Empty;
            }

            return modelCandidates[selectedModelIndex].path;
        }

        private GameObject LoadSelectedModelAsset()
        {
            string modelPath = GetSelectedModelPath();
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                cachedSelectedModelAssetPath = string.Empty;
                cachedSelectedModelAsset = null;
                return null;
            }

            if (!string.Equals(cachedSelectedModelAssetPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                cachedSelectedModelAssetPath = modelPath;
                cachedSelectedModelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            }

            return cachedSelectedModelAsset;
        }

        private static ModelImporterAnimationType GetModelImporterAnimationType(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return ModelImporterAnimationType.None;
            }

            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            return importer == null ? ModelImporterAnimationType.None : importer.animationType;
        }

        private static bool IsEditorPreviewClip(AnimationClip animationClip)
        {
            return animationClip == null
                || string.IsNullOrWhiteSpace(animationClip.name)
                || animationClip.name.StartsWith("__", StringComparison.Ordinal)
                || animationClip.name.Equals("Take 001", StringComparison.OrdinalIgnoreCase) && animationClip.empty;
        }

        private static string BuildAnimationLabel(string path, string clipName)
        {
            return BuildAnimationDisplayName(clipName) + "  [" + BuildAssetSourceCode(path) + "]";
        }

        private void UseSelectedAnimationClip()
        {
            AnimationClip selectedClip = LoadSelectedAnimationClip();
            if (selectedClip == null)
            {
                RefreshAnimationLibrary(false);
                return;
            }

            clip = selectedClip;
            normalizedTime = 0f;

            if (definition != null)
            {
                definition.clip = clip;
                EditorUtility.SetDirty(definition);
            }

            SampleClip();
        }

        private void UseFirstCompatibleAnimationIfNeeded()
        {
            if (clip != null && FindAnimationCandidateIndex(clip) >= 0)
            {
                return;
            }

            if (animationCandidates.Count == 0)
            {
                if (clip != null)
                {
                    clip = null;
                    if (definition != null)
                    {
                        definition.clip = null;
                        EditorUtility.SetDirty(definition);
                    }

                    SampleClip();
                }

                selectedAnimationIndex = -1;
                return;
            }

            selectedAnimationIndex = Mathf.Clamp(selectedAnimationIndex, 0, animationCandidates.Count - 1);
            UseSelectedAnimationClip();
        }

        private AnimationClip LoadSelectedAnimationClip()
        {
            if (selectedAnimationIndex < 0 || selectedAnimationIndex >= animationCandidates.Count)
            {
                return null;
            }

            AnimationClipCandidate candidate = animationCandidates[selectedAnimationIndex];
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(candidate.path);
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset is AnimationClip animationClip && animationClip.name == candidate.clipName)
                {
                    return animationClip;
                }
            }

            return null;
        }

        private int FindAnimationCandidateIndex(AnimationClip animationClip)
        {
            if (animationClip == null)
            {
                return -1;
            }

            string clipPath = AssetDatabase.GetAssetPath(animationClip);
            for (int i = 0; i < animationCandidates.Count; i++)
            {
                AnimationClipCandidate candidate = animationCandidates[i];
                if (string.Equals(candidate.path, clipPath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidate.clipName, animationClip.name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private int GetAuthoringFrameRate()
        {
            if (definition == null)
            {
                return 60;
            }

            definition.authoringFrameRate = Mathf.Max(1, definition.authoringFrameRate);
            return definition.authoringFrameRate;
        }

        private int GetClipFrameCount(int frameRate)
        {
            if (clip == null || frameRate <= 0)
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.RoundToInt(clip.length * frameRate));
        }

        private static int NormalizedTimeToFrame(float value, int frameCount)
        {
            if (frameCount <= 0)
            {
                return 0;
            }

            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value) * frameCount), 0, frameCount);
        }

        private static float FrameToNormalizedTime(int frame, int frameCount)
        {
            if (frameCount <= 0)
            {
                return 0f;
            }

            return Mathf.Clamp01((float)Mathf.Clamp(frame, 0, frameCount) / frameCount);
        }

        private static float FrameToX(Rect track, int frame, int frameCount)
        {
            if (frameCount <= 0)
            {
                return track.x;
            }

            return track.x + track.width * Mathf.Clamp01((float)frame / frameCount);
        }

        private static Rect GetTimelineLaneRect(Rect trackArea, int lane, float laneHeight, float laneGap)
        {
            return new Rect(trackArea.x, trackArea.y + lane * (laneHeight + laneGap), trackArea.width, laneHeight);
        }

        private static int TimelineLaneFor(CombatAnimationEventKind kind)
        {
            switch (kind)
            {
                case CombatAnimationEventKind.Hitbox:
                case CombatAnimationEventKind.ProjectileSpawn:
                    return 0;
                case CombatAnimationEventKind.Hurtbox:
                    return 1;
                case CombatAnimationEventKind.Invulnerability:
                case CombatAnimationEventKind.SuperArmor:
                    return 2;
                case CombatAnimationEventKind.MovementLock:
                case CombatAnimationEventKind.RootMotionScale:
                case CombatAnimationEventKind.Footstep:
                    return 3;
                case CombatAnimationEventKind.ComboBranch:
                case CombatAnimationEventKind.NetworkSync:
                    return 4;
                case CombatAnimationEventKind.Vfx:
                case CombatAnimationEventKind.Sfx:
                case CombatAnimationEventKind.Custom:
                default:
                    return 5;
            }
        }

        private static int TimelineLaneForLinks()
        {
            return 4;
        }

        private static void DrawRectOutline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        private void SetCurrentFrame(int frame, int frameCount)
        {
            normalizedTime = FrameToNormalizedTime(frame, frameCount);
            SampleClip();
            Repaint();
        }

        private void SetNormalizedTime(float value)
        {
            int frameCount = GetClipFrameCount(GetAuthoringFrameRate());
            normalizedTime = Mathf.Clamp01(value);
            if (snapToFrames && frameCount > 0)
            {
                normalizedTime = FrameToNormalizedTime(NormalizedTimeToFrame(normalizedTime, frameCount), frameCount);
            }

            SampleClip();
            Repaint();
        }

        private CombatActionLink CreateActionLinkFromSelectedMarker()
        {
            CombatActionLink link = new CombatActionLink();
            ApplySelectedMarkerToActionLink(link);

            if (string.IsNullOrWhiteSpace(link.triggerTag))
            {
                link.triggerTag = "combat.combo.branch";
            }

            return link;
        }

        private void ApplySelectedMarkerToActionLink(CombatActionLink link)
        {
            if (link == null || definition == null)
            {
                return;
            }

            definition.EnsureMarkers();
            int frameRate = GetAuthoringFrameRate();
            if (selectedMarkerIndex >= 0 && selectedMarkerIndex < definition.markers.Count)
            {
                CombatAnimationMarker marker = definition.markers[selectedMarkerIndex];
                link.triggerTag = marker.gameplayTag;
                link.startFrame = marker.StartFrame(clip, frameRate);
                link.endFrame = Mathf.Max(link.startFrame, marker.EndFrame(clip, frameRate));
            }
            else
            {
                int currentFrame = NormalizedTimeToFrame(normalizedTime, GetClipFrameCount(frameRate));
                link.startFrame = currentFrame;
                link.endFrame = currentFrame + Mathf.RoundToInt(FramesToSeconds(8, frameRate) * frameRate);
            }

            EditorUtility.SetDirty(definition);
        }

        private void AddMarker(CombatAnimationEventKind kind)
        {
            if (definition == null)
            {
                return;
            }

            definition.EnsureMarkers();
            definition.markers.Add(CreateMarkerFromTemplate(kind));
            definition.SortMarkers();
            EditorUtility.SetDirty(definition);
        }

        private void AppendRecipeMarkers(ActionRecipe recipe)
        {
            if (definition == null)
            {
                return;
            }

            definition.EnsureMarkers();
            switch (recipe)
            {
                case ActionRecipe.LightAttack:
                    AddRecipeMarker(CombatAnimationEventKind.NetworkSync, 0, 0);
                    AddRecipeMarker(CombatAnimationEventKind.MovementLock, 0, 18);
                    AddRecipeMarker(CombatAnimationEventKind.Sfx, 7, 0);
                    AddRecipeMarker(CombatAnimationEventKind.Hitbox, 10, 4);
                    AddRecipeMarker(CombatAnimationEventKind.Vfx, 10, 0);
                    AddRecipeMarker(CombatAnimationEventKind.ComboBranch, 18, 10);
                    break;
                case ActionRecipe.HeavyAttack:
                    AddRecipeMarker(CombatAnimationEventKind.NetworkSync, 0, 0);
                    AddRecipeMarker(CombatAnimationEventKind.MovementLock, 0, 34);
                    AddRecipeMarker(CombatAnimationEventKind.SuperArmor, 8, 18);
                    AddRecipeMarker(CombatAnimationEventKind.Sfx, 14, 0);
                    AddRecipeMarker(CombatAnimationEventKind.Hitbox, 20, 6);
                    AddRecipeMarker(CombatAnimationEventKind.Vfx, 20, 0);
                    AddRecipeMarker(CombatAnimationEventKind.ComboBranch, 32, 10);
                    break;
                case ActionRecipe.Dodge:
                    AddRecipeMarker(CombatAnimationEventKind.NetworkSync, 0, 0);
                    AddRecipeMarker(CombatAnimationEventKind.MovementLock, 0, 20);
                    AddRecipeMarker(CombatAnimationEventKind.Invulnerability, 3, 12);
                    AddRecipeMarker(CombatAnimationEventKind.Sfx, 1, 0);
                    break;
                case ActionRecipe.Projectile:
                    AddRecipeMarker(CombatAnimationEventKind.NetworkSync, 0, 0);
                    AddRecipeMarker(CombatAnimationEventKind.MovementLock, 0, 22);
                    AddRecipeMarker(CombatAnimationEventKind.ProjectileSpawn, 13, 0);
                    AddRecipeMarker(CombatAnimationEventKind.Vfx, 13, 0);
                    AddRecipeMarker(CombatAnimationEventKind.Sfx, 13, 0);
                    AddRecipeMarker(CombatAnimationEventKind.ComboBranch, 24, 8);
                    break;
                case ActionRecipe.HitReaction:
                    AddRecipeMarker(CombatAnimationEventKind.NetworkSync, 0, 0);
                    AddRecipeMarker(CombatAnimationEventKind.MovementLock, 0, 24);
                    AddRecipeMarker(CombatAnimationEventKind.Hurtbox, 0, 24);
                    AddRecipeMarker(CombatAnimationEventKind.Sfx, 1, 0);
                    break;
            }

            definition.SortMarkers();
            EditorUtility.SetDirty(definition);
        }

        private void AddRecipeMarker(CombatAnimationEventKind kind, int startFrame, int durationFrames)
        {
            int frameRate = GetAuthoringFrameRate();
            int frameCount = GetClipFrameCount(frameRate);
            CombatAnimationMarker marker = CreateMarkerFromTemplate(kind);
            marker.normalizedTime = FrameToNormalizedTime(Mathf.Clamp(startFrame, 0, frameCount), frameCount);
            marker.duration = FramesToSeconds(durationFrames, frameRate);
            definition.markers.Add(marker);
        }

        private CombatAnimationMarker CreateMarkerFromTemplate(CombatAnimationEventKind kind)
        {
            int frameRate = GetAuthoringFrameRate();
            CombatAnimationMarker marker = new CombatAnimationMarker
            {
                kind = kind,
                normalizedTime = normalizedTime,
                duration = FramesToSeconds(3, frameRate)
            };

            switch (kind)
            {
                case CombatAnimationEventKind.Hitbox:
                    marker.gameplayTag = "combat.hitbox.light";
                    marker.duration = FramesToSeconds(4, frameRate);
                    marker.localOffset = new Vector3(0f, 1.1f, 0.9f);
                    marker.size = new Vector3(0.8f, 0.9f, 1.0f);
                    marker.color = new Color(1f, 0.55f, 0.18f, 0.75f);
                    marker.serverAuthoritative = true;
                    break;
                case CombatAnimationEventKind.Hurtbox:
                    marker.gameplayTag = "combat.hurtbox";
                    marker.duration = clip == null ? FramesToSeconds(10, frameRate) : clip.length;
                    marker.localOffset = new Vector3(0f, 1f, 0f);
                    marker.size = new Vector3(0.8f, 1.8f, 0.55f);
                    marker.color = new Color(0.2f, 0.75f, 1f, 0.45f);
                    marker.serverAuthoritative = false;
                    break;
                case CombatAnimationEventKind.Invulnerability:
                    marker.gameplayTag = "combat.invulnerable";
                    marker.duration = FramesToSeconds(8, frameRate);
                    marker.color = new Color(0.45f, 0.4f, 1f, 0.65f);
                    marker.serverAuthoritative = true;
                    break;
                case CombatAnimationEventKind.SuperArmor:
                    marker.gameplayTag = "combat.super_armor";
                    marker.duration = FramesToSeconds(10, frameRate);
                    marker.color = new Color(0.95f, 0.75f, 0.2f, 0.65f);
                    marker.serverAuthoritative = true;
                    break;
                case CombatAnimationEventKind.MovementLock:
                    marker.gameplayTag = "movement.lock";
                    marker.duration = FramesToSeconds(12, frameRate);
                    marker.color = new Color(0.8f, 0.55f, 0.35f, 0.65f);
                    marker.serverAuthoritative = true;
                    break;
                case CombatAnimationEventKind.RootMotionScale:
                    marker.gameplayTag = "movement.root_motion_scale";
                    marker.payload = "1.0";
                    marker.duration = 0f;
                    marker.color = new Color(0.35f, 0.7f, 0.35f, 0.7f);
                    marker.serverAuthoritative = true;
                    break;
                case CombatAnimationEventKind.ComboBranch:
                    marker.gameplayTag = "combat.combo.branch";
                    marker.duration = FramesToSeconds(8, frameRate);
                    marker.color = new Color(0.25f, 0.9f, 0.45f, 0.7f);
                    marker.serverAuthoritative = true;
                    break;
                case CombatAnimationEventKind.ProjectileSpawn:
                    marker.gameplayTag = "combat.projectile.spawn";
                    marker.duration = 0f;
                    marker.color = new Color(0.95f, 0.35f, 0.25f, 0.75f);
                    marker.serverAuthoritative = true;
                    break;
                case CombatAnimationEventKind.Vfx:
                    marker.gameplayTag = "vfx.spawn";
                    marker.duration = 0f;
                    marker.color = new Color(0.35f, 0.95f, 0.95f, 0.7f);
                    marker.serverAuthoritative = false;
                    break;
                case CombatAnimationEventKind.Sfx:
                    marker.gameplayTag = "sfx.play";
                    marker.duration = 0f;
                    marker.color = new Color(0.55f, 0.75f, 1f, 0.7f);
                    marker.serverAuthoritative = false;
                    break;
                case CombatAnimationEventKind.Footstep:
                    marker.gameplayTag = "movement.footstep";
                    marker.duration = 0f;
                    marker.color = new Color(0.65f, 0.65f, 0.65f, 0.7f);
                    marker.serverAuthoritative = false;
                    break;
                case CombatAnimationEventKind.NetworkSync:
                    marker.gameplayTag = "net.sync.action";
                    marker.duration = 0f;
                    marker.color = new Color(1f, 0.95f, 0.25f, 0.95f);
                    marker.serverAuthoritative = true;
                    break;
                default:
                    marker.gameplayTag = "custom";
                    marker.color = new Color(0.9f, 0.9f, 0.9f, 0.65f);
                    break;
            }

            return marker;
        }

        private static float FramesToSeconds(int frames, int frameRate)
        {
            if (frameRate <= 0)
            {
                return 0f;
            }

            return Mathf.Max(0, frames) / (float)frameRate;
        }

        private static CombatAnimationMarker CloneMarker(CombatAnimationMarker source)
        {
            return new CombatAnimationMarker
            {
                kind = source.kind,
                normalizedTime = source.normalizedTime,
                duration = source.duration,
                gameplayTag = source.gameplayTag,
                payload = source.payload,
                localOffset = source.localOffset,
                size = source.size,
                color = source.color,
                serverAuthoritative = source.serverAuthoritative
            };
        }

        private static string BuildAssetSourceCode(string path)
        {
            string normalized = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
            if (normalized.Contains("Kenney_AnimatedCharacters3", StringComparison.OrdinalIgnoreCase))
            {
                return "Kenney";
            }

            if (normalized.Contains("UniversalAnimationLibrary2", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("UAL2", StringComparison.OrdinalIgnoreCase))
            {
                return "UAL2";
            }

            if (normalized.Contains("UniversalAnimationLibrary", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("AnimationLibrary", StringComparison.OrdinalIgnoreCase))
            {
                return "UAL";
            }

            return "Local";
        }

        private static string BuildModelDisplayName(string path)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName.Equals("characterMedium", StringComparison.OrdinalIgnoreCase))
            {
                return "Character Medium";
            }

            if (fileName.Equals("Mannequin_F", StringComparison.OrdinalIgnoreCase))
            {
                return "Female Mannequin";
            }

            return ToReadableWords(fileName);
        }

        private static string BuildAnimationDisplayName(string rawName)
        {
            string readable = ToReadableAnimationName(rawName);
            string category = CategorizeAnimationName(readable);
            if (string.IsNullOrWhiteSpace(category)
                || readable.Equals(category, StringComparison.OrdinalIgnoreCase)
                || readable.StartsWith(category + " ", StringComparison.OrdinalIgnoreCase))
            {
                return readable;
            }

            return category + " / " + readable;
        }

        private static string BuildActionIdFromClipName(string rawName)
        {
            string displayName = BuildAnimationDisplayName(rawName).Replace(" / ", ".");
            return "action." + SanitizeActionId(displayName);
        }

        private static string ToReadableAnimationName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "Unknown";
            }

            string name = rawName.Trim();
            int pipeIndex = name.LastIndexOf('|');
            if (pipeIndex >= 0 && pipeIndex < name.Length - 1)
            {
                name = name.Substring(pipeIndex + 1);
            }

            List<string> tokens = TokenizeName(name);
            List<string> readableTokens = new List<string>();
            foreach (string token in tokens)
            {
                string normalized = NormalizeAnimationNameToken(token);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    readableTokens.Add(normalized);
                }
            }

            return readableTokens.Count == 0 ? ToReadableWords(rawName) : string.Join(" ", readableTokens);
        }

        private static string ToReadableWords(string rawName)
        {
            List<string> tokens = TokenizeName(rawName);
            List<string> readableTokens = new List<string>();
            foreach (string token in tokens)
            {
                string normalized = NormalizeNameToken(token);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    readableTokens.Add(normalized);
                }
            }

            return readableTokens.Count == 0 ? rawName : string.Join(" ", readableTokens);
        }

        private static List<string> TokenizeName(string value)
        {
            List<string> tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return tokens;
            }

            string spaced = InsertCamelCaseSpaces(value);
            char[] separators = { ' ', '_', '-', '.', '/', '\\', '[', ']', '(', ')' };
            string[] parts = spaced.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    tokens.Add(part.Trim());
                }
            }

            return tokens;
        }

        private static string InsertCamelCaseSpaces(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (i > 0 && char.IsUpper(current) && (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1])))
                {
                    builder.Append(' ');
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        private static string NormalizeNameToken(string token)
        {
            string lower = token.ToLowerInvariant();
            switch (lower)
            {
                case "armature":
                case "mixamo":
                case "com":
                case "loop":
                case "loops":
                case "animation":
                case "animations":
                case "anim":
                case "library":
                case "standard":
                case "unity":
                case "unreal":
                case "godot":
                case "fbx":
                case "glb":
                case "take":
                    return string.Empty;
                case "f":
                case "female":
                    return "Female";
                case "m":
                case "male":
                    return "Male";
                case "fwd":
                case "forward":
                case "forwards":
                    return "Forward";
                case "bwd":
                case "back":
                case "backward":
                case "backwards":
                    return "Backward";
                case "l":
                case "left":
                    return "Left";
                case "r":
                case "right":
                    return "Right";
                case "idle":
                    return "Idle";
                case "run":
                case "running":
                    return "Run";
                case "walk":
                case "walking":
                    return "Walk";
                case "jump":
                case "jumping":
                    return "Jump";
                case "fall":
                case "falling":
                    return "Fall";
                case "land":
                case "landing":
                    return "Land";
                case "strafe":
                case "straffing":
                    return "Strafe";
                case "sprint":
                case "sprinting":
                    return "Sprint";
                case "crouch":
                case "crouching":
                    return "Crouch";
                case "dodge":
                case "dodging":
                    return "Dodge";
                case "roll":
                case "rolling":
                    return "Roll";
                case "block":
                case "blocking":
                    return "Block";
                case "parry":
                case "parrying":
                    return "Parry";
                case "guard":
                case "guarding":
                    return "Guard";
                case "attack":
                case "attacking":
                    return "Attack";
                case "punch":
                case "punching":
                    return "Punch";
                case "kick":
                case "kicking":
                    return "Kick";
                case "slash":
                case "slashing":
                    return "Slash";
                case "swing":
                case "swinging":
                    return "Swing";
                case "stab":
                case "stabbing":
                    return "Stab";
                case "shoot":
                case "shooting":
                    return "Shoot";
                case "throw":
                case "throwing":
                    return "Throw";
                case "hit":
                case "hurt":
                case "damage":
                case "damaged":
                    return "Hit";
                case "reaction":
                case "react":
                    return "Reaction";
                case "death":
                case "die":
                case "dying":
                    return "Death";
                default:
                    return char.ToUpperInvariant(token[0]) + token.Substring(1).ToLowerInvariant();
            }
        }

        private static string NormalizeAnimationNameToken(string token)
        {
            string lower = token.ToLowerInvariant();
            switch (lower)
            {
                case "zombie":
                case "zombies":
                case "human":
                case "humans":
                case "character":
                case "characters":
                case "mannequin":
                case "skeleton":
                case "rig":
                case "male":
                case "female":
                    return string.Empty;
                default:
                    return NormalizeNameToken(token);
            }
        }

        private static string CategorizeAnimationName(string readableName)
        {
            string lower = readableName.ToLowerInvariant();
            if (lower.Contains("idle") || lower.Contains("stand"))
            {
                return "Idle";
            }

            if (ContainsAny(lower, "run", "walk", "sprint", "jump", "fall", "land", "strafe", "crouch", "forward", "backward", "left", "right"))
            {
                return "Move";
            }

            if (ContainsAny(lower, "dodge", "roll", "block", "parry", "guard"))
            {
                return "Defense";
            }

            if (ContainsAny(lower, "attack", "punch", "kick", "slash", "swing", "stab", "shoot", "throw"))
            {
                return "Combat";
            }

            if (ContainsAny(lower, "hit", "hurt", "reaction", "death"))
            {
                return "Hit";
            }

            return "Misc";
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            for (int i = 0; i < terms.Length; i++)
            {
                if (value.Contains(terms[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string SanitizeAssetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "NewAction";
            }

            string sanitized = value.Replace(" / ", "_").Replace(' ', '_');
            char[] chars = sanitized.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                bool valid = char.IsLetterOrDigit(chars[i]) || chars[i] == '_' || chars[i] == '-';
                if (!valid)
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        private static string SanitizeActionId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "new";
            }

            char[] chars = value.Trim().Replace(" / ", ".").Replace(' ', '_').ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                bool valid = char.IsLetterOrDigit(chars[i]) || chars[i] == '_' || chars[i] == '-' || chars[i] == '.';
                if (!valid)
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        private void ExportDefinitionJson()
        {
            if (definition == null)
            {
                return;
            }

            ActToolkitEditorUtilities.EnsureGeneratedFolders();

            CombatAnimationExport export = CombatAnimationExport.FromDefinition(definition);
            string json = JsonUtility.ToJson(export, true);
            string path = ActToolkitEditorUtilities.GeneratedFolder + "/Animations/" + definition.name + ".combat-animation.json";
            ActToolkitEditorUtilities.WriteTextAsset(path, json);
            EditorUtility.RevealInFinder(path);
        }

        private void AnalyzeRootMotion()
        {
            rootMotionDistances.Clear();
            rootMotionSpeeds.Clear();
            rootMotionTotalDistance = 0f;
            rootMotionPeakSpeed = 0f;

            GameObject modelAsset = LoadSelectedModelAsset();
            if (modelAsset == null || clip == null)
            {
                rootMotionStatus = "Assign a preview Animator and clip first.";
                return;
            }

            int frameRate = GetAuthoringFrameRate();
            int frameCount = GetClipFrameCount(frameRate);
            if (frameCount <= 0)
            {
                rootMotionStatus = "Clip has no measurable frames.";
                return;
            }

            GameObject sampleInstance = Instantiate(modelAsset);
            sampleInstance.hideFlags = HideFlags.HideAndDontSave;
            ApplyPreviewHideFlags(sampleInstance);

            try
            {
                Transform root = sampleInstance.transform;
                Vector3 previousPosition = root.position;
                Vector3 firstPosition = root.position;
                bool initialized = false;

                for (int frame = 0; frame <= frameCount; frame++)
                {
                    float sampleTime = frame / (float)frameRate;
                    clip.SampleAnimation(sampleInstance, Mathf.Min(sampleTime, clip.length));

                    Vector3 currentPosition = root.position;
                    if (!initialized)
                    {
                        initialized = true;
                        firstPosition = currentPosition;
                        previousPosition = currentPosition;
                    }

                    float distanceFromStart = Vector3.Distance(firstPosition, currentPosition);
                    float frameDistance = Vector3.Distance(previousPosition, currentPosition);
                    float speed = frame == 0 ? 0f : frameDistance * frameRate;
                    rootMotionDistances.Add(distanceFromStart);
                    rootMotionSpeeds.Add(speed);
                    rootMotionPeakSpeed = Mathf.Max(rootMotionPeakSpeed, speed);
                    previousPosition = currentPosition;
                }

                rootMotionTotalDistance = rootMotionDistances.Count == 0 ? 0f : rootMotionDistances[rootMotionDistances.Count - 1];
                rootMotionStatus = rootMotionTotalDistance <= 0.0001f
                    ? "Analyzed. No root translation was detected on the preview object."
                    : "Analyzed " + (frameCount + 1) + " samples.";
            }
            finally
            {
                DestroyImmediate(sampleInstance);
            }

            SampleClip();
        }

        private void EditorUpdate()
        {
            if (!isPlaying || clip == null)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float delta = (float)(now - lastUpdateTime);
            lastUpdateTime = now;

            float length = Mathf.Max(0.001f, clip.length);
            normalizedTime += delta / length;

            bool shouldLoop = definition != null && definition.loopPreview;
            if (normalizedTime > 1f)
            {
                normalizedTime = shouldLoop ? normalizedTime % 1f : 1f;
                isPlaying = shouldLoop;
            }

            SampleClip();
            if (now >= nextEditorRepaintTime)
            {
                nextEditorRepaintTime = now + EditorRepaintInterval;
                Repaint();
            }
        }

        private void SampleClip()
        {
            if (previewRoot == null || clip == null)
            {
                return;
            }

            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
            }

            float sampleTime = Mathf.Clamp01(normalizedTime) * clip.length;
            GameObject sampleTarget = previewAnimator != null ? previewAnimator.gameObject : previewRoot;
            AnimationMode.SampleAnimationClip(sampleTarget, clip, sampleTime);
            NormalizePreviewTransformsAfterSample();
            SceneView.RepaintAll();
        }

        private void DuringSceneGui(SceneView sceneView)
        {
            if (previewRoot == null || definition == null || definition.markers == null || definition.markers.Count == 0)
            {
                return;
            }

            AnimationClip activeClip = clip != null ? clip : definition.clip;
            float clipLength = activeClip == null ? 0f : activeClip.length;
            float currentTime = normalizedTime * clipLength;

            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;
            Handles.matrix = previewRoot.transform.localToWorldMatrix;

            for (int i = 0; i < definition.markers.Count; i++)
            {
                CombatAnimationMarker marker = definition.markers[i];
                if (marker == null)
                {
                    continue;
                }

                if (!drawAllMarkers && !IsMarkerActive(marker, currentTime, clipLength))
                {
                    continue;
                }

                Handles.color = marker.color;
                Handles.DrawWireCube(marker.localOffset, marker.size);
                Handles.Label(marker.localOffset + Vector3.up * Mathf.Max(0.15f, marker.size.y * 0.5f), marker.kind + " " + marker.gameplayTag);

                if (i == selectedMarkerIndex)
                {
                    DrawSelectedMarkerHandles(marker);
                }
            }

            Handles.color = previousColor;
            Handles.matrix = previousMatrix;
        }

        private void DrawSelectedMarkerHandles(CombatAnimationMarker marker)
        {
            if (previewRoot == null)
            {
                return;
            }

            Transform root = previewRoot.transform;
            Matrix4x4 previousMatrix = Handles.matrix;
            Handles.matrix = Matrix4x4.identity;

            Vector3 worldPosition = root.TransformPoint(marker.localOffset);
            Quaternion worldRotation = root.rotation;
            float handleSize = HandleUtility.GetHandleSize(worldPosition);

            EditorGUI.BeginChangeCheck();
            Vector3 nextWorldPosition = Handles.PositionHandle(worldPosition, worldRotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(definition, "Move Combat Marker");
                marker.localOffset = root.InverseTransformPoint(nextWorldPosition);
                EditorUtility.SetDirty(definition);
                Repaint();
            }

            EditorGUI.BeginChangeCheck();
            Vector3 nextSize = Handles.ScaleHandle(marker.size, worldPosition, worldRotation, handleSize);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(definition, "Scale Combat Marker");
                marker.size = new Vector3(
                    Mathf.Max(0.01f, nextSize.x),
                    Mathf.Max(0.01f, nextSize.y),
                    Mathf.Max(0.01f, nextSize.z));
                EditorUtility.SetDirty(definition);
                Repaint();
            }

            Handles.matrix = previousMatrix;
        }

        private static bool IsMarkerActive(CombatAnimationMarker marker, float currentTime, float clipLength)
        {
            if (clipLength <= 0f)
            {
                return false;
            }

            float start = marker.normalizedTime * clipLength;
            float end = start + marker.duration;
            return currentTime >= start && currentTime <= end;
        }

        private void StopPreview()
        {
            isPlaying = false;
            if (AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }
        }

        private readonly struct ModelCandidate
        {
            public readonly string path;
            public readonly string label;

            public ModelCandidate(string path, string label)
            {
                this.path = path;
                this.label = label;
            }
        }

        private readonly struct AnimationClipCandidate
        {
            public readonly string path;
            public readonly string clipName;
            public readonly string label;

            public AnimationClipCandidate(string path, string clipName, string label)
            {
                this.path = path;
                this.clipName = clipName;
                this.label = label;
            }
        }

        private enum ActionRecipe
        {
            LightAttack,
            HeavyAttack,
            Dodge,
            Projectile,
            HitReaction
        }

        [Serializable]
        private sealed class CombatAnimationExport
        {
            public string actionId;
            public string stateName;
            public string clipAssetPath;
            public float clipLength;
            public int authoringFrameRate;
            public int frameCount;
            public bool requiresNetworkSync;
            public bool loopPreview;
            public float rootMotionScale;
            public List<CombatAnimationMarkerExport> markers = new List<CombatAnimationMarkerExport>();
            public List<CombatActionLinkExport> actionLinks = new List<CombatActionLinkExport>();

            public static CombatAnimationExport FromDefinition(CombatAnimationDefinition definition)
            {
                int frameRate = Mathf.Max(1, definition.authoringFrameRate);
                int frameCount = definition.clip == null ? 0 : Mathf.Max(1, Mathf.RoundToInt(definition.clip.length * frameRate));
                CombatAnimationExport export = new CombatAnimationExport
                {
                    actionId = definition.actionId,
                    stateName = definition.stateName,
                    clipAssetPath = definition.clip == null ? string.Empty : AssetDatabase.GetAssetPath(definition.clip),
                    clipLength = definition.clip == null ? 0f : definition.clip.length,
                    authoringFrameRate = frameRate,
                    frameCount = frameCount,
                    requiresNetworkSync = definition.requiresNetworkSync,
                    loopPreview = definition.loopPreview,
                    rootMotionScale = definition.rootMotionScale
                };

                definition.EnsureMarkers();
                foreach (CombatAnimationMarker marker in definition.markers)
                {
                    export.markers.Add(CombatAnimationMarkerExport.FromMarker(marker, definition.clip, frameRate));
                }

                definition.EnsureActionLinks();
                foreach (CombatActionLink link in definition.actionLinks)
                {
                    export.actionLinks.Add(CombatActionLinkExport.FromLink(link, definition.clip, frameRate));
                }

                return export;
            }
        }

        [Serializable]
        private sealed class CombatAnimationMarkerExport
        {
            public string id;
            public string kind;
            public float normalizedTime;
            public float timeSeconds;
            public float duration;
            public int startFrame;
            public int durationFrames;
            public int endFrame;
            public string gameplayTag;
            public string payload;
            public Vector3 localOffset;
            public Vector3 size;
            public bool serverAuthoritative;

            public static CombatAnimationMarkerExport FromMarker(CombatAnimationMarker marker, AnimationClip clip, int frameRate)
            {
                return new CombatAnimationMarkerExport
                {
                    id = marker.id,
                    kind = marker.kind.ToString(),
                    normalizedTime = marker.normalizedTime,
                    timeSeconds = marker.TimeSeconds(clip),
                    duration = marker.duration,
                    startFrame = marker.StartFrame(clip, frameRate),
                    durationFrames = marker.DurationFrames(frameRate),
                    endFrame = marker.EndFrame(clip, frameRate),
                    gameplayTag = marker.gameplayTag,
                    payload = marker.payload,
                    localOffset = marker.localOffset,
                    size = marker.size,
                    serverAuthoritative = marker.serverAuthoritative
                };
            }
        }

        [Serializable]
        private sealed class CombatActionLinkExport
        {
            public string id;
            public string inputAction;
            public string triggerTag;
            public string targetActionId;
            public string targetAssetPath;
            public int startFrame;
            public int endFrame;
            public float startNormalizedTime;
            public float endNormalizedTime;
            public bool serverAuthoritative;

            public static CombatActionLinkExport FromLink(CombatActionLink link, AnimationClip clip, int frameRate)
            {
                return new CombatActionLinkExport
                {
                    id = link.id,
                    inputAction = link.inputAction,
                    triggerTag = link.triggerTag,
                    targetActionId = link.targetActionId,
                    targetAssetPath = link.targetDefinition == null ? string.Empty : AssetDatabase.GetAssetPath(link.targetDefinition),
                    startFrame = link.startFrame,
                    endFrame = link.endFrame,
                    startNormalizedTime = link.StartNormalizedTime(clip, frameRate),
                    endNormalizedTime = link.EndNormalizedTime(clip, frameRate),
                    serverAuthoritative = link.serverAuthoritative
                };
            }
        }
    }

    internal readonly struct CombatAnimationValidationIssue
    {
        public readonly MessageType type;
        public readonly string message;

        public CombatAnimationValidationIssue(MessageType type, string message)
        {
            this.type = type;
            this.message = message;
        }
    }

    internal static class CombatAnimationValidation
    {
        public static List<CombatAnimationValidationIssue> ValidateDefinition(CombatAnimationDefinition definition)
        {
            List<CombatAnimationValidationIssue> issues = new List<CombatAnimationValidationIssue>();
            if (definition == null)
            {
                issues.Add(new CombatAnimationValidationIssue(MessageType.Error, "Definition is missing."));
                return issues;
            }

            definition.EnsureMarkers();
            definition.EnsureActionLinks();

            AnimationClip clip = definition.clip;
            int frameRate = Mathf.Max(1, definition.authoringFrameRate);
            int frameCount = clip == null ? 0 : Mathf.Max(1, Mathf.RoundToInt(clip.length * frameRate));

            if (clip == null)
            {
                issues.Add(new CombatAnimationValidationIssue(MessageType.Error, "No animation clip is assigned."));
            }

            if (string.IsNullOrWhiteSpace(definition.actionId))
            {
                issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "Action Id is empty. Use a stable id for runtime and server references."));
            }

            bool hasNetworkSync = false;
            bool hasHitbox = false;
            bool hasComboBranch = false;

            if (definition.markers.Count == 0)
            {
                issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "No gameplay markers yet. Add at least a NetworkSync marker and any combat windows this action needs."));
            }

            for (int i = 0; i < definition.markers.Count; i++)
            {
                CombatAnimationMarker marker = definition.markers[i];
                if (marker == null)
                {
                    issues.Add(new CombatAnimationValidationIssue(MessageType.Error, "Marker " + (i + 1) + " is null."));
                    continue;
                }

                if (marker.kind == CombatAnimationEventKind.NetworkSync)
                {
                    hasNetworkSync = true;
                }

                if (marker.kind == CombatAnimationEventKind.ComboBranch)
                {
                    hasComboBranch = true;
                }

                if (marker.kind == CombatAnimationEventKind.Hitbox)
                {
                    hasHitbox = true;
                    if (!marker.serverAuthoritative)
                    {
                        issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "Hitbox marker " + (i + 1) + " is not server authoritative."));
                    }

                    if (marker.size.x <= 0f || marker.size.y <= 0f || marker.size.z <= 0f)
                    {
                        issues.Add(new CombatAnimationValidationIssue(MessageType.Error, "Hitbox marker " + (i + 1) + " has a non-positive size."));
                    }
                }

                if (string.IsNullOrWhiteSpace(marker.gameplayTag))
                {
                    issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "Marker " + (i + 1) + " has an empty gameplay tag."));
                }

                if (clip != null && marker.TimeSeconds(clip) + marker.duration > clip.length + 0.0001f)
                {
                    issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "Marker " + (i + 1) + " extends past the end of the clip."));
                }
            }

            if (definition.requiresNetworkSync && !hasNetworkSync)
            {
                issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "Require Net Sync is enabled but no NetworkSync marker exists."));
            }

            if (hasHitbox && !hasNetworkSync)
            {
                issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "This action has hitboxes but no NetworkSync marker. Add one near the authoritative startup frame."));
            }

            if (hasComboBranch && definition.actionLinks.Count == 0)
            {
                issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "This action has ComboBranch markers but no action links."));
            }

            ValidateActionLinks(definition, issues, frameCount);
            return issues;
        }

        public static int ErrorCount(IReadOnlyList<CombatAnimationValidationIssue> issues)
        {
            return CountByType(issues, MessageType.Error);
        }

        public static int WarningCount(IReadOnlyList<CombatAnimationValidationIssue> issues)
        {
            return CountByType(issues, MessageType.Warning);
        }

        private static int CountByType(IReadOnlyList<CombatAnimationValidationIssue> issues, MessageType type)
        {
            int count = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].type == type)
                {
                    count++;
                }
            }

            return count;
        }

        private static void ValidateActionLinks(CombatAnimationDefinition definition, List<CombatAnimationValidationIssue> issues, int frameCount)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < definition.actionLinks.Count; i++)
            {
                CombatActionLink link = definition.actionLinks[i];
                if (link == null)
                {
                    issues.Add(new CombatAnimationValidationIssue(MessageType.Error, "Action link " + (i + 1) + " is null."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(link.id))
                {
                    issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "Action link " + (i + 1) + " has an empty id."));
                }
                else if (!ids.Add(link.id))
                {
                    issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "Action link " + (i + 1) + " duplicates another link id."));
                }

                if (string.IsNullOrWhiteSpace(link.inputAction))
                {
                    issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "Action link " + (i + 1) + " has an empty input action."));
                }

                if (string.IsNullOrWhiteSpace(link.targetActionId))
                {
                    issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "Action link " + (i + 1) + " has no target action id."));
                }

                if (link.endFrame < link.startFrame)
                {
                    issues.Add(new CombatAnimationValidationIssue(MessageType.Error, "Action link " + (i + 1) + " ends before it starts."));
                }

                if (frameCount > 0 && link.endFrame > frameCount)
                {
                    issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "Action link " + (i + 1) + " extends past the clip frame count."));
                }

                if (link.targetDefinition != null && link.targetDefinition == definition)
                {
                    issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "Action link " + (i + 1) + " targets itself."));
                }
            }
        }
    }

    public sealed class CombatAnimationBatchValidatorWindow : EditorWindow
    {
        private readonly List<ValidationRow> rows = new List<ValidationRow>();
        private Vector2 scroll;
        private int totalErrors;
        private int totalWarnings;

        [MenuItem("Tools/Act Toolkit/Combat Animation Batch Validator")]
        public static void Open()
        {
            CombatAnimationBatchValidatorWindow window = GetWindow<CombatAnimationBatchValidatorWindow>();
            window.titleContent = new GUIContent("Combat Batch Validator");
            window.minSize = new Vector2(620f, 420f);
            window.Refresh();
            window.Show();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Combat Animation Batch Validator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Scans every CombatAnimationDefinition asset and reports action-data issues before runtime or server export.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Height(28f)))
            {
                Refresh();
            }

            if (GUILayout.Button("Select All Problem Assets", GUILayout.Height(28f)))
            {
                SelectProblemAssets();
            }
            EditorGUILayout.EndHorizontal();

            MessageType summaryType = totalErrors > 0 ? MessageType.Error : totalWarnings > 0 ? MessageType.Warning : MessageType.Info;
            EditorGUILayout.HelpBox(rows.Count + " definitions checked. Errors: " + totalErrors + ". Warnings: " + totalWarnings + ".", summaryType);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (ValidationRow row in rows)
            {
                DrawRow(row);
            }
            EditorGUILayout.EndScrollView();
        }

        private void Refresh()
        {
            rows.Clear();
            totalErrors = 0;
            totalWarnings = 0;

            string[] guids = AssetDatabase.FindAssets("t:CombatAnimationDefinition");
            Dictionary<string, int> actionIdCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CombatAnimationDefinition definition = AssetDatabase.LoadAssetAtPath<CombatAnimationDefinition>(path);
                if (definition == null)
                {
                    continue;
                }

                definition.EnsureMarkers();
                definition.EnsureActionLinks();

                if (!string.IsNullOrWhiteSpace(definition.actionId))
                {
                    actionIdCounts.TryGetValue(definition.actionId, out int count);
                    actionIdCounts[definition.actionId] = count + 1;
                }

                rows.Add(new ValidationRow(definition, path, CombatAnimationValidation.ValidateDefinition(definition)));
            }

            foreach (ValidationRow row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.definition.actionId)
                    && actionIdCounts.TryGetValue(row.definition.actionId, out int count)
                    && count > 1)
                {
                    row.issues.Add(new CombatAnimationValidationIssue(MessageType.Error, "Duplicate action id: " + row.definition.actionId));
                }

                totalErrors += CombatAnimationValidation.ErrorCount(row.issues);
                totalWarnings += CombatAnimationValidation.WarningCount(row.issues);
            }

            rows.Sort((left, right) =>
            {
                int leftScore = CombatAnimationValidation.ErrorCount(left.issues) * 1000 + CombatAnimationValidation.WarningCount(left.issues);
                int rightScore = CombatAnimationValidation.ErrorCount(right.issues) * 1000 + CombatAnimationValidation.WarningCount(right.issues);
                int scoreCompare = rightScore.CompareTo(leftScore);
                return scoreCompare != 0 ? scoreCompare : string.Compare(left.path, right.path, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static void DrawRow(ValidationRow row)
        {
            int errors = CombatAnimationValidation.ErrorCount(row.issues);
            int warnings = CombatAnimationValidation.WarningCount(row.issues);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(row.definition.actionId + "  (" + row.definition.name + ")", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(errors + " errors / " + warnings + " warnings", GUILayout.Width(150f));
            if (GUILayout.Button("Ping", GUILayout.Width(58f)))
            {
                EditorGUIUtility.PingObject(row.definition);
            }

            if (GUILayout.Button("Select", GUILayout.Width(64f)))
            {
                Selection.activeObject = row.definition;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(row.path, EditorStyles.miniLabel);

            if (row.issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Ready.", MessageType.Info);
            }
            else
            {
                foreach (CombatAnimationValidationIssue issue in row.issues)
                {
                    EditorGUILayout.HelpBox(issue.message, issue.type);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void SelectProblemAssets()
        {
            List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
            foreach (ValidationRow row in rows)
            {
                if (row.issues.Count > 0)
                {
                    assets.Add(row.definition);
                }
            }

            Selection.objects = assets.ToArray();
        }

        private sealed class ValidationRow
        {
            public readonly CombatAnimationDefinition definition;
            public readonly string path;
            public readonly List<CombatAnimationValidationIssue> issues;

            public ValidationRow(CombatAnimationDefinition definition, string path, List<CombatAnimationValidationIssue> issues)
            {
                this.definition = definition;
                this.path = path;
                this.issues = issues;
            }
        }
    }

    internal sealed class CombatComboGraphView
    {
        private const string DatabasePrefsKey = "ActToolkit.CombatComboGraph.Database";
        private const string DefaultDatabasePath = ActToolkitEditorUtilities.CombatMvpFolder + "/MVP_CombatActionDatabase.asset";
        private const float CanvasWidth = 2200f;
        private const float CanvasHeight = 1400f;
        private const float NodeWidth = 190f;
        private const float NodeHeight = 92f;
        private const float PortSize = 12f;

        private readonly List<GraphNode> nodes = new List<GraphNode>();
        private readonly Dictionary<CombatAnimationDefinition, GraphNode> nodeLookup = new Dictionary<CombatAnimationDefinition, GraphNode>();
        private readonly Dictionary<string, GraphNode> idLookup = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        private Vector2 graphScroll;
        private Vector2 inspectorScroll;
        private Rect entryNodeRect = new Rect(24f, 120f, 170f, 78f);
        private CombatActionDatabase database;
        private string selectedInputAction = CombatInputActionNames.LightAttack;
        private int defaultStartFrame = 12;
        private int defaultEndFrame = 24;
        private bool draggingLink;
        private bool draggingFromEntry;
        private CombatAnimationDefinition dragSourceDefinition;
        private Vector2 dragMousePosition;
        private CombatAnimationDefinition selectedDefinition;
        private CombatAnimationDefinition selectedLinkSource;
        private CombatActionLink selectedLink;
        private CombatActionEntry selectedEntry;
        private readonly Action repaint;

        public CombatComboGraphView(Action repaint)
        {
            this.repaint = repaint;
        }

        public CombatActionDatabase Database => database;

        public void Initialize()
        {
            string databasePath = EditorPrefs.GetString(DatabasePrefsKey, DefaultDatabasePath);
            database = AssetDatabase.LoadAssetAtPath<CombatActionDatabase>(databasePath);
            if (database == null)
            {
                database = AssetDatabase.LoadAssetAtPath<CombatActionDatabase>(DefaultDatabasePath);
            }

            RefreshGraph();
        }

        public void SetDatabase(CombatActionDatabase nextDatabase, bool savePreference)
        {
            if (database == nextDatabase)
            {
                return;
            }

            database = nextDatabase;
            selectedDefinition = null;
            selectedLinkSource = null;
            selectedLink = null;
            selectedEntry = null;

            if (savePreference)
            {
                string path = database == null ? string.Empty : AssetDatabase.GetAssetPath(database);
                EditorPrefs.SetString(DatabasePrefsKey, path);
            }

            RefreshGraph();
            RequestRepaint();
        }

        public void Refresh()
        {
            RefreshGraph();
            RequestRepaint();
        }

        public void Draw(bool embedded)
        {
            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawGraphPanel(embedded);
            DrawInspectorPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("Combo Table", GUILayout.Width(76f));
            EditorGUI.BeginChangeCheck();
            database = (CombatActionDatabase)EditorGUILayout.ObjectField(database, typeof(CombatActionDatabase), false, GUILayout.Width(220f));
            if (EditorGUI.EndChangeCheck())
            {
                string path = database == null ? string.Empty : AssetDatabase.GetAssetPath(database);
                EditorPrefs.SetString(DatabasePrefsKey, path);
                RefreshGraph();
            }

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                RefreshGraph();
            }

            GUILayout.Space(10f);
            GUILayout.Label("New Link", GUILayout.Width(56f));
            selectedInputAction = DrawInputActionToolbarPopup(selectedInputAction, GUILayout.Width(150f));
            GUILayout.Label("Window", GUILayout.Width(48f));
            defaultStartFrame = Mathf.Max(0, EditorGUILayout.IntField(defaultStartFrame, GUILayout.Width(42f)));
            GUILayout.Label("-", GUILayout.Width(10f));
            defaultEndFrame = Mathf.Max(defaultStartFrame, EditorGUILayout.IntField(defaultEndFrame, GUILayout.Width(42f)));

            GUILayout.FlexibleSpace();
            GUILayout.Label("Drag from a right port to a left port.", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawGraphPanel(bool embedded)
        {
            if (embedded)
            {
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.Height(430f));
            }
            else
            {
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }

            try
            {
                Rect outerRect = embedded
                    ? GUILayoutUtility.GetRect(200f, 10000f, 360f, 360f, GUILayout.ExpandWidth(true), GUILayout.Height(360f))
                    : GUILayoutUtility.GetRect(200f, 10000f, 200f, 10000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                Rect canvasRect = new Rect(0f, 0f, CanvasWidth, CanvasHeight);

                graphScroll = GUI.BeginScrollView(outerRect, graphScroll, canvasRect);
                try
                {
                    DrawGrid(canvasRect, 24f, new Color(0f, 0f, 0f, 0.18f));
                    DrawGrid(canvasRect, 120f, new Color(0f, 0f, 0f, 0.28f));
                    DrawConnections();

                    if (draggingLink)
                    {
                        DrawDragConnection(Event.current.mousePosition);
                    }

                    if (database != null)
                    {
                        entryNodeRect = GUI.Window(1, entryNodeRect, DrawEntryNodeWindow, "Entry");
                    }

                    for (int i = 0; i < nodes.Count; i++)
                    {
                        GraphNode node = nodes[i];
                        Rect before = node.rect;
                        node.rect = GUI.Window(100 + i, node.rect, id => DrawActionNodeWindow(node), NodeTitle(node.definition));
                        if (node.rect.position != before.position)
                        {
                            SaveNodePosition(node);
                        }
                    }

                    DrawPorts();
                    HandleGraphEvents(Event.current);
                }
                finally
                {
                    GUI.EndScrollView();
                }
            }
            finally
            {
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawInspectorPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(330f), GUILayout.ExpandHeight(true));
            try
            {
                EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);
                inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
                try
                {
                    if (selectedEntry != null)
                    {
                        DrawEntryInspector();
                    }
                    else if (selectedLink != null)
                    {
                        DrawLinkInspector();
                    }
                    else if (selectedDefinition != null)
                    {
                        DrawDefinitionInspector();
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Select a node or link. Drag Entry -> Action to set an opening move, or Action -> Action to create a combo branch.", MessageType.None);
                    }
                }
                finally
                {
                    EditorGUILayout.EndScrollView();
                }
            }
            finally
            {
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawEntryInspector()
        {
            if (database == null)
            {
                selectedEntry = null;
                return;
            }

            EditorGUI.BeginChangeCheck();
            selectedEntry.inputAction = DrawInputActionPopup("Input", selectedEntry.inputAction);
            selectedEntry.targetDefinition = (CombatAnimationDefinition)EditorGUILayout.ObjectField("Target", selectedEntry.targetDefinition, typeof(CombatAnimationDefinition), false);
            if (selectedEntry.targetDefinition != null)
            {
                selectedEntry.targetActionId = selectedEntry.targetDefinition.actionId;
            }

            selectedEntry.targetActionId = EditorGUILayout.TextField("Target Id", selectedEntry.targetActionId);
            selectedEntry.serverAuthoritative = EditorGUILayout.Toggle("Server Auth", selectedEntry.serverAuthoritative);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(database);
                RefreshGraphLookupsOnly();
            }

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Remove Entry Link", GUILayout.Height(28f)))
            {
                database.entryActions.Remove(selectedEntry);
                EditorUtility.SetDirty(database);
                selectedEntry = null;
            }
        }

        private void DrawLinkInspector()
        {
            if (selectedLinkSource == null || selectedLink == null)
            {
                selectedLink = null;
                return;
            }

            int frameCount = selectedLinkSource.clip == null
                ? 0
                : Mathf.Max(1, Mathf.RoundToInt(selectedLinkSource.clip.length * Mathf.Max(1, selectedLinkSource.authoringFrameRate)));

            EditorGUILayout.LabelField("Source", selectedLinkSource.actionId, EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            selectedLink.inputAction = DrawInputActionPopup("Input", selectedLink.inputAction);
            selectedLink.triggerTag = EditorGUILayout.TextField("Trigger Tag", selectedLink.triggerTag);
            selectedLink.targetDefinition = (CombatAnimationDefinition)EditorGUILayout.ObjectField("Target", selectedLink.targetDefinition, typeof(CombatAnimationDefinition), false);
            if (selectedLink.targetDefinition != null)
            {
                selectedLink.targetActionId = selectedLink.targetDefinition.actionId;
            }

            selectedLink.targetActionId = EditorGUILayout.TextField("Target Id", selectedLink.targetActionId);
            selectedLink.startFrame = Mathf.Clamp(EditorGUILayout.IntField("Start Frame", selectedLink.startFrame), 0, Mathf.Max(0, frameCount));
            selectedLink.endFrame = Mathf.Clamp(EditorGUILayout.IntField("End Frame", selectedLink.endFrame), selectedLink.startFrame, Mathf.Max(selectedLink.startFrame, frameCount));
            selectedLink.serverAuthoritative = EditorGUILayout.Toggle("Server Auth", selectedLink.serverAuthoritative);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(selectedLinkSource);
                RefreshGraphLookupsOnly();
            }

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Remove Combo Link", GUILayout.Height(28f)))
            {
                selectedLinkSource.actionLinks.Remove(selectedLink);
                EditorUtility.SetDirty(selectedLinkSource);
                selectedLink = null;
                selectedLinkSource = null;
            }
        }

        private void DrawDefinitionInspector()
        {
            CombatAnimationDefinition inspectedDefinition = selectedDefinition;
            if (inspectedDefinition == null)
            {
                selectedDefinition = null;
                EditorGUILayout.HelpBox("The selected definition is no longer available. Select a node again.", MessageType.None);
                return;
            }

            EditorGUILayout.ObjectField("Definition", inspectedDefinition, typeof(CombatAnimationDefinition), false);
            EditorGUI.BeginChangeCheck();
            inspectedDefinition.actionId = EditorGUILayout.TextField("Action Id", inspectedDefinition.actionId);
            inspectedDefinition.stateName = EditorGUILayout.TextField("State Name", inspectedDefinition.stateName);
            inspectedDefinition.clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", inspectedDefinition.clip, typeof(AnimationClip), false);
            inspectedDefinition.authoringFrameRate = Mathf.Max(1, EditorGUILayout.IntField("Frame Rate", inspectedDefinition.authoringFrameRate));
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(inspectedDefinition);
                RefreshGraphLookupsOnly();
            }

            EditorGUILayout.Space(8f);
            inspectedDefinition.EnsureActionLinks();
            EditorGUILayout.LabelField("Outgoing Links", EditorStyles.boldLabel);
            for (int i = 0; i < inspectedDefinition.actionLinks.Count; i++)
            {
                CombatActionLink link = inspectedDefinition.actionLinks[i];
                if (link == null)
                {
                    continue;
                }

                string target = link.targetDefinition != null ? link.targetDefinition.actionId : link.targetActionId;
                if (GUILayout.Button(ShortLabel(link.inputAction) + " -> " + target + "  " + link.startFrame + "-" + link.endFrame + "f"))
                {
                    SelectLink(inspectedDefinition, link);
                    return;
                }
            }

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Select Asset", GUILayout.Height(26f)))
            {
                Selection.activeObject = inspectedDefinition;
                EditorGUIUtility.PingObject(inspectedDefinition);
            }
        }

        private void DrawEntryNodeWindow(int id)
        {
            if (database == null)
            {
                GUILayout.Label("No database", EditorStyles.miniLabel);
            }
            else
            {
                database.EnsureEntryActions();
                GUILayout.Label(database.name, EditorStyles.miniBoldLabel);
                GUILayout.Label(database.entryActions.Count + " entry links", EditorStyles.miniLabel);
                if (GUILayout.Button("Select"))
                {
                    Selection.activeObject = database;
                }
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private void DrawActionNodeWindow(GraphNode node)
        {
            CombatAnimationDefinition action = node.definition;
            if (action == null)
            {
                return;
            }

            GUILayout.Label(ShortLabel(action.actionId), EditorStyles.miniBoldLabel);
            GUILayout.Label(action.clip == null ? "No clip" : action.clip.name, EditorStyles.miniLabel);
            action.EnsureActionLinks();
            GUILayout.Label(action.actionLinks.Count + " links", EditorStyles.miniLabel);

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private void DrawConnections()
        {
            if (database != null)
            {
                database.EnsureEntryActions();
                foreach (CombatActionEntry entry in database.entryActions)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    GraphNode targetNode = ResolveTargetNode(entry.targetDefinition, entry.targetActionId);
                    if (targetNode == null)
                    {
                        continue;
                    }

                    DrawConnection(GetEntryOutputPort().center, GetInputPort(targetNode.rect).center, InputColor(entry.inputAction), selectedEntry == entry);
                    DrawConnectionButton(GetConnectionMidpoint(GetEntryOutputPort().center, GetInputPort(targetNode.rect).center), ShortLabel(entry.inputAction), () =>
                    {
                        selectedEntry = entry;
                        selectedLink = null;
                        selectedLinkSource = null;
                        selectedDefinition = null;
                    });
                }
            }

            foreach (GraphNode sourceNode in nodes)
            {
                CombatAnimationDefinition source = sourceNode.definition;
                if (source == null)
                {
                    continue;
                }

                source.EnsureActionLinks();
                foreach (CombatActionLink link in source.actionLinks)
                {
                    if (link == null)
                    {
                        continue;
                    }

                    GraphNode targetNode = ResolveTargetNode(link.targetDefinition, link.targetActionId);
                    if (targetNode == null)
                    {
                        continue;
                    }

                    Vector2 start = GetOutputPort(sourceNode.rect).center;
                    Vector2 end = GetInputPort(targetNode.rect).center;
                    DrawConnection(start, end, InputColor(link.inputAction), selectedLink == link);
                    DrawConnectionButton(GetConnectionMidpoint(start, end), ShortLabel(link.inputAction) + " " + link.startFrame + "-" + link.endFrame + "f", () => SelectLink(source, link));
                }
            }
        }

        private void DrawDragConnection(Vector2 mousePosition)
        {
            Vector2 start = draggingFromEntry
                ? GetEntryOutputPort().center
                : dragSourceDefinition != null && nodeLookup.TryGetValue(dragSourceDefinition, out GraphNode sourceNode)
                    ? GetOutputPort(sourceNode.rect).center
                    : mousePosition;

            DrawConnection(start, mousePosition, InputColor(selectedInputAction), true);
        }

        private void DrawPorts()
        {
            if (database != null)
            {
                DrawPort(GetEntryOutputPort(), InputColor(selectedInputAction));
            }

            foreach (GraphNode node in nodes)
            {
                DrawPort(GetInputPort(node.rect), new Color(0.38f, 0.62f, 0.88f, 1f));
                DrawPort(GetOutputPort(node.rect), InputColor(selectedInputAction));
            }
        }

        private void HandleGraphEvents(Event evt)
        {
            dragMousePosition = evt.mousePosition;
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (database != null && GetEntryOutputPort().Contains(evt.mousePosition))
                {
                    BeginDragLink(true, null, evt);
                    return;
                }

                foreach (GraphNode node in nodes)
                {
                    if (GetOutputPort(node.rect).Contains(evt.mousePosition))
                    {
                        BeginDragLink(false, node.definition, evt);
                        return;
                    }
                }

                foreach (GraphNode node in nodes)
                {
                    if (node.rect.Contains(evt.mousePosition))
                    {
                        selectedDefinition = node.definition;
                        selectedLink = null;
                        selectedLinkSource = null;
                        selectedEntry = null;
                        RequestRepaint();
                        return;
                    }
                }

                if (database != null && entryNodeRect.Contains(evt.mousePosition))
                {
                    selectedDefinition = null;
                    selectedLink = null;
                    selectedLinkSource = null;
                    selectedEntry = null;
                    Selection.activeObject = database;
                    RequestRepaint();
                }
            }
            else if (evt.type == EventType.MouseDrag && draggingLink)
            {
                RequestRepaint();
            }
            else if (evt.type == EventType.MouseUp && draggingLink)
            {
                CompleteDragLink(evt.mousePosition);
                evt.Use();
            }
        }

        private void BeginDragLink(bool fromEntry, CombatAnimationDefinition source, Event evt)
        {
            draggingLink = true;
            draggingFromEntry = fromEntry;
            dragSourceDefinition = source;
            dragMousePosition = evt.mousePosition;
            evt.Use();
        }

        private void CompleteDragLink(Vector2 mousePosition)
        {
            GraphNode target = null;
            foreach (GraphNode node in nodes)
            {
                if (GetInputPort(node.rect).Contains(mousePosition) || node.rect.Contains(mousePosition))
                {
                    target = node;
                    break;
                }
            }

            if (target != null)
            {
                if (draggingFromEntry)
                {
                    CreateOrUpdateEntry(target.definition);
                }
                else if (dragSourceDefinition != null && dragSourceDefinition != target.definition)
                {
                    CreateOrUpdateActionLink(dragSourceDefinition, target.definition);
                }
            }

            draggingLink = false;
            draggingFromEntry = false;
            dragSourceDefinition = null;
            RequestRepaint();
        }

        private void RequestRepaint()
        {
            repaint?.Invoke();
        }

        private void CreateOrUpdateEntry(CombatAnimationDefinition target)
        {
            if (database == null || target == null)
            {
                return;
            }

            database.EnsureEntryActions();
            CombatActionEntry entry = null;
            foreach (CombatActionEntry candidate in database.entryActions)
            {
                if (candidate != null && CombatInputActionNames.Matches(candidate.inputAction, selectedInputAction))
                {
                    entry = candidate;
                    break;
                }
            }

            if (entry == null)
            {
                entry = new CombatActionEntry();
                database.entryActions.Add(entry);
            }

            entry.inputAction = CombatInputActionNames.Normalize(selectedInputAction);
            entry.targetDefinition = target;
            entry.targetActionId = target.actionId;
            entry.serverAuthoritative = true;
            EditorUtility.SetDirty(database);

            selectedEntry = entry;
            selectedLink = null;
            selectedLinkSource = null;
            selectedDefinition = null;
        }

        private void CreateOrUpdateActionLink(CombatAnimationDefinition source, CombatAnimationDefinition target)
        {
            if (source == null || target == null)
            {
                return;
            }

            source.EnsureActionLinks();
            CombatActionLink link = null;
            foreach (CombatActionLink candidate in source.actionLinks)
            {
                if (candidate == null)
                {
                    continue;
                }

                bool sameInput = CombatInputActionNames.Matches(candidate.inputAction, selectedInputAction);
                bool sameTarget = candidate.targetDefinition == target || string.Equals(candidate.targetActionId, target.actionId, StringComparison.OrdinalIgnoreCase);
                if (sameInput && sameTarget)
                {
                    link = candidate;
                    break;
                }
            }

            if (link == null)
            {
                link = new CombatActionLink();
                source.actionLinks.Add(link);
            }

            link.inputAction = CombatInputActionNames.Normalize(selectedInputAction);
            link.triggerTag = "combat.combo.branch";
            link.targetDefinition = target;
            link.targetActionId = target.actionId;
            link.startFrame = Mathf.Min(defaultStartFrame, defaultEndFrame);
            link.endFrame = Mathf.Max(defaultStartFrame, defaultEndFrame);
            link.serverAuthoritative = true;
            EditorUtility.SetDirty(source);

            SelectLink(source, link);
        }

        private void SelectLink(CombatAnimationDefinition source, CombatActionLink link)
        {
            selectedDefinition = null;
            selectedEntry = null;
            selectedLinkSource = source;
            selectedLink = link;
        }

        private void RefreshGraph()
        {
            nodes.Clear();
            nodeLookup.Clear();
            idLookup.Clear();

            List<CombatAnimationDefinition> definitions = LoadGraphDefinitions();
            for (int i = 0; i < definitions.Count; i++)
            {
                CombatAnimationDefinition definition = definitions[i];
                GraphNode node = new GraphNode(definition, LoadNodeRect(definition, i));
                nodes.Add(node);
                nodeLookup[definition] = node;
                if (!string.IsNullOrWhiteSpace(definition.actionId))
                {
                    idLookup[definition.actionId] = node;
                }
            }

            RefreshGraphLookupsOnly();
        }

        private void RefreshGraphLookupsOnly()
        {
            idLookup.Clear();
            foreach (GraphNode node in nodes)
            {
                if (node.definition != null && !string.IsNullOrWhiteSpace(node.definition.actionId))
                {
                    idLookup[node.definition.actionId] = node;
                }
            }
        }

        private List<CombatAnimationDefinition> LoadGraphDefinitions()
        {
            List<CombatAnimationDefinition> definitions = new List<CombatAnimationDefinition>();
            if (database != null && database.actions != null)
            {
                database.EnsureEntryActions();
                foreach (CombatAnimationDefinition action in database.actions)
                {
                    if (action != null && !definitions.Contains(action))
                    {
                        definitions.Add(action);
                    }
                }
            }

            if (definitions.Count == 0)
            {
                string[] guids = AssetDatabase.FindAssets("t:CombatAnimationDefinition", new[] { ActToolkitEditorUtilities.DefaultCombatDefinitionFolder });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    CombatAnimationDefinition definition = AssetDatabase.LoadAssetAtPath<CombatAnimationDefinition>(path);
                    if (definition != null)
                    {
                        definitions.Add(definition);
                    }
                }
            }

            definitions.Sort((left, right) => string.Compare(NodeTitle(left), NodeTitle(right), StringComparison.OrdinalIgnoreCase));
            return definitions;
        }

        private GraphNode ResolveTargetNode(CombatAnimationDefinition targetDefinition, string targetActionId)
        {
            if (targetDefinition != null && nodeLookup.TryGetValue(targetDefinition, out GraphNode directNode))
            {
                return directNode;
            }

            if (!string.IsNullOrWhiteSpace(targetActionId) && idLookup.TryGetValue(targetActionId, out GraphNode idNode))
            {
                return idNode;
            }

            return null;
        }

        private static Rect LoadNodeRect(CombatAnimationDefinition definition, int index)
        {
            string key = NodePrefsKey(definition);
            float x = EditorPrefs.GetFloat(key + ".x", 240f + index % 4 * 240f);
            float y = EditorPrefs.GetFloat(key + ".y", 70f + index / 4 * 150f);
            return new Rect(x, y, NodeWidth, NodeHeight);
        }

        private static void SaveNodePosition(GraphNode node)
        {
            if (node == null || node.definition == null)
            {
                return;
            }

            string key = NodePrefsKey(node.definition);
            EditorPrefs.SetFloat(key + ".x", node.rect.x);
            EditorPrefs.SetFloat(key + ".y", node.rect.y);
        }

        private static string NodePrefsKey(CombatAnimationDefinition definition)
        {
            string path = AssetDatabase.GetAssetPath(definition);
            string guid = string.IsNullOrWhiteSpace(path)
                ? GlobalObjectId.GetGlobalObjectIdSlow(definition).ToString()
                : AssetDatabase.AssetPathToGUID(path);
            return "ActToolkit.CombatComboGraph.Node." + guid;
        }

        private static Rect GetInputPort(Rect nodeRect)
        {
            return new Rect(nodeRect.x - PortSize * 0.5f, nodeRect.y + nodeRect.height * 0.5f - PortSize * 0.5f, PortSize, PortSize);
        }

        private static Rect GetOutputPort(Rect nodeRect)
        {
            return new Rect(nodeRect.xMax - PortSize * 0.5f, nodeRect.y + nodeRect.height * 0.5f - PortSize * 0.5f, PortSize, PortSize);
        }

        private Rect GetEntryOutputPort()
        {
            return GetOutputPort(entryNodeRect);
        }

        private static Vector2 GetConnectionMidpoint(Vector2 start, Vector2 end)
        {
            return (start + end) * 0.5f;
        }

        private static void DrawConnection(Vector2 start, Vector2 end, Color color, bool selected)
        {
            Color previousColor = Handles.color;
            Handles.BeginGUI();
            Handles.DrawBezier(
                start,
                end,
                start + Vector2.right * 80f,
                end + Vector2.left * 80f,
                selected ? Color.yellow : color,
                null,
                selected ? 4f : 2.5f);
            Handles.EndGUI();
            Handles.color = previousColor;
        }

        private static void DrawConnectionButton(Vector2 center, string label, Action onClick)
        {
            Rect rect = new Rect(center.x - 58f, center.y - 10f, 116f, 20f);
            if (GUI.Button(rect, label, EditorStyles.miniButton))
            {
                onClick?.Invoke();
            }
        }

        private static void DrawPort(Rect rect, Color color)
        {
            EditorGUI.DrawRect(rect, color);
        }

        private static void DrawGrid(Rect rect, float spacing, Color color)
        {
            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = color;

            for (float x = 0f; x < rect.width; x += spacing)
            {
                Handles.DrawLine(new Vector3(x, 0f), new Vector3(x, rect.height));
            }

            for (float y = 0f; y < rect.height; y += spacing)
            {
                Handles.DrawLine(new Vector3(0f, y), new Vector3(rect.width, y));
            }

            Handles.color = previousColor;
            Handles.EndGUI();
        }

        private static Color InputColor(string inputAction)
        {
            string normalized = CombatInputActionNames.Normalize(inputAction);
            if (string.Equals(normalized, CombatInputActionNames.HeavyAttack, StringComparison.OrdinalIgnoreCase))
            {
                return new Color(0.95f, 0.48f, 0.26f, 1f);
            }

            if (string.Equals(normalized, CombatInputActionNames.Dodge, StringComparison.OrdinalIgnoreCase))
            {
                return new Color(0.34f, 0.78f, 0.88f, 1f);
            }

            return new Color(0.48f, 0.75f, 0.38f, 1f);
        }

        private static string DrawInputActionPopup(string label, string inputAction)
        {
            string normalized = CombatInputActionNames.Normalize(inputAction);
            int selectedIndex = Array.IndexOf(CombatInputActionNames.AuthoringNames, normalized);
            string[] labels = BuildInputActionPopupLabels(inputAction, selectedIndex);
            int nextIndex = EditorGUILayout.Popup(label, selectedIndex >= 0 ? selectedIndex : labels.Length - 1, labels);
            if (nextIndex >= 0 && nextIndex < CombatInputActionNames.AuthoringNames.Length)
            {
                return CombatInputActionNames.AuthoringNames[nextIndex];
            }

            return EditorGUILayout.TextField("Custom Input", string.IsNullOrWhiteSpace(inputAction) ? normalized : inputAction);
        }

        private static string DrawInputActionToolbarPopup(string inputAction, params GUILayoutOption[] options)
        {
            string normalized = CombatInputActionNames.Normalize(inputAction);
            int selectedIndex = Array.IndexOf(CombatInputActionNames.AuthoringNames, normalized);
            string[] labels = BuildInputActionPopupLabels(inputAction, selectedIndex);
            int nextIndex = EditorGUILayout.Popup(selectedIndex >= 0 ? selectedIndex : labels.Length - 1, labels, EditorStyles.toolbarPopup, options);
            if (nextIndex >= 0 && nextIndex < CombatInputActionNames.AuthoringNames.Length)
            {
                return CombatInputActionNames.AuthoringNames[nextIndex];
            }

            return inputAction;
        }

        private static string[] BuildInputActionPopupLabels(string inputAction, int selectedIndex)
        {
            if (selectedIndex >= 0)
            {
                return CombatInputActionNames.AuthoringLabels;
            }

            string custom = string.IsNullOrWhiteSpace(inputAction) ? "Custom" : "Custom: " + inputAction;
            string[] labels = new string[CombatInputActionNames.AuthoringLabels.Length + 1];
            Array.Copy(CombatInputActionNames.AuthoringLabels, labels, CombatInputActionNames.AuthoringLabels.Length);
            labels[labels.Length - 1] = custom;
            return labels;
        }

        private static string NodeTitle(CombatAnimationDefinition definition)
        {
            if (definition == null)
            {
                return "Missing";
            }

            return string.IsNullOrWhiteSpace(definition.stateName) ? definition.name : definition.stateName;
        }

        private static string ShortLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "None";
            }

            if (string.Equals(CombatInputActionNames.Normalize(value), CombatInputActionNames.LightAttack, StringComparison.OrdinalIgnoreCase))
            {
                return "Light";
            }

            if (string.Equals(value, CombatInputActionNames.HeavyAttack, StringComparison.OrdinalIgnoreCase))
            {
                return "Heavy";
            }

            return value.Length <= 20 ? value : value.Substring(0, 17) + "...";
        }

        private sealed class GraphNode
        {
            public readonly CombatAnimationDefinition definition;
            public Rect rect;

            public GraphNode(CombatAnimationDefinition definition, Rect rect)
            {
                this.definition = definition;
                this.rect = rect;
            }
        }
    }

    public sealed class CombatComboGraphEditorWindow : EditorWindow
    {
        private CombatComboGraphView graphView;

        public static void Open()
        {
            CombatComboGraphEditorWindow window = GetWindow<CombatComboGraphEditorWindow>();
            window.titleContent = new GUIContent("Combat Combo Graph");
            window.minSize = new Vector2(860f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            graphView = new CombatComboGraphView(Repaint);
            graphView.Initialize();
        }

        private void OnGUI()
        {
            if (graphView == null)
            {
                graphView = new CombatComboGraphView(Repaint);
                graphView.Initialize();
            }

            graphView.Draw(false);
        }
    }
}
