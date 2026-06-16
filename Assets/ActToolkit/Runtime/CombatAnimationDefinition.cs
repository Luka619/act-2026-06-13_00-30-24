using System;
using System.Collections.Generic;
using UnityEngine;

namespace ActToolkit
{
    public static class CombatInputActionNames
    {
        public const string LegacyAttack = "attack";
        public const string LightAttack = "Attack.Light";
        public const string LightAttackUp = "Stick.Up+Attack.Light";
        public const string LightAttackDown = "Stick.Down+Attack.Light";
        public const string LightAttackLeft = "Stick.Left+Attack.Light";
        public const string LightAttackRight = "Stick.Right+Attack.Light";
        public const string HeavyAttack = "Attack.Heavy";
        public const string HeavyAttackUp = "Stick.Up+Attack.Heavy";
        public const string HeavyAttackDown = "Stick.Down+Attack.Heavy";
        public const string HeavyAttackLeft = "Stick.Left+Attack.Heavy";
        public const string HeavyAttackRight = "Stick.Right+Attack.Heavy";
        public const string Dodge = "Dodge";
        public const string Jump = "Jump";
        public const string Guard = "Guard";

        public static readonly string[] AuthoringNames =
        {
            LightAttack,
            LightAttackUp,
            LightAttackDown,
            LightAttackLeft,
            LightAttackRight,
            HeavyAttack,
            HeavyAttackUp,
            HeavyAttackDown,
            HeavyAttackLeft,
            HeavyAttackRight,
            Dodge,
            Jump,
            Guard
        };

        public static readonly string[] AuthoringLabels =
        {
            "Square / Light",
            "Left Stick Up + Square",
            "Left Stick Down + Square",
            "Left Stick Left + Square",
            "Left Stick Right + Square",
            "Triangle / Heavy",
            "Left Stick Up + Triangle",
            "Left Stick Down + Triangle",
            "Left Stick Left + Triangle",
            "Left Stick Right + Triangle",
            "Circle / Dodge",
            "Cross / Jump",
            "L1 / Guard"
        };

        public static bool ExactMatches(string candidate, string requested)
        {
            return string.Equals(Normalize(candidate), Normalize(requested), StringComparison.OrdinalIgnoreCase);
        }

        public static bool Matches(string candidate, string requested)
        {
            string left = Normalize(candidate);
            string right = Normalize(requested);
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IsGenericLight(left) && IsLightVariant(right)
                || IsLightVariant(left) && IsGenericLight(right)
                || IsGenericHeavy(left) && IsHeavyVariant(right)
                || IsHeavyVariant(left) && IsGenericHeavy(right);
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

        public static string WithMoveDirection(string baseAction, Vector2 move, float directionThreshold = 0.45f)
        {
            string normalized = Normalize(baseAction);
            string direction = DominantStickDirection(move, directionThreshold);
            if (string.IsNullOrEmpty(direction))
            {
                return normalized;
            }

            if (string.Equals(normalized, LightAttack, StringComparison.OrdinalIgnoreCase))
            {
                return "Stick." + direction + "+Attack.Light";
            }

            if (string.Equals(normalized, HeavyAttack, StringComparison.OrdinalIgnoreCase))
            {
                return "Stick." + direction + "+Attack.Heavy";
            }

            return normalized;
        }

        public static string DisplayLabel(string inputAction)
        {
            string normalized = Normalize(inputAction);
            for (int i = 0; i < AuthoringNames.Length; i++)
            {
                if (string.Equals(AuthoringNames[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return AuthoringLabels[i];
                }
            }

            return string.IsNullOrWhiteSpace(inputAction) ? "None" : inputAction;
        }

        public static bool TryDescribeSequence(string inputAction, out string stickToken, out string buttonToken, out string actionToken)
        {
            stickToken = string.Empty;
            buttonToken = string.Empty;
            actionToken = string.Empty;

            string normalized = Normalize(inputAction);
            if (normalized.StartsWith("Stick.", StringComparison.OrdinalIgnoreCase))
            {
                int plusIndex = normalized.IndexOf('+');
                if (plusIndex > 6)
                {
                    string direction = normalized.Substring(6, plusIndex - 6);
                    stickToken = DirectionToken(direction);
                    normalized = normalized.Substring(plusIndex + 1);
                }
            }

            if (string.Equals(normalized, LightAttack, StringComparison.OrdinalIgnoreCase))
            {
                buttonToken = "□";
                actionToken = "Light";
                return true;
            }

            if (string.Equals(normalized, HeavyAttack, StringComparison.OrdinalIgnoreCase))
            {
                buttonToken = "△";
                actionToken = "Heavy";
                return true;
            }

            if (string.Equals(normalized, Dodge, StringComparison.OrdinalIgnoreCase))
            {
                buttonToken = "○";
                actionToken = "Dodge";
                return true;
            }

            if (string.Equals(normalized, Jump, StringComparison.OrdinalIgnoreCase))
            {
                buttonToken = "×";
                actionToken = "Jump";
                return true;
            }

            if (string.Equals(normalized, Guard, StringComparison.OrdinalIgnoreCase))
            {
                buttonToken = "L1";
                actionToken = "Guard";
                return true;
            }

            buttonToken = string.IsNullOrWhiteSpace(inputAction) ? "?" : inputAction;
            actionToken = "Custom";
            return false;
        }

        private static string DominantStickDirection(Vector2 move, float threshold)
        {
            if (move.sqrMagnitude < threshold * threshold)
            {
                return string.Empty;
            }

            if (Mathf.Abs(move.y) >= Mathf.Abs(move.x))
            {
                return move.y >= 0f ? "Up" : "Down";
            }

            return move.x >= 0f ? "Right" : "Left";
        }

        private static string DirectionToken(string direction)
        {
            if (string.Equals(direction, "Up", StringComparison.OrdinalIgnoreCase))
            {
                return "LS ↑";
            }

            if (string.Equals(direction, "Down", StringComparison.OrdinalIgnoreCase))
            {
                return "LS ↓";
            }

            if (string.Equals(direction, "Left", StringComparison.OrdinalIgnoreCase))
            {
                return "LS ←";
            }

            if (string.Equals(direction, "Right", StringComparison.OrdinalIgnoreCase))
            {
                return "LS →";
            }

            return "LS";
        }

        private static bool IsLightAlias(string inputAction)
        {
            return string.Equals(inputAction, LegacyAttack, StringComparison.OrdinalIgnoreCase)
                || string.Equals(inputAction, LightAttack, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGenericLight(string inputAction)
        {
            return string.Equals(inputAction, LightAttack, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLightVariant(string inputAction)
        {
            return IsGenericLight(inputAction)
                || string.Equals(inputAction, LightAttackUp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(inputAction, LightAttackDown, StringComparison.OrdinalIgnoreCase)
                || string.Equals(inputAction, LightAttackLeft, StringComparison.OrdinalIgnoreCase)
                || string.Equals(inputAction, LightAttackRight, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGenericHeavy(string inputAction)
        {
            return string.Equals(inputAction, HeavyAttack, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHeavyVariant(string inputAction)
        {
            return IsGenericHeavy(inputAction)
                || string.Equals(inputAction, HeavyAttackUp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(inputAction, HeavyAttackDown, StringComparison.OrdinalIgnoreCase)
                || string.Equals(inputAction, HeavyAttackLeft, StringComparison.OrdinalIgnoreCase)
                || string.Equals(inputAction, HeavyAttackRight, StringComparison.OrdinalIgnoreCase);
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
