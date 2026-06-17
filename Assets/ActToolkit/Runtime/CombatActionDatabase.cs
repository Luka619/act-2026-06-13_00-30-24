using System.Collections.Generic;
using UnityEngine;

namespace ActToolkit
{
    [System.Serializable]
    public sealed class CombatActionEntry
    {
        public string id = System.Guid.NewGuid().ToString("N");
        public string inputAction = CombatInputActionNames.LightAttack;
        public CombatAnimationDefinition targetDefinition;
        public string targetActionId = "action.light_1";
        public bool serverAuthoritative = true;
    }

    [CreateAssetMenu(menuName = "Act Toolkit/Combat Action Database", fileName = "CombatActionDatabase")]
    public sealed class CombatActionDatabase : ScriptableObject
    {
        public List<CombatActionEntry> entryActions = new List<CombatActionEntry>();
        public List<CombatAnimationDefinition> actions = new List<CombatAnimationDefinition>();

        private readonly Dictionary<string, CombatAnimationDefinition> lookup = new Dictionary<string, CombatAnimationDefinition>();

        private void OnEnable()
        {
            EnsureEntryActions();
            RebuildLookup();
        }

        public void EnsureEntryActions()
        {
            if (entryActions == null)
            {
                entryActions = new List<CombatActionEntry>();
                return;
            }

            for (int i = 0; i < entryActions.Count; i++)
            {
                if (entryActions[i] == null)
                {
                    entryActions[i] = new CombatActionEntry();
                }

                if (entryActions[i].targetDefinition != null)
                {
                    entryActions[i].targetActionId = entryActions[i].targetDefinition.EnsureInternalActionId();
                }
            }
        }

        public void RebuildLookup()
        {
            lookup.Clear();
            if (actions == null)
            {
                return;
            }

            foreach (CombatAnimationDefinition action in actions)
            {
                if (action == null)
                {
                    continue;
                }

                string actionId = action.EnsureInternalActionId();
                if (!string.IsNullOrWhiteSpace(actionId))
                {
                    lookup[actionId] = action;
                }
            }
        }

        public bool TryGetAction(string actionId, out CombatAnimationDefinition action)
        {
            if (lookup.Count == 0)
            {
                RebuildLookup();
            }

            if (lookup.TryGetValue(actionId, out action))
            {
                return true;
            }

            if (actions != null)
            {
                foreach (CombatAnimationDefinition candidate in actions)
                {
                    if (candidate != null && string.Equals(candidate.DisplayName, actionId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        action = candidate;
                        return true;
                    }
                }
            }

            action = null;
            return false;
        }

        public bool TryGetEntryAction(string inputAction, out string targetActionId)
        {
            targetActionId = string.Empty;
            EnsureEntryActions();

            foreach (CombatActionEntry entry in entryActions)
            {
                if (entry == null || !CombatInputActionNames.ExactMatches(entry.inputAction, inputAction))
                {
                    continue;
                }

                targetActionId = entry.targetDefinition != null ? entry.targetDefinition.EnsureInternalActionId() : entry.targetActionId;
                return !string.IsNullOrWhiteSpace(targetActionId);
            }

            foreach (CombatActionEntry entry in entryActions)
            {
                if (entry == null || !CombatInputActionNames.Matches(entry.inputAction, inputAction))
                {
                    continue;
                }

                targetActionId = entry.targetDefinition != null ? entry.targetDefinition.EnsureInternalActionId() : entry.targetActionId;
                return !string.IsNullOrWhiteSpace(targetActionId);
            }

            return false;
        }

        public CombatAnimationDefinition FirstAction()
        {
            if (actions == null)
            {
                return null;
            }

            foreach (CombatAnimationDefinition action in actions)
            {
                if (action != null)
                {
                    return action;
                }
            }

            return null;
        }
    }
}
