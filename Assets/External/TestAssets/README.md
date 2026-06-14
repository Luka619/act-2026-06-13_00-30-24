# Test assets

Drop prototype-only downloads here. Keep the original license or terms page export in `Licenses`.

Suggested layout:

- `Characters`: FBX characters, avatar prefabs, and prototype rigs.
- `Animations`: FBX animation clips, grouped by source and character rig.
- `Environments`: whitebox reference props, modular kits, and test props.
- `Licenses`: source license files, screenshots, or copied terms for each downloaded pack.

Move cleaned, project-owned prefabs into a future `Assets/Game` folder before production use.

The Combat Animation Editor scans `Characters` by default. Put previewable character models in this folder or a child folder named `Model`, `Models`, or a pack-specific equivalent. Child folders named `Animation` or `Animations` are ignored for preview-model selection.

The Combat Animation Editor scans `Animations/PreviewClips` by default. Put Unity-ready FBX files or `.anim` files there to show them in the animation dropdown.
