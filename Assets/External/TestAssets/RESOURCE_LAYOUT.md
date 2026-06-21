# Test asset layout

Current active test character:

- `Characters/Quaternius_UniversalAnimationLibrary2_Standard/Mannequin_F.fbx`

Current active preview animation sources:

- `Animations/PreviewClips/Quaternius_UniversalAnimationLibrary_Standard/UAL1_Standard.fbx`
- `Animations/PreviewClips/Quaternius_UniversalAnimationLibrary2_Standard/UAL2_Standard.fbx`

`UAL1_Standard.fbx` is the root-motion-disabled locomotion source currently used for `Idle_Loop`, `Walk_Loop`, and `Jog_Fwd_Loop`.

`UAL2_Standard.fbx` is the root-motion-disabled export and should be the default for combat authoring. `UAL2_Standard_RM.fbx` is kept out of the active preview folder so it does not appear in normal clip selection.

The animation editor defaults to `Characters` for models and `Animations/PreviewClips` for animation clips. Keep those active folders limited to assets that match the current Mannequin_F skeleton so authoring only presents usable clips.

Assets that are useful for reference but do not match the current Mannequin_F skeleton are kept under `_ArchivedIncompatibleForMannequinF` instead of being deleted.
