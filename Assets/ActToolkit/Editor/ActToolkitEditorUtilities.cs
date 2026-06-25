using System.IO;
using ActToolkit;
using UnityEditor;
using UnityEngine;

namespace ActToolkit.EditorTools
{
    internal static class ActToolkitEditorUtilities
    {
        public const string RootFolder = "Assets/ActToolkit";
        public const string GeneratedFolder = RootFolder + "/Generated";
        public const string MaterialsFolder = RootFolder + "/Generated/Materials";
        public const string CombatMvpFolder = GeneratedFolder + "/CombatMvp";
        public const string DefaultCombatDefinitionFolder = CombatMvpFolder + "/Actions";
        public const string ExternalFolder = "Assets/External";
        public const string TestAssetsFolder = ExternalFolder + "/TestAssets";
        public const string DefaultModelFolder = TestAssetsFolder + "/Characters";
        public const string DefaultAnimationFolder = TestAssetsFolder + "/Animations";
        public const string DefaultPreviewClipFolder = DefaultAnimationFolder + "/PreviewClips";
        public const string DefaultEnvironmentFolder = TestAssetsFolder + "/Environments";
        public const string DefaultLicenseFolder = TestAssetsFolder + "/Licenses";

        public static void EnsureGeneratedFolders()
        {
            EnsureFolder("Assets", "ActToolkit");
            EnsureFolder(RootFolder, "Generated");
            EnsureFolder(GeneratedFolder, "Materials");
            EnsureFolder(GeneratedFolder, "Blockouts");
            EnsureFolder(GeneratedFolder, "Animations");
            EnsureFolder(GeneratedFolder, "CombatMvp");
            EnsureFolder(CombatMvpFolder, "Actions");
        }

        public static void EnsureExternalAssetFolders()
        {
            EnsureFolder("Assets", "External");
            EnsureFolder(ExternalFolder, "TestAssets");
            EnsureFolder(TestAssetsFolder, "Characters");
            EnsureFolder(TestAssetsFolder, "Animations");
            EnsureFolder(DefaultAnimationFolder, "PreviewClips");
            EnsureFolder(TestAssetsFolder, "Environments");
            EnsureFolder(TestAssetsFolder, "Licenses");
        }

        public static string EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, name);
            }

            return path;
        }

        public static Material GetOrCreateBlockoutMaterial(BlockoutElementKind kind, Color color)
        {
            EnsureGeneratedFolders();

            string path = MaterialsFolder + "/M_Blockout_" + kind + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                ConfigureBlockoutMaterial(material, color);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
                return material;
            }

            material = new Material(FindBlockoutShader());
            material.name = "M_Blockout_" + kind;
            ConfigureBlockoutMaterial(material, color);
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static Shader FindBlockoutShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                return shader;
            }

            return Shader.Find("Standard");
        }

        private static void ConfigureBlockoutMaterial(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            Shader shader = FindBlockoutShader();
            if (shader != null && material.shader != shader)
            {
                material.shader = shader;
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

        public static Color ColorFor(BlockoutElementKind kind)
        {
            switch (kind)
            {
                case BlockoutElementKind.Floor:
                    return new Color(0.28f, 0.32f, 0.36f, 1f);
                case BlockoutElementKind.Wall:
                    return new Color(0.38f, 0.43f, 0.48f, 1f);
                case BlockoutElementKind.Ramp:
                    return new Color(0.50f, 0.56f, 0.44f, 1f);
                case BlockoutElementKind.Platform:
                    return new Color(0.28f, 0.52f, 0.67f, 1f);
                case BlockoutElementKind.Cover:
                    return new Color(0.60f, 0.46f, 0.32f, 1f);
                case BlockoutElementKind.SpawnPoint:
                    return new Color(0.18f, 0.75f, 0.44f, 1f);
                case BlockoutElementKind.Objective:
                    return new Color(0.92f, 0.72f, 0.20f, 1f);
                case BlockoutElementKind.TriggerVolume:
                    return new Color(0.48f, 0.40f, 0.94f, 0.45f);
                case BlockoutElementKind.KillZone:
                    return new Color(0.88f, 0.22f, 0.22f, 0.55f);
                case BlockoutElementKind.NavMarker:
                    return new Color(0.20f, 0.76f, 0.86f, 1f);
                case BlockoutElementKind.EnemySpawn:
                    return new Color(0.88f, 0.50f, 0.24f, 1f);
                case BlockoutElementKind.DummySpawn:
                    return new Color(0.78f, 0.66f, 0.30f, 1f);
                case BlockoutElementKind.CombatZone:
                    return new Color(0.38f, 0.70f, 0.36f, 0.45f);
                default:
                    return new Color(0.48f, 0.58f, 0.68f, 1f);
            }
        }

        public static Vector3 Snap(Vector3 value, float grid)
        {
            if (grid <= 0f)
            {
                return value;
            }

            return new Vector3(
                Mathf.Round(value.x / grid) * grid,
                Mathf.Round(value.y / grid) * grid,
                Mathf.Round(value.z / grid) * grid);
        }

        public static Vector3 SceneViewGroundPoint(float grid)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null)
            {
                return Vector3.zero;
            }

            Ray ray = new Ray(sceneView.camera.transform.position, sceneView.camera.transform.forward);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float distance))
            {
                return Snap(ray.GetPoint(distance), grid);
            }

            return Snap(sceneView.pivot, grid);
        }

        public static void WriteTextAsset(string path, string content)
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content);
            AssetDatabase.ImportAsset(path);
        }
    }

    internal static class ActToolkitMenu
    {
        public const string Root = "Act Toolkit";
        public const string CharacterRoot = Root + "/Character";
        public const string LevelRoot = Root + "/Level";
        public const string PlaytestRoot = Root + "/Playtest";
        public const string DiagnosticsRoot = Root + "/Diagnostics";

        // One-off probes, repair jobs, and asset-mutation helpers live here.
        public const string TempRoot = Root + "/Temp";
    }
}
