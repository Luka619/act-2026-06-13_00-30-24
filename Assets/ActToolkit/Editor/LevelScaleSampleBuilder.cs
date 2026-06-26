using System.IO;
using ActToolkit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ActToolkit.EditorTools
{
    public static class LevelScaleSampleBuilder
    {
        private const string ScenePath = ActToolkitEditorUtilities.GeneratedFolder + "/Scenes/Blockout_Level.unity";
        private const string BlockoutRootName = "Blockout_Root";
        private const string SampleRootName = "Scale_Experience_Samples";

        [MenuItem(ActToolkitMenu.LevelRoot + "/Build Scale Experience Samples", false, 44)]
        public static void BuildInBlockoutLevel()
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogError("[ActToolkit] Blockout level scene not found: " + ScenePath);
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            ActToolkitEditorUtilities.EnsureGeneratedFolders();

            Transform blockoutRoot = FindOrCreateRoot(BlockoutRootName).transform;
            Transform sampleRoot = ResetSampleRoot(blockoutRoot);

            BuildSharedGround(sampleRoot);
            BuildTightPassage(sampleRoot, new Vector3(6f, 0f, 14f));
            BuildComfortPassage(sampleRoot, new Vector3(16f, 0f, 14f));
            BuildDuelPocket(sampleRoot, new Vector3(28f, 0f, 14f));
            BuildSkirmishPocket(sampleRoot, new Vector3(43f, 0f, 14f));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[ActToolkit] Built scale experience samples in " + ScenePath);
        }

        private static void BuildSharedGround(Transform parent)
        {
            CreateElement(parent, "ScaleSamples_ConnectedFloor", BlockoutElementKind.Floor, new Vector3(24f, -0.05f, 14f), new Vector3(54f, 0.1f, 20f), "scale.floor", 0);
            CreateElement(parent, "ScaleSamples_StartMarker", BlockoutElementKind.NavMarker, new Vector3(2f, 0.9f, 6.5f), new Vector3(0.7f, 1.8f, 0.7f), "scale.start", 0);
        }

        private static void BuildTightPassage(Transform parent, Vector3 center)
        {
            Transform root = CreateSectionRoot(parent, "A_TightPassage_2m");
            CreateElement(root, "TightPassage_Floor_2mClearance", BlockoutElementKind.Floor, center + new Vector3(0f, -0.045f, 0f), new Vector3(4.8f, 0.1f, 12f), "scale.tight.floor", 0);
            CreateElement(root, "TightPassage_LeftWall", BlockoutElementKind.Wall, center + new Vector3(-1.15f, 1.2f, 0f), new Vector3(0.3f, 2.4f, 12f), "scale.tight.wall", 0);
            CreateElement(root, "TightPassage_RightWall", BlockoutElementKind.Wall, center + new Vector3(1.15f, 1.2f, 0f), new Vector3(0.3f, 2.4f, 12f), "scale.tight.wall", 0);
            CreateElement(root, "TightPassage_LeftShoulderBlock", BlockoutElementKind.Block, center + new Vector3(-2.1f, 0.5f, -2.6f), new Vector3(1.2f, 1f, 2.2f), "scale.tight.obstacle", 0);
            CreateElement(root, "TightPassage_RightShoulderBlock", BlockoutElementKind.Block, center + new Vector3(2.1f, 0.5f, 2.7f), new Vector3(1.2f, 1f, 2.2f), "scale.tight.obstacle", 0);
            CreateElement(root, "TightPassage_ExitDummy", BlockoutElementKind.DummySpawn, center + new Vector3(0f, 0.9f, 4.6f), new Vector3(0.7f, 1.8f, 0.7f), "scale.tight.dummy", 0);
            CreateLabel(root, "Label_TightPassage", "Tight Passage\n2m clear", center + new Vector3(0f, 0.08f, -6.5f));
        }

        private static void BuildComfortPassage(Transform parent, Vector3 center)
        {
            Transform root = CreateSectionRoot(parent, "B_ComfortPassage_3m");
            CreateElement(root, "ComfortPassage_Floor_3mClearance", BlockoutElementKind.Floor, center + new Vector3(0f, -0.04f, 0f), new Vector3(6f, 0.1f, 12f), "scale.comfort.floor", 0);
            CreateElement(root, "ComfortPassage_LeftWall", BlockoutElementKind.Wall, center + new Vector3(-1.65f, 1.2f, 0f), new Vector3(0.3f, 2.4f, 12f), "scale.comfort.wall", 0);
            CreateElement(root, "ComfortPassage_RightWall", BlockoutElementKind.Wall, center + new Vector3(1.65f, 1.2f, 0f), new Vector3(0.3f, 2.4f, 12f), "scale.comfort.wall", 0);
            CreateElement(root, "ComfortPassage_LowCover_Left", BlockoutElementKind.Cover, center + new Vector3(-2.6f, 0.5f, -2.5f), new Vector3(1.4f, 1f, 2.4f), "scale.comfort.cover", 0);
            CreateElement(root, "ComfortPassage_LowCover_Right", BlockoutElementKind.Cover, center + new Vector3(2.6f, 0.5f, 2.6f), new Vector3(1.4f, 1f, 2.4f), "scale.comfort.cover", 0);
            CreateElement(root, "ComfortPassage_ExitDummy", BlockoutElementKind.DummySpawn, center + new Vector3(0f, 0.9f, 4.6f), new Vector3(0.7f, 1.8f, 0.7f), "scale.comfort.dummy", 0);
            CreateLabel(root, "Label_ComfortPassage", "Comfort Passage\n3m clear", center + new Vector3(0f, 0.08f, -6.5f));
        }

        private static void BuildDuelPocket(Transform parent, Vector3 center)
        {
            Transform root = CreateSectionRoot(parent, "C_DuelPocket_6m");
            CreateElement(root, "DuelPocket_Floor", BlockoutElementKind.Floor, center + new Vector3(0f, -0.035f, 0f), new Vector3(8f, 0.1f, 8f), "scale.duel.floor", 0);
            CreateElement(root, "DuelPocket_CombatZone_6m", BlockoutElementKind.CombatZone, center + new Vector3(0f, 1f, 0f), new Vector3(6f, 2f, 6f), "scale.duel.zone", 0);
            CreateElement(root, "DuelPocket_CornerPillar_A", BlockoutElementKind.Block, center + new Vector3(-3.4f, 0.6f, -3.4f), new Vector3(0.8f, 1.2f, 0.8f), "scale.duel.boundary", 0);
            CreateElement(root, "DuelPocket_CornerPillar_B", BlockoutElementKind.Block, center + new Vector3(3.4f, 0.6f, -3.4f), new Vector3(0.8f, 1.2f, 0.8f), "scale.duel.boundary", 0);
            CreateElement(root, "DuelPocket_CornerPillar_C", BlockoutElementKind.Block, center + new Vector3(-3.4f, 0.6f, 3.4f), new Vector3(0.8f, 1.2f, 0.8f), "scale.duel.boundary", 0);
            CreateElement(root, "DuelPocket_CornerPillar_D", BlockoutElementKind.Block, center + new Vector3(3.4f, 0.6f, 3.4f), new Vector3(0.8f, 1.2f, 0.8f), "scale.duel.boundary", 0);
            CreateElement(root, "DuelPocket_Dummy", BlockoutElementKind.DummySpawn, center + new Vector3(0f, 0.9f, 2.2f), new Vector3(0.7f, 1.8f, 0.7f), "scale.duel.dummy", 0);
            CreateLabel(root, "Label_DuelPocket", "Duel Pocket\n6m", center + new Vector3(0f, 0.08f, -4.8f));
        }

        private static void BuildSkirmishPocket(Transform parent, Vector3 center)
        {
            Transform root = CreateSectionRoot(parent, "D_SkirmishPocket_10m");
            CreateElement(root, "SkirmishPocket_Floor", BlockoutElementKind.Floor, center + new Vector3(0f, -0.03f, 0f), new Vector3(13f, 0.1f, 13f), "scale.skirmish.floor", 0);
            CreateElement(root, "SkirmishPocket_CombatZone_10m", BlockoutElementKind.CombatZone, center + new Vector3(0f, 1f, 0f), new Vector3(10f, 2f, 10f), "scale.skirmish.zone", 0);
            CreateElement(root, "SkirmishPocket_Cover_Center", BlockoutElementKind.Cover, center + new Vector3(0f, 0.5f, 0f), new Vector3(2.4f, 1f, 1.1f), "scale.skirmish.cover", 0);
            CreateElement(root, "SkirmishPocket_Cover_Left", BlockoutElementKind.Cover, center + new Vector3(-3.2f, 0.5f, -1.8f), new Vector3(1.2f, 1f, 2.8f), "scale.skirmish.cover", 0);
            CreateElement(root, "SkirmishPocket_Cover_Right", BlockoutElementKind.Cover, center + new Vector3(3.3f, 0.5f, 2.2f), new Vector3(1.2f, 1f, 2.8f), "scale.skirmish.cover", 0);
            CreateElement(root, "SkirmishPocket_Dummy_A", BlockoutElementKind.DummySpawn, center + new Vector3(-2.4f, 0.9f, 3.6f), new Vector3(0.7f, 1.8f, 0.7f), "scale.skirmish.dummy", 0);
            CreateElement(root, "SkirmishPocket_Dummy_B", BlockoutElementKind.DummySpawn, center + new Vector3(2.8f, 0.9f, 4f), new Vector3(0.7f, 1.8f, 0.7f), "scale.skirmish.dummy", 0);
            CreateElement(root, "SkirmishPocket_Objective", BlockoutElementKind.Objective, center + new Vector3(0f, 0.7f, -3.6f), new Vector3(2.2f, 1.4f, 2.2f), "scale.skirmish.objective", 0);
            CreateLabel(root, "Label_SkirmishPocket", "Skirmish Pocket\n10m", center + new Vector3(0f, 0.08f, -6.9f));
        }

        private static Transform FindOrCreateRoot(string rootName)
        {
            GameObject root = GameObject.Find(rootName);
            if (root == null)
            {
                root = new GameObject(rootName);
            }

            return root.transform;
        }

        private static Transform ResetSampleRoot(Transform parent)
        {
            Transform existing = parent.Find(SampleRootName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            GameObject root = new GameObject(SampleRootName);
            root.transform.SetParent(parent, false);
            return root.transform;
        }

        private static Transform CreateSectionRoot(Transform parent, string sectionName)
        {
            GameObject root = new GameObject(sectionName);
            root.transform.SetParent(parent, false);
            return root.transform;
        }

        private static GameObject CreateElement(Transform parent, string name, BlockoutElementKind kind, Vector3 position, Vector3 size, string tag, int team)
        {
            PrimitiveType primitive = IsPointMarkerKind(kind) ? PrimitiveType.Capsule : PrimitiveType.Cube;
            GameObject go = GameObject.CreatePrimitive(primitive);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.localScale = size;

            BlockoutElement element = go.AddComponent<BlockoutElement>();
            element.kind = kind;
            element.gameplayTag = tag;
            element.team = team;
            element.serverAuthoritativeCollision = IsSolidKind(kind);
            element.logicalSize = size;
            element.gizmoColor = ActToolkitEditorUtilities.ColorFor(kind);
            element.EnsureId();

            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = !IsSolidKind(kind);
            }

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = ResolveMaterial(kind);
            }

            EditorUtility.SetDirty(element);
            return go;
        }

        private static void CreateLabel(Transform parent, string name, string text, Vector3 position)
        {
            GameObject label = new GameObject(name);
            label.transform.SetParent(parent, true);
            label.transform.position = position;
            label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            TextMesh textMesh = label.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = 0.35f;
            textMesh.fontSize = 36;
            textMesh.color = new Color(0.82f, 0.93f, 1f, 1f);
        }

        private static Material ResolveMaterial(BlockoutElementKind kind)
        {
            string materialPath = ActToolkitEditorUtilities.MaterialsFolder + "/M_Blockout_" + kind + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            return material != null
                ? material
                : ActToolkitEditorUtilities.GetOrCreateBlockoutMaterial(kind, ActToolkitEditorUtilities.ColorFor(kind));
        }

        private static bool IsPointMarkerKind(BlockoutElementKind kind)
        {
            return kind == BlockoutElementKind.SpawnPoint
                || kind == BlockoutElementKind.EnemySpawn
                || kind == BlockoutElementKind.DummySpawn
                || kind == BlockoutElementKind.NavMarker;
        }

        private static bool IsSolidKind(BlockoutElementKind kind)
        {
            return kind == BlockoutElementKind.Block
                || kind == BlockoutElementKind.Floor
                || kind == BlockoutElementKind.Wall
                || kind == BlockoutElementKind.Ramp
                || kind == BlockoutElementKind.Platform
                || kind == BlockoutElementKind.Cover;
        }
    }
}
