using UnityEngine;

namespace ActToolkit
{
    [CreateAssetMenu(menuName = "Act Toolkit/Character Action Profile", fileName = "CharacterActionProfile")]
    public sealed class CharacterActionProfile : ScriptableObject
    {
        public string characterId = "character.new";
        public string displayName = "New Character";
        public GameObject modelPrefab;
        public Avatar avatar;
        public AnimationClip idleClip;
        public AnimationClip walkClip;
        public AnimationClip moveClip;
        public CombatActionDatabase comboTable;

        public void EnsureDefaults()
        {
            if (string.IsNullOrWhiteSpace(characterId))
            {
                characterId = "character." + name.ToLowerInvariant().Replace(' ', '_');
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }
        }
    }
}
