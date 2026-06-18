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
        private const string EditorPagePrefsKey = "ActToolkit.CharacterActionEditor.Page";
        private const string DefaultCharacterProfilePath = ActToolkitEditorUtilities.CombatMvpFolder + "/Female_Mannequin_Profile.asset";
        private const string ModelFolderPrefsKey = "ActToolkit.CombatAnimationEditor.ModelFolder";
        private const string AnimationFolderPrefsKey = "ActToolkit.CombatAnimationEditor.AnimationFolder";
        private const string PreviewModelNamePrefix = "ActPreview_";
        private const string LegacyPreviewModelNamePrefix = "Preview_";
        private const string PreviewLightName = "Preview Key Light";
        private const string PreviewSampleRootName = "Preview Model";
        private const double EditorRepaintInterval = 1d / 15d;
        private const string PreviewSceneName = "ActAnimationPreview";

        private enum EditorPage
        {
            CharacterSetup,
            ComboTable,
            DefinitionEditor
        }

        private enum CharacterDefaultClipSlot
        {
            Idle,
            Move
        }

        private enum AnimationLibraryContext
        {
            ActionAuthoring,
            CharacterDefaults
        }

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
        private bool showAdvancedComboGraph;
        private EditorPage currentPage = EditorPage.ComboTable;
        private CharacterDefaultClipSlot defaultClipSlot = CharacterDefaultClipSlot.Idle;
        private string comboEditorInputAction = CombatInputActionNames.LightAttack;
        private int comboEditorDefaultStartFrame = 12;
        private int comboEditorDefaultEndFrame = 24;
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
        private bool keepSelectedActionOnScheduledPreviewRefresh;
        private readonly List<ComboPreviewStep> comboPreviewSteps = new List<ComboPreviewStep>();
        private CombatAnimationDefinition comboPreviewTargetDefinition;
        private float comboPreviewTime;
        private string comboPreviewPathLabel = string.Empty;
        private bool comboPreviewActive;

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
            currentPage = (EditorPage)Mathf.Clamp(
                EditorPrefs.GetInt(EditorPagePrefsKey, (int)EditorPage.ComboTable),
                (int)EditorPage.CharacterSetup,
                (int)EditorPage.DefinitionEditor);
            characterActionGraphView = new CombatComboGraphView(Repaint, SelectActionForAuthoring, SelectComboActionForPreview);
            characterActionGraphView.Initialize();
            LoadCharacterProfileFromPrefs();
            if (characterProfile == null)
            {
                currentPage = EditorPage.CharacterSetup;
            }

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
            EnsureDefaultCharacterProfileLoaded();
            EditorGUILayout.Space(6f);
            DrawCharacterProfileHeader();
            EditorGUILayout.Space(4f);
            DrawPageNavigator();

            if (currentPage == EditorPage.DefinitionEditor)
            {
                EditorGUILayout.Space(4f);
                DrawStatusHeader();
                EditorGUILayout.Space(4f);
                DrawQuickActions();
            }

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
            UnityEngine.Object selectedCharacterObject = EditorGUILayout.ObjectField(characterProfile, typeof(UnityEngine.Object), false, GUILayout.MinWidth(180f));
            if (EditorGUI.EndChangeCheck())
            {
                SelectCharacterProfileObject(selectedCharacterObject);
            }

            if (GUILayout.Button("New Character", GUILayout.Width(106f)))
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
            EditorGUILayout.EndVertical();
        }

        private void DrawPageNavigator()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Work Area", GUILayout.Width(72f));
            DrawPageButton(EditorPage.CharacterSetup, "Character Setup", 132f);
            DrawPageButton(EditorPage.ComboTable, "Combo Table", 112f);
            DrawPageButton(EditorPage.DefinitionEditor, "Definition Editor", 142f);
            GUILayout.FlexibleSpace();

            if (characterProfile != null && currentPage != EditorPage.CharacterSetup)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    UnityEngine.Object contextObject = currentPage == EditorPage.ComboTable
                        ? characterProfile.comboTable
                        : characterProfile.modelPrefab;
                    Type contextType = currentPage == EditorPage.ComboTable
                        ? typeof(CombatActionDatabase)
                        : typeof(GameObject);
                    EditorGUILayout.ObjectField(contextObject, contextType, false, GUILayout.Width(220f));
                }

                GUILayout.Label(currentPage == EditorPage.ComboTable
                    ? "combo table comes from the selected character"
                    : "model comes from the selected character", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPageButton(EditorPage page, string label, float width)
        {
            bool selected = currentPage == page;
            bool nextSelected = GUILayout.Toggle(selected, label, EditorStyles.toolbarButton, GUILayout.Width(width));
            if (!selected && nextSelected)
            {
                SwitchPage(page);
                GUI.FocusControl(null);
            }
        }

        private void SwitchPage(EditorPage page)
        {
            CombatAnimationDefinition graphSelection = page == EditorPage.DefinitionEditor
                ? GetSelectedComboTableDefinition()
                : null;

            currentPage = page;
            EditorPrefs.SetInt(EditorPagePrefsKey, (int)currentPage);
            if (currentPage == EditorPage.DefinitionEditor)
            {
                bool hasGraphSelection = graphSelection != null;
                ApplyCharacterProfileToEditor(true, !hasGraphSelection);
                if (hasGraphSelection)
                {
                    SelectSingleActionForPreview(graphSelection);
                    mainScroll = Vector2.zero;
                }
            }
        }

        private CombatAnimationDefinition GetSelectedComboTableDefinition()
        {
            CombatAnimationDefinition selectedAction = characterActionGraphView == null
                ? null
                : characterActionGraphView.SelectedDefinition;
            return IsDefinitionInCurrentComboTable(selectedAction) ? selectedAction : null;
        }

        private bool IsDefinitionInCurrentComboTable(CombatAnimationDefinition action)
        {
            if (action == null || characterProfile == null || characterProfile.comboTable == null)
            {
                return false;
            }

            foreach (CombatAnimationDefinition candidate in GetDatabaseActions(characterProfile.comboTable))
            {
                if (candidate == action)
                {
                    return true;
                }
            }

            return false;
        }

        private void LoadCharacterProfileFromPrefs()
        {
            string profilePath = EditorPrefs.GetString(CharacterProfilePrefsKey, DefaultCharacterProfilePath);
            characterProfile = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(profilePath);
            if (!IsUsableCharacterProfile(characterProfile))
            {
                characterProfile = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(DefaultCharacterProfilePath);
            }

            if (!IsUsableCharacterProfile(characterProfile))
            {
                characterProfile = FindBestCharacterProfileAsset();
            }

            if (characterProfile != null)
            {
                SaveCharacterProfilePrefs();
            }
        }

        private void EnsureDefaultCharacterProfileLoaded()
        {
            if (characterProfile != null && !IsPlaceholderCharacterProfile(characterProfile))
            {
                return;
            }

            CharacterActionProfile previousProfile = characterProfile;
            LoadCharacterProfileFromPrefs();
            if (characterProfile == null || characterProfile == previousProfile)
            {
                return;
            }

            ApplyCharacterProfileToEditor(true);
        }

        private static bool IsUsableCharacterProfile(CharacterActionProfile profile)
        {
            if (profile == null)
            {
                return false;
            }

            if (profile.modelPrefab != null)
            {
                return true;
            }

            if (profile.comboTable != null
                && profile.comboTable.actions != null
                && profile.comboTable.actions.Count > 0
                && !IsPlaceholderCharacterProfile(profile))
            {
                return true;
            }

            return false;
        }

        private static bool IsPlaceholderCharacterProfile(CharacterActionProfile profile)
        {
            if (profile == null)
            {
                return true;
            }

            return string.Equals(profile.name, "New_Character_Profile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(profile.displayName, "New Character", StringComparison.OrdinalIgnoreCase)
                || string.Equals(profile.characterId, "character.new.character", StringComparison.OrdinalIgnoreCase);
        }

        private static CharacterActionProfile FindBestCharacterProfileAsset()
        {
            string[] preferredPaths =
            {
                ActToolkitEditorUtilities.CombatMvpFolder + "/Female_Mannequin_Profile.asset",
                ActToolkitEditorUtilities.CombatMvpFolder + "/CharacterActionProfile.asset"
            };

            foreach (string path in preferredPaths)
            {
                CharacterActionProfile preferred = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(path);
                if (preferred != null)
                {
                    return preferred;
                }
            }

            string[] guids = AssetDatabase.FindAssets("t:CharacterActionProfile", new[] { ActToolkitEditorUtilities.CombatMvpFolder });
            CharacterActionProfile bestProfile = null;
            int bestScore = int.MinValue;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CharacterActionProfile candidate = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(path);
                if (candidate == null)
                {
                    continue;
                }

                int score = 0;
                if (candidate.modelPrefab != null)
                {
                    score += 100;
                }

                if (candidate.comboTable != null)
                {
                    score += 60;
                    if (candidate.comboTable.actions != null)
                    {
                        score += Mathf.Min(candidate.comboTable.actions.Count, 20);
                    }
                }

                if (!string.IsNullOrWhiteSpace(candidate.displayName)
                    && !candidate.displayName.StartsWith("New", System.StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestProfile = candidate;
                }
            }

            return bestProfile;
        }

        private static CharacterActionProfile FindCharacterProfileForModel(GameObject modelPrefab)
        {
            if (modelPrefab == null)
            {
                return null;
            }

            string modelPath = AssetDatabase.GetAssetPath(modelPrefab);
            string[] guids = AssetDatabase.FindAssets("t:CharacterActionProfile");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CharacterActionProfile candidate = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(path);
                if (candidate == null || candidate.modelPrefab == null)
                {
                    continue;
                }

                if (candidate.modelPrefab == modelPrefab)
                {
                    return candidate;
                }

                string candidateModelPath = AssetDatabase.GetAssetPath(candidate.modelPrefab);
                if (!string.IsNullOrWhiteSpace(modelPath)
                    && string.Equals(candidateModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void SaveCharacterProfilePrefs()
        {
            string path = characterProfile == null ? string.Empty : AssetDatabase.GetAssetPath(characterProfile);
            EditorPrefs.SetString(CharacterProfilePrefsKey, path);
        }

        private void CreateCharacterProfileAsset()
        {
            ActToolkitEditorUtilities.EnsureGeneratedFolders();

            string modelPath = GetSelectedModelPath();
            string displayName = string.IsNullOrWhiteSpace(modelPath) ? "New Character" : BuildModelDisplayName(modelPath);

            CharacterActionProfile profile = CreateInstance<CharacterActionProfile>();
            profile.characterId = BuildCharacterIdFromName(displayName);
            profile.displayName = displayName;
            profile.modelPrefab = LoadSelectedModelAsset();
            profile.avatar = LoadAvatarFromModel(modelPath);
            profile.comboTable = CreateComboTableAsset(displayName);
            profile.idleClip = null;
            profile.moveClip = null;
            profile.EnsureDefaults();

            string path = AssetDatabase.GenerateUniqueAssetPath(ActToolkitEditorUtilities.CombatMvpFolder + "/" + SanitizeAssetName(displayName) + "_Profile.asset");
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();

            characterProfile = profile;
            SaveCharacterProfilePrefs();
            ApplyCharacterProfileToEditor(true);
            currentPage = EditorPage.CharacterSetup;
            EditorPrefs.SetInt(EditorPagePrefsKey, (int)currentPage);
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        private static string BuildCharacterIdFromName(string displayName)
        {
            string sanitized = SanitizeAssetName(string.IsNullOrWhiteSpace(displayName) ? "New Character" : displayName);
            return "character." + sanitized.ToLowerInvariant().Replace('_', '.');
        }

        private CombatActionDatabase CreateComboTableAsset(string displayName)
        {
            ActToolkitEditorUtilities.EnsureGeneratedFolders();

            string assetName = SanitizeAssetName(string.IsNullOrWhiteSpace(displayName) ? "New Character" : displayName) + "_ComboTable";
            string path = AssetDatabase.GenerateUniqueAssetPath(ActToolkitEditorUtilities.CombatMvpFolder + "/" + assetName + ".asset");
            CombatActionDatabase database = CreateInstance<CombatActionDatabase>();
            AssetDatabase.CreateAsset(database, path);
            EditorUtility.SetDirty(database);
            return database;
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

        private void ApplyCharacterProfileToEditor(bool refreshPreview, bool selectFirstAction = true)
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
            if (selectFirstAction)
            {
                SelectFirstProfileAction();
            }

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

            ClearComboPreview();
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
            if (currentPage == EditorPage.CharacterSetup)
            {
                DrawCharacterSetupPage();
                return;
            }

            if (currentPage == EditorPage.ComboTable)
            {
                DrawComboTablePage();
                return;
            }

            DrawDefinitionEditorPage();
        }

        private void DrawComboTablePage()
        {
            DrawCharacterActionGraph(false);
        }

        private void DrawDefinitionEditorPage()
        {
            DrawMainAuthoringWorkbench();
            EditorGUILayout.Space(8f);
            DrawUtilityDrawers();
        }

        private void DrawCharacterSetupPage()
        {
            if (characterProfile == null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Character Setup", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Create or select a Character Action Profile first. This page owns the model, Avatar, idle/move clips, and combo table for that character.", MessageType.Info);
                if (GUILayout.Button("New Character", GUILayout.Height(30f), GUILayout.Width(160f)))
                {
                    CreateCharacterProfileAsset();
                }

                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.MinWidth(360f));
            DrawCharacterBasicSettings();
            EditorGUILayout.Space(8f);
            DrawCharacterRuntimeSummary();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(GUILayout.MinWidth(360f));
            DrawModelLibraryControls(true);
            EditorGUILayout.Space(8f);
            DrawCharacterDefaultClipPicker();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawCharacterBasicSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Basic Parameters", EditorStyles.boldLabel);

            GameObject previousModel = characterProfile.modelPrefab;
            CombatActionDatabase previousComboTable = characterProfile.comboTable;

            EditorGUI.BeginChangeCheck();
            characterProfile.characterId = EditorGUILayout.TextField("Character Id", characterProfile.characterId);
            characterProfile.displayName = EditorGUILayout.TextField("Display Name", characterProfile.displayName);
            characterProfile.modelPrefab = (GameObject)EditorGUILayout.ObjectField("Model", characterProfile.modelPrefab, typeof(GameObject), false);
            characterProfile.avatar = (Avatar)EditorGUILayout.ObjectField("Avatar", characterProfile.avatar, typeof(Avatar), false);
            characterProfile.idleClip = (AnimationClip)EditorGUILayout.ObjectField("Idle Clip", characterProfile.idleClip, typeof(AnimationClip), false);
            characterProfile.moveClip = (AnimationClip)EditorGUILayout.ObjectField("Move Clip", characterProfile.moveClip, typeof(AnimationClip), false);
            characterProfile.comboTable = (CombatActionDatabase)EditorGUILayout.ObjectField("Combo Table", characterProfile.comboTable, typeof(CombatActionDatabase), false);
            if (EditorGUI.EndChangeCheck())
            {
                bool modelChanged = previousModel != characterProfile.modelPrefab;
                if (modelChanged)
                {
                    AutoAssignAvatarFromProfileModel(true);
                }

                SaveCharacterProfileChanges(modelChanged, previousComboTable != characterProfile.comboTable);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(characterProfile.modelPrefab == null))
            {
                if (GUILayout.Button("Auto Fill Avatar", GUILayout.Height(26f)))
                {
                    AutoAssignAvatarFromProfileModel(true);
                    SaveCharacterProfileChanges(true, false);
                }
            }

            if (GUILayout.Button("Create Combo Table", GUILayout.Height(26f)))
            {
                characterProfile.comboTable = CreateComboTableAsset(characterProfile.displayName);
                SaveCharacterProfileChanges(false, true);
                AssetDatabase.SaveAssets();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("Combo Table and Definition Editor read these values from the selected character. Once they are set here, you do not need to pick the model again while editing moves.", MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private void DrawCharacterRuntimeSummary()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Resolved For Authoring", EditorStyles.boldLabel);
            DrawReadonlyObjectField("Profile", characterProfile, typeof(CharacterActionProfile));
            DrawReadonlyObjectField("Model", characterProfile.modelPrefab, typeof(GameObject));
            DrawReadonlyObjectField("Avatar", characterProfile.avatar, typeof(Avatar));
            DrawReadonlyObjectField("Combo Table", characterProfile.comboTable, typeof(CombatActionDatabase));
            DrawReadonlyObjectField("Idle Clip", characterProfile.idleClip, typeof(AnimationClip));
            DrawReadonlyObjectField("Move Clip", characterProfile.moveClip, typeof(AnimationClip));
            EditorGUILayout.EndVertical();
        }

        private static void DrawReadonlyObjectField(string label, UnityEngine.Object value, Type type)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(label, value, type, false);
            }
        }

        private void DrawCharacterDefaultClipPicker()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Default Animation Picker", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            defaultClipSlot = (CharacterDefaultClipSlot)EditorGUILayout.EnumPopup("Assign Selected Clip To", defaultClipSlot);
            if (EditorGUI.EndChangeCheck())
            {
                SelectCurrentDefaultClipInLibrary();
            }

            DrawAnimationLibraryControls(AnimationLibraryContext.CharacterDefaults);
            EditorGUILayout.EndVertical();
        }

        private void SaveCharacterProfileChanges(bool refreshPreview, bool comboTableChanged)
        {
            if (characterProfile == null)
            {
                return;
            }

            characterProfile.EnsureDefaults();
            EditorUtility.SetDirty(characterProfile);

            if (comboTableChanged && characterActionGraphView != null)
            {
                characterActionGraphView.SetDatabase(characterProfile.comboTable, true);
                characterActionGraphView.Refresh();
            }

            ApplyCharacterProfileToEditor(refreshPreview, false);
        }

        private void AutoAssignAvatarFromProfileModel(bool overwriteExisting)
        {
            if (characterProfile == null || characterProfile.modelPrefab == null)
            {
                return;
            }

            if (!overwriteExisting && characterProfile.avatar != null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(characterProfile.modelPrefab);
            Avatar avatar = LoadAvatarFromModel(path);
            if (avatar != null)
            {
                characterProfile.avatar = avatar;
            }
        }

        private void DrawCharacterActionGraph(bool showFoldout)
        {
            if (showFoldout)
            {
                showCharacterActionGraph = EditorGUILayout.Foldout(showCharacterActionGraph, "Combo Table Editor", true);
                if (!showCharacterActionGraph)
                {
                    return;
                }
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (characterProfile == null)
            {
                EditorGUILayout.HelpBox("Select or create a character before editing combo rules.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (characterProfile.comboTable == null)
            {
                EditorGUILayout.HelpBox("This character does not have a combo table yet.", MessageType.Warning);
                if (GUILayout.Button("Create Combo Table", GUILayout.Height(28f), GUILayout.Width(160f)))
                {
                    characterProfile.comboTable = CreateComboTableAsset(characterProfile.displayName);
                    SaveCharacterProfileChanges(false, true);
                    AssetDatabase.SaveAssets();
                }

                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Combo Graph", EditorStyles.boldLabel, GUILayout.Width(90f));
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(characterProfile.comboTable, typeof(CombatActionDatabase), false);
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(72f)))
            {
                characterProfile.comboTable.RebuildLookup();
                characterActionGraphView?.Refresh();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("Author the combo table first, then open an action node to edit its CombatAnimationDefinition.", MessageType.None);

            if (characterActionGraphView == null)
            {
                characterActionGraphView = new CombatComboGraphView(Repaint, SelectActionForAuthoring, SelectComboActionForPreview);
                characterActionGraphView.Initialize();
            }

            characterActionGraphView.SetDatabase(characterProfile.comboTable, true);
            characterActionGraphView.Draw(true);

            EditorGUILayout.EndVertical();
        }

        private void DrawComboTableHeader(CombatActionDatabase database)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Combo Table", EditorStyles.boldLabel, GUILayout.Width(88f));
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(database, typeof(CombatActionDatabase), false);
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(72f)))
            {
                database.RebuildLookup();
                characterActionGraphView?.Refresh();
            }

            EditorGUILayout.EndHorizontal();

            int actionCount = GetDatabaseActions(database).Count;
            database.EnsureEntryActions();
            EditorGUILayout.LabelField(actionCount + " actions, " + database.entryActions.Count + " entry rules", EditorStyles.miniLabel);
        }

        private void DrawEntryMovesEditor(CombatActionDatabase database)
        {
            database.EnsureEntryActions();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("1. Entry Moves", EditorStyles.boldLabel);

            if (database.entryActions.Count == 0)
            {
                EditorGUILayout.HelpBox("No entry move yet. Add one so the character knows which action starts when you press a button from idle.", MessageType.None);
            }

            int removeIndex = -1;
            for (int i = 0; i < database.entryActions.Count; i++)
            {
                CombatActionEntry entry = database.entryActions[i];
                if (entry == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();

                CombatAnimationDefinition resolvedTarget = ResolveEntryTarget(database, entry);
                EditorGUILayout.BeginHorizontal();
                DrawInputSequence(entry.inputAction, GUILayout.Width(170f));
                GUILayout.Label("starts", GUILayout.Width(42f));
                if (GUILayout.Button(ActionChoiceLabel(resolvedTarget), EditorStyles.miniButton, GUILayout.MinWidth(220f), GUILayout.Height(24f)))
                {
                    SelectActionForAuthoring(resolvedTarget);
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                {
                    removeIndex = i;
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Input", GUILayout.Width(42f));
                entry.inputAction = DrawInputActionCompactPopup(entry.inputAction, GUILayout.Width(180f));
                GUILayout.Label("Action", GUILayout.Width(48f));
                CombatAnimationDefinition target = DrawComboActionCompactPopup(database, entry.targetDefinition, entry.targetActionId, GUILayout.MinWidth(240f));
                if (target != entry.targetDefinition)
                {
                    SetEntryTarget(entry, target);
                }

                entry.serverAuthoritative = GUILayout.Toggle(entry.serverAuthoritative, "Server", GUILayout.Width(68f));
                if (GUILayout.Button("Edit Definition", GUILayout.Width(112f)))
                {
                    SelectActionForAuthoring(ResolveEntryTarget(database, entry));
                }

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(database);
                    RefreshComboGraphView(database);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("New entry", GUILayout.Width(72f));
            DrawInputSequence(comboEditorInputAction, GUILayout.Width(170f));
            comboEditorInputAction = DrawInputActionCompactPopup(comboEditorInputAction, GUILayout.Width(180f));
            using (new EditorGUI.DisabledScope(GetDatabaseActions(database).Count == 0))
            {
                if (GUILayout.Button("Add / Update Entry", GUILayout.Width(150f)))
                {
                    AddOrUpdateEntryMove(database);
                }
            }

            EditorGUILayout.EndHorizontal();

            if (removeIndex >= 0)
            {
                database.entryActions.RemoveAt(removeIndex);
                EditorUtility.SetDirty(database);
                RefreshComboGraphView(database);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawComboBranchesEditor(CombatActionDatabase database)
        {
            List<CombatAnimationDefinition> actions = GetDatabaseActions(database);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("2. Combo Branches", EditorStyles.boldLabel);

            if (actions.Count == 0)
            {
                EditorGUILayout.HelpBox("Create actions from animation clips first. Each action will appear here with its follow-up branches.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("New branch input", GUILayout.Width(110f));
            DrawInputSequence(comboEditorInputAction, GUILayout.Width(170f));
            comboEditorInputAction = DrawInputActionCompactPopup(comboEditorInputAction, GUILayout.Width(180f));
            GUILayout.Label("window", GUILayout.Width(52f));
            comboEditorDefaultStartFrame = Mathf.Max(0, EditorGUILayout.IntField(comboEditorDefaultStartFrame, GUILayout.Width(48f)));
            GUILayout.Label("-", GUILayout.Width(10f));
            comboEditorDefaultEndFrame = Mathf.Max(comboEditorDefaultStartFrame, EditorGUILayout.IntField(comboEditorDefaultEndFrame, GUILayout.Width(48f)));
            GUILayout.Label("frames", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            foreach (CombatAnimationDefinition action in actions)
            {
                DrawActionBranchCard(database, action);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawActionBranchCard(CombatActionDatabase database, CombatAnimationDefinition action)
        {
            if (action == null)
            {
                return;
            }

            action.EnsureActionLinks();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(ActionChoiceLabel(action), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Edit Definition", GUILayout.Width(104f)))
            {
                SelectActionForAuthoring(action);
            }

            if (GUILayout.Button("Ping", GUILayout.Width(48f)))
            {
                Selection.activeObject = action;
                EditorGUIUtility.PingObject(action);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Clip: " + (action.clip == null ? "None" : BuildAnimationDisplayName(action.clip.name)), EditorStyles.miniLabel);

            if (action.actionLinks.Count == 0)
            {
                EditorGUILayout.HelpBox("No follow-up yet. Add a branch if this action can cancel or combo into another action.", MessageType.None);
            }

            int removeIndex = -1;
            int frameCount = GetActionFrameCount(action);
            for (int i = 0; i < action.actionLinks.Count; i++)
            {
                CombatActionLink link = action.actionLinks[i];
                if (link == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();

                CombatAnimationDefinition resolvedTarget = ResolveLinkTarget(database, link);
                EditorGUILayout.BeginHorizontal();
                DrawInputSequence(link.inputAction, GUILayout.Width(170f));
                GUILayout.Label("during " + link.startFrame + "-" + Mathf.Max(link.endFrame, link.startFrame) + "f", GUILayout.Width(104f));
                GUILayout.Label("goes to", GUILayout.Width(56f));
                if (GUILayout.Button(ActionChoiceLabel(resolvedTarget), EditorStyles.miniButton, GUILayout.MinWidth(220f), GUILayout.Height(24f)))
                {
                    SelectActionForAuthoring(resolvedTarget);
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                {
                    removeIndex = i;
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Input", GUILayout.Width(42f));
                link.inputAction = DrawInputActionCompactPopup(link.inputAction, GUILayout.Width(180f));
                GUILayout.Label("Frames", GUILayout.Width(48f));
                link.startFrame = Mathf.Clamp(EditorGUILayout.IntField(link.startFrame, GUILayout.Width(48f)), 0, Mathf.Max(0, frameCount));
                GUILayout.Label("-", GUILayout.Width(10f));
                link.endFrame = Mathf.Clamp(EditorGUILayout.IntField(Mathf.Max(link.endFrame, link.startFrame), GUILayout.Width(48f)), link.startFrame, Mathf.Max(link.startFrame, frameCount));
                GUILayout.Label("Target", GUILayout.Width(44f));
                CombatAnimationDefinition target = DrawComboActionCompactPopup(database, link.targetDefinition, link.targetActionId, GUILayout.MinWidth(240f));
                if (target != link.targetDefinition)
                {
                    SetLinkTarget(link, target);
                }

                if (GUILayout.Button("Edit Definition", GUILayout.Width(112f)))
                {
                    SelectActionForAuthoring(ResolveLinkTarget(database, link));
                }

                if (EditorGUI.EndChangeCheck())
                {
                    if (string.IsNullOrWhiteSpace(link.triggerTag))
                    {
                        link.triggerTag = "combat.combo.branch";
                    }

                    EditorUtility.SetDirty(action);
                    RefreshComboGraphView(database);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(GetFirstOtherAction(database, action) == null))
            {
                if (GUILayout.Button("Add Branch To Next Action", GUILayout.Width(180f)))
                {
                    AddBranchToNextAction(database, action);
                }
            }

            GUILayout.Label("Uses the input/window above; target can be changed on the new row.", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (removeIndex >= 0)
            {
                action.actionLinks.RemoveAt(removeIndex);
                EditorUtility.SetDirty(action);
                RefreshComboGraphView(database);
            }

            EditorGUILayout.EndVertical();
        }

        private List<CombatAnimationDefinition> GetDatabaseActions(CombatActionDatabase database)
        {
            List<CombatAnimationDefinition> actions = new List<CombatAnimationDefinition>();
            if (database == null)
            {
                return actions;
            }

            if (database.actions == null)
            {
                database.actions = new List<CombatAnimationDefinition>();
            }

            foreach (CombatAnimationDefinition action in database.actions)
            {
                if (action != null)
                {
                    actions.Add(action);
                }
            }

            return actions;
        }

        private CombatAnimationDefinition DrawComboActionCompactPopup(CombatActionDatabase database, CombatAnimationDefinition current, string targetActionId, params GUILayoutOption[] options)
        {
            List<CombatAnimationDefinition> actions = GetDatabaseActions(database);
            string[] labels = new string[actions.Count + 1];
            labels[0] = "None";

            int selectedIndex = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                CombatAnimationDefinition action = actions[i];
                labels[i + 1] = ActionChoiceLabel(action);
                if (action == current || current == null && string.Equals(action.actionId, targetActionId, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i + 1;
                }
            }

            int nextIndex = EditorGUILayout.Popup(selectedIndex, labels, options);
            return nextIndex <= 0 || nextIndex > actions.Count ? null : actions[nextIndex - 1];
        }

        private static string DrawInputActionCompactPopup(string inputAction, params GUILayoutOption[] options)
        {
            string normalized = CombatInputActionNames.Normalize(inputAction);
            int selectedIndex = Array.IndexOf(CombatInputActionNames.AuthoringNames, normalized);
            string[] labels = BuildInputActionPopupLabels(inputAction, selectedIndex);
            int nextIndex = EditorGUILayout.Popup(selectedIndex >= 0 ? selectedIndex : labels.Length - 1, labels, options);
            if (nextIndex >= 0 && nextIndex < CombatInputActionNames.AuthoringNames.Length)
            {
                return CombatInputActionNames.AuthoringNames[nextIndex];
            }

            return inputAction;
        }

        private static void DrawInputSequence(string inputAction, params GUILayoutOption[] options)
        {
            EditorGUILayout.BeginHorizontal(options);
            CombatInputActionNames.TryDescribeSequence(inputAction, out string stickToken, out string buttonToken, out string actionToken);

            if (!string.IsNullOrWhiteSpace(stickToken))
            {
                DrawInputChip(stickToken, new Color(0.24f, 0.42f, 0.62f, 1f), 48f);
                GUILayout.Label("+", EditorStyles.miniBoldLabel, GUILayout.Width(12f));
            }

            DrawInputChip(buttonToken, ButtonColor(buttonToken), 34f);
            GUILayout.Space(4f);
            GUILayout.Label(actionToken, EditorStyles.miniBoldLabel, GUILayout.Width(54f));
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawInputChip(string text, Color color, float width)
        {
            Color previousColor = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUILayout.Box(text, EditorStyles.miniButton, GUILayout.Width(width), GUILayout.Height(22f));
            GUI.backgroundColor = previousColor;
        }

        private static Color ButtonColor(string buttonToken)
        {
            switch (buttonToken)
            {
                case "□":
                    return new Color(0.32f, 0.55f, 0.95f, 1f);
                case "△":
                    return new Color(0.34f, 0.70f, 0.48f, 1f);
                case "○":
                    return new Color(0.90f, 0.38f, 0.38f, 1f);
                case "×":
                    return new Color(0.60f, 0.48f, 0.95f, 1f);
                default:
                    return new Color(0.42f, 0.42f, 0.42f, 1f);
            }
        }

        private static string ActionChoiceLabel(CombatAnimationDefinition action)
        {
            if (action == null)
            {
                return "None";
            }

            return ShortenMiddle(action.DisplayName, 54);
        }

        private CombatAnimationDefinition ResolveEntryTarget(CombatActionDatabase database, CombatActionEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            if (entry.targetDefinition != null)
            {
                return entry.targetDefinition;
            }

            return FindActionById(database, entry.targetActionId);
        }

        private CombatAnimationDefinition ResolveLinkTarget(CombatActionDatabase database, CombatActionLink link)
        {
            if (link == null)
            {
                return null;
            }

            if (link.targetDefinition != null)
            {
                return link.targetDefinition;
            }

            return FindActionById(database, link.targetActionId);
        }

        private CombatAnimationDefinition FindActionById(CombatActionDatabase database, string actionId)
        {
            if (database == null || string.IsNullOrWhiteSpace(actionId))
            {
                return null;
            }

            foreach (CombatAnimationDefinition action in GetDatabaseActions(database))
            {
                if (action != null && string.Equals(action.actionId, actionId, StringComparison.OrdinalIgnoreCase))
                {
                    return action;
                }
            }

            return null;
        }

        private void SetEntryTarget(CombatActionEntry entry, CombatAnimationDefinition target)
        {
            if (entry == null)
            {
                return;
            }

            entry.targetDefinition = target;
            entry.targetActionId = target == null ? string.Empty : target.actionId;
        }

        private void SetLinkTarget(CombatActionLink link, CombatAnimationDefinition target)
        {
            if (link == null)
            {
                return;
            }

            link.targetDefinition = target;
            link.targetActionId = target == null ? string.Empty : target.actionId;
        }

        private void AddOrUpdateEntryMove(CombatActionDatabase database)
        {
            if (database == null)
            {
                return;
            }

            database.EnsureEntryActions();
            CombatAnimationDefinition target = definition != null && GetDatabaseActions(database).Contains(definition)
                ? definition
                : database.FirstAction();
            if (target == null)
            {
                return;
            }

            CombatActionEntry entry = null;
            foreach (CombatActionEntry candidate in database.entryActions)
            {
                if (candidate != null && CombatInputActionNames.ExactMatches(candidate.inputAction, comboEditorInputAction))
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

            entry.inputAction = CombatInputActionNames.Normalize(comboEditorInputAction);
            SetEntryTarget(entry, target);
            entry.serverAuthoritative = true;
            EditorUtility.SetDirty(database);
            RefreshComboGraphView(database);
        }

        private void AddBranchToNextAction(CombatActionDatabase database, CombatAnimationDefinition source)
        {
            if (database == null || source == null)
            {
                return;
            }

            CombatAnimationDefinition target = GetFirstOtherAction(database, source);
            if (target == null)
            {
                return;
            }

            source.EnsureActionLinks();
            CombatActionLink link = new CombatActionLink
            {
                inputAction = CombatInputActionNames.Normalize(comboEditorInputAction),
                triggerTag = "combat.combo.branch",
                startFrame = Mathf.Max(0, comboEditorDefaultStartFrame),
                endFrame = Mathf.Max(comboEditorDefaultStartFrame, comboEditorDefaultEndFrame),
                serverAuthoritative = true
            };
            SetLinkTarget(link, target);
            source.actionLinks.Add(link);
            EditorUtility.SetDirty(source);
            RefreshComboGraphView(database);
        }

        private CombatAnimationDefinition GetFirstOtherAction(CombatActionDatabase database, CombatAnimationDefinition source)
        {
            foreach (CombatAnimationDefinition action in GetDatabaseActions(database))
            {
                if (action != null && action != source)
                {
                    return action;
                }
            }

            return null;
        }

        private static int GetActionFrameCount(CombatAnimationDefinition action)
        {
            if (action == null || action.clip == null)
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.RoundToInt(action.clip.length * Mathf.Max(1, action.authoringFrameRate)));
        }

        private void SelectActionForAuthoring(CombatAnimationDefinition action)
        {
            if (action == null)
            {
                return;
            }

            SelectSingleActionForPreview(action);
            currentPage = EditorPage.DefinitionEditor;
            EditorPrefs.SetInt(EditorPagePrefsKey, (int)currentPage);
            mainScroll = Vector2.zero;
        }

        private void SelectSingleActionForPreview(CombatAnimationDefinition action)
        {
            if (action == null)
            {
                return;
            }

            ClearComboPreview();
            definition = action;
            clip = action.clip;
            selectedMarkerIndex = -1;
            normalizedTime = 0f;
            int animationIndex = FindAnimationCandidateIndex(clip);
            if (animationIndex >= 0)
            {
                selectedAnimationIndex = animationIndex;
            }

            EnsureSelectedPreviewModelInstance();
            if (previewRoot == null)
            {
                keepSelectedActionOnScheduledPreviewRefresh = true;
                SchedulePreviewSceneRefresh(true);
            }
            else
            {
                SampleClip();
            }

            Repaint();
        }

        private void SelectComboActionForPreview(CombatAnimationDefinition action)
        {
            if (action == null)
            {
                return;
            }

            if (!BuildComboPreview(action))
            {
                SelectSingleActionForPreview(action);
                return;
            }

            comboPreviewTargetDefinition = action;
            comboPreviewTime = 0f;
            selectedMarkerIndex = -1;
            SetComboPreviewTime(0f, false);
            isPlaying = true;
            lastUpdateTime = EditorApplication.timeSinceStartup;

            EnsureSelectedPreviewModelInstance();
            isPlaying = true;
            lastUpdateTime = EditorApplication.timeSinceStartup;
            if (previewRoot == null)
            {
                keepSelectedActionOnScheduledPreviewRefresh = true;
                SchedulePreviewSceneRefresh(true);
            }
            else
            {
                if (comboPreviewActive)
                {
                    isPlaying = true;
                    lastUpdateTime = EditorApplication.timeSinceStartup;
                }

                SampleClip();
            }

            Repaint();
        }

        private bool BuildComboPreview(CombatAnimationDefinition target)
        {
            comboPreviewSteps.Clear();
            comboPreviewPathLabel = string.Empty;
            comboPreviewTargetDefinition = null;
            comboPreviewActive = false;

            CombatActionDatabase database = characterProfile == null ? null : characterProfile.comboTable;
            if (database == null || database.entryActions == null || target == null)
            {
                return false;
            }

            database.EnsureEntryActions();
            database.RebuildLookup();

            List<ComboPreviewStep> workingSteps = new List<ComboPreviewStep>();
            HashSet<CombatAnimationDefinition> visited = new HashSet<CombatAnimationDefinition>();
            foreach (CombatActionEntry entry in database.entryActions)
            {
                CombatAnimationDefinition entryTarget = ResolveEntryTarget(database, entry);
                if (entryTarget == null)
                {
                    continue;
                }

                workingSteps.Clear();
                visited.Clear();
                workingSteps.Add(new ComboPreviewStep(entryTarget, entry == null ? string.Empty : entry.inputAction));
                visited.Add(entryTarget);

                if (!TryBuildComboPreviewPath(database, entryTarget, target, workingSteps, visited, 0))
                {
                    continue;
                }

                for (int i = 0; i < workingSteps.Count; i++)
                {
                    comboPreviewSteps.Add(workingSteps[i].Clone());
                }

                comboPreviewPathLabel = BuildComboPreviewPathLabel(comboPreviewSteps);
                comboPreviewActive = comboPreviewSteps.Count > 0;
                return comboPreviewActive;
            }

            return false;
        }

        private bool TryBuildComboPreviewPath(
            CombatActionDatabase database,
            CombatAnimationDefinition current,
            CombatAnimationDefinition target,
            List<ComboPreviewStep> steps,
            HashSet<CombatAnimationDefinition> visited,
            int depth)
        {
            if (current == null || target == null || steps.Count == 0)
            {
                return false;
            }

            if (current == target)
            {
                return true;
            }

            if (depth > 32)
            {
                return false;
            }

            current.EnsureActionLinks();
            foreach (CombatActionLink link in current.actionLinks)
            {
                CombatAnimationDefinition next = ResolveLinkTarget(database, link);
                if (next == null || visited.Contains(next))
                {
                    continue;
                }

                ComboPreviewStep sourceStep = steps[steps.Count - 1];
                CombatActionLink previousOutgoingLink = sourceStep.outgoingLink;
                sourceStep.outgoingLink = link;
                steps.Add(new ComboPreviewStep(next, string.Empty));
                visited.Add(next);

                if (TryBuildComboPreviewPath(database, next, target, steps, visited, depth + 1))
                {
                    return true;
                }

                visited.Remove(next);
                steps.RemoveAt(steps.Count - 1);
                sourceStep.outgoingLink = previousOutgoingLink;
            }

            return false;
        }

        private static string BuildComboPreviewPathLabel(List<ComboPreviewStep> steps)
        {
            if (steps == null || steps.Count == 0)
            {
                return string.Empty;
            }

            string label = "Entry";
            if (!string.IsNullOrWhiteSpace(steps[0].entryInputAction))
            {
                label += " " + CombatInputActionNames.DisplayLabel(steps[0].entryInputAction);
            }

            for (int i = 0; i < steps.Count; i++)
            {
                ComboPreviewStep step = steps[i];
                label += " -> " + (step.action == null ? "Missing" : step.action.DisplayName);
                if (step.outgoingLink != null && i < steps.Count - 1)
                {
                    label += " @" + step.outgoingLink.startFrame + "f";
                }
            }

            return label;
        }

        private void ClearComboPreview()
        {
            comboPreviewActive = false;
            comboPreviewSteps.Clear();
            comboPreviewTargetDefinition = null;
            comboPreviewTime = 0f;
            comboPreviewPathLabel = string.Empty;
        }

        private float GetComboPreviewTotalDuration()
        {
            if (!comboPreviewActive || comboPreviewSteps.Count == 0)
            {
                return 0f;
            }

            float total = 0f;
            for (int i = 0; i < comboPreviewSteps.Count; i++)
            {
                total += GetComboPreviewStepDuration(i);
            }

            return total;
        }

        private float GetComboPreviewStepDuration(int index)
        {
            if (index < 0 || index >= comboPreviewSteps.Count)
            {
                return 0f;
            }

            ComboPreviewStep step = comboPreviewSteps[index];
            AnimationClip stepClip = step.action == null ? null : step.action.clip;
            if (stepClip == null)
            {
                return 0f;
            }

            if (step.outgoingLink == null)
            {
                return Mathf.Max(0f, stepClip.length);
            }

            int frameRate = Mathf.Max(1, step.action == null ? 60 : step.action.authoringFrameRate);
            float transitionTime = step.outgoingLink.startFrame / (float)frameRate;
            return Mathf.Clamp(transitionTime, 0f, Mathf.Max(0f, stepClip.length));
        }

        private ComboPreviewStep GetCurrentComboPreviewStep()
        {
            if (!comboPreviewActive || comboPreviewSteps.Count == 0)
            {
                return null;
            }

            float cursor = 0f;
            for (int i = 0; i < comboPreviewSteps.Count; i++)
            {
                float duration = GetComboPreviewStepDuration(i);
                if (comboPreviewTime <= cursor + duration || i == comboPreviewSteps.Count - 1)
                {
                    return comboPreviewSteps[i];
                }

                cursor += duration;
            }

            return comboPreviewSteps[comboPreviewSteps.Count - 1];
        }

        private void SetComboPreviewTime(float time, bool sample)
        {
            if (!comboPreviewActive || comboPreviewSteps.Count == 0)
            {
                return;
            }

            float totalDuration = GetComboPreviewTotalDuration();
            comboPreviewTime = Mathf.Clamp(time, 0f, Mathf.Max(0f, totalDuration));

            float cursor = 0f;
            for (int i = 0; i < comboPreviewSteps.Count; i++)
            {
                ComboPreviewStep step = comboPreviewSteps[i];
                float duration = GetComboPreviewStepDuration(i);
                if (comboPreviewTime > cursor + duration && i < comboPreviewSteps.Count - 1)
                {
                    cursor += duration;
                    continue;
                }

                definition = step.action;
                clip = definition == null ? null : definition.clip;
                float localTime = Mathf.Clamp(comboPreviewTime - cursor, 0f, clip == null ? 0f : clip.length);
                normalizedTime = clip == null || clip.length <= 0f ? 0f : Mathf.Clamp01(localTime / clip.length);
                int animationIndex = FindAnimationCandidateIndex(clip);
                if (animationIndex >= 0)
                {
                    selectedAnimationIndex = animationIndex;
                }

                if (sample)
                {
                    SampleCurrentClip();
                }

                return;
            }
        }

        private void RefreshComboGraphView(CombatActionDatabase database)
        {
            database?.RebuildLookup();
            if (characterActionGraphView != null)
            {
                characterActionGraphView.SetDatabase(database, true);
                characterActionGraphView.Refresh();
            }
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
            showAssetDrawer = EditorGUILayout.Foldout(showAssetDrawer, "Action Clip and Definition", true);
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

            DrawCurrentDefinitionClipField();

            EditorGUI.BeginChangeCheck();
            definition.stateName = EditorGUILayout.TextField("Name", definition.stateName);
            definition.authoringFrameRate = Mathf.Max(1, EditorGUILayout.IntField("Authoring FPS", definition.authoringFrameRate));
            definition.requiresNetworkSync = EditorGUILayout.Toggle("Require Net Sync", definition.requiresNetworkSync);
            definition.loopPreview = EditorGUILayout.Toggle("Loop Preview", definition.loopPreview);
            definition.rootMotionScale = EditorGUILayout.FloatField("Root Motion Scale", definition.rootMotionScale);
            definition.clip = clip;
            if (EditorGUI.EndChangeCheck())
            {
                definition.EnsureInternalActionId();
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

        private void DrawCurrentDefinitionClipField()
        {
            if (definition == null)
            {
                return;
            }

            if (clip == null && definition.clip != null)
            {
                clip = definition.clip;
            }

            EditorGUI.BeginChangeCheck();
            AnimationClip nextClip = (AnimationClip)EditorGUILayout.ObjectField("Clip", clip, typeof(AnimationClip), false);
            if (EditorGUI.EndChangeCheck())
            {
                AssignClipToCurrentDefinition(nextClip, true);
            }
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
            EditorGUILayout.LabelField("Action Asset Binding", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Preview Scene", SceneManager.GetActiveScene().name == PreviewSceneName ? PreviewSceneName : "Not active");
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Character Model", characterProfile == null ? null : characterProfile.modelPrefab, typeof(GameObject), false);
            }

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
            DrawAnimationLibraryControls(AnimationLibraryContext.ActionAuthoring);
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
            if (characterProfile != null && characterProfile.modelPrefab != null)
            {
                string path = AssetDatabase.GetAssetPath(characterProfile.modelPrefab);
                return string.IsNullOrWhiteSpace(path) ? characterProfile.modelPrefab.name : BuildModelDisplayName(path);
            }

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

            return definition.DisplayName;
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

        private void DrawModelLibraryControls(bool bindToCharacterProfile)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Character Model Library", EditorStyles.boldLabel);

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
                if (bindToCharacterProfile)
                {
                    AssignSelectedModelToCharacterProfile();
                }

                RefreshAnimationLibrary(false);
                SchedulePreviewSceneRefresh(true);
            }
            else
            {
                selectedModelIndex = nextModelIndex;
                EnsureSelectedPreviewModelInstance();
            }

            EditorGUILayout.HelpBox("Selecting a model here writes it to the character profile and refreshes the dedicated preview Scene.", MessageType.None);

            EditorGUILayout.EndVertical();
        }

        private void DrawAnimationLibraryControls(AnimationLibraryContext context)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(context == AnimationLibraryContext.ActionAuthoring ? "Action Animation Library" : "Compatible Animation Library", EditorStyles.boldLabel);

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
                if (context == AnimationLibraryContext.ActionAuthoring)
                {
                    UseSelectedAnimationClip();
                }
                else
                {
                    AssignSelectedAnimationToDefaultClip();
                }
            }
            else
            {
                selectedAnimationIndex = nextAnimationIndex;
            }

            EditorGUILayout.EndVertical();
        }

        private void AssignSelectedModelToCharacterProfile()
        {
            if (characterProfile == null)
            {
                return;
            }

            GameObject selectedModel = LoadSelectedModelAsset();
            if (selectedModel == null)
            {
                return;
            }

            characterProfile.modelPrefab = selectedModel;
            AutoAssignAvatarFromProfileModel(true);
            SaveCharacterProfileChanges(true, false);
        }

        private void SelectCurrentDefaultClipInLibrary()
        {
            AnimationClip target = characterProfile == null
                ? null
                : defaultClipSlot == CharacterDefaultClipSlot.Idle
                    ? characterProfile.idleClip
                    : characterProfile.moveClip;
            int index = FindAnimationCandidateIndex(target);
            if (index >= 0)
            {
                selectedAnimationIndex = index;
            }
        }

        private void AssignSelectedAnimationToDefaultClip()
        {
            if (characterProfile == null)
            {
                return;
            }

            AnimationClip selectedClip = LoadSelectedAnimationClip();
            if (selectedClip == null)
            {
                RefreshAnimationLibrary(false);
                return;
            }

            if (defaultClipSlot == CharacterDefaultClipSlot.Idle)
            {
                characterProfile.idleClip = selectedClip;
            }
            else
            {
                characterProfile.moveClip = selectedClip;
            }

            SaveCharacterProfileChanges(false, false);
        }

        private void DrawPreviewControls()
        {
            if (comboPreviewActive)
            {
                DrawComboPreviewControls();
                return;
            }

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

        private void SelectCharacterProfileObject(UnityEngine.Object selectedObject)
        {
            if (selectedObject == null)
            {
                characterProfile = null;
                SaveCharacterProfilePrefs();
                currentPage = EditorPage.CharacterSetup;
                EditorPrefs.SetInt(EditorPagePrefsKey, (int)currentPage);
                return;
            }

            CharacterActionProfile selectedProfile = ResolveCharacterProfileSelection(selectedObject);
            if (selectedProfile == null)
            {
                ShowNotification(new GUIContent("Select a CharacterActionProfile or a model used by one."));
                return;
            }

            characterProfile = selectedProfile;
            SaveCharacterProfilePrefs();
            ApplyCharacterProfileToEditor(true);
        }

        private static CharacterActionProfile ResolveCharacterProfileSelection(UnityEngine.Object selectedObject)
        {
            if (selectedObject == null)
            {
                return null;
            }

            if (selectedObject is CharacterActionProfile directProfile)
            {
                return directProfile;
            }

            string selectedPath = AssetDatabase.GetAssetPath(selectedObject);
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                CharacterActionProfile profileAtPath = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(selectedPath);
                if (profileAtPath != null)
                {
                    return profileAtPath;
                }
            }

            if (selectedObject is GameObject modelPrefab)
            {
                return FindCharacterProfileForModel(modelPrefab);
            }

            return null;
        }

        private void DrawComboPreviewControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Combo Preview", EditorStyles.boldLabel);

            string targetName = comboPreviewTargetDefinition == null ? "None" : comboPreviewTargetDefinition.DisplayName;
            EditorGUILayout.LabelField("Target", targetName);
            EditorGUILayout.LabelField(new GUIContent("Path", comboPreviewPathLabel), ShortenMiddle(comboPreviewPathLabel, 96));

            float totalDuration = GetComboPreviewTotalDuration();
            EditorGUI.BeginChangeCheck();
            float nextTime = EditorGUILayout.Slider("Combo Time", comboPreviewTime, 0f, Mathf.Max(0.001f, totalDuration));
            if (EditorGUI.EndChangeCheck())
            {
                SetComboPreviewTime(nextTime, true);
            }

            ComboPreviewStep currentStep = GetCurrentComboPreviewStep();
            string currentActionName = currentStep == null || currentStep.action == null ? "None" : currentStep.action.DisplayName;
            float localSeconds = clip == null ? 0f : Mathf.Clamp01(normalizedTime) * clip.length;
            int currentFrame = clip == null || definition == null
                ? 0
                : Mathf.RoundToInt(localSeconds * Mathf.Max(1, definition.authoringFrameRate));
            EditorGUILayout.LabelField("Current Action", currentActionName);
            EditorGUILayout.LabelField("Seconds", comboPreviewTime.ToString("0.000") + " / " + totalDuration.ToString("0.000"));
            EditorGUILayout.LabelField("Action Frame", currentFrame + "f");

            if (currentStep != null && currentStep.outgoingLink != null)
            {
                EditorGUILayout.LabelField(
                    "Next Branch",
                    CombatInputActionNames.DisplayLabel(currentStep.outgoingLink.inputAction)
                        + " @ " + currentStep.outgoingLink.startFrame + "-" + currentStep.outgoingLink.endFrame + "f");
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(isPlaying ? "Pause" : "Play", GUILayout.Height(26f)))
            {
                isPlaying = !isPlaying;
                lastUpdateTime = EditorApplication.timeSinceStartup;
                SampleClip();
            }

            if (GUILayout.Button("Stop", GUILayout.Height(26f)))
            {
                isPlaying = false;
                SetComboPreviewTime(0f, true);
            }

            if (GUILayout.Button("Sample", GUILayout.Height(26f)))
            {
                SampleClip();
            }

            EditorGUILayout.EndHorizontal();

            drawAllMarkers = EditorGUILayout.Toggle("Draw All Markers", drawAllMarkers);
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

            DrawCurrentDefinitionClipField();

            EditorGUI.BeginChangeCheck();
            definition.stateName = EditorGUILayout.TextField("Name", definition.stateName);
            definition.authoringFrameRate = Mathf.Max(1, EditorGUILayout.IntField("Authoring FPS", definition.authoringFrameRate));
            definition.requiresNetworkSync = EditorGUILayout.Toggle("Require Net Sync", definition.requiresNetworkSync);
            definition.loopPreview = EditorGUILayout.Toggle("Loop Preview", definition.loopPreview);
            definition.rootMotionScale = EditorGUILayout.FloatField("Root Motion Scale", definition.rootMotionScale);
            definition.clip = clip;
            if (EditorGUI.EndChangeCheck())
            {
                definition.EnsureInternalActionId();
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
                    link.targetActionId = link.targetDefinition.EnsureInternalActionId();
                }

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
            int nextIndex = EditorGUILayout.Popup(label + " Preset", selectedIndex >= 0 ? selectedIndex : labels.Length - 1, labels);
            string nextValue = string.IsNullOrWhiteSpace(inputAction) ? normalized : inputAction.Trim();
            if (nextIndex >= 0 && nextIndex < CombatInputActionNames.AuthoringNames.Length)
            {
                nextValue = CombatInputActionNames.AuthoringNames[nextIndex];
            }

            nextValue = EditorGUILayout.TextField(label + " Sequence", nextValue);
            return CombatInputActionNames.Normalize(nextValue);
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
            bool keepSelectedAction = keepSelectedActionOnScheduledPreviewRefresh;
            previewSceneRefreshScheduled = false;
            scheduledPreviewSceneCreateNew = false;
            keepSelectedActionOnScheduledPreviewRefresh = false;

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
            if (!keepSelectedAction)
            {
                UseFirstCompatibleAnimationIfNeeded();
            }
            else
            {
                SampleClip();
            }

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
            FramePreviewRoot(wrapper);

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

                    ClearSelectionIfInside(root);
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

                ClearSelectionIfInside(sceneObject);
                DestroyImmediate(sceneObject);
            }
        }

        private static void ClearSelectionIfInside(GameObject root)
        {
            if (root == null || Selection.objects == null || Selection.objects.Length == 0)
            {
                return;
            }

            bool shouldClearSelection = false;
            foreach (UnityEngine.Object selected in Selection.objects)
            {
                if (selected == null)
                {
                    shouldClearSelection = true;
                    break;
                }

                if (!SelectionObjectIsInside(selected, root))
                {
                    continue;
                }

                shouldClearSelection = true;
                break;
            }

            if (shouldClearSelection)
            {
                Selection.activeObject = null;
                Selection.objects = Array.Empty<UnityEngine.Object>();
                ActiveEditorTracker.sharedTracker.ForceRebuild();
            }
        }

        private static void FramePreviewRoot(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return;
            }

            sceneView.Frame(CalculatePreviewBounds(root), false);
        }

        private static Bounds CalculatePreviewBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position + Vector3.up, Vector3.one * 2f);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            if (bounds.size.sqrMagnitude <= 0.0001f)
            {
                bounds.size = Vector3.one * 2f;
            }

            return bounds;
        }

        private static bool SelectionObjectIsInside(UnityEngine.Object selected, GameObject root)
        {
            if (root == null || selected == null)
            {
                return false;
            }

            Transform selectedTransform = null;
            if (selected is GameObject selectedGameObject)
            {
                selectedTransform = selectedGameObject.transform;
            }
            else if (selected is Component selectedComponent)
            {
                selectedTransform = selectedComponent.transform;
            }

            return selectedTransform != null && selectedTransform.IsChildOf(root.transform);
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

            AssignClipToCurrentDefinition(selectedClip, true);
        }

        private void AssignClipToCurrentDefinition(AnimationClip nextClip, bool resetTime)
        {
            clip = nextClip;
            if (resetTime)
            {
                normalizedTime = 0f;
            }

            ClearComboPreview();

            if (definition != null)
            {
                definition.clip = clip;
                EditorUtility.SetDirty(definition);
            }

            int animationIndex = FindAnimationCandidateIndex(clip);
            if (animationIndex >= 0)
            {
                selectedAnimationIndex = animationIndex;
            }
            else if (clip == null)
            {
                selectedAnimationIndex = -1;
            }

            SampleClip();
            Repaint();
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
            if (!isPlaying)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float delta = (float)(now - lastUpdateTime);
            lastUpdateTime = now;

            if (comboPreviewActive)
            {
                float totalDuration = GetComboPreviewTotalDuration();
                comboPreviewTime += delta;
                bool comboShouldLoop = comboPreviewTargetDefinition != null && comboPreviewTargetDefinition.loopPreview;
                if (comboPreviewTime > totalDuration)
                {
                    comboPreviewTime = comboShouldLoop && totalDuration > 0f ? comboPreviewTime % totalDuration : totalDuration;
                    isPlaying = comboShouldLoop;
                }

                SetComboPreviewTime(comboPreviewTime, true);
                if (now >= nextEditorRepaintTime)
                {
                    nextEditorRepaintTime = now + EditorRepaintInterval;
                    Repaint();
                }

                return;
            }

            if (clip == null)
            {
                return;
            }

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
            if (comboPreviewActive)
            {
                SetComboPreviewTime(comboPreviewTime, true);
                return;
            }

            SampleCurrentClip();
        }

        private void SampleCurrentClip()
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

        private sealed class ComboPreviewStep
        {
            public readonly CombatAnimationDefinition action;
            public readonly string entryInputAction;
            public CombatActionLink outgoingLink;

            public ComboPreviewStep(CombatAnimationDefinition action, string entryInputAction)
            {
                this.action = action;
                this.entryInputAction = entryInputAction;
            }

            public ComboPreviewStep Clone()
            {
                return new ComboPreviewStep(action, entryInputAction)
                {
                    outgoingLink = outgoingLink
                };
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

            if (string.IsNullOrWhiteSpace(definition.stateName))
            {
                issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "State name is empty. Give this action a readable name."));
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

                if (link.targetDefinition == null && string.IsNullOrWhiteSpace(link.targetActionId))
                {
                    issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "Action link " + (i + 1) + " has no target definition."));
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
            Dictionary<string, int> stateNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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

                if (!string.IsNullOrWhiteSpace(definition.stateName))
                {
                    stateNameCounts.TryGetValue(definition.stateName, out int count);
                    stateNameCounts[definition.stateName] = count + 1;
                }

                rows.Add(new ValidationRow(definition, path, CombatAnimationValidation.ValidateDefinition(definition)));
            }

            foreach (ValidationRow row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.definition.stateName)
                    && stateNameCounts.TryGetValue(row.definition.stateName, out int count)
                    && count > 1)
                {
                    row.issues.Add(new CombatAnimationValidationIssue(MessageType.Warning, "Duplicate state name: " + row.definition.stateName));
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
            EditorGUILayout.LabelField(row.definition.DisplayName + "  (" + row.definition.name + ")", EditorStyles.boldLabel);
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
        private const float CanvasWidth = 4800f;
        private const float CanvasHeight = 3200f;
        private const float NodeWidth = 190f;
        private const float NodeHeight = 98f;
        private const float AutoLayoutEntryX = 40f;
        private const float AutoLayoutFirstActionX = 300f;
        private const float AutoLayoutColumnSpacing = 260f;
        private const float AutoLayoutTop = 84f;
        private const float AutoLayoutLaneSpacing = 145f;
        private const float NodeContentX = 16f;
        private const float NodeContentWidth = NodeWidth - NodeContentX * 2f;
        private const float ConnectorWidth = 10f;
        private const float ConnectorHeight = 40f;
        private const float ConnectorInset = 2f;
        private static readonly Color InputPortColor = new Color(0.36f, 0.62f, 0.88f, 1f);
        private static readonly Color OutputPortColor = new Color(0.72f, 0.74f, 0.76f, 1f);
        private static readonly Color EntryPortColor = new Color(0.86f, 0.72f, 0.32f, 1f);

        private readonly List<GraphNode> nodes = new List<GraphNode>();
        private readonly Dictionary<CombatAnimationDefinition, GraphNode> nodeLookup = new Dictionary<CombatAnimationDefinition, GraphNode>();
        private readonly Dictionary<string, GraphNode> idLookup = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        private Vector2 graphScroll;
        private Vector2 graphViewportSize;
        private Vector2 inspectorScroll;
        private Rect entryNodeRect = new Rect(24f, 80f, NodeWidth, NodeHeight);
        private CombatActionDatabase database;
        private string selectedInputAction = CombatInputActionNames.LightAttack;
        private int defaultStartFrame = 12;
        private int defaultEndFrame = 24;
        private bool draggingLink;
        private bool draggingFromEntry;
        private CombatAnimationDefinition dragSourceDefinition;
        private string dragInputAction = CombatInputActionNames.LightAttack;
        private CombatActionEntry dragExistingEntry;
        private CombatActionLink dragExistingLink;
        private Vector2 dragMousePosition;
        private CombatAnimationDefinition selectedDefinition;
        private CombatAnimationDefinition selectedLinkSource;
        private CombatActionLink selectedLink;
        private CombatActionEntry selectedEntry;
        private GraphNode draggedNode;
        private bool draggingEntryNode;
        private bool draggingCanvas;
        private Vector2 nodeDragOffset;
        private readonly Action repaint;
        private readonly Action<CombatAnimationDefinition> openAction;
        private readonly Action<CombatAnimationDefinition> selectAction;

        public CombatComboGraphView(Action repaint, Action<CombatAnimationDefinition> openAction = null, Action<CombatAnimationDefinition> selectAction = null)
        {
            this.repaint = repaint;
            this.openAction = openAction;
            this.selectAction = selectAction;
        }

        public CombatActionDatabase Database => database;

        public CombatAnimationDefinition SelectedDefinition => selectedDefinition;

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

            using (new EditorGUI.DisabledScope(database == null || nodes.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Auto Layout", "Arrange the combo graph from entry moves and save the node positions."), EditorStyles.toolbarButton, GUILayout.Width(88f)))
                {
                    ApplyAutoLayoutToAllNodes();
                    RequestRepaint();
                }
            }

            GUILayout.Space(10f);
            GUILayout.Label("New Link Input", GUILayout.Width(92f));
            selectedInputAction = DrawInputActionComposer(selectedInputAction, true);
            DrawInputSequencePill(selectedInputAction, string.Empty, false);
            GUILayout.Label("Window", GUILayout.Width(48f));
            defaultStartFrame = Mathf.Max(0, EditorGUILayout.IntField(defaultStartFrame, GUILayout.Width(42f)));
            GUILayout.Label("-", GUILayout.Width(10f));
            defaultEndFrame = Mathf.Max(defaultStartFrame, EditorGUILayout.IntField(defaultEndFrame, GUILayout.Width(42f)));

            GUILayout.FlexibleSpace();
            GUILayout.Label("Drag output to create; drag connected input to retarget.", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawGraphPanel(bool embedded)
        {
            if (embedded)
            {
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.Height(560f));
            }
            else
            {
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }

            try
            {
                Rect outerRect = embedded
                    ? GUILayoutUtility.GetRect(200f, 10000f, 480f, 500f, GUILayout.ExpandWidth(true), GUILayout.Height(500f))
                    : GUILayoutUtility.GetRect(200f, 10000f, 200f, 10000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                Rect canvasRect = new Rect(0f, 0f, CanvasWidth, CanvasHeight);
                graphViewportSize = outerRect.size;

                graphScroll = GUI.BeginScrollView(outerRect, graphScroll, canvasRect);
                try
                {
                    DrawGrid(canvasRect, 24f, new Color(0f, 0f, 0f, 0.18f));
                    DrawGrid(canvasRect, 120f, new Color(0f, 0f, 0f, 0.28f));
                    TryHandlePortMouseDown(Event.current);
                    DrawConnections();

                    if (draggingLink)
                    {
                        DrawDragConnection(Event.current.mousePosition);
                    }

                    if (database != null)
                    {
                        DrawEntryNode();
                    }

                    for (int i = 0; i < nodes.Count; i++)
                    {
                        GraphNode node = nodes[i];
                        DrawActionNode(node);
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
                selectedEntry.targetActionId = selectedEntry.targetDefinition.EnsureInternalActionId();
            }

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

            EditorGUILayout.LabelField("Source", NodeDisplayName(selectedLinkSource), EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            selectedLink.inputAction = DrawInputActionPopup("Input", selectedLink.inputAction);
            selectedLink.triggerTag = EditorGUILayout.TextField("Trigger Tag", selectedLink.triggerTag);
            selectedLink.targetDefinition = (CombatAnimationDefinition)EditorGUILayout.ObjectField("Target", selectedLink.targetDefinition, typeof(CombatAnimationDefinition), false);
            if (selectedLink.targetDefinition != null)
            {
                selectedLink.targetActionId = selectedLink.targetDefinition.EnsureInternalActionId();
            }

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
            inspectedDefinition.stateName = EditorGUILayout.TextField("Name", inspectedDefinition.stateName);
            inspectedDefinition.clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", inspectedDefinition.clip, typeof(AnimationClip), false);
            inspectedDefinition.authoringFrameRate = Mathf.Max(1, EditorGUILayout.IntField("Frame Rate", inspectedDefinition.authoringFrameRate));
            if (EditorGUI.EndChangeCheck())
            {
                inspectedDefinition.EnsureInternalActionId();
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

                string target = link.targetDefinition != null ? NodeDisplayName(link.targetDefinition) : link.targetActionId;
                if (GUILayout.Button(ShortLabel(link.inputAction) + " -> " + target + "  " + link.startFrame + "-" + link.endFrame + "f"))
                {
                    SelectLink(inspectedDefinition, link);
                    return;
                }
            }

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Edit Definition", GUILayout.Height(26f)))
            {
                OpenAction(inspectedDefinition);
            }

            if (GUILayout.Button("Select Asset", GUILayout.Height(26f)))
            {
                Selection.activeObject = inspectedDefinition;
                EditorGUIUtility.PingObject(inspectedDefinition);
            }
        }

        private void DrawEntryNode()
        {
            DrawNodeFrame(entryNodeRect, EntryPortColor, selectedEntry != null);
            HandleEntryNodeSelection(Event.current);

            if (database == null)
            {
                GUI.Label(NodeLocalRect(entryNodeRect, 30f, 18f), "No database", EditorStyles.miniLabel);
            }
            else
            {
                database.EnsureEntryActions();
                GUI.Label(NodeLocalRect(entryNodeRect, 20f, 20f), "Entry", EditorStyles.boldLabel);
                GUI.Label(NodeLocalRect(entryNodeRect, 43f, 16f), ShortLabel(database.name), EditorStyles.miniLabel);
                if (GUI.Button(NodeLocalRect(entryNodeRect, 68f, 22f), "Select Table"))
                {
                    Selection.activeObject = database;
                }
            }
        }

        private void DrawActionNode(GraphNode node)
        {
            CombatAnimationDefinition action = node.definition;
            if (action == null)
            {
                return;
            }

            DrawNodeFrame(node.rect, InputPortColor, selectedDefinition == action);
            HandleActionNodeSelection(node, Event.current);

            action.EnsureActionLinks();
            EditorGUI.BeginChangeCheck();
            string nextName = GUI.TextField(NodeLocalRect(node.rect, 20f, 22f), NodeDisplayName(action));
            if (EditorGUI.EndChangeCheck())
            {
                action.stateName = nextName;
                EditorUtility.SetDirty(action);
            }

            GUI.Label(NodeLocalRect(node.rect, 45f, 16f), ShortLabel(action.name), EditorStyles.miniLabel);

            if (GUI.Button(NodeLocalRect(node.rect, 68f, 22f), "Edit Definition"))
            {
                OpenAction(action);
            }
        }

        private void DrawConnections()
        {
            Dictionary<GraphNode, int> incomingLabelLanes = new Dictionary<GraphNode, int>();
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

                    Vector2 start = GetEntryOutputPort().center;
                    Vector2 end = GetInputPort(targetNode.rect).center;
                    int labelLane = NextIncomingLabelLane(incomingLabelLanes, targetNode);
                    DrawConnection(start, end, InputColor(entry.inputAction), selectedEntry == entry);
                    DrawConnectionNode(start, end, entryNodeRect, targetNode.rect, entry.inputAction, "start", labelLane, selectedEntry == entry, () =>
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
                    int labelLane = NextIncomingLabelLane(incomingLabelLanes, targetNode);
                    DrawConnection(start, end, InputColor(link.inputAction), selectedLink == link);
                    DrawConnectionNode(start, end, sourceNode.rect, targetNode.rect, link.inputAction, link.startFrame + "-" + link.endFrame + "f", labelLane, selectedLink == link, () => SelectLink(source, link));
                }
            }
        }

        private static int NextIncomingLabelLane(Dictionary<GraphNode, int> incomingLabelLanes, GraphNode targetNode)
        {
            if (targetNode == null)
            {
                return 0;
            }

            incomingLabelLanes.TryGetValue(targetNode, out int lane);
            incomingLabelLanes[targetNode] = lane + 1;
            return lane;
        }

        private void DrawDragConnection(Vector2 mousePosition)
        {
            Vector2 start = draggingFromEntry
                ? GetEntryOutputPort().center
                : dragSourceDefinition != null && nodeLookup.TryGetValue(dragSourceDefinition, out GraphNode sourceNode)
                    ? GetOutputPort(sourceNode.rect).center
                    : mousePosition;

            DrawConnection(start, mousePosition, InputColor(dragInputAction), true);
        }

        private void DrawPorts()
        {
            if (database != null)
            {
                DrawPort(GetEntryOutputPort(), EntryPortColor);
            }

            foreach (GraphNode node in nodes)
            {
                DrawPort(GetInputPort(node.rect), InputPortColor);
                DrawPort(GetOutputPort(node.rect), OutputPortColor);
            }
        }

        private bool TryHandlePortMouseDown(Event evt)
        {
            if (evt.type != EventType.MouseDown || evt.button != 0)
            {
                return false;
            }

            if (database != null && IsInOutputHotZone(entryNodeRect, evt.mousePosition))
            {
                BeginDragLink(true, null, evt);
                return true;
            }

            foreach (GraphNode node in nodes)
            {
                if (IsInOutputHotZone(node.rect, evt.mousePosition))
                {
                    BeginDragLink(false, node.definition, evt);
                    return true;
                }
            }

            foreach (GraphNode node in nodes)
            {
                if (TryBeginRetargetFromInput(node, evt))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryBeginRetargetFromInput(GraphNode targetNode, Event evt)
        {
            if (targetNode == null || targetNode.definition == null || !IsInInputHotZone(targetNode.rect, evt.mousePosition))
            {
                return false;
            }

            if (selectedEntry != null && EntryTargets(selectedEntry, targetNode.definition))
            {
                BeginRetargetEntry(selectedEntry, evt);
                return true;
            }

            if (selectedLink != null && selectedLinkSource != null && LinkTargets(selectedLink, targetNode.definition))
            {
                BeginRetargetLink(selectedLinkSource, selectedLink, evt);
                return true;
            }

            CombatActionEntry entry = FindEntryTargeting(targetNode.definition);
            if (entry != null)
            {
                BeginRetargetEntry(entry, evt);
                return true;
            }

            CombatAnimationDefinition source;
            CombatActionLink link = FindLinkTargeting(targetNode.definition, out source);
            if (link != null && source != null)
            {
                BeginRetargetLink(source, link, evt);
                return true;
            }

            return false;
        }

        private void HandleGraphEvents(Event evt)
        {
            dragMousePosition = evt.mousePosition;
            if (evt.type == EventType.MouseDown && evt.button == 2)
            {
                draggingCanvas = true;
                draggedNode = null;
                draggingEntryNode = false;
                draggingLink = false;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && draggingCanvas)
            {
                graphScroll = ClampGraphScroll(graphScroll - evt.delta, graphViewportSize);
                RequestRepaint();
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && evt.button == 2 && draggingCanvas)
            {
                draggingCanvas = false;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (TryHandlePortMouseDown(evt))
                {
                    return;
                }

                foreach (GraphNode node in nodes)
                {
                    if (node.rect.Contains(evt.mousePosition))
                    {
                        SelectDefinitionNode(node.definition);
                        if (evt.clickCount > 1)
                        {
                            OpenAction(node.definition);
                        }
                        else
                        {
                            BeginNodeDrag(node, evt.mousePosition);
                        }

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
                    BeginEntryNodeDrag(evt.mousePosition);
                    RequestRepaint();
                }
            }
            else if (evt.type == EventType.MouseDrag && draggedNode != null)
            {
                draggedNode.rect.position = evt.mousePosition - nodeDragOffset;
                SaveNodePosition(draggedNode);
                RequestRepaint();
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && draggingEntryNode)
            {
                entryNodeRect.position = evt.mousePosition - nodeDragOffset;
                RequestRepaint();
                evt.Use();
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
            else if (evt.type == EventType.MouseUp && (draggedNode != null || draggingEntryNode))
            {
                draggedNode = null;
                draggingEntryNode = false;
                evt.Use();
            }
        }

        private static Vector2 ClampGraphScroll(Vector2 scroll, Vector2 viewportSize)
        {
            return new Vector2(
                Mathf.Clamp(scroll.x, 0f, Mathf.Max(0f, CanvasWidth - viewportSize.x)),
                Mathf.Clamp(scroll.y, 0f, Mathf.Max(0f, CanvasHeight - viewportSize.y)));
        }

        private void HandleEntryNodeSelection(Event evt)
        {
            if (evt.type != EventType.MouseDown || evt.button != 0 || !entryNodeRect.Contains(evt.mousePosition))
            {
                return;
            }

            selectedDefinition = null;
            selectedLink = null;
            selectedLinkSource = null;
            selectedEntry = null;
        }

        private void HandleActionNodeSelection(GraphNode node, Event evt)
        {
            if (node == null || node.definition == null || evt.type != EventType.MouseDown || evt.button != 0 || !node.rect.Contains(evt.mousePosition))
            {
                return;
            }

            SelectDefinitionNode(node.definition);
        }

        private void SelectDefinitionNode(CombatAnimationDefinition action)
        {
            if (action == null)
            {
                return;
            }

            bool changed = selectedDefinition != action;
            selectedDefinition = action;
            selectedLink = null;
            selectedLinkSource = null;
            selectedEntry = null;
            if (changed)
            {
                selectAction?.Invoke(action);
            }
        }

        private void BeginNodeDrag(GraphNode node, Vector2 mousePosition)
        {
            draggedNode = node;
            draggingEntryNode = false;
            nodeDragOffset = mousePosition - node.rect.position;
        }

        private void BeginEntryNodeDrag(Vector2 mousePosition)
        {
            draggedNode = null;
            draggingEntryNode = true;
            nodeDragOffset = mousePosition - entryNodeRect.position;
        }

        private void BeginDragLink(bool fromEntry, CombatAnimationDefinition source, Event evt)
        {
            draggingLink = true;
            draggingFromEntry = fromEntry;
            dragSourceDefinition = source;
            dragExistingEntry = null;
            dragExistingLink = null;
            dragInputAction = selectedInputAction;
            dragMousePosition = evt.mousePosition;
            evt.Use();
        }

        private void BeginRetargetEntry(CombatActionEntry entry, Event evt)
        {
            selectedDefinition = null;
            selectedLink = null;
            selectedLinkSource = null;
            selectedEntry = entry;
            draggingLink = true;
            draggingFromEntry = true;
            dragSourceDefinition = null;
            dragExistingEntry = entry;
            dragExistingLink = null;
            dragInputAction = entry == null ? selectedInputAction : entry.inputAction;
            dragMousePosition = evt.mousePosition;
            evt.Use();
        }

        private void BeginRetargetLink(CombatAnimationDefinition source, CombatActionLink link, Event evt)
        {
            selectedDefinition = null;
            selectedEntry = null;
            selectedLinkSource = source;
            selectedLink = link;
            draggingLink = true;
            draggingFromEntry = false;
            dragSourceDefinition = source;
            dragExistingEntry = null;
            dragExistingLink = link;
            dragInputAction = link == null ? selectedInputAction : link.inputAction;
            dragMousePosition = evt.mousePosition;
            evt.Use();
        }

        private void CompleteDragLink(Vector2 mousePosition)
        {
            GraphNode target = null;
            foreach (GraphNode node in nodes)
            {
                if (IsInInputHotZone(node.rect, mousePosition) || node.rect.Contains(mousePosition))
                {
                    target = node;
                    break;
                }
            }

            if (target != null)
            {
                if (dragExistingEntry != null)
                {
                    RetargetEntry(dragExistingEntry, target.definition);
                }
                else if (dragExistingLink != null && dragSourceDefinition != null && dragSourceDefinition != target.definition)
                {
                    RetargetActionLink(dragSourceDefinition, dragExistingLink, target.definition);
                }
                else if (draggingFromEntry)
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
            dragExistingEntry = null;
            dragExistingLink = null;
            dragInputAction = selectedInputAction;
            RequestRepaint();
        }

        private void RequestRepaint()
        {
            repaint?.Invoke();
        }

        private void OpenAction(CombatAnimationDefinition action)
        {
            if (action == null)
            {
                return;
            }

            if (openAction != null)
            {
                openAction.Invoke(action);
                return;
            }

            Selection.activeObject = action;
            EditorGUIUtility.PingObject(action);
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
                if (candidate != null && CombatInputActionNames.ExactMatches(candidate.inputAction, selectedInputAction))
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

                bool sameInput = CombatInputActionNames.ExactMatches(candidate.inputAction, selectedInputAction);
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

        private void RetargetEntry(CombatActionEntry entry, CombatAnimationDefinition target)
        {
            if (database == null || entry == null || target == null)
            {
                return;
            }

            entry.targetDefinition = target;
            entry.targetActionId = target.actionId;
            EditorUtility.SetDirty(database);

            selectedEntry = entry;
            selectedLink = null;
            selectedLinkSource = null;
            selectedDefinition = null;
            RefreshGraphLookupsOnly();
        }

        private void RetargetActionLink(CombatAnimationDefinition source, CombatActionLink link, CombatAnimationDefinition target)
        {
            if (source == null || link == null || target == null)
            {
                return;
            }

            link.targetDefinition = target;
            link.targetActionId = target.actionId;
            EditorUtility.SetDirty(source);
            SelectLink(source, link);
            RefreshGraphLookupsOnly();
        }

        private void SelectLink(CombatAnimationDefinition source, CombatActionLink link)
        {
            selectedDefinition = null;
            selectedEntry = null;
            selectedLinkSource = source;
            selectedLink = link;
        }

        private CombatActionEntry FindEntryTargeting(CombatAnimationDefinition target)
        {
            if (database == null || target == null || database.entryActions == null)
            {
                return null;
            }

            foreach (CombatActionEntry entry in database.entryActions)
            {
                if (EntryTargets(entry, target))
                {
                    return entry;
                }
            }

            return null;
        }

        private CombatActionLink FindLinkTargeting(CombatAnimationDefinition target, out CombatAnimationDefinition source)
        {
            source = null;
            if (target == null)
            {
                return null;
            }

            foreach (GraphNode node in nodes)
            {
                CombatAnimationDefinition candidateSource = node.definition;
                if (candidateSource == null || candidateSource.actionLinks == null)
                {
                    continue;
                }

                foreach (CombatActionLink link in candidateSource.actionLinks)
                {
                    if (LinkTargets(link, target))
                    {
                        source = candidateSource;
                        return link;
                    }
                }
            }

            return null;
        }

        private static bool EntryTargets(CombatActionEntry entry, CombatAnimationDefinition target)
        {
            if (entry == null || target == null)
            {
                return false;
            }

            return entry.targetDefinition == target
                || (!string.IsNullOrWhiteSpace(target.actionId) && string.Equals(entry.targetActionId, target.actionId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool LinkTargets(CombatActionLink link, CombatAnimationDefinition target)
        {
            if (link == null || target == null)
            {
                return false;
            }

            return link.targetDefinition == target
                || (!string.IsNullOrWhiteSpace(target.actionId) && string.Equals(link.targetActionId, target.actionId, StringComparison.OrdinalIgnoreCase));
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
            ApplyAutoLayoutToUnsavedNodes();
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

        private void ApplyAutoLayoutToUnsavedNodes()
        {
            ApplyAutoLayout(false, false);
        }

        private void ApplyAutoLayoutToAllNodes()
        {
            ApplyAutoLayout(true, true);
        }

        private void ApplyAutoLayout(bool overwriteSavedPositions, bool savePositions)
        {
            if (nodes.Count == 0)
            {
                entryNodeRect.position = new Vector2(AutoLayoutEntryX, AutoLayoutTop);
                graphScroll = Vector2.zero;
                return;
            }

            entryNodeRect.position = new Vector2(AutoLayoutEntryX, AutoLayoutTop);

            Dictionary<GraphNode, int> depths = BuildAutoLayoutDepths();
            Dictionary<GraphNode, int> lanes = BuildAutoLayoutLanes(depths);
            List<Rect> occupiedRects = new List<Rect>();

            foreach (GraphNode node in nodes)
            {
                if (!overwriteSavedPositions && HasSavedNodePosition(node.definition))
                {
                    occupiedRects.Add(node.rect);
                }
            }

            List<GraphNode> orderedNodes = new List<GraphNode>(nodes);
            orderedNodes.Sort((left, right) =>
            {
                int leftDepth = GetLayoutDepth(depths, left);
                int rightDepth = GetLayoutDepth(depths, right);
                int depthCompare = leftDepth.CompareTo(rightDepth);
                if (depthCompare != 0)
                {
                    return depthCompare;
                }

                int laneCompare = GetLayoutLane(lanes, left).CompareTo(GetLayoutLane(lanes, right));
                return laneCompare != 0
                    ? laneCompare
                    : string.Compare(NodeTitle(left.definition), NodeTitle(right.definition), StringComparison.OrdinalIgnoreCase);
            });

            foreach (GraphNode node in orderedNodes)
            {
                if (!overwriteSavedPositions && HasSavedNodePosition(node.definition))
                {
                    continue;
                }

                int depth = GetLayoutDepth(depths, node);
                int lane = Mathf.Max(0, GetLayoutLane(lanes, node));
                Rect rect = AutoLayoutRect(depth, lane);
                while (OverlapsAny(rect, occupiedRects))
                {
                    lane++;
                    rect = AutoLayoutRect(depth, lane);
                }

                node.rect = rect;
                if (savePositions)
                {
                    SaveNodePosition(node);
                }

                occupiedRects.Add(rect);
            }

            graphScroll = ClampGraphScroll(new Vector2(0f, Mathf.Max(0f, AutoLayoutTop - 24f)), graphViewportSize);
        }

        private Dictionary<GraphNode, int> BuildAutoLayoutDepths()
        {
            Dictionary<GraphNode, int> depths = new Dictionary<GraphNode, int>();
            if (database != null)
            {
                database.EnsureEntryActions();
                foreach (CombatActionEntry entry in database.entryActions)
                {
                    GraphNode targetNode = ResolveTargetNode(entry == null ? null : entry.targetDefinition, entry == null ? string.Empty : entry.targetActionId);
                    AssignAutoLayoutDepth(targetNode, 1, depths, new HashSet<GraphNode>());
                }
            }

            foreach (GraphNode node in nodes)
            {
                if (!depths.ContainsKey(node))
                {
                    AssignAutoLayoutDepth(node, 1, depths, new HashSet<GraphNode>());
                }
            }

            return depths;
        }

        private void AssignAutoLayoutDepth(GraphNode node, int depth, Dictionary<GraphNode, int> depths, HashSet<GraphNode> visiting)
        {
            if (node == null)
            {
                return;
            }

            int clampedDepth = Mathf.Clamp(depth, 1, Mathf.Max(1, nodes.Count + 1));
            if (depths.TryGetValue(node, out int existingDepth) && existingDepth >= clampedDepth)
            {
                return;
            }

            depths[node] = clampedDepth;
            if (!visiting.Add(node))
            {
                return;
            }

            foreach (GraphNode targetNode in ResolveOutgoingTargetNodes(node))
            {
                AssignAutoLayoutDepth(targetNode, clampedDepth + 1, depths, visiting);
            }

            visiting.Remove(node);
        }

        private Dictionary<GraphNode, int> BuildAutoLayoutLanes(Dictionary<GraphNode, int> depths)
        {
            Dictionary<GraphNode, int> lanes = new Dictionary<GraphNode, int>();
            int nextLane = 0;

            if (database != null)
            {
                database.EnsureEntryActions();
                foreach (CombatActionEntry entry in database.entryActions)
                {
                    GraphNode targetNode = ResolveTargetNode(entry == null ? null : entry.targetDefinition, entry == null ? string.Empty : entry.targetActionId);
                    if (targetNode == null)
                    {
                        continue;
                    }

                    int lane = GetOrAssignLane(targetNode, lanes, ref nextLane);
                    AssignAutoLayoutLane(targetNode, lane, lanes, new HashSet<GraphNode>(), ref nextLane);
                }
            }

            List<GraphNode> orderedNodes = new List<GraphNode>(nodes);
            orderedNodes.Sort((left, right) =>
            {
                int depthCompare = GetLayoutDepth(depths, left).CompareTo(GetLayoutDepth(depths, right));
                return depthCompare != 0
                    ? depthCompare
                    : string.Compare(NodeTitle(left.definition), NodeTitle(right.definition), StringComparison.OrdinalIgnoreCase);
            });

            foreach (GraphNode node in orderedNodes)
            {
                if (lanes.ContainsKey(node))
                {
                    continue;
                }

                int lane = GetOrAssignLane(node, lanes, ref nextLane);
                AssignAutoLayoutLane(node, lane, lanes, new HashSet<GraphNode>(), ref nextLane);
            }

            return lanes;
        }

        private void AssignAutoLayoutLane(GraphNode node, int lane, Dictionary<GraphNode, int> lanes, HashSet<GraphNode> visiting, ref int nextLane)
        {
            if (node == null || !visiting.Add(node))
            {
                return;
            }

            List<GraphNode> targets = ResolveOutgoingTargetNodes(node);
            for (int i = 0; i < targets.Count; i++)
            {
                GraphNode targetNode = targets[i];
                int childLane = i == 0 ? lane : nextLane++;
                if (!lanes.ContainsKey(targetNode))
                {
                    lanes[targetNode] = childLane;
                }

                AssignAutoLayoutLane(targetNode, lanes[targetNode], lanes, visiting, ref nextLane);
            }

            visiting.Remove(node);
        }

        private List<GraphNode> ResolveOutgoingTargetNodes(GraphNode sourceNode)
        {
            List<GraphNode> targetNodes = new List<GraphNode>();
            CombatAnimationDefinition source = sourceNode == null ? null : sourceNode.definition;
            if (source == null)
            {
                return targetNodes;
            }

            source.EnsureActionLinks();
            List<CombatActionLink> links = new List<CombatActionLink>();
            foreach (CombatActionLink link in source.actionLinks)
            {
                if (link != null)
                {
                    links.Add(link);
                }
            }

            links.Sort(CompareActionLinksForLayout);
            foreach (CombatActionLink link in links)
            {
                GraphNode targetNode = ResolveTargetNode(link.targetDefinition, link.targetActionId);
                if (targetNode != null && !targetNodes.Contains(targetNode))
                {
                    targetNodes.Add(targetNode);
                }
            }

            return targetNodes;
        }

        private static int CompareActionLinksForLayout(CombatActionLink left, CombatActionLink right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int startCompare = left.startFrame.CompareTo(right.startFrame);
            if (startCompare != 0)
            {
                return startCompare;
            }

            int endCompare = left.endFrame.CompareTo(right.endFrame);
            if (endCompare != 0)
            {
                return endCompare;
            }

            int inputCompare = string.Compare(left.inputAction, right.inputAction, StringComparison.OrdinalIgnoreCase);
            if (inputCompare != 0)
            {
                return inputCompare;
            }

            string leftTargetName = left.targetDefinition == null ? left.targetActionId : NodeTitle(left.targetDefinition);
            string rightTargetName = right.targetDefinition == null ? right.targetActionId : NodeTitle(right.targetDefinition);
            return string.Compare(leftTargetName, rightTargetName, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetOrAssignLane(GraphNode node, Dictionary<GraphNode, int> lanes, ref int nextLane)
        {
            if (node == null)
            {
                return nextLane;
            }

            if (lanes.TryGetValue(node, out int lane))
            {
                return lane;
            }

            lane = nextLane++;
            lanes[node] = lane;
            return lane;
        }

        private static int GetLayoutDepth(Dictionary<GraphNode, int> depths, GraphNode node)
        {
            return node != null && depths.TryGetValue(node, out int depth) ? depth : 1;
        }

        private static int GetLayoutLane(Dictionary<GraphNode, int> lanes, GraphNode node)
        {
            return node != null && lanes.TryGetValue(node, out int lane) ? lane : 0;
        }

        private static Rect AutoLayoutRect(int depth, int lane)
        {
            return new Rect(
                AutoLayoutFirstActionX + (Mathf.Max(1, depth) - 1) * AutoLayoutColumnSpacing,
                AutoLayoutTop + Mathf.Max(0, lane) * AutoLayoutLaneSpacing,
                NodeWidth,
                NodeHeight);
        }

        private static bool OverlapsAny(Rect rect, List<Rect> occupiedRects)
        {
            Rect paddedRect = ExpandRect(rect, 10f);
            foreach (Rect occupiedRect in occupiedRects)
            {
                if (paddedRect.Overlaps(ExpandRect(occupiedRect, 10f)))
                {
                    return true;
                }
            }

            return false;
        }

        private static Rect LoadNodeRect(CombatAnimationDefinition definition, int index)
        {
            string key = NodePrefsKey(definition);
            float x = EditorPrefs.GetFloat(key + ".x", 300f + index % 5 * 250f);
            float y = EditorPrefs.GetFloat(key + ".y", 80f + index / 5 * 140f);
            return new Rect(x, y, NodeWidth, NodeHeight);
        }

        private static bool HasSavedNodePosition(CombatAnimationDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            string key = NodePrefsKey(definition);
            return EditorPrefs.HasKey(key + ".x") && EditorPrefs.HasKey(key + ".y");
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
            return new Rect(
                nodeRect.x + ConnectorInset,
                nodeRect.y + nodeRect.height * 0.5f - ConnectorHeight * 0.5f,
                ConnectorWidth,
                ConnectorHeight);
        }

        private static Rect GetOutputPort(Rect nodeRect)
        {
            return new Rect(
                nodeRect.xMax - ConnectorWidth - ConnectorInset,
                nodeRect.y + nodeRect.height * 0.5f - ConnectorHeight * 0.5f,
                ConnectorWidth,
                ConnectorHeight);
        }

        private Rect GetEntryOutputPort()
        {
            return GetOutputPort(entryNodeRect);
        }

        private static bool IsInInputHotZone(Rect nodeRect, Vector2 point)
        {
            Rect port = GetInputPort(nodeRect);
            return ExpandRect(port, 8f).Contains(point);
        }

        private static bool IsInOutputHotZone(Rect nodeRect, Vector2 point)
        {
            Rect port = GetOutputPort(nodeRect);
            return ExpandRect(port, 8f).Contains(point);
        }

        private static Rect ExpandRect(Rect rect, float amount)
        {
            return new Rect(rect.x - amount, rect.y - amount, rect.width + amount * 2f, rect.height + amount * 2f);
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

        private static void DrawConnectionNode(Vector2 start, Vector2 end, Rect sourceRect, Rect targetRect, string inputAction, string detail, int lane, bool selected, Action onClick)
        {
            Rect rect = GetConnectionLabelRect(start, end, sourceRect, targetRect, ConnectionLabelWidth(inputAction, detail), lane);
            Color connectionColor = InputColor(inputAction);
            DrawConnectionLabelLeader(rect, end, connectionColor, selected);
            EditorGUI.DrawRect(rect, selected ? new Color(0.28f, 0.24f, 0.12f, 0.98f) : new Color(0.18f, 0.19f, 0.20f, 0.94f));

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                onClick?.Invoke();
            }

            if (selected)
            {
                DrawRectOutline(rect, Color.yellow, 2f);
            }

            GUILayout.BeginArea(new Rect(rect.x + 4f, rect.y + 3f, rect.width - 8f, rect.height - 6f));
            GUILayout.BeginHorizontal();
            DrawInputSequencePill(inputAction, detail, selected);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private static Rect GetConnectionLabelRect(Vector2 start, Vector2 end, Rect sourceRect, Rect targetRect, float width, int lane)
        {
            const float height = 30f;
            const float horizontalGap = 16f;

            bool targetIsToRight = end.x >= start.x;
            float x = targetIsToRight
                ? targetRect.xMin - width - horizontalGap
                : targetRect.xMax + horizontalGap;
            float y = end.y - height * 0.5f + StackedConnectionLabelOffset(lane);

            if (targetIsToRight && x < sourceRect.xMax + 10f)
            {
                x = Mathf.Lerp(sourceRect.xMax, targetRect.xMin, 0.5f) - width * 0.5f;
                y = Mathf.Min(sourceRect.yMin, targetRect.yMin) - height - 8f + StackedConnectionLabelOffset(lane);
            }
            else if (!targetIsToRight && x + width > sourceRect.xMin - 10f)
            {
                x = Mathf.Lerp(targetRect.xMax, sourceRect.xMin, 0.5f) - width * 0.5f;
                y = Mathf.Max(sourceRect.yMax, targetRect.yMax) + 8f + StackedConnectionLabelOffset(lane);
            }

            return new Rect(
                Mathf.Clamp(x, 8f, CanvasWidth - width - 8f),
                Mathf.Clamp(y, 8f, CanvasHeight - height - 8f),
                width,
                height);
        }

        private static float StackedConnectionLabelOffset(int lane)
        {
            if (lane <= 0)
            {
                return 0f;
            }

            int row = (lane + 1) / 2;
            float direction = lane % 2 == 0 ? 1f : -1f;
            return direction * row * 34f;
        }

        private static void DrawConnectionLabelLeader(Rect labelRect, Vector2 portCenter, Color color, bool selected)
        {
            Vector2 labelAnchor = portCenter.x < labelRect.center.x
                ? new Vector2(labelRect.xMin, labelRect.center.y)
                : new Vector2(labelRect.xMax, labelRect.center.y);

            Color previousColor = Handles.color;
            Handles.BeginGUI();
            Handles.color = selected ? Color.yellow : color;
            Handles.DrawAAPolyLine(selected ? 3f : 2f, labelAnchor, portCenter);
            Handles.DrawSolidDisc(portCenter, Vector3.forward, selected ? 4f : 3f);
            Handles.EndGUI();
            Handles.color = previousColor;
        }

        private static float ConnectionLabelWidth(string inputAction, string detail)
        {
            float width = 16f;
            string[] tokens = CombatInputActionNames.GetInputActionTokens(inputAction);
            for (int i = 0; i < tokens.Length; i++)
            {
                width += InputTokenWidth(CombatInputActionNames.DisplayToken(tokens[i]));
                if (i < tokens.Length - 1)
                {
                    width += 10f;
                }
            }

            if (!string.IsNullOrWhiteSpace(detail))
            {
                width += 82f;
            }

            return Mathf.Clamp(width, 150f, 520f);
        }

        private static void DrawInputSequencePill(string inputAction, string detail, bool selected)
        {
            string[] tokens = CombatInputActionNames.GetInputActionTokens(inputAction);
            Color previousBackground = GUI.backgroundColor;
            if (tokens.Length == 0)
            {
                GUI.backgroundColor = new Color(0.40f, 0.42f, 0.46f, 1f);
                GUILayout.Box("None", EditorStyles.miniButton, GUILayout.Width(48f), GUILayout.Height(22f));
                GUI.backgroundColor = previousBackground;
            }
            else
            {
                for (int i = 0; i < tokens.Length; i++)
                {
                    string tokenLabel = CombatInputActionNames.DisplayToken(tokens[i]);
                    GUI.backgroundColor = InputTokenColor(tokens[i]);
                    GUILayout.Box(tokenLabel, EditorStyles.miniButton, GUILayout.Width(InputTokenWidth(tokenLabel)), GUILayout.Height(22f));
                    GUI.backgroundColor = previousBackground;
                    if (i < tokens.Length - 1)
                    {
                        GUILayout.Label("+", EditorStyles.miniBoldLabel, GUILayout.Width(10f));
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(detail))
            {
                GUILayout.Label(detail, selected ? EditorStyles.whiteMiniLabel : EditorStyles.miniBoldLabel, GUILayout.Width(72f), GUILayout.Height(22f));
            }
        }

        private static float InputTokenWidth(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return 32f;
            }

            return Mathf.Clamp(token.Length * 8f + 18f, 36f, 96f);
        }

        private static Color InputButtonColor(string buttonToken)
        {
            switch (buttonToken)
            {
                case "□":
                    return new Color(0.32f, 0.55f, 0.95f, 1f);
                case "△":
                    return new Color(0.34f, 0.70f, 0.48f, 1f);
                case "○":
                    return new Color(0.90f, 0.38f, 0.38f, 1f);
                case "×":
                    return new Color(0.60f, 0.48f, 0.95f, 1f);
                default:
                    return new Color(0.42f, 0.42f, 0.42f, 1f);
            }
        }

        private static Color InputTokenColor(string token)
        {
            string normalized = CombatInputActionNames.Normalize(token);
            if (normalized.StartsWith("Stick.", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("DPad.", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(0.22f, 0.40f, 0.62f, 1f);
            }

            if (normalized.Contains("Attack.Heavy", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(0.95f, 0.48f, 0.26f, 1f);
            }

            if (normalized.Contains("Dodge", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(0.34f, 0.78f, 0.88f, 1f);
            }

            if (normalized.Contains("Jump", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(0.60f, 0.48f, 0.95f, 1f);
            }

            if (normalized.Contains("Guard", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Button.", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(0.34f, 0.70f, 0.48f, 1f);
            }

            return new Color(0.42f, 0.42f, 0.42f, 1f);
        }

        private static void DrawPort(Rect rect, Color color)
        {
            EditorGUI.DrawRect(rect, color);
            DrawRectOutline(rect, Color.black, 1f);
        }

        private static Rect NodeLocalRect(Rect nodeRect, float y, float height)
        {
            return new Rect(nodeRect.x + NodeContentX, nodeRect.y + y, NodeContentWidth, height);
        }

        private static void DrawNodeFrame(Rect rect, Color color, bool selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.20f, 0.21f, 0.22f, 0.96f));
            DrawRectOutline(rect, selected ? Color.yellow : color, selected ? 3f : 2f);
        }

        private static void DrawRectOutline(Rect rect, Color color, float width)
        {
            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = color;
            Vector3 topLeft = new Vector3(rect.xMin, rect.yMin);
            Vector3 topRight = new Vector3(rect.xMax, rect.yMin);
            Vector3 bottomRight = new Vector3(rect.xMax, rect.yMax);
            Vector3 bottomLeft = new Vector3(rect.xMin, rect.yMax);
            Handles.DrawAAPolyLine(width, topLeft, topRight, bottomRight, bottomLeft, topLeft);
            Handles.color = previousColor;
            Handles.EndGUI();
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
            if (normalized.Contains("Attack.Heavy", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(0.95f, 0.48f, 0.26f, 1f);
            }

            if (normalized.Contains("Dodge", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(0.34f, 0.78f, 0.88f, 1f);
            }

            return new Color(0.48f, 0.75f, 0.38f, 1f);
        }

        private static string DrawInputActionPopup(string label, string inputAction)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            string nextValue = DrawInputActionComposer(inputAction, false);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Sequence", nextValue);
            }

            return nextValue;
        }

        private static string DrawInputActionToolbarPopup(string inputAction, params GUILayoutOption[] options)
        {
            return DrawInputActionComposer(inputAction, true, options);
        }

        private static readonly string[] InputTokenPresetLabels =
        {
            "Square", "Triangle", "Circle", "Cross",
            "L1", "R1", "L2", "R2", "L3", "R3",
            "LS Up", "LS Down", "LS Left", "LS Right",
            "DPad Up", "DPad Down", "DPad Left", "DPad Right",
            "Custom"
        };

        private static readonly string[] InputTokenPresetValues =
        {
            CombatInputActionNames.LightAttack,
            CombatInputActionNames.HeavyAttack,
            CombatInputActionNames.Dodge,
            CombatInputActionNames.Jump,
            CombatInputActionNames.Guard,
            CombatInputActionNames.ButtonR1,
            CombatInputActionNames.ButtonL2,
            CombatInputActionNames.ButtonR2,
            CombatInputActionNames.ButtonL3,
            CombatInputActionNames.ButtonR3,
            "Stick.Up",
            "Stick.Down",
            "Stick.Left",
            "Stick.Right",
            CombatInputActionNames.DPadUp,
            CombatInputActionNames.DPadDown,
            CombatInputActionNames.DPadLeft,
            CombatInputActionNames.DPadRight,
            string.Empty
        };

        private static string DrawInputActionComposer(string inputAction, bool toolbar, params GUILayoutOption[] options)
        {
            GUIStyle popupStyle = toolbar ? EditorStyles.toolbarPopup : EditorStyles.popup;
            GUIStyle textStyle = toolbar ? EditorStyles.toolbarTextField : EditorStyles.textField;
            List<string> tokens = new List<string>(CombatInputActionNames.GetInputActionTokens(inputAction));
            if (tokens.Count == 0)
            {
                tokens.Add(CombatInputActionNames.LightAttack);
            }

            int removeIndex = -1;
            if (toolbar)
            {
                EditorGUILayout.BeginHorizontal(options);
                for (int i = 0; i < tokens.Count; i++)
                {
                    DrawInputTokenRow(tokens, i, popupStyle, textStyle, true, ref removeIndex);
                    GUILayout.Space(2f);
                }

                if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                {
                    tokens.Add(NextNewInputToken(tokens));
                }

                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, options);
                for (int i = 0; i < tokens.Count; i++)
                {
                    DrawInputTokenRow(tokens, i, popupStyle, textStyle, false, ref removeIndex);
                }

                if (GUILayout.Button("+ Add Token", GUILayout.Height(24f)))
                {
                    tokens.Add(NextNewInputToken(tokens));
                }

                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0 && tokens.Count > 1)
            {
                tokens.RemoveAt(removeIndex);
            }

            return CombatInputActionNames.ComposeInputAction(tokens);
        }

        private static void DrawInputTokenRow(List<string> tokens, int index, GUIStyle popupStyle, GUIStyle textStyle, bool toolbar, ref int removeIndex)
        {
            if (!toolbar)
            {
                EditorGUILayout.BeginHorizontal();
            }

            int presetIndex = InputTokenPresetIndex(tokens[index]);
            int nextPresetIndex = EditorGUILayout.Popup(presetIndex, InputTokenPresetLabels, popupStyle, GUILayout.Width(toolbar ? 78f : 104f));
            if (nextPresetIndex >= 0
                && nextPresetIndex < InputTokenPresetValues.Length
                && !string.IsNullOrWhiteSpace(InputTokenPresetValues[nextPresetIndex]))
            {
                tokens[index] = InputTokenPresetValues[nextPresetIndex];
            }

            tokens[index] = EditorGUILayout.TextField(tokens[index], textStyle, GUILayout.Width(toolbar ? 108f : 150f));
            if (GUILayout.Button("-", toolbar ? EditorStyles.toolbarButton : EditorStyles.miniButton, GUILayout.Width(22f)))
            {
                removeIndex = index;
            }

            if (!toolbar)
            {
                EditorGUILayout.EndHorizontal();
            }
        }

        private static string NextNewInputToken(List<string> existingTokens)
        {
            for (int i = 0; i < InputTokenPresetValues.Length; i++)
            {
                string candidate = InputTokenPresetValues[i];
                if (string.IsNullOrWhiteSpace(candidate) || HasInputToken(existingTokens, candidate))
                {
                    continue;
                }

                return candidate;
            }

            return "Custom." + existingTokens.Count;
        }

        private static bool HasInputToken(List<string> tokens, string candidate)
        {
            string normalizedCandidate = CombatInputActionNames.ComposeInputAction(new[] { candidate });
            foreach (string token in tokens)
            {
                string normalizedToken = CombatInputActionNames.ComposeInputAction(new[] { token });
                if (string.Equals(normalizedToken, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int InputTokenPresetIndex(string token)
        {
            string normalized = CombatInputActionNames.ComposeInputAction(new[] { token });
            for (int i = 0; i < InputTokenPresetValues.Length - 1; i++)
            {
                if (string.Equals(InputTokenPresetValues[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return InputTokenPresetValues.Length - 1;
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

            return NodeDisplayName(definition);
        }

        private static string NodeDisplayName(CombatAnimationDefinition definition)
        {
            if (definition == null)
            {
                return "Missing";
            }

            if (!string.IsNullOrWhiteSpace(definition.stateName))
            {
                return definition.stateName;
            }

            return string.IsNullOrWhiteSpace(definition.name) ? "Unnamed Action" : definition.name;
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
