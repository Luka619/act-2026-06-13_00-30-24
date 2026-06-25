using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ActToolkit.EditorTools
{
    public static class ActToolkitLocomotionStrideAnalyzer
    {
        private const string ReportPath = "Temp/ActToolkitLocomotionStrideReport.txt";
        private const string ModelPath = "Assets/External/TestAssets/Characters/Quaternius_UniversalAnimationLibrary2_Standard/Mannequin_F.fbx";
        private const string LocomotionPath = "Assets/External/TestAssets/Animations/PreviewClips/Quaternius_UniversalAnimationLibrary_Standard/UAL1_Standard.fbx";
        private const string WalkClipName = "Armature|Walk_Loop";
        private const string MoveClipName = "Armature|Jog_Fwd_Loop";

        [MenuItem(ActToolkitMenu.DiagnosticsRoot + "/Animation/Estimate Locomotion Stride Speeds", false, 424)]
        public static void EstimateLocomotionStrideSpeeds()
        {
            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            AnimationClip walkClip = LoadClip(WalkClipName);
            AnimationClip moveClip = LoadClip(MoveClipName);

            List<string> lines = new List<string>
            {
                "Act Toolkit Locomotion Stride Estimate",
                "Model\t" + ModelPath,
                "Animation Source\t" + LocomotionPath,
                "Note\tEstimated from in-place foot contact velocity. cycleSpeed is kept only as a legacy sanity check.",
                string.Empty
            };

            if (modelPrefab == null)
            {
                lines.Add("ERROR\tMissing model prefab.");
                WriteReport(lines);
                return;
            }

            AnalyzeClip(modelPrefab, walkClip, WalkClipName, lines);
            AnalyzeClip(modelPrefab, moveClip, MoveClipName, lines);
            WriteReport(lines);
        }

        private static AnimationClip LoadClip(string clipName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(LocomotionPath);
            foreach (Object asset in assets)
            {
                if (asset is AnimationClip clip && clip.name == clipName)
                {
                    return clip;
                }
            }

            return null;
        }

        private static void AnalyzeClip(GameObject modelPrefab, AnimationClip clip, string clipName, List<string> lines)
        {
            if (clip == null)
            {
                lines.Add(clipName + "\tERROR\tMissing clip.");
                return;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(modelPrefab) as GameObject;
            if (instance == null)
            {
                lines.Add(clipName + "\tERROR\tCould not instantiate model.");
                return;
            }

            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Transform leftFoot = FindFootBone(instance.transform, true);
                Transform rightFoot = FindFootBone(instance.transform, false);
                if (leftFoot == null || rightFoot == null)
                {
                    lines.Add(clipName + "\tERROR\tMissing foot bones by name.");
                    lines.Add("Bone candidates\t" + string.Join(", ", CollectBoneCandidates(instance.transform)));
                    return;
                }

                StrideEstimate estimate = SampleStride(instance, clip, leftFoot, rightFoot);
                lines.Add(clipName
                    + "\tlength=" + clip.length.ToString("0.000") + "s"
                    + "\tleftStep=" + estimate.leftStepLength.ToString("0.000") + "m"
                    + "\trightStep=" + estimate.rightStepLength.ToString("0.000") + "m"
                    + "\tavgStep=" + estimate.averageStepLength.ToString("0.000") + "m"
                    + "\tstride=" + estimate.strideLength.ToString("0.000") + "m"
                    + "\tcycleSpeed=" + estimate.cycleSpeed.ToString("0.000") + "m/s"
                    + "\tleftContactSpeed=" + estimate.leftContactSpeed.ToString("0.000") + "m/s"
                    + "\trightContactSpeed=" + estimate.rightContactSpeed.ToString("0.000") + "m/s"
                    + "\tleftContactDuty=" + estimate.leftContactDuty.ToString("0.00")
                    + "\trightContactDuty=" + estimate.rightContactDuty.ToString("0.00")
                    + "\testimatedSpeed=" + estimate.referenceSpeed.ToString("0.000") + "m/s"
                    + "\tleftBone=" + leftFoot.name
                    + "\trightBone=" + rightFoot.name);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static StrideEstimate SampleStride(GameObject instance, AnimationClip clip, Transform leftFoot, Transform rightFoot)
        {
            const int sampleCount = 241;
            List<Vector3> leftSamples = new List<Vector3>(sampleCount);
            List<Vector3> rightSamples = new List<Vector3>(sampleCount);

            AnimationMode.StartAnimationMode();
            try
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    float t = clip.length <= 0f ? 0f : clip.length * i / (sampleCount - 1);
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(instance, clip, t);
                    AnimationMode.EndSampling();

                    leftSamples.Add(instance.transform.InverseTransformPoint(leftFoot.position));
                    rightSamples.Add(instance.transform.InverseTransformPoint(rightFoot.position));
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }

            float leftStep = EstimateStepLength(leftSamples);
            float rightStep = EstimateStepLength(rightSamples);
            float averageStep = (leftStep + rightStep) * 0.5f;
            float stride = averageStep * 2f;
            float cycleSpeed = clip.length <= 0f ? 0f : stride / clip.length;
            FootContactEstimate leftContact = EstimateFootContactSpeed(leftSamples, clip.length);
            FootContactEstimate rightContact = EstimateFootContactSpeed(rightSamples, clip.length);
            float contactSpeed = AveragePositive(leftContact.speed, rightContact.speed);
            float speed = contactSpeed > 0.001f ? contactSpeed : cycleSpeed;
            return new StrideEstimate(
                leftStep,
                rightStep,
                averageStep,
                stride,
                cycleSpeed,
                leftContact.speed,
                rightContact.speed,
                leftContact.duty,
                rightContact.duty,
                speed);
        }

        private static Transform FindFootBone(Transform root, bool left)
        {
            string[] exactNames = left
                ? new[] { "LeftFoot", "Foot_L", "L_Foot", "foot.L", "foot_l", "mixamorig:LeftFoot" }
                : new[] { "RightFoot", "Foot_R", "R_Foot", "foot.R", "foot_r", "mixamorig:RightFoot" };

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (string exactName in exactNames)
            {
                foreach (Transform transform in transforms)
                {
                    if (string.Equals(transform.name, exactName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return transform;
                    }
                }
            }

            string sideTokenA = left ? "left" : "right";
            string sideTokenB = left ? "_l" : "_r";
            string sideTokenC = left ? ".l" : ".r";
            foreach (Transform transform in transforms)
            {
                string name = transform.name.ToLowerInvariant();
                if (!name.Contains("foot"))
                {
                    continue;
                }

                if (name.Contains(sideTokenA) || name.Contains(sideTokenB) || name.Contains(sideTokenC))
                {
                    return transform;
                }
            }

            return null;
        }

        private static List<string> CollectBoneCandidates(Transform root)
        {
            List<string> candidates = new List<string>();
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform transform in transforms)
            {
                string name = transform.name.ToLowerInvariant();
                if (name.Contains("foot") || name.Contains("toe") || name.Contains("ankle") || name.Contains("leg"))
                {
                    candidates.Add(transform.name);
                }
            }

            return candidates;
        }

        private static float EstimateStepLength(List<Vector3> samples)
        {
            if (samples == null || samples.Count == 0)
            {
                return 0f;
            }

            float minY = float.PositiveInfinity;
            for (int i = 0; i < samples.Count; i++)
            {
                minY = Mathf.Min(minY, samples[i].y);
            }

            float maxPlantedY = minY + 0.04f;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;
            int plantedCount = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].y > maxPlantedY)
                {
                    continue;
                }

                minZ = Mathf.Min(minZ, samples[i].z);
                maxZ = Mathf.Max(maxZ, samples[i].z);
                plantedCount++;
            }

            if (plantedCount < 8)
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    minZ = Mathf.Min(minZ, samples[i].z);
                    maxZ = Mathf.Max(maxZ, samples[i].z);
                }
            }

            return Mathf.Max(0f, maxZ - minZ);
        }

        private static FootContactEstimate EstimateFootContactSpeed(List<Vector3> samples, float clipLength)
        {
            if (samples == null || samples.Count < 2 || clipLength <= 0f)
            {
                return new FootContactEstimate(0f, 0f);
            }

            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < samples.Count; i++)
            {
                minY = Mathf.Min(minY, samples[i].y);
                maxY = Mathf.Max(maxY, samples[i].y);
            }

            float plantedThreshold = minY + Mathf.Max(0.035f, (maxY - minY) * 0.22f);
            return EstimateFootContactSpeed(samples, clipLength, plantedThreshold, 0.75f, 0.05f);
        }

        private static FootContactEstimate EstimateFootContactSpeed(
            List<Vector3> samples,
            float clipLength,
            float plantedThreshold,
            float maxVerticalSpeed,
            float minHorizontalSpeed)
        {
            float dt = clipLength / (samples.Count - 1);
            List<float> speeds = new List<float>();
            float contactDuration = 0f;

            for (int i = 1; i < samples.Count; i++)
            {
                Vector3 previous = samples[i - 1];
                Vector3 current = samples[i];
                if (previous.y > plantedThreshold || current.y > plantedThreshold)
                {
                    continue;
                }

                float verticalSpeed = Mathf.Abs(current.y - previous.y) / dt;
                float horizontalSpeed = Mathf.Abs(current.z - previous.z) / dt;
                if (verticalSpeed > maxVerticalSpeed || horizontalSpeed < minHorizontalSpeed)
                {
                    continue;
                }

                speeds.Add(horizontalSpeed);
                contactDuration += dt;
            }

            if (speeds.Count < 4 && maxVerticalSpeed < 2f)
            {
                return EstimateFootContactSpeed(samples, clipLength, plantedThreshold, 2f, minHorizontalSpeed);
            }

            float duty = Mathf.Clamp01(contactDuration / clipLength);
            if (speeds.Count == 0)
            {
                return new FootContactEstimate(0f, duty);
            }

            speeds.Sort();
            return new FootContactEstimate(Percentile(speeds, 0.65f), duty);
        }

        private static float AveragePositive(float left, float right)
        {
            bool hasLeft = left > 0.001f;
            bool hasRight = right > 0.001f;
            if (hasLeft && hasRight)
            {
                return (left + right) * 0.5f;
            }

            if (hasLeft)
            {
                return left;
            }

            return hasRight ? right : 0f;
        }

        private static float Percentile(List<float> sortedValues, float percentile)
        {
            if (sortedValues == null || sortedValues.Count == 0)
            {
                return 0f;
            }

            if (sortedValues.Count == 1)
            {
                return sortedValues[0];
            }

            float index = Mathf.Clamp01(percentile) * (sortedValues.Count - 1);
            int lower = Mathf.FloorToInt(index);
            int upper = Mathf.CeilToInt(index);
            if (lower == upper)
            {
                return sortedValues[lower];
            }

            return Mathf.Lerp(sortedValues[lower], sortedValues[upper], index - lower);
        }

        private static void WriteReport(List<string> lines)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllLines(ReportPath, lines);
            Debug.Log("[ActToolkit] Locomotion stride estimate written to " + ReportPath + "\n" + string.Join("\n", lines));
            AssetDatabase.Refresh();
        }

        private readonly struct StrideEstimate
        {
            public readonly float leftStepLength;
            public readonly float rightStepLength;
            public readonly float averageStepLength;
            public readonly float strideLength;
            public readonly float cycleSpeed;
            public readonly float leftContactSpeed;
            public readonly float rightContactSpeed;
            public readonly float leftContactDuty;
            public readonly float rightContactDuty;
            public readonly float referenceSpeed;

            public StrideEstimate(
                float leftStepLength,
                float rightStepLength,
                float averageStepLength,
                float strideLength,
                float cycleSpeed,
                float leftContactSpeed,
                float rightContactSpeed,
                float leftContactDuty,
                float rightContactDuty,
                float referenceSpeed)
            {
                this.leftStepLength = leftStepLength;
                this.rightStepLength = rightStepLength;
                this.averageStepLength = averageStepLength;
                this.strideLength = strideLength;
                this.cycleSpeed = cycleSpeed;
                this.leftContactSpeed = leftContactSpeed;
                this.rightContactSpeed = rightContactSpeed;
                this.leftContactDuty = leftContactDuty;
                this.rightContactDuty = rightContactDuty;
                this.referenceSpeed = referenceSpeed;
            }
        }

        private readonly struct FootContactEstimate
        {
            public readonly float speed;
            public readonly float duty;

            public FootContactEstimate(float speed, float duty)
            {
                this.speed = speed;
                this.duty = duty;
            }
        }
    }
}
