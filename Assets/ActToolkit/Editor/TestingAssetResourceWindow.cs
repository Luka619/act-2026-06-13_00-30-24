using UnityEditor;
using UnityEngine;

namespace ActToolkit.EditorTools
{
    public sealed class TestingAssetResourceWindow : EditorWindow
    {
        private Vector2 scroll;

        private readonly TestAssetSource[] sources =
        {
            new TestAssetSource("Quaternius", "https://quaternius.com/", "CC0 on free packs", "Low-poly characters, monsters, props, and environments. Good first stop for prototype-friendly Unity/Godot/Unreal assets."),
            new TestAssetSource("Quaternius Universal Animation Library", "https://quaternius.com/packs/universalanimationlibrary.html", "CC0 for the free portion", "Humanoid action and locomotion clips for retargeting. Useful for jog, sprint, dodge, combat, death, and traversal tests."),
            new TestAssetSource("Quaternius Universal Animation Library 2", "https://quaternius.com/packs/universalanimationlibrary2.html", "CC0 for the free portion", "More action-focused humanoid clips including melee combos, parkour movement, and zombie locomotion."),
            new TestAssetSource("Kenney 3D Assets", "https://kenney.nl/assets", "Many packs are CC0; verify per pack page", "Clean prototype art with permissive licensing. Useful for props, pickups, modular scenes, UI, and simple animated characters."),
            new TestAssetSource("Kenney Animated Characters 3", "https://kenney-assets.itch.io/animated-characters-3", "CC0 1.0 Universal", "Small humanoid character set for quick movement and scale tests."),
            new TestAssetSource("Mixamo", "https://www.mixamo.com/", "Free with Adobe ID; do not redistribute as an asset library", "Fastest way to test humanoid combat, locomotion, hit reactions, and emotes. Export FBX for Unity."),
            new TestAssetSource("Khronos glTF Sample Assets", "https://github.khronos.org/glTF-Assets/", "Varies by model; each entry lists license", "Reliable glTF/GLB test models for loader and animation preview tests. Use for technical validation rather than final art."),
            new TestAssetSource("Khronos glTF Sample Models GitHub", "https://github.com/KhronosGroup/glTF-Sample-Models", "Varies by model", "Raw sample-model repository. The Fox sample has Survey, Walk, and Run cycles for animation playback checks.")
        };

        [MenuItem("Tools/Act Toolkit/Testing Asset Sources")]
        public static void Open()
        {
            TestingAssetResourceWindow window = GetWindow<TestingAssetResourceWindow>();
            window.titleContent = new GUIContent("Asset Sources");
            window.minSize = new Vector2(520f, 420f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Testing Asset Sources", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Use these sources for prototype characters, animations, props, and loader validation. Keep each downloaded pack's license text in Assets/External/TestAssets/Licenses.", MessageType.Info);

            if (GUILayout.Button("Create Test Asset Folder Layout", GUILayout.Height(30f)))
            {
                ActToolkitEditorUtilities.EnsureExternalAssetFolders();
                AssetDatabase.Refresh();
            }

            EditorGUILayout.Space(6f);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (TestAssetSource source in sources)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(source.name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("License", source.license);
                EditorGUILayout.LabelField(source.notes, EditorStyles.wordWrappedLabel);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.SelectableLabel(source.url, GUILayout.Height(18f));
                if (GUILayout.Button("Open", GUILayout.Width(70f)))
                {
                    Application.OpenURL(source.url);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private sealed class TestAssetSource
        {
            public readonly string name;
            public readonly string url;
            public readonly string license;
            public readonly string notes;

            public TestAssetSource(string name, string url, string license, string notes)
            {
                this.name = name;
                this.url = url;
                this.license = license;
                this.notes = notes;
            }
        }
    }
}
