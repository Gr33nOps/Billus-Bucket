# Artwork

The active art is hand-authored in `tools/draw_assets.lua` with Aseprite pixel primitives. No generation service is required. Edit the Lua for reproducible changes, or edit the `.aseprite` source directly and export it. **Running the drawing script overwrites its generated sources and PNGs**, so preserve direct Aseprite edits before rebuilding.

## Runtime assets

| PNG | Frame size | Layout / purpose |
| --- | --- | --- |
| rooftop | 320 × 180 | Layered dusk rooftop background |
| billu-carry | 32 × 48 | 8 frames; front pose and mirrored side walk with bucket |
| billu-burned | 24 × 32 | 2 crying/blinking frames |
| bucket | 28 × 16 | Separate bucket used when dropped after explosion |
| rupee-notes | 12 × 8 | 7 denominations × 4 flutter frames, horizontal |
| bomb-sheet | 16 × 16 | 4 upright fuse/squash frames |
| explosion-sheet | 48 × 48 | 8 flash/flame/smoke frames |
| ui-sign | 144 × 78 | Start, pause and game-over sign |
| ui-button | 68 × 18 | Normal and hover frames |
| ui-audio | 20 × 16 | Music on/off, effects on/off |

Editable files are in `BilluKiBalti/Assets/Sources`. `billu-body`, `cash-sheet`, and `ui-font` are construction/reference sources; they do not ship unused runtime PNGs. The font itself is drawn by `Presentation/PixelFont.cs`.

## Rebuild in Aseprite

Run from the repository root, replacing the executable path with your Aseprite installation:

```powershell
$assetRoot = (Resolve-Path BilluKiBalti/Assets).Path.Replace('\', '/')
& 'D:\aseprite\build\bin\aseprite.exe' -b --script-param "root=$assetRoot" --script tools/draw_assets.lua
```

Wait for Aseprite to finish before building the game. The game copies runtime assets to its output during the build. Generate review screenshots with `--art-review art/review` as documented in the main README.

## Style rules

- One native pixel for foreground contours, using plum ink `#232438`.
- Softer distant outlines `#484158` preserve depth.
- Muted purple dusk, unlit distant windows, warm masonry, teal clothes and blue bucket.
- Cash colors vary by denomination, while keeping the same outline and highlight treatment.
- Nearest-neighbor rendering, integer placement, no smoothing.
- Clouds stay clear of the score HUD and right-hand audio controls.

Only current artwork and authoring scripts are kept in this project. Historical experiments were backed up outside the workspace.
