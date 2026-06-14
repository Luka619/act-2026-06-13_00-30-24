using System;
using System.Collections.Generic;
using ActToolkit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ActToolkit.EditorTools
{
    public sealed class LevelBlockoutEditorWindow : EditorWindow
    {
        private BlockoutElementKind kind = BlockoutElementKind.Block;
        private Vector3 size = new Vector3(2f, 1f, 2f);
        private float gridSize = 1f;
        private int team;
        private string gameplayTag = "arena";
        private bool sceneClickPlacement;
        private Transform parent;

        [MenuItem("Tools/Act Toolkit/Level Blockout Editor")]
        public static void Open()
        {
            LevelBlockoutEditorWindow window = GetWindow<LevelBlockoutEditorWindow>();
            window.titleContent = new GUIContent("Level Blockout");
            window.minSize = new Vector2(420f, 480f);
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
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Level Blockout Editor", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Place whitebox gameplay geometry, spawn points, trigger volumes, and navigation markers. Exported JSON is intended for server validation and runtime spawning.", MessageType.Info);

            kind = (BlockoutElementKind)EditorGUILayout.EnumPopup("Kind", kind);
            size = EditorGUILayout.Vector3Field("Size", size);
            gridSize = Mathf.Max(0.05f, EditorGUILayout.FloatField("Grid Size", gridSize));
            team = EditorGUILayout.IntField("Team", team);
            gameplayTag = EditorGUILayout.TextField("Gameplay Tag", gameplayTag);
            parent = (Transform)EditorGUILayout.ObjectField("Parent", parent, typeof(Transform), true);
            sceneClickPlacement = EditorGUILayout.Toggle("Scene Click Placement", sceneClickPlacement);

            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Place At Scene View", GUILayout.Height(30f)))
            {
                CreateElement(ActToolkitEditorUtilities.SceneViewGroundPoint(gridSize));
            }

            using (new EditorGUI.DisabledScope(Selection.activeTransform == null))
            {
                if (GUILayout.Button("Place At Selection", GUILayout.Height(30f)))
                {
                    CreateElement(ActToolkitEditorUtilities.Snap(Selection.activeTransform.position, gridSize));
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Create Test Arena", GUILayout.Height(30f)))
            {
                CreateTestArena();
            }

            if (GUILayout.Button("Export Scene Blockout JSON", GUILayout.Height(30f)))
            {
                ExportSceneBlockout();
            }

            if (GUILayout.Button("Select All Blockout Elements", GUILayout.Height(26f)))
            {
                SelectAllBlockoutElements();
            }
        }

        private void DuringSceneGui(SceneView sceneView)
        {
            if (!sceneClickPlacement)
            {
                return;
            }

            Handles.BeginGUI();
            GUI.Label(new Rect(12f, 12f, 280f, 24f), "Act Toolkit placement: Left click ground");
            Handles.EndGUI();

            Event current = Event.current;
            if (current == null || current.type != EventType.MouseDown || current.button != 0 || current.alt)
            {
                return;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float distance))
            {
                CreateElement(ActToolkitEditorUtilities.Snap(ray.GetPoint(distance), gridSize));
                current.Use();
            }
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

            if (parent != null)
            {
                go.transform.SetParent(parent, true);
            }

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = ActToolkitEditorUtilities.GetOrCreateBlockoutMaterial(kind, ActToolkitEditorUtilities.ColorFor(kind));
            }

            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = kind == BlockoutElementKind.TriggerVolume || kind == BlockoutElementKind.KillZone || kind == BlockoutElementKind.Objective;
            }

            BlockoutElement element = go.AddComponent<BlockoutElement>();
            element.kind = kind;
            element.gameplayTag = gameplayTag;
            element.team = team;
            element.logicalSize = go.transform.localScale;
            element.gizmoColor = ActToolkitEditorUtilities.ColorFor(kind);
            element.serverAuthoritativeCollision = collider != null && !collider.isTrigger;
            element.EnsureId();

            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            return go;
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

        private static void SelectAllBlockoutElements()
        {
            BlockoutElement[] elements = FindObjectsByType<BlockoutElement>(FindObjectsInactive.Exclude);
            GameObject[] objects = new GameObject[elements.Length];
            for (int i = 0; i < elements.Length; i++)
            {
                objects[i] = elements[i].gameObject;
            }

            Selection.objects = objects;
        }

        private static void ExportSceneBlockout()
        {
            ActToolkitEditorUtilities.EnsureGeneratedFolders();

            BlockoutElement[] elements = FindObjectsByType<BlockoutElement>(FindObjectsInactive.Exclude);
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
            string path = ActToolkitEditorUtilities.GeneratedFolder + "/Blockouts/" + safeSceneName + ".blockout.json";
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
