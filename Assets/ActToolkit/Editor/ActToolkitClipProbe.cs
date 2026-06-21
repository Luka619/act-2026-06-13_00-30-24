using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ActToolkit.EditorTools
{
    public static class ActToolkitClipProbe
    {
        private const string ReportPath = "Temp/ActToolkitClipProbe.txt";
        private const string MannequinModelPath = "Assets/External/TestAssets/Characters/Quaternius_UniversalAnimationLibrary2_Standard/Mannequin_F.fbx";
        private const string Ual1Path = "Assets/External/TestAssets/Animations/PreviewClips/Quaternius_UniversalAnimationLibrary_Standard/UAL1_Standard.fbx";

        [MenuItem("Act Toolkit/Probe UAL1 Locomotion Clips")]
        public static void ProbeUal1LocomotionClips()
        {
            ActToolkitSkeletonCompatibility.ClearCache();

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Model\t" + MannequinModelPath);
            builder.AppendLine("Source\t" + Ual1Path);

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(Ual1Path);
            foreach (Object asset in assets)
            {
                if (asset is not AnimationClip clip || !IsLocomotionCandidate(clip.name))
                {
                    continue;
                }

                ActToolkitSkeletonCompatibility.ClipCompatibilityDiagnostic diagnostic =
                    ActToolkitSkeletonCompatibility.GetClipCompatibilityDiagnostic(MannequinModelPath, clip);

                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long localId);
                builder.Append(diagnostic.compatible ? "OK" : "BAD");
                builder.Append('\t').Append(clip.name);
                builder.Append('\t').Append("fileID=").Append(localId);
                builder.Append('\t').Append("guid=").Append(guid);
                builder.Append('\t').Append("length=").Append(clip.length.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                builder.Append('\t').Append("fps=").Append(clip.frameRate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                builder.Append('\t').Append("bindings=").Append(diagnostic.transformBindingCount);
                builder.Append('\t').Append("missing=").Append(diagnostic.missingBindingCount);
                builder.Append('\t').Append("firstMissing=").Append(string.IsNullOrWhiteSpace(diagnostic.firstMissingBinding) ? "-" : diagnostic.firstMissingBinding);
                builder.Append('\t').Append("reason=").Append(diagnostic.reason);
                builder.AppendLine();
            }

            File.WriteAllText(ReportPath, builder.ToString(), Encoding.UTF8);
            Debug.Log("[ActToolkit] UAL1 locomotion probe written to " + Path.GetFullPath(ReportPath) + "\n" + builder);
        }

        private static bool IsLocomotionCandidate(string clipName)
        {
            return Contains(clipName, "Idle")
                || Contains(clipName, "Walk")
                || Contains(clipName, "Jog")
                || Contains(clipName, "Sprint")
                || Contains(clipName, "Run")
                || Contains(clipName, "Crouch");
        }

        private static bool Contains(string source, string value)
        {
            return source != null && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
