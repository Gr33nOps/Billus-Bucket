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

## Day/night scenery and persistent bucket contents (2026-11-02)

- Settled cash uses the broad paper face and sits higher in the mouth, keeping colour visible after the catch animation. The bounded five-note pile still retains its contents until a bomb or new run; existing placements do not shuffle when a new paper arrives.
- Simplified scattered skin, shirt, trouser and bucket highlights into broader colour groups. Preserved scarf/sleeve/shoe outlines, facial animation and directional poses; regenerated matching editable Aseprite sources and favicon.
- Split scenery and clouds into authored transparent assets. A 180-second day/night cycle blends the sky and town palette, with dusk colour, a small crescent and sparse stars at night. The left lamp fades/flickers on at dusk and off at dawn. Three clouds drift right at different gentle speeds behind the town; three small flapping birds cross the upper daylight sky.
- Environment motion follows the existing ambient clock and freezes with pause. Render coordinates remain integer native pixels, with point sampling and no collision changes.
- Verification: Release solution build with warnings as errors; gameplay/platform/leaderboard checks and 100 seeded difficulty runs; PNG/outline/source checks; deterministic day/dusk/night/dawn and single-note captures; local Chromium desktop lifecycle and three mobile landscape viewport checks.

## Daylight-only follow-up (2026-11-02)

- Removed the day/night cycle, moon, stars, birds and lamp switching; the daylight palette remains constant.
- Render the sky/cloud layer directly at display resolution behind the transparent native game canvas. Slow fractional cloud positions now move in screen-pixel increments instead of jumping a whole scaled game pixel; point sampling preserves the authored shapes. Scissor clipping keeps clouds within the play viewport. Diagnostic native captures still include the sky.
- Removed the cash spawn director's persistent directional lane. Each note now chooses an independent spawn position; existing independent per-note flutter/momentum and soft boundary handling are preserved.
- Verified the spawn regression fails before the change, then passes; Release build has zero warnings/errors, gameplay/difficulty tests pass. Browser screenshots show 1px cloud movement at 4x game scale. Pause freezes the scene; three mobile landscape sizes still fit; no browser errors.

## Short run-based night (2026-11-02)

Reintroduced night as one cool dark veil over the complete world (including sky, clouds, cash, bombs, character and effects), drawn before HUD/dialogs. No moon, stars or birds. The lamp ignites with a short dusk flicker and stays warm at night. Each 60-second run cycle has 30 seconds daylight, 12 seconds smooth dusk, 9 seconds full night and 9 seconds smooth dawn. Uses elapsed run time, already reset on retry and frozen during pause; menus do not consume the opening daylight. Maximum veil opacity is 55%, retaining bomb silhouette/fuse visibility.

Verified cycle timing/continuity tests, zero-warning Release build, full gameplay/difficulty suite, daytime/dusk/night/dawn captures, browser asset/pause checks and mobile landscape sizing. Smooth display-resolution clouds remain intact.

## Fuller night palette and duration (2026-11-02)

Restored blue-purple night sky, muted cool clouds and cool town shading, with a lighter 22% world veil to unify the character, notes and effects. No celestial objects or birds. Extended the run cycle to 125 seconds: 45 daylight, 20 dusk, 40 full night, 20 dawn. Lamp ignition follows the new dusk timing. Run resets and pause behavior remain tied to elapsed gameplay time. Verified revised timing regression fails before the change, passes after; zero-warning solution build, gameplay/difficulty tests and rendered night/dusk/dawn previews passed.

## Neighbourhood art, lamp and pickup polish (2026-11-02)

Replaced rectangular lamp halos with a soft elliptical pixel falloff and rounded bulb fill that preserves the housing outline. HUD now uses the shared text shadow rather than the heavy eight-neighbour border. Added a third atmospheric sky tone for day/night. Reauthored background architecture with flat parapets, offset rooftop stair rooms, water tanks, antennas and shaded balconies, preserving the existing scenery palette and cable continuity. Pickup sound emphasizes paper rustle with a faint high note, removing the heavy metal hit and reducing peak level. Unrelated sound assets were preserved.

Verified day/night captures, binary alpha and editable asset sources, character and bucket outline checks, zero-warning build, gameplay and difficulty tests.

## Rooftop contour and pickup comfort follow-up (2026-11-02)

Closed rooftop stair-room bottom borders and rebuilt water tanks with inset fills, continuous rectangular contours and two visible supports. Catch audio is now purely softened low-pass paper noise, with no high pitched tone or metal hit; peak reduced to .28. HUD retains the game's shared font/drop shadow with small dark translucent backing strips for daylight contrast. Verified day render, art source/alpha/outline checks, zero-warning build and gameplay/difficulty suite.

## Unboxed HUD and slower cycle (2026-11-02)

Removed HUD backing strips. Shared glyphs use dark plum/green/brown faces with a pale offset shadow in daylight, blending to the original bright colours and dark shadow at night. Cycle is now 240 seconds: 120 daylight, 30 dusk, 60 night, 30 dawn; lamp timing updated. Catch audio changed to a quiet 75ms noise-based paper tap with rounded attack and short decay, no pitched tone or long airy tail. Sound wording was ambiguous; asked for clarification and proceeded with the gentle tap interpretation while awaiting a reply.

Revised timing tests failed before the change and pass after; zero-warning Release build and gameplay/difficulty suite pass. Browser HUD/modal/movement audit has no page errors and daylight screenshot was visually checked. Desktop art capture stalled this time and was stopped; browser preview used instead.
