# Art consistency review — 2026-11-02

Reviewed every shipped PNG atlas at native and enlarged nearest-neighbour sizes: character, burned state, bucket and wreck, money, bombs, explosion/smoke, rooftop, favicon, loading logo, signs, buttons and audio icons. Also reviewed the runtime trophy icon, rankings, name keyboard, cloud sync and code-entry panels in Chromium.

## Changes

- Authored separate left/right walking frames with a single readable profile eye and nose. Rendering selects the directional frames instead of flipping the entire character and bucket.
- The scarf neck band, hanging tail and outline turn with the torso in both walking directions. Preserved full sleeves, full trousers and matching shoes.
- Restored restrained hair, shirt and trouser shade clusters in both normal and burned states. Kept the dark plum outline shared by foreground props.
- Added broad metal highlights/shadow to the bucket. It stays fixed across directions and the favicon uses the same exact bucket pixels.
- Made shared button corner steps symmetric, removing the chipped-looking right edge on main and leaderboard controls. Kept the clean faces without decorative dots.

The muted distant scenery, distinct cloud silhouettes, banknote colours, plant/pot details and warm charcoal wreck already fit the palette and were retained. Input fields deliberately use recessed shading, while buttons use raised bevels.

## Verification

- Release solution build with warnings treated as errors: passed.
- Gameplay/platform/leaderboard C# tests, including directional frame selection: passed.
- `python GameTests/art-assets.py` (Pillow required): binary alpha, editable sources, unique walk cycles, ground alignment, bucket stability and exact favicon pixels passed.
- Chromium screenshots inspected for ready, left/right movement, pause, ten ranking rows, name keyboard, sync and code entry; no page errors.
- Mobile landscape viewport checks at 844x390, 667x320 and 568x240, plus portrait prompt: passed.

Visual review covers these assets and states; artistic preference still benefits from feedback during play.

Follow-up: closed the two teal gaps at the scarf neck edge in both directions; added matching shirt hems, trouser folds and tank-like bucket reflections. Directional scarf and outline regression checks pass in every walk frame.

Face/readability follow-up: ranking numbers now remain visible on the highlighted player row. Removed the single side-pose tooth, closed the nose-to-sleeve contour, simplified clothing/hair highlights, and added independent brief blinking with matching eye frames for every gait. Release build, C# tests, atlas checks and Chromium UI review passed.

## Catching and character finish — 2026-11-02

Matched the character's material shading to the detailed bucket using broad hair, skin, shirt and trouser shade groups; the bucket artwork retains its detail. The character atlas now also includes a body-only row so the bucket can react independently. Blink, gait and scarf outline checks cover both render layers.

Caught notes animate down into the mouth, with a brief one-pixel dip/rebound in the bucket. Overlapping note tops in the caught denominations remain behind its front lip, showing a bounded stack of the latest five notes. The stack follows movement, freezes on pause and clears on a bomb/new run. Catch presentation does not move the hitbox or change scoring.

Reviewed ready, carrying, both walking directions, pause, catch impact, full bucket and burned screenshots from the desktop renderer. Release build with warnings as errors, C# catch persistence/reset tests, atlas checks and desktop/mobile browser checks passed.

Pile refinement: each paper now retains its own offset, fold and tilt when later notes arrive. Incoming notes fall near the catch point, flutter and briefly bounce into an uneven overlapping pile instead of sliding into evenly spaced slots. Reviewed full/impact renderer captures; gameplay checks and warnings-as-errors build passed.

Outline follow-up: closed exposed rim-highlight pixels in the authored bucket (and matching favicon/carry atlas). Added a solid alpha-silhouette border at native resolution around rotated/scaled pile notes and restored side rim caps above them. Empty, impact and full-bucket renderer captures reviewed; closed-contour regression, Release build, gameplay tests and browser launch passed.
