# Testing asset sources

This project starts with permissive or free prototype sources. Keep downloaded license files in `Assets/External/TestAssets/Licenses` and avoid committing third-party assets until the license has been checked.

## Recommended first-pass sources

| Source | Use | License notes | Link |
| --- | --- | --- | --- |
| Quaternius | Low-poly characters, monsters, props, environments | Free packs are listed as CC0; verify the pack page before shipping | https://quaternius.com/ |
| Quaternius Universal Animation Library | Humanoid locomotion, traversal, combat, death, emotes | Free portion is listed as CC0 | https://quaternius.com/packs/universalanimationlibrary.html |
| Quaternius Universal Animation Library 2 | Humanoid melee combos, parkour, zombie locomotion, action variants | Free portion is listed as CC0 | https://quaternius.com/packs/universalanimationlibrary2.html |
| Kenney assets | Prototype props, UI, modular environments, some animated characters | Many packs are CC0; verify per pack page | https://kenney.nl/assets |
| Kenney Animated Characters 3 | Simple animated humanoids for scale and controller tests | CC0 1.0 Universal on itch page | https://kenney-assets.itch.io/animated-characters-3 |
| Mixamo | Fast humanoid animation tests and placeholder characters | Free with Adobe ID; do not resell or redistribute as an asset library | https://www.mixamo.com/ |
| Khronos glTF Sample Assets | Technical loader and animation validation | License varies by model and is listed per entry | https://github.khronos.org/glTF-Assets/ |

## Unity import notes

- Prefer FBX for immediate Unity humanoid retargeting.
- For Mixamo, use FBX for Unity. Character downloads should include skin. Animation-only downloads can use "Without Skin" after the avatar is configured.
- Keep one neutral humanoid scale target in `Assets/External/TestAssets/Characters`.
- Put raw downloads in `Assets/External/TestAssets`, then create cleaned prefabs under a future game folder such as `Assets/Game/Characters`.
- For networked action tests, animation clips should get a `CombatAnimationDefinition` asset so hit windows, combo windows, root locks, and sync points are exported separately from the visual clip.

## Suggested starter set

1. One humanoid placeholder character from Mixamo or Kenney.
2. Idle, walk, run, sprint, jump, dodge, light attack, heavy attack, hit reaction, knockdown, and death clips.
3. One Quaternius or Kenney environment pack for scale reference.
4. One Khronos animated glTF sample only for loader validation, not as the main Unity animation source.
