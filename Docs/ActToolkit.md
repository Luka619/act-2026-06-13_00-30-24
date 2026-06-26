# Act Toolkit

Act Toolkit is a small Unity Editor toolset for the first playable slice of a multiplayer action game.

## Tools

- `Tools/Act Toolkit/Combat Animation Editor`
  - Uses a main authoring layout with a fixed status header, quick actions, a timeline workspace, and a right-side inspector.
  - Use the top status header to confirm the active preview model, animation clip, definition, current frame, and selected marker.
  - Select a model or animation from the asset library to apply it immediately; no extra confirm button is required.
  - Use the quick action bar for common authoring operations: create definition, add hitbox/combo/net-sync markers, export JSON, and open batch validation.
  - Displays imported models and animation clips with short normalized names such as `Character Medium [Kenney]`, `Female Mannequin [UAL2]`, and `Move / Run Forward [UAL2]`.
  - Filters the animation library against the selected preview model so clips with incompatible skeleton bindings are hidden.
  - Creates new definitions with normalized asset names and stable action ids, for example `CA_Move_Run_Forward` and `action.move.run_forward`.
  - Saves newly created definitions by default to `Assets/ActToolkit/Generated/CombatMvp/Actions`.
  - Keep preview playback, frame scrubbing, timeline lanes, marker overview, and the selected marker inspector visible together while tuning an action.
  - Use the right-side inspector to edit the selected marker's kind, frame, duration, tag, payload, offset, size, color, and server-authoritative flag.
  - When no marker is selected, the inspector edits the action definition and marker templates.
  - Use drawers for lower-frequency work: asset binding, root motion analysis, full action links, validation, and export.
  - Scans `Assets/External/TestAssets/Characters` as the default preview model library.
  - Select a detected model to replace the preview model instead of dragging an Animator by hand.
  - Scans `Assets/External/TestAssets/Animations/PreviewClips` as the default animation library.
  - Select a detected clip to use it immediately instead of dragging an AnimationClip by hand.
  - Preview a scene `Animator`.
  - Scrub an `AnimationClip` by seconds or exact authoring frames.
  - Use the clickable timeline to jump through a clip and inspect marker windows.
  - Read timeline lanes for Combat, Hurtbox, Defense, Movement, Combo/Links, and Presentation events.
  - Create `CombatAnimationDefinition` assets.
  - Set action id, authoring FPS, network sync requirement, loop preview, and root motion scale.
  - Read the action summary for total frames, hitbox window, combo window, invulnerability frames, and armor frames.
  - Analyze root motion distance and peak speed from the current preview model and clip.
  - Mark hitboxes, hurtboxes, invulnerability, super armor, movement locks, combo branches, projectile spawns, VFX/SFX, footsteps, and network sync points.
  - Add marker templates, duplicate markers, jump to marker time, and sort markers.
  - Append action recipes for light attack, heavy attack, dodge, projectile, and hit reaction.
  - Author `Action Links` that connect combo windows to target action ids or target definitions.
  - Select a marker and adjust its volume directly in Scene view with position/scale handles.
  - Validate multiplayer/action-game data risks before export.
  - Export marker data and action links to JSON with seconds, frame indices, and normalized link windows for runtime or server validation.

- `Tools/Act Toolkit/Combat Animation Batch Validator`
  - Scans every `CombatAnimationDefinition` asset in the project.
  - Reports per-action errors and warnings.
  - Checks duplicate action ids across the whole project.
  - Selects or pings problem assets for cleanup.

- `Tools/Act Toolkit/Create Combat Dummy MVP Scene`
  - Creates `Assets/ActToolkit/Generated/Scenes/CombatDummyMvp.unity`.
  - Creates `Assets/ActToolkit/Generated/CombatMvp/MVP_CombatActionDatabase.asset`.
  - Creates starter `CA_Light_1`, `CA_Light_2`, and `CA_Light_3` definitions under `Assets/ActToolkit/Generated/CombatMvp/Actions` with hitbox, movement-lock, network-sync, and combo markers.
  - Places a player, arena floor, camera, directional light, and a training dummy.
  - The dummy logs hits, flashes on impact, shows world-space HP text, and spawns floating damage numbers.
  - Edit the generated definitions in the Combat Animation Editor, then enter Play Mode in the MVP scene to observe timing and hitbox changes.

- `Tools/Act Toolkit/Level Blockout Editor`
  - Place floor, wall, ramp, platform, cover, spawn point, objective, trigger, kill-zone, and nav marker objects.
  - Use grid snapping and scene-click placement.
  - Generate a small test arena.
  - Export blockout data to JSON with stable element IDs and server-authoritative collision hints.

- `Tools/Act Toolkit/Testing Asset Sources`
  - Opens curated prototype asset sources.
  - Creates a folder layout for downloaded test characters, animations, environments, and license files.

## Data boundaries

- Prototype character models live under `Assets/External/TestAssets/Characters`.
- Prototype animation clips live under `Assets/External/TestAssets/Animations/PreviewClips`.
- Visual animation clips stay in Unity.
- Gameplay timing and action links live in `CombatAnimationDefinition` assets and exported JSON.
- Whitebox geometry stays in the scene.
- Server-readable blockout data is exported from `BlockoutElement` components.

This keeps the first prototype useful for both client feel and multiplayer validation.

## MVP controls

- The first combat slice is gamepad-first and targets a PS5 controller through Unity's Input System `Gamepad` abstraction.
- `Left Stick`: move.
- `Square`: light attack / combo input.
- `Triangle`: heavy attack placeholder.
- `Circle`: dodge placeholder.
- `Cross`: jump placeholder.
- `L1`: guard placeholder.
- `R3`: lock-on placeholder.
- Keyboard fallback is available for quick editor tests: `WASD`, `J`, `K`, `Space`, `Enter`, `Left Shift`, and `Tab`.
- Runtime scripts should read `PlayerCombatGamepadInput` instead of talking directly to the device.

## Planning

- `Docs/CombatAnimationEditorRoadmap.md` defines the target level for the combat animation editor and the next implementation slices.
- `Docs/LevelDesignScaleGuide.md` defines the first whitebox scale rules for player footprint, passages, cover, and combat pockets.
