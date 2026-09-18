# Grass skyline correction and larger blades

Source baseline: `1721278`, with the existing uncommitted terrain fade held
constant. Engine 26.09.15. Fixed scenario and results belong to
[the validation ledger](../../ValidationResults.md).

## Visual reproduction

- `before.png`: initial player view, before changes.
- `sky-before.png`: ground-level reproduction. Blades are cropped at the exact
  terrain skyline. Camera (-775,3919,680), angles (-4,0,0), FOV75, 1440x900;
  player at (-825.440186,3919.98169,672.601379).
- `sky-fog-disabled.png`: attempted component Enabled mutation was rejected;
  fog was **not** disabled. Retained as an unsuccessful diagnostic, not evidence
  for or against the cause.
- `sky-post-disabled.png`: main camera post-processing toggle did not change
  the detached camera capture; no causal conclusion from that toggle.
- `sky-after-hot.png`: same camera after the current-depth fog fix and larger
  blades; complete silhouettes cross the sky. This is a hot preview, not the
  required cold-start qualification.

The original fog samples `DepthChainDownsample` through `Depth::GetNormalized`.
The first candidate recorded `CommandList.Attributes.GrabDepthTexture` before
fog. It corrected the image but failed the performance gates, including after
halving root density. Both runs and source patches are retained. A stationary
screen with original fog measured1.71ms versus1.93ms with the copy.

The final candidate leaves fog unchanged and writes grass depth in the existing
terrain depth callback. Generation runs once for the actual view; one shared
triangle model draws depth, and the existing indirect forward draw reuses the
roots. Both modes use the same vertex shader. There is no grass shadow pass.
`sky-depth-preview.png` verifies the same skyline pose with this implementation.

The blade compute shader sets height9..20 inches, half-width0.55..1 inch and
range32 m, retaining aggressive density thinning and the65,536-root cap. Final
density isone/72square units (~21.5/m²),30% at12m,8% at24m,zero32m.
The original20 m prototype's performance claims do not transfer automatically.

## Accepted result

`06722ee9576c47a8902d88d9c59195b6` passes the unchanged canonical route:
moving567.74FPS, stationary515.23FPS, stationary GPU1.6057ms. This is
+0.1345ms over the no-grass control and below the original grass's1.7415ms.
Frame tails, memory, allocations, collision readiness and capacity all pass;
zero exceptions,7312peak roots,zero overflow. `comparison.json` contains all
candidate decisions. Full result JSON is losslessly compressed with source-byte
hashes in `compressed-evidence.json`. Preflight evidence records the exact
source hashes, physical resolution, settings and world identity.

Final cold screenshots: `sky-final.png`, `close-final.png`,
`range-24m-final.png`, `range-32m-final.png`, and `player-final.png`.
Close blades retain their shapes in `close-final-still.png`; the two captures
are not pixel-identical (see `static-image-comparison.json`). No wind/time input
exists. Fresh edits, multiplayer load and other hardware were not requalified.
