# Pixel-art review — 30 October 2026

## Scope

Inspected all 10 runtime PNG assets at native size and enlarged with nearest-neighbor scaling: eight carry frames, two burned frames, bucket, four bomb frames, 28 note frames, eight explosion frames, sign, two button states, four audio-button states, and layered rooftop. Inspected the eight diagnostic game renders as a contact sheet.

## Corrections

- Removed the fixed bottom stroke from the bomb animation. The ellipse now controls the bottom edge in each squash/stretch pose.
- Rebuilt smoke-puff corners relative to puff radius. The old fixed corner offsets crossed at small radii, producing malformed late-animation puffs.
- Expanded editable font-atlas cells from 6 to 8 pixels to accommodate the widest current letters without overlap.
- Added final sign/button contour passes as an authoring safeguard. Pixel comparison confirmed these do not change the current UI appearance.

## Verification

- All runtime PNGs use only fully opaque or fully transparent pixels.
- Character, bucket, bomb, note, and UI frames have a single connected opaque component each (eight-neighbor connectivity). Explosion embers and smoke intentionally contain separate components.
- Full Aseprite regeneration preserved rooftop, character, bucket, notes, and audio-button pixels exactly.
- Release build succeeded with zero warnings and errors; all deployed PNGs match their authored files.
- Reviewed ready, gameplay, right walk, left walk, pause, explosion, burned, and retry renders in `review/audit/game-states.png`.

This is a visual asset and deterministic render review, not a claim that every artistic choice is objectively perfect or a manual gameplay test.
