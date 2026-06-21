# Downloaded test asset manifest

Downloaded on 2026-06-13 for prototype use in the Act Unity project.

## Kenney Animated Characters 3

- Source page: https://opengameart.org/content/animated-characters-3
- Original creator page: https://kenney-assets.itch.io/animated-characters-3
- Downloaded file: `Assets/External/TestAssets/_Downloads/kenney_animated-characters-3.zip`
- Archived to: `Assets/External/TestAssets/_ArchivedIncompatibleForMannequinF/Characters/Kenney_AnimatedCharacters3`
- SHA256: `6C197462DC9976B522A07D11E0FB42B9CAA7CF71D39ABBC3CA708B69F13A40DD`
- License: CC0, confirmed by included `License.txt`
- Useful files:
  - `Model/characterMedium.fbx`
  - `Animations/idle.fbx`
  - `Animations/jump.fbx`
  - `Animations/run.fbx`
- Preview clip copies:
  - `Assets/External/TestAssets/_ArchivedIncompatibleForMannequinF/Animations/PreviewClips/Kenney_AnimatedCharacters3/idle.fbx`
  - `Assets/External/TestAssets/_ArchivedIncompatibleForMannequinF/Animations/PreviewClips/Kenney_AnimatedCharacters3/jump.fbx`
  - `Assets/External/TestAssets/_ArchivedIncompatibleForMannequinF/Animations/PreviewClips/Kenney_AnimatedCharacters3/run.fbx`

## Quaternius Universal Animation Library Standard

- Source page: https://quaternius.itch.io/universal-animation-library
- Original creator page: https://quaternius.com/packs/universalanimationlibrary.html
- Downloaded file: `Assets/External/TestAssets/_Downloads/universal_animation_librarystandard.zip`
- Extracted to: `Assets/External/TestAssets/Animations/Quaternius_UniversalAnimationLibrary_Standard`
- SHA256: `CC73FC4E495B82958207316596317A3F40B9FA38065BDE1027937452DA537724`
- Version note: refreshed from the 2026-06-16 Standard package. Use `UAL1_Standard.fbx` for current locomotion clips; it is the root-motion-disabled export. `UAL1_Standard_RM.fbx` is kept in the full extracted package only as a reference copy.
- License: CC0, confirmed by included `License.txt`
- Useful Unity files:
  - `Universal Animation Library [Standard]/Unity/UAL1_Standard.fbx`
  - `Universal Animation Library [Standard]/Unity/UAL1_Standard_RM.fbx`
- Preview clip copy:
  - `Assets/External/TestAssets/Animations/PreviewClips/Quaternius_UniversalAnimationLibrary_Standard/UAL1_Standard.fbx`

## Quaternius Universal Animation Library 2 Standard

- Source page: https://quaternius.itch.io/universal-animation-library-2
- Original creator page: https://quaternius.com/packs/universalanimationlibrary2.html
- Downloaded file: `Assets/External/TestAssets/_Downloads/universal_animation_library_2standard.zip`
- Extracted to: `Assets/External/TestAssets/Animations/Quaternius_UniversalAnimationLibrary2_Standard`
- SHA256: `4008EA208A604773A2B2177D965F0F5D3195498B5BF838C3F5785D68E95F2A68`
- Version note: refreshed from the 2026-06-16 Standard package. Use `UAL2_Standard.fbx` for current test combat clips; it is the root-motion-disabled export. `UAL2_Standard_RM.fbx` is kept in the full extracted package only as a reference copy.
- License: CC0, confirmed by included `License.txt`
- Useful Unity files:
  - `Universal Animation Library 2 [Standard]/Unity/UAL2_Standard.fbx`
  - `Universal Animation Library 2 [Standard]/Unity/UAL2_Standard_RM.fbx`
  - `Universal Animation Library 2 [Standard]/Female Mannequin/Unity/Mannequin_F.fbx`
- Preview model copy:
  - `Assets/External/TestAssets/Characters/Quaternius_UniversalAnimationLibrary2_Standard/Mannequin_F.fbx`
- Preview clip copy:
  - `Assets/External/TestAssets/Animations/PreviewClips/Quaternius_UniversalAnimationLibrary2_Standard/UAL2_Standard.fbx`

## KayKit Character Animations 1.2

- Source page: https://opengameart.org/content/kaykit-character-animations
- Original creator page: https://kaylousberg.itch.io/kaykit-character-animations
- Downloaded file: `Assets/External/TestAssets/_Downloads/kaykit_character_animations_1.2.zip`
- Archived to: `Assets/External/TestAssets/_ArchivedIncompatibleForMannequinF/Animations/KayKit_CharacterAnimations`
- SHA256: `C9D3FBEA492DC6EDD0903939369A564C2240B892430BCD99E0AEE4876110BB8F`
- License: CC0, confirmed by included `License.txt`
- Useful files:
  - `Animations/fbx/KayKit Animated Character_v1.2.fbx`
  - `Animations/fbx/Single Animations/Attack(1h).fbx`
  - `Animations/fbx/Single Animations/AttackCombo.fbx`
  - `Animations/fbx/Single Animations/AttackSpinning.fbx`
  - `Animations/fbx/Single Animations/HeavyAttack.fbx`
  - `Animations/fbx/Single Animations/Jump.fbx`
  - `Animations/fbx/Single Animations/Hop.fbx`
  - `Animations/fbx/Single Animations/Roll.fbx`
  - `Animations/fbx/Single Animations/DashFront.fbx`
  - `Animations/fbx/Single Animations/Block.fbx`
- Preview clip copies:
  - `Assets/External/TestAssets/_ArchivedIncompatibleForMannequinF/Animations/PreviewClips/KayKit_CharacterAnimations`

## Import notes

- Default authoring folders currently only expose the Quaternius Universal Animation Library 2 Mannequin_F model and matching UAL2 animation clips.
- Archived folders are intentionally kept outside the default model and preview clip roots because their skeletons do not match Mannequin_F.
- Set character rigs to Humanoid where possible.
- Use the Unity-specific FBX files from the Quaternius folders first.
- Keep the zip files until the first playable prototype is stable, then decide whether to remove raw downloads from source control.
