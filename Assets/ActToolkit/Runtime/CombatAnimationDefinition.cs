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
        public const string ButtonR1 = "Button.R1";
        public const string ButtonL2 = "Button.L2";
        public const string ButtonR2 = "Button.R2";
        public const string ButtonL3 = "Button.L3";
        public const string ButtonR3 = "Button.R3";
        public const string DPadUp = "DPad.Up";
        public const string DPadDown = "DPad.Down";
        public const string DPadLeft = "DPad.Left";
        public const string DPadRight = "DPad.Right";

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

            string[] leftTokens = GetInputActionTokens(left);
            string[] rightTokens = GetInputActionTokens(right);
            if (leftTokens.Length == 0 || rightTokens.Length == 0)
            {
                return false;
            }

            string leftPrimary = PrimaryToken(leftTokens);
            string rightPrimary = PrimaryToken(rightTokens);
            if (string.IsNullOrWhiteSpace(leftPrimary)
                || !string.Equals(leftPrimary, rightPrimary, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return TokensContainAllForFallback(rightTokens, leftTokens);
        }

        public static string Normalize(string inputAction)
        {
            if (string.IsNullOrWhiteSpace(inputAction))
            {
                return string.Empty;
            }

            string trimmed = inputAction.Trim();
            return ComposeInputAction(trimmed.Split('+'));
        }

        public static string WithMoveDirection(string baseAction, Vector2 move, float directionThreshold = 0.45f)
        {
            return WithMoveDirection(baseAction, move, false, directionThreshold);
        }

        public static string WithMoveDirection(string baseAction, Vector2 move, bool guardHeld, float directionThreshold = 0.45f)
        {
            string normalized = Normalize(baseAction);
            string direction = DominantStickDirection(move, directionThreshold);
            return ComposeInputAction(guardHeld, direction, normalized);
        }

        public static string ComposeInputAction(bool guard, string direction, string primaryAction)
        {
            string primary = NormalizePrimaryAction(primaryAction);
            if (string.IsNullOrWhiteSpace(primary))
            {
                return string.Empty;
            }

            List<string> tokens = new List<string>();
            if (guard && !string.Equals(primary, Guard, StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(Guard);
            }

            string normalizedDirection = NormalizeDirection(direction);
            if (!string.IsNullOrWhiteSpace(normalizedDirection))
            {
                tokens.Add("Stick." + normalizedDirection);
            }

            tokens.Add(primary);
            return ComposeInputAction(tokens);
        }

        public static string ComposeInputAction(IEnumerable<string> tokens)
        {
            List<string> normalizedTokens = new List<string>();
            foreach (string token in tokens)
            {
                string normalized = NormalizeInputToken(token);
                if (string.IsNullOrWhiteSpace(normalized) || ContainsToken(normalizedTokens, normalized))
                {
                    continue;
                }

                normalizedTokens.Add(normalized);
            }

            normalizedTokens.Sort(CompareInputTokens);
            return string.Join("+", normalizedTokens);
        }

        public static string ComposeInputAction(IEnumerable<string> heldTokens, Vector2 move, string pressedToken, float directionThreshold = 0.45f)
        {
            List<string> tokens = new List<string>();
            if (heldTokens != null)
            {
                tokens.AddRange(heldTokens);
            }

            string direction = DominantStickDirection(move, directionThreshold);
            if (!string.IsNullOrWhiteSpace(direction))
            {
                tokens.Add("Stick." + direction);
            }

            tokens.Add(pressedToken);
            return ComposeInputAction(tokens);
        }

        public static string[] GetInputActionTokens(string inputAction)
        {
            string normalized = Normalize(inputAction);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return Array.Empty<string>();
            }

            return normalized.Split('+');
        }

        public static bool TryGetInputActionParts(string inputAction, out bool guard, out string direction, out string primaryAction)
        {
            guard = false;
            direction = string.Empty;
            primaryAction = string.Empty;

            if (string.IsNullOrWhiteSpace(inputAction))
            {
                return false;
            }

            string[] tokens = inputAction.Split('+');
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = NormalizeInputToken(tokens[i]);
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                string tokenDirection = NormalizeDirection(token);
                if (!string.IsNullOrWhiteSpace(tokenDirection))
                {
                    direction = tokenDirection;
                    continue;
                }

                if (string.Equals(token, Guard, StringComparison.OrdinalIgnoreCase))
                {
                    guard = true;
                }
                else
                {
                    primaryAction = token;
                }
            }

            if (string.IsNullOrWhiteSpace(primaryAction) && guard)
            {
                primaryAction = Guard;
                guard = false;
            }

            return !string.IsNullOrWhiteSpace(primaryAction);
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
            if (TryGetInputActionParts(normalized, out bool guard, out string direction, out string primaryAction))
            {
                List<string> prefixTokens = new List<string>();
                if (guard)
                {
                    prefixTokens.Add("L1");
                }

                if (!string.IsNullOrWhiteSpace(direction))
                {
                    prefixTokens.Add(DirectionToken(direction));
                }

                stickToken = string.Join(" + ", prefixTokens);
                normalized = primaryAction;
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

        public static string DisplayToken(string token)
        {
            string normalized = NormalizeInputToken(token);
            if (string.Equals(normalized, LightAttack, StringComparison.OrdinalIgnoreCase))
            {
                return "□";
            }

            if (string.Equals(normalized, HeavyAttack, StringComparison.OrdinalIgnoreCase))
            {
                return "△";
            }

            if (string.Equals(normalized, Dodge, StringComparison.OrdinalIgnoreCase))
            {
                return "○";
            }

            if (string.Equals(normalized, Jump, StringComparison.OrdinalIgnoreCase))
            {
                return "×";
            }

            if (string.Equals(normalized, Guard, StringComparison.OrdinalIgnoreCase))
            {
                return "L1";
            }

            if (string.Equals(normalized, ButtonR1, StringComparison.OrdinalIgnoreCase))
            {
                return "R1";
            }

            if (string.Equals(normalized, ButtonL2, StringComparison.OrdinalIgnoreCase))
            {
                return "L2";
            }

            if (string.Equals(normalized, ButtonR2, StringComparison.OrdinalIgnoreCase))
            {
                return "R2";
            }

            if (string.Equals(normalized, ButtonL3, StringComparison.OrdinalIgnoreCase))
            {
                return "L3";
            }

            if (string.Equals(normalized, ButtonR3, StringComparison.OrdinalIgnoreCase))
            {
                return "R3";
            }

            if (normalized.StartsWith("Stick.", StringComparison.OrdinalIgnoreCase))
            {
                return DirectionToken(normalized.Substring(6));
            }

            if (normalized.StartsWith("DPad.", StringComparison.OrdinalIgnoreCase))
            {
                return "DPad " + normalized.Substring(5);
            }

            return normalized;
        }

        private static string NormalizeInputToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            string value = token.Trim();
            string direction = NormalizeDirection(value);
            if (!string.IsNullOrWhiteSpace(direction))
            {
                return "Stick." + direction;
            }

            string primary = NormalizePrimaryAction(value);
            if (!string.IsNullOrWhiteSpace(primary))
            {
                return primary;
            }

            if (string.Equals(value, "R1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "RightShoulder", StringComparison.OrdinalIgnoreCase))
            {
                return ButtonR1;
            }

            if (string.Equals(value, "L2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "LeftTrigger", StringComparison.OrdinalIgnoreCase))
            {
                return ButtonL2;
            }

            if (string.Equals(value, "R2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "RightTrigger", StringComparison.OrdinalIgnoreCase))
            {
                return ButtonR2;
            }

            if (string.Equals(value, "L3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "LeftStickButton", StringComparison.OrdinalIgnoreCase))
            {
                return ButtonL3;
            }

            if (string.Equals(value, "R3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "RightStickButton", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "LockOn", StringComparison.OrdinalIgnoreCase))
            {
                return ButtonR3;
            }

            string dpad = NormalizeDPad(value);
            if (!string.IsNullOrWhiteSpace(dpad))
            {
                return "DPad." + dpad;
            }

            return value;
        }

        private static int CompareInputTokens(string left, string right)
        {
            int leftOrder = InputTokenOrder(left);
            int rightOrder = InputTokenOrder(right);
            if (leftOrder != rightOrder)
            {
                return leftOrder.CompareTo(rightOrder);
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static int InputTokenOrder(string token)
        {
            if (string.Equals(token, Guard, StringComparison.OrdinalIgnoreCase))
            {
                return 10;
            }

            if (token.StartsWith("Stick.", StringComparison.OrdinalIgnoreCase))
            {
                return 20;
            }

            if (token.StartsWith("DPad.", StringComparison.OrdinalIgnoreCase))
            {
                return 30;
            }

            if (string.Equals(token, LightAttack, StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, HeavyAttack, StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, Dodge, StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, Jump, StringComparison.OrdinalIgnoreCase))
            {
                return 50;
            }

            if (token.StartsWith("Button.", StringComparison.OrdinalIgnoreCase))
            {
                return 60;
            }

            return 100;
        }

        private static bool ContainsToken(List<string> tokens, string token)
        {
            foreach (string existing in tokens)
            {
                if (string.Equals(existing, token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string PrimaryToken(string[] tokens)
        {
            foreach (string token in tokens)
            {
                if (InputTokenOrder(token) == 50)
                {
                    return token;
                }
            }

            for (int i = tokens.Length - 1; i >= 0; i--)
            {
                if (!IsDirectionToken(tokens[i])
                    && !string.Equals(tokens[i], Guard, StringComparison.OrdinalIgnoreCase)
                    && !tokens[i].StartsWith("DPad.", StringComparison.OrdinalIgnoreCase))
                {
                    return tokens[i];
                }
            }

            return tokens.Length == 0 ? string.Empty : tokens[tokens.Length - 1];
        }

        private static bool IsDirectionToken(string token)
        {
            return !string.IsNullOrWhiteSpace(token)
                && token.StartsWith("Stick.", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TokensContainAllForFallback(string[] requestedTokens, string[] candidateTokens)
        {
            foreach (string candidateToken in candidateTokens)
            {
                bool found = false;
                foreach (string requestedToken in requestedTokens)
                {
                    if (string.Equals(candidateToken, requestedToken, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found && IsDirectionToken(candidateToken))
                {
                    return false;
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private static string NormalizeDirection(string direction)
        {
            if (string.IsNullOrWhiteSpace(direction))
            {
                return string.Empty;
            }

            string value = direction.Trim();
            if (value.StartsWith("Stick.", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(6);
            }

            if (string.Equals(value, "LS Up", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Up", StringComparison.OrdinalIgnoreCase)
                || value == "↑")
            {
                return "Up";
            }

            if (string.Equals(value, "LS Down", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Down", StringComparison.OrdinalIgnoreCase)
                || value == "↓")
            {
                return "Down";
            }

            if (string.Equals(value, "LS Left", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase)
                || value == "←")
            {
                return "Left";
            }

            if (string.Equals(value, "LS Right", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase)
                || value == "→")
            {
                return "Right";
            }

            return string.Empty;
        }

        private static string NormalizeDPad(string direction)
        {
            if (string.IsNullOrWhiteSpace(direction))
            {
                return string.Empty;
            }

            string value = direction.Trim();
            if (value.StartsWith("DPad.", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(5);
            }

            if (string.Equals(value, "DPad Up", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Up", StringComparison.OrdinalIgnoreCase) && direction.StartsWith("DPad", StringComparison.OrdinalIgnoreCase))
            {
                return "Up";
            }

            if (string.Equals(value, "DPad Down", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Down", StringComparison.OrdinalIgnoreCase) && direction.StartsWith("DPad", StringComparison.OrdinalIgnoreCase))
            {
                return "Down";
            }

            if (string.Equals(value, "DPad Left", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase) && direction.StartsWith("DPad", StringComparison.OrdinalIgnoreCase))
            {
                return "Left";
            }

            if (string.Equals(value, "DPad Right", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase) && direction.StartsWith("DPad", StringComparison.OrdinalIgnoreCase))
            {
                return "Right";
            }

            return string.Empty;
        }

        private static string NormalizePrimaryAction(string primaryAction)
        {
            if (string.IsNullOrWhiteSpace(primaryAction))
            {
                return string.Empty;
            }

            string value = primaryAction.Trim();
            if (IsLightAlias(value)
                || string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Square", StringComparison.OrdinalIgnoreCase)
                || value == "□")
            {
                return LightAttack;
            }

            if (string.Equals(value, HeavyAttack, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Heavy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Triangle", StringComparison.OrdinalIgnoreCase)
                || value == "△")
            {
                return HeavyAttack;
            }

            if (string.Equals(value, Dodge, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Circle", StringComparison.OrdinalIgnoreCase)
                || value == "○")
            {
                return Dodge;
            }

            if (string.Equals(value, Jump, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Cross", StringComparison.OrdinalIgnoreCase)
                || value == "×")
            {
                return Jump;
            }

            if (string.Equals(value, Guard, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "L1", StringComparison.OrdinalIgnoreCase))
            {
                return Guard;
            }

            return string.Empty;
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
