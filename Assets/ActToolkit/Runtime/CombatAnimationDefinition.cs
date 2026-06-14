using System;
using System.Collections.Generic;
using UnityEngine;

namespace ActToolkit
{
    public static class CombatInputActionNames
    {
        public const string LegacyAttack = "attack";
        public const string LightAttack = "Attack.Light";
        public const string HeavyAttack = "Attack.Heavy";
        public const string Dodge = "Dodge";
        public const string Jump = "Jump";
        public const string Guard = "Guard";

        public static readonly string[] AuthoringNames =
        {
            LightAttack,
            HeavyAttack,
            Dodge,
            Jump,
            Guard
        };

        public static readonly string[] AuthoringLabels =
        {
            "Square / Light",
            "Triangle / Heavy",
            "Circle / Dodge",
            "Cross / Jump",
            "L1 / Guard"
        };

        public static bool Matches(string candidate, string requested)
        {
            string left = Normalize(candidate);
            string right = Normalize(requested);
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IsLightAlias(left) && IsLightAlias(right);
        }

        public static string Normalize(string inputAction)
        {
            if (string.IsNullOrWhiteSpace(inputAction))
            {
                return string.Empty;
            }

            string trimmed = inputAction.Trim();
            return IsLightAlias(trimmed) ? LightAttack : trimmed;
        }

        private static bool IsLightAlias(string inputAction)
        {
            return string.Equals(inputAction, LegacyAttack, StringComparison.OrdinalIgnoreCase)
                || string.Equals(inputAction, LightAttack, StringComparison.OrdinalIgnoreCase);
        }
    }

    public enum CombatAnimationEventKind
    {
        Hitbox,
        Hurtbox,
        Invulnerability,
        SuperArmor,
        MovementLock,
        RootMotionScale,
        ComboBranch,
        ProjectileSpawn,
        Vfx,
        Sfx,
        Footstep,
        NetworkSync,
        Custom
    }

    [Serializable]
    public sealed class CombatAnimationMarker
    {
        public string id = Guid.NewGuid().ToString("N");
        public CombatAnimationEventKind kind = CombatAnimationEventKind.Hitbox;

        [Range(0f, 1f)]
        public float normalizedTime;

        [Min(0f)]
        public float duration = 0.08f;

        public string gameplayTag = "combat.hit.light";
        public string payload;
        public Vector3 localOffset = new Vector3(0f, 1f, 0.8f);
        public Vector3 size = new Vector3(0.7f, 0.8f, 0.9f);
        public Color color = new Color(1f, 0.75f, 0.2f, 0.7f);
        public bool serverAuthoritative = true;

        public int StartFrame(AnimationClip clip, int frameRate)
        {
            if (clip == null || frameRate <= 0)
            {
                return 0;
            }

            return Mathf.RoundToInt(TimeSeconds(clip) * frameRate);
        }

        public int DurationFrames(int frameRate)
        {
            if (frameRate <= 0)
            {
                return 0;
            }

            return Mathf.Max(0, Mathf.RoundToInt(duration * frameRate));
        }

        public int EndFrame(AnimationClip clip, int frameRate)
        {
            return StartFrame(clip, frameRate) + DurationFrames(frameRate);
        }

        public float TimeSeconds(AnimationClip clip)
        {
            if (clip == null)
            {
                return 0f;
            }

            return Mathf.Clamp01(normalizedTime) * clip.length;
        }
    }

    [Serializable]
    public sealed class CombatActionLink
    {
        public string id = Guid.NewGuid().ToString("N");
        public string inputAction = CombatInputActionNames.LightAttack;
        public string triggerTag = "combat.combo.branch";
        public CombatAnimationDefinition targetDefinition;
        public string targetActionId = "action.next";
        public int startFrame;
        public int endFrame = 1;
        public bool serverAuthoritative = true;

        public float StartNormalizedTime(AnimationClip clip, int frameRate)
        {
            int frameCount = FrameCount(clip, frameRate);
            if (frameCount <= 0)
            {
                return 0f;
            }

            return Mathf.Clamp01((float)Mathf.Clamp(startFrame, 0, frameCount) / frameCount);
        }

        public float EndNormalizedTime(AnimationClip clip, int frameRate)
        {
            int frameCount = FrameCount(clip, frameRate);
            if (frameCount <= 0)
            {
                return 0f;
            }

            return Mathf.Clamp01((float)Mathf.Clamp(endFrame, 0, frameCount) / frameCount);
        }

        private static int FrameCount(AnimationClip clip, int frameRate)
        {
            if (clip == null || frameRate <= 0)
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.RoundToInt(clip.length * frameRate));
        }
    }

    [CreateAssetMenu(menuName = "Act Toolkit/Combat Animation Definition", fileName = "CombatAnimationDefinition")]
    public sealed class CombatAnimationDefinition : ScriptableObject
    {
        public AnimationClip clip;
        public string actionId = "action.new";
        public string stateName = "NewAction";
        public int authoringFrameRate = 60;
        public bool requiresNetworkSync = true;
        public bool loopPreview;

        [Tooltip("Runtime hint for netcode and character controller blending. The marker payload can override this per event.")]
        public float rootMotionScale = 1f;

        public List<CombatAnimationMarker> markers = new List<CombatAnimationMarker>();
        public List<CombatActionLink> actionLinks = new List<CombatActionLink>();

        public void EnsureMarkers()
        {
            authoringFrameRate = Mathf.Max(1, authoringFrameRate);

            if (markers == null)
            {
                markers = new List<CombatAnimationMarker>();
                return;
            }

            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i] == null)
                {
                    markers[i] = new CombatAnimationMarker();
                }
            }
        }

        public void EnsureActionLinks()
        {
            authoringFrameRate = Mathf.Max(1, authoringFrameRate);

            if (actionLinks == null)
            {
                actionLinks = new List<CombatActionLink>();
                return;
            }

            for (int i = 0; i < actionLinks.Count; i++)
            {
                if (actionLinks[i] == null)
                {
                    actionLinks[i] = new CombatActionLink();
                }

                if (actionLinks[i].targetDefinition != null)
                {
                    actionLinks[i].targetActionId = actionLinks[i].targetDefinition.actionId;
                }
            }
        }

        public void SortMarkers()
        {
            EnsureMarkers();
            markers.Sort((left, right) => left.normalizedTime.CompareTo(right.normalizedTime));
        }
    }
}
