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
    public sealed class LevelBlockoutEditorWindow : EditorWindow
    {
        private const string SceneFolder = ActToolkitEditorUtilities.GeneratedFolder + "/Scenes";
        private const string DefaultLevelName = "Blockout_Level";
        private const string BlockoutRootName = "Blockout_Root";

        private BlockoutElementKind kind = BlockoutElementKind.Block;
        private Vector3 size = new Vector3(2f, 1f, 2f);
        private float gridSize = 1f;
        private int team;
        private string gameplayTag = "arena";
        private Transform parent;
        private bool sceneEditingEnabled = true;
        private bool createOnEmptyLeftClick = true;
        private bool showPlacementPreview = true;
        private bool snapSceneDrag = true;
        private string newLevelName = DefaultLevelName;
        private Vector2 scrollPosition;
        private Vector2 levelListScrollPosition;
        private Vector2 elementListScrollPosition;
        private BlockoutElement draggedElement;
        private Vector3 dragOffset;
        private Vector3 sceneHoverPoint;
        private bool hasSceneHoverPoint;

        [MenuItem("Tools/Act Toolkit/Level Blockout Editor")]
        [MenuItem("Act Toolkit/Level/Level Blockout Editor")]
        public static void Open()
        {
            LevelBlockoutEditorWindow window = GetWindow<LevelBlockoutEditorWindow>();
            window.titleContent = new GUIContent("Level Blockout");
            window.minSize = new Vector2(500f, 620f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += DuringSceneGui;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DuringSceneGui;
        }

        private void OnGUI()
        {
            ActToolkitEditorUtilities.EnsureGeneratedFolders();
            ActToolkitEditorUtilities.EnsureFolder(ActToolkitEditorUtilities.GeneratedFolder, "Scenes");

            using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(scrollPosition))
            {
                scrollPosition = scroll.scrollPosition;

                DrawLevelHeader();
                EditorGUILayout.Space(8f);
                DrawSceneToolSettings();
                EditorGUILayout.Space(8f);
                DrawSelectedElementInspector();
                EditorGUILayout.Space(8f);
                DrawElementList();
            }
        }

        private void DrawLevelHeader()
        {
            Scene activeScene = SceneManager.GetActiveScene();

            EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);
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

                    if (GUILayout.Button("Play", GUILayout.Height(26f)))
                    {
                        EnterPlayMode();
                    }

                    if (GUILayout.Button("Frame Blockout", GUILayout.Height(26f)))
                    {
                        FrameAllBlockoutElements();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    newLevelName = EditorGUILayout.TextField("New Level", newLevelName);
                    if (GUILayout.Button("Create", GUILayout.Width(90f)))
                    {
                        CreateNewLevel();
                    }
                }
            }

            EditorGUILayout.Space(4f);
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
                    using (EditorGUILayout.ScrollViewScope levelScroll = new EditorGUILayout.ScrollViewScope(levelListScrollPosition, GUILayout.MinHeight(72f), GUILayout.MaxHeight(150f)))
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
            using (new EditorGUILayout.HorizontalScope())
            {
                bool isCurrent = string.Equals(scenePath, activeScenePath, StringComparison.OrdinalIgnoreCase);
                string label = Path.GetFileNameWithoutExtension(scenePath);
                EditorGUILayout.LabelField(isCurrent ? label + "  *" : label);

                using (new EditorGUI.DisabledScope(isCurrent))
                {
                    if (GUILayout.Button("Open", GUILayout.Width(72f)))
                    {
                        OpenLevel(scenePath);
                    }
                }

                if (GUILayout.Button("Ping", GUILayout.Width(64f)))
                {
                    UnityEngine.Object sceneAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(scenePath);
                    EditorGUIUtility.PingObject(sceneAsset);
                }
            }
        }

        private void DrawSceneToolSettings()
        {
            EditorGUILayout.LabelField("Blockout Tool", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                sceneEditingEnabled = EditorGUILayout.Toggle("Scene Editing", sceneEditingEnabled);
                createOnEmptyLeftClick = EditorGUILayout.Toggle("LMB Creates On Empty", createOnEmptyLeftClick);
                showPlacementPreview = EditorGUILayout.Toggle("Placement Preview", showPlacementPreview);
                snapSceneDrag = EditorGUILayout.Toggle("Snap Drag", snapSceneDrag);

                kind = (BlockoutElementKind)EditorGUILayout.EnumPopup("Kind", kind);
                size = EditorGUILayout.Vector3Field("Size", size);
                gridSize = Mathf.Max(0.05f, EditorGUILayout.FloatField("Grid Size", gridSize));
                team = EditorGUILayout.IntField("Team", team);
                gameplayTag = EditorGUILayout.TextField("Gameplay Tag", gameplayTag);
                parent = (Transform)EditorGUILayout.ObjectField("Parent", parent, typeof(Transform), true);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Place At View", GUILayout.Height(28f)))
                    {
                        CreateElement(ActToolkitEditorUtilities.SceneViewGroundPoint(gridSize));
                    }

                    using (new EditorGUI.DisabledScope(Selection.activeTransform == null))
                    {
                        if (GUILayout.Button("Place At Selection", GUILayout.Height(28f)))
                        {
                            CreateElement(ActToolkitEditorUtilities.Snap(Selection.activeTransform.position, gridSize));
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Create Test Arena", GUILayout.Height(28f)))
                    {
                        CreateTestArena();
                    }

                    if (GUILayout.Button("Export JSON", GUILayout.Height(28f)))
                    {
                        ExportSceneBlockout();
                    }

                    if (GUILayout.Button("Select All", GUILayout.Height(28f)))
                    {
                        SelectAllBlockoutElements();
                    }
                }
            }
        }

        private void DrawSelectedElementInspector()
        {
            BlockoutElement selected = SelectedBlockoutElement();

            EditorGUILayout.LabelField("Selected Element", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (selected == null)
                {
                    EditorGUILayout.LabelField("No blockout element selected.");
                    return;
                }

                EditorGUI.BeginChangeCheck();
                string nextName = EditorGUILayout.TextField("Name", selected.gameObject.name);
                BlockoutElementKind nextKind = (BlockoutElementKind)EditorGUILayout.EnumPopup("Kind", selected.kind);
                string nextTag = EditorGUILayout.TextField("Gameplay Tag", selected.gameplayTag);
                int nextTeam = EditorGUILayout.IntField("Team", selected.team);
                bool nextServerCollision = EditorGUILayout.Toggle("Server Collision", selected.serverAuthoritativeCollision);
                Vector3 nextSize = EditorGUILayout.Vector3Field("Logical Size", selected.logicalSize);

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
                    selected.gizmoColor = ActToolkitEditorUtilities.ColorFor(nextKind);
                    ApplyElementVisuals(selected);
                    EditorUtility.SetDirty(selected);
                    EditorSceneManager.MarkSceneDirty(selected.gameObject.scene);
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

                    if (GUILayout.Button("Delete", GUILayout.Height(26f)))
                    {
                        DeleteElement(selected);
                    }
                }
            }
        }

        private void DrawElementList()
        {
            EditorGUILayout.LabelField("Current Scene Elements", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                BlockoutElement[] elements = FindSceneBlockoutElements();
                if (elements.Length == 0)
                {
                    EditorGUILayout.LabelField("No blockout elements in the current scene.");
                    return;
                }

                using (EditorGUILayout.ScrollViewScope elementScroll = new EditorGUILayout.ScrollViewScope(elementListScrollPosition, GUILayout.MinHeight(120f), GUILayout.MaxHeight(240f)))
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
                EditorGUILayout.LabelField(element.kind.ToString(), GUILayout.Width(92f));
                if (GUILayout.Button(element.name, EditorStyles.miniButtonLeft))
                {
                    Selection.activeGameObject = element.gameObject;
                    FrameObject(element.gameObject);
                }

                if (GUILayout.Button("Ping", EditorStyles.miniButtonMid, GUILayout.Width(46f)))
                {
                    EditorGUIUtility.PingObject(element.gameObject);
                }

                if (GUILayout.Button("X", EditorStyles.miniButtonRight, GUILayout.Width(26f)))
                {
                    DeleteElement(element);
                }
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
            DrawSelectedElementHandles();

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
                BlockoutElement picked = PickBlockoutElement(current.mousePosition);
                if (picked != null)
                {
                    Selection.activeGameObject = picked.gameObject;
                    BeginDrag(picked, current.mousePosition);
                    current.Use();
                    return;
                }

                if (createOnEmptyLeftClick && hasSceneHoverPoint)
                {
                    GameObject created = CreateElement(sceneHoverPoint);
                    BeginDrag(created.GetComponent<BlockoutElement>(), current.mousePosition);
                    current.Use();
                }
            }

            if (current.type == EventType.MouseDown && current.button == 2)
            {
                BlockoutElement selected = SelectedBlockoutElement();
                if (selected != null)
                {
                    BeginDrag(selected, current.mousePosition);
                    current.Use();
                }
            }

            if (current.type == EventType.MouseDrag && draggedElement != null && (current.button == 0 || current.button == 2))
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
            }
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
            Rect rect = new Rect(12f, 12f, 390f, 76f);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 18f), "Level Blockout");
            GUI.Label(new Rect(rect.x + 8f, rect.y + 26f, rect.width - 16f, 18f), "LMB: select/drag, empty LMB: create, RMB: menu");
            GUI.Label(new Rect(rect.x + 8f, rect.y + 46f, rect.width - 16f, 18f), "MMB: drag selected, Del: delete, Ctrl+D: duplicate, Alt: camera");
            Handles.EndGUI();
        }

        private void DrawPlacementPreview()
        {
            if (!showPlacementPreview || !hasSceneHoverPoint || draggedElement != null)
            {
                return;
            }

            Color color = ActToolkitEditorUtilities.ColorFor(kind);
            Handles.color = new Color(color.r, color.g, color.b, 0.85f);
            Vector3 previewSize = SanitizedSize(kind, size);
            Matrix4x4 previousMatrix = Handles.matrix;
            Handles.matrix = Matrix4x4.TRS(sceneHoverPoint, kind == BlockoutElementKind.Ramp ? Quaternion.Euler(-16f, 0f, 0f) : Quaternion.identity, previewSize);

            if (kind == BlockoutElementKind.SpawnPoint || kind == BlockoutElementKind.NavMarker)
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

            if (selected.kind == BlockoutElementKind.SpawnPoint || selected.kind == BlockoutElementKind.NavMarker)
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
            Vector3 menuPoint = hasSceneHoverPoint ? sceneHoverPoint : ActToolkitEditorUtilities.SceneViewGroundPoint(gridSize);
            BlockoutElement picked = PickBlockoutElement(mousePosition);
            if (picked != null)
            {
                Selection.activeGameObject = picked.gameObject;
            }

            BlockoutElement selected = SelectedBlockoutElement();
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Create/" + kind + " Here"), false, () => CreateElement(menuPoint));

            foreach (BlockoutElementKind menuKind in Enum.GetValues(typeof(BlockoutElementKind)))
            {
                BlockoutElementKind capturedKind = menuKind;
                menu.AddItem(new GUIContent("Create As/" + capturedKind), false, () =>
                {
                    BlockoutElementKind previousKind = kind;
                    kind = capturedKind;
                    CreateElement(menuPoint);
                    kind = previousKind;
                });
            }

            menu.AddSeparator("");
            if (selected == null)
            {
                menu.AddDisabledItem(new GUIContent("Duplicate Selected Here"));
                menu.AddDisabledItem(new GUIContent("Delete Selected"));
                menu.AddDisabledItem(new GUIContent("Frame Selected"));
            }
            else
            {
                menu.AddItem(new GUIContent("Duplicate Selected Here"), false, () => DuplicateElement(selected, menuPoint));
                menu.AddItem(new GUIContent("Delete Selected"), false, () => DeleteElement(selected));
                menu.AddItem(new GUIContent("Frame Selected"), false, () => FrameObject(selected.gameObject));
                menu.AddSeparator("");

                foreach (BlockoutElementKind menuKind in Enum.GetValues(typeof(BlockoutElementKind)))
                {
                    BlockoutElementKind capturedKind = menuKind;
                    menu.AddItem(new GUIContent("Set Selected Kind/" + capturedKind), selected.kind == capturedKind, () => SetElementKind(selected, capturedKind));
                }
            }

            menu.ShowAsContext();
        }

        private GameObject CreateElement(Vector3 position)
        {
            ActToolkitEditorUtilities.EnsureGeneratedFolders();

            PrimitiveType primitive = kind == BlockoutElementKind.SpawnPoint || kind == BlockoutElementKind.NavMarker
                ? PrimitiveType.Capsule
                : PrimitiveType.Cube;

            GameObject go = GameObject.CreatePrimitive(primitive);
            Undo.RegisterCreatedObjectUndo(go, "Create Blockout Element");

            go.name = "Blockout_" + kind;
            go.transform.position = position;
            go.transform.localScale = SanitizedSize(kind, size);

            if (kind == BlockoutElementKind.Ramp)
            {
                go.transform.rotation = Quaternion.Euler(-16f, 0f, 0f);
            }

            Transform targetParent = parent != null ? parent : EnsureBlockoutRoot();
            if (targetParent != null)
            {
                go.transform.SetParent(targetParent, true);
            }

            BlockoutElement element = go.AddComponent<BlockoutElement>();
            element.kind = kind;
            element.gameplayTag = gameplayTag;
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
                collider.isTrigger = element.kind == BlockoutElementKind.TriggerVolume
                    || element.kind == BlockoutElementKind.KillZone
                    || element.kind == BlockoutElementKind.Objective;
                element.serverAuthoritativeCollision = !collider.isTrigger && element.serverAuthoritativeCollision;
            }
        }

        private static Vector3 SanitizedSize(BlockoutElementKind elementKind, Vector3 requestedSize)
        {
            Vector3 safe = new Vector3(
                Mathf.Max(0.05f, Mathf.Abs(requestedSize.x)),
                Mathf.Max(0.05f, Mathf.Abs(requestedSize.y)),
                Mathf.Max(0.05f, Mathf.Abs(requestedSize.z)));

            if (elementKind == BlockoutElementKind.SpawnPoint || elementKind == BlockoutElementKind.NavMarker)
            {
                return new Vector3(0.7f, 1.8f, 0.7f);
            }

            return safe;
        }

        private void CreateTestArena()
        {
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Act Test Arena");

            GameObject root = new GameObject("Blockout_TestArena");
            Undo.RegisterCreatedObjectUndo(root, "Create Test Arena Root");
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
            CreateConfigured(BlockoutElementKind.SpawnPoint, new Vector3(-8f, 0.9f, -5f), Vector3.one, "spawn.teamA", 1);
            CreateConfigured(BlockoutElementKind.SpawnPoint, new Vector3(8f, 0.9f, 5f), Vector3.one, "spawn.teamB", 2);
            CreateConfigured(BlockoutElementKind.Objective, new Vector3(0f, 0.7f, 0f), new Vector3(2.2f, 1.4f, 2.2f), "objective.center", 0);

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
            CreateElement(nextPosition);

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
            light.type = LightType.Directional;
            light.intensity = 0.8f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            GameObject cameraObject = new GameObject("Level Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.transform.position = new Vector3(8f, 8f, -10f);
            cameraObject.transform.rotation = Quaternion.Euler(38f, -38f, 0f);
        }

        private static void OpenLevel(string scenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
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
            hasSceneHoverPoint = TryGetGroundPoint(mousePosition, out sceneHoverPoint);
            if (hasSceneHoverPoint)
            {
                sceneHoverPoint = ActToolkitEditorUtilities.Snap(sceneHoverPoint, gridSize);
            }
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

        [Serializable]
        private sealed class BlockoutExport
        {
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
