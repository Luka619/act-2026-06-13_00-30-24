using System.Collections.Generic;
using System.IO;
using System.Text;
using ActToolkit;
using UnityEditor;
using UnityEngine;

namespace ActToolkit.EditorTools
{
    internal static class ActToolkitConfiguredAnimationAudit
    {
        private const string ReportPath = "Temp/ActToolkitConfiguredAnimationAudit.txt";
        private const string RequestPath = "Temp/ActToolkitRunAnimationAudit.flag";
        private const string RepairPath = "Temp/ActToolkitRepairAnimationAudit.flag";
        private const string Ual2Path = "Assets/External/TestAssets/Animations/PreviewClips/Quaternius_UniversalAnimationLibrary2_Standard/UAL2_Standard.fbx";

        [InitializeOnLoadMethod]
        private static void RunRequestedAuditAfterReload()
        {
            if (File.Exists(RepairPath))
            {
                File.Delete(RepairPath);
                EditorApplication.delayCall += RepairConfiguredAnimationsAndAudit;
                return;
            }

            if (!File.Exists(RequestPath))
            {
                return;
            }

            File.Delete(RequestPath);
            EditorApplication.delayCall += AuditConfiguredAnimationCompatibility;
        }

        [MenuItem(ActToolkitMenu.DiagnosticsRoot + "/Animation/Audit Configured Animation Compatibility", false, 420)]
        public static void AuditConfiguredAnimationCompatibility()
        {
            string report = BuildReport();
            File.WriteAllText(ReportPath, report, Encoding.UTF8);
            Debug.Log("[ActToolkit] Configured animation compatibility audit written to " + Path.GetFullPath(ReportPath) + "\n" + report);
        }

        [MenuItem(ActToolkitMenu.TempRoot + "/Animation Repair/Repair Configured Animation Compatibility", false, 920)]
        public static void RepairConfiguredAnimationsAndAudit()
        {
            int changed = RepairConfiguredAnimations();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string report = "Repair\tchanged=" + changed + "\n" + BuildReport();
            File.WriteAllText(ReportPath, report, Encoding.UTF8);
            Debug.Log("[ActToolkit] Configured animation compatibility repair finished. Changed " + changed + " definitions.\n" + report);
        }

        private static int RepairConfiguredAnimations()
        {
            Dictionary<string, string> clipNamesByActionId = new Dictionary<string, string>
            {
                { "action.light_1", "Armature|Sword_Regular_A" },
                { "action.light_2", "Armature|Sword_Regular_B" },
                { "action.light_3", "Armature|Sword_Regular_C" },
                { "action.heavy_1", "Armature|Sword_Regular_Combo" },
                { "action.jump", "Armature|NinjaJump_Start" },
                { "action.jump_attack", "Armature|Sword_Dash_RM" },
                { "action.roll", "Armature|Slide_Start" },
                { "action.block", "Armature|Sword_Block" },
                { "action.throw", "Armature|OverhandThrow" },
            };

            Dictionary<string, AnimationClip> clipsByActionId = new Dictionary<string, AnimationClip>();
            foreach (KeyValuePair<string, string> pair in clipNamesByActionId)
            {
                AnimationClip clip = LoadClipByName(Ual2Path, pair.Value);
                if (clip != null)
                {
                    clipsByActionId[pair.Key] = clip;
                }
            }

            int changed = 0;
            string[] definitionGuids = AssetDatabase.FindAssets("t:CombatAnimationDefinition", new[] { ActToolkitEditorUtilities.DefaultCombatDefinitionFolder });
            foreach (string definitionGuid in definitionGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(definitionGuid);
                CombatAnimationDefinition definition = AssetDatabase.LoadAssetAtPath<CombatAnimationDefinition>(path);
                if (definition == null)
                {
                    continue;
                }

                string actionId = definition.EnsureInternalActionId();
                if (!clipsByActionId.TryGetValue(actionId, out AnimationClip targetClip) || targetClip == null)
                {
                    continue;
                }

                string currentPath = definition.clip == null ? string.Empty : AssetDatabase.GetAssetPath(definition.clip);
                bool shouldRepair = definition.clip == null
                    || definition.clip.name != targetClip.name
                    || currentPath.StartsWith("Assets/External/TestAssets/_ArchivedIncompatibleForMannequinF/", System.StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(currentPath, Ual2Path, System.StringComparison.OrdinalIgnoreCase);

                if (!shouldRepair)
                {
                    continue;
                }

                definition.clip = targetClip;
                EditorUtility.SetDirty(definition);
                changed++;
            }

            return changed;
        }

        private static AnimationClip LoadClipByName(string path, string clipName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object asset in assets)
            {
                if (asset is AnimationClip clip && clip.name == clipName)
                {
                    return clip;
                }
            }

            return null;
        }

