using System;
using System.Collections.Generic;
using System.IO;
using ActToolkit;
using UnityEditor;
using UnityEngine;

namespace ActToolkit.EditorTools
{
    internal static class ActToolkitSkeletonCompatibility
    {
        private static readonly Dictionary<string, HashSet<string>> AssetBoneNameCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<string>> AssetTransformPathCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache()
        {
            AssetBoneNameCache.Clear();
            AssetTransformPathCache.Clear();
        }

        public static bool IsDefinitionCompatibleWithModel(string modelPath, CombatAnimationDefinition definition)
        {
            return definition != null && IsClipCompatibleWithModel(modelPath, definition.clip);
        }

        public static bool IsClipCompatibleWithModel(string modelPath, AnimationClip clip)
        {
            return GetClipCompatibilityDiagnostic(modelPath, clip).compatible;
        }

        public static ClipCompatibilityDiagnostic GetClipCompatibilityDiagnostic(string modelPath, AnimationClip clip)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return ClipCompatibilityDiagnostic.Compatible(string.Empty, "No model selected.");
            }

            if (clip == null)
            {
                return ClipCompatibilityDiagnostic.Incompatible(string.Empty, "No clip assigned.");
            }

            string animationPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrWhiteSpace(animationPath))
            {
                return ClipCompatibilityDiagnostic.Incompatible(string.Empty, "Clip has no asset path.");
            }

            if (string.Equals(modelPath, animationPath, StringComparison.OrdinalIgnoreCase))
            {
                return ClipCompatibilityDiagnostic.Compatible(animationPath, "Clip comes from the model asset.");
            }

            string extension = Path.GetExtension(animationPath).ToLowerInvariant();
            ClipBindingCompatibility bindingCompatibility = GetClipTransformBindingCompatibility(modelPath, clip);
            if (bindingCompatibility.transformBindingCount > 0)
            {
                return bindingCompatibility.missingBindingCount == 0
                    ? ClipCompatibilityDiagnostic.Compatible(
                        animationPath,
                        "All transform bindings exist on the selected model.",
                        bindingCompatibility.transformBindingCount,
                        0,
                        string.Empty,
                        false)
                    : ClipCompatibilityDiagnostic.Incompatible(
                        animationPath,
                        "Clip transform bindings do not match the selected model.",
                        bindingCompatibility.transformBindingCount,
                        bindingCompatibility.missingBindingCount,
                        bindingCompatibility.firstMissingBinding,
                        false);
            }

            if (extension == ".anim")
            {
                return ClipCompatibilityDiagnostic.Incompatible(animationPath, "Clip has no transform bindings to compare.");
            }

            bool assetSkeletonCompatible = AreAssetSkeletonsCompatible(modelPath, animationPath);
            return assetSkeletonCompatible
                ? ClipCompatibilityDiagnostic.Compatible(animationPath, "No transform bindings found; source FBX skeleton matches the model.", 0, 0, string.Empty, true)
                : ClipCompatibilityDiagnostic.Incompatible(animationPath, "No transform bindings found; source FBX skeleton does not match the model.", 0, 0, string.Empty, false);
        }

        public static bool AreAssetSkeletonsCompatible(string modelPath, string animationPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(animationPath))
            {
                return false;
            }

            HashSet<string> modelBones = GetAssetBoneNames(modelPath);
            HashSet<string> animationBones = GetAssetBoneNames(animationPath);
            if (modelBones.Count == 0 || animationBones.Count == 0)
            {
                return false;
            }

            return BoneSetsMatch(modelBones, animationBones);
        }

        public static HashSet<string> GetAssetTransformPaths(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            if (AssetTransformPathCache.TryGetValue(assetPath, out HashSet<string> cached))
            {
                return cached;
            }

            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset != null)
            {
                CollectTransformPaths(asset.transform, string.Empty, paths);
            }

            AssetTransformPathCache[assetPath] = paths;
            return paths;
        }

        private static bool AreClipTransformBindingsCompatibleWithModel(string modelPath, AnimationClip clip)
        {
            ClipBindingCompatibility compatibility = GetClipTransformBindingCompatibility(modelPath, clip);
            return compatibility.transformBindingCount > 0 && compatibility.missingBindingCount == 0;
        }

        private static ClipBindingCompatibility GetClipTransformBindingCompatibility(string modelPath, AnimationClip clip)
        {
            if (clip == null)
            {
                return default;
            }

            HashSet<string> transformPaths = GetAssetTransformPaths(modelPath);
            if (transformPaths.Count == 0)
            {
                return default;
            }

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            int transformBindingCount = 0;
            int missingBindingCount = 0;
            string firstMissingBinding = string.Empty;
            foreach (EditorCurveBinding binding in bindings)
            {
                if (binding.type != typeof(Transform))
                {
                    continue;
                }

                transformBindingCount++;
                if (!transformPaths.Contains(binding.path))
                {
                    missingBindingCount++;
                    if (string.IsNullOrWhiteSpace(firstMissingBinding))
                    {
                        firstMissingBinding = binding.path;
                    }
                }
            }

            return new ClipBindingCompatibility(transformBindingCount, missingBindingCount, firstMissingBinding);
        }

        private static HashSet<string> GetAssetBoneNames(string assetPath)
        {
            if (AssetBoneNameCache.TryGetValue(assetPath, out HashSet<string> cached))
            {
                return cached;
            }

            HashSet<string> boneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset != null)
            {
                CollectSkinnedBoneNames(asset, boneNames);
                if (boneNames.Count == 0)
                {
                    CollectFallbackBoneNames(asset.transform, boneNames);
                }
            }

            AssetBoneNameCache[assetPath] = boneNames;
            return boneNames;
        }

        private static void CollectSkinnedBoneNames(GameObject asset, HashSet<string> boneNames)
        {
            SkinnedMeshRenderer[] renderers = asset.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                AddBoneName(renderer.rootBone, boneNames);
                foreach (Transform bone in renderer.bones)
                {
                    AddBoneName(bone, boneNames);
                }
            }
        }

        private static void CollectFallbackBoneNames(Transform transform, HashSet<string> boneNames)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (!IsNonBoneTransform(child))
                {
                    AddBoneName(child, boneNames);
                }

                CollectFallbackBoneNames(child, boneNames);
            }
        }

        private static void CollectTransformPaths(Transform transform, string path, HashSet<string> paths)
        {
            paths.Add(path);
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                string childPath = string.IsNullOrWhiteSpace(path) ? child.name : path + "/" + child.name;
                CollectTransformPaths(child, childPath, paths);
            }
        }

        private static bool IsNonBoneTransform(Transform transform)
        {
            return transform.GetComponent<MeshFilter>() != null
                || transform.GetComponent<MeshRenderer>() != null
                || transform.GetComponent<SkinnedMeshRenderer>() != null
                || transform.GetComponent<Camera>() != null
                || transform.GetComponent<Light>() != null;
        }

        private static void AddBoneName(Transform bone, HashSet<string> boneNames)
        {
            if (bone == null)
            {
                return;
            }

            string name = NormalizeBoneName(bone.name);
            if (string.IsNullOrWhiteSpace(name) || IsIgnoredWrapperName(name))
            {
                return;
            }

            boneNames.Add(name);
        }

        private static string NormalizeBoneName(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName))
            {
                return string.Empty;
            }

            string value = boneName.Trim();
            int namespaceIndex = value.LastIndexOf(':');
            if (namespaceIndex >= 0 && namespaceIndex < value.Length - 1)
            {
                value = value.Substring(namespaceIndex + 1);
            }

            return value.ToLowerInvariant();
        }

        private static bool IsIgnoredWrapperName(string normalizedName)
        {
            return string.Equals(normalizedName, "armature", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedName, "skeleton", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedName, "rig", StringComparison.OrdinalIgnoreCase);
        }

        private static bool BoneSetsMatch(HashSet<string> modelBones, HashSet<string> animationBones)
        {
            int common = 0;
            foreach (string modelBone in modelBones)
            {
                if (animationBones.Contains(modelBone))
                {
                    common++;
                }
            }

            if (common == modelBones.Count && common == animationBones.Count)
            {
                return true;
            }

            if (common < 8)
            {
                return false;
            }

            float modelCoverage = common / (float)modelBones.Count;
            float animationCoverage = common / (float)animationBones.Count;
            return modelCoverage >= 0.95f && animationCoverage >= 0.90f;
        }

        internal readonly struct ClipCompatibilityDiagnostic
        {
            public readonly bool compatible;
            public readonly string animationPath;
            public readonly string reason;
            public readonly int transformBindingCount;
            public readonly int missingBindingCount;
            public readonly string firstMissingBinding;
            public readonly bool usedAssetSkeletonFallback;

            private ClipCompatibilityDiagnostic(
                bool compatible,
                string animationPath,
                string reason,
                int transformBindingCount,
                int missingBindingCount,
                string firstMissingBinding,
                bool usedAssetSkeletonFallback)
            {
                this.compatible = compatible;
                this.animationPath = animationPath;
                this.reason = reason;
                this.transformBindingCount = transformBindingCount;
                this.missingBindingCount = missingBindingCount;
                this.firstMissingBinding = firstMissingBinding;
                this.usedAssetSkeletonFallback = usedAssetSkeletonFallback;
            }

            public static ClipCompatibilityDiagnostic Compatible(
                string animationPath,
                string reason,
                int transformBindingCount = 0,
                int missingBindingCount = 0,
                string firstMissingBinding = "",
                bool usedAssetSkeletonFallback = false)
            {
                return new ClipCompatibilityDiagnostic(true, animationPath, reason, transformBindingCount, missingBindingCount, firstMissingBinding, usedAssetSkeletonFallback);
            }

            public static ClipCompatibilityDiagnostic Incompatible(
                string animationPath,
                string reason,
                int transformBindingCount = 0,
                int missingBindingCount = 0,
                string firstMissingBinding = "",
                bool usedAssetSkeletonFallback = false)
            {
                return new ClipCompatibilityDiagnostic(false, animationPath, reason, transformBindingCount, missingBindingCount, firstMissingBinding, usedAssetSkeletonFallback);
            }
        }

        private readonly struct ClipBindingCompatibility
        {
            public readonly int transformBindingCount;
            public readonly int missingBindingCount;
            public readonly string firstMissingBinding;

            public ClipBindingCompatibility(int transformBindingCount, int missingBindingCount, string firstMissingBinding)
            {
                this.transformBindingCount = transformBindingCount;
                this.missingBindingCount = missingBindingCount;
                this.firstMissingBinding = firstMissingBinding;
            }
        }
    }
}
