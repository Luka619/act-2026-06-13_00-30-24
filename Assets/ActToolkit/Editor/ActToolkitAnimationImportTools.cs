using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ActToolkit.EditorTools
{
    public static class ActToolkitAnimationImportTools
    {
        private const string PreviewClipFolder = "Assets/External/TestAssets/Animations/PreviewClips/Quaternius_UniversalAnimationLibrary2_Standard";

        [MenuItem(ActToolkitMenu.TempRoot + "/Animation Import/Make Mannequin Test Clips In-Place", false, 930)]
        public static void MakeMannequinTestClipsInPlace()
        {
            string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { PreviewClipFolder });
            HashSet<string> importerPaths = new HashSet<string>();

            foreach (string guid in modelGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is ModelImporter importer && importer.importAnimation)
                {
                    importerPaths.Add(path);
                }
            }

            MakeImporterPathsRootXzInPlace(importerPaths, "mannequin test clips");
        }

        [MenuItem(ActToolkitMenu.TempRoot + "/Animation Import/Make Configured Clips Root-XZ In-Place", false, 931)]
        public static void MakeConfiguredClipsRootXzInPlace()
        {
            List<AnimationClip> configuredClips = CollectConfiguredClips();
            HashSet<string> importerPaths = new HashSet<string>();
            foreach (AnimationClip configuredClip in configuredClips)
            {
                string path = AssetDatabase.GetAssetPath(configuredClip);
                if (AssetImporter.GetAtPath(path) is ModelImporter importer && importer.importAnimation)
                {
                    importerPaths.Add(path);
                }
            }

            MakeImporterPathsRootXzInPlace(importerPaths, "configured clips");
        }

        [MenuItem(ActToolkitMenu.DiagnosticsRoot + "/Animation/Check Configured Clip Root-XZ Settings", false, 421)]
        public static void CheckConfiguredClipRootXzSettings()
        {
            List<AnimationClip> configuredClips = CollectConfiguredClips();
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("[ActToolkit] Configured clip Root-XZ import settings:");
            builder.AppendLine("Bake Root XZ only affects Unity root motion extraction. Bone or hips translation can still be visible in the pose.");

            if (configuredClips.Count == 0)
            {
                builder.AppendLine("- No configured clips found.");
            }
            else
            {
                foreach (AnimationClip configuredClip in configuredClips)
                {
                    AppendClipImportReport(builder, configuredClip);
                }
            }

            Debug.Log(builder.ToString());
        }

        private static void MakeImporterPathsRootXzInPlace(HashSet<string> importerPaths, string label)
        {
            int changedImporters = 0;
            int changedClips = 0;

            foreach (string path in importerPaths)
            {
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null || !importer.importAnimation)
                {
                    continue;
                }

                ModelImporterClipAnimation[] clips = GetEditableClips(importer);
                int importerClipChanges = MakeClipArrayRootXzInPlace(clips);
                if (importerClipChanges <= 0)
                {
                    continue;
                }

                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                changedImporters++;
                changedClips += importerClipChanges;
            }

            AssetDatabase.Refresh();

            Debug.Log("[ActToolkit] Root-XZ in-place import complete for "
                + label
                + ". Importers changed: "
                + changedImporters
                + ", clips changed: "
                + changedClips
                + ".");
        }

        [MenuItem(ActToolkitMenu.TempRoot + "/Animation Import/Make Selected Model Clips In-Place", false, 932)]
        public static void MakeSelectedModelClipsInPlace()
        {
            Object[] selectedAssets = Selection.objects;
            HashSet<string> importerPaths = new HashSet<string>();

            foreach (Object asset in selectedAssets)
            {
                string path = AssetDatabase.GetAssetPath(asset);
                if (AssetImporter.GetAtPath(path) is ModelImporter importer && importer.importAnimation)
                {
                    importerPaths.Add(path);
                }
            }

            MakeImporterPathsRootXzInPlace(importerPaths, "selected model clips");
        }

        [MenuItem(ActToolkitMenu.DiagnosticsRoot + "/Animation/Check Selected Model Clip Root-XZ Settings", false, 422)]
        public static void CheckSelectedModelClipRootXzSettings()
        {
            Object[] selectedAssets = Selection.objects;
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("[ActToolkit] Selected model clip Root-XZ import settings:");
            builder.AppendLine("Bake Root XZ only affects Unity root motion extraction. Bone or hips translation can still be visible in the pose.");

            int reportCount = 0;
            foreach (Object asset in selectedAssets)
            {
                string path = AssetDatabase.GetAssetPath(asset);
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null || !importer.importAnimation)
                {
                    continue;
                }

                ModelImporterClipAnimation[] clips = GetEditableClips(importer);
                builder.AppendLine("- " + path + " (" + clips.Length + " clips)");
                for (int i = 0; i < clips.Length; i++)
                {
                    AppendImporterClipReport(builder, "  ", clips[i]);
                    reportCount++;
                }
            }

            if (reportCount == 0)
            {
                builder.AppendLine("- No selected model importers found.");
            }

            Debug.Log(builder.ToString());
        }

        [MenuItem(ActToolkitMenu.TempRoot + "/Animation Import/Make Selected Model Clips In-Place", true)]
        private static bool ValidateMakeSelectedModelClipsInPlace()
        {
            return HasSelectedModelImporter();
        }

        [MenuItem(ActToolkitMenu.DiagnosticsRoot + "/Animation/Check Selected Model Clip Root-XZ Settings", true)]
        private static bool ValidateCheckSelectedModelClipRootXzSettings()
        {
            return HasSelectedModelImporter();
        }

        private static bool HasSelectedModelImporter()
        {
            foreach (Object asset in Selection.objects)
            {
                if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(asset)) is ModelImporter importer && importer.importAnimation)
                {
                    return true;
                }
            }

            return false;
        }

        private static ModelImporterClipAnimation[] GetEditableClips(ModelImporter importer)
        {
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips != null && clips.Length > 0)
            {
                return clips;
            }

            clips = importer.defaultClipAnimations;
            return clips == null ? new ModelImporterClipAnimation[0] : clips;
        }

        private static int MakeClipArrayRootXzInPlace(ModelImporterClipAnimation[] clips)
        {
            int changedClips = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                if (MakeClipInPlace(clips[i]))
                {
                    changedClips++;
                }
            }

            return changedClips;
        }

        private static bool MakeClipInPlace(ModelImporterClipAnimation clip)
        {
            bool changed = false;

            if (!clip.lockRootPositionXZ)
            {
                clip.lockRootPositionXZ = true;
                changed = true;
            }

            if (clip.keepOriginalPositionXZ)
            {
                clip.keepOriginalPositionXZ = false;
                changed = true;
            }

            return changed;
        }

        private static List<AnimationClip> CollectConfiguredClips()
        {
            HashSet<AnimationClip> uniqueClips = new HashSet<AnimationClip>();
            List<AnimationClip> clips = new List<AnimationClip>();

            string[] profileGuids = AssetDatabase.FindAssets("t:CharacterActionProfile", new[] { ActToolkitEditorUtilities.CombatMvpFolder });
            foreach (string guid in profileGuids)
            {
                CharacterActionProfile profile = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (profile == null)
                {
                    continue;
                }

                AddClip(profile.idleClip, uniqueClips, clips);
                AddClip(profile.walkClip, uniqueClips, clips);
                AddClip(profile.moveClip, uniqueClips, clips);
                AddClip(profile.jumpStartClip, uniqueClips, clips);
                AddClip(profile.jumpLoopClip, uniqueClips, clips);
                AddClip(profile.jumpLandClip, uniqueClips, clips);
                if (profile.comboTable != null && profile.comboTable.actions != null)
                {
                    foreach (CombatAnimationDefinition definition in profile.comboTable.actions)
                    {
                        AddClip(definition == null ? null : definition.clip, uniqueClips, clips);
                    }
                }
            }

            string[] definitionGuids = AssetDatabase.FindAssets("t:CombatAnimationDefinition", new[] { ActToolkitEditorUtilities.DefaultCombatDefinitionFolder });
            foreach (string guid in definitionGuids)
            {
                CombatAnimationDefinition definition = AssetDatabase.LoadAssetAtPath<CombatAnimationDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                AddClip(definition == null ? null : definition.clip, uniqueClips, clips);
            }

            return clips;
        }

        private static void AddClip(AnimationClip clip, HashSet<AnimationClip> uniqueClips, List<AnimationClip> clips)
        {
            if (clip == null || uniqueClips.Contains(clip))
            {
                return;
            }

            uniqueClips.Add(clip);
            clips.Add(clip);
        }

        private static void AppendClipImportReport(StringBuilder builder, AnimationClip clip)
        {
            if (clip == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(clip);
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                builder.AppendLine("- " + clip.name + " -> not a model importer: " + path);
                return;
            }

            ModelImporterClipAnimation importerClip = FindImporterClip(importer, clip.name);
            if (importerClip == null)
            {
                builder.AppendLine("- " + clip.name + " -> import clip not found in: " + path);
                return;
            }

            builder.AppendLine("- " + clip.name + " -> " + path);
            AppendImporterClipReport(builder, "  ", importerClip);
        }

        private static ModelImporterClipAnimation FindImporterClip(ModelImporter importer, string clipName)
        {
            ModelImporterClipAnimation[] clips = GetEditableClips(importer);
            for (int i = 0; i < clips.Length; i++)
            {
                if (string.Equals(clips[i].name, clipName, System.StringComparison.Ordinal)
                    || string.Equals(clips[i].takeName, clipName, System.StringComparison.Ordinal))
                {
                    return clips[i];
                }
            }

            return null;
        }

        private static void AppendImporterClipReport(StringBuilder builder, string indent, ModelImporterClipAnimation clip)
        {
            builder.AppendLine(indent
                + clip.name
                + " rootXZBake="
                + clip.lockRootPositionXZ
                + ", keepOriginalXZ="
                + clip.keepOriginalPositionXZ
                + ", rootYBake="
                + clip.lockRootHeightY
                + ", keepOriginalY="
                + clip.keepOriginalPositionY
                + ", rootRotationBake="
                + clip.lockRootRotation
                + ", keepOriginalRotation="
                + clip.keepOriginalOrientation);
        }
    }
}
