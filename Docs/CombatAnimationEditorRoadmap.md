# Combat animation editor roadmap

For this project, the animation editor should not try to replace Maya, Blender, or Unity's Animation window. Its job is gameplay authoring: turning visual clips into frame-accurate, server-readable action data.

## Target level

### Level 1: First-playable authoring

This is the minimum useful level for the first multiplayer action prototype.

- Scan a default model library and spawn preview characters without hand wiring.
- Scan a default animation library and select clips without hand dragging.
- Scrub by normalized time and exact gameplay frames.
- Show a timeline with current frame and marker windows.
- Author common marker types: hitbox, hurtbox, invulnerability, super armor, movement lock, combo branch, projectile spawn, VFX, SFX, footstep, network sync, custom.
- Add markers from presets instead of hand-filling every field.
- Draw marker volumes in Scene view.
- Validate common multiplayer risks: missing sync marker, invalid hitbox sizes, empty gameplay tags, markers past clip end.
- Export JSON with seconds and frame data.

### Level 2: Action-feel iteration

This is the level needed once we start tuning real attacks.

- Dedicated lanes for startup, active, recovery, cancel, movement, hitbox, hurtbox, and presentation events.
- Side-by-side ghosting or pose snapshots for readability.
- Root motion distance and velocity graph.
- Hitbox/hurtbox handles that can be moved directly in Scene view.
- Batch retarget checks for model + clip compatibility.
- Attack summary panel: startup frames, active frames, recovery frames, total frames, cancel windows, invulnerability frames.
- Preset action recipes such as light attack, heavy attack, dodge, roll, launcher, projectile, hit reaction, knockdown.

### Level 3: Production pipeline

This is the level needed before large content production.

- Batch validation over all action definitions.
- Diff-friendly export with stable ordering and deterministic ids.
- Runtime preview scene with local hit detection simulation.
- Network simulation hooks for rollback/authority tests.
- Authoring of combo graph links between action definitions.
- Per-character overrides for offsets, scale, timings, and VFX/SFX references.
- Import reports for third-party clips and licenses.

## Current implementation status

Implemented now:

- Default model library: `Assets/External/TestAssets/Characters`.
- Default animation library: `Assets/External/TestAssets/Animations/PreviewClips`.
- One-click preview model spawn.
- Animation clip dropdown and clip selection.
- Frame-rate authoring field.
- Frame slider and step buttons.
- Clickable timeline with marker strips.
- Dedicated timeline lanes for combat, hurtbox, defense, movement, combo/link, and presentation events.
- Marker templates for common action-game events.
- Action recipes for light attack, heavy attack, dodge, projectile, and hit reaction.
- Action summary panel for total frames, hitbox window, combo window, invulnerability frames, and super armor frames.
- Root motion distance and peak speed analysis graph.
- Scene-view marker volume drawing.
- Scene-view position and scale handles for the selected marker volume.
- Marker duplicate, jump, remove, sort.
- Validation panel.
- Action links for combo/action transitions.
- Batch validator for all `CombatAnimationDefinition` assets.
- Duplicate action id checks across the project.
- JSON export with action id, clip path, clip length, FPS, frame count, marker seconds, marker start/duration/end frames, and action links.

Recommended next implementation slice:

1. Runtime preview scene with local hit detection simulation.
2. Per-character overrides for offsets, scale, timings, and VFX/SFX references.
3. Combo graph visualization across action definitions.
4. Import reports for third-party clips and licenses.
