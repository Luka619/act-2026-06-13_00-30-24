using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ActToolkit.EditorTools
{
    public static class ActToolkitInPlaceClipBaker
    {
        private const string InPlaceFolder = ActToolkitEditorUtilities.CombatMvpFolder + "/InPlaceClips";
        private const float DriftThresholdMeters = 0.02f;
        private const int DefaultSampleRate = 60;

        [MenuItem("Act Toolkit/Animation/Report Configured Body Drift")]
        public static void ReportConfiguredBodyDrift()
        {
            BodyDriftReport report = BuildConfiguredBodyDriftReport();
            Debug.Log(report.ToLogText());
        }

        [MenuItem("Act Toolkit/Animation/Create In-Place Copies For Drifted Definitions")]
        public static void CreateInPlaceCopiesForDriftedDefinitions()
        {
            BodyDriftReport report = CreateInPlaceCopiesForConfiguredDefinitions(false);
            Debug.Log(report.ToLogText());
        }

        [MenuItem("Act Toolkit/Animation/Rebuild Configured In-Place Clips")]
        public static void RebuildConfiguredInPlaceClips()
        {
            BodyDriftReport report = RebuildConfiguredInPlaceClips(false);
            Debug.Log(report.ToLogText());
        }

        public static void BatchReportConfiguredBodyDrift()
        {
            BodyDriftReport report = BuildConfiguredBodyDriftReport();
            WriteBatchReport("BodyDriftReport.txt", report.ToLogText());
        }

        public static void BatchCreateInPlaceCopiesForConfiguredDefinitions()
        {
            BodyDriftReport report = CreateInPlaceCopiesForConfiguredDefinitions(true);
            WriteBatchReport("InPlaceBakeReport.txt", report.ToLogText());
        }

        public static void BatchRebuildConfiguredInPlaceClips()
        {
            BodyDriftReport report = RebuildConfiguredInPlaceClips(true);
            WriteBatchReport("InPlaceRebuildReport.txt", report.ToLogText());
        }

        private static BodyDriftReport BuildConfiguredBodyDriftReport()
        {
            BodyDriftReport report = new BodyDriftReport("Configured body drift report");
            foreach (DefinitionBakeTarget target in CollectDefinitionBakeTargets())
            {
                DriftMeasurement measurement = MeasureBodyDrift(target);
                report.measurements.Add(measurement);
            }

            return report;
        }

        private static BodyDriftReport RebuildConfiguredInPlaceClips(bool saveAssets)
        {
            ActToolkitEditorUtilities.EnsureGeneratedFolders();
            ActToolkitEditorUtilities.EnsureFolder(ActToolkitEditorUtilities.CombatMvpFolder, "InPlaceClips");

            BodyDriftReport report = new BodyDriftReport("In-place rebuild report");
            foreach (DefinitionBakeTarget target in CollectDefinitionBakeTargets())
            {
                if (target.definition == null || target.definition.clip == null)
                {
                    continue;
                }

                string inPlacePath = AssetDatabase.GetAssetPath(target.definition.clip);
                if (!IsInPlaceClipPath(inPlacePath))
                {
                    continue;
                }

                AnimationClip sourceClip = ResolveSourceClipForInPlace(target.definition.clip);
                if (sourceClip == null)
                {
                    DriftMeasurement missingSource = new DriftMeasurement(target);
                    missingSource.status = "Missing source clip for " + target.definition.clip.name;
                    report.measurements.Add(missingSource);
                    continue;
                }

                int sampleRate = GetSampleRate(target.definition, sourceClip);
                AnimationClip rebuiltClip = BakeInPlaceClip(target, sourceClip, sampleRate, inPlacePath);
                if (rebuiltClip == null)
                {
                    DriftMeasurement failed = new DriftMeasurement(target);
                    failed.status = "Rebuild failed for " + sourceClip.name;
                    report.measurements.Add(failed);
                    continue;
                }

                target.definition.clip = rebuiltClip;
                EditorUtility.SetDirty(target.definition);
                report.createdClips.Add(inPlacePath);
                report.replacedDefinitions.Add(AssetDatabase.GetAssetPath(target.definition));
            }

            if (saveAssets)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return report;
        }

        private static BodyDriftReport CreateInPlaceCopiesForConfiguredDefinitions(bool saveAssets)
        {
            ActToolkitEditorUtilities.EnsureGeneratedFolders();
            ActToolkitEditorUtilities.EnsureFolder(ActToolkitEditorUtilities.CombatMvpFolder, "InPlaceClips");

            BodyDriftReport report = new BodyDriftReport("In-place bake report");
            Dictionary<AnimationClip, AnimationClip> bakedBySourceClip = new Dictionary<AnimationClip, AnimationClip>();

            foreach (DefinitionBakeTarget target in CollectDefinitionBakeTargets())
            {
                DriftMeasurement measurement = MeasureBodyDrift(target);
                report.measurements.Add(measurement);
                if (!measurement.canBake || measurement.peakHorizontalDrift < DriftThresholdMeters)
                {
                    continue;
                }

                if (!bakedBySourceClip.TryGetValue(target.definition.clip, out AnimationClip inPlaceClip))
                {
                    inPlaceClip = BakeInPlaceClip(target, measurement);
                    if (inPlaceClip == null)
                    {
                        measurement.status = "Bake failed";
                        continue;
                    }

                    bakedBySourceClip[target.definition.clip] = inPlaceClip;
                    report.createdClips.Add(AssetDatabase.GetAssetPath(inPlaceClip));
                }

                target.definition.clip = inPlaceClip;
                EditorUtility.SetDirty(target.definition);
                report.replacedDefinitions.Add(AssetDatabase.GetAssetPath(target.definition));
            }

            if (saveAssets)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return report;
        }

        private static List<DefinitionBakeTarget> CollectDefinitionBakeTargets()
        {
            Dictionary<CombatAnimationDefinition, CharacterActionProfile> profileByDefinition =
                new Dictionary<CombatAnimationDefinition, CharacterActionProfile>();

            string[] profileGuids = AssetDatabase.FindAssets("t:CharacterActionProfile", new[] { ActToolkitEditorUtilities.CombatMvpFolder });
            CharacterActionProfile fallbackProfile = null;
            foreach (string guid in profileGuids)
            {
                CharacterActionProfile profile = AssetDatabase.LoadAssetAtPath<CharacterActionProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (profile == null)
                {
                    continue;
                }

                if (fallbackProfile == null || profile.name == "Female_Mannequin_Profile")
                {
                    fallbackProfile = profile;
                }

                if (profile.comboTable == null || profile.comboTable.actions == null)
                {
                    continue;
                }

                foreach (CombatAnimationDefinition definition in profile.comboTable.actions)
                {
                    if (definition != null && !profileByDefinition.ContainsKey(definition))
                    {
                        profileByDefinition.Add(definition, profile);
                    }
                }
            }

            List<DefinitionBakeTarget> targets = new List<DefinitionBakeTarget>();
            string[] definitionGuids = AssetDatabase.FindAssets("t:CombatAnimationDefinition", new[] { ActToolkitEditorUtilities.DefaultCombatDefinitionFolder });
            foreach (string guid in definitionGuids)
            {
                CombatAnimationDefinition definition = AssetDatabase.LoadAssetAtPath<CombatAnimationDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition == null || definition.clip == null)
                {
                    continue;
                }

                profileByDefinition.TryGetValue(definition, out CharacterActionProfile profile);
                if (profile == null)
                {
                    profile = fallbackProfile;
                }

                targets.Add(new DefinitionBakeTarget(definition, profile));
            }

            return targets;
        }

        private static DriftMeasurement MeasureBodyDrift(DefinitionBakeTarget target)
        {
            DriftMeasurement measurement = new DriftMeasurement(target);
            if (target.definition == null || target.definition.clip == null)
            {
                measurement.status = "Missing definition clip";
                return measurement;
            }

            if (target.profile == null || target.profile.modelPrefab == null)
            {
                measurement.status = "Missing character model";
                return measurement;
            }

            GameObject instance = Object.Instantiate(target.profile.modelPrefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Animator animator = EnsureAnimator(instance, target.profile);
                Transform sampleRoot = animator == null ? instance.transform : animator.transform;
                Transform bodyRoot = ResolveBodyRoot(animator, sampleRoot);
                if (bodyRoot == null)
                {
                    measurement.status = "Missing body root";
                    return measurement;
                }

                AnimationClip clip = target.definition.clip;
                int sampleRate = GetSampleRate(target.definition, clip);
                int frameCount = Mathf.Max(1, Mathf.CeilToInt(clip.length * sampleRate));

                clip.SampleAnimation(sampleRoot.gameObject, 0f);
                Vector3 first = bodyRoot.position;
                Vector3 previous = first;
                measurement.canBake = true;
                measurement.status = "Measured";

                for (int frame = 0; frame <= frameCount; frame++)
                {
                    float time = Mathf.Min(frame / (float)sampleRate, clip.length);
                    clip.SampleAnimation(sampleRoot.gameObject, time);
                    Vector3 current = bodyRoot.position;
                    Vector3 fromStart = current - first;
                    Vector3 fromPrevious = current - previous;
                    float horizontalFromStart = new Vector2(fromStart.x, fromStart.z).magnitude;
                    float horizontalStep = frame == 0 ? 0f : new Vector2(fromPrevious.x, fromPrevious.z).magnitude;
                    measurement.peakHorizontalDrift = Mathf.Max(measurement.peakHorizontalDrift, horizontalFromStart);
                    measurement.peakHorizontalSpeed = Mathf.Max(measurement.peakHorizontalSpeed, horizontalStep * sampleRate);
                    previous = current;
                }

                Vector3 finalOffset = previous - first;
                measurement.finalHorizontalDrift = new Vector2(finalOffset.x, finalOffset.z).magnitude;
                measurement.sampleRate = sampleRate;
                measurement.frameCount = frameCount;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            return measurement;
        }

        private static AnimationClip BakeInPlaceClip(DefinitionBakeTarget target, DriftMeasurement measurement)
        {
            if (target.definition == null)
            {
                return null;
            }

            return BakeInPlaceClip(
                target,
                target.definition.clip,
                measurement == null ? 0 : measurement.sampleRate,
                null);
        }

        private static AnimationClip BakeInPlaceClip(DefinitionBakeTarget target, AnimationClip sourceClip, int sampleRateOverride, string destinationPath)
        {
            GameObject instance = Object.Instantiate(target.profile.modelPrefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Animator animator = EnsureAnimator(instance, target.profile);
                Transform sampleRoot = animator == null ? instance.transform : animator.transform;
                Transform bodyRoot = ResolveBodyRoot(animator, sampleRoot);
                List<Transform> compensationRoots = ResolveCompensationRoots(sampleRoot);
                if (bodyRoot == null || sourceClip == null || compensationRoots.Count == 0)
                {
                    return null;
                }

                int sampleRate = sampleRateOverride > 0 ? sampleRateOverride : GetSampleRate(target.definition, sourceClip);
                int frameCount = Mathf.Max(1, Mathf.CeilToInt(sourceClip.length * sampleRate));

                sourceClip.SampleAnimation(sampleRoot.gameObject, 0f);
                Vector3 firstBodyPosition = bodyRoot.position;
                Transform[] transforms = sampleRoot.GetComponentsInChildren<Transform>(true);
                Dictionary<Transform, LocalPose> fixedRendererPoses = CaptureRendererLocalPoses(transforms);
                Dictionary<Transform, Vector3> fixedMotionRootPositions = CaptureTopLevelHorizontalPositions(sampleRoot);
                Dictionary<Transform, TransformCurveSet> curvesByTransform = new Dictionary<Transform, TransformCurveSet>();

                for (int frame = 0; frame <= frameCount; frame++)
                {
                    float time = Mathf.Min(frame / (float)sampleRate, sourceClip.length);
                    sourceClip.SampleAnimation(sampleRoot.gameObject, time);

                    Vector3 bodyOffset = bodyRoot.position - firstBodyPosition;
                    bodyOffset.y = 0f;
                    for (int rootIndex = 0; rootIndex < compensationRoots.Count; rootIndex++)
                    {
                        Transform compensationRoot = compensationRoots[rootIndex];
                        if (compensationRoot == null)
                        {
                            continue;
                        }

                        Vector3 localCompensation = compensationRoot.parent == null
                            ? bodyOffset
                            : compensationRoot.parent.InverseTransformVector(bodyOffset);
                        compensationRoot.localPosition -= localCompensation;
                    }

                    foreach (Transform transform in transforms)
                    {
                        if (transform == sampleRoot)
                        {
                            continue;
                        }

                        if (!curvesByTransform.TryGetValue(transform, out TransformCurveSet curveSet))
                        {
                            curveSet = new TransformCurveSet(AnimationUtility.CalculateTransformPath(transform, sampleRoot));
                            curvesByTransform.Add(transform, curveSet);
                        }

                        if (fixedRendererPoses.TryGetValue(transform, out LocalPose fixedPose))
                        {
                            curveSet.AddKey(time, fixedPose.position, fixedPose.rotation, fixedPose.scale);
                        }
                        else if (fixedMotionRootPositions.TryGetValue(transform, out Vector3 fixedPosition))
                        {
                            Vector3 position = transform.localPosition;
                            position.x = fixedPosition.x;
                            position.z = fixedPosition.z;
                            curveSet.AddKey(time, position, transform.localRotation, transform.localScale);
                        }
                        else
                        {
                            curveSet.AddKey(time, transform.localPosition, transform.localRotation, transform.localScale);
                        }
                    }
                }

                AnimationClip bakedClip = new AnimationClip();
                bakedClip.name = target.definition.clip.name + "_InPlace";
                bakedClip.frameRate = sampleRate;
                bakedClip.legacy = false;

                foreach (TransformCurveSet curveSet in curvesByTransform.Values)
                {
                    curveSet.ApplyTo(bakedClip);
                }

                AnimationUtility.SetAnimationEvents(bakedClip, AnimationUtility.GetAnimationEvents(sourceClip));

                string path = string.IsNullOrWhiteSpace(destinationPath)
                    ? AssetDatabase.GenerateUniqueAssetPath(InPlaceFolder + "/" + SanitizeAssetName(bakedClip.name) + ".anim")
                    : destinationPath;
                AnimationClip existingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existingClip != null)
                {
                    EditorUtility.CopySerialized(bakedClip, existingClip);
                    existingClip.name = Path.GetFileNameWithoutExtension(path);
                    EditorUtility.SetDirty(existingClip);
                    return existingClip;
                }

                AssetDatabase.CreateAsset(bakedClip, path);
                return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static Animator EnsureAnimator(GameObject instance, CharacterActionProfile profile)
        {
            Animator animator = instance.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }

            if (animator.avatar == null && profile != null && profile.avatar != null)
            {
                animator.avatar = profile.avatar;
            }

            if (animator.avatar == null && profile != null && profile.modelPrefab != null)
            {
                Avatar avatar = LoadAvatarFromModel(AssetDatabase.GetAssetPath(profile.modelPrefab));
                if (avatar != null)
                {
                    animator.avatar = avatar;
                }
            }

            animator.applyRootMotion = false;
            animator.enabled = true;
            return animator;
        }

        private static Avatar LoadAvatarFromModel(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object asset in assets)
            {
                if (asset is Avatar avatar)
                {
                    return avatar;
                }
            }

            return null;
        }

        private static List<Transform> ResolveCompensationRoots(Transform sampleRoot)
        {
            List<Transform> roots = new List<Transform>();
            if (sampleRoot == null)
            {
                return roots;
            }

            if (sampleRoot.childCount == 0)
            {
                roots.Add(sampleRoot);
                return roots;
            }

            for (int i = 0; i < sampleRoot.childCount; i++)
            {
                roots.Add(sampleRoot.GetChild(i));
            }

            return roots;
        }

        private static Dictionary<Transform, Vector3> CaptureTopLevelHorizontalPositions(Transform sampleRoot)
        {
            Dictionary<Transform, Vector3> positions = new Dictionary<Transform, Vector3>();
            if (sampleRoot == null)
            {
                return positions;
            }

            for (int i = 0; i < sampleRoot.childCount; i++)
            {
                Transform child = sampleRoot.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                positions[child] = child.localPosition;
            }

            return positions;
        }

        private static Dictionary<Transform, LocalPose> CaptureRendererLocalPoses(Transform[] transforms)
        {
            Dictionary<Transform, LocalPose> poses = new Dictionary<Transform, LocalPose>();
            if (transforms == null)
            {
                return poses;
            }

            for (int i = 0; i < transforms.Length; i++)
            {
                Transform transform = transforms[i];
                if (transform == null || transform.GetComponent<Renderer>() == null)
                {
                    continue;
                }

                poses[transform] = new LocalPose(transform.localPosition, transform.localRotation, transform.localScale);
            }

            return poses;
        }

        private static Transform ResolveBodyRoot(Animator animator, Transform sampleRoot)
        {
            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null)
                {
                    return hips;
                }
            }

            return FindDescendantByName(sampleRoot, "Hips", "hips", "Pelvis", "pelvis");
        }

        private static Transform FindDescendantByName(Transform root, params string[] names)
        {
            if (root == null)
            {
                return null;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
                {
                    if (string.Equals(child.name, names[nameIndex], System.StringComparison.OrdinalIgnoreCase))
                    {
                        return child;
                    }
                }

                Transform nested = FindDescendantByName(child, names);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static int GetSampleRate(CombatAnimationDefinition definition, AnimationClip clip)
        {
            if (definition != null && definition.authoringFrameRate > 0)
            {
                return definition.authoringFrameRate;
            }

            if (clip != null && clip.frameRate > 0f)
            {
                return Mathf.RoundToInt(clip.frameRate);
            }

            return DefaultSampleRate;
        }

        private static string SanitizeAssetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "InPlaceClip";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                bool invalid = false;
                for (int i = 0; i < invalidChars.Length; i++)
                {
                    if (c == invalidChars[i])
                    {
                        invalid = true;
                        break;
                    }
                }

                builder.Append(invalid ? '_' : c);
            }

            return builder.ToString();
        }

        private static AnimationClip ResolveSourceClipForInPlace(AnimationClip inPlaceClip)
        {
            if (inPlaceClip == null)
            {
                return null;
            }

            string inPlaceName = inPlaceClip.name;
            const string suffix = "_InPlace";
            string sourceKey = inPlaceName.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase)
                ? inPlaceName.Substring(0, inPlaceName.Length - suffix.Length)
                : inPlaceName;

            string[] clipGuids = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets" });
            HashSet<string> visitedPaths = new HashSet<string>();
            foreach (string guid in clipGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(path) || IsInPlaceClipPath(path) || !visitedPaths.Add(path))
                {
                    continue;
                }

                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (Object asset in assets)
                {
                    if (asset is AnimationClip clip && SanitizeAssetName(clip.name) == sourceKey)
                    {
                        return clip;
                    }
                }
            }

            return null;
        }

        private static bool IsInPlaceClipPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && path.Replace('\\', '/').StartsWith(InPlaceFolder + "/", System.StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteBatchReport(string fileName, string text)
        {
            string folder = Path.GetFullPath("Temp/ActToolkit");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, fileName), text);
            Debug.Log(text);
        }

        private sealed class DefinitionBakeTarget
        {
            public readonly CombatAnimationDefinition definition;
            public readonly CharacterActionProfile profile;

            public DefinitionBakeTarget(CombatAnimationDefinition definition, CharacterActionProfile profile)
            {
                this.definition = definition;
                this.profile = profile;
            }

            public string DisplayName => definition == null ? "None" : definition.DisplayName;
        }

        private sealed class DriftMeasurement
        {
            public readonly DefinitionBakeTarget target;
            public float finalHorizontalDrift;
            public float peakHorizontalDrift;
            public float peakHorizontalSpeed;
            public int sampleRate;
            public int frameCount;
            public bool canBake;
            public string status = "Not measured";

            public DriftMeasurement(DefinitionBakeTarget target)
            {
                this.target = target;
            }
        }

        private readonly struct LocalPose
        {
            public readonly Vector3 position;
            public readonly Quaternion rotation;
            public readonly Vector3 scale;

            public LocalPose(Vector3 position, Quaternion rotation, Vector3 scale)
            {
                this.position = position;
                this.rotation = rotation;
                this.scale = scale;
            }
        }

        private sealed class BodyDriftReport
        {
            private readonly string title;
            public readonly List<DriftMeasurement> measurements = new List<DriftMeasurement>();
            public readonly List<string> createdClips = new List<string>();
            public readonly List<string> replacedDefinitions = new List<string>();

            public BodyDriftReport(string title)
            {
                this.title = title;
            }

            public string ToLogText()
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine("[ActToolkit] " + title);
                foreach (DriftMeasurement measurement in measurements)
                {
                    string definitionPath = measurement.target.definition == null
                        ? "None"
                        : AssetDatabase.GetAssetPath(measurement.target.definition);
                    string clipName = measurement.target.definition == null || measurement.target.definition.clip == null
                        ? "None"
                        : measurement.target.definition.clip.name;
                    builder.AppendLine("- "
                        + measurement.target.DisplayName
                        + " / "
                        + clipName
                        + " | final="
                        + measurement.finalHorizontalDrift.ToString("0.000")
                        + "m, peak="
                        + measurement.peakHorizontalDrift.ToString("0.000")
                        + "m, speed="
                        + measurement.peakHorizontalSpeed.ToString("0.000")
                        + "m/s, status="
                        + measurement.status
                        + " | "
                        + definitionPath);
                }

                if (createdClips.Count > 0)
                {
                    builder.AppendLine("Created clips:");
                    foreach (string path in createdClips)
                    {
                        builder.AppendLine("- " + path);
                    }
                }

                if (replacedDefinitions.Count > 0)
                {
                    builder.AppendLine("Replaced definitions:");
                    foreach (string path in replacedDefinitions)
                    {
                        builder.AppendLine("- " + path);
                    }
                }

                return builder.ToString();
            }
        }

        private sealed class TransformCurveSet
        {
            private readonly string path;
            private readonly AnimationCurve posX = new AnimationCurve();
            private readonly AnimationCurve posY = new AnimationCurve();
            private readonly AnimationCurve posZ = new AnimationCurve();
            private readonly AnimationCurve rotX = new AnimationCurve();
            private readonly AnimationCurve rotY = new AnimationCurve();
            private readonly AnimationCurve rotZ = new AnimationCurve();
            private readonly AnimationCurve rotW = new AnimationCurve();
            private readonly AnimationCurve scaleX = new AnimationCurve();
            private readonly AnimationCurve scaleY = new AnimationCurve();
            private readonly AnimationCurve scaleZ = new AnimationCurve();

            public TransformCurveSet(string path)
            {
                this.path = path;
            }

            public void AddKey(float time, Vector3 position, Quaternion rotation, Vector3 scale)
            {
                posX.AddKey(time, position.x);
                posY.AddKey(time, position.y);
                posZ.AddKey(time, position.z);
                rotX.AddKey(time, rotation.x);
                rotY.AddKey(time, rotation.y);
                rotZ.AddKey(time, rotation.z);
                rotW.AddKey(time, rotation.w);
                scaleX.AddKey(time, scale.x);
                scaleY.AddKey(time, scale.y);
                scaleZ.AddKey(time, scale.z);
            }

            public void ApplyTo(AnimationClip clip)
            {
                SetCurve(clip, "m_LocalPosition.x", posX);
                SetCurve(clip, "m_LocalPosition.y", posY);
                SetCurve(clip, "m_LocalPosition.z", posZ);
                SetCurve(clip, "m_LocalRotation.x", rotX);
                SetCurve(clip, "m_LocalRotation.y", rotY);
                SetCurve(clip, "m_LocalRotation.z", rotZ);
                SetCurve(clip, "m_LocalRotation.w", rotW);
                SetCurve(clip, "m_LocalScale.x", scaleX);
                SetCurve(clip, "m_LocalScale.y", scaleY);
                SetCurve(clip, "m_LocalScale.z", scaleZ);
            }

            private void SetCurve(AnimationClip clip, string propertyName, AnimationCurve curve)
            {
                AnimationUtility.SetEditorCurve(
                    clip,
                    EditorCurveBinding.FloatCurve(path, typeof(Transform), propertyName),
                    curve);
            }
        }
    }
}
