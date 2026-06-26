# Level Design Scale Guide

This is the first whitebox scale target for the action prototype. Treat these values as starting rules, then tune them in playtest.

## Player Reference

- Player collision diameter: `0.7m`
- Player visual height: `1.8m`
- Tight passage: `2m`
- Comfortable passage: `3m`

Use `2m` only when the space is intentionally tense or when the player is meant to commit to a route. Use `3m` as the default clear lane around cover, objectives, and wall corners.

## Combat Space

- Simple duel pocket: `6m` diameter
- Small skirmish pocket: `10m` diameter
- Central arena for early tests: roughly `18m x 14m` to `24m x 18m`

If the player cannot circle, dodge, recover the camera, and read the enemy silhouette, the pocket is too tight.

## Cover And Obstacles

- Low cover height: `0.75m-1.1m`
- Full body cover height: `1.6m+`
- Prototype cover width: `1.5m-3m`
- Leave `3m` around repeated cover unless the route is an intentional choke.

Place cover to create route choices, not to fill empty floor. A good first pass is two or three cover pieces that form a flank route, a pause point, and one risky shortcut.

## Vertical Layout

- Step-like platforms should stay visually readable before adding decoration.
- Ramps should have clear approach space at the bottom and top.
- Avoid putting critical combat in a narrow ramp mouth until camera behavior has been tested.

## Whitebox Review Checklist

- Can the player see the first goal from spawn?
- Is the main route at least `3m` wide most of the time?
- Does every combat pocket fit at least a `6m` duel guide?
- Do cover pieces create choices instead of just narrowing the room?
- Can the camera rotate without immediately hitting a wall or tall obstacle?
- Are objectives in fighting pockets rather than dead-end corners?
