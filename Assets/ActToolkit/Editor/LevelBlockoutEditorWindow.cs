using System;
using System.Collections.Generic;
using System.IO;
using ActToolkit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ActToolkit.EditorTools
{
    public sealed class LevelBlockoutEditorWindow : EditorWindow
    {
        private const string SceneFolder = ActToolkitEditorUtilities.GeneratedFolder + "/Scenes";
        private const string DefaultLevelName = "Blockout_Level";
        private const string BlockoutRootName = "Blockout_Root";
        private const string PlaytestProfilePrefsKey = "ActToolkit.LevelWorkspace.PlaytestProfile";
        private const string DefaultPlaytestProfilePath = ActToolkitEditorUtilities.CombatMvpFolder + "/Female_Mannequin_Profile.asset";
        private const string PlaytestBootstrapName = "ActPlaytestBootstrap";
        private const float LevelKeyLightIntensity = 0.75f;
        private static readonly Color LevelCameraBackgroundColor = new Color(0.18f, 0.20f, 0.22f, 1f);
        private static readonly Color LevelAmbientColor = new Color(0.30f, 0.32f, 0.34f, 1f);
        private static readonly Color LevelKeyLightColor = new Color(1f, 0.96f, 0.90f, 1f);

        private enum LevelWorkspaceMode
        {
            Level,
            Blockout,
            Gameplay,
            Validate,
            Playtest,
            Export
        }

        private enum ValidationSeverity
        {
            Info,
            Warning,
            Error
        }

        private enum SceneEditTool
        {
            Select,
            Place
        }

        private sealed class ValidationIssue
        {
            public ValidationSeverity severity;
            public string message;
            public BlockoutElement element;
        }

        private LevelWorkspaceMode workspaceMode = LevelWorkspaceMode.Level;
        private BlockoutElementKind kind = BlockoutElementKind.Block;
        private Vector3 size = new Vector3(2f, 1f, 2f);
        private float gridSize = 1f;
        private int team;
        private string gameplayTag = "arena";
        private Transform parent;
        private CharacterActionProfile playtestProfile;
        private SceneEditTool sceneEditTool = SceneEditTool.Place;
        private bool sceneEditingEnabled = true;
        private bool createOnEmptyLeftClick = true;
        private bool showPlacementPreview = true;
        private bool snapSceneDrag = true;
        private string newLevelName = DefaultLevelName;
        private Vector2 leftScrollPosition;
        private Vector2 centerScrollPosition;
        private Vector2 rightScrollPosition;
        private Vector2 levelListScrollPosition;
        private Vector2 elementListScrollPosition;
        private Vector2 planViewCenter;
        private float planViewPixelsPerMeter = 24f;
        private bool planFrameRequested = true;
        private BlockoutElement planDraggedElement;
        private Vector2 planDragOffset;
        private bool planIsPanning;
        private Vector2 planPanStartMouse;
        private Vector2 planPanStartCenter;
        private BlockoutElement draggedElement;
        private Vector3 dragOffset;
        private Vector3 sceneHoverPoint;
        private Vector3 sceneHoverSurfacePoint;
        private Vector3 sceneHoverSurfaceNormal = Vector3.up;
        private bool hasSceneHoverPoint;
        private readonly List<ValidationIssue> validationIssues = new List<ValidationIssue>();
        private string validationScenePath;

        [MenuItem(ActToolkitMenu.LevelRoot + "/Level Workspace", false, 20)]
        public static void Open()
        {
            LevelBlockoutEditorWindow window = GetWindow<LevelBlockoutEditorWindow>();
            window.titleContent = new GUIContent("Level Workspace");
            window.minSize = new Vector2(1080f, 620f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += DuringSceneGui;
            LoadPlaytestProfileFromPrefs();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DuringSceneGui;
        }

        private void OnSelectionChange()
        {
            Repaint();
        }

        private void LoadPlaytestProfileFromPrefs()
        {
            string profilePath = EditorPrefs.GetString(PlaytestProfilePrefsKey, DefaultPlaytestProfilePath);
            playtestProfile = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(profilePath);
            if (playtestProfile == null)
            {
                playtestProfile = FindBestPlaytestProfileAsset();
                SavePlaytestProfilePrefs();
            }
        }

        private void SavePlaytestProfilePrefs()
        {
            if (playtestProfile == null)
            {
                EditorPrefs.DeleteKey(PlaytestProfilePrefsKey);
                return;
            }

            string path = AssetDatabase.GetAssetPath(playtestProfile);
            if (!string.IsNullOrWhiteSpace(path))
            {
                EditorPrefs.SetString(PlaytestProfilePrefsKey, path);
            }
        }

        private static CharacterActionProfile FindBestPlaytestProfileAsset()
        {
            string[] preferredPaths =
            {
                DefaultPlaytestProfilePath,
                ActToolkitEditorUtilities.CombatMvpFolder + "/CharacterActionProfile.asset",
                ActToolkitEditorUtilities.CombatMvpFolder + "/MVP_CharacterActionProfile.asset"
            };

            foreach (string path in preferredPaths)
            {
                CharacterActionProfile preferred = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(path);
                if (IsUsablePlaytestProfile(preferred))
                {
                    return preferred;
                }
            }

            string[] guids = AssetDatabase.FindAssets("t:CharacterActionProfile", new[] { ActToolkitEditorUtilities.CombatMvpFolder });
            CharacterActionProfile fallback = null;
            foreach (string guid in guids)
            {
                CharacterActionProfile candidate = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate == null)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = candidate;
                }

                if (IsUsablePlaytestProfile(candidate))
                {
                    return candidate;
                }
            }

            return fallback;
        }

        private static bool IsUsablePlaytestProfile(CharacterActionProfile profile)
        {
            return profile != null && profile.modelPrefab != null && profile.comboTable != null;
        }

        private void OnGUI()
        {
            ActToolkitEditorUtilities.EnsureGeneratedFolders();
            ActToolkitEditorUtilities.EnsureFolder(ActToolkitEditorUtilities.GeneratedFolder, "Scenes");

            DrawTopBar();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeftModePanel();
                DrawSceneWorkspacePanel();
                DrawRightInspectorPanel();
            }

            DrawStatusBar();
        }

        private void DrawTopBar()
        {
            Scene activeScene = SceneManager.GetActiveScene();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(32f)))
            {
                GUILayout.Label("Level Workspace", EditorStyles.boldLabel, GUILayout.Width(142f));
                DrawModeButton(LevelWorkspaceMode.Level, "Level");
                DrawModeButton(LevelWorkspaceMode.Blockout, "Blockout");
                DrawModeButton(LevelWorkspaceMode.Gameplay, "Gameplay");
                DrawModeButton(LevelWorkspaceMode.Validate, "Validate");
                DrawModeButton(LevelWorkspaceMode.Playtest, "Playtest");
                DrawModeButton(LevelWorkspaceMode.Export, "Export");

                GUILayout.FlexibleSpace();

                string sceneName = string.IsNullOrWhiteSpace(activeScene.name) ? "Unsaved Scene" : activeScene.name;
                string dirtyMark = activeScene.isDirty ? " *" : string.Empty;
                GUILayout.Label(sceneName + dirtyMark, EditorStyles.miniLabel, GUILayout.MaxWidth(260f));

                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(56f)))
                {
                    SaveActiveScene();
                }

                if (GUILayout.Button("Playtest", EditorStyles.toolbarButton, GUILayout.Width(74f)))
                {
                    SaveAndEnterPlaytest();
                }
            }
        }

        private void DrawModeButton(LevelWorkspaceMode mode, string label)
        {
            bool selected = workspaceMode == mode;
            bool nextSelected = GUILayout.Toggle(selected, label, EditorStyles.toolbarButton, GUILayout.Width(86f));
            if (nextSelected && !selected)
            {
                workspaceMode = mode;
                if (mode == LevelWorkspaceMode.Validate)
                {
                    RunLevelValidation();
                }

                Repaint();
            }
        }

        private void DrawLeftModePanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(330f), GUILayout.ExpandHeight(true)))
            {
                using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(leftScrollPosition))
                {
                    leftScrollPosition = scroll.scrollPosition;

                    switch (workspaceMode)
                    {
                        case LevelWorkspaceMode.Level:
                            DrawLevelBrowserPanel();
                            break;
                        case LevelWorkspaceMode.Blockout:
                            DrawBlockoutBrushPanel();
                            break;
                        case LevelWorkspaceMode.Gameplay:
                            DrawGameplayMarkerPanel();
                            break;
                        case LevelWorkspaceMode.Validate:
                            DrawValidationPanel();
                            break;
                        case LevelWorkspaceMode.Playtest:
                            DrawPlaytestPanel();
                            break;
                        case LevelWorkspaceMode.Export:
                            DrawExportPanel();
                            break;
                    }

                    EditorGUILayout.Space(8f);
                    DrawSharedSceneSettings();
                }
            }
        }

        private void DrawLevelBrowserPanel()
        {
            Scene activeScene = SceneManager.GetActiveScene();

            EditorGUILayout.LabelField("Level Browser", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Current", string.IsNullOrWhiteSpace(activeScene.name) ? "Unsaved Scene" : activeScene.name);
                EditorGUILayout.LabelField("Path", string.IsNullOrWhiteSpace(activeScene.path) ? "(not saved)" : activeScene.path);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Save", GUILayout.Height(26f)))
                    {
                        SaveActiveScene();
                    }

                    if (GUILayout.Button("Frame All", GUILayout.Height(26f)))
                    {
                        FrameAllBlockoutElements();
                    }
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Create Level", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                newLevelName = EditorGUILayout.TextField("Name", newLevelName);
                if (GUILayout.Button("Create New Level", GUILayout.Height(28f)))
                {
                    CreateNewLevel();
                }

                if (GUILayout.Button("Create Test Arena", GUILayout.Height(28f)))
                {
                    CreateTestArena();
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Generated Levels", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string[] scenePaths = FindGeneratedLevelScenes();
                if (scenePaths.Length == 0)
                {
                    EditorGUILayout.LabelField("No generated level scenes yet.");
                }
                else
                {
                    using (EditorGUILayout.ScrollViewScope levelScroll = new EditorGUILayout.ScrollViewScope(levelListScrollPosition, GUILayout.MinHeight(140f), GUILayout.MaxHeight(260f)))
                    {
                        levelListScrollPosition = levelScroll.scrollPosition;
                        foreach (string scenePath in scenePaths)
                        {
                            DrawLevelRow(scenePath, activeScene.path);
                        }
                    }
                }
            }
        }

        private static void DrawLevelRow(string scenePath, string activeScenePath)
        {
            bool isCurrent = string.Equals(scenePath, activeScenePath, StringComparison.OrdinalIgnoreCase);
            string label = Path.GetFileNameWithoutExtension(scenePath);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUIStyle style = isCurrent ? EditorStyles.boldLabel : EditorStyles.label;
                EditorGUILayout.LabelField(isCurrent ? label + " *" : label, style);

                using (new EditorGUI.DisabledScope(isCurrent))
                {
                    if (GUILayout.Button("Open", GUILayout.Width(58f)))
                    {
                        OpenLevel(scenePath);
                    }
                }

                if (GUILayout.Button("Clone", GUILayout.Width(58f)))
                {
                    CloneLevel(scenePath);
                }
            }
        }

        private void DrawBlockoutBrushPanel()
        {
            EditorGUILayout.LabelField("Blockout", EditorStyles.boldLabel);
            DrawBrushGrid(new[]
            {
                BlockoutElementKind.Floor,
                BlockoutElementKind.Wall,
                BlockoutElementKind.Block,
                BlockoutElementKind.Ramp,
                BlockoutElementKind.Platform,
                BlockoutElementKind.Cover
            });

            EditorGUILayout.Space(6f);
            DrawBrushSettings();
        }

        private void DrawGameplayMarkerPanel()
        {
            EditorGUILayout.LabelField("Gameplay Markers", EditorStyles.boldLabel);
            DrawBrushGrid(new[]
            {
                BlockoutElementKind.SpawnPoint,
                BlockoutElementKind.EnemySpawn,
                BlockoutElementKind.DummySpawn,
                BlockoutElementKind.Objective,
                BlockoutElementKind.CombatZone,
                BlockoutElementKind.TriggerVolume,
                BlockoutElementKind.KillZone,
                BlockoutElementKind.NavMarker
            });

            EditorGUILayout.Space(6f);
            DrawBrushSettings();
        }

        private void DrawBrushGrid(BlockoutElementKind[] kinds)
        {
            const int columns = 2;
            for (int i = 0; i < kinds.Length; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int index = i + column;
                        if (index >= kinds.Length)
                        {
                            GUILayout.FlexibleSpace();
                            continue;
                        }

                        BlockoutElementKind buttonKind = kinds[index];
                        Color previousColor = GUI.backgroundColor;
                        if (kind == buttonKind)
                        {
                            GUI.backgroundColor = ActToolkitEditorUtilities.ColorFor(buttonKind);
                        }

                        if (GUILayout.Button(DisplayName(buttonKind), GUILayout.Height(34f)))
                        {
                            SetActiveBrush(buttonKind);
                        }

                        GUI.backgroundColor = previousColor;
                    }
                }
            }
        }

        private void DrawBrushSettings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Active Brush", DisplayName(kind));

                EditorGUI.BeginChangeCheck();
                size = EditorGUILayout.Vector3Field("Size", size);
                gridSize = Mathf.Max(0.05f, EditorGUILayout.FloatField("Grid", gridSize));
                gameplayTag = EditorGUILayout.TextField("Gameplay Tag", gameplayTag);
                team = EditorGUILayout.IntField("Team", team);
                parent = (Transform)EditorGUILayout.ObjectField("Parent", parent, typeof(Transform), true);
                if (EditorGUI.EndChangeCheck())
                {
                    size = SanitizedSize(kind, size);
                    SceneView.RepaintAll();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Place At View", GUILayout.Height(26f)))
                    {
                        CreateElement(ActToolkitEditorUtilities.SceneViewGroundPoint(gridSize));
                    }

                    if (GUILayout.Button("Select All", GUILayout.Height(26f)))
                    {
                        SelectAllBlockoutElements();
                    }
                }
            }
        }

        private void DrawSharedSceneSettings()
        {
            EditorGUILayout.LabelField("Scene Tools", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                sceneEditTool = (SceneEditTool)GUILayout.Toolbar((int)sceneEditTool, new[] { "Select", "Place" }, GUILayout.Height(28f));
                if (EditorGUI.EndChangeCheck())
                {
                    draggedElement = null;
                    SceneView.RepaintAll();
                }

                sceneEditingEnabled = EditorGUILayout.Toggle("Scene Editing", sceneEditingEnabled);
                createOnEmptyLeftClick = EditorGUILayout.Toggle("Click Places Brush", createOnEmptyLeftClick);
                showPlacementPreview = EditorGUILayout.Toggle("Placement Preview", showPlacementPreview);
                snapSceneDrag = EditorGUILayout.Toggle("Snap Drag", snapSceneDrag);
                gridSize = Mathf.Max(0.05f, EditorGUILayout.FloatField("Grid Size", gridSize));
            }
        }

        private void DrawValidationPanel()
        {
            EditorGUILayout.LabelField("Validate + Fix", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (GUILayout.Button("Run Validation", GUILayout.Height(28f)))
                {
                    RunLevelValidation();
                }

                DrawValidationSummary();
            }
        }

        private void DrawPlaytestPanel()
        {
            EditorGUILayout.LabelField("Playtest", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                playtestProfile = (CharacterActionProfile)EditorGUILayout.ObjectField("Character", playtestProfile, typeof(CharacterActionProfile), false);
                if (EditorGUI.EndChangeCheck())
                {
                    SavePlaytestProfilePrefs();
                }

                DrawPlaytestReadiness();

                if (GUILayout.Button("Install / Update Playtest Bootstrap", GUILayout.Height(28f)))
                {
                    InstallOrUpdatePlaytestBootstrap(true);
                }

                if (GUILayout.Button("Save And Playtest Current Level", GUILayout.Height(30f)))
                {
                    SaveAndEnterPlaytest();
                }

                if (GUILayout.Button("Create Test Arena", GUILayout.Height(26f)))
                {
                    CreateTestArena();
                }

                if (GUILayout.Button("Run Validation First", GUILayout.Height(26f)))
                {
                    RunLevelValidation();
                    workspaceMode = LevelWorkspaceMode.Validate;
                }
            }
        }

        private void DrawPlaytestReadiness()
        {
            BlockoutElement[] elements = FindSceneBlockoutElements();
            int playerSpawnCount = CountElements(elements, BlockoutElementKind.SpawnPoint);
            int dummySpawnCount = CountElements(elements, BlockoutElementKind.DummySpawn);
            int enemySpawnCount = CountElements(elements, BlockoutElementKind.EnemySpawn);
            ActPlaytestBootstrap bootstrap = FindPlaytestBootstrap();

            MessageType profileMessageType = MessageType.Info;
            string profileMessage = "Character profile is ready.";
            if (playtestProfile == null)
            {
                profileMessageType = MessageType.Error;
                profileMessage = "Choose a CharacterActionProfile before playtesting.";
            }
            else if (playtestProfile.comboTable == null)
            {
                profileMessageType = MessageType.Error;
                profileMessage = "Selected character has no combo table.";
            }
            else if (playtestProfile.modelPrefab == null)
            {
                profileMessageType = MessageType.Warning;
                profileMessage = "Selected character has no model prefab; playtest will use a placeholder.";
            }

            EditorGUILayout.HelpBox(profileMessage, profileMessageType);
            EditorGUILayout.LabelField("Bootstrap", bootstrap == null ? "Not installed" : bootstrap.name);
            EditorGUILayout.LabelField("Player Spawns", playerSpawnCount.ToString());
            EditorGUILayout.LabelField("Dummy Spawns", dummySpawnCount.ToString());
            EditorGUILayout.LabelField("Enemy Spawns", enemySpawnCount.ToString());

            if (playerSpawnCount == 0)
            {
                EditorGUILayout.HelpBox("No Player Spawn found. Playtest will spawn the player at world origin.", MessageType.Warning);
            }

            if (dummySpawnCount == 0 && enemySpawnCount == 0)
            {
                EditorGUILayout.HelpBox("No Dummy Spawn or Enemy Spawn found. Playtest will create one dummy in front of the player.", MessageType.Info);
            }
        }

        private static int CountElements(BlockoutElement[] elements, BlockoutElementKind elementKind)
        {
            int count = 0;
            foreach (BlockoutElement element in elements)
            {
                if (element != null && element.kind == elementKind)
                {
                    count++;
                }
            }

            return count;
        }

        private void DrawExportPanel()
        {
            EditorGUILayout.LabelField("Export Runtime", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (GUILayout.Button("Export Current Level JSON", GUILayout.Height(30f)))
                {
                    ExportSceneBlockout();
                }

                if (GUILayout.Button("Validate Before Export", GUILayout.Height(28f)))
                {
                    RunLevelValidation();
                    workspaceMode = LevelWorkspaceMode.Validate;
                }
            }
        }

        private void DrawSceneWorkspacePanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(centerScrollPosition))
                {
                    centerScrollPosition = scroll.scrollPosition;

                    DrawSceneCanvasHeader();
                    EditorGUILayout.Space(8f);
                    DrawPlanPreview();
                    EditorGUILayout.Space(8f);
                    DrawElementList();
                }
            }
        }

        private void DrawSceneCanvasHeader()
        {
            EditorGUILayout.LabelField("Scene Canvas", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Mode", workspaceMode.ToString());
                EditorGUILayout.LabelField("Brush", DisplayName(kind));
                EditorGUILayout.LabelField("Controls", "Place: LMB puts brush on surfaces. Select: LMB selects/drags. RMB context, Q/Esc select.");

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Frame All", GUILayout.Height(26f)))
                    {
                        FrameAllBlockoutElements();
                    }

                    if (GUILayout.Button("Run Validation", GUILayout.Height(26f)))
                    {
                        RunLevelValidation();
                        workspaceMode = LevelWorkspaceMode.Validate;
                    }

                    if (GUILayout.Button("Export JSON", GUILayout.Height(26f)))
                    {
                        ExportSceneBlockout();
                    }
                }
            }
        }

        private void DrawPlanPreview()
        {
            BlockoutElement[] elements = FindSceneBlockoutElements();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Plan Preview", EditorStyles.boldLabel);

                if (GUILayout.Button("Frame Plan", GUILayout.Width(88f)))
                {
                    planFrameRequested = true;
                }

                if (GUILayout.Button("-", GUILayout.Width(26f)))
                {
                    planViewPixelsPerMeter = Mathf.Max(6f, planViewPixelsPerMeter * 0.85f);
                }

                if (GUILayout.Button("+", GUILayout.Width(26f)))
                {
                    planViewPixelsPerMeter = Mathf.Min(96f, planViewPixelsPerMeter * 1.15f);
                }
            }

            Rect rect = GUILayoutUtility.GetRect(240f, 340f, GUILayout.ExpandWidth(true));
            if (planFrameRequested)
            {
                FramePlanView(rect, elements);
                planFrameRequested = false;
            }

            HandlePlanPreviewInput(rect, elements);
            DrawPlanPreviewSurface(rect, elements);
        }

        private void DrawPlanPreviewSurface(Rect rect, BlockoutElement[] elements)
        {
            GUI.BeginGroup(rect);
            Rect localRect = new Rect(0f, 0f, rect.width, rect.height);

            EditorGUI.DrawRect(localRect, new Color(0.13f, 0.15f, 0.17f, 1f));
            GUI.Box(localRect, GUIContent.none);

            Handles.BeginGUI();
            DrawPlanGrid(localRect);
            DrawPlanElements(localRect, elements);
            Handles.EndGUI();

            Rect hintRect = new Rect(8f, 8f, 430f, 38f);
            GUI.Box(hintRect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(hintRect.x + 8f, hintRect.y + 3f, hintRect.width - 16f, 16f), "Top-down layout. LMB select/drag, MMB pan, wheel zoom.");
            GUI.Label(new Rect(hintRect.x + 8f, hintRect.y + 19f, hintRect.width - 16f, 16f), "Drag changes Scene X/Z and keeps the current height.");

            GUI.EndGroup();
        }

        private void DrawPlanGrid(Rect rect)
        {
            Vector2 worldMin = PlanToWorldXZ(rect, new Vector2(rect.xMin, rect.yMax));
            Vector2 worldMax = PlanToWorldXZ(rect, new Vector2(rect.xMax, rect.yMin));
            float gridWorldStep = Mathf.Max(0.5f, gridSize);
            while (gridWorldStep * planViewPixelsPerMeter < 18f)
            {
                gridWorldStep *= 2f;
            }

            Handles.color = new Color(0.24f, 0.27f, 0.30f, 1f);
            float startX = Mathf.Floor(worldMin.x / gridWorldStep) * gridWorldStep;
            float endX = Mathf.Ceil(worldMax.x / gridWorldStep) * gridWorldStep;
            for (float x = startX; x <= endX; x += gridWorldStep)
            {
                float screenX = WorldToPlan(rect, new Vector3(x, 0f, 0f)).x;
                Handles.DrawLine(new Vector3(screenX, rect.yMin), new Vector3(screenX, rect.yMax));
            }

            float startZ = Mathf.Floor(worldMin.y / gridWorldStep) * gridWorldStep;
            float endZ = Mathf.Ceil(worldMax.y / gridWorldStep) * gridWorldStep;
            for (float z = startZ; z <= endZ; z += gridWorldStep)
            {
                float screenY = WorldToPlan(rect, new Vector3(0f, 0f, z)).y;
                Handles.DrawLine(new Vector3(rect.xMin, screenY), new Vector3(rect.xMax, screenY));
            }

            Handles.color = new Color(0.42f, 0.48f, 0.54f, 1f);
            Vector2 origin = WorldToPlan(rect, Vector3.zero);
            Handles.DrawLine(new Vector3(rect.xMin, origin.y), new Vector3(rect.xMax, origin.y));
            Handles.DrawLine(new Vector3(origin.x, rect.yMin), new Vector3(origin.x, rect.yMax));
        }

        private void DrawPlanElements(Rect rect, BlockoutElement[] elements)
        {
            BlockoutElement selected = SelectedBlockoutElement();
            foreach (BlockoutElement element in elements)
            {
                if (element == null)
                {
                    continue;
                }

                Rect elementRect = PlanElementRect(rect, element);
                if (!elementRect.Overlaps(rect))
                {
                    continue;
                }

                Color color = ActToolkitEditorUtilities.ColorFor(element.kind);
                bool isSelected = element == selected;
                EditorGUI.DrawRect(elementRect, new Color(color.r, color.g, color.b, IsTriggerLikeKind(element.kind) ? 0.26f : 0.50f));

                Handles.color = isSelected ? new Color(1f, 0.86f, 0.20f, 1f) : new Color(color.r, color.g, color.b, 0.95f);
                DrawPlanRectOutline(elementRect, isSelected ? 3f : 1.5f);

                if (IsPointMarkerKind(element.kind))
                {
                    Vector2 center = elementRect.center;
                    Handles.DrawLine(new Vector3(center.x, center.y - 7f), new Vector3(center.x, center.y + 7f));
                    Handles.DrawLine(new Vector3(center.x - 7f, center.y), new Vector3(center.x + 7f, center.y));
                }

                if (elementRect.width > 46f && elementRect.height > 18f)
                {
                    GUI.Label(new Rect(elementRect.x + 4f, elementRect.y + 2f, elementRect.width - 8f, 18f), ShortElementLabel(element), EditorStyles.miniLabel);
                }
            }
        }

        private static void DrawPlanRectOutline(Rect rect, float width)
        {
            Handles.DrawAAPolyLine(width,
                new Vector3(rect.xMin, rect.yMin),
                new Vector3(rect.xMax, rect.yMin),
                new Vector3(rect.xMax, rect.yMax),
                new Vector3(rect.xMin, rect.yMax),
                new Vector3(rect.xMin, rect.yMin));
        }

        private void HandlePlanPreviewInput(Rect rect, BlockoutElement[] elements)
        {
            Event current = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive, rect);
            EventType eventType = current.GetTypeForControl(controlId);

            if (eventType == EventType.ScrollWheel && rect.Contains(current.mousePosition))
            {
                Vector2 before = PlanToWorldXZ(rect, current.mousePosition);
                float zoomFactor = current.delta.y > 0f ? 0.9f : 1.1f;
                planViewPixelsPerMeter = Mathf.Clamp(planViewPixelsPerMeter * zoomFactor, 6f, 96f);
                Vector2 after = PlanToWorldXZ(rect, current.mousePosition);
                planViewCenter += before - after;
                current.Use();
                Repaint();
                return;
            }

            if (eventType == EventType.MouseDown && rect.Contains(current.mousePosition))
            {
                if (current.button == 0)
                {
                    BlockoutElement hit = HitTestPlanElement(rect, elements, current.mousePosition);
                    if (hit != null)
                    {
                        GUIUtility.hotControl = controlId;
                        planDraggedElement = hit;
                        Vector2 mouseWorld = PlanToWorldXZ(rect, current.mousePosition);
                        planDragOffset = new Vector2(hit.transform.position.x, hit.transform.position.z) - mouseWorld;
                        Undo.RecordObject(hit.transform, "Move Blockout Element In Plan");
                        Selection.activeGameObject = hit.gameObject;
                        current.Use();
                        Repaint();
                    }
                    else
                    {
                        Selection.activeGameObject = null;
                        current.Use();
                        Repaint();
                    }

                    return;
                }

                if (current.button == 2)
                {
                    GUIUtility.hotControl = controlId;
                    planIsPanning = true;
                    planPanStartMouse = current.mousePosition;
                    planPanStartCenter = planViewCenter;
                    current.Use();
                    return;
                }
            }

            if (eventType == EventType.MouseDrag && GUIUtility.hotControl == controlId)
            {
                if (planDraggedElement != null && current.button == 0)
                {
                    DragPlanElement(rect, current.mousePosition);
                    current.Use();
                    return;
                }

                if (planIsPanning && current.button == 2)
                {
                    Vector2 delta = current.mousePosition - planPanStartMouse;
                    planViewCenter = planPanStartCenter + new Vector2(-delta.x / planViewPixelsPerMeter, delta.y / planViewPixelsPerMeter);
                    current.Use();
                    Repaint();
                    return;
                }
            }

            if (eventType == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                planDraggedElement = null;
                planIsPanning = false;
                GUIUtility.hotControl = 0;
                current.Use();
            }
        }

        private void DragPlanElement(Rect rect, Vector2 mousePosition)
        {
            if (planDraggedElement == null)
            {
                return;
            }

            Vector2 nextWorld = PlanToWorldXZ(rect, mousePosition) + planDragOffset;
            if (snapSceneDrag)
            {
                nextWorld.x = Mathf.Round(nextWorld.x / gridSize) * gridSize;
                nextWorld.y = Mathf.Round(nextWorld.y / gridSize) * gridSize;
            }

            Transform transform = planDraggedElement.transform;
            transform.position = new Vector3(nextWorld.x, transform.position.y, nextWorld.y);
            EditorSceneManager.MarkSceneDirty(planDraggedElement.gameObject.scene);
            SceneView.RepaintAll();
            Repaint();
        }

        private BlockoutElement HitTestPlanElement(Rect rect, BlockoutElement[] elements, Vector2 mousePosition)
        {
            BlockoutElement best = null;
            float bestArea = float.PositiveInfinity;

            foreach (BlockoutElement element in elements)
            {
                if (element == null)
                {
                    continue;
                }

                Rect elementRect = PlanElementRect(rect, element);
                if (!elementRect.Contains(mousePosition))
                {
                    continue;
                }

                float area = elementRect.width * elementRect.height;
                if (area < bestArea)
                {
                    bestArea = area;
                    best = element;
                }
            }

            return best;
        }

        private Rect PlanElementRect(Rect rect, BlockoutElement element)
        {
            Vector2 center = WorldToPlan(rect, element.transform.position);
            Vector2 sizeXZ = IsPointMarkerKind(element.kind)
                ? new Vector2(16f, 16f)
                : new Vector2(
                    Mathf.Max(0.35f, element.logicalSize.x) * planViewPixelsPerMeter,
                    Mathf.Max(0.35f, element.logicalSize.z) * planViewPixelsPerMeter);

            return new Rect(center.x - sizeXZ.x * 0.5f, center.y - sizeXZ.y * 0.5f, sizeXZ.x, sizeXZ.y);
        }

        private Vector2 WorldToPlan(Rect rect, Vector3 worldPosition)
        {
            return new Vector2(
                rect.center.x + (worldPosition.x - planViewCenter.x) * planViewPixelsPerMeter,
                rect.center.y - (worldPosition.z - planViewCenter.y) * planViewPixelsPerMeter);
        }

        private Vector2 PlanToWorldXZ(Rect rect, Vector2 planPosition)
        {
            return new Vector2(
                planViewCenter.x + (planPosition.x - rect.center.x) / planViewPixelsPerMeter,
                planViewCenter.y - (planPosition.y - rect.center.y) / planViewPixelsPerMeter);
        }

        private void FramePlanView(Rect rect, BlockoutElement[] elements)
        {
            if (elements.Length == 0)
            {
                planViewCenter = Vector2.zero;
                planViewPixelsPerMeter = 24f;
                return;
            }

            bool hasBounds = false;
            Vector2 min = Vector2.zero;
            Vector2 max = Vector2.zero;

            foreach (BlockoutElement element in elements)
            {
                if (element == null)
                {
                    continue;
                }

                Vector2 half = new Vector2(
                    Mathf.Max(0.5f, element.logicalSize.x * 0.5f),
                    Mathf.Max(0.5f, element.logicalSize.z * 0.5f));
                Vector2 center = new Vector2(element.transform.position.x, element.transform.position.z);
                Vector2 elementMin = center - half;
                Vector2 elementMax = center + half;

                if (!hasBounds)
                {
                    min = elementMin;
                    max = elementMax;
                    hasBounds = true;
                }
                else
                {
                    min = Vector2.Min(min, elementMin);
                    max = Vector2.Max(max, elementMax);
                }
            }

            if (!hasBounds)
            {
                return;
            }

            Vector2 extent = max - min;
            planViewCenter = (min + max) * 0.5f;
            float pixelsX = rect.width / Mathf.Max(1f, extent.x + 4f);
            float pixelsY = rect.height / Mathf.Max(1f, extent.y + 4f);
            planViewPixelsPerMeter = Mathf.Clamp(Mathf.Min(pixelsX, pixelsY), 6f, 72f);
        }

        private static string ShortElementLabel(BlockoutElement element)
        {
            if (element == null)
            {
                return string.Empty;
            }

            string label = string.IsNullOrWhiteSpace(element.name) ? DisplayName(element.kind) : element.name;
            return label.Length <= 24 ? label : label.Substring(0, 21) + "...";
        }

        private void DrawElementList()
        {
            EditorGUILayout.LabelField("Current Level Elements", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                BlockoutElement[] elements = FindSceneBlockoutElements();
                if (elements.Length == 0)
                {
                    EditorGUILayout.LabelField("No blockout elements in the current scene.");
                    return;
                }

                using (EditorGUILayout.ScrollViewScope elementScroll = new EditorGUILayout.ScrollViewScope(elementListScrollPosition, GUILayout.MinHeight(220f)))
                {
                    elementListScrollPosition = elementScroll.scrollPosition;
                    foreach (BlockoutElement element in elements)
                    {
                        if (element == null)
                        {
                            continue;
                        }

                        DrawElementRow(element);
                    }
                }
            }
        }

        private static void DrawElementRow(BlockoutElement element)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(DisplayName(element.kind), GUILayout.Width(104f));

                if (GUILayout.Button(element.name, EditorStyles.miniButtonLeft))
                {
                    Selection.activeGameObject = element.gameObject;
                    FrameObject(element.gameObject);
                }

                if (GUILayout.Button("Frame", EditorStyles.miniButtonMid, GUILayout.Width(54f)))
                {
                    FrameObject(element.gameObject);
                }

                if (GUILayout.Button("X", EditorStyles.miniButtonRight, GUILayout.Width(26f)))
                {
                    DeleteElement(element);
                }
            }
        }

        private void DrawRightInspectorPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(360f), GUILayout.ExpandHeight(true)))
            {
                using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(rightScrollPosition))
                {
                    rightScrollPosition = scroll.scrollPosition;
                    DrawSelectedElementInspector();

                    if (workspaceMode == LevelWorkspaceMode.Validate)
                    {
                        EditorGUILayout.Space(10f);
                        DrawValidationResults();
                    }
                }
            }
        }

        private void DrawSelectedElementInspector()
        {
            BlockoutElement selected = SelectedBlockoutElement();

            EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (selected == null)
                {
                    EditorGUILayout.LabelField("No element selected.");
                    return;
                }

                EditorGUI.BeginChangeCheck();
                string nextName = EditorGUILayout.TextField("Name", selected.gameObject.name);
                BlockoutElementKind nextKind = (BlockoutElementKind)EditorGUILayout.EnumPopup("Kind", selected.kind);
                string nextTag = EditorGUILayout.TextField("Gameplay Tag", selected.gameplayTag);
                int nextTeam = EditorGUILayout.IntField("Team", selected.team);
                bool nextServerCollision = EditorGUILayout.Toggle("Server Collision", selected.serverAuthoritativeCollision);
                Vector3 nextSize = EditorGUILayout.Vector3Field("Logical Size", selected.logicalSize);
                Vector3 nextPosition = EditorGUILayout.Vector3Field("Position", selected.transform.position);
                Vector3 nextEuler = EditorGUILayout.Vector3Field("Rotation", selected.transform.rotation.eulerAngles);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(selected, "Edit Blockout Element");
                    Undo.RecordObject(selected.transform, "Edit Blockout Element");
                    selected.gameObject.name = string.IsNullOrWhiteSpace(nextName) ? "Blockout_" + nextKind : nextName;
                    selected.kind = nextKind;
                    selected.gameplayTag = nextTag;
                    selected.team = nextTeam;
                    selected.serverAuthoritativeCollision = nextServerCollision;
                    selected.logicalSize = SanitizedSize(nextKind, nextSize);
                    selected.transform.localScale = selected.logicalSize;
                    selected.transform.position = snapSceneDrag ? ActToolkitEditorUtilities.Snap(nextPosition, gridSize) : nextPosition;
                    selected.transform.rotation = Quaternion.Euler(nextEuler);
                    selected.gizmoColor = ActToolkitEditorUtilities.ColorFor(nextKind);
                    ApplyElementVisuals(selected);
                    EditorUtility.SetDirty(selected);
                    EditorSceneManager.MarkSceneDirty(selected.gameObject.scene);
                    SceneView.RepaintAll();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Frame", GUILayout.Height(26f)))
                    {
                        FrameObject(selected.gameObject);
                    }

                    if (GUILayout.Button("Duplicate", GUILayout.Height(26f)))
                    {
                        DuplicateElement(selected, selected.transform.position + Vector3.right * Mathf.Max(gridSize, 1f));
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Ground", GUILayout.Height(26f)))
                    {
                        GroundElement(selected);
                    }

                    if (GUILayout.Button("Reset Size", GUILayout.Height(26f)))
                    {
                        ResetElementSize(selected);
                    }

                    if (GUILayout.Button("Delete", GUILayout.Height(26f)))
                    {
                        DeleteElement(selected);
                    }
                }
            }
        }

        private void DrawValidationSummary()
        {
            int errorCount;
            int warningCount;
            CountValidationIssues(out errorCount, out warningCount);

            EditorGUILayout.LabelField("Errors", errorCount.ToString());
            EditorGUILayout.LabelField("Warnings", warningCount.ToString());

            if (validationIssues.Count == 0)
            {
                EditorGUILayout.LabelField("No validation results yet.");
            }
        }

        private void DrawValidationResults()
        {
            EditorGUILayout.LabelField("Validation Results", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (NeedsValidationRefresh())
                {
                    RunLevelValidation();
                }

                if (validationIssues.Count == 0)
                {
                    EditorGUILayout.LabelField("No issues found.");
                    return;
                }

                foreach (ValidationIssue issue in validationIssues)
                {
                    DrawValidationIssue(issue);
                }
            }
        }

        private static void DrawValidationIssue(ValidationIssue issue)
        {
            Color previousColor = GUI.backgroundColor;
            GUI.backgroundColor = SeverityColor(issue.severity);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUI.backgroundColor = previousColor;
                EditorGUILayout.LabelField(issue.severity.ToString(), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(issue.message, EditorStyles.wordWrappedLabel);

                using (new EditorGUI.DisabledScope(issue.element == null))
                {
                    if (GUILayout.Button("Select", GUILayout.Height(24f)))
                    {
                        FrameObject(issue.element.gameObject);
                    }
                }
            }

            GUI.backgroundColor = previousColor;
        }

        private void DrawStatusBar()
        {
            int errorCount;
            int warningCount;
            CountValidationIssues(out errorCount, out warningCount);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(24f)))
            {
                GUILayout.Label("Scene editing: " + (sceneEditingEnabled ? "On" : "Off"), EditorStyles.miniLabel, GUILayout.Width(120f));
                GUILayout.Label("Tool: " + sceneEditTool, EditorStyles.miniLabel, GUILayout.Width(90f));
                GUILayout.Label("Grid: " + gridSize.ToString("0.##") + "m", EditorStyles.miniLabel, GUILayout.Width(90f));
                GUILayout.Label("Snap: " + (snapSceneDrag ? "On" : "Off"), EditorStyles.miniLabel, GUILayout.Width(90f));
                GUILayout.FlexibleSpace();
                GUILayout.Label("Validation: " + errorCount + " errors, " + warningCount + " warnings", EditorStyles.miniLabel, GUILayout.Width(210f));
            }
        }

        private void DuringSceneGui(SceneView sceneView)
        {
            if (!sceneEditingEnabled)
            {
                return;
            }

            Event current = Event.current;
            if (current == null)
            {
                return;
            }

            UpdateSceneHoverPoint(current.mousePosition);
            DrawSceneOverlay();
            DrawPlacementPreview();
            if (sceneEditTool == SceneEditTool.Select)
            {
                DrawSelectedElementHandles();
            }

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (current.type == EventType.Layout && !current.alt)
            {
                HandleUtility.AddDefaultControl(controlId);
            }

            HandleSceneKeyboard(current);
            HandleSceneMouse(current);
        }

        private void HandleSceneMouse(Event current)
        {
            if (current.alt)
            {
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 1)
            {
                ShowSceneContextMenu(current.mousePosition);
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                if (sceneEditTool == SceneEditTool.Place && CanCreateFromCurrentMode())
                {
                    if (createOnEmptyLeftClick && hasSceneHoverPoint)
                    {
                        CreateElement(sceneHoverPoint, true);
                        current.Use();
                    }

                    return;
                }

                BlockoutElement picked = PickBlockoutElement(current.mousePosition);
                if (picked != null)
                {
                    if (current.control)
                    {
                        GameObject copy = DuplicateElement(picked, picked.transform.position + Vector3.right * Mathf.Max(gridSize, 1f));
                        BeginDrag(copy == null ? picked : copy.GetComponent<BlockoutElement>(), current.mousePosition);
                    }
                    else
                    {
                        Selection.activeGameObject = picked.gameObject;
                        BeginDrag(picked, current.mousePosition);
                    }

                    current.Use();
                    return;
                }

                draggedElement = null;
            }

            if (current.type == EventType.MouseDrag && draggedElement != null && current.button == 0)
            {
                DragSelectedElement(current.mousePosition);
                current.Use();
            }

            if (current.type == EventType.MouseUp && draggedElement != null)
            {
                draggedElement = null;
                current.Use();
            }
        }

        private void HandleSceneKeyboard(Event current)
        {
            if (current.type != EventType.KeyDown || current.alt)
            {
                return;
            }

            BlockoutElement selected = SelectedBlockoutElement();
            if (selected != null && (current.keyCode == KeyCode.Delete || current.keyCode == KeyCode.Backspace))
            {
                DeleteElement(selected);
                current.Use();
                return;
            }

            if (selected != null && current.control && current.keyCode == KeyCode.D)
            {
                DuplicateElement(selected, selected.transform.position + Vector3.right * Mathf.Max(gridSize, 1f));
                current.Use();
                return;
            }

            if (selected != null && current.keyCode == KeyCode.F)
            {
                FrameObject(selected.gameObject);
                current.Use();
                return;
            }

            if (current.keyCode == KeyCode.Escape || current.keyCode == KeyCode.Q)
            {
                sceneEditTool = SceneEditTool.Select;
                draggedElement = null;
                SceneView.RepaintAll();
                Repaint();
                current.Use();
                return;
            }

            if (!current.control && !current.shift)
            {
                if (TrySetBrushFromHotkey(current.keyCode))
                {
                    current.Use();
                }
            }
        }

        private bool TrySetBrushFromHotkey(KeyCode keyCode)
        {
            if (workspaceMode == LevelWorkspaceMode.Blockout)
            {
                switch (keyCode)
                {
                    case KeyCode.Alpha1:
                        SetActiveBrush(BlockoutElementKind.Floor);
                        return true;
                    case KeyCode.Alpha2:
                        SetActiveBrush(BlockoutElementKind.Wall);
                        return true;
                    case KeyCode.Alpha3:
                        SetActiveBrush(BlockoutElementKind.Block);
                        return true;
                    case KeyCode.Alpha4:
                        SetActiveBrush(BlockoutElementKind.Ramp);
                        return true;
                    case KeyCode.Alpha5:
                        SetActiveBrush(BlockoutElementKind.Platform);
                        return true;
                    case KeyCode.Alpha6:
                        SetActiveBrush(BlockoutElementKind.Cover);
                        return true;
                }
            }

            if (workspaceMode == LevelWorkspaceMode.Gameplay)
            {
                switch (keyCode)
                {
                    case KeyCode.Alpha1:
                        SetActiveBrush(BlockoutElementKind.SpawnPoint);
                        return true;
                    case KeyCode.Alpha2:
                        SetActiveBrush(BlockoutElementKind.EnemySpawn);
                        return true;
                    case KeyCode.Alpha3:
                        SetActiveBrush(BlockoutElementKind.DummySpawn);
                        return true;
                    case KeyCode.Alpha4:
                        SetActiveBrush(BlockoutElementKind.Objective);
                        return true;
                    case KeyCode.Alpha5:
                        SetActiveBrush(BlockoutElementKind.CombatZone);
                        return true;
                    case KeyCode.Alpha6:
                        SetActiveBrush(BlockoutElementKind.TriggerVolume);
                        return true;
                }
            }

            return false;
        }

        private void BeginDrag(BlockoutElement element, Vector2 mousePosition)
        {
            if (element == null || !TryGetGroundPoint(mousePosition, out Vector3 groundPoint))
            {
                return;
            }

            draggedElement = element;
            dragOffset = element.transform.position - groundPoint;
            Undo.RecordObject(element.transform, "Move Blockout Element");
        }

        private void DragSelectedElement(Vector2 mousePosition)
        {
            if (draggedElement == null || !TryGetGroundPoint(mousePosition, out Vector3 groundPoint))
            {
                return;
            }

            Vector3 nextPosition = groundPoint + dragOffset;
            if (snapSceneDrag)
            {
                nextPosition = ActToolkitEditorUtilities.Snap(nextPosition, gridSize);
            }

            draggedElement.transform.position = nextPosition;
            EditorSceneManager.MarkSceneDirty(draggedElement.gameObject.scene);
            SceneView.RepaintAll();
        }

        private void DrawSceneOverlay()
        {
            Handles.BeginGUI();

            Rect rect = new Rect(12f, 12f, 430f, 112f);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 18f), "Level Workspace | " + workspaceMode, EditorStyles.boldLabel);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 30f, rect.width - 20f, 18f), "Tool: " + sceneEditTool + " | Brush: " + DisplayName(kind) + " | Grid " + gridSize.ToString("0.##") + "m");
            GUI.Label(new Rect(rect.x + 10f, rect.y + 52f, rect.width - 20f, 18f), "Place: LMB puts brush on the hovered surface. Select: LMB selects/drags.");
            GUI.Label(new Rect(rect.x + 10f, rect.y + 74f, rect.width - 20f, 18f), "RMB context, Q/Esc select, Del delete, Ctrl+D duplicate, F frame, Alt camera");

            Handles.EndGUI();
        }

        private void DrawPlacementPreview()
        {
            if (sceneEditTool != SceneEditTool.Place || !showPlacementPreview || !hasSceneHoverPoint || draggedElement != null || !CanCreateFromCurrentMode())
            {
                return;
            }

            Color color = ActToolkitEditorUtilities.ColorFor(kind);
            Handles.color = new Color(color.r, color.g, color.b, 0.85f);
            Vector3 previewSize = SanitizedSize(kind, size);
            Matrix4x4 previousMatrix = Handles.matrix;
            Handles.matrix = Matrix4x4.TRS(sceneHoverPoint, DefaultRotation(kind), previewSize);

            if (IsPointMarkerKind(kind))
            {
                Handles.SphereHandleCap(0, Vector3.zero, Quaternion.identity, 0.7f, EventType.Repaint);
                Handles.DrawLine(Vector3.zero, Vector3.forward * 1.2f);
            }
            else
            {
                Handles.DrawWireCube(Vector3.zero, Vector3.one);
            }

            Handles.matrix = previousMatrix;
        }

        private void DrawSelectedElementHandles()
        {
            BlockoutElement selected = SelectedBlockoutElement();
            if (selected == null)
            {
                return;
            }

            Transform transform = selected.transform;

            EditorGUI.BeginChangeCheck();
            Vector3 nextPosition = Handles.PositionHandle(transform.position, transform.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(transform, "Move Blockout Element");
                transform.position = snapSceneDrag ? ActToolkitEditorUtilities.Snap(nextPosition, gridSize) : nextPosition;
                EditorSceneManager.MarkSceneDirty(selected.gameObject.scene);
            }

            if (IsPointMarkerKind(selected.kind))
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            Vector3 nextScale = Handles.ScaleHandle(transform.localScale, transform.position, transform.rotation, HandleUtility.GetHandleSize(transform.position));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(transform, "Resize Blockout Element");
                Undo.RecordObject(selected, "Resize Blockout Element");
                selected.logicalSize = SanitizedSize(selected.kind, nextScale);
                transform.localScale = selected.logicalSize;
                EditorUtility.SetDirty(selected);
                EditorSceneManager.MarkSceneDirty(selected.gameObject.scene);
            }
        }

        private void ShowSceneContextMenu(Vector2 mousePosition)
        {
            Vector3 menuSurfacePoint = hasSceneHoverPoint ? sceneHoverSurfacePoint : ActToolkitEditorUtilities.SceneViewGroundPoint(gridSize);
            Vector3 menuSurfaceNormal = hasSceneHoverPoint ? sceneHoverSurfaceNormal : Vector3.up;
            Vector3 menuPoint = PlacementCenter(kind, menuSurfacePoint, menuSurfaceNormal, SanitizedSize(kind, size));
            BlockoutElement picked = PickBlockoutElement(mousePosition);
            if (picked != null)
            {
                Selection.activeGameObject = picked.gameObject;
            }

            BlockoutElement selected = SelectedBlockoutElement();
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Create/" + DisplayName(kind) + " Here"), false, () => CreateElement(menuPoint, true));
            menu.AddSeparator("");

            AddCreateSubmenu(menu, "Create Blockout", new[]
            {
                BlockoutElementKind.Floor,
                BlockoutElementKind.Wall,
                BlockoutElementKind.Block,
                BlockoutElementKind.Ramp,
                BlockoutElementKind.Platform,
                BlockoutElementKind.Cover
            }, menuSurfacePoint, menuSurfaceNormal);

            AddCreateSubmenu(menu, "Create Gameplay", new[]
            {
                BlockoutElementKind.SpawnPoint,
                BlockoutElementKind.EnemySpawn,
                BlockoutElementKind.DummySpawn,
                BlockoutElementKind.Objective,
                BlockoutElementKind.CombatZone,
                BlockoutElementKind.TriggerVolume,
                BlockoutElementKind.KillZone,
                BlockoutElementKind.NavMarker
            }, menuSurfacePoint, menuSurfaceNormal);

            menu.AddSeparator("");
            if (selected == null)
            {
                menu.AddDisabledItem(new GUIContent("Duplicate Selected Here"));
                menu.AddDisabledItem(new GUIContent("Delete Selected"));
                menu.AddDisabledItem(new GUIContent("Frame Selected"));
            }
            else
            {
                Vector3 duplicatePoint = PlacementCenter(selected.kind, menuSurfacePoint, menuSurfaceNormal, selected.logicalSize);
                menu.AddItem(new GUIContent("Duplicate Selected Here"), false, () => DuplicateElement(selected, duplicatePoint));
                menu.AddItem(new GUIContent("Delete Selected"), false, () => DeleteElement(selected));
                menu.AddItem(new GUIContent("Frame Selected"), false, () => FrameObject(selected.gameObject));
                menu.AddSeparator("");

                foreach (BlockoutElementKind menuKind in Enum.GetValues(typeof(BlockoutElementKind)))
                {
                    BlockoutElementKind capturedKind = menuKind;
                    menu.AddItem(new GUIContent("Set Selected Kind/" + DisplayName(capturedKind)), selected.kind == capturedKind, () => SetElementKind(selected, capturedKind));
                }
            }

            menu.ShowAsContext();
        }

        private void AddCreateSubmenu(GenericMenu menu, string root, BlockoutElementKind[] kinds, Vector3 surfacePoint, Vector3 surfaceNormal)
        {
            foreach (BlockoutElementKind menuKind in kinds)
            {
                BlockoutElementKind capturedKind = menuKind;
                menu.AddItem(new GUIContent(root + "/" + DisplayName(capturedKind)), false, () =>
                {
                    BlockoutElementKind previousKind = kind;
                    Vector3 previousSize = size;
                    string previousTag = gameplayTag;
                    kind = capturedKind;
                    size = DefaultSize(capturedKind);
                    gameplayTag = DefaultGameplayTag(capturedKind);
                    Vector3 position = PlacementCenter(capturedKind, surfacePoint, surfaceNormal, SanitizedSize(capturedKind, size));
                    CreateElement(position, true);
                    kind = previousKind;
                    size = previousSize;
                    gameplayTag = previousTag;
                });
            }
        }

        private GameObject CreateElement(Vector3 position, bool preservePosition = false)
        {
            ActToolkitEditorUtilities.EnsureGeneratedFolders();

            PrimitiveType primitive = IsPointMarkerKind(kind) ? PrimitiveType.Capsule : PrimitiveType.Cube;
            GameObject go = GameObject.CreatePrimitive(primitive);
            Undo.RegisterCreatedObjectUndo(go, "Create Blockout Element");

            Vector3 elementSize = SanitizedSize(kind, size);
            go.name = DefaultObjectName(kind);
            go.transform.position = preservePosition ? position : GroundedPlacementPosition(kind, position, elementSize);
            go.transform.rotation = DefaultRotation(kind);
            go.transform.localScale = elementSize;

            Transform targetParent = parent != null ? parent : EnsureBlockoutRoot();
            if (targetParent != null)
            {
                go.transform.SetParent(targetParent, true);
            }

            BlockoutElement element = go.AddComponent<BlockoutElement>();
            element.kind = kind;
            element.gameplayTag = string.IsNullOrWhiteSpace(gameplayTag) ? DefaultGameplayTag(kind) : gameplayTag;
            element.team = team;
            element.logicalSize = go.transform.localScale;
            element.gizmoColor = ActToolkitEditorUtilities.ColorFor(kind);
            element.EnsureId();
            ApplyElementVisuals(element);

            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Repaint();
            return go;
        }

        private void SetActiveBrush(BlockoutElementKind nextKind)
        {
            kind = nextKind;
            size = DefaultSize(nextKind);
            gameplayTag = DefaultGameplayTag(nextKind);
            sceneEditTool = SceneEditTool.Place;
            if (IsGameplayKind(nextKind) && workspaceMode != LevelWorkspaceMode.Gameplay)
            {
                workspaceMode = LevelWorkspaceMode.Gameplay;
            }
            else if (IsBlockoutKind(nextKind) && workspaceMode != LevelWorkspaceMode.Blockout)
            {
                workspaceMode = LevelWorkspaceMode.Blockout;
            }

            Repaint();
            SceneView.RepaintAll();
        }

        private static void SetElementKind(BlockoutElement element, BlockoutElementKind nextKind)
        {
            if (element == null)
            {
                return;
            }

            Undo.RecordObject(element, "Set Blockout Kind");
            Undo.RecordObject(element.transform, "Set Blockout Kind");
            element.kind = nextKind;
            element.gizmoColor = ActToolkitEditorUtilities.ColorFor(nextKind);
            element.logicalSize = SanitizedSize(nextKind, element.logicalSize);
            element.transform.localScale = element.logicalSize;
            ApplyElementVisuals(element);
            EditorUtility.SetDirty(element);
            EditorSceneManager.MarkSceneDirty(element.gameObject.scene);
        }

        private static GameObject DuplicateElement(BlockoutElement source, Vector3 position)
        {
            if (source == null)
            {
                return null;
            }

            GameObject copy = Instantiate(source.gameObject, position, source.transform.rotation, source.transform.parent);
            copy.name = source.gameObject.name + "_Copy";
            Undo.RegisterCreatedObjectUndo(copy, "Duplicate Blockout Element");

            BlockoutElement copyElement = copy.GetComponent<BlockoutElement>();
            if (copyElement != null)
            {
                copyElement.elementId = string.Empty;
                copyElement.EnsureId();
                ApplyElementVisuals(copyElement);
                EditorUtility.SetDirty(copyElement);
            }

            Selection.activeGameObject = copy;
            EditorSceneManager.MarkSceneDirty(copy.scene);
            return copy;
        }

        private static void DeleteElement(BlockoutElement element)
        {
            if (element == null)
            {
                return;
            }

            GameObject go = element.gameObject;
            Scene scene = go.scene;
            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void GroundElement(BlockoutElement element)
        {
            if (element == null)
            {
                return;
            }

            Undo.RecordObject(element.transform, "Ground Blockout Element");
            Vector3 surfacePoint = element.transform.position;
            surfacePoint.y = 0f;
            element.transform.position = PlacementCenter(element.kind, surfacePoint, Vector3.up, element.logicalSize);
            EditorSceneManager.MarkSceneDirty(element.gameObject.scene);
        }

        private static void ResetElementSize(BlockoutElement element)
        {
            if (element == null)
            {
                return;
            }

            Undo.RecordObject(element, "Reset Blockout Element Size");
            Undo.RecordObject(element.transform, "Reset Blockout Element Size");
            element.logicalSize = DefaultSize(element.kind);
            element.transform.localScale = element.logicalSize;
            EditorUtility.SetDirty(element);
            EditorSceneManager.MarkSceneDirty(element.gameObject.scene);
        }

        private static void ApplyElementVisuals(BlockoutElement element)
        {
            if (element == null)
            {
                return;
            }

            Renderer renderer = element.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = ActToolkitEditorUtilities.GetOrCreateBlockoutMaterial(element.kind, ActToolkitEditorUtilities.ColorFor(element.kind));
            }

            Collider collider = element.GetComponent<Collider>();
            if (collider != null)
            {
                bool isTrigger = IsTriggerLikeKind(element.kind) || IsPointMarkerKind(element.kind);
                collider.isTrigger = isTrigger;
                if (isTrigger)
                {
                    element.serverAuthoritativeCollision = false;
                }
            }
        }

        private static void RefreshActiveSceneBlockoutVisuals()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            BlockoutElement[] elements = FindObjectsByType<BlockoutElement>(FindObjectsInactive.Include);
            foreach (BlockoutElement element in elements)
            {
                if (element == null || element.gameObject.scene != activeScene)
                {
                    continue;
                }

                ApplyElementVisuals(element);
                EditorUtility.SetDirty(element);
            }
        }

        private static void ApplyNeutralPlaytestLightingPreset()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            Light keyLight = FindOrCreateLevelKeyLight(activeScene);
            if (keyLight != null)
            {
                ConfigureLevelKeyLight(keyLight);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = LevelAmbientColor;

            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include);
            foreach (Camera camera in cameras)
            {
                if (camera == null || camera.gameObject.scene != activeScene)
                {
                    continue;
                }

                ConfigureLevelCamera(camera);
            }

            EditorSceneManager.MarkSceneDirty(activeScene);
        }

        private static Light FindOrCreateLevelKeyLight(Scene activeScene)
        {
            Light fallbackDirectional = null;
            Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Include);
            foreach (Light light in lights)
            {
                if (light == null || light.gameObject.scene != activeScene || light.type != LightType.Directional)
                {
                    continue;
                }

                if (light.name == "Level Key Light")
                {
                    return light;
                }

                fallbackDirectional ??= light;
            }

            if (fallbackDirectional != null)
            {
                return fallbackDirectional;
            }

            GameObject lightObject = new GameObject("Level Key Light");
            Undo.RegisterCreatedObjectUndo(lightObject, "Create Level Key Light");
            Light createdLight = lightObject.AddComponent<Light>();
            createdLight.type = LightType.Directional;
            lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            return createdLight;
        }

        private static void ConfigureLevelKeyLight(Light light)
        {
            light.type = LightType.Directional;
            light.intensity = LevelKeyLightIntensity;
            light.color = LevelKeyLightColor;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.35f;
            EditorUtility.SetDirty(light);
        }

        private static void ConfigureLevelCamera(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = LevelCameraBackgroundColor;
            camera.allowHDR = false;

            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;
            cameraData.allowHDROutput = false;
            cameraData.stopNaN = true;
            cameraData.dithering = false;
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(cameraData);
        }

        private static Vector3 SanitizedSize(BlockoutElementKind elementKind, Vector3 requestedSize)
        {
            Vector3 safe = new Vector3(
                Mathf.Max(0.05f, Mathf.Abs(requestedSize.x)),
                Mathf.Max(0.05f, Mathf.Abs(requestedSize.y)),
                Mathf.Max(0.05f, Mathf.Abs(requestedSize.z)));

            if (IsPointMarkerKind(elementKind))
            {
                return DefaultSize(elementKind);
            }

            return safe;
        }

        private void CreateTestArena()
        {
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Act Test Arena");

            GameObject root = GameObject.Find(BlockoutRootName);
            if (root == null)
            {
                root = new GameObject(BlockoutRootName);
                Undo.RegisterCreatedObjectUndo(root, "Create Blockout Root");
            }

            parent = root.transform;

            CreateConfigured(BlockoutElementKind.Floor, new Vector3(0f, -0.05f, 0f), new Vector3(24f, 0.1f, 18f), "arena.floor", 0);
            CreateConfigured(BlockoutElementKind.Wall, new Vector3(0f, 1.5f, 9.2f), new Vector3(24f, 3f, 0.4f), "arena.wall", 0);
            CreateConfigured(BlockoutElementKind.Wall, new Vector3(0f, 1.5f, -9.2f), new Vector3(24f, 3f, 0.4f), "arena.wall", 0);
            CreateConfigured(BlockoutElementKind.Wall, new Vector3(12.2f, 1.5f, 0f), new Vector3(0.4f, 3f, 18f), "arena.wall", 0);
            CreateConfigured(BlockoutElementKind.Wall, new Vector3(-12.2f, 1.5f, 0f), new Vector3(0.4f, 3f, 18f), "arena.wall", 0);
            CreateConfigured(BlockoutElementKind.Platform, new Vector3(-4f, 1.1f, 1.5f), new Vector3(5f, 0.4f, 4f), "arena.highground", 0);
            CreateConfigured(BlockoutElementKind.Ramp, new Vector3(-1.5f, 0.45f, -2.2f), new Vector3(5f, 0.35f, 5f), "arena.ramp", 0);
            CreateConfigured(BlockoutElementKind.Cover, new Vector3(4f, 0.5f, -2f), new Vector3(2.5f, 1f, 1f), "arena.cover", 0);
            CreateConfigured(BlockoutElementKind.Cover, new Vector3(6.5f, 0.5f, 3f), new Vector3(1.2f, 1f, 2.8f), "arena.cover", 0);
            CreateConfigured(BlockoutElementKind.SpawnPoint, new Vector3(-8f, 0.9f, -5f), Vector3.one, "spawn.player", 1);
            CreateConfigured(BlockoutElementKind.EnemySpawn, new Vector3(6f, 0.9f, 3f), Vector3.one, "spawn.enemy", 2);
            CreateConfigured(BlockoutElementKind.DummySpawn, new Vector3(0f, 0.9f, 4f), Vector3.one, "dummy.training", 0);
            CreateConfigured(BlockoutElementKind.CombatZone, new Vector3(0f, 1f, 0f), new Vector3(12f, 2f, 10f), "combat.zone", 0);
            CreateConfigured(BlockoutElementKind.Objective, new Vector3(0f, 0.7f, 0f), new Vector3(2.2f, 1.4f, 2.2f), "objective.center", 0);

            parent = null;
            RefreshActiveSceneBlockoutVisuals();
            ApplyNeutralPlaytestLightingPreset();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private void CreateConfigured(BlockoutElementKind nextKind, Vector3 nextPosition, Vector3 nextSize, string nextTag, int nextTeam)
        {
            BlockoutElementKind previousKind = kind;
            Vector3 previousSize = size;
            string previousTag = gameplayTag;
            int previousTeam = team;

            kind = nextKind;
            size = nextSize;
            gameplayTag = nextTag;
            team = nextTeam;
            CreateElement(nextPosition, true);

            kind = previousKind;
            size = previousSize;
            gameplayTag = previousTag;
            team = previousTeam;
        }

        private void CreateNewLevel()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            ActToolkitEditorUtilities.EnsureFolder(ActToolkitEditorUtilities.GeneratedFolder, "Scenes");
            string levelName = SanitizeFileName(newLevelName);
            string scenePath = AssetDatabase.GenerateUniqueAssetPath(SceneFolder + "/" + levelName + ".unity");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateDefaultLevelObjects();
            EditorSceneManager.SaveScene(scene, scenePath);
            parent = GameObject.Find(BlockoutRootName)?.transform;
            AssetDatabase.Refresh();
        }

        private static void CreateDefaultLevelObjects()
        {
            GameObject root = new GameObject(BlockoutRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Blockout Root");

            GameObject lightObject = new GameObject("Level Key Light");
            Light light = lightObject.AddComponent<Light>();
            lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            ConfigureLevelKeyLight(light);

            GameObject cameraObject = new GameObject("Level Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(8f, 8f, -10f);
            cameraObject.transform.rotation = Quaternion.Euler(38f, -38f, 0f);
            ConfigureLevelCamera(camera);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = LevelAmbientColor;
        }

        private static void OpenLevel(string scenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        private static void CloneLevel(string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                return;
            }

            string folder = Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
            string file = Path.GetFileNameWithoutExtension(scenePath);
            string clonePath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + file + "_Copy.unity");
            AssetDatabase.CopyAsset(scenePath, clonePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void SaveActiveScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (string.IsNullOrWhiteSpace(activeScene.path))
            {
                ActToolkitEditorUtilities.EnsureFolder(ActToolkitEditorUtilities.GeneratedFolder, "Scenes");
                string scenePath = AssetDatabase.GenerateUniqueAssetPath(SceneFolder + "/Untitled_Blockout.unity");
                EditorSceneManager.SaveScene(activeScene, scenePath);
                return;
            }

            EditorSceneManager.SaveScene(activeScene);
        }

        private void SaveAndEnterPlaytest()
        {
            if (InstallOrUpdatePlaytestBootstrap(false) == null)
            {
                return;
            }

            EnterPlayMode();
        }

        private ActPlaytestBootstrap InstallOrUpdatePlaytestBootstrap(bool selectAfterInstall)
        {
            if (playtestProfile == null)
            {
                playtestProfile = FindBestPlaytestProfileAsset();
                SavePlaytestProfilePrefs();
            }

            if (playtestProfile == null)
            {
                EditorUtility.DisplayDialog("Playtest Missing Character", "Choose a CharacterActionProfile before playtesting.", "OK");
                return null;
            }

            if (playtestProfile.comboTable == null)
            {
                EditorUtility.DisplayDialog("Playtest Missing Combo Table", "The selected character has no combo table.", "OK");
                return null;
            }

            ActPlaytestBootstrap bootstrap = FindPlaytestBootstrap();
            if (bootstrap == null)
            {
                GameObject go = new GameObject(PlaytestBootstrapName);
                Undo.RegisterCreatedObjectUndo(go, "Create Playtest Bootstrap");
                bootstrap = go.AddComponent<ActPlaytestBootstrap>();
            }

            Undo.RecordObject(bootstrap, "Update Playtest Bootstrap");
            bootstrap.Configure(playtestProfile);
            EditorUtility.SetDirty(bootstrap);
            RefreshActiveSceneBlockoutVisuals();
            ApplyNeutralPlaytestLightingPreset();
            EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);

            if (selectAfterInstall)
            {
                Selection.activeGameObject = bootstrap.gameObject;
            }

            return bootstrap;
        }

        private static ActPlaytestBootstrap FindPlaytestBootstrap()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            ActPlaytestBootstrap[] bootstraps = FindObjectsByType<ActPlaytestBootstrap>(FindObjectsInactive.Include);
            foreach (ActPlaytestBootstrap bootstrap in bootstraps)
            {
                if (bootstrap != null && bootstrap.gameObject.scene == activeScene)
                {
                    return bootstrap;
                }
            }

            return null;
        }

        private static void EnterPlayMode()
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorApplication.EnterPlaymode();
            }
        }

        private Transform EnsureBlockoutRoot()
        {
            GameObject existing = GameObject.Find(BlockoutRootName);
            if (existing != null)
            {
                return existing.transform;
            }

            GameObject root = new GameObject(BlockoutRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Blockout Root");
            EditorSceneManager.MarkSceneDirty(root.scene);
            return root.transform;
        }

        private static void SelectAllBlockoutElements()
        {
            BlockoutElement[] elements = FindSceneBlockoutElements();
            GameObject[] objects = new GameObject[elements.Length];
            for (int i = 0; i < elements.Length; i++)
            {
                objects[i] = elements[i].gameObject;
            }

            Selection.objects = objects;
        }

        private static void FrameAllBlockoutElements()
        {
            SelectAllBlockoutElements();
            SceneView.FrameLastActiveSceneView();
        }

        private static void FrameObject(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            Selection.activeGameObject = go;
            SceneView.FrameLastActiveSceneView();
        }

        private static string[] FindGeneratedLevelScenes()
        {
            if (!AssetDatabase.IsValidFolder(SceneFolder))
            {
                return Array.Empty<string>();
            }

            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { SceneFolder });
            List<string> paths = new List<string>(guids.Length);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    paths.Add(path);
                }
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths.ToArray();
        }

        private static BlockoutElement[] FindSceneBlockoutElements()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            BlockoutElement[] allElements = FindObjectsByType<BlockoutElement>(FindObjectsInactive.Exclude);
            List<BlockoutElement> elements = new List<BlockoutElement>(allElements.Length);
            foreach (BlockoutElement element in allElements)
            {
                if (element != null && element.gameObject.scene == activeScene)
                {
                    elements.Add(element);
                }
            }

            elements.Sort((left, right) => string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
            return elements.ToArray();
        }

        private static BlockoutElement SelectedBlockoutElement()
        {
            return Selection.activeGameObject == null ? null : Selection.activeGameObject.GetComponentInParent<BlockoutElement>();
        }

        private static BlockoutElement PickBlockoutElement(Vector2 mousePosition)
        {
            GameObject picked = HandleUtility.PickGameObject(mousePosition, false);
            return picked == null ? null : picked.GetComponentInParent<BlockoutElement>();
        }

        private void UpdateSceneHoverPoint(Vector2 mousePosition)
        {
            hasSceneHoverPoint = TryGetPlacementSurface(mousePosition, out sceneHoverSurfacePoint, out sceneHoverSurfaceNormal);
            if (hasSceneHoverPoint)
            {
                sceneHoverSurfacePoint = SnapSurfacePoint(sceneHoverSurfacePoint, sceneHoverSurfaceNormal, gridSize);
                sceneHoverPoint = PlacementCenter(kind, sceneHoverSurfacePoint, sceneHoverSurfaceNormal, SanitizedSize(kind, size));
            }
        }

        private static bool TryGetPlacementSurface(Vector2 mousePosition, out Vector3 point, out Vector3 normal)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 10000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (hits.Length > 1)
            {
                Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            }

            Scene activeScene = SceneManager.GetActiveScene();
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || hit.collider.gameObject.scene != activeScene)
                {
                    continue;
                }

                point = hit.point;
                normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
                return true;
            }

            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                normal = Vector3.up;
                return true;
            }

            point = Vector3.zero;
            normal = Vector3.up;
            return false;
        }

        private static bool TryGetGroundPoint(Vector2 mousePosition, out Vector3 point)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }

            point = Vector3.zero;
            return false;
        }

        private bool CanCreateFromCurrentMode()
        {
            return workspaceMode == LevelWorkspaceMode.Blockout || workspaceMode == LevelWorkspaceMode.Gameplay;
        }

        private void RunLevelValidation()
        {
            validationIssues.Clear();
            validationScenePath = SceneManager.GetActiveScene().path;

            Scene activeScene = SceneManager.GetActiveScene();
            if (string.IsNullOrWhiteSpace(activeScene.path))
            {
                AddIssue(ValidationSeverity.Warning, "Scene has not been saved yet.", null);
            }

            if (GameObject.Find(BlockoutRootName) == null)
            {
                AddIssue(ValidationSeverity.Warning, "Scene is missing Blockout_Root.", null);
            }

            BlockoutElement[] elements = FindSceneBlockoutElements();
            if (elements.Length == 0)
            {
                AddIssue(ValidationSeverity.Warning, "Scene has no blockout elements.", null);
                return;
            }

            bool hasPlayerSpawn = false;
            bool hasEnemySpawn = false;
            Dictionary<string, BlockoutElement> ids = new Dictionary<string, BlockoutElement>();

            foreach (BlockoutElement element in elements)
            {
                if (element == null)
                {
                    continue;
                }

                element.EnsureId();

                if (element.kind == BlockoutElementKind.SpawnPoint)
                {
                    hasPlayerSpawn = true;
                }

                if (element.kind == BlockoutElementKind.EnemySpawn || element.kind == BlockoutElementKind.DummySpawn)
                {
                    hasEnemySpawn = true;
                }

                if (ids.TryGetValue(element.elementId, out BlockoutElement duplicate))
                {
                    AddIssue(ValidationSeverity.Error, "Duplicate element id with " + duplicate.name + ".", element);
                }
                else
                {
                    ids.Add(element.elementId, element);
                }

                if (element.logicalSize.x <= 0.05f || element.logicalSize.y <= 0.05f || element.logicalSize.z <= 0.05f)
                {
                    AddIssue(ValidationSeverity.Error, "Element has an invalid logical size.", element);
                }

                Collider collider = element.GetComponent<Collider>();
                if (collider == null)
                {
                    AddIssue(ValidationSeverity.Warning, "Element has no collider.", element);
                }
                else
                {
                    if (IsSolidKind(element.kind) && collider.isTrigger)
                    {
                        AddIssue(ValidationSeverity.Warning, "Solid blockout geometry should not be a trigger.", element);
                    }

                    if ((IsTriggerLikeKind(element.kind) || IsPointMarkerKind(element.kind)) && !collider.isTrigger)
                    {
                        AddIssue(ValidationSeverity.Warning, "Gameplay marker should use trigger collision.", element);
                    }
                }

                if (IsGameplayKind(element.kind) && string.IsNullOrWhiteSpace(element.gameplayTag))
                {
                    AddIssue(ValidationSeverity.Warning, "Gameplay marker has an empty gameplay tag.", element);
                }
            }

            if (!hasPlayerSpawn)
            {
                AddIssue(ValidationSeverity.Error, "Scene needs at least one Player Spawn.", null);
            }

            if (!hasEnemySpawn)
            {
                AddIssue(ValidationSeverity.Info, "No Enemy Spawn or Dummy Spawn has been placed yet.", null);
            }

            Repaint();
        }

        private void AddIssue(ValidationSeverity severity, string message, BlockoutElement element)
        {
            validationIssues.Add(new ValidationIssue
            {
                severity = severity,
                message = message,
                element = element
            });
        }

        private bool NeedsValidationRefresh()
        {
            return !string.Equals(validationScenePath, SceneManager.GetActiveScene().path, StringComparison.OrdinalIgnoreCase);
        }

        private void CountValidationIssues(out int errorCount, out int warningCount)
        {
            errorCount = 0;
            warningCount = 0;

            foreach (ValidationIssue issue in validationIssues)
            {
                if (issue.severity == ValidationSeverity.Error)
                {
                    errorCount++;
                }
                else if (issue.severity == ValidationSeverity.Warning)
                {
                    warningCount++;
                }
            }
        }

        private static Color SeverityColor(ValidationSeverity severity)
        {
            switch (severity)
            {
                case ValidationSeverity.Error:
                    return new Color(0.9f, 0.35f, 0.35f, 1f);
                case ValidationSeverity.Warning:
                    return new Color(0.9f, 0.72f, 0.3f, 1f);
                default:
                    return new Color(0.45f, 0.7f, 0.95f, 1f);
            }
        }

        private static string SanitizeFileName(string value)
        {
            string safe = string.IsNullOrWhiteSpace(value) ? DefaultLevelName : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(safe) ? DefaultLevelName : safe;
        }

        private static void ExportSceneBlockout()
        {
            ActToolkitEditorUtilities.EnsureGeneratedFolders();

            BlockoutElement[] elements = FindSceneBlockoutElements();
            Array.Sort(elements, (left, right) => string.Compare(left.name, right.name, StringComparison.Ordinal));

            BlockoutExport export = new BlockoutExport
            {
                schemaVersion = 1,
                sceneName = SceneManager.GetActiveScene().name,
                scenePath = SceneManager.GetActiveScene().path,
                exportedUtc = DateTime.UtcNow.ToString("o")
            };

            foreach (BlockoutElement element in elements)
            {
                element.EnsureId();
                export.elements.Add(BlockoutElementExport.FromElement(element));
                EditorUtility.SetDirty(element);
            }

            string safeSceneName = string.IsNullOrWhiteSpace(export.sceneName) ? "UntitledScene" : export.sceneName;
            string path = ActToolkitEditorUtilities.GeneratedFolder + "/Blockouts/" + SanitizeFileName(safeSceneName) + ".blockout.json";
            ActToolkitEditorUtilities.WriteTextAsset(path, JsonUtility.ToJson(export, true));
            EditorUtility.RevealInFinder(path);
        }

        private static bool IsSolidKind(BlockoutElementKind elementKind)
        {
            return elementKind == BlockoutElementKind.Block
                || elementKind == BlockoutElementKind.Floor
                || elementKind == BlockoutElementKind.Wall
                || elementKind == BlockoutElementKind.Ramp
                || elementKind == BlockoutElementKind.Platform
                || elementKind == BlockoutElementKind.Cover;
        }

        private static bool IsPointMarkerKind(BlockoutElementKind elementKind)
        {
            return elementKind == BlockoutElementKind.SpawnPoint
                || elementKind == BlockoutElementKind.EnemySpawn
                || elementKind == BlockoutElementKind.DummySpawn
                || elementKind == BlockoutElementKind.NavMarker;
        }

        private static bool IsTriggerLikeKind(BlockoutElementKind elementKind)
        {
            return elementKind == BlockoutElementKind.Objective
                || elementKind == BlockoutElementKind.TriggerVolume
                || elementKind == BlockoutElementKind.KillZone
                || elementKind == BlockoutElementKind.CombatZone;
        }

        private static bool IsGameplayKind(BlockoutElementKind elementKind)
        {
            return IsPointMarkerKind(elementKind) || IsTriggerLikeKind(elementKind);
        }

        private static bool IsBlockoutKind(BlockoutElementKind elementKind)
        {
            return IsSolidKind(elementKind);
        }

        private static Vector3 DefaultSize(BlockoutElementKind elementKind)
        {
            switch (elementKind)
            {
                case BlockoutElementKind.Floor:
                    return new Vector3(8f, 0.1f, 8f);
                case BlockoutElementKind.Wall:
                    return new Vector3(4f, 3f, 0.3f);
                case BlockoutElementKind.Ramp:
                    return new Vector3(4f, 0.35f, 4f);
                case BlockoutElementKind.Platform:
                    return new Vector3(4f, 0.4f, 4f);
                case BlockoutElementKind.Cover:
                    return new Vector3(2f, 1f, 1f);
                case BlockoutElementKind.SpawnPoint:
                case BlockoutElementKind.EnemySpawn:
                case BlockoutElementKind.DummySpawn:
                case BlockoutElementKind.NavMarker:
                    return new Vector3(0.7f, 1.8f, 0.7f);
                case BlockoutElementKind.Objective:
                    return new Vector3(2.2f, 1.4f, 2.2f);
                case BlockoutElementKind.TriggerVolume:
                case BlockoutElementKind.CombatZone:
                    return new Vector3(4f, 2f, 4f);
                case BlockoutElementKind.KillZone:
                    return new Vector3(4f, 0.25f, 4f);
                default:
                    return new Vector3(2f, 1f, 2f);
            }
        }

        private static Quaternion DefaultRotation(BlockoutElementKind elementKind)
        {
            return elementKind == BlockoutElementKind.Ramp ? Quaternion.Euler(-16f, 0f, 0f) : Quaternion.identity;
        }

        private static Vector3 SnapSurfacePoint(Vector3 point, Vector3 normal, float grid)
        {
            if (grid <= 0f)
            {
                return point;
            }

            point.x = Mathf.Round(point.x / grid) * grid;
            point.z = Mathf.Round(point.z / grid) * grid;

            if (Mathf.Abs(normal.y) < 0.5f)
            {
                point.y = Mathf.Round(point.y / grid) * grid;
            }

            return point;
        }

        private static Vector3 PlacementCenter(BlockoutElementKind elementKind, Vector3 surfacePoint, Vector3 surfaceNormal, Vector3 elementSize)
        {
            Vector3 normal = surfaceNormal.sqrMagnitude > 0.0001f ? surfaceNormal.normalized : Vector3.up;
            float offset = SurfaceOffset(elementKind, normal, elementSize);
            return surfacePoint + normal * offset;
        }

        private static float SurfaceOffset(BlockoutElementKind elementKind, Vector3 normal, Vector3 elementSize)
        {
            float halfExtent =
                Mathf.Abs(normal.x) * elementSize.x * 0.5f
                + Mathf.Abs(normal.y) * elementSize.y * 0.5f
                + Mathf.Abs(normal.z) * elementSize.z * 0.5f;

            if (elementKind == BlockoutElementKind.Floor && normal.y > 0.5f)
            {
                return -halfExtent;
            }

            return halfExtent;
        }

        private static Vector3 GroundedPlacementPosition(BlockoutElementKind elementKind, Vector3 groundPoint, Vector3 elementSize)
        {
            return PlacementCenter(elementKind, groundPoint, Vector3.up, elementSize);
        }

        private static string DefaultGameplayTag(BlockoutElementKind elementKind)
        {
            switch (elementKind)
            {
                case BlockoutElementKind.Floor:
                    return "arena.floor";
                case BlockoutElementKind.Wall:
                    return "arena.wall";
                case BlockoutElementKind.Ramp:
                    return "arena.ramp";
                case BlockoutElementKind.Platform:
                    return "arena.platform";
                case BlockoutElementKind.Cover:
                    return "arena.cover";
                case BlockoutElementKind.SpawnPoint:
                    return "spawn.player";
                case BlockoutElementKind.EnemySpawn:
                    return "spawn.enemy";
                case BlockoutElementKind.DummySpawn:
                    return "dummy.training";
                case BlockoutElementKind.Objective:
                    return "objective";
                case BlockoutElementKind.TriggerVolume:
                    return "trigger";
                case BlockoutElementKind.KillZone:
                    return "killzone";
                case BlockoutElementKind.NavMarker:
                    return "nav.marker";
                case BlockoutElementKind.CombatZone:
                    return "combat.zone";
                default:
                    return "arena";
            }
        }

        private static string DisplayName(BlockoutElementKind elementKind)
        {
            switch (elementKind)
            {
                case BlockoutElementKind.SpawnPoint:
                    return "Player Spawn";
                case BlockoutElementKind.EnemySpawn:
                    return "Enemy Spawn";
                case BlockoutElementKind.DummySpawn:
                    return "Dummy Spawn";
                case BlockoutElementKind.TriggerVolume:
                    return "Trigger Volume";
                case BlockoutElementKind.KillZone:
                    return "Kill Zone";
                case BlockoutElementKind.NavMarker:
                    return "Nav Marker";
                case BlockoutElementKind.CombatZone:
                    return "Combat Zone";
                default:
                    return ObjectNames.NicifyVariableName(elementKind.ToString());
            }
        }

        private static string DefaultObjectName(BlockoutElementKind elementKind)
        {
            return "Blockout_" + elementKind;
        }

        [Serializable]
        private sealed class BlockoutExport
        {
            public int schemaVersion;
            public string sceneName;
            public string scenePath;
            public string exportedUtc;
            public List<BlockoutElementExport> elements = new List<BlockoutElementExport>();
        }

        [Serializable]
        private sealed class BlockoutElementExport
        {
            public string id;
            public string name;
            public string kind;
            public string gameplayTag;
            public int team;
            public bool serverAuthoritativeCollision;
            public Vector3 position;
            public Vector3 eulerAngles;
            public Vector3 scale;
            public Vector3 logicalSize;

            public static BlockoutElementExport FromElement(BlockoutElement element)
            {
                Transform transform = element.transform;
                return new BlockoutElementExport
                {
                    id = element.elementId,
                    name = element.name,
                    kind = element.kind.ToString(),
                    gameplayTag = element.gameplayTag,
                    team = element.team,
                    serverAuthoritativeCollision = element.serverAuthoritativeCollision,
                    position = transform.position,
                    eulerAngles = transform.rotation.eulerAngles,
                    scale = transform.localScale,
                    logicalSize = element.logicalSize
                };
            }
        }
    }
}