        private static string BuildReport()
        {
            ActToolkitSkeletonCompatibility.ClearCache();

            StringBuilder builder = new StringBuilder();
            int checkedCount = 0;
            int incompatibleCount = 0;

            string[] profileGuids = AssetDatabase.FindAssets("t:CharacterActionProfile", new[] { ActToolkitEditorUtilities.CombatMvpFolder });
            foreach (string profileGuid in profileGuids)
            {
                string profilePath = AssetDatabase.GUIDToAssetPath(profileGuid);
                CharacterActionProfile profile = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(profilePath);
                if (profile == null)
                {
                    continue;
                }

                string modelPath = profile.modelPrefab == null ? string.Empty : AssetDatabase.GetAssetPath(profile.modelPrefab);
                builder.AppendLine("Profile\t" + profile.displayName + "\t" + profilePath);
                builder.AppendLine("Model\t" + (string.IsNullOrWhiteSpace(modelPath) ? "<none>" : modelPath));

                AuditClip(builder, profile.displayName, "Idle", string.Empty, modelPath, profile.idleClip, ref checkedCount, ref incompatibleCount);
                AuditClip(builder, profile.displayName, "Walk", string.Empty, modelPath, profile.walkClip, ref checkedCount, ref incompatibleCount);
                AuditClip(builder, profile.displayName, "Move", string.Empty, modelPath, profile.moveClip, ref checkedCount, ref incompatibleCount);

                if (profile.comboTable == null || profile.comboTable.actions == null)
                {
                    builder.AppendLine("ComboTable\t<none>");
                    builder.AppendLine();
                    continue;
                }

                string comboTablePath = AssetDatabase.GetAssetPath(profile.comboTable);
                builder.AppendLine("ComboTable\t" + comboTablePath);

                HashSet<CombatAnimationDefinition> seen = new HashSet<CombatAnimationDefinition>();
                foreach (CombatAnimationDefinition definition in profile.comboTable.actions)
                {
                    if (definition == null || !seen.Add(definition))
                    {
                        continue;
                    }

                    AuditClip(
                        builder,
                        profile.displayName,
                        "Action",
                        definition.DisplayName + " | " + definition.EnsureInternalActionId(),
                        modelPath,
                        definition.clip,
                        ref checkedCount,
                        ref incompatibleCount);
                }

                builder.AppendLine();
            }

            builder.AppendLine("Summary\tchecked=" + checkedCount + "\tincompatible=" + incompatibleCount);
            return builder.ToString();
        }

        private static void AuditClip(
            StringBuilder builder,
            string profileName,
            string field,
            string actionName,
            string modelPath,
            AnimationClip clip,
            ref int checkedCount,
            ref int incompatibleCount)
        {
            if (clip == null)
            {
                builder.AppendLine("EMPTY\t" + profileName + "\t" + field + "\t" + (string.IsNullOrWhiteSpace(actionName) ? "-" : actionName));
                return;
            }

            checkedCount++;
            ActToolkitSkeletonCompatibility.ClipCompatibilityDiagnostic diagnostic = ActToolkitSkeletonCompatibility.GetClipCompatibilityDiagnostic(modelPath, clip);
            if (!diagnostic.compatible)
            {
                incompatibleCount++;
            }

            string guid = string.Empty;
            long localId = 0;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out guid, out localId);

            builder.Append(diagnostic.compatible ? "OK" : "BAD");
            builder.Append('\t').Append(profileName);
            builder.Append('\t').Append(field);
            builder.Append('\t').Append(string.IsNullOrWhiteSpace(actionName) ? "-" : actionName);
            builder.Append('\t').Append("clip=").Append(clip.name);
            builder.Append('\t').Append("fileID=").Append(localId);
            builder.Append('\t').Append("guid=").Append(guid);
            builder.Append('\t').Append("path=").Append(AssetDatabase.GetAssetPath(clip));
            builder.Append('\t').Append("bindings=").Append(diagnostic.transformBindingCount);
            builder.Append('\t').Append("missing=").Append(diagnostic.missingBindingCount);
            builder.Append('\t').Append("firstMissing=").Append(string.IsNullOrWhiteSpace(diagnostic.firstMissingBinding) ? "-" : diagnostic.firstMissingBinding);
            builder.Append('\t').Append("reason=").Append(diagnostic.reason);
            builder.AppendLine();
        }
    }
}
